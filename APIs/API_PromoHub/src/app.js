import express from 'express';
import routes from './routes/cupones.routes.js';
import { requirePartnerKey } from './middlewares/partnerKey.js';
import { errorHandler } from './middlewares/errorHandler.js';

const app = express();
app.use('/api/socios/promohub', requirePartnerKey(process.env.PARTNER_KEY),
  express.json({ limit: '16kb' }), routes);
app.use(errorHandler);
export default app;
