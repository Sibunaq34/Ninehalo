using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using System.Security.Claims;
namespace APINineHalo.Endpoints;
public static class LicenciasEndpoints
{
    public static void MapLicenciasEndpoints(this WebApplication app)
    {
        app.MapGet("/api/licencias", async (ILicenciasService service) => {
            var result = await service.GetLicenciasConDetallesAsync();
            return ApiSecurity.Ok(result.Data, result.Message);
        });
        app.MapGet("/api/licencias/usuario/{idUsuario:int}", async (ILicenciasService service, int idUsuario, ClaimsPrincipal user) => {
            if (idUsuario <= 0) return ApiSecurity.Error("Id de usuario inválido.");
            if (user.Actor() != idUsuario) return ApiSecurity.Error("Permiso denegado.", 403);
            var result = await service.GetLicenciaActualAsync(idUsuario);
            return ApiSecurity.Ok(result.Data, result.Message);
        }).RequireAuthorization();
        app.MapPut("/api/licencias/{id:int}", async (int id, ActualizarLicenciaRequest request, ClaimsPrincipal user, LicenciasRepository repository) => {
            if (id <= 0 || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Length > 100
                || request.Precio is null or < 0 or > 99999999.99m || decimal.Round(request.Precio.Value, 2) != request.Precio
                || request.Duracion_Meses is null or < 1 or > 65535 || request.Max_Juegos is null or < 1 or > 65535 || request.Activo is null)
                return ApiSecurity.Error("Datos de licencia inválidos.");
            await repository.UpdateAsync(user.Actor(), id, request);
            return ApiSecurity.Ok(null, "Licencia actualizada correctamente.");
        }).RequireAuthorization();
    }
}
