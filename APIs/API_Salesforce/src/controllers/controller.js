import * as service from '../services/service.js';
import * as validate from '../validators/requests.js';

export async function createCase(req, res) {
  res.json({ data: await service.createCase(validate.createCase(req.body)) });
}

export async function listCases(req, res) {
  res.json({ data: await service.listCases() });
}

export async function sendReply(req, res) {
  res.json({ data: await service.sendReply(validate.sendReply(req.body)) });
}
