using APINineHalo;
using System.Security.Claims;

using APINineHalo.Entities;
using APINineHalo.Services;

namespace APINineHalo.Endpoints
{
    public static class CompraEndpoints
    {
        public static void MapCompraEndpoints(this WebApplication app)
        {
            app.MapPost("/api/compras", async (
                ICompraService service,
                PurchaseRequest request, ClaimsPrincipal user) =>
            {
                if (request == null)
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "Datos de compra inválidos.",
                        data = (object?)null
                    });
                }

                if (request.id_usuario <= 0 || request.id_licencia <= 0)
                {
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "Identificadores inválidos.",
                        data = (object?)null
                    });
                }

                if (request.id_usuario != user.Actor()) return ApiSecurity.Error("Permiso denegado.",403);
                var (Success, Message, Purchase) =
                    await service.RegisterPurchaseAsync(request.id_usuario, request);

                if (!Success)
                {
                    if (Message.Contains("igual o superior", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Conflict(new
                        {
                            success = false,
                            message = Message,
                            data = (object?)null
                        });
                    }

                    if (Message.Contains("no existe", StringComparison.OrdinalIgnoreCase) ||
                        Message.Contains("no encontrados", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.NotFound(new
                        {
                            success = false,
                            message = Message,
                            data = (object?)null
                        });
                    }

                    if (Message.Contains("inválidos", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            message = Message,
                            data = (object?)null
                        });
                    }

                    return Results.Json(new
                    {
                        success = false,
                        message = Message,
                        data = (object?)null
                    }, statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Json(new
                {
                    success = true,
                    message = Message,
                    data = Purchase
                }, statusCode: StatusCodes.Status201Created);
            })
            .RequireAuthorization().WithName("RegisterCompra");
        }
    }
}

