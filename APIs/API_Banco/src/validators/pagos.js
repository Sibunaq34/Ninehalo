import { AppError } from '../middlewares/errorHandler.js';

const invalid = (mensaje) => { throw new AppError(400, mensaje); };

export function validarMonto(monto) {
  if (typeof monto !== 'number' || !Number.isFinite(monto) || monto <= 0 || monto > 9999999999999.99) {
    invalid('El monto debe ser un número mayor a 0 y dentro del límite permitido.');
  }
  const centavos = Math.round(monto * 100);
  if (Math.abs(monto * 100 - centavos) > 0.001) invalid('El monto admite como máximo dos decimales.');
  return (centavos / 100).toFixed(2);
}

export function validarVencimiento(fecha, ahora = new Date()) {
  if (typeof fecha !== 'string' || !/^(0[1-9]|1[0-2])\/\d{2}$/.test(fecha)) invalid('La fecha de vencimiento debe tener formato MM/YY.');
  const [mes, anio] = fecha.split('/').map(Number);
  if (2000 + anio < ahora.getFullYear() || (2000 + anio === ahora.getFullYear() && mes < ahora.getMonth() + 1)) invalid('La tarjeta está vencida.');
  return fecha;
}

export function validarTarjeta(body) {
  const { numeroTarjeta, cvv, fechaVencimiento, monto } = body ?? {};
  if (typeof numeroTarjeta !== 'string' || !/^(?:\d{16}|3[47]\d{13})$/.test(numeroTarjeta)) invalid('El número de tarjeta debe contener 16 dígitos o 15 para American Express.');
  let suma = 0;
  for (let i = 0; i < numeroTarjeta.length; i++) {
    let digito = Number(numeroTarjeta[i]);
    if (i % 2 === numeroTarjeta.length % 2) { digito *= 2; if (digito > 9) digito -= 9; }
    suma += digito;
  }
  if (suma % 10 !== 0 || /^0+$/.test(numeroTarjeta)) invalid('El número de tarjeta no pasa la validación de Luhn.');
  if (typeof cvv !== 'string' || !(/^3[47]/.test(numeroTarjeta) ? /^\d{4}$/ : /^\d{3}$/).test(cvv)) invalid('El CVV debe contener 3 dígitos o 4 para American Express.');
  return { numeroTarjeta, cvv, fechaVencimiento: validarVencimiento(fechaVencimiento), monto: validarMonto(monto) };
}

export function validarSinpe(body) {
  const { telefono, monto } = body ?? {};
  if (typeof telefono !== 'string' || !/^(?:(?:\+506|506))?[2678]\d{7}$/.test(telefono)) invalid('El teléfono debe ser costarricense: 8 dígitos iniciando en 2, 6, 7 u 8, con prefijo opcional +506 o 506.');
  return { telefono: telefono.slice(-8), monto: validarMonto(monto) };
}
