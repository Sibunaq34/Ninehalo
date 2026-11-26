using APINineHalo.Entities;
using APINineHalo.Repository;
using System.Security.Cryptography;
using System.Text;
using MySql.Data.MySqlClient;

namespace APINineHalo.Services
{
    /// <summary>Emite y valida códigos por correo; nunca genera tokens JWT.</summary>
    public class VerificationCodeService : IVerificationCodeService
    {
        private const string LoginPurpose = "LOGIN_2FA";
        private const int CodeExpirationSeconds = 300;
        private readonly LoginRepository _repository;
        private readonly UsuarioRepository _usuarioRepository;
        private readonly IEmailService _emailService;
        private readonly PreAuthenticationService _preAuthentication;
        private readonly byte[] _verificationSecret;

        public VerificationCodeService(
            LoginRepository repository,
            UsuarioRepository usuarioRepository,
            IEmailService emailService,
            IConfiguration configuration,
            PreAuthenticationService preAuthentication)
        {
            _repository = repository;
            _usuarioRepository = usuarioRepository;
            _emailService = emailService;
            _preAuthentication = preAuthentication;
            string secret = configuration["VerificationCode:SecretKey"]
                ?? throw new InvalidOperationException("Falta configurar VerificationCode:SecretKey.");
            _verificationSecret = Convert.FromBase64String(secret);
            if (_verificationSecret.Length < 32)
                throw new InvalidOperationException("La clave debe contener al menos 32 bytes.");
        }

