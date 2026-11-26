import express from 'express';
import cors from 'cors';
import personasRoutes from './routes/personas.routes.js';
import { errorHandler } from './middlewares/errorHandler.js';

const app = express();
app.use(cors());
app.use('/api/personas', personasRoutes);
app.use((req, res) => res.status(404).json({ ok: false, mensaje: 'La ruta solicitada no existe.' }));
app.use(errorHandler);
export default app;
