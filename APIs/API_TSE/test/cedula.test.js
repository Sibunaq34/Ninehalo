import test from 'node:test';
import assert from 'node:assert/strict';
import { validarCedula } from '../src/validators/cedula.validator.js';

test('acepta cédula de nueve dígitos y normaliza guiones o espacios', () => {
  for (const valor of ['102340567', '1-0234-0567', '1 0234 0567', ' 102340567 ']) {
    assert.equal(validarCedula(valor), '102340567');
  }
});
test('rechaza vacíos, longitudes incorrectas y caracteres ajenos', () => {
  for (const valor of [undefined, null, '', ' ', '---', 102340567, '123', '1023405670', 'a102340567', '1/0234/0567', '102340567 OR 1=1']) {
    assert.throws(() => validarCedula(valor), { status: 400 });
  }
});
