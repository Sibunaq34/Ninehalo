# Contrato de licencias, estadísticas y soporte

## Perfil de cuenta

GET y PUT `/api/perfil/{id}` requieren JWT de APIUsuarios. La consulta llama a `sp_consultar_perfil(p_actor,p_usuario)` con la identidad del token y devuelve identificación personal, nombre, apellidos, teléfono, correo, estado y licencia activa más reciente. `id_usuario`/`id` identifican la cuenta; `identificacion` contiene la cédula. Sin licencia activa, `tipo_licencia` y `fecha_caducidad` son null.

PUT acepta nombre, apellidos y correo; conserva el teléfono existente y devuelve el perfil completo tras ejecutar `sp_editar_perfil`, que no devuelve filas. El editor permite actualizar únicamente la cuenta autenticada. El Gateway dirige estas rutas al puerto 5090 de API_Persona, conforme a launchSettings.json. La ruta específica de contraseña sigue en APIUsuarios.

Validación de perfil: 27 aserciones HTTP con procedimientos reales en una base MySQL temporal, incluyendo carga de datos, licencia activa/vencida, identidad, acceso denegado, correo duplicado, guardado y conservación de teléfono e identificación. El frontend incorpora estados de carga, error y reintento, y deja de usar el número interno de cuenta como cédula.

Referencia: estructura y procedimientos del backup.sql del 26 de octubre de 2026. No se importa el respaldo ni se reemplazan datos.

Todas las respuestas de negocio usan `{ success, message, data }`. Las rutas privadas requieren Bearer JWT de APIUsuarios; el actor siempre se obtiene del token. Los permisos administrativos se comprueban en los procedimientos de MySQL. Configurar Jwt:SecretKey (Base64, mínimo 32 bytes), Jwt:Issuer y Jwt:Audience iguales a APIUsuarios; en desarrollo se reutiliza su UserSecretsId. En producción usar configuración externa.

- GET /api/licencias: público; catálogo activo con descripción, precio, duración, máximo de juegos y detalles ordenados.
- GET /api/licencias/usuario/{idUsuario}: privado, sólo el propio usuario; compra ACTIVA no vencida más reciente, o data null. `precio` corresponde al precio pagado. Se conservan los campos existentes y se añaden límite y duración adquiridos.
- PUT /api/licencias/{id}: privado; nombre, descripcion, precio, duracion_meses, max_juegos, activo. Requiere licencia.gestionar en BD.
- GET /api/estadisticas/{id}: privado; consulta con actor y usuario. Conserva plataforma y jugadores_baneados; añade id_compra, id_compra_juego, licencia, id_juego y juego. Sin registros devuelve data [].
- PUT /api/estadisticas: privado; id_compra_juego, id_plataforma y jugadores_baneados no negativo. Requiere estadistica.gestionar; valida compatibilidad mediante el procedimiento.
- GET /api/conversaciones: privadas del usuario o agente autenticado.
- POST /api/conversaciones: asunto obligatorio (máximo 180), id_compra opcional propio. Devuelve 201 e id_conversacion. No exige licencia, conforme al respaldo.
- GET /api/conversaciones/soporte?pagina=1&tamano=10: bandeja de soporte autorizada por BD.
- GET /api/conversaciones/{id}/mensajes?pagina=1&tamano=10: participante autorizado; mensajes en orden descendente. Página >=1 y tamaño 1..100.
- POST /api/conversaciones/{id}/mensajes: contenido obligatorio, máximo 10000 caracteres; devuelve 201 e id_mensaje.
- PUT /api/conversaciones/{id}/agente: id_agente; requiere chat.asignar.
- PUT /api/conversaciones/{id}/estado: estado ABIERTA, EN_PROCESO, RESUELTA o CERRADA; agente asignado o chat.gestionar.
- GET /api/tickets/{idUsuario}?pagina=1: compatibilidad con frontend; conversaciones propias convertidas a id_soporte, titulo, fecha_creacion, estado, con totales y páginas de 10.
- POST /api/tickets: compatibilidad; id_usuario debe coincidir con token, titulo (máximo 180), detalle (máximo 10000), id_compra opcional. Conversación y mensaje inicial se guardan en una transacción; devuelve 201 con id_soporte e id_conversacion.

Errores: 400 entradas o reglas de negocio inválidas, 401 JWT ausente/inválido, 403 acceso denegado, 404 recurso inexistente cuando el procedimiento lo distingue, 409 duplicado, 500 error interno sin detalles de infraestructura. Los rechazos JWT usan la respuesta estándar del middleware de autenticación.

El frontend actual muestra un aviso de licencia para cualquier 403 de tickets; debe interpretar permisos por separado si incorpora las nuevas pantallas de agentes. Las rutas y cuerpos antiguos siguen disponibles.

## Validación

Los tres servicios y Gateway compilaron sin advertencias ni errores. La verificación ejecutó 52 aserciones HTTP contra los servicios reales usando una base MySQL temporal creada a partir de las tablas y procedimientos del respaldo, sin importar sus datos. Incluye JWT ausente, vencido y con firma inválida; acceso cruzado; detalles ordenados; conservación del precio y límite adquiridos; estadísticas de más de 32 bits; escritura administrativa; compatibilidad de tickets; mensajes y asignación de agentes; cierre de chat y rollback completo ante fallo del mensaje inicial.

Para repetir desde esta carpeta, compilar APILicencias, APIEstadisticas y APISoporte, y ejecutar:

```powershell
dotnet run --project verification/ApiVerification.csproj -- "$PWD" "C:\Users\anton\Desktop\backup.sql"
```

