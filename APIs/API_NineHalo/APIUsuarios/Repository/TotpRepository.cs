using APINineHalo.Entities;
using Dapper;
namespace APINineHalo.Repository;

public class TotpRecord
{
    public int Id_Usuario { get; set; }
    public string Id_Vinculacion { get; set; } = "";
    public string Secreto_Protegido { get; set; } = "";
    public bool Habilitado { get; set; }
    public DateTime? Pendiente_Expira_En { get; set; }
    public long? Ultimo_Paso { get; set; }
    public int Fallos { get; set; }
    public DateTime? Bloqueado_Hasta { get; set; }
}
public record TotpDecision<T>(T Result, TotpRecord? State, string? Audit = null);

public class TotpRepository(IDbConnectionF factory)
{
    public async Task<TotpRecord?> ReadAsync(int user)
    {
        using var connection = factory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<TotpRecord>("SELECT * FROM usuario_totp WHERE id_usuario=@user", new { user });
    }

    public async Task<T> ApplyAsync<T>(int user, Func<TotpRecord?, TotpDecision<T>> decide, string? expectedPasswordHash = null)
    {
        using var connection = factory.CreateConnection(); connection.Open();
        using var transaction = connection.BeginTransaction();
        // Lock the account first even when no TOTP row exists. This serializes
        // enrollment, confirmation, verification and removal for that account.
        var account = await connection.QuerySingleOrDefaultAsync<UserAuthEntity>(
            "SELECT estado,correo_verificado,password_hash FROM usuario WHERE id_usuario=@user FOR UPDATE", new { user }, transaction);
        if (account is null || account.Estado != "ACTIVO" || !account.Correo_Verificado)
            throw new AuthenticatorException("La cuenta no está habilitada para esta operación.", 403);
        if (expectedPasswordHash is not null && expectedPasswordHash != account.Password_Hash)
            throw new AuthenticatorException("La contraseña cambió. Confírmala nuevamente.", 409);
        var current = await connection.QuerySingleOrDefaultAsync<TotpRecord>(
            "SELECT * FROM usuario_totp WHERE id_usuario=@user FOR UPDATE", new { user }, transaction);
        var decision = decide(current);
        if (decision.State is { } state)
        {
            await connection.ExecuteAsync("""
                INSERT INTO usuario_totp(id_usuario,id_vinculacion,secreto_protegido,habilitado,pendiente_expira_en,ultimo_paso,fallos,bloqueado_hasta)
                VALUES(@Id_Usuario,@Id_Vinculacion,@Secreto_Protegido,@Habilitado,@Pendiente_Expira_En,@Ultimo_Paso,@Fallos,@Bloqueado_Hasta)
                ON DUPLICATE KEY UPDATE id_vinculacion=@Id_Vinculacion,secreto_protegido=@Secreto_Protegido,habilitado=@Habilitado,
                  pendiente_expira_en=@Pendiente_Expira_En,ultimo_paso=@Ultimo_Paso,fallos=@Fallos,bloqueado_hasta=@Bloqueado_Hasta
                """, state, transaction);
        }
        else if (current is not null)
            await connection.ExecuteAsync("DELETE FROM usuario_totp WHERE id_usuario=@user", new { user }, transaction);
        if (decision.Audit is not null)
            await connection.ExecuteAsync("sp_registrar_bitacora", new { p_actor = user, p_accion = decision.Audit, p_entidad = "usuario_totp" }, transaction, commandType: System.Data.CommandType.StoredProcedure);
        transaction.Commit();
        return decision.Result;
    }
}
