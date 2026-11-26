using APINineHalo.Entities;
using APINineHalo.Repository;
using Microsoft.AspNetCore.DataProtection;
using QRCoder;
using System.Security.Cryptography;

namespace APINineHalo.Services;

public class AuthenticatorService(
    UsuarioRepository users, TotpRepository repository, PreAuthenticationService preAuth,
    IDataProtectionProvider protection, TimeProvider clock)
{
    private IDataProtector SecretProtector(int user) => protection.CreateProtector("NineHalo.Totp.Secret.v1", user.ToString());

    private async Task<UserAuthEntity> AccountAsync(int user)
    {
        var account = await users.GetUserByIdAsync(user);
        if (account is null || account.Estado != "ACTIVO" || !account.Correo_Verificado)
            throw new AuthenticatorException("La cuenta no está habilitada para esta operación.", 403);
        return account;
    }

    private async Task<UserAuthEntity> PasswordAsync(int user, string? password)
    {
        var account = await AccountAsync(user);
        bool valid = false;
        if (!string.IsNullOrEmpty(password) && password.Length <= 1024)
        {
            try { valid = BCrypt.Net.BCrypt.Verify(password, account.Password_Hash); }
            catch { valid = false; }
        }
        if (!valid) throw new AuthenticatorException("La contraseña actual no es correcta.");
        return account;
    }

    private int PreAuthenticatedUser(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            throw new AuthenticatorException("La autorización temporal expiró o es inválida. Inicia sesión nuevamente.", 401);
        try
        {
            if (int.TryParse(preAuth.Unprotect(token), out int user) && user > 0) return user;
        }
        catch (CryptographicException) { }
        throw new AuthenticatorException("La autorización temporal expiró o es inválida. Inicia sesión nuevamente.", 401);
    }

    public async Task<AuthenticatorStatus> StatusAsync(int user)
    {
        await AccountAsync(user);
        var state = await repository.ReadAsync(user);
        return new(state?.Habilitado == true, state?.Habilitado == true ? state.Id_Vinculacion : null);
    }

    public Task<AuthenticatorStatus> LoginStatusAsync(string token) => StatusAsync(PreAuthenticatedUser(token));

    public async Task<AuthenticatorSetup> BeginAsync(int user, string password)
    {
        var account = await PasswordAsync(user, password);
        byte[] secret = RandomNumberGenerator.GetBytes(20);
        try
        {
            string manual = TotpAlgorithm.Base32(secret);
            string uri = $"otpauth://totp/NineHalo:{Uri.EscapeDataString(account.Correo)}?secret={manual}&issuer=NineHalo&algorithm=SHA1&digits=6&period=30";
            string image = Convert.ToBase64String(PngByteQRCodeHelper.GetQRCode(uri, QRCodeGenerator.ECCLevel.Q, 8));
            string id = Guid.NewGuid().ToString();
            string encrypted = SecretProtector(user).Protect(Convert.ToBase64String(secret));
            return await repository.ApplyAsync<AuthenticatorSetup>(user, state =>
            {
                if (state?.Habilitado == true)
                    throw new AuthenticatorException("Google Authenticator ya está activado. Desactívalo antes de vincular otra app.", 409);
                var pending = new TotpRecord { Id_Usuario = user, Id_Vinculacion = id, Secreto_Protegido = encrypted,
                    Pendiente_Expira_En = clock.GetUtcNow().UtcDateTime.AddMinutes(10) };
                return new(new(id, manual, image, 600), pending, "TOTP_VINCULACION_INICIADA");
            }, account.Password_Hash);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private TotpCheck Check(TotpRecord state, int user, string? code)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        if (state.Bloqueado_Hasta > now)
            return new(false, "Demasiados intentos. Espera cinco minutos antes de volver a intentar.", Status: 429);
        if (state.Bloqueado_Hasta.HasValue) { state.Fallos = 0; state.Bloqueado_Hasta = null; }
        byte[] secret = Convert.FromBase64String(SecretProtector(user).Unprotect(state.Secreto_Protegido));
        long? step;
        try { step = TotpAlgorithm.Match(secret, code, clock.GetUtcNow(), state.Ultimo_Paso); }
        finally { CryptographicOperations.ZeroMemory(secret); }
        if (!step.HasValue)
        {
            state.Fallos++;
            if (state.Fallos >= 5) state.Bloqueado_Hasta = now.AddMinutes(5);
            return new(false, "Código incorrecto, vencido o ya utilizado. Usa un código nuevo de la app.");
        }
        state.Ultimo_Paso = step; state.Fallos = 0; state.Bloqueado_Hasta = null;
        return new(true, "Código verificado.", user, 200);
    }

    public Task<TotpCheck> ConfirmAsync(int user, AuthenticatorConfirmationRequest request) =>
        repository.ApplyAsync<TotpCheck>(user, state =>
        {
            if (state is null || state.Id_Vinculacion != request.Id_Vinculacion || state.Habilitado ||
                state.Pendiente_Expira_En is null || state.Pendiente_Expira_En <= clock.GetUtcNow().UtcDateTime)
                throw new AuthenticatorException("La vinculación expiró o ya cambió. Comienza de nuevo.", 409);
            var result = Check(state, user, request.Codigo);
            if (!result.Success) return new(result, state);
            state.Habilitado = true; state.Pendiente_Expira_En = null;
            return new(new(true, "Google Authenticator activado.", user, 200), state, "TOTP_ACTIVADO");
        });

    public async Task<TotpCheck> VerifyLoginAsync(VerifyAuthenticatorRequest request)
    {
        int user = PreAuthenticatedUser(request.Pre_Auth_Token);
        return await repository.ApplyAsync<TotpCheck>(user, state =>
        {
            if (state?.Habilitado != true) throw new AuthenticatorException("Google Authenticator no está activado en esta cuenta.");
            var result = Check(state, user, request.Codigo);
            return new(result, state, result.Success ? "TOTP_LOGIN_VERIFICADO" : null);
        });
    }

    // Recuperación demuestra posesión de la app sin exigir la contraseña olvidada.
    // Comparte el bloqueo y la protección contra reutilización con el inicio de sesión.
    public Task<TotpCheck> VerifyRecoveryAsync(int user, string code) =>
        repository.ApplyAsync<TotpCheck>(user, state =>
        {
            if (state?.Habilitado != true) throw new AuthenticatorException("No se pudo verificar la identidad.");
            var result = Check(state, user, code);
            return new(result, state, result.Success ? "TOTP_RECUPERACION_VERIFICADA" : null);
        });

    public async Task<TotpCheck> DisableAsync(int user, AuthenticatorConfirmationRequest request)
    {
        var account = await PasswordAsync(user, request.Password);
        return await repository.ApplyAsync<TotpCheck>(user, state =>
        {
            if (state?.Habilitado != true || state.Id_Vinculacion != request.Id_Vinculacion)
                throw new AuthenticatorException("La configuración cambió. Actualiza la página antes de continuar.", 409);
            var result = Check(state, user, request.Codigo);
            if (!result.Success) return new(result, state);
            return new(new(true, "Google Authenticator desactivado. El acceso por correo sigue disponible.", user, 200), null, "TOTP_DESACTIVADO");
        }, account.Password_Hash);
    }
}
