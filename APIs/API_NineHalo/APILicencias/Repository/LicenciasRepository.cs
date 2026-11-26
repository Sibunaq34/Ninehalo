using APINineHalo.Entities;
using Dapper;
using System.Data;

namespace APINineHalo.Repository;
public class LicenciasRepository(IDbConnectionF dbFactory)
{
    public async Task<IEnumerable<LicenciaDto>> GetLicenciasAsync()
    {
        using var connection = dbFactory.CreateConnection();
        var licenses = (await connection.QueryAsync<LicenciaDto>("sp_consultar_licencias", commandType: CommandType.StoredProcedure)).ToList();
        foreach (var license in licenses)
        {
            var details = await connection.QueryAsync<LicenciaDetalleDto>("sp_consultar_detalles_licencia",
                new { p_licencia = license.Id_licencia }, commandType: CommandType.StoredProcedure);
            license.Detalles = details.Select(d => d.Descripcion).ToList();
        }
        return licenses;
    }

    public async Task<LicenciaActualDto?> GetLicenciaActualAsync(int idUsuario)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<LicenciaActualDto>("""
            SELECT c.id_compra,c.id_licencia,l.nombre,c.precio_pagado AS precio,
                   c.fecha_compra,c.fecha_vencimiento,c.max_juegos_compra AS max_juegos,
                   c.duracion_meses_compra AS duracion_meses
            FROM compra c JOIN licencia l ON l.id_licencia=c.id_licencia
            WHERE c.id_usuario=@idUsuario AND c.estado='ACTIVA' AND c.fecha_vencimiento>NOW()
            ORDER BY c.fecha_compra DESC,c.id_compra DESC LIMIT 1
            """, new { idUsuario });
    }

    public async Task UpdateAsync(int actor, int id, ActualizarLicenciaRequest request)
    {
        using var connection = dbFactory.CreateConnection();
        await connection.ExecuteAsync("sp_actualizar_licencia", new {
            p_actor=actor, p_licencia=id, p_nombre=request.Nombre.Trim(), p_descripcion=request.Descripcion,
            p_precio=request.Precio, p_meses=request.Duracion_Meses, p_max_juegos=request.Max_Juegos, p_activo=request.Activo
        }, commandType: CommandType.StoredProcedure);
    }
}
