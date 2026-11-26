# PayPal en Nine Halo

Implementación de pago único por licencias mediante PayPal Checkout / Orders v2. El entorno predeterminado es Sandbox y la moneda es USD, igual que el carrito. APICompras habla con PayPal; el navegador nunca recibe el Client Secret. El gateway existente ya cubre /api/compras/**.

## Obtener y configurar credenciales

1. Abrir https://developer.paypal.com/dashboard/applications/sandbox e iniciar sesión.
2. Seleccionar Sandbox, crear una aplicación con nombre Nine Halo y asociarla a la cuenta Business de prueba.
3. Copiar Client ID y Secret a APICompras/appsettings.PayPal.local.json:

```json
{
  "PayPal": {
    "Environment": "Sandbox",
    "ClientId": "CLIENT_ID_DE_LA_APP_SANDBOX",
    "ClientSecret": "SECRET_DE_LA_APP_SANDBOX"
  }
}
```

Este archivo está excluido de Git. No colocar secretos en frontend, VITE_*, capturas de pantalla, chats ni commits. También se admiten PayPal__ClientId, PayPal__ClientSecret y PayPal__Environment como variables de entorno, que prevalecen sobre el archivo local. Reiniciar APICompras después de configurar.

Una cuenta Sandbox Business recibe los pagos; la Personal Sandbox se usa para comprar en la ventana de PayPal. Las credenciales de inicio de sesión de estas cuentas no son el Client ID/Secret de la aplicación. No se configuraron pagos reales.

## Base de datos

Ejecutar APICompras/Migrations/20261118_paypal.sql en la base configurada por APICompras. El script solo crea checkout_paypal si no existe. No modifica compras previas.

Desde la raíz de API_NineHalo también se puede ejecutar:

```powershell
dotnet run --project .\paypal-verification -- . --migrate
```

## Flujo y rutas autenticadas

- GET /api/compras/paypal/config: devuelve disponibilidad, Client ID público, entorno y moneda. Nunca devuelve el secreto.
- POST /api/compras/carrito/{clave}/paypal/orden: lee usuario del JWT y el total de las compras guardadas. Crea o reutiliza una orden y guarda su asociación con ese usuario/pedido.
- POST /api/compras/carrito/{clave}/paypal/capturar: recupera la orden guardada, consulta PayPal y captura solo si está aprobada. Verifica pedido, usuario, importe, moneda y captura COMPLETED antes de activar todas las licencias en una transacción.

No se acepta un monto ni un orderID elegidos por el cliente en estas rutas. Las claves de idempotencia son estables por usuario/pedido/acción. Repetir la confirmación consulta primero el estado remoto y no vuelve a capturar un pago completado ni extiende la vigencia de licencias activas. Una creación ambigua con más de cinco horas sin ID se bloquea para revisión, en vez de crear automáticamente otra orden.

## Frontend

Seleccionar PayPal muestra su botón oficial y oculta/deshabilita los datos bancarios. Se exige revisar el pedido antes de abrir PayPal. El entorno Sandbox se indica en pantalla. Si no hay credenciales o no carga el SDK, aparece una explicación y los pagos bancarios siguen disponibles.

Después de iniciar PayPal, el pedido queda reservado para este medio en la sesión. Cancelar la ventana no captura ni activa licencias: se puede volver a abrir la misma orden. La acción Revisar pago PayPal recupera el resultado después de errores o al recargar la pantalla, sin crear otra orden. No se borra el carrito hasta que APICompras confirma licencias ACTIVA.

## Validación local

El simulador bajo postman/ reproduce el subconjunto utilizado de OAuth y Orders v2 según la documentación oficial de PayPal. No representa una validación contra las cuentas Sandbox reales. La búsqueda de mocks publicados se omitió porque la sesión de Postman CLI no tenía una cuenta conectada.

```powershell
postman simulation run postman/simulations/paypal-checkout.sim.yaml --port 19400 --output ndjson --no-history
dotnet run --project paypal-verification -- .
```

El verificador utiliza una base temporal con un nombre único, la elimina al terminar y no usa tus compras reales. Comprueba activación, propiedad del pedido, importe, moneda, estado pendiente/rechazado y recuperación tras una respuesta perdida. NINEHALO_TEST_CONNECTION permite configurar una conexión de pruebas.


## Alcance

Se implementó captura y revisión iniciadas por el usuario. Si el usuario cierra la aplicación después de un cobro, la activación se completa al volver a revisar el pago. No se implementaron webhooks, conciliación en segundo plano, suscripciones ni reembolsos automáticos. Antes de producción se deben probar las cuentas reales de Sandbox y definir la conciliación y el tratamiento de reembolsos.

Fuentes: https://developer.paypal.com/api/orders/v2 ; https://developer.paypal.com/studio/checkout/standard/integrate ; https://developer.paypal.com/api/rest/requests/

## Resultado de esta revisión

Pasaron 17 comprobaciones de backend con MySQL aislado y simulación HTTP de PayPal, y 11 comprobaciones del formulario en Edge sin ventana visible. La tabla checkout_paypal fue creada/verificada en la base del proyecto de D:/Universidad; referencia_pago admite 120 caracteres. Las compilaciones de APICompras y frontend pasaron. Las credenciales Sandbox aún no están configuradas; falta realizar una compra de prueba con la cuenta Personal Sandbox.
