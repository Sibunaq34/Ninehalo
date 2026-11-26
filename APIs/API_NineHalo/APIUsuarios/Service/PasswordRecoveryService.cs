using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APINineHalo.Entities;
using APINineHalo.Repository;
using Dapper;
using Microsoft.AspNetCore.DataProtection;

namespace APINineHalo.Services;

public class RecoveryAccount
{
    public int Id_Usuario { get; set; }
    public string Correo { get; set; } = "";
    public string Password_Hash { get; set; } = "";
    public string Estado { get; set; } = "";
    public bool Correo_Verificado { get; set; }
    public string? Identificacion { get; set; }
    public string? Telefono { get; set; }
}
public class RecoveryState
{
    public int Fallos { get; set; }
    public DateTime? Bloqueado_Hasta { get; set; }
    public string? Nonce_Hash { get; set; }
}
public record RecoveryGrant(string token, int expires_in = 600);
public record RecoveryProof(int User, string PasswordHash, string Nonce);

/// <summary>Recuperación sin sesión: pruebas de identidad y autorización limitada de un solo uso.</summary>
public class PasswordRecoveryService(IDbConnectionF factory, AuthenticatorService authenticator,
    IDataProtectionProvider protection, TimeProvider clock)
{
    private const string Invalid = "No se pudo verificar la identidad. Revisa los datos o espera quince minutos si agotaste los intentos.";
    private ITimeLimitedDataProtector Protector => protection.CreateProtector("NineHalo.PasswordRecovery.v1").ToTimeLimitedDataProtector();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Eligible(RecoveryAccount? account) => account is { Estado: "ACTIVO", Correo_Verificado: true };
    private static string Digits(string? value) => new((value ?? "").Where(c => c is >= '0' and <= '9').ToArray());
    private static string Identification(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    public static int CorrectAnswers(RecoveryAccount account, RecoveryQuestionsRequest request)
    {
        int correct = 0;
        string id = Identification(account.Identificacion), phone = Digits(account.Telefono);
        if (id.Length > 0 && id == Identification(request.Identificacion)) correct++;
        if (phone.Length > 0 && phone == Digits(request.Telefono)) correct++;
        return correct;
    }
    private async Task<RecoveryAccount?> FindAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 150) return null;
        using var db = factory.CreateConnection();
        return await db.QuerySingleOrDefaultAsync<RecoveryAccount>("""
            SELECT u.id_usuario,u.correo,u.password_hash,u.estado,u.correo_verificado,p.identificacion,p.telefono
            FROM usuario u LEFT JOIN persona p ON p.id_usuario=u.id_usuario WHERE u.correo=@email
            """, new { email = email.Trim() });
    }
    private RecoveryGrant Grant(RecoveryAccount account, out string nonceHash)
    {
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        nonceHash = Hash(nonce);
        return new(Protector.Protect(JsonSerializer.Serialize(new RecoveryProof(account.Id_Usuario, account.Password_Hash, nonce)), TimeSpan.FromMinutes(10)));
    }
    public async Task<RecoveryGrant> QuestionsAsync(RecoveryQuestionsRequest request)
    {
        if (new[] { request.Identificacion, request.Telefono }.Any(v => v?.Length > 100))
            throw new AuthenticatorException(Invalid);
        var found = await FindAsync(request.Correo);
        if (!Eligible(found)) throw new AuthenticatorException(Invalid);
        using var db = factory.CreateConnection(); db.Open(); using var tx = db.BeginTransaction();
        var account = await db.QuerySingleAsync<RecoveryAccount>("""
            SELECT u.id_usuario,u.correo,u.password_hash,u.estado,u.correo_verificado,p.identificacion,p.telefono
            FROM usuario u LEFT JOIN persona p ON p.id_usuario=u.id_usuario WHERE u.id_usuario=@id FOR UPDATE
            """, new { id = found!.Id_Usuario }, tx);
        if (!Eligible(account)) throw new AuthenticatorException(Invalid);
        await db.ExecuteAsync("INSERT IGNORE INTO usuario_recuperacion(id_usuario) VALUES(@id)", new { id = account.Id_Usuario }, tx);
        var state = await db.QuerySingleAsync<RecoveryState>("SELECT * FROM usuario_recuperacion WHERE id_usuario=@id FOR UPDATE", new { id = account.Id_Usuario }, tx);
        if (state.Bloqueado_Hasta > clock.GetUtcNow().UtcDateTime) throw new AuthenticatorException(Invalid);
        int failures = state.Bloqueado_Hasta.HasValue ? 0 : state.Fallos;
        if (CorrectAnswers(account, request) < 2)
        {
            failures++;
            await db.ExecuteAsync("UPDATE usuario_recuperacion SET fallos=@failures,bloqueado_hasta=@until WHERE id_usuario=@id",
                new { failures, until = failures >= 5 ? clock.GetUtcNow().UtcDateTime.AddMinutes(15) : (DateTime?)null, id = account.Id_Usuario }, tx);
            tx.Commit(); // Los intentos fallidos deben persistir aunque la respuesta sea un rechazo.
            throw new AuthenticatorException(Invalid);
        }
        var grant = Grant(account, out var nonceHash);
        await db.ExecuteAsync("UPDATE usuario_recuperacion SET nonce_hash=@nonceHash,fallos=0,bloqueado_hasta=NULL WHERE id_usuario=@id", new { nonceHash, id = account.Id_Usuario }, tx);
        tx.Commit(); return grant;
    }
    public async Task<RecoveryGrant> AuthenticatorAsync(RecoveryAuthenticatorRequest request)
    {
        var account = await FindAsync(request.Correo);
        if (!Eligible(account)) throw new AuthenticatorException(Invalid);
        var result = await authenticator.VerifyRecoveryAsync(account!.Id_Usuario, request.Codigo);
        if (!result.Success) throw new AuthenticatorException(Invalid, result.Status == 429 ? 429 : 400);
        using var db = factory.CreateConnection(); db.Open(); using var tx = db.BeginTransaction();
        var current = await db.QuerySingleAsync<RecoveryAccount>("SELECT * FROM usuario WHERE id_usuario=@id FOR UPDATE", new { id = account.Id_Usuario }, tx);
        if (!Eligible(current) || current.Password_Hash != account.Password_Hash) throw new AuthenticatorException(Invalid);
        var grant = Grant(current, out var nonceHash);
        await db.ExecuteAsync("""
            INSERT INTO usuario_recuperacion(id_usuario,nonce_hash) VALUES(@id,@nonceHash)
            ON DUPLICATE KEY UPDATE nonce_hash=@nonceHash
            """, new { id = current.Id_Usuario, nonceHash }, tx);
        tx.Commit(); return grant;
    }
    public async Task ResetAsync(RecoveryPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password_Nueva) || request.Password_Nueva.Length < 8 || Encoding.UTF8.GetByteCount(request.Password_Nueva) > 72 || request.Password_Nueva != request.Confirmacion)
            throw new AuthenticatorException("Usa una contraseña de al menos 8 caracteres y máximo 72 bytes. La confirmación debe coincidir.");
        RecoveryProof proof;
        try
        {
            if (string.IsNullOrEmpty(request.Token) || request.Token.Length > 4096) throw new CryptographicException();
            proof = JsonSerializer.Deserialize<RecoveryProof>(Protector.Unprotect(request.Token)) ?? throw new CryptographicException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException)
        { throw new AuthenticatorException("La autorización expiró o ya fue utilizada. Inicia la recuperación nuevamente.", 401); }
        if (BCrypt.Net.BCrypt.Verify(request.Password_Nueva, proof.PasswordHash)) throw new AuthenticatorException("La nueva contraseña debe ser diferente a la anterior.");
        string newHash = BCrypt.Net.BCrypt.HashPassword(request.Password_Nueva);
        using var db = factory.CreateConnection(); db.Open(); using var tx = db.BeginTransaction();
        var account = await db.QuerySingleOrDefaultAsync<RecoveryAccount>("SELECT * FROM usuario WHERE id_usuario=@id FOR UPDATE", new { id = proof.User }, tx);
        var state = await db.QuerySingleOrDefaultAsync<RecoveryState>("SELECT * FROM usuario_recuperacion WHERE id_usuario=@id FOR UPDATE", new { id = proof.User }, tx);
        if (!Eligible(account) || account!.Password_Hash != proof.PasswordHash || state?.Nonce_Hash != Hash(proof.Nonce))
            throw new AuthenticatorException("La autorización expiró o ya fue utilizada. Inicia la recuperación nuevamente.", 401);
        await db.ExecuteAsync("UPDATE usuario SET password_hash=@newHash,intentos_fallidos=0 WHERE id_usuario=@id; UPDATE usuario_recuperacion SET nonce_hash=NULL WHERE id_usuario=@id", new { newHash, id = proof.User }, tx);
        await db.ExecuteAsync("sp_registrar_bitacora", new { p_actor = proof.User, p_accion = "PASSWORD_RESTABLECIDA", p_entidad = "usuario" }, tx, commandType: System.Data.CommandType.StoredProcedure);
        tx.Commit();
    }
}
