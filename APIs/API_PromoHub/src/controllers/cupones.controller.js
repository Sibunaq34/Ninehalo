import { consultarCupon, diagnosticarCupon } from '../services/cupones.service.js';
import { validarSolicitud } from '../validators/cupones.validator.js';

export async function validarCupon(req, res) {
  const { codigo, licencias } = validarSolicitud(req.body);
  if (licencias.includes(1)) return res.status(422).json({error:'El cupón no se puede aplicar si el carrito contiene Básica. Puedes continuar sin cupón.',motivo:'BASICA'});
  const coupon = await consultarCupon(codigo, licencias);
  if (!coupon) return res.status(422).json(await diagnosticarCupon(codigo,licencias));
  res.json({ data: coupon });
}
