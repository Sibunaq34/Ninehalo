import { Router } from 'express';
import { createCase, listCases, sendReply } from '../controllers/controller.js';

const router = Router();
router.post('/casos', createCase);
router.get('/casos', listCases);
router.post('/respuestas', sendReply);
export default router;
