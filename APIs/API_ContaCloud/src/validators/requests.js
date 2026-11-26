function field(value, size = 180) {
  if (typeof value !== 'string' || !value.trim() || value.length > size)
    throw new RangeError('Campo de texto inválido');
  return value.trim();
}

export function createSale(body) {
  if (!body || Array.isArray(body) || typeof body !== 'object')
    throw new RangeError('JSON inválido');
  const factura = field(body.factura);
  const fecha = field(body.fecha, 40);
  const { total_usd, total_crc } = body;
  if (![total_usd, total_crc].every(n => Number.isFinite(n) && n >= 0) || !/^\d{4}-\d{2}-\d{2}/.test(fecha))
    throw new RangeError('Importes o fecha inválidos');
  return [factura, fecha, total_usd, total_crc];
}
