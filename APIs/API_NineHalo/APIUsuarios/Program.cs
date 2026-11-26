using APINineHalo.Entities;
using System.Threading.RateLimiting;
using System.Security.Claims;

using APINineHalo.Endpoints;
using APINineHalo.Repository;
using APINineHalo.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("NineHaloFrontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


builder.Services.AddSingleton<IDbConnectionF,DbConnectionFactory>();
builder.Services.AddScoped<UsuarioRepository>();
builder.Services.AddScoped<LoginRepository>();
builder.Services.AddScoped<IUsuarioService,UsuarioService>();
builder.Services.AddScoped<ILoginService,LoginService>();
builder.Services.AddScoped<IVerificationCodeService, VerificationCodeService>();
builder.Services.AddSingleton<PreAuthenticationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TotpRepository>();
builder.Services.AddScoped<AuthenticatorService>();
builder.Services.AddScoped<PasswordRecoveryService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("authenticator", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(new { success = false, message = "Demasiadas solicitudes. Espera un minuto e inténtalo de nuevo." }, token);
    };
});
builder.Services.AddScoped<IEmailService,EmailService>();
builder.Services.AddSingleton<JwtTokenService>();

string jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("No se configuró Jwt:SecretKey.");

byte[] jwtKey;

try
{
    jwtKey = Convert.FromBase64String(jwtSecret);
}
catch (FormatException ex)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey debe contener una clave válida en Base64.",
        ex);
}

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "La clave JWT debe contener al menos 32 bytes.");
}

string issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "No se configuró Jwt:Issuer.");

string audience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "No se configuró Jwt:Audience.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = true;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                // Validar quién emitió el token
                ValidateIssuer = true,
                ValidIssuer = issuer,

                // Validar para qué API fue emitido
                ValidateAudience = true,
                ValidAudience = audience,

                // Verificar firma criptográfica
                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(jwtKey),

                RequireSignedTokens = true,

                // Verificar fecha de expiración
                ValidateLifetime = true,
                RequireExpirationTime = true,

                // Tolerancia de reloj
                ClockSkew = TimeSpan.FromSeconds(30)
            };
    });


builder.Services.AddAuthorization();
builder.Services.AddDataProtection()
    .SetApplicationName("NineHalo.APIUsuarios");
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (AuthenticatorException ex)
    {
        context.Response.StatusCode = ex.Status;
        await context.Response.WriteAsJsonAsync(new { success = false, message = ex.Message, data = (object?)null });
    }
});


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseCors("NineHaloFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapAuthenticatorEndpoints();
app.MapPasswordRecoveryEndpoints();
app.MapUsuarioEndpoints();
app.MapUbicacionEndpoints();


app.Run();

