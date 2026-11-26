import { timingSafeEqual } from 'node:crypto';

export function requirePartnerKey(key) {
  if (!key || key.length < 24) throw new Error('Configurá PARTNER_KEY con al menos 24 caracteres');
  const expected = Buffer.from(key);
  return (req, res, next) => {
    const token = Buffer.from(req.get('X-Partner-Key') || '');
    if (token.length !== expected.length || !timingSafeEqual(token, expected))
      return res.status(401).json({ error: 'Credencial inválida' });
    next();
  };
}
