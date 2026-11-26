using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APINineHalo.Entities;

namespace APINineHalo.Services;

public sealed record BccrQuote(decimal venta, string fecha, string fuente, int indicador, bool provisional, DateTimeOffset consultado_utc, string id);

public sealed class BccrService(IHttpClientFactory clients, IConfiguration configuration, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private BccrQuote? cached;
    private static DateOnly CostaRicaDate(DateTimeOffset now) => DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(-6)).DateTime);

    public async Task<BccrQuote> GetAsync(CancellationToken cancellationToken = default)
    {
        bool Fresh() => cached is not null && clock.GetUtcNow() - cached.consultado_utc < TimeSpan.FromMinutes(15)
            && DateOnly.ParseExact(cached.fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture) <= CostaRicaDate(clock.GetUtcNow());
        if (Fresh()) return cached!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh()) return cached!;
            var token = configuration["Bccr:Token"]?.Trim();
            if (string.IsNullOrWhiteSpace(token))
                throw new CartPurchaseException("Falta configurar el token del BCCR en el servidor para consultar el tipo de cambio.", 503, false);
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
            var now = clock.GetUtcNow();
            var today = CostaRicaDate(now);
            var from = Uri.EscapeDataString(today.AddDays(-7).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            var until = Uri.EscapeDataString(today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"indicadoresEconomicos/318/series?fechaInicio={from}&fechaFin={until}&idioma=ES");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var client = clients.CreateClient("Bccr");
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new CartPurchaseException("El BCCR no aceptó el token del servidor. Revisa su configuración y vigencia.", 503, false);
            if (!response.IsSuccessStatusCode)
                throw new CartPurchaseException("El BCCR no pudo entregar el tipo de cambio. Inténtalo nuevamente en unos minutos.", 503, false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            cached = Parse(document.RootElement, now);
            return cached;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new CartPurchaseException("No se pudo obtener un tipo de cambio válido del BCCR. No se usará una tasa provisional.", 503, false); }
        finally { gate.Release(); }
    }

    public static BccrQuote Parse(JsonElement root, DateTimeOffset now)
    {
        try
        {
            if (root.GetProperty("estado").ValueKind != JsonValueKind.True) throw Invalid();
            var today = CostaRicaDate(now);
            var values = new List<(DateOnly Date, decimal Rate)>();
            foreach (var indicator in root.GetProperty("datos").EnumerateArray())
            {
                var code = indicator.GetProperty("codigoIndicador");
                if ((code.ValueKind == JsonValueKind.String ? code.GetString() : code.GetRawText()) != "318") continue;
                foreach (var row in indicator.GetProperty("series").EnumerateArray())
                {
                    var value = row.GetProperty("valorDatoPorPeriodo");
                    if (value.ValueKind == JsonValueKind.Null) continue;
                    if (!value.TryGetDecimal(out var rate) || rate <= 0 || rate > 100000m || decimal.Round(rate, 6) != rate) throw Invalid();
                    var dateText = row.GetProperty("fecha").GetString();
                    if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) throw Invalid();
                    var day = DateOnly.FromDateTime(date);
                    if (day <= today && day >= today.AddDays(-7)) values.Add((day, rate));
                }
            }
            if (values.Count == 0) throw Invalid();
            var latest = values.Max(row => row.Date);
            var rates = values.Where(row => row.Date == latest).Select(row => row.Rate).Distinct().ToArray();
            if (rates.Length != 1) throw Invalid();
            var rateValue = rates[0];
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"BCCR:318:{latest:yyyy-MM-dd}:{rateValue.ToString(CultureInfo.InvariantCulture)}")));
            return new(rateValue, latest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "BCCR", 318, false, now, id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
        { throw Invalid(); }
    }
    private static CartPurchaseException Invalid() => new("El BCCR no devolvió una tasa de venta válida y reciente para el dólar. No se usará una tasa provisional.", 503, false);
}
