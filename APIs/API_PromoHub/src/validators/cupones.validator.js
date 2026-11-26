export function validarSolicitud(body) {
  const codigo = typeof body?.codigo === 'string' ? body.codigo.trim().toUpperCase() : '';
  const licencias = body?.licencias;
  if (!/^[A-Z0-9-]{1,32}$/.test(codigo) || !Array.isArray(licencias)
      || licencias.length < 1 || licencias.length > 10
      || !licencias.every(id => Number.isInteger(id) && id > 0 && id <= 2147483647)) {
    throw new RangeError('Indica un código y los identificadores de las licencias.');
  }
  return { codigo, licencias };
}
