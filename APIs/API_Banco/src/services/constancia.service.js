import { createHmac } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { AppError } from '../middlewares/errorHandler.js'
export function prepararConstancia(body, metodo) {
 if(body?.pedido === undefined)return null // Compatibilidad con pagos independientes.
 const p=body.pedido
 if(!p || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(p.clave_operacion) || !Number.isSafeInteger(p.id_usuario)||p.id_usuario<=0)throw new AppError(400,'El pedido no es válido.')
 const key=process.env.PAYMENT_SIGNING_KEY || readFileSync(new URL('../../.payment-signing-key',import.meta.url),'utf8').trim()
 if(key.length<32)throw new Error('Falta configurar la firma bancaria.')
 return transaccionId=>{
  const payload=Buffer.from(JSON.stringify({pedido:p.clave_operacion.toLowerCase(),usuario:p.id_usuario,centavos:String(Math.round(Number(body.monto)*100)),transaccion:String(transaccionId),metodo})).toString('base64url')
  return payload+'.'+createHmac('sha256',key).update(payload).digest('base64url')
 }
}
