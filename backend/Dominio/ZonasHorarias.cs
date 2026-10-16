using System.Collections.Concurrent;

namespace Backend.Dominio;

/// <summary>
/// "Hoy" de una empresa, en su propia zona horaria (empresa.zona_horaria). Todas las reglas que
/// dependen de la fecha (vencidos, ventana de aviso, proyección) la usan: con la fecha del
/// servidor, que corre en UTC, en Chile el día cambiaría a las 20 o 21 horas.
/// </summary>
public static class ZonasHorarias
{
    public const string Chile = "America/Santiago";

    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Resueltas = new();

    /// <summary>
    /// Acepta ids IANA ("America/Santiago"), que es lo que guarda la base. Si el sistema no los
    /// conoce (Windows sin ICU) se prueba el equivalente de Windows, y si tampoco existe se cae a
    /// UTC en vez de reventar: una fecha corrida por unas horas es mejor que una pantalla caída.
    /// </summary>
    public static TimeZoneInfo Resolver(string? id)
    {
        return Resueltas.GetOrAdd(string.IsNullOrWhiteSpace(id) ? Chile : id, zona =>
        {
            if (TryBuscar(zona, out var encontrada))
            {
                return encontrada;
            }

            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zona, out var idWindows)
                && TryBuscar(idWindows, out encontrada))
            {
                return encontrada;
            }

            return TimeZoneInfo.Utc;
        });
    }

    public static DateOnly Hoy(string? zona, DateTime ahoraUtc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc), Resolver(zona)));

    private static bool TryBuscar(string id, out TimeZoneInfo zona)
    {
        try
        {
            zona = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zona = TimeZoneInfo.Utc;
            return false;
        }
    }
}
