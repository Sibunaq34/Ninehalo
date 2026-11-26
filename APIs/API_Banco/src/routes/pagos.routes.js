import {consultarIntento} from '../services/intentos.service.js';
import { Router } from 'express';
import { pagarTarjeta, pagarSinpe } from '../controllers/pagos.controller.js';

const router = Router();
router.get('/intentos/:intento',consultarIntento);
router.post('/tarjeta', pagarTarjeta);
router.post('/sinpe', pagarSinpe);
export default router;
