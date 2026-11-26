import { Router } from 'express';
import { consultarPersona } from '../controllers/personas.controller.js';

const router = Router();
router.get('/', consultarPersona);
router.get('/:cedula', consultarPersona);
export default router;
