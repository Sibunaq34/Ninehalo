import { validarCedula } from '../validators/cedula.validator.js';
import { obtenerPersonaPorCedula } from '../services/personas.service.js';

export async function consultarPersona(req, res) {
  // Validar antes de solicitar datos a MySQL.
  const cedula = validarCedula(req.params.cedula);
  const data = await obtenerPersonaPorCedula(cedula);
  res.status(200).json({ ok: true, mensaje: 'Persona encontrada', data });
}
