using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using System.Security.Claims;
namespace APINineHalo.Endpoints;
public static class SoporteEndpoints
{
    private static bool ValidPage(int page, int size) => page >= 1 && page <= int.MaxValue / 100 && size is >= 1 and <= 100;
    private static IResult Created(string location, object data) => Results.Created(location, new { success=true, message="Registro creado correctamente.", data });
    public static void MapSoporteEndpoints(this WebApplication app)
    {
        var chats = app.MapGroup("/api/conversaciones").RequireAuthorization();
        chats.MapGet("", async (ClaimsPrincipal user, SoporteRepository repository) => ApiSecurity.Ok(await repository.GetConversationsAsync(user.Actor())));
        chats.MapGet("/soporte", async (ClaimsPrincipal user, SoporteRepository repository, int? pagina, int? tamano) => {
            int page=pagina ?? 1, size=tamano ?? 10;
            if (!ValidPage(page, size)) return ApiSecurity.Error("Paginación inválida.");
            return ApiSecurity.Ok(await repository.GetInboxAsync(user.Actor(), page, size));
        });
        chats.MapPost("", async (CrearConversacionRequest request, ClaimsPrincipal user, SoporteRepository repository) => {
            if (string.IsNullOrWhiteSpace(request.Asunto) || request.Asunto.Length > 180 || request.Id_Compra == 0)
                return ApiSecurity.Error("Asunto o compra inválidos.");
            var id = await repository.CreateAsync(user.Actor(), request);
            return Created($"/api/conversaciones/{id}/mensajes", new { id_conversacion=id });
        });
        chats.MapGet("/{id}/mensajes", async (ulong id, ClaimsPrincipal user, SoporteRepository repository, int? pagina, int? tamano) => {
            int page=pagina ?? 1, size=tamano ?? 10;
            if (id == 0 || !ValidPage(page,size)) return ApiSecurity.Error("Conversación o paginación inválida.");
            return ApiSecurity.Ok(await repository.GetMessagesAsync(user.Actor(), id, page, size));
        });
        chats.MapPost("/{id}/mensajes", async (ulong id, MensajeRequest request, ClaimsPrincipal user, SoporteRepository repository) => {
            if (id == 0 || string.IsNullOrWhiteSpace(request.Contenido) || request.Contenido.Length > 10000)
                return ApiSecurity.Error("Mensaje inválido: se requieren entre 1 y 10000 caracteres.");
            var message = await repository.SendAsync(user.Actor(), id, request.Contenido);
            return Created($"/api/conversaciones/{id}/mensajes", new { id_mensaje=message });
        });
        chats.MapPut("/{id}/agente", async (ulong id, AgenteRequest request, ClaimsPrincipal user, SoporteRepository repository) => {
            if (id == 0 || request.Id_Agente <= 0) return ApiSecurity.Error("Conversación o agente inválidos.");
            await repository.AssignAsync(user.Actor(), id, request.Id_Agente);
            return ApiSecurity.Ok(null);
        });
        chats.MapPut("/{id}/estado", async (ulong id, EstadoRequest request, ClaimsPrincipal user, SoporteRepository repository) => {
            if (id == 0 || request.Estado is not ("ABIERTA" or "EN_PROCESO" or "RESUELTA" or "CERRADA"))
                return ApiSecurity.Error("Estado inválido.");
            await repository.ChangeStateAsync(user.Actor(), id, request.Estado);
            return ApiSecurity.Ok(null);
        });
        var tickets = app.MapGroup("/api/tickets").RequireAuthorization();
        tickets.MapGet("/{idUsuario:int}", async (int idUsuario, int? pagina, ClaimsPrincipal user, ISoporteService service) => {
            if (idUsuario <= 0 || !ValidPage(pagina ?? 1, 10)) return ApiSecurity.Error("Usuario o página inválidos.");
            if (idUsuario != user.Actor()) return ApiSecurity.Error("Permiso denegado.", 403);
            return ApiSecurity.Ok(await service.GetTicketsAsync(user.Actor(), pagina ?? 1));
        });
        tickets.MapPost("", async (CreateTicketRequest request, ClaimsPrincipal user, ISoporteService service) => {
            if (request.Id_Usuario <= 0 || request.Id_Compra == 0 || string.IsNullOrWhiteSpace(request.Titulo)
                || request.Titulo.Length > 180 || string.IsNullOrWhiteSpace(request.Detalle) || request.Detalle.Length > 10000)
                return ApiSecurity.Error("Título, detalle o compra inválidos.");
            if (request.Id_Usuario != user.Actor()) return ApiSecurity.Error("Permiso denegado.", 403);
            var id = await service.CreateTicketAsync(user.Actor(), request);
            return Created($"/api/conversaciones/{id}/mensajes", new { id_soporte=id, id_conversacion=id });
        });
    }
}
