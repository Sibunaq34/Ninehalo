using APINineHalo.Entities;
using APINineHalo.Repository;
using APINineHalo.Services;
using Dapper;
namespace APINineHalo.Endpoints;

public sealed class CouponPreviewRequest
{
    public string Codigo { get; set; } = "";
    public List<CartPurchaseItem>? Items { get; set; }
}
public static class CuponEndpoints
{
    public static void MapCuponEndpoints(this WebApplication app)
    {
        app.MapPost("/api/compras/cupones/previsualizar", async (CouponPreviewRequest request, IDbConnectionF factory, PromoHubClient promo) =>
        {
            var code = (request.Codigo??"").Trim().ToUpperInvariant();
            if (code.Length is < 1 or > 32 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || request.Items is not { Count: >= 1 and <= 10 }
                || request.Items.Any(item => item is null || item.Id_Licencia <= 0 || item.Cantidad is < 1 or > 20)
                || request.Items.Select(item => item.Id_Licencia).Distinct().Count() != request.Items.Count || request.Items.Sum(item => item.Cantidad) > 50)
                return Results.Json(new { success = false, message = "Indica un código y un carrito válidos." }, statusCode: 400);
            try
            {
                using var db = factory.CreateConnection();
                var ids = request.Items.Select(item => item.Id_Licencia).ToArray();
                var prices = (await db.QueryAsync<CatalogPrice>("SELECT id_licencia AS Id,precio FROM licencia WHERE id_licencia IN @ids AND activo=1", new { ids })).ToDictionary(row => row.Id, row => row.Precio);
                if (prices.Count != ids.Length) throw new CartPurchaseException("Una licencia ya no está disponible. Revisa el carrito.", 409);
                var percent = await promo.ValidateAsync(code, ids);
                var subtotal = request.Items.Sum(item => prices[item.Id_Licencia] * item.Cantidad);
                var total = request.Items.Sum(item => decimal.Round(prices[item.Id_Licencia] * (1 - percent / 100m), 2, MidpointRounding.AwayFromZero) * item.Cantidad);
                return Results.Ok(new { success = true, data = new { cupon = code, subtotal, descuento = subtotal - total, total, porcentaje = percent, estimado = true } });
            }
            catch (CartPurchaseException ex) { return Results.Json(new { success = false, message = ex.Message }, statusCode: ex.Status); }
        }).RequireAuthorization();
    }
    private sealed class CatalogPrice { public int Id { get; set; } public decimal Precio { get; set; } }
}
