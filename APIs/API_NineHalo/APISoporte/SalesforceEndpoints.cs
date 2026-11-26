using System.Globalization;
using System.Text.Json.Serialization;
using APINineHalo.Repository;

namespace APINineHalo.Endpoints;

public static class SalesforceEndpoints
{
    public static void MapSalesforceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/conversaciones/socio/respuesta", async (
            SalesforceReply request, HttpRequest http, IConfiguration configuration, SoporteRepository repository) =>
        {
            if (!PartnerOutbox.KeyMatches(configuration["Partners:Salesforce:Key"], http.Headers["X-Partner-Key"].ToString()))
                return ApiSecurity.Error("Credencial inválida.", 401);
            if (!ulong.TryParse(request.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0
                || string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 10000
                || string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 80
                || request.State is not ("EN_PROCESO" or "RESUELTA"))
                return ApiSecurity.Error("Respuesta inválida.");
            if (!int.TryParse(configuration["Partners:Salesforce:AgentId"], out var agent) || agent <= 0)
                return ApiSecurity.Error("Agente de Salesforce sin configurar.", 503);

            return await repository.AcceptSalesforceReplyAsync(
                id, agent, request.Content.Trim(), request.State, request.Reference.Trim());
        }).AllowAnonymous();
    }
}

public sealed record SalesforceReply(
    [property: JsonPropertyName("id_conversacion")] string? Id,
    [property: JsonPropertyName("contenido")] string? Content,
    [property: JsonPropertyName("estado")] string? State,
    [property: JsonPropertyName("referencia")] string? Reference);
