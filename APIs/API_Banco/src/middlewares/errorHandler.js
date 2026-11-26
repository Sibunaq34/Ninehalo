export class AppError extends Error {
  constructor(status, mensaje, codigo = "BANCO_RECHAZADO") { super(mensaje); this.status = status; this.codigo = codigo; }
}

export function errorHandler(error, req, res, next) {
  if (error instanceof AppError) return res.status(error.status).json({ ok: false, mensaje: error.message, codigo:error.codigo, definitivo:[400,404,422].includes(error.status) });
  if (error.type === 'entity.parse.failed') return res.status(400).json({ ok: false, mensaje: 'El cuerpo debe ser JSON válido.' });
  if (error.status === 413) return res.status(413).json({ ok: false, mensaje: 'El cuerpo de la solicitud es demasiado grande.' });
  // No registrar datos bancarios ni credenciales.
  console.error('Error interno de pagos:', error.code ?? error.name);
  return res.status(500).json({ ok: false, mensaje: 'Ocurrió un error interno al procesar el pago.' });
}