El verificador usa la conexión configurada en APILicencias, o `NINEHALO_TEST_CONNECTION` si está definida. Necesita permisos para crear y eliminar exclusivamente su base temporal `ninehalo_verify_<identificador>`. La clave JWT de prueba se genera en memoria y los procesos de prueba se cierran al terminar. No se verificó la navegación de las pantallas en el navegador.

## Tipo de cambio BCCR (SDDE)

GET /api/compras/tipo-cambio requiere sesión y entrega la tasa de venta del indicador 318, fuente BCCR, fecha de publicación e identificador de cotización. El servidor consulta la API REST SDDE con Bearer Token, busca la última observación válida hasta el día actual de Costa Rica dentro de los siete días previos y mantiene caché de quince minutos. No utiliza el servicio SOAP antiguo.

Guardar el token en APICompras/appsettings.Bccr.local.json bajo Bccr.Token, o en Bccr__Token como variable de entorno. El archivo local está excluido de Git y de publicación. Obtener el token en https://sdd.bccr.fi.cr/es/IndicadoresEconomicos/Inicio/ → Mi Perfil → Generar token. Reiniciar APICompras al cambiarlo. Nunca colocar el token en el frontend.

La preparación de factura usa exclusivamente la tasa del servidor; rechaza cotizaciones que no coincidan con la mostrada. Un pedido preparado con referencia oficial conserva esa tasa para recuperar el mismo pago. Las facturas guardan fuente y fecha en su JSON sin requerir otra migración. Facturas anteriores mantienen sus valores y, cuando corresponde, su etiqueta de referencia provisional. Los importes de cobro siguen en USD.

Prueba real del proveedor realizada el 19/11/2026: respuesta válida del indicador 318 con fecha 19/11/2026. Para repetir sin mostrar el token: dotnet run --project bccr-verification -- APICompras --live desde la raíz de la solución. Las pruebas simuladas utilizan postman/simulations/bccr-checkout.sim.yaml.

## Integración local con socios

Aplicá `database/schema.sql` de `API_Salesforce`, `API_ContaCloud` y `API_NotifyHub` con el cliente MySQL. Aplicá también `APISoporte/Migrations/20261124_socios.sql` en la base `nine_halo` compartida con APICompras. Cada script conserva las tablas y los datos existentes.

| Servicio | Destino local | Configuración ASP.NET Core |
| --- | --- | --- |
| Salesforce | `http://127.0.0.1:3007/api/socios/salesforce/casos` | `Partners__Salesforce__Key`, `Partners__Salesforce__AgentId` en APISoporte |
| ContaCloud | `http://127.0.0.1:3008/api/socios/contacloud/ventas` | `Partners__ContaCloud__Key` en APICompras |
| NotifyHub | `http://127.0.0.1:3009/api/socios/notifyhub/avisos` | `Partners__NotifyHub__Key` en APISoporte y APICompras |

En cada socio, definí `PARTNER_KEY` con al menos 24 caracteres y usá ese mismo valor en la variable correspondiente de ASP.NET Core. Podés definir la variable del proceso para conservar el archivo `.env`. Iniciá cada API Node con `npm start` desde su carpeta. El agente debe ser una cuenta activa de soporte dedicada a Salesforce. Para cambiar los destinos de salida, definí `Partners__<Socio>__Url` con la URL completa de su endpoint. Salesforce admite `NINEHALO_URL` como URL base del callback y usa APISoporte en el puerto 5206 por defecto.

Los tickets ingresados por `POST /api/tickets` generan un caso con `id_compra` igual a `"0"` cuando no se asocia una compra. Salesforce envía las respuestas a `POST /api/conversaciones/socio/respuesta` con `X-Partner-Key` y escribe en el chat de NineHalo. La misma referencia se puede reenviar sin duplicar mensajes. Una factura de carrito confirmada por banco o PayPal se registra como venta. NotifyHub recibe avisos de ticket, respuesta de soporte y pago confirmado para usuarios con correo verificado.

Los envíos se registran junto con la operación de NineHalo y se entregan en segundo plano. Consultá `socio_envio` para revisar los pendientes y la fecha de entrega. Los socios usan la referencia de cada operación para aceptar reintentos sin duplicar registros. YARP expone las rutas `/api/socios/<socio>/...` y conserva la ruta de retorno de Salesforce bajo `/api/conversaciones`.
## Integración local con socios

Aplicá `database/schema.sql` de `API_Salesforce`, `API_ContaCloud` y
`API_NotifyHub` con el cliente MySQL. Aplicá también
`APISoporte/Migrations/20261124_socios.sql` en `nine_halo`. Los scripts
crean las tablas si faltan y conservan sus filas. Usá las migraciones
previas de compras y soporte correspondientes a tu base.

En desarrollo, `APISoporte` y `APICompras` leen
`appsettings.Partners.local.json` desde la raíz de la solución. Las
claves `Partners:Salesforce:Key`, `Partners:ContaCloud:Key` y
`Partners:NotifyHub:Key` deben coincidir con `PARTNER_KEY` en el
`.env` de cada socio. Para habilitar las respuestas de Salesforce,
configurá `Partners:Salesforce:AgentId` con el ID de una cuenta activa
designada para soporte. En otros entornos se pueden usar las variables
`Partners__Salesforce__Key`, `Partners__ContaCloud__Key`,
`Partners__NotifyHub__Key` y `Partners__Salesforce__AgentId`.

Arrancá cada socio con `npm start` desde su carpeta y ejecutá
`APISoporte`, `APICompras` y `Gateway` con el perfil HTTP. El Gateway
local usa el puerto `5253`. Configurá `NINEHALO_URL` de Salesforce con
la URL del Gateway para que `POST /api/socios/salesforce/respuestas`
reenvíe la respuesta a `POST /api/conversaciones/socio/respuesta`.
Las solicitudes a socios usan el encabezado `X-Partner-Key`.

