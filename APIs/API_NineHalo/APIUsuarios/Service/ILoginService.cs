
using APINineHalo.Entities;

namespace APINineHalo.Services
{
    public interface ILoginService
    {
        Task<(bool Success, string Message,
            LoginPreparationDto? Data)>
            LoginAsync(string correo, string password);

        Task<(bool Success, string Message,
            LoginResponseDto? Data)>
            SendLoginCodeAsync(string preAuthToken);

        Task<(bool Success, string Message,
            string? AccessToken)>
            VerifyCodeAsync(VerifyLoginCodeRequest request);

        Task<(bool Success, string Message,
            LoginResponseDto? Data)>
            SendRegistrationCodeAsync(int idUsuario);

        Task<(bool Success, string Message)>
            VerifyRegistrationCodeAsync(
                VerifyLoginCodeRequest request);
        Task<(bool Success, string Message, string? AccessToken)>
            VerifyAuthenticatorAsync(VerifyAuthenticatorRequest request);
    }
}

