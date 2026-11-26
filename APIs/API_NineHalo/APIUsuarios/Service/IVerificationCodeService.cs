using APINineHalo.Entities;

namespace APINineHalo.Services;

public interface IVerificationCodeService
{
    Task<(bool Success, string Message, LoginResponseDto? Data)>
        SendLoginCodeAsync(string preAuthToken);

    Task<(bool Success, string Message, int? UserId)>
        VerifyLoginCodeAsync(VerifyLoginCodeRequest request);

    Task<(bool Success, string Message, LoginResponseDto? Data)>
        SendRegistrationCodeAsync(int idUsuario);

    Task<(bool Success, string Message)>
        VerifyRegistrationCodeAsync(VerifyLoginCodeRequest request);
}
