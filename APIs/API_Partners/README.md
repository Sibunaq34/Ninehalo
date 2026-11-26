# NineHalo Partner API

FastAPI usa MySQL por detrás. En local la API escucha en el puerto 8011 y los
socios acceden a las cuatro rutas por YARP en el puerto 5253.

```powershell
cd API_Partners
.\.venv\Scripts\python.exe -m uvicorn main:app --host 127.0.0.1 --port 8011
```

Para otra instalación, creá el entorno virtual e instalá `requirements.txt`.
Aplicá `database/schema.sql` con una cuenta administradora y configurá el
`.env` con una cuenta MySQL exclusiva del servicio. Concedé SELECT en
`licencia`, `detalle_licencia` y `partner_comercial`, y SELECT e INSERT en
`partner_pedido`. Guardá el SHA-256 binario de una clave aleatoria por socio
en `partner_comercial.clave_hash` y entregale únicamente su clave de API.

En Postman configurá el encabezado `X-Partner-Key` con la clave del socio.
Para pruebas locales usá `PARTNER_DEMO_KEY` del `.env`.

| Método | URL por YARP |
|---|---|
| GET | `http://localhost:5253/api/partners/v1/licencias` |
| POST | `http://localhost:5253/api/partners/v1/pedidos` |
| GET | `http://localhost:5253/api/partners/v1/pedidos/{id}` |
| GET | `http://localhost:5253/api/partners/v1/estadisticas?desde=2026-11-01&hasta=2026-11-30` |

El POST requiere `Content-Type: application/json`.

```json
{"referencia":"TIENDA-001","id_licencia":2,"cantidad":1}
```

La primera solicitud devuelve 201 con el pedido pendiente. Repetir los
mismos datos devuelve 200 con el mismo pedido. Reutilizar la referencia con
otra licencia o cantidad devuelve 409. Las estadísticas incluyen solo
pedidos confirmados y usan fechas UTC inclusivas.

La confirmación de pagos y la activación de licencias para pedidos de socios
quedan pendientes de una integración interna de NineHalo. El socio puede
consultar el estado, pero la API comercial no le permite cambiarlo.
