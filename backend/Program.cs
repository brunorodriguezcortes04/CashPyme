using System.Text;
using Backend.Data;
using Backend.Exceptions;
using Backend.Security;
using Backend.Services;
using Backend.Services.Correo;
using Backend.Services.Notificaciones;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

const string FrontendCorsPolicy = "FrontendCorsPolicy";

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
// Orígenes permitidos en appsettings (Cors:AllowedOrigins) para no recompilar al desplegar.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? throw new InvalidOperationException("Falta Cors:AllowedOrigins en la configuración.");

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// PostgreSQL (Supabase). La cadena de conexión NO va en el repo:
//   dev:  dotnet user-secrets set "ConnectionStrings:Default" "<cadena del Session pooler>"
//   prod: variable de entorno ConnectionStrings__Default
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta ConnectionStrings:Default. Configúrala con 'dotnet user-secrets set' (ver README).");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Key) && o.Key.Length >= 32,
        "Jwt:Key must be configured and at least 32 characters long.")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MovimientosService>();
builder.Services.AddScoped<EmpresaService>();
builder.Services.AddScoped<CuentasService>();
builder.Services.AddScoped<PantallasService>();
builder.Services.AddScoped<RolEmpresaService>();
// Scoped porque resuelve el rol contra AppDbContext en cada request.
builder.Services.AddScoped<IAuthorizationHandler, PermisoAuthorizationHandler>();

// Correo (Resend). Sin Email:ResendApiKey los correos se escriben en la consola en vez de
// enviarse, para que se pueda desarrollar sin cuenta de Resend (ver README).
//   dev:  dotnet user-secrets set "Email:ResendApiKey" "re_..."
//   prod: variable de entorno Email__ResendApiKey
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<NotificacionesOptions>(builder.Configuration.GetSection("Notificaciones"));
if (string.IsNullOrWhiteSpace(builder.Configuration["Email:ResendApiKey"]))
{
    builder.Services.AddSingleton<IEmailSender, ConsolaEmailSender>();
}
else
{
    builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
    {
        client.BaseAddress = new Uri("https://api.resend.com/");
        client.Timeout = TimeSpan.FromSeconds(15);
    });
}
builder.Services.AddScoped<CorreoCuentaService>();
builder.Services.AddScoped<NotificacionesService>();
builder.Services.AddHostedService<NotificacionesWorker>();
builder.Services.AddCashPymeRateLimits();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sin esto, el handler remapea "sub" a ClaimTypes.NameIdentifier (URI largo) y rompe
        // CurrentUserExtensions.GetUserId(), que busca el claim corto que emite JwtTokenService.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"] ?? string.Empty))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// El esquema de la base lo define database/cashpyme_modelo_datos_v2.sql (se ejecuta
// en el SQL Editor de Supabase), por eso ya no se usa EnsureCreated().

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

// En desarrollo NO se redirige a HTTPS. El proxy de Angular apunta a http://localhost:5131,
// y si el perfil https está levantado esto respondía 307 hacia https://localhost:7016: el
// navegador seguía ese redirect cruzando de origen (4200 -> 7016) y, por regla de seguridad,
// BORRA el header Authorization al hacerlo. Resultado: cada petición autenticada llegaba sin
// token y devolvía 401 con cuerpo vacío, así que la aplicación parecía cerrar sesión sola.
// En producción el frontend y la API se sirven bajo el mismo origen y esto no aplica.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    // Solo desarrollo: dispara a mano el ciclo de notificaciones sin esperar al worker ni al
    // lunes. Genera y envía las alertas pendientes y fuerza el resumen semanal de una empresa.
    //   POST http://localhost:5131/api/dev/notificaciones?idEmpresa=1
    app.MapPost("/api/dev/notificaciones", async (long idEmpresa, NotificacionesService service) =>
    {
        var alertas = await service.ProcesarAlertasAsync();
        var reportes = await service.ProcesarReportesSemanalesAsync(idEmpresa);
        return Results.Ok(new { alertasEnviadas = alertas, reportesEnviados = reportes });
    });
}

app.Run();
