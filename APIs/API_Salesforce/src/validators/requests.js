function field(value, size = 180) {
  if (typeof value !== 'string' || !value.trim() || value.length > size)
    throw new RangeError('Campo de texto inválido');
  return value.trim();
}

export function createCase(body) {
  if (!body || Array.isArray(body) || typeof body !== 'object')
    throw new RangeError('JSON inválido');
  return [field(body.id_conversacion, 40), field(body.id_compra, 40), field(body.asunto), field(body.detalle, 10000)];
}

export function sendReply(body) {
  if (!body || Array.isArray(body) || typeof body !== 'object')
    throw new RangeError('JSON inválido');
  const id = field(body.id_conversacion, 40);
  const estado = body.estado || 'EN_PROCESO';
  if (!/^\d+$/.test(id) || !['EN_PROCESO', 'RESUELTA'].includes(estado))
    throw new RangeError('Caso o estado inválido');
  return { id_conversacion: id, contenido: field(body.contenido, 10000), estado, referencia: field(body.referencia, 80) };
}
