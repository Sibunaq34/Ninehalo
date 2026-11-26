export class AppError extends Error {
  constructor(status, mensaje) {
    super(mensaje);
    this.status = status;
  }
}

export function errorHandler(error, req, res, next) {
  if (error instanceof AppError) return res.status(error.status).json({ ok: false, mensaje: error.message });
  if (error instanceof URIError) return res.status(400).json({ ok: false, mensaje: 'La URL contiene caracteres inválidos.' });
  // Registrar solo el código del error, sin datos personales ni credenciales.
  console.error('Error interno de consulta:', error.code || error.name);
  return res.status(500).json({ ok: false, mensaje: 'Ocurrió un error interno al consultar la persona.' });
}
