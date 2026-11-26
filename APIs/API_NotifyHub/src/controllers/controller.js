import * as service from '../services/service.js';
import * as validate from '../validators/requests.js';

export async function createNotice(req, res) {
  res.json({ data: await service.createNotice(validate.createNotice(req.body)) });
}

export async function listNotices(req, res) {
  res.json({ data: await service.listNotices() });
}
