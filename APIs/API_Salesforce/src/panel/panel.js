const access = document.querySelector('#acceso');
const reply = document.querySelector('#respuesta');
const cases = document.querySelector('#caso');
const detail = document.querySelector('#detalle');
const content = document.querySelector('#contenido');
const state = document.querySelector('#estado');
const result = document.querySelector('#resultado');
let key = '', rows = [], pending;

async function request(resource, body) {
  const response = await fetch('/api/socios/salesforce/' + resource, {
    method: body ? 'POST' : 'GET',
    headers: { 'X-Partner-Key': key, 'Content-Type': 'application/json' },
    body: body && JSON.stringify(body)
  });
  const payload = await response.json();
  if (!response.ok) throw new Error(payload.error || 'No se pudo completar la solicitud.');
  return payload.data;
}

function showCase() {
  const selected = rows.find(row => String(row.id) === cases.value);
  detail.textContent = selected ? selected.estado + '\n' + selected.detalle : '';
  state.value = selected?.estado === 'RESUELTA' ? 'RESUELTA' : 'EN_PROCESO';
}

async function loadCases() {
  rows = await request('casos');
  const previous = cases.value;
  cases.replaceChildren(...rows.map(row => new Option('#' + row.id + ' · ' + row.asunto, row.id)));
  if (rows.some(row => String(row.id) === previous)) cases.value = previous;
  reply.hidden = rows.length === 0;
  reply.style.display = rows.length ? '' : 'none';
  showCase();
}

access.addEventListener('submit', async event => {
  event.preventDefault();
  const button = access.querySelector('button');
  button.disabled = true;
  key = document.querySelector('#clave').value;
  try {
    await loadCases();
    result.textContent = rows.length ? 'Casos cargados.' : 'No hay casos registrados.';
  } catch (error) {
    reply.hidden = true;
    reply.style.display = 'none';
    result.textContent = error.message;
  } finally { button.disabled = false; }
});

cases.addEventListener('change', showCase);
reply.addEventListener('submit', async event => {
  event.preventDefault();
  const body = { id_conversacion: cases.value, contenido: content.value.trim(), estado: state.value };
  if (!body.contenido) { result.textContent = 'Escribe una respuesta.'; return; }
  const signature = JSON.stringify(body);
  if (pending?.signature !== signature) pending = { signature, reference: crypto.randomUUID() };
  const controls = [...reply.elements, access.querySelector('button')];
  controls.forEach(control => { control.disabled = true; });
  try {
    const confirmed = await request('respuestas', { ...body, referencia: pending.reference });
    pending = undefined;
    content.value = '';
    const row = rows.find(row => String(row.id) === body.id_conversacion);
    if (row) row.estado = confirmed.estado;
    showCase();
    result.textContent = 'NineHalo recibió la respuesta del ticket #' + body.id_conversacion + '.';
  } catch (error) {
    result.textContent = error.message;
  } finally { controls.forEach(control => { control.disabled = false; }); }
});
