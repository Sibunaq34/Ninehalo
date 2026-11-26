# API Banco

Simulación educativa en JavaScript, Express 5 y MySQL (tablas InnoDB). Requiere Node.js 20.6 o posterior, pnpm y MySQL 8.

## Preparación

1. Abre una terminal en `D:\Universidad\Programacion\JavaScript\NineHalo\APIs\API_Banco` y ejecuta `pnpm install`.
2. El archivo `.env` ya incluye valores locales de ejemplo. Ajusta `DB_HOST`, `DB_USER`, `DB_PASSWORD`, `DB_NAME` y `PORT`. Puedes restaurarlo copiando `.env.example`.
3. En MySQL Workbench abre y ejecuta `database/schema.sql`. También puedes abrir el cliente con `mysql -u root -p` y ejecutar:

```sql
SOURCE D:/Universidad/Programacion/JavaScript/NineHalo/APIs/API_Banco/database/schema.sql;
```

El esquema crea `banco_db`. Carga los datos de prueba una sola vez; repetir los INSERT produce claves duplicadas. No elimina datos existentes.

4. Ejecuta `pnpm dev` para desarrollo con recarga o `pnpm start` para inicio normal. Puerto predeterminado: 3005.
5. Ejecuta `pnpm test` para las pruebas locales (sin requerir una base de datos).

## Requests

Envía `Content-Type: application/json` desde Postman o tu frontend.

`POST http://localhost:3005/api/pagos/tarjeta`

```json
{ "numeroTarjeta": "4111111111111111", "cvv": "123", "fechaVencimiento": "12/39", "monto": 1000 }
```

`POST http://localhost:3005/api/pagos/sinpe`

```json
{ "telefono": "+50688881111", "monto": 2500 }
```

También se acepta `50688881111` o `88881111`. Los campos tarjeta, CVV y teléfono son cadenas; monto es un número con máximo dos decimales.

Respuesta 200 (el saldo y el ID dependen de los pagos anteriores):

```json
{ "ok": true, "mensaje": "Pago con tarjeta realizado correctamente.", "data": { "transaccionId": 1, "nuevoSaldo": 99000 } }
```

## Casos de error

| Endpoint | Body JSON | HTTP |
| --- | --- | --- |
| tarjeta | `{"numeroTarjeta":"4111111111111112","cvv":"123","fechaVencimiento":"12/39","monto":100}` | 400: Luhn inválido |
| tarjeta | `{"numeroTarjeta":"4111111111111111","cvv":"999","fechaVencimiento":"12/39","monto":100}` | 400: CVV incorrecto |
| tarjeta | `{"numeroTarjeta":"4111111111111111","cvv":"123","fechaVencimiento":"11/39","monto":100}` | 400: vencimiento no coincide |
| tarjeta | `{"numeroTarjeta":"4000000000000002","cvv":"789","fechaVencimiento":"01/20","monto":100}` | 400: tarjeta vencida |
| tarjeta | `{"numeroTarjeta":"4012888888881881","cvv":"123","fechaVencimiento":"12/39","monto":100}` | 404: tarjeta inexistente |
| tarjeta | `{"numeroTarjeta":"5555555555554444","cvv":"456","fechaVencimiento":"12/39","monto":1000}` | 422: fondos insuficientes |
| sinpe | `{"telefono":"87772222","monto":1000}` | 422: fondos insuficientes |
| sinpe | `{"telefono":"89999999","monto":100}` | 404: teléfono inexistente |
| sinpe | `{"telefono":"123","monto":100}` | 400: teléfono inválido |
| sinpe | `{"telefono":"88881111","monto":0}` | 400: monto inválido |

Formato de error: `{"ok":false,"mensaje":"La cuenta no tiene fondos suficientes."}`. Los fallos internos de conexión o SQL retornan 500 sin exponer credenciales.

## Datos y funcionamiento

| Cliente | Saldo inicial | Tarjeta / CVV / vencimiento | Teléfono |
| --- | ---: | --- | --- |
| Ana Rodríguez | 100000.00 | 4111111111111111 / 123 / 12/39 | 88881111 |
| Luis Mora | 500.00 | 5555555555554444 / 456 / 12/39 | 87772222 |
| María Solís | 25000.00 | 4000000000000002 / 789 / 01/20 | 86663333 |

La API valida entradas antes de solicitar una conexión. Luego consulta el procedimiento de lectura, bloquea la cuenta con `SELECT ... FOR UPDATE`, valida credenciales y saldo, llama al procedimiento de pago y confirma la transacción. Si algo falla, revierte. Así dos pagos simultáneos sobre una cuenta no gastan el mismo saldo. Los procedimientos no contienen validaciones ni administran transacciones; deben invocarse desde este flujo de API. Los importes se comparan como centavos enteros y se guardan como DECIMAL(15,2).

Los nombres de los procedimientos son constantes y los valores se pasan mediante placeholders (`CALL sp_obtener_tarjeta(?)`); MySQL no admite un placeholder para el nombre del procedimiento. CORS está habilitado.

Este simulador guarda CVV y tarjetas ficticias en texto plano únicamente para cumplir el ejercicio; no debe usarse con datos bancarios reales. No incluye autenticación, transferencias a destinatarios ni protección contra reintentos: cada petición válida representa un pago nuevo.

## Integración con Nine Halo

El frontend llama al gateway de Nine Halo: POST /api/pagos/tarjeta y POST /api/pagos/sinpe se dirigen al banco en localhost:3005. APICompras no consulta al banco: verifica la constancia firmada que entrega el frontend.

Para firmar las constancias, configurar el mismo PAYMENT_SIGNING_KEY (al menos 32 caracteres) en Banco y APICompras, o conservar sus archivos locales .payment-signing-key. Estos archivos no se versionan y la clave no debe exponerse al frontend. El banco firma después de confirmar el pago. APICompras valida usuario, pedido e importe antes de activar las licencias y guardar la factura.

American Express requiere 15 dígitos y CVV de cuatro. En una base existente, ejecutar database/migracion_amex.sql. Para PayPal, consultar ../API_NineHalo/PAYPAL.md.
