using System.Security.Claims;
using APINineHalo.Entities;
using APINineHalo.Services;
using APINineHalo.Repository;
namespace APINineHalo.Endpoints;

public static class PayPalEndpoints
{
    public static void MapPayPalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/compras").RequireAuthorization();
        group.MapGet("/paypal/config", (PayPalOptions options) => Results.Ok(new {
            success = true, data = new { enabled = options.Enabled, client_id = options.Enabled ? options.ClientId : null,
                currency = options.Currency, environment = options.Environment }
        }));
        group.MapPost("/carrito/{key:guid}/paypal/orden", async (Guid key, ClaimsPrincipal user, PayPalCheckoutService service, FacturaRepository invoices) =>
        {
            try
            {
                var order = await service.CreateAsync(user.Actor(), key);
                return Results.Ok(new { success = true, data = new { order_id = order.OrderId } });
            }
            catch (CartPurchaseException ex) { return Results.Json(new { success = false, message = ex.Message }, statusCode: ex.Status); }
        });
        group.MapPost("/carrito/{key:guid}/paypal/capturar", async (Guid key, ClaimsPrincipal user, PayPalCheckoutService service, FacturaRepository invoices) =>
        {
            try
            {
                var result = await service.CaptureAsync(user.Actor(), key);
                return Results.Ok(new { success = true, message = "Pago PayPal confirmado y licencias activadas.", data = new {
                    clave_operacion = key, estado = "ACTIVA", transaccion_id = result.CaptureId,
                    total = result.Compras.Sum(row => row.Precio_Pagado), compras = result.Compras, factura = await invoices.GetAsync(user.Actor(),key)
                } });
            }
            catch (CartPurchaseException ex) { return Results.Json(new { success = false, message = ex.Message }, statusCode: ex.Status); }
        });
    }
}
