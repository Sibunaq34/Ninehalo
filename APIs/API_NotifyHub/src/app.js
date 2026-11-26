import express from 'express';
import routes from './routes/routes.js';
import { requirePartnerKey } from './middlewares/partnerKey.js';
import { errorHandler } from './middlewares/errorHandler.js';

const app = express();
app.use(requirePartnerKey(process.env.PARTNER_KEY));
app.use(express.json({ limit: '64kb' }));
app.use('/api/socios/notifyhub', routes);
app.use(errorHandler);
export default app;
