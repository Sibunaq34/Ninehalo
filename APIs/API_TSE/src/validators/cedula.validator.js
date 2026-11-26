import { AppError } from '../middlewares/errorHandler.js';

export function validarCedula(valor) {
  if (typeof valor !== 'string' || !valor.trim()) {
    throw new AppError(400, 'La cédula es obligatoria.');
  }
  // Solo se permiten dígitos, guiones y espacios; no descartar otros caracteres.
  if (!/^[0-9 -]+$/.test(valor)) {
    throw new AppError(400, 'La cédula solo puede contener dígitos, guiones o espacios.');
  }
  const cedula = valor.replace(/[ -]/g, '');
  if (!/^\d{9}$/.test(cedula)) {
    throw new AppError(400, 'La cédula debe contener exactamente 9 dígitos.');
  }
  return cedula;
}
