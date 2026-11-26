export function errorHandler(err, req, res, next) {
  if (res.headersSent) return next(err);
  const status = err instanceof RangeError || err.status === 400 ? 400 : err.status === 413 ? 413 : 500;
  if (status === 500) console.error(err.code || err.message);
  res.status(status).json({ error: status === 500 ? 'Error del servicio' : err.message });
}
