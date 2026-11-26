import { call } from '../config/db.js';

export async function createSale(params) {
  const [row] = await call('CALL sp_contacloud_registrar_venta(?,?,?,?)', params);
  return row;
}

export async function listSales() {
  return call('CALL sp_contacloud_listar_ventas()');
}

export async function summarizeSales() {
  return call('CALL sp_contacloud_resumen()');
}
