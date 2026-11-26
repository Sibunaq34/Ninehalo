using APINineHalo;
using APINineHalo.Endpoints;
using APINineHalo.Repository;
using APINineHalo.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalPartnerConfiguration();

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

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
builder.Services.AddScoped<SoporteRepository>();
builder.Services.AddScoped<ISoporteService, SoporteService>();


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
app.MapSoporteEndpoints();
app.MapSalesforceEndpoints();

app.Run();

