using APINineHalo.Entities;
using APINineHalo.Services;
using System.Net.Mail;
using System.Security.Claims;
namespace APINineHalo.Endpoints;
public static class PersonaEndpoints
{
    public static void MapPersonaEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/api/perfil").RequireAuthorization();
        group.MapGet("/{id:int}", async (IPersonaService service, int id, ClaimsPrincipal user) => {
            if(id<=0) return ApiSecurity.Error("Id de usuario inválido.");
            var profile=await service.GetProfileAsync(user.Actor(), id);
            return profile is null ? ApiSecurity.Error("Perfil no encontrado.",404) : ApiSecurity.Ok(profile,"Perfil obtenido correctamente.");
        }).WithName("GetProfile");
        group.MapPut("/{id:int}", async (IPersonaService service, int id, ProfileUpdateRequest request, ClaimsPrincipal user) => {
            if(id<=0 || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Length>100
                || string.IsNullOrWhiteSpace(request.Apellidos) || request.Apellidos.Length>100
                || string.IsNullOrWhiteSpace(request.Correo) || request.Correo.Length>150
                || !MailAddress.TryCreate(request.Correo.Trim(),out var email) || email.Address!=request.Correo.Trim())
                return ApiSecurity.Error("Nombre, apellidos o correo electrónico inválidos.");
            // The self-service editor must never edit a different account.
            if(user.Actor()!=id) return ApiSecurity.Error("Permiso denegado.",403);
            var location = new[] { request.IdPais, request.IdProvincia, request.IdCanton, request.IdDistrito };
            if(location.Any(value => value.HasValue) && location.Any(value => !value.HasValue || value.Value <= 0))
                return ApiSecurity.Error("Selecciona país, provincia, cantón y distrito válidos para actualizar tu ubicación.");
            var profile=await service.UpdateProfileAsync(user.Actor(),id,request);
            return profile is null ? ApiSecurity.Error("Perfil no encontrado.",404) : ApiSecurity.Ok(profile,"Perfil actualizado correctamente.");
        }).WithName("UpdateProfile");
    }
}
