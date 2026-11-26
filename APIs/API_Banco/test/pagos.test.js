import test from 'node:test';
import assert from 'node:assert/strict';
import { validarTarjeta, validarSinpe, validarMonto, validarVencimiento } from '../src/validators/pagos.js';
import { crearServicioPagos } from '../src/services/pagos.service.js';
import app from '../src/app.js';

test('Luhn, CVV, vencimiento y montos inválidos', () => {
  const valido = { numeroTarjeta: '4111111111111111', cvv: '123', fechaVencimiento: '12/39', monto: 10.25 };
  assert.equal(validarTarjeta(valido).monto, '10.25');
  for (const cambio of [{ numeroTarjeta: '4111111111111112' }, { cvv: '12' }, { fechaVencimiento: '13/39' }, { fechaVencimiento: '01/20' }, { monto: '10' }]) {
    assert.throws(() => validarTarjeta({ ...valido, ...cambio }), { status: 400 });
  }
  for (const monto of [0, -1, NaN, Infinity, 0.001, 1e13]) assert.throws(() => validarMonto(monto), { status: 400 });
  assert.equal(validarVencimiento('11/26', new Date(2026, 10, 30)), '11/26');
  assert.throws(() => validarVencimiento('10/26', new Date(2026, 10, 1)), { status: 400 });
});

test('normaliza prefijos de teléfono y rechaza formatos inválidos', () => {
  for (const telefono of ['88881111', '50688881111', '+50688881111']) assert.equal(validarSinpe({ telefono, monto: 1 }).telefono, '88881111');
  for (const telefono of ['12345678', '8888', 88881111, '+50788881111']) assert.throws(() => validarSinpe({ telefono, monto: 1 }), { status: 400 });
});

function conexionSimulada({ saldo = '10.00', existe = true, falla = false } = {}) {
  const llamadas = [];
  return {
    llamadas,
    beginTransaction: async () => llamadas.push('begin'),
    commit: async () => llamadas.push('commit'),
    rollback: async () => llamadas.push('rollback'),
    release: () => llamadas.push('release'),
    execute: async (sql) => {
      llamadas.push(sql);
      if (sql.includes('sp_obtener')) return [[existe ? [{ cuenta_id: 1, cvv: '123', fecha_vencimiento: '12/39' }] : []]];
      if (sql.startsWith('SELECT')) return [[{ saldo }]];
      if (falla) throw new Error('Fallo SQL simulado');
      return [[[{ transaccionId: 1, nuevoSaldo: '0.00' }]]];
    },
  };
}

test('pago bloquea cuenta, confirma y permite gastar saldo exacto', async () => {
  const c = conexionSimulada();
  const servicio = crearServicioPagos({ getConnection: async () => c });
  assert.deepEqual(await servicio.pagarSinpe({ telefono: '88881111', monto: '10.00' }), { transaccionId: 1, nuevoSaldo: 0 });
  assert.ok(c.llamadas[2].endsWith('FOR UPDATE'));
  assert.deepEqual(c.llamadas.slice(-2), ['commit', 'release']);
});

test('fondos insuficientes, inexistencia y error SQL revierten y liberan conexión', async () => {
  for (const opciones of [{ saldo: '9.99' }, { existe: false }, { falla: true }]) {
    const c = conexionSimulada(opciones);
    const servicio = crearServicioPagos({ getConnection: async () => c });
    await assert.rejects(servicio.pagarSinpe({ telefono: '88881111', monto: '10.00' }));
    assert.ok(!c.llamadas.includes('commit'));
    assert.deepEqual(c.llamadas.slice(-2), ['rollback', 'release']);
    if (!opciones.falla) assert.ok(!c.llamadas.some(x => x.includes('sp_realizar')));
  }
});

test('HTTP: validación sin BD, JSON malformado, CORS y ruta inexistente', async () => {
  const server = app.listen(0, '127.0.0.1');
  await new Promise(resolve => server.once('listening', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  try {
    for (const body of ['{"telefono":"123","monto":1}', '{']) {
      const res = await fetch(`${url}/api/pagos/sinpe`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
      assert.equal(res.status, 400);
      assert.equal((await res.json()).ok, false);
      assert.equal(res.headers.get('access-control-allow-origin'), '*');
    }
    assert.equal((await fetch(`${url}/inexistente`)).status, 404);
  } finally { await new Promise(resolve => server.close(resolve)); }
});

test('American Express acepta 15 dígitos y exige CVV de cuatro',()=>{
 const tarjeta={numeroTarjeta:'378282246310005',cvv:'1234',fechaVencimiento:'12/39',monto:1};
 assert.equal(validarTarjeta(tarjeta).numeroTarjeta,tarjeta.numeroTarjeta);
 assert.throws(()=>validarTarjeta({...tarjeta,cvv:'123'}),{status:400});
 assert.throws(()=>validarTarjeta({...tarjeta,numeroTarjeta:'378282246310006'}),{status:400});
});
