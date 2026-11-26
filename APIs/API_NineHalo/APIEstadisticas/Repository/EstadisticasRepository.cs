using APINineHalo.Entities;
using Dapper;
using System.Data;
namespace APINineHalo.Repository;
public class EstadisticasRepository(IDbConnectionF dbFactory)
{
    public async Task<IEnumerable<EstadisticaDto>> GetStatisticsAsync(int actor, int idUsuario)
    {
        using var connection = dbFactory.CreateConnection();
        return await connection.QueryAsync<EstadisticaDto>("sp_consultar_estadisticas",
            new { p_actor=actor, p_usuario=idUsuario }, commandType: CommandType.StoredProcedure);
    }
    public async Task UpdateAsync(int actor, ActualizarEstadisticaRequest request)
    {
        using var connection = dbFactory.CreateConnection();
        await connection.ExecuteAsync("sp_actualizar_estadistica", new {
            p_actor=actor, p_compra_juego=request.Id_Compra_Juego, p_plataforma=request.Id_Plataforma, p_baneados=request.Jugadores_Baneados
        }, commandType: CommandType.StoredProcedure);
    }
}
