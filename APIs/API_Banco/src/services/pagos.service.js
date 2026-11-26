import { pool } from '../config/db.js';
import { AppError } from '../middlewares/errorHandler.js';
import { validarVencimiento } from '../validators/pagos.js';

// Comparar dinero en centavos evita errores de redondeo de punto flotante.
const centavos = (valor) => {
  const [entero, decimal = ''] = String(valor).split('.');
  return BigInt(entero) * 100n + BigInt(decimal.padEnd(2, '0'));
};

export function crearServicioPagos(db = pool) {
  async function pagar(tipo, datos) {
    const conexion = await db.getConnection();
    try {
      await conexion.beginTransaction();
      const consulta = tipo === 'TARJETA' ? 'CALL sp_obtener_tarjeta(?)' : 'CALL sp_obtener_cuenta_por_telefono(?)';
      const [resultados] = await conexion.execute(consulta, [tipo === 'TARJETA' ? datos.numeroTarjeta : datos.telefono]);
      const registro = resultados[0][0];
      if (!registro) throw new AppError(404, tipo === 'TARJETA' ? 'La tarjeta no existe.' : 'El teléfono no está asociado a una cuenta.');

      // Bloqueo compartido por ambos medios de pago, hasta confirmar o revertir.
      const [cuentas] = await conexion.execute('SELECT saldo FROM cuentas WHERE id = ? FOR UPDATE', [registro.cuenta_id]);
      if (!cuentas.length) throw new AppError(404, 'La cuenta asociada no existe.');
      if (tipo === 'TARJETA') {
        if (registro.cvv !== datos.cvv) throw new AppError(400, 'El CVV no coincide.');
        validarVencimiento(registro.fecha_vencimiento);
        if (registro.fecha_vencimiento !== datos.fechaVencimiento) throw new AppError(400, 'La fecha de vencimiento no coincide.');
      }
      if (centavos(cuentas[0].saldo) < centavos(datos.monto)) throw new AppError(422, tipo === 'SINPE' ? 'La cuenta SINPE no tiene fondos suficientes' : 'La cuenta de la tarjeta no tiene fondos suficientes', 'FONDOS_INSUFICIENTES');
      const procedimiento = tipo === 'TARJETA' ? 'CALL sp_realizar_pago_tarjeta(?, ?, ?)' : 'CALL sp_realizar_pago_sinpe(?, ?, ?)';
      const detalle = tipo === 'TARJETA' ? `Compra con tarjeta terminada en ${datos.numeroTarjeta.slice(-4)}` : 'Pago por SINPE Móvil';
      const [pago] = await conexion.execute(procedimiento, [registro.cuenta_id, datos.monto, detalle]);
      const { transaccionId, nuevoSaldo } = pago[0][0];
      await conexion.commit();
      return { transaccionId, nuevoSaldo: Number(nuevoSaldo) };
    } catch (error) {
      await conexion.rollback();
      throw error;
    } finally { conexion.release(); }
  }
  return { pagarTarjeta: (datos) => pagar('TARJETA', datos), pagarSinpe: (datos) => pagar('SINPE', datos) };
}

export const pagosService = crearServicioPagos();
