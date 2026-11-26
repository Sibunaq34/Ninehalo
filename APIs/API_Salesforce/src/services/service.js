import { call } from '../config/db.js';

export async function createCase(params) {
  const [row] = await call('CALL sp_salesforce_registrar_caso(?,?,?,?)', params);
  return row;
}

export async function listCases() {
  return call('CALL sp_salesforce_listar_casos()');
}

export async function sendReply(params) {
  const { id_conversacion } = params;
  const [existing] = await call('CALL sp_salesforce_consultar_caso(?)', [id_conversacion]);
  if (!existing) throw new RangeError('Caso inexistente');

  const baseUrl = process.env.NINEHALO_URL || 'http://localhost:5206';
  const response = await fetch(new URL('/api/conversaciones/socio/respuesta', baseUrl), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Partner-Key': process.env.PARTNER_KEY },
    body: JSON.stringify(params),
    redirect: 'error',
    signal: AbortSignal.timeout(5000)
  });
  if (!response.ok) throw new Error('NineHalo rechazó la respuesta: ' + response.status);
  const { success, data } = await response.json();
  const confirmedState = data?.estado;
  if (success !== true || !['EN_PROCESO', 'RESUELTA'].includes(confirmedState)) {
    throw new Error('NineHalo no confirmó el estado del caso');
  }
  await call('CALL sp_salesforce_actualizar_caso(?,?)', [id_conversacion, confirmedState]);
  return { id_conversacion, estado: confirmedState };
}
