using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using APINineHalo.Entities;

namespace APINineHalo.Services;

public sealed class PromoHubClient(IHttpClientFactory clients, IConfiguration configuration)
{
    public async Task<decimal> ValidateAsync(string code, int[] licenses)
    {
        if (licenses.Contains(1)) throw new CartPurchaseException("El cupón no se puede aplicar si el carrito contiene Básica. Puedes continuar sin cupón.", 422);
        if (code == "NINE10" && licenses.Any(id => id is not (2 or 3)))
            throw new CartPurchaseException("NINE10 solo aplica a Intermedia y Professional.", 422);
        var key = configuration["Partners:PromoHub:Key"];
        if (!configuration.GetValue<bool>("Partners:PromoHub:Enabled") || key is not { Length: >= 24 })
            throw new CartPurchaseException("La validación de cupones no está disponible. Puedes continuar sin cupón.", 503);
        var url = configuration["Partners:PromoHub:Url"] ?? "http://127.0.0.1:3010/api/socios/promohub/cupones/validar";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination) || (destination.Scheme != "https" && !destination.IsLoopback))
            throw new CartPurchaseException("La validación de cupones no está configurada correctamente.", 503);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, destination);
            request.Headers.Add("X-Partner-Key", key);
            request.Content = JsonContent.Create(new { codigo = code, licencias = licenses });
            using var client = clients.CreateClient("Partners");
            using var response = await client.SendAsync(request);
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                var message = data.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
                throw new CartPurchaseException(message ?? "El cupón no aplica a este pedido.", 422);
            }
            if (!response.IsSuccessStatusCode) throw new CartPurchaseException("PromoHub no pudo validar el cupón. Puedes continuar sin cupón o volver a intentar.", 503);
            var coupon = data.GetProperty("data");
            var percentValue = coupon.GetProperty("porcentaje");
            var percent = percentValue.ValueKind == JsonValueKind.String
                ? decimal.Parse(percentValue.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : percentValue.GetDecimal();
            if (coupon.GetProperty("codigo").GetString() != code || percent <= 0 || percent >= 100 || (code == "NINE10" && percent != 10))
                throw new JsonException();
            return percent;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new CartPurchaseException("No fue posible validar el cupón con PromoHub. Puedes continuar sin cupón o volver a intentar.", 503); }
    }
}
