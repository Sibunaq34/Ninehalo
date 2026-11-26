import { pool } from '../config/db.js';
import { AppError } from '../middlewares/errorHandler.js';

export async function obtenerPersonaPorCedula(cedula) {
  const [resultados] = await pool.execute('CALL sp_obtener_persona_por_cedula(?)', [cedula]);
  const persona = resultados[0][0];
  if (!persona) throw new AppError(404, 'No existe una persona registrada con esa cédula.');
  return {
    cedula,
    nombre: persona.nombre,
    primerApellido: persona.primer_apellido,
    segundoApellido: persona.segundo_apellido,
  };
}
