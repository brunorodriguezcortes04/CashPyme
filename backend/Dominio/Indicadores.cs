namespace Backend.Dominio;

/// <summary>
/// Comparación de un monto del período contra el mismo monto del período anterior.
/// Porcentaje es null cuando el anterior es 0: no hay base contra la cual comparar.
/// Favorable dice si el cambio es bueno para la caja (más ingresos, menos egresos), null si no
/// cambió o no hay base: la pantalla lo usa para el color sin decidir nada.
/// </summary>
public record Variacion(decimal Actual, decimal Anterior, decimal Diferencia, decimal? Porcentaje, bool? Favorable);

/// <summary>Monto acumulado de una categoría en el período.</summary>
public record MontoCategoria(long IdCategoria, string NombreCategoria, decimal Monto, int CantidadMovimientos);

/// <summary>Una categoría del ranking, con su peso sobre el total de su tipo.</summary>
public record CategoriaRanking(
    int Posicion,
    long IdCategoria,
    string NombreCategoria,
    decimal Monto,
    int CantidadMovimientos,
    decimal Participacion);

/// <summary>
/// Indicadores del dashboard: variación contra el mes anterior y ranking de categorías.
/// Funciones puras: DashboardService trae los montos y acá se decide qué significan.
/// </summary>
public static class Indicadores
{
    /// <summary>Cuántas categorías muestra el ranking.</summary>
    public const int TamanoRanking = 5;

    /// <summary>
    /// (actual − anterior) / anterior, en porcentaje con un decimal. El divisor va en valor
    /// absoluto: para ingresos y egresos (siempre positivos) da lo mismo, y para un flujo neto
    /// anterior negativo evita que una mejora salga con signo negativo.
    /// </summary>
    public static Variacion Calcular(decimal actual, decimal anterior, bool masEsMejor)
    {
        var diferencia = actual - anterior;
        decimal? porcentaje = anterior == 0
            ? null
            : Math.Round(diferencia / Math.Abs(anterior) * 100, 1, MidpointRounding.AwayFromZero);
        bool? favorable = diferencia == 0 || porcentaje is null ? null : (diferencia > 0) == masEsMejor;

        return new Variacion(actual, anterior, diferencia, porcentaje, favorable);
    }

    /// <summary>
    /// Las categorías de mayor a menor monto acumulado; a igual monto, por nombre, para que el
    /// orden no cambie entre una carga y otra. La participación es sobre el total de todas las
    /// categorías del tipo, no solo las que entran al ranking.
    /// </summary>
    public static IReadOnlyList<CategoriaRanking> Ranking(IEnumerable<MontoCategoria> categorias, int tamano = TamanoRanking)
    {
        var lista = categorias.Where(c => c.Monto > 0).ToList();
        var total = lista.Sum(c => c.Monto);

        return lista
            .OrderByDescending(c => c.Monto)
            .ThenBy(c => c.NombreCategoria, StringComparer.CurrentCultureIgnoreCase)
            .Take(tamano)
            .Select((c, i) => new CategoriaRanking(
                i + 1, c.IdCategoria, c.NombreCategoria, c.Monto, c.CantidadMovimientos,
                total == 0 ? 0 : Math.Round(c.Monto / total * 100, 1, MidpointRounding.AwayFromZero)))
            .ToList();
    }

    /// <summary>El mes calendario anterior al período (que siempre empieza el día 1).</summary>
    public static (DateOnly Desde, DateOnly Hasta) MesAnterior(DateOnly desdePeriodo)
    {
        var desde = desdePeriodo.AddMonths(-1);
        return (desde, desdePeriodo.AddDays(-1));
    }
}
