import { call } from '../config/db.js';

export async function createNotice(params) {
  const [row] = await call('CALL sp_notifyhub_registrar_aviso(?,?,?)', params);
  return row;
}

export async function listNotices() {
  return call('CALL sp_notifyhub_listar_avisos()');
}
