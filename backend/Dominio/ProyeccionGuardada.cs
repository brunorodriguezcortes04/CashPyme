namespace Backend.Dominio;

/// <summary>Debe coincidir con ck_proyeccion_modelo en la base.</summary>
public static class ModelosProyeccion
{
    /// <summary>
    /// Documentos con saldo + flujos recurrentes (ProyeccionCaja). Es el que se usa porque ningún
    /// modelo estadístico alcanzó el desempeño mínimo (analitica/MODELO.md).
    /// </summary>
    public const string DocumentosRecurrentes = "documentos_recurrentes";

    public static string Etiqueta(string modelo) => modelo switch
    {
        DocumentosRecurrentes => "Cuentas por cobrar y por pagar + flujos recurrentes",
        _ => modelo
    };
}

/// <summary>Lo que se muestra de un horizonte sin recorrer los días: de dónde parte, adónde llega y su peor punto.</summary>
public record ResumenHorizonte(
    int HorizonteDias,
    DateOnly Desde,
    DateOnly Hasta,
    decimal SaldoInicial,
    decimal SaldoFinal,
    decimal TotalIngresos,
    decimal TotalEgresos,
    decimal SaldoMinimo,
    DateOnly FechaSaldoMinimo,
    DateOnly? PrimerDiaNegativo);

/// <summary>
/// Reglas de la proyección que se guarda en proyeccion_flujo_caja: qué horizontes se guardan,
/// cómo se sacan de un solo cálculo, cómo se resume cada uno y cuándo toca generar otra.
/// </summary>
public static class ProyeccionGuardada
{
    /// <summary>Horizontes que se guardan en cada generación (la historia pide 30 y 60 días).</summary>
    public static readonly IReadOnlyList<int> Horizontes = [30, 60];

    /// <summary>
    /// Se calcula una vez, al horizonte más largo, y los demás salen recortándolo: así el de 30
    /// días es siempre el comienzo exacto del de 60, sin dos cálculos que puedan diferir.
    /// También es el horizonte que mira la alerta de caja negativa.
    /// </summary>
    public static int HorizonteMayor => Horizontes.Max();

    /// <summary>Días de hoy a hoy + horizonte, inclusive (horizonte + 1 filas), igual que ProyeccionCaja.</summary>
    public static IReadOnlyList<DiaProyectado> Recortar(IReadOnlyList<DiaProyectado> dias, int horizonte)
    {
        if (dias.Count < horizonte + 1)
        {
            throw new ArgumentException($"La proyección tiene {dias.Count} días y el horizonte de {horizonte} pide {horizonte + 1}.", nameof(dias));
        }

        return dias.Take(horizonte + 1).ToList();
    }

    public static ResumenHorizonte Resumir(int horizonte, IReadOnlyList<DiaProyectado> dias)
    {
        var primero = dias[0];
        // Si el saldo más bajo se repite, cuenta la primera vez que se llega a él: es cuando hay que actuar.
        var minimo = dias.Aggregate((menor, d) => d.SaldoProyectado < menor.SaldoProyectado ? d : menor);

        return new ResumenHorizonte(
            horizonte,
            primero.Dia,
            dias[^1].Dia,
            // El saldo del primer día ya incluye lo que vence hoy: el inicial es el de antes.
            primero.SaldoProyectado - primero.TotalIngresos + primero.TotalEgresos,
            dias[^1].SaldoProyectado,
            dias.Sum(d => d.TotalIngresos),
            dias.Sum(d => d.TotalEgresos),
            minimo.SaldoProyectado,
            minimo.Dia,
            ReglasAlertas.PrimerDiaNegativo(dias));
    }

    /// <summary>
    /// El job genera una por día y por empresa: toca si nunca se generó o si la última es de un
    /// día anterior en la zona horaria de la empresa (no en la del servidor, que corre en UTC).
    /// </summary>
    public static bool TocaGenerar(DateTime? ultimaGeneracionUtc, string? zonaHoraria, DateTime ahoraUtc)
        => ultimaGeneracionUtc is not { } ultima
           || ZonasHorarias.Hoy(zonaHoraria, ultima) < ZonasHorarias.Hoy(zonaHoraria, ahoraUtc);
}
