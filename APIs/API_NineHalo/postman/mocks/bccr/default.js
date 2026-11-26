// SDDE contract: https://gee.bccr.fi.cr/indicadoreseconomicos/Documentos/DocumentosMetodologiasNotasTecnicas/Estandar_API_SDDE.pdf
const http=require('http');
const reply=(res,status,body)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(body))};
async function read(req){if(req.body&&typeof req.body==='object')return req.body;let s='';for await(const p of req)s+=p;try{return JSON.parse(s)}catch{return {}}}
http.createServer(async(req,res)=>{
 try{
 const url=new URL(req.url,'http://localhost'),body=await read(req);let state=await pm.state.get('bccr.state')||{mode:'success',requests:0};
 if(url.pathname==='/__scenario'&&req.method==='POST'){state.mode=body.mode;await pm.state.set('bccr.state',state);return reply(res,200,{ok:true})}
 if(url.pathname==='/__state')return reply(res,200,state);
 if(url.pathname==='/__reset'){await pm.state.set('bccr.state',{mode:'success',requests:0});return reply(res,200,{ok:true})}
 if(url.pathname==='/indicadoresEconomicos/318/series'){
  state.requests++;await pm.state.set('bccr.state',state);
  if(req.headers.authorization!=='Bearer test-bccr-token'||state.mode==='unauthorized')return reply(res,401,{mensaje:'No autorizado'});
  if(!/^\d{4}\/\d{2}\/\d{2}$/.test(url.searchParams.get('fechaInicio'))||!/^\d{4}\/\d{2}\/\d{2}$/.test(url.searchParams.get('fechaFin'))||url.searchParams.get('idioma')!=='ES')return reply(res,400,{estado:false});
  if(state.mode==='outage')return reply(res,500,{estado:false});
  if(state.mode==='malformed'){res.writeHead(200,{'Content-Type':'application/json'});return res.end('<html>Unavailable</html>')}
  const day=new Date(Date.now()-6*3600000).toISOString().slice(0,10);
  return reply(res,200,{estado:true,mensaje:'Consulta exitosa',datos:[{codigoIndicador:'318',nombreIndicador:'Tipo de cambio de venta',series:[{fecha:day,valorDatoPorPeriodo:500}]}]});
 }
 reply(res,501,{estado:false,mensaje:'Endpoint no modelado'});
 }catch(error){reply(res,500,{estado:false,mensaje:error.message})}
}).listen(process.env.PORT||19402);
