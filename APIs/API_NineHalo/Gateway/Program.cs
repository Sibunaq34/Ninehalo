
var builder = WebApplication.CreateBuilder(args);

// Obtener los orígenes permitidos
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

// Configurar CORS
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

// Configurar YARP
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(
        builder.Configuration.GetSection("ReverseProxy")
    );

var app = builder.Build();

// Habilitar CORS
app.UseCors("NineHaloFrontend");

// Mapear las rutas del Gateway
app.MapReverseProxy();

app.Run();
