import * as service from '../services/service.js';
import * as validate from '../validators/requests.js';

export async function createSale(req, res) {
  res.json({ data: await service.createSale(validate.createSale(req.body)) });
}

export async function listSales(req, res) {
  res.json({ data: await service.listSales() });
}

export async function summarizeSales(req, res) {
  res.json({ data: await service.summarizeSales() });
}
