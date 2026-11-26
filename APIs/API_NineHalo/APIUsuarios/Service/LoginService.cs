using APINineHalo.Entities;
using APINineHalo.Repository;

namespace APINineHalo.Services
{
    /// <summary>Coordina credenciales, segundo factor y emisión del JWT.</summary>
    public class LoginService : ILoginService
    {
        private readonly LoginRepository _repository;
        private readonly JwtTokenService _jwtTokenService;
        private readonly IVerificationCodeService _verificationCodes;
        private readonly PreAuthenticationService _preAuthentication;
        private readonly AuthenticatorService _authenticator;

        public LoginService(
            LoginRepository repository,
            JwtTokenService jwtTokenService,
            IVerificationCodeService verificationCodes,
            PreAuthenticationService preAuthentication, AuthenticatorService authenticator)
        {
            _repository = repository;
            _jwtTokenService = jwtTokenService;
            _verificationCodes = verificationCodes;
            _preAuthentication = preAuthentication;
            _authenticator = authenticator;
        }

        public async Task<(
            bool Success,
            string Message,
            LoginPreparationDto? Data)> LoginAsync(
                string correo,
                string password)
        {
            var (success, message, usuario) =
                await ValidarCredencialesAsync(correo, password);

            if (!success || usuario == null)
            {
                return (false, message, null);
            }

            // Token exclusivo para continuar el proceso 2FA.
            // No es un JWT y no concede acceso a la aplicación.
            string preAuthToken = _preAuthentication.CreateToken(usuario.Id_Usuario);

            return (
                true,
                "Credenciales válidas. Selecciona un método de verificación.",
                new LoginPreparationDto
                {
                    Requires2FA = true,
                    PreAuthToken = preAuthToken,
                    ExpiresIn = PreAuthenticationService.ExpirationSeconds
                }
            );
        }

        public async Task<(
            bool Success,
            string Message,
            string? AccessToken)> VerifyCodeAsync(
                VerifyLoginCodeRequest request)
        {
            var result = await _verificationCodes.VerifyLoginCodeAsync(request);
            if (!result.Success || !result.UserId.HasValue)
            {
                return (false, result.Message, null);
            }
            int idUsuario = result.UserId.Value;

            return await CompleteLoginAsync(idUsuario);
        }

        public async Task<(bool Success, string Message, string? AccessToken)> VerifyAuthenticatorAsync(VerifyAuthenticatorRequest request)
        {
            var result = await _authenticator.VerifyLoginAsync(request);
            if (!result.Success || !result.UserId.HasValue)
            {
                if (result.Status == 429) throw new AuthenticatorException(result.Message, 429);
                return (false, result.Message, null);
            }
            return await CompleteLoginAsync(result.UserId.Value);
        }

        private async Task<(bool Success, string Message, string? AccessToken)> CompleteLoginAsync(int idUsuario)
        {
            // Consultar roles desde MySQL, nunca desde el frontend
            var roles = (await _repository.GetUserRolesAsync(
                idUsuario
            )).ToList();

            if (roles.Count == 0)
            {
                return (
                    false,
                    "El usuario no tiene roles activos asignados.",
                    null
                );
            }

            // Generar JWT solamente después de validar el código
            string accessToken = _jwtTokenService.GenerateToken(
                idUsuario,
                roles
            );

            // Registrar login exitoso
            await _repository.RegisterLoginAttemptAsync(
                idUsuario,
                true
            );

            return (
                true,
                "Inicio de sesión exitoso.",
                accessToken
            );
        }

        private async Task<(
            bool Success,
            string Message,
            UserAuthEntity? Usuario)> ValidarCredencialesAsync(
                string correo,
                string password)
        {
            if (string.IsNullOrWhiteSpace(correo) ||
                string.IsNullOrEmpty(password) ||
                password.Length > 1024)
            {
                return (
                    false,
                    "Correo o contraseña incorrectos.",
                    null
                );
            }

            correo = correo.Trim();

            if (correo.Length > 150)
            {
                return (
                    false,
                    "Correo o contraseña incorrectos.",
                    null
                );
            }

            var usuario = await _repository.GetUserByEmailAsync(correo);

            if (usuario == null)
            {
                return (
                    false,
                    "Correo o contraseña incorrectos.",
                    null
                );
            }

            bool passwordValida;

            try
            {
                passwordValida = BCrypt.Net.BCrypt.Verify(
                    password,
                    usuario.Password_Hash
                );
            }
            catch
            {
                passwordValida = false;
            }

            if (!passwordValida)
            {
                await _repository.RegisterLoginAttemptAsync(
                    usuario.Id_Usuario,
                    false
                );

                return (
                    false,
                    "Correo o contraseña incorrectos.",
                    null
                );
            }

            if (!string.Equals(
                usuario.Estado,
                "ACTIVO",
                StringComparison.OrdinalIgnoreCase))
            {
                return (
                    false,
                    "La cuenta no está activa.",
                    null
                );
            }

            if (!usuario.Correo_Verificado)
            {
                return (
                    false,
                    "Debes verificar tu correo electrónico.",
                    null
                );
            }

            return (
                true,
                "Credenciales válidas.",
                usuario
            );
        }
        // Se conserva ILoginService para no cambiar los endpoints existentes.
        public Task<(bool Success, string Message, LoginResponseDto? Data)>
            SendLoginCodeAsync(string preAuthToken) =>
            _verificationCodes.SendLoginCodeAsync(preAuthToken);

        public Task<(bool Success, string Message, LoginResponseDto? Data)>
            SendRegistrationCodeAsync(int idUsuario) =>
            _verificationCodes.SendRegistrationCodeAsync(idUsuario);

        public Task<(bool Success, string Message)>
            VerifyRegistrationCodeAsync(VerifyLoginCodeRequest request) =>
            _verificationCodes.VerifyRegistrationCodeAsync(request);
    }
}

