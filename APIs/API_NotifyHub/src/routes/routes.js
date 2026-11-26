import { Router } from 'express';
import { createNotice, listNotices } from '../controllers/controller.js';

const router = Router();
router.post('/avisos', createNotice);
router.get('/avisos', listNotices);
export default router;
