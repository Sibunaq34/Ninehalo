import {ejecutarIntento} from '../services/intentos.service.js';
import { prepararConstancia } from '../services/constancia.service.js';
import { pagosService } from '../services/pagos.service.js';
import { validarTarjeta, validarSinpe } from '../validators/pagos.js';

export const pagarTarjeta = async (req, res) => {
  return ejecutarIntento(req,res,'tarjeta',async()=>{
  const datos = validarTarjeta(req.body);
  const firmar = prepararConstancia(req.body, 'tarjeta');
  const data = await pagosService.pagarTarjeta(datos);
  if (firmar) data.constancia = firmar(data.transaccionId);
  return data;
  });
};

export const pagarSinpe = async (req, res) => {
  return ejecutarIntento(req,res,'sinpe',async()=>{
  const datos = validarSinpe(req.body);
  const firmar = prepararConstancia(req.body, 'sinpe');
  const data = await pagosService.pagarSinpe(datos);
  if (firmar) data.constancia = firmar(data.transaccionId);
  return data;
  });
};
