import express from 'express';
import cors from 'cors';
import pagosRoutes from './routes/pagos.routes.js';
import { errorHandler } from './middlewares/errorHandler.js';

const app = express();
app.use(cors());
app.use(express.json({ limit: '16kb' }));
app.use('/api/pagos', pagosRoutes);
app.use((req, res) => res.status(404).json({ ok: false, mensaje: 'La ruta solicitada no existe.' }));
app.use(errorHandler);
export default app;
