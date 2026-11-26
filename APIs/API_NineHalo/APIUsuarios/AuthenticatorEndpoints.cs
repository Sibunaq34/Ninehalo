using APINineHalo.Entities;
using APINineHalo.Services;
using System.Security.Claims;

namespace APINineHalo.Endpoints;

public static class AuthenticatorEndpoints
{
    private static int Actor(ClaimsPrincipal user) => int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) && id > 0
        ? id : throw new AuthenticatorException("Sesión inválida.", 401);
    public static void MapAuthenticatorEndpoints(this WebApplication app)
    {
        var management = app.MapGroup("/api/perfil/autenticador").RequireAuthorization().RequireRateLimiting("authenticator");
        management.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            return await next(context);
        });
        management.MapGet("", async (ClaimsPrincipal user, AuthenticatorService service) =>
            Results.Ok(new { success = true, data = await service.StatusAsync(Actor(user)) }));
        management.MapPost("/vincular", async (ClaimsPrincipal user, AuthenticatorPasswordRequest request, AuthenticatorService service) =>
            Results.Ok(new { success = true, data = await service.BeginAsync(Actor(user), request.Password) }));
        management.MapPost("/confirmar", async (ClaimsPrincipal user, AuthenticatorConfirmationRequest request, AuthenticatorService service) =>
            CheckResponse(await service.ConfirmAsync(Actor(user), request)));
        management.MapPost("/desactivar", async (ClaimsPrincipal user, AuthenticatorConfirmationRequest request, AuthenticatorService service) =>
            CheckResponse(await service.DisableAsync(Actor(user), request)));
        var login = app.MapGroup("/api/login/autenticador").RequireRateLimiting("authenticator");
        login.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        login.MapPost("/estado", async (VerifyAuthenticatorRequest request, AuthenticatorService service) =>
            Results.Ok(new { success = true, data = await service.LoginStatusAsync(request.Pre_Auth_Token) }));
        login.MapPost("/verificar", async (VerifyAuthenticatorRequest request, ILoginService service) =>
        {
            var (success, message, token) = await service.VerifyAuthenticatorAsync(request);
            return success ? Results.Ok(new { success, message, data = new { access_token = token, token_type = "Bearer", expires_in = 900 } })
                : Results.Json(new { success, message, data = (object?)null }, statusCode: 400);
        });
    }
    private static IResult CheckResponse(TotpCheck result) => Results.Json(new { success = result.Success, message = result.Message, data = (object?)null }, statusCode: result.Status);
}
