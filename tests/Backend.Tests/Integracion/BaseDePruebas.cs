using System.Data.Common;
using System.Globalization;
using Backend.Data;
using Backend.Dtos;
using Backend.Services;
using Backend.Services.Correo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Backend.Tests.Integracion;

/// <summary>
/// Una base para las pruebas de integración: un esquema propio (prueba_api_…) dentro de la base
/// de desarrollo, con el esquema de database/cashpyme_modelo_datos_v2.sql instalado de cero. Las
/// pruebas nunca tocan public, y el esquema se borra al terminar.
///
/// Usa la misma cadena de conexión que la API (user-secrets o ConnectionStrings__Default). Sin
/// ella las pruebas de integración se saltan en vez de fallar.
/// </summary>
public sealed class BaseDePruebas : IAsyncLifetime
{
    private const string Prefijo = "prueba_api_";
    private const string FormatoFecha = "yyyyMMddHHmmss";

    /// <summary>
    /// Un esquema de una corrida que se cortó a la mitad (sin llegar a DisposeAsync) se borra en
    /// la corrida siguiente. Solo los viejos: otro desarrollador puede estar corriendo las
    /// pruebas ahora mismo contra la misma base.
    /// </summary>
    private static readonly TimeSpan EdadAbandonado = TimeSpan.FromHours(6);

    private readonly string? _conexion;

    public BaseDePruebas()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        _conexion = config.GetConnectionString("Default");
        Esquema = $"{Prefijo}{DateTime.UtcNow.ToString(FormatoFecha, CultureInfo.InvariantCulture)}_{Random.Shared.Next(100_000, 999_999)}";
    }

    public string Esquema { get; }

    public bool Disponible => !string.IsNullOrWhiteSpace(_conexion);

    public async Task InitializeAsync()
    {
        if (!Disponible)
        {
            return;
        }

        await using var conn = new NpgsqlConnection(_conexion);
        await conn.OpenAsync();
        await BorrarAbandonadosAsync(conn);

        await EjecutarAsync(conn, $"CREATE SCHEMA {Esquema}; SET search_path TO {Esquema};");
        var esquemaBase = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "cashpyme_modelo_datos_v2.sql"));
        await EjecutarAsync(conn, esquemaBase);
    }

    public async Task DisposeAsync()
    {
        if (!Disponible)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(_conexion);
        await conn.OpenAsync();
        await EjecutarAsync(conn, $"DROP SCHEMA IF EXISTS {Esquema} CASCADE");
    }

    /// <summary>
    /// Los servicios tal como los arma Program.cs, apuntando al esquema de la prueba. Cada
    /// llamada es un contenedor nuevo: dos contenedores son dos "peticiones" con conexiones
    /// distintas, que es lo que necesitan las pruebas de concurrencia.
    /// </summary>
    public ServiceProvider CrearServicios(TimeProvider? reloj = null)
    {
        Skip.IfNot(Disponible, "Sin ConnectionStrings:Default (user-secrets del backend): se saltan las pruebas de integración.");

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_conexion)
            .AddInterceptors(new EsquemaInterceptor(Esquema)));
        servicios.AddSingleton(reloj ?? TimeProvider.System);
        servicios.AddOptions<JwtOptions>().Configure(o =>
        {
            o.Issuer = "pruebas";
            o.Audience = "pruebas";
            o.Key = new string('k', 48);
        });
        servicios.AddOptions<AppOptions>();
        servicios.AddSingleton<IEmailSender, CorreoDescartado>();
        servicios.AddSingleton<JwtTokenService>();
        servicios.AddScoped<CorreoCuentaService>();
        servicios.AddScoped<AuthService>();
        servicios.AddScoped<TercerosService>();
        servicios.AddScoped<SaldosService>();
        servicios.AddScoped<ProyeccionCajaService>();
        servicios.AddScoped<MovimientosService>();
        servicios.AddScoped<DocumentosService>();
        servicios.AddScoped<PagosService>();
        servicios.AddScoped<DashboardService>();
        servicios.AddScoped<AlertasService>();
        servicios.AddScoped<VencimientosService>();
        servicios.AddScoped<CuentasService>();
        servicios.AddScoped<EmpresaService>();
        servicios.AddScoped<Backend.Services.Reportes.ReportesService>();
        return servicios.BuildServiceProvider();
    }

    /// <summary>Registra una empresa nueva por el mismo camino que la pantalla de registro.</summary>
    public async Task<(long EmpresaId, long UsuarioId)> RegistrarEmpresaAsync(IServiceProvider sp, string nombre)
    {
        var email = $"{Guid.NewGuid():N}@pruebas.cashpyme.cl";
        await sp.GetRequiredService<AuthService>().RegisterAsync(new RegisterRequest(nombre, email, "ClaveDePrueba123"));

        var db = sp.GetRequiredService<AppDbContext>();
        var membresia = await db.CompanyMemberships
            .Where(m => m.User.Email == email)
            .Select(m => new { m.CompanyId, m.UserId })
            .SingleAsync();
        return (membresia.CompanyId, membresia.UserId);
    }

    private static async Task BorrarAbandonadosAsync(NpgsqlConnection conn)
    {
        var abandonados = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 'prueba\\_api\\_%'", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
            {
                var nombre = r.GetString(0);
                var fecha = nombre.Substring(Prefijo.Length, FormatoFecha.Length);
                if (DateTime.TryParseExact(fecha, FormatoFecha, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var creado)
                    && DateTime.UtcNow - creado > EdadAbandonado)
                {
                    abandonados.Add(nombre);
                }
            }
        }

        foreach (var nombre in abandonados)
        {
            await EjecutarAsync(conn, $"DROP SCHEMA IF EXISTS {nombre} CASCADE");
        }
    }

    private static async Task EjecutarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Fija el esquema de la prueba en cada conexión que abre EF. Npgsql limpia la sesión al
    /// devolver la conexión al pool, así que hay que hacerlo en cada apertura y no una sola vez.
    /// </summary>
    private sealed class EsquemaInterceptor(string esquema) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET search_path TO {esquema}";
            cmd.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET search_path TO {esquema}";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class CorreoDescartado : IEmailSender
    {
        public Task EnviarAsync(CorreoSaliente correo, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionBase : ICollectionFixture<BaseDePruebas>
{
    public const string Nombre = "Base de pruebas";
}
