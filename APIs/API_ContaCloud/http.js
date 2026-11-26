import express from 'express';
import mysql from 'mysql2/promise';
import { timingSafeEqual } from 'node:crypto';

export function start(port, routes) {
  const key = process.env.PARTNER_KEY;
  if (!key || key.length < 24) throw new Error('Configurá PARTNER_KEY con al menos 24 caracteres');
  const pool = mysql.createPool({
    host: process.env.DB_HOST || '127.0.0.1',
    port: Number(process.env.DB_PORT || 3306),
    user: process.env.DB_USER,
    password: process.env.DB_PASSWORD,
    database: process.env.DB_NAME,
    connectionLimit: 5
  });
  const db = async (sql, ...params) => (await pool.execute(sql, params))[0];
  const app = express();
  app.use((req, res, next) => {
    const token = Buffer.from(req.get('X-Partner-Key') || '');
    const expected = Buffer.from(key);
    if (token.length !== expected.length || !timingSafeEqual(token, expected))
      return res.status(401).json({ error: 'Credencial inválida' });
    next();
  });
  app.use(express.json({ limit: '64kb' }));
  for (const [route, handler] of Object.entries(routes)) {
    const [method, path] = route.split(' ');
    app[method.toLowerCase()](path, async (req, res, next) => {
      try {
        const body = req.body ?? {};
        if (Array.isArray(body) || typeof body !== 'object') throw new RangeError('JSON inválido');
        res.json({ data: await handler(db, body) });
      } catch (err) { next(err); }
    });
  }
  app.use((err, req, res, next) => {
    const status = err instanceof RangeError ? 400 : err.status === 413 ? 413 : err.status === 400 ? 400 : 500;
    if (status === 500) console.error(err.code || err.message);
    res.status(status).json({ error: status === 500 ? 'Error del servicio' : err.message });
  });
  app.listen(Number(process.env.PORT || port), '127.0.0.1');
}

export function field(value, size = 180) {
  if (typeof value !== 'string' || !value.trim() || value.length > size)
    throw new RangeError('Campo de texto inválido');
  return value.trim();
}
