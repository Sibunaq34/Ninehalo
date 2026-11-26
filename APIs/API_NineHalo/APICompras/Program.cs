using APINineHalo;
using APINineHalo.Endpoints;
using APINineHalo.Repository;
using APINineHalo.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.PayPal.local.json", optional: true, reloadOnChange: false).AddJsonFile("appsettings.Bccr.local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.AddLocalPartnerConfiguration();
var paypal = new PayPalOptions();
builder.Configuration.GetSection("PayPal").Bind(paypal);
builder.Services.AddSingleton(paypal);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BccrService>();
builder.Services.AddHttpClient("Bccr", http => {
    http.BaseAddress = new Uri("https://apim.bccr.fi.cr/SDDE/api/Bccr.Ge.SDDE.Publico.Indicadores.API/");
    if(builder.Environment.IsEnvironment("Testing") && builder.Configuration["Bccr:TestBaseUrl"] is string url) {
        var uri = new Uri(url);
        if(!uri.IsLoopback)throw new InvalidOperationException("BCCR tests require a loopback URL.");
        http.BaseAddress = uri;
    }
    http.Timeout = TimeSpan.FromSeconds(12);
});
builder.Services.AddScoped<PayPalRepository>();
builder.Services.AddScoped<PayPalCheckoutService>();
builder.Services.AddHttpClient<PayPalClient>(http => {
    http.BaseAddress = new Uri(paypal.BaseUrl);
    // A loopback-only override lets integration tests use the provider simulation.
    if (builder.Environment.IsEnvironment("Testing") && builder.Configuration["PayPal:TestBaseUrl"] is string testUrl) {
        var testUri = new Uri(testUrl);
        if (!testUri.IsLoopback) throw new InvalidOperationException("PayPal tests require a loopback URL.");
        http.BaseAddress = testUri;
    }
    http.Timeout = TimeSpan.FromSeconds(20);
});

// Add services to the container.
builder.Services.AddOpenApi();

// Get allowed origins from appsettings.json
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

// Configure CORS for the frontend.
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


builder.Services.AddSingleton<IDbConnectionF, DbConnectionFactory>();
builder.Services.AddScoped<CompraRepository>();
builder.Services.AddScoped<ICompraService, CompraService>();

builder.Services.AddScoped<PedidoPagoService>();
builder.Services.AddHttpClient("Banco", http=>http.Timeout=TimeSpan.FromSeconds(18)).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
builder.Services.AddScoped<CarritoRepository>();
builder.Services.AddScoped<PromoHubClient>();
builder.Services.AddScoped<FacturaRepository>();
builder.Services.AddHttpClient("Partners", client => client.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<PartnerDispatcher>();
builder.AddNineHaloSecurity();
var app = builder.Build();
app.UseNineHaloErrors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("NineHaloFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapPedidoPagoEndpoints();
app.MapCarritoEndpoints();
app.MapCuponEndpoints();
app.MapPayPalEndpoints();
app.MapFacturaEndpoints();
app.MapBccrEndpoints();
app.MapCompraEndpoints();


app.Run();
