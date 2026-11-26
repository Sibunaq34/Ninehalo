
using APINineHalo.Entities;
using APINineHalo.Services;
using System.Security.Claims;

namespace APINineHalo.Endpoints
{
    public static class UsuarioEndpoints
    {
        public static void MapUsuarioEndpoints(
            this WebApplication app)
        {
            // =====================================
            // REGISTRAR CUENTA
            // =====================================

            app.MapPost("/api/registro", async (
                IUsuarioService usuarioService,
                ILoginService loginService,
                RegisterRequest request) =>
            {
                var (success, message, idUsuario) =
                    await usuarioService.RegisterAsync(request);

                if (!success)
                {
                    if (message.Contains(
                        "ya registrados",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Conflict(new
                        {
                            success = false,
                            message,
                            data = (object?)null
                        });
                    }

                    return Results.BadRequest(new
                    {
                        success = false,
                        message,
                        data = (object?)null
                    });
                }

                if (!idUsuario.HasValue)
                {
                    return Results.Problem(
                        "No se recibió el identificador del usuario.");
                }

                var (codeSent, codeMessage, verification) =
                    await loginService.SendRegistrationCodeAsync(
                        idUsuario.Value
                    );

                // La cuenta ya existe aunque el correo falle.
                return Results.Json(new
                {
                    success = true,
                    message = codeSent
                        ? "Usuario registrado. " + codeMessage
                        : "Usuario registrado, pero no se pudo " +
                          "enviar el código de verificación.",
                    data = new
                    {
                        id_usuario = idUsuario.Value,
                        correo_verificado = false,
                        codigo_enviado = codeSent,
                        id_verificacion = verification?.IdVerificacion,
                        expires_in = verification?.ExpiresIn
                    }
                }, statusCode: StatusCodes.Status201Created);
            })
            .WithName("RegisterUser");

            // =====================================
            // VERIFICAR CORREO DEL REGISTRO
            // =====================================

            app.MapPost("/api/registro/verificar-codigo",
                async (
                    ILoginService service,
                    VerifyLoginCodeRequest request) =>
                {
                    var (success, message) =
                        await service.VerifyRegistrationCodeAsync(
                            request
                        );

                    if (!success)
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message,
                            data = (object?)null
                        });
                    }

                    return Results.Ok(new
                    {
                        success = true,
                        message,
                        data = (object?)null
                    });
                })
                .WithName("VerifyRegistrationCode");

            // =====================================
            // INICIAR SESION
            // =====================================

            app.MapPost("/api/login", async (
                ILoginService service,
                LoginRequest request) =>
            {
                if (request == null)
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "Datos inválidos.",
                        data = (object?)null
                    });
                }

                var (success, message, data) =
                    await service.LoginAsync(
                        request.Correo,
                        request.Password
                    );

                if (!success)
                {
                    return Results.Json(new
                    {
                        success = false,
                        message,
                        data = (object?)null
                    }, statusCode:
                        StatusCodes.Status401Unauthorized);
                }

                return Results.Ok(new
                {
                    success = true,
                    message,
                    data
                });
            })
            .WithName("Login");

            // =====================================
            // VERIFICAR CODIGO DEL LOGIN
            // =====================================

            app.MapPost("/api/login/verificar-codigo",
                async (
                    ILoginService service,
                    VerifyLoginCodeRequest request) =>
                {
                    var (success, message, token) =
                        await service.VerifyCodeAsync(request);

                    if (!success)
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message,
                            data = (object?)null
                        });
                    }

                    return Results.Ok(new
                    {
                        success = true,
                        message,
                        data = new
                        {
                            access_token = token,
                            token_type = "Bearer",
                            expires_in = 900
                        }
                    });
                })
                .WithName("VerifyLoginCode");

            // =====================================
            // CAMBIAR CONTRASEÑA
            // =====================================

            app.MapPut("/api/perfil/{id:int}/password",
                async (
                    IUsuarioService service,
                    ClaimsPrincipal user,
                    int id,
                    ChangePasswordRequest request) =>
                {
                    string? userIdClaim =
                        user.FindFirst(
                            ClaimTypes.NameIdentifier
                        )?.Value;

                    if (!int.TryParse(
                        userIdClaim,
                        out int authenticatedUserId))
                    {
                        return Results.Unauthorized();
                    }

                    if (id != authenticatedUserId)
                    {
                        return Results.Forbid();
                    }

                    var (success, message) =
                        await service.ChangePasswordAsync(
                            authenticatedUserId,
                            request
                        );

                    if (!success)
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message,
                            data = (object?)null
                        });
                    }

                    return Results.Ok(new
                    {
                        success = true,
                        message,
                        data = (object?)null
                    });
                })
                .RequireAuthorization()
                .WithName("ChangePassword");


            app.MapPost("/api/login/enviar-codigo",
                async (
                    ILoginService service,
                    SendLoginCodeRequest request) =>
                {
                    if (request == null ||
                        string.IsNullOrWhiteSpace(request.PreAuthToken))
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message = "Falta el token temporal."
                        });
                    }

                    var (success, message, data) =
                        await service.SendLoginCodeAsync(
                            request.PreAuthToken
                        );

                    if (!success)
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message,
                            data = (object?)null
                        });
                    }

                    return Results.Ok(new
                    {
                        success = true,
                        message,
                        data
                    });
                })
                .WithName("SendLoginCode");

        }
    }
}
