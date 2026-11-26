using APINineHalo.Entities;
using APINineHalo.Services;
namespace APINineHalo.Endpoints;

public static class BccrEndpoints
{
    public static void MapBccrEndpoints(this WebApplication app)
    {
        app.MapGet("/api/compras/tipo-cambio", async (BccrService bccr, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { return Results.Ok(new { success = true, data = await bccr.GetAsync(context.RequestAborted) }); }
            catch (CartPurchaseException ex) { return Results.Json(new { success = false, message = ex.Message }, statusCode: ex.Status); }
        }).RequireAuthorization();
    }
}
