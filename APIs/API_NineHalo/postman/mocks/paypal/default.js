// Contract subset: OAuth + Orders v2 create/get/capture. This is NOT PayPal.
const http=require('http');
const reply=(res,status,body)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(body))};
async function bodyOf(req){if(req.body&&typeof req.body==='object')return req.body;let raw='';for await(const b of req)raw+=b;try{return JSON.parse(raw)}catch{return {}}}
http.createServer(async(req,res)=>{
 try{
 const body=await bodyOf(req),url=req.url.split('?')[0];
 let state=await pm.state.get('paypal.state')||{orders:{},requests:{},next:1,captures:0,mode:'success'};
 const save=()=>pm.state.set('paypal.state',state);
 if(url==='/__scenario'&&req.method==='POST'){state.mode=body.mode||'success';await save();return reply(res,200,{ok:true})}
 if(url==='/__state')return reply(res,200,{captures:state.captures,orders:Object.keys(state.orders).length});
 if(url==='/v1/oauth2/token')return reply(res,200,{access_token:'sandbox-test-token',token_type:'Bearer',expires_in:3600});
 const requestId=req.headers['paypal-request-id'];
 if(url==='/v2/checkout/orders'&&req.method==='POST'){
  if(!requestId||body.intent!=='CAPTURE'||body.purchase_units?.length!==1)return reply(res,400,{name:'INVALID_REQUEST'});
  if(state.requests[requestId])return reply(res,200,state.orders[state.requests[requestId]]);
  const id='TESTORDER'+state.next++;
  const order={id,intent:'CAPTURE',status:'APPROVED',purchase_units:body.purchase_units};
  state.orders[id]=order;state.requests[requestId]=id;await save();return reply(res,201,order);
 }
 const match=url.match(/^\/v2\/checkout\/orders\/([A-Z0-9]+)(\/capture)?$/);
 if(match){
  const order=state.orders[match[1]];if(!order)return reply(res,404,{name:'RESOURCE_NOT_FOUND'});
  if(req.method==='GET'&&!match[2]){
   const copy=JSON.parse(JSON.stringify(order));
   if(state.mode==='wrong-amount')copy.purchase_units[0].amount.value='0.01';
   if(state.mode==='wrong-currency')copy.purchase_units[0].amount.currency_code='EUR';
   if(state.mode==='foreign-order')copy.purchase_units[0].custom_id='another-order';
   return reply(res,200,copy);
  }
  if(req.method==='POST'&&match[2]){
   if(!requestId)return reply(res,400,{name:'INVALID_REQUEST'});
   if(order.status==='COMPLETED')return reply(res,422,{name:'UNPROCESSABLE_ENTITY',details:[{issue:'ORDER_ALREADY_CAPTURED'}]});
   state.captures++;
   const capture={id:'CAPTURE'+state.next++,status:state.mode==='pending'?'PENDING':state.mode==='declined'?'DECLINED':'COMPLETED',amount:order.purchase_units[0].amount,final_capture:true};
   order.status='COMPLETED';order.purchase_units[0].payments={captures:[capture]};
   await save();
   if(state.mode==='lost-response')return reply(res,500,{name:'INTERNAL_SERVER_ERROR'});
   return reply(res,201,order);
  }
 }
 return reply(res,501,{name:'NOT_IMPLEMENTED_IN_SIMULATION'});
 }catch(error){return reply(res,500,{name:'MOCK_HANDLER_ERROR',message:error.message})}
}).listen(process.env.PORT||19401);
