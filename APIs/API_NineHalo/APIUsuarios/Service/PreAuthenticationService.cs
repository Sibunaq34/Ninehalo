using Microsoft.AspNetCore.DataProtection;

namespace APINineHalo.Services;

/// <summary>Protege la autorización temporal que permite solicitar el segundo factor.</summary>
public class PreAuthenticationService
{
    public const int ExpirationSeconds = 300;
    private readonly ITimeLimitedDataProtector _protector;

    public PreAuthenticationService(IDataProtectionProvider provider)
    {
        // Mantener el propósito permite leer las autorizaciones ya emitidas.
        _protector = provider.CreateProtector("NineHalo.Login.PreAuthentication.v1")
            .ToTimeLimitedDataProtector();
    }

    public string CreateToken(int idUsuario) => _protector.Protect(
        idUsuario.ToString(), TimeSpan.FromSeconds(ExpirationSeconds));

    public string Unprotect(string token) => _protector.Unprotect(token);
}
