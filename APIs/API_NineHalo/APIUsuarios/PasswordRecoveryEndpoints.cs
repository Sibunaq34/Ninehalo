using APINineHalo.Entities;
using APINineHalo.Services;
namespace APINineHalo.Endpoints;

public static class PasswordRecoveryEndpoints
{
    public static void MapPasswordRecoveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/login/recuperacion").AllowAnonymous().RequireRateLimiting("authenticator");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });
        group.MapPost("/preguntas", async (RecoveryQuestionsRequest request, PasswordRecoveryService service) =>
            Results.Ok(new { success = true, data = await service.QuestionsAsync(request) }));
        group.MapPost("/autenticador", async (RecoveryAuthenticatorRequest request, PasswordRecoveryService service) =>
            Results.Ok(new { success = true, data = await service.AuthenticatorAsync(request) }));
        group.MapPost("/restablecer", async (RecoveryPasswordRequest request, PasswordRecoveryService service) =>
        {
            await service.ResetAsync(request);
            return Results.Ok(new { success = true, message = "Contraseña restablecida. Ya puedes iniciar sesión." });
        });
    }
}
