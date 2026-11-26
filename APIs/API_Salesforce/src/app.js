import express from 'express';
import { fileURLToPath } from 'node:url';
import routes from './routes/routes.js';
import { requirePartnerKey } from './middlewares/partnerKey.js';
import { errorHandler } from './middlewares/errorHandler.js';

const app = express();
app.use('/soporte', express.static(fileURLToPath(new URL('./panel/', import.meta.url))));
app.use('/api/socios/salesforce', requirePartnerKey(process.env.PARTNER_KEY),
  express.json({ limit: '64kb' }), routes);
app.use(errorHandler);
export default app;
