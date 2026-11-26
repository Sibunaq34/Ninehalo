import { Router } from 'express';
import { createSale, listSales, summarizeSales } from '../controllers/controller.js';

const router = Router();
router.post('/ventas', createSale);
router.get('/ventas', listSales);
router.get('/resumen', summarizeSales);
export default router;
