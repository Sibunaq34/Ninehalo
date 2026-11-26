using APINineHalo.Entities;
using Dapper;
using System.Data;
namespace APINineHalo.Repository;
public class PersonaRepository(IDbConnectionF dbFactory)
{
    public async Task<ProfileDto?> GetProfileAsync(int actor, int idUsuario)
    {
        using var connection = dbFactory.CreateConnection();
        var profile = await connection.QueryFirstOrDefaultAsync<ProfileDto>("sp_consultar_perfil",
            new { p_actor=actor, p_usuario=idUsuario }, commandType: CommandType.StoredProcedure);
        if (profile is null) return null;
        var license = await connection.QueryFirstOrDefaultAsync<ProfileDto>("""
            SELECT l.nombre AS tipo_licencia, c.fecha_vencimiento AS fecha_caducidad
            FROM compra c JOIN licencia l ON l.id_licencia=c.id_licencia
            WHERE c.id_usuario=@idUsuario AND c.estado='ACTIVA' AND c.fecha_vencimiento>NOW()
            ORDER BY c.fecha_compra DESC,c.id_compra DESC LIMIT 1
            """, new { idUsuario });
        profile.Tipo_Licencia=license?.Tipo_Licencia;
        profile.Fecha_Caducidad=license?.Fecha_Caducidad;
        return profile;
    }
    public async Task EditProfileAsync(int actor, int idUsuario, ProfileUpdateRequest request, string? telefono)
    {
        using var connection = dbFactory.CreateConnection();
        await connection.ExecuteAsync("sp_editar_perfil", new {
            p_actor=actor, p_usuario=idUsuario, p_nombre=request.Nombre.Trim(),
            p_apellidos=request.Apellidos.Trim(), p_telefono=telefono, p_correo=request.Correo.Trim(),
            p_id_pais=request.IdPais, p_id_provincia=request.IdProvincia,
            p_id_canton=request.IdCanton, p_id_distrito=request.IdDistrito
        }, commandType: CommandType.StoredProcedure);
    }
}
