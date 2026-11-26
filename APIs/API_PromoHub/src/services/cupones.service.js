import { call, query } from '../config/db.js';

export async function consultarCupon(codigo, licencias) {
  const [coupon] = await call('CALL sp_promohub_validar(?,?)', [codigo, JSON.stringify(licencias)]);
  return coupon;
}

export function describirRechazo(coupon) {
  if (!coupon || Number(coupon.activo)!==1) return {error:'El código de descuento no es válido.',motivo:'INVALIDO'};
  if (Number(coupon.vencido)===1) return {error:'El cupón está vencido.',motivo:'VENCIDO'};
  if (Number(coupon.usos)>=Number(coupon.limite_usos)) return {error:'El cupón agotó sus usos disponibles.',motivo:'AGOTADO'};
  return {error:'El cupón no aplica a las licencias del carrito.',motivo:'NO_APLICA'};
}
export async function diagnosticarCupon(codigo,licencias) {
  const rows=await query('SELECT c.activo,c.vence_utc<=UTC_TIMESTAMP() AS vencido,c.limite_usos,(SELECT COUNT(*) FROM cupon_pedido p WHERE p.codigo=c.codigo) AS usos FROM cupon c WHERE c.codigo=?',[codigo]);
  return describirRechazo(rows[0]);
}
