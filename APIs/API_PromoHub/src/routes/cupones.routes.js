import { Router } from 'express';
import { validarCupon } from '../controllers/cupones.controller.js';

const router = Router();
router.post('/cupones/validar', validarCupon);
export default router;
