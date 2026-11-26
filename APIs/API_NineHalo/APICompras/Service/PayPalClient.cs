using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using APINineHalo.Entities;

namespace APINineHalo.Services;

public sealed class PayPalClient(HttpClient http, PayPalOptions options)
{
    public static string RequestId(int actor, Guid key, string action) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"ninehalo:{actor}:{key:N}:{action}")))[..32];
    public static string Reference(int actor, Guid key) => $"nh:{actor}:{key:N}";
    public async Task<string> CreateAsync(int actor, Guid key, decimal amount)
    {
        var order = await SendAsync(HttpMethod.Post, "v2/checkout/orders", new {
            intent = "CAPTURE",
            purchase_units = new[] { new { reference_id = "ninehalo", custom_id = Reference(actor, key),
                amount = new { currency_code = options.Currency, value = amount.ToString("F2", CultureInfo.InvariantCulture) },
                description = "Licencias Nine Halo" } },
            application_context = new { shipping_preference = "NO_SHIPPING", user_action = "PAY_NOW" }
        }, RequestId(actor, key, "create"));
        var id = order.GetProperty("id").GetString();
        if (id is null || !Regex.IsMatch(id, "^[A-Z0-9]{1,32}$")) throw InvalidResponse();
        return id;
    }
    public Task<JsonElement> GetAsync(string orderId) => SendAsync(HttpMethod.Get, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}");
    public Task<JsonElement> CaptureAsync(int actor, Guid key, string orderId) =>
        SendAsync(HttpMethod.Post, $"v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", new { }, RequestId(actor, key, "capture"));

    public static string ValidateOrder(JsonElement order, string expectedId, int actor, Guid key, decimal amount, string currency)
    {
        try
        {
            if (order.GetProperty("id").GetString() != expectedId || order.GetProperty("intent").GetString() != "CAPTURE") throw InvalidResponse();
            var units = order.GetProperty("purchase_units");
            if (units.GetArrayLength() != 1) throw InvalidResponse();
            var unit = units[0];
            if (unit.GetProperty("custom_id").GetString() != Reference(actor, key)) throw InvalidResponse();
            ValidateAmount(unit.GetProperty("amount"), amount, currency);
            return order.GetProperty("status").GetString() ?? "";
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException) { throw InvalidResponse(); }
    }
    public static string CompletedCapture(JsonElement order, decimal amount, string currency)
    {
        try
        {
            var captures = order.GetProperty("purchase_units")[0].GetProperty("payments").GetProperty("captures");
            if (captures.GetArrayLength() != 1) throw InvalidResponse();
            var capture = captures[0];
            if (capture.GetProperty("status").GetString() != "COMPLETED")
                throw new CartPurchaseException("PayPal todavía no confirmó el pago. Revisa el resultado sin realizar otro cobro.", 409, false);
            ValidateAmount(capture.GetProperty("amount"), amount, currency);
            var id = capture.GetProperty("id").GetString();
            if (id is null || !Regex.IsMatch(id, "^[A-Z0-9]{1,32}$")) throw InvalidResponse();
            return id;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException) { throw InvalidResponse(); }
    }
    private static void ValidateAmount(JsonElement money, decimal expected, string currency)
    {
        if (money.GetProperty("currency_code").GetString() != currency ||
            !decimal.TryParse(money.GetProperty("value").GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var actual) || actual != expected)
            throw InvalidResponse();
    }
    private static CartPurchaseException InvalidResponse() => new("La respuesta de PayPal no coincide con el pedido. No se activaron licencias; revisa el estado del pago.", 502, false);
    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null, string? requestId = null)
    {
        options.RequireEnabled();
        try
        {
            using var auth = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            auth.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.ClientId + ":" + options.ClientSecret)));
            auth.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
            using var tokenResponse = await http.SendAsync(auth);
            if (!tokenResponse.IsSuccessStatusCode) throw new CartPurchaseException("PayPal no aceptó las credenciales del servidor.", 503, false);
            var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
            request.Headers.Add("Prefer", "return=representation");
            if (requestId is not null) request.Headers.Add("PayPal-Request-Id", requestId);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new CartPurchaseException("PayPal no pudo completar esta operación. Revisa el resultado antes de intentar otro pago.", 502, false);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        { throw new CartPurchaseException("No se pudo confirmar la respuesta de PayPal. Reintenta revisar este mismo pago.", 502, false); }
    }
}
