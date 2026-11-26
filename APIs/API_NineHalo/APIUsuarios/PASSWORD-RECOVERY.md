# Recuperación de contraseña

En el login, **Olvidé mi contraseña** abre `/recuperar-contrasena`. El usuario introduce el correo de su cuenta y elige Google Authenticator o preguntas. Una prueba válida permite pasar directamente a `/restablecer-contrasena`. No crea una sesión: tras guardar se debe iniciar sesión normalmente.

## Contrato

Todos los endpoints son POST anónimos bajo `/api/login/recuperacion`, con cuerpos JSON y `Cache-Control: no-store`. El Gateway ya enruta `/api/login/{**catch-all}` a APIUsuarios.

| Ruta | Cuerpo | Resultado |
| --- | --- | --- |
| `/preguntas` | `correo`, `identificacion`, `telefono` | Ambas respuestas correctas: `{success:true,data:{token,expires_in:600}}` |
| `/autenticador` | `correo`, `codigo` | Código válido de la app previamente vinculada: mismo resultado |
| `/restablecer` | `token`, `password_nueva`, `confirmacion` | `{success:true,message}`; contraseña actualizada con BCrypt |

Errores: 400 datos/prueba no válidos; 401 autorización vencida o consumida; 429 límite de solicitudes/códigos. Los errores de preguntas no indican cuál respuesta coincidió ni si existe el correo. Sólo se admiten cuentas activas y verificadas.

## Reglas

- Preguntas: identificación y teléfono registrado; ambas deben coincidir. La identificación ignora separadores; el teléfono compara todos los dígitos, sin eliminar el código de país. Valores ausentes no cuentan como coincidencia. Cinco pruebas fallidas bloquean ese método quince minutos, persistiendo en MySQL. El correo sólo identifica la cuenta y no cuenta como respuesta.
- Identificación y teléfono son datos personales, no secretos. Authenticator ofrece una prueba de posesión más fuerte.
- Authenticator reutiliza la validación TOTP, cinco fallos/cinco minutos y rechazo de códigos usados, incluyendo los usados para login o activación. No necesita contraseña anterior ni envía correo.
- Autorización protegida por Data Protection con propósito exclusivo, válida diez minutos y ligada al hash de contraseña y un nonce aleatorio. MySQL guarda sólo SHA-256 del nonce. Una nueva prueba válida reemplaza la autorización anterior. El cambio bloquea filas, valida y consume atómicamente; un cambio de contraseña por otra vía también invalida la autorización.
- Nueva contraseña: mínimo ocho caracteres, máximo 72 bytes UTF-8 por el límite de BCrypt, confirmación coincidente y distinta de la anterior. No se desbloquean cuentas ni se modifica la vinculación de Authenticator.
- Frontend mantiene la autorización sólo en memoria, sin URL ni almacenamiento persistente. Recargar exige verificar la identidad otra vez. No se devuelve JWT ni se cambia la sesión existente. Los JWT ya emitidos conservan su vencimiento normal; este cambio no incorpora revocación global de sesiones.
- Límite HTTP compartido con las rutas de Authenticator: diez solicitudes/minuto por IP o usuario autenticado. La protección por cuenta de las preguntas es independiente y persiste entre reinicios.

## Instalación y pruebas

Ejecutar la migración aditiva `Migrations/20261027_password_recovery.sql` antes de iniciar el backend actualizado. No cambia contraseñas existentes. Mantener las claves Data Protection al reiniciar.

Desde la carpeta de la solución:

```powershell
dotnet run --project recovery-verification/RecoveryVerification.csproj -- . --migrate
dotnet run --project recovery-verification/RecoveryVerification.csproj -- . 'C:/Users/anton/Desktop/backup.sql'
```

El primer comando exige que la configuración apunte a `nine_halo`; crea sólo la tabla. El segundo crea y elimina una base temporal, utiliza únicamente el esquema/procedimientos del respaldo y cuentas sintéticas, y comprueba los endpoints HTTP reales. No envía correos ni modifica cuentas reales.
