# Organización del inicio de sesión

- `Service/LoginService.cs`: valida correo y contraseña; prepara el inicio de sesión; tras validar el código consulta los roles, emite el JWT y registra el acceso exitoso. Conserva los métodos de `ILoginService` como fachada para los endpoints actuales.
- `Service/VerificationCodeService.cs` e `IVerificationCodeService.cs`: generación aleatoria de códigos de seis dígitos, HMAC-SHA256, emisión por correo, validación y consumo de códigos de registro y login. Los vencimientos, intentos y límites de reenvío siguen siendo controlados por los mismos procedimientos de MySQL. Este servicio devuelve el usuario verificado al coordinador y no genera JWT.
- `Service/PreAuthenticationService.cs`: autorización temporal previa al segundo factor. Conserva el propósito `NineHalo.Login.PreAuthentication.v1`, el contenido del token y su duración de cinco minutos para mantener la compatibilidad con las autorizaciones existentes.
- `Service/JwtTokenService.cs`: conserva la firma y generación del JWT actual, sin cambios.

`Program.cs` registra `IVerificationCodeService` por solicitud y `PreAuthenticationService` como singleton. La integración posterior de TOTP amplía ILoginService con VerifyAuthenticatorAsync y añade AuthenticatorService/TotpRepository; conserva los endpoints, DTOs y procedimientos anteriores. Véase AUTHENTICATOR.md.

## Validación

Se ejecutaron las mismas 44 comprobaciones antes y después de separar los servicios. Las pruebas utilizan repositorios y procedimientos reales en una base MySQL temporal; capturan los correos en memoria sin conectarse a SMTP ni modificar usuarios reales. Cubren credenciales, autorización temporal, compatibilidad del propósito de protección, formato y HMAC del código, cooldown, intentos, vencimiento, consumo único, verificación de registro, roles y firma del JWT.

Desde la carpeta de la solución:

```powershell
dotnet run --project login-verification/LoginVerification.csproj -- "$PWD" "C:\Users\anton\Desktop\backup.sql"
```

El verificador utiliza la conexión configurada en APIUsuarios, o `NINEHALO_TEST_CONNECTION` si se proporciona. Requiere crear y eliminar una base temporal `ninehalo_login_verify_<identificador>`; no importa los datos del respaldo. Las claves de prueba se generan en memoria. La entrega real de correos no forma parte de estas pruebas.

