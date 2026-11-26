using APINineHalo;

using APINineHalo.Endpoints;
using APINineHalo.Repository;
using APINineHalo.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<LicenciasRepository>();
builder.Services.AddScoped<ILicenciasService, LicenciasService>();


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
app.MapLicenciasEndpoints();


app.Run();
