import {createHmac,timingSafeEqual} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {pool} from '../config/db.js';
import {AppError} from '../middlewares/errorHandler.js';
const secret=()=>process.env.PAYMENT_SIGNING_KEY||readFileSync(new URL('../../.payment-signing-key',import.meta.url),'utf8').trim();
export function validarIntento(req,action,method){
 try{
  const [payload,signature,...extra]=(req.get('X-NineHalo-Intent')||'').split('.');if(extra.length||!payload||!signature)throw Error();
  const expected=createHmac('sha256',secret()).update(payload).digest(),received=Buffer.from(signature,'base64url');if(expected.length!==received.length||!timingSafeEqual(expected,received))throw Error();
  const p=JSON.parse(Buffer.from(payload,'base64url'));const guid=/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
  if(p.accion!==action||p.exp<Date.now()/1000||!guid.test(p.intento)||!guid.test(p.pedido)||!Number.isSafeInteger(p.usuario)||p.usuario<=0||!['sinpe','tarjeta'].includes(p.metodo)||!/^\d+$/.test(p.centavos))throw Error();
  if(action==='pagar'&&(p.metodo!==method||req.body?.pedido?.clave_operacion?.toLowerCase()!==p.pedido.toLowerCase()||req.body?.pedido?.id_usuario!==p.usuario||String(Math.round(Number(req.body.monto)*100))!==p.centavos))throw Error();
  if(action==='consultar'&&req.params.intento!==p.intento)throw Error();return p;
 }catch{throw new AppError(401,'El pago del pedido debe iniciarse desde NineHalo.','INTENTO_NO_AUTORIZADO');}
}
export async function ejecutarIntento(req,res,method,action){
 if(!req.body?.pedido)return res.json({ok:true,mensaje:'Pago realizado correctamente.',data:await action()});
 const p=validarIntento(req,'pagar',method),db=await pool.getConnection(),lock='nhbank:'+p.intento;
 let acquired=false;
 try{
  const [locks]=await db.query('SELECT GET_LOCK(?,10) AS acquired',[lock]);acquired=Number(locks[0].acquired)===1;if(!acquired)throw new AppError(409,'El intento está en proceso.','PAGO_PENDIENTE');
  const [rows]=await db.execute('SELECT * FROM ninehalo_intento WHERE intento=?',[p.intento]);
  if(rows.length){const row=rows[0];if(row.pedido!==p.pedido||row.usuario!==p.usuario||row.metodo!==method||row.centavos!==p.centavos)throw new AppError(409,'El intento no coincide.','INTENTO_NO_COINCIDE');if(row.respuesta&&row.http_status)return res.status(row.http_status).json(typeof row.respuesta==='string'?JSON.parse(row.respuesta):row.respuesta);throw new AppError(409,'El resultado del pago está pendiente.','PAGO_PENDIENTE');}
  await db.execute("INSERT INTO ninehalo_intento(intento,pedido,usuario,metodo,centavos,estado) VALUES(?,?,?,?,?,'EN_CURSO')",[p.intento,p.pedido,p.usuario,method,p.centavos]);
  let status=200,result,state='CONFIRMADO';
  try{result={ok:true,mensaje:'Pago realizado correctamente.',data:await action()};}
  catch(error){if(!(error instanceof AppError)||![400,404,422].includes(error.status))throw error;status=error.status;state='RECHAZADO';result={ok:false,definitivo:true,codigo:error.codigo||'BANCO_RECHAZADO',mensaje:error.message};}
  await db.execute('UPDATE ninehalo_intento SET estado=?,http_status=?,respuesta=? WHERE intento=?',[state,status,JSON.stringify(result),p.intento]);return res.status(status).json(result);
 }finally{let reusable=true;try{if(acquired)await db.query('SELECT RELEASE_LOCK(?)',[lock]);}catch{reusable=false;db.destroy();}finally{if(reusable)db.release();}}
}
export async function consultarIntento(req,res){
 const p=validarIntento(req,'consultar');const [rows]=await pool.execute('SELECT estado,http_status,respuesta FROM ninehalo_intento WHERE intento=? AND pedido=? AND usuario=? AND metodo=? AND centavos=?',[p.intento,p.pedido,p.usuario,p.metodo,p.centavos]);
 if(!rows.length)return res.json({encontrado:false});const row=rows[0];res.json({encontrado:true,estado:row.estado,http_status:row.http_status,respuesta:typeof row.respuesta==='string'?JSON.parse(row.respuesta):row.respuesta});
}
