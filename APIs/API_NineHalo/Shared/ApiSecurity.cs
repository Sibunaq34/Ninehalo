using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;

namespace APINineHalo;

public static class ApiSecurity
{
    public static void AddNineHaloSecurity(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            var secret = builder.Configuration["Jwt:SecretKey"]
                ?? throw new InvalidOperationException("Configure Jwt:SecretKey con la misma clave de APIUsuarios.");
            var key = Convert.FromBase64String(secret);
            if (key.Length < 32) throw new InvalidOperationException("La clave JWT debe contener al menos 32 bytes.");
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Falta Jwt:Issuer."),
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Falta Jwt:Audience."),
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });
        builder.Services.AddAuthorization();
    }

    public static int Actor(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id) && id > 0
            ? id : throw new UnauthorizedAccessException();

    public static IResult Error(string message, int status = 400) =>
        Results.Json(new { success = false, message, data = (object?)null }, statusCode: status);

    public static IResult Ok(object? data, string message = "Operación realizada correctamente.") =>
        Results.Ok(new { success = true, message, data });

    public static void UseNineHaloErrors(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (UnauthorizedAccessException) { await Error("Identidad de usuario inválida.", 401).ExecuteAsync(context); }
            catch (MySqlException ex) when (ex.Number == 1644)
            {
                var message = ex.Message;
                var status = message.Contains("no existe", StringComparison.OrdinalIgnoreCase) ? 404
                    : message.Contains("Permiso", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("acceso", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("autorizado", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("participa", StringComparison.OrdinalIgnoreCase)
                      || message.Contains("pertenece", StringComparison.OrdinalIgnoreCase) ? 403 : 400;
                await Error(message, status).ExecuteAsync(context);
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            { await Error("El registro ya existe.", 409).ExecuteAsync(context); }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Error en {Path}", context.Request.Path);
                await Error("No fue posible completar la operación.", 500).ExecuteAsync(context);
            }
        });
    }
}