        public async Task<(
            bool Success,
            string Message,
            LoginResponseDto? Data)> SendLoginCodeAsync(
                string preAuthToken)
        {
            if (string.IsNullOrWhiteSpace(preAuthToken))
            {
                return (false, "Solicitud de verificación inválida.", null);
            }

            int idUsuario;

            try
            {
                string contenido = _preAuthentication.Unprotect(
                    preAuthToken
                );

                if (!int.TryParse(contenido, out idUsuario) ||
                    idUsuario <= 0)
                {
                    return (false, "Solicitud de verificación inválida.", null);
                }
            }
            catch (CryptographicException)
            {
                return (
                    false,
                    "La autorización temporal expiró o es inválida. Inicia sesión nuevamente.",
                    null
                );
            }

            // Volver a comprobar el estado de la cuenta.
            var usuario = await _usuarioRepository.GetUserByIdAsync(
                idUsuario
            );

            if (usuario == null ||
                !string.Equals(
                    usuario.Estado,
                    "ACTIVO",
                    StringComparison.OrdinalIgnoreCase) ||
                !usuario.Correo_Verificado)
            {
                return (
                    false,
                    "La cuenta no está habilitada para iniciar sesión.",
                    null
                );
            }

            // Generar el código solamente ahora.
            string codigo = GenerarCodigo();

            byte[] codigoHash = CalcularHash(
                idUsuario,
                LoginPurpose,
                codigo
            );

            long idVerificacion;

            try
            {
                idVerificacion =
                    await _repository.CreateVerificationCodeAsync(
                        idUsuario,
                        LoginPurpose,
                        codigoHash
                    );
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
                when (ex.Number == 1644)
            {
                return (
                    false,
                    "No puedes solicitar otro código todavía. Intenta nuevamente más tarde.",
                    null
                );
            }

            try
            {
                await _emailService.SendVerificationCodeAsync(
                    usuario.Correo,
                    codigo,
                    LoginPurpose
                );
            }
            catch
            {
                return (
                    false,
                    "No se pudo enviar el código de verificación.",
                    null
                );
            }

            return (
                true,
                "Se envió un código de verificación a tu correo.",
                new LoginResponseDto
                {
                    Requires2FA = true,
                    IdVerificacion = idVerificacion,
                    ExpiresIn = CodeExpirationSeconds
                }
            );
        }

        public async Task<(
            bool Success,
            string Message,
            int? UserId)> VerifyLoginCodeAsync(
                VerifyLoginCodeRequest request)
        {
            if (request == null ||
                request.IdVerificacion <= 0 ||
                !CodigoValido(request.Codigo))
            {
                return (
                    false,
                    "Código de verificación inválido.",
                    null
                );
            }

            // Buscar internamente al propietario del desafío
            int? idUsuario =
                await _repository.GetUserIdByVerificationCodeAsync(
                    request.IdVerificacion
                );

            if (!idUsuario.HasValue || idUsuario.Value <= 0)
            {
                return (
                    false,
                    "Código inválido, expirado o ya utilizado.",
                    null
                );
            }

            // Recalcular HMAC con los datos originales
            byte[] codigoHash = CalcularHash(
                idUsuario.Value,
                LoginPurpose,
                request.Codigo
            );

            bool codigoCorrecto;

            try
            {
                codigoCorrecto =
                    await _repository.ValidateVerificationCodeAsync(
                        idUsuario.Value,
                        request.IdVerificacion,
                        LoginPurpose,
                        codigoHash
                    );
            }
            catch (MySqlException ex) when (ex.Number == 1644)
            {
                return (
                    false,
                    "Código inválido, expirado o ya utilizado.",
                    null
                );
            }

            if (!codigoCorrecto)
            {
                return (
                    false,
                    "Código de verificación incorrecto.",
                    null
                );
            }

            return (true, "Código de verificación correcto.", idUsuario.Value);
        }

        public async Task<(
            bool Success,
            string Message,
            LoginResponseDto? Data)>
            SendRegistrationCodeAsync(int idUsuario)
        {
            if (idUsuario <= 0)
            {
                return (false, "Usuario inválido.", null);
            }

            var usuario = await _usuarioRepository.GetUserByIdAsync(
                idUsuario
            );

            if (usuario == null)
            {
                return (false, "Usuario no encontrado.", null);
            }

            if (usuario.Estado != "ACTIVO")
            {
                return (false, "La cuenta no está activa.", null);
            }

            if (usuario.Correo_Verificado)
            {
                return (false, "El correo ya está verificado.", null);
            }

            string codigo = GenerarCodigo();

            byte[] hash = CalcularHash(
                usuario.Id_Usuario,
                "REGISTRO",
                codigo
            );

            long idVerificacion;

            try
            {
                idVerificacion =
                    await _repository.CreateVerificationCodeAsync(
                        usuario.Id_Usuario,
                        "REGISTRO",
                        hash
                    );
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
                when (ex.Number == 1644)
            {
                return (
                    false,
                    "No se puede emitir otro código en este momento.",
                    null
                );
            }

            try
            {
                await _emailService.SendVerificationCodeAsync(
                    usuario.Correo,
                    codigo,
                    "REGISTRO"
                );
            }
            catch
            {
                return (
                    false,
                    "La cuenta fue creada, pero no se pudo enviar " +
                    "el código de verificación.",
                    null
                );
            }

            return (
                true,
                "Se envió un código para verificar tu correo.",
                new LoginResponseDto
                {
                    Requires2FA = false,
                    IdVerificacion = idVerificacion,
                    ExpiresIn = CodeExpirationSeconds
                }
            );
        }

        public async Task<(bool Success, string Message)>
            VerifyRegistrationCodeAsync(
                VerifyLoginCodeRequest request)
        {
            if (request == null ||
                request.IdVerificacion <= 0 ||
                request.Codigo == null ||
                request.Codigo.Length != 6 ||
                !request.Codigo.All(c => c >= '0' && c <= '9'))
            {
                return (false, "Código de verificación inválido.");
            }

            int? idUsuario =
                await _repository.GetUserIdByRegistrationCodeAsync(
                    request.IdVerificacion
                );

            if (!idUsuario.HasValue)
            {
                return (
                    false,
                    "Código inválido, expirado o ya utilizado."
                );
            }

            byte[] hash = CalcularHash(
                idUsuario.Value,
                "REGISTRO",
                request.Codigo
            );

            bool valido;

            try
            {
                valido = await _repository.ValidateVerificationCodeAsync(
                    idUsuario.Value,
                    request.IdVerificacion,
                    "REGISTRO",
                    hash
                );
            }
            catch (MySql.Data.MySqlClient.MySqlException ex)
                when (ex.Number == 1644)
            {
                return (
                    false,
                    "Código inválido, expirado o ya utilizado."
                );
            }

            if (!valido)
            {
                return (false, "Código incorrecto.");
            }

            return (true, "Correo electrónico verificado correctamente.");
        }

        private static string GenerarCodigo()
        {
            int numero = RandomNumberGenerator.GetInt32(
                0,
                1_000_000
            );

            return numero.ToString("D6");
        }

        private static bool CodigoValido(string? codigo)
        {
            return codigo != null &&
                   codigo.Length == 6 &&
                   codigo.All(c => c >= '0' && c <= '9');
        }

        private byte[] CalcularHash(
            int idUsuario,
            string proposito,
            string codigo)
        {
            string contenido =
                $"{idUsuario}:{proposito}:{codigo}";

            byte[] datos = Encoding.UTF8.GetBytes(contenido);

            return HMACSHA256.HashData(
                _verificationSecret,
                datos
            );
        }
    }
}
