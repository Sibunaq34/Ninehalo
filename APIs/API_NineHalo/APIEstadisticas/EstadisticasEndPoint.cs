using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using System.Security.Claims;
namespace APINineHalo.Endpoints;
public static class EstadisticasEndpoints
{
    public static void MapEstadisticasEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/estadisticas").RequireAuthorization();
        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal user, IEstadisticasService service) => {
            if (id <= 0) return ApiSecurity.Error("Id de usuario inválido.");
            return ApiSecurity.Ok(await service.GetStatisticsAsync(user.Actor(), id), "Estadísticas obtenidas correctamente.");
        });
        group.MapPut("", async (ActualizarEstadisticaRequest request, ClaimsPrincipal user, EstadisticasRepository repository) => {
            if (request.Id_Compra_Juego == 0 || request.Id_Plataforma <= 0 || request.Jugadores_Baneados is null)
                return ApiSecurity.Error("Datos de estadística inválidos.");
            await repository.UpdateAsync(user.Actor(), request);
            return ApiSecurity.Ok(null, "Estadística actualizada correctamente.");
        });
    }
}
