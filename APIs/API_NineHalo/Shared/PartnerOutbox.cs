using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using APINineHalo.Repository;

namespace APINineHalo;

public static class PartnerOutbox
{
    public static void AddLocalPartnerConfiguration(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment()) return;

        var path = Path.GetFullPath(Path.Combine(
            builder.Environment.ContentRootPath, "..", "appsettings.Partners.local.json"));
        if (File.Exists(path))
            builder.Configuration.AddJsonFile(path, optional: false, reloadOnChange: false)
                .AddEnvironmentVariables()
                .AddCommandLine(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }

    private static bool Enabled(IConfiguration configuration, string partner) =>
        configuration[$"Partners:{partner}:Key"] is { Length: >= 24 };

    public static bool KeyMatches(string? expected, string provided)
    {
        if (expected is not { Length: >= 24 }) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
    }

    public static async Task EnqueueAsync(IDbConnection connection, IDbTransaction transaction,
        IConfiguration configuration, string partner, string reference, object payload)
    {
        if (!Enabled(configuration, partner)) return;
        await connection.ExecuteAsync("""
            INSERT INTO socio_envio(socio, referencia, contenido, proximo_intento)
            VALUES(@partner, @reference, @json, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE referencia=socio_envio.referencia
            """, new { partner, reference, json = JsonSerializer.Serialize(payload) }, transaction);
    }

    public static async Task NotifyAsync(IDbConnection connection, IDbTransaction transaction,
        IConfiguration configuration, int actor, string reference, string message)
    {
        if (!Enabled(configuration, "NotifyHub")) return;
        var email = await connection.QuerySingleOrDefaultAsync<string>(
            "SELECT correo FROM usuario WHERE id_usuario=@actor AND correo_verificado=1",
            new { actor }, transaction);
        if (email is { Length: > 0 and <= 180 })
            await EnqueueAsync(connection, transaction, configuration, "NotifyHub", reference,
                new { referencia = reference, destinatario = email, mensaje = message });
    }
}

public sealed class PartnerDispatcher(
    IDbConnectionF factory, IConfiguration configuration, IHttpClientFactory clients,
    ILogger<PartnerDispatcher> logger) : BackgroundService
{
    private static readonly (string Partner, string Url)[] Destinations =
    [
        ("Salesforce", "http://127.0.0.1:3007/api/socios/salesforce/casos"),
        ("ContaCloud", "http://127.0.0.1:3008/api/socios/contacloud/ventas"),
        ("NotifyHub", "http://127.0.0.1:3009/api/socios/notifyhub/avisos")
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            foreach (var (partner, url) in Destinations)
            {
                if (configuration[$"Partners:{partner}:Key"] is not { Length: >= 24 } key) continue;
                try
                {
                    await DispatchAsync(partner, configuration[$"Partners:{partner}:Url"] ?? url, key, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) { logger.LogWarning(ex, "No se pudo procesar la cola de {Partner}", partner); }
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchAsync(string partner, string url, string key, CancellationToken token)
    {
        using var connection = factory.CreateConnection();
        var pending = await connection.QueryAsync<Delivery>(new CommandDefinition("""
            SELECT id AS Id, contenido AS Payload FROM socio_envio
            WHERE socio=@partner AND entregado IS NULL AND proximo_intento<=UTC_TIMESTAMP()
            ORDER BY proximo_intento, id LIMIT 20
            """, new { partner }, cancellationToken: token));

        using var client = clients.CreateClient("Partners");
        foreach (var delivery in pending)
        {
            // An expired lease lets another worker retry after a process stops.
            var claimed = await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE socio_envio SET proximo_intento=DATE_ADD(UTC_TIMESTAMP(), INTERVAL 1 MINUTE)
                WHERE id=@id AND entregado IS NULL AND proximo_intento<=UTC_TIMESTAMP()
                """, new { id = delivery.Id }, cancellationToken: token));
            if (claimed == 0) continue;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("X-Partner-Key", key);
                request.Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request, token);
                response.EnsureSuccessStatusCode();
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE socio_envio SET entregado=UTC_TIMESTAMP() WHERE id=@id",
                    new { id = delivery.Id }, cancellationToken: token));
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Envío {Id} a {Partner} pendiente. HTTP {Status}", delivery.Id, partner, ex.StatusCode);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                logger.LogWarning("Tiempo agotado para el envío {Id} a {Partner}", delivery.Id, partner);
            }
        }
    }

    private sealed class Delivery
    {
        public ulong Id { get; set; }
        public string Payload { get; set; } = "";
    }
}
