# Google Authenticator

Segundo método opcional junto al correo existente. Se vincula en **Perfil → Google Authenticator** con contraseña actual y confirmación de un código de la app. Nunca se activa sólo por generar o escanear el QR.

## Contrato

Respuestas de negocio: `{ success, message?, data }`. Las rutas de gestión requieren JWT y usan exclusivamente la identidad autenticada, sin aceptar un usuario objetivo en el cuerpo.

- GET `/api/perfil/autenticador`: `{ habilitado, id_vinculacion }`; nunca devuelve la clave.
- POST `/api/perfil/autenticador/vincular`: `{ password }`; contraseña actual, cuenta activa y correo verificado. Devuelve `{ id_vinculacion, secreto, qr_png, expires_in:600 }`. El QR es un PNG generado dentro de la API con QRCoder; `secreto` es la alternativa de ingreso manual. No se envía a Google ni a un generador externo. No se puede reemplazar una vinculación activa sin desactivarla antes.
- POST `/api/perfil/autenticador/confirmar`: `{ id_vinculacion, codigo }`; confirma posesión del secreto pendiente, activa la app y consume ese paso temporal.
- POST `/api/perfil/autenticador/desactivar`: `{ id_vinculacion, password, codigo }`; requiere contraseña y código nuevo de la app. Elimina la clave cifrada y conserva el acceso por correo. Si se pierde la app, se puede acceder por correo y solicitar ayuda a soporte; no hay una ruta anónima de desactivación.
- POST `/api/login/autenticador/estado`: `{ pre_auth_token }`; consulta disponibilidad después de verificar contraseña, sin emitir JWT.
- POST `/api/login/autenticador/verificar`: `{ pre_auth_token, codigo }`; verifica TOTP y utiliza el coordinador de login existente para consultar roles, emitir el JWT y registrar el acceso. Devuelve el mismo contrato de sesión que la verificación por correo.

Errores: 400 código/contraseña incorrectos, 401 autorización temporal o sesión inválida, 403 cuenta no habilitada, 409 vinculación vencida o modificada, 429 límite de intentos/solicitudes. No se incluyen secretos en mensajes de error. Respuestas TOTP con `Cache-Control: no-store`.

## Protección y compatibilidad

TOTP RFC 6238, HMAC-SHA1, secreto aleatorio de 160 bits, Base32 sin padding, seis dígitos y períodos de 30 segundos; tolerancia de un paso anterior o posterior. Se rechaza cualquier paso igual o anterior al último aceptado, incluso entre distintas autorizaciones temporales o solicitudes simultáneas. Después de vincular, debe usarse un código posterior para entrar.

La clave se cifra con ASP.NET Core Data Protection, propósito separado y ligado al usuario. Se mantienen las claves y el nombre de aplicación existentes. Deben conservarse y respaldarse las claves de Data Protection para poder descifrar las vinculaciones tras una reinstalación o despliegue; no basta el respaldo de MySQL.

Cinco códigos fallidos bloquean la verificación de la cuenta durante cinco minutos. El contador es persistente; cambiar de token no lo reinicia. Las rutas tienen además diez solicitudes por minuto por usuario autenticado o IP, según corresponda. Todas las mutaciones bloquean primero la fila del usuario y después la configuración, con bitácora en la misma transacción. La configuración pendiente dura diez minutos y se identifica con un valor opaco para rechazar solicitudes antiguas.

`AuthenticatorService` y `TotpRepository` contienen el ciclo de vinculación/verificación; `TotpAlgorithm` implementa el cálculo. `LoginService` conserva el control de la emisión del JWT. El frontend sólo conserva el método elegido y la autorización temporal para el login; el QR y la clave no se guardan en sessionStorage/localStorage ni se incluyen en URLs de navegación.

## Instalación y pruebas

Migración aditiva: `Migrations/20261027_usuario_totp.sql`, únicamente crea `usuario_totp`. No activa cuentas ni modifica datos existentes. El Gateway da prioridad a `/api/perfil/autenticador/**` hacia APIUsuarios; las rutas de login ya se dirigen a ese servicio.

Desde la carpeta de la solución:

```powershell
dotnet run --project totp-verification/TotpVerification.csproj -- "$PWD" "C:\Users\anton\Desktop\backup.sql"
dotnet run --project login-verification/LoginVerification.csproj -- "$PWD" "C:\Users\anton\Desktop\backup.sql"
```

La primera batería cubre vectores publicados de RFC 6238, cifrado, QR, vinculación, intentos, caducidad, repetición, concurrencia, desactivación y endpoints HTTP reales en una base temporal. La segunda conserva las 44 comprobaciones del flujo de correo; todos los correos se capturan en memoria. No se vincula ninguna cuenta real durante las pruebas. El escaneo en el teléfono del usuario se realiza al activar su propia cuenta.

Referencias: [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238), [formato de URI de Google Authenticator](https://github.com/google/google-authenticator/wiki/Key-Uri-Format), [QRCoder 1.8.1](https://www.nuget.org/packages/QRCoder/1.8.1).
