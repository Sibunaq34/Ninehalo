function field(value, size = 180) {
  if (typeof value !== 'string' || !value.trim() || value.length > size)
    throw new RangeError('Campo de texto inválido');
  return value.trim();
}

export function createNotice(body) {
  if (!body || Array.isArray(body) || typeof body !== 'object')
    throw new RangeError('JSON inválido');
  return [field(body.referencia), field(body.destinatario), field(body.mensaje, 2000)];
}
