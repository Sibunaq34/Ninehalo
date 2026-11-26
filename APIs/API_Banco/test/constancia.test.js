import test from 'node:test'
import assert from 'node:assert/strict'
import { createHmac } from 'node:crypto'
import { prepararConstancia } from '../src/services/constancia.service.js'
const key='payment-verification-key-for-local-tests-only'
process.env.PAYMENT_SIGNING_KEY=key
const pedido={clave_operacion:'1a111111-1111-4111-8111-111111111111',id_usuario:7}
test('constancia vincula pago, pedido, usuario, método e importe',()=>{
 for(const metodo of ['tarjeta','sinpe']){
  const proof=prepararConstancia({pedido,monto:12.34},metodo)(42)
  const [payload,signature]=proof.split('.')
  assert.equal(signature,createHmac('sha256',key).update(payload).digest('base64url'))
  assert.deepEqual(JSON.parse(Buffer.from(payload,'base64url')),{pedido:pedido.clave_operacion,usuario:7,centavos:'1234',transaccion:'42',metodo})
 }
})
test('rechaza contexto incompleto antes de cobrar y conserva pagos independientes',()=>{
 assert.equal(prepararConstancia({monto:1},'sinpe'),null)
 for(const p of [null,{}, {...pedido,id_usuario:0},{...pedido,clave_operacion:'incorrecto'}])assert.throws(()=>prepararConstancia({pedido:p,monto:1},'sinpe'))
})
