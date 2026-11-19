using System.Globalization;
using System.Text;

namespace Backend.Dominio;

/// <summary>Un movimiento tal como sale en el archivo exportado.</summary>
public record MovimientoExportado(
    DateOnly Fecha,
    string Tipo,
    string Cuenta,
    string Categoria,
    string MedioPago,
    string? Descripcion,
    decimal Monto);

/// <summary>Formato de exportación con lo que necesita la respuesta HTTP.</summary>
public record FormatoExportacion(string Valor, string Extension, string ContentType);

/// <summary>
/// Reglas de la exportación del listado de movimientos. El contenido es el mismo listado
/// filtrado (MovimientosService), sin paginar; acá se decide el formato, el nombre del archivo
/// y cómo se escribe cada celda.
/// </summary>
public static class ExportacionMovimientos
{
    public static readonly FormatoExportacion Csv = new("csv", "csv", "text/csv; charset=utf-8");
    public static readonly FormatoExportacion Xlsx = new(
        "xlsx", "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

    public static readonly IReadOnlyList<FormatoExportacion> Formatos = [Csv, Xlsx];

    /// <summary>
    /// Tope de filas: más que eso ya no es un listado para revisar sino un respaldo de la base, y
    /// generarlo en memoria en una petición web no es razonable. Se pide acotar el filtro.
    /// </summary>
    public const int MaximoFilas = 50_000;

    /// <summary>Encabezados en el orden de las columnas.</summary>
    public static readonly IReadOnlyList<string> Encabezados =
        ["Fecha", "Tipo", "Cuenta", "Categoría", "Medio de pago", "Descripción", "Monto"];

    /// <summary>Sin formato pedido se exporta CSV; uno desconocido es null.</summary>
    public static FormatoExportacion? Formato(string? valor)
        => string.IsNullOrWhiteSpace(valor)
            ? Csv
            : Formatos.FirstOrDefault(f => string.Equals(f.Valor, valor.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>movimientos-egreso-2026-10-01-a-2026-10-31.xlsx; sin tipo, movimientos-….</summary>
    public static string NombreArchivo(string? tipo, DateOnly? desde, DateOnly? hasta, FormatoExportacion formato)
    {
        var partes = new List<string> { "movimientos" };
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            partes.Add(tipo);
        }

        if (desde is not null)
        {
            partes.Add($"{desde:yyyy-MM-dd}");
        }

        if (hasta is not null)
        {
            partes.Add(desde is null ? $"hasta-{hasta:yyyy-MM-dd}" : $"a-{hasta:yyyy-MM-dd}");
        }

        return $"{string.Join('-', partes)}.{formato.Extension}";
    }

    /// <summary>
    /// CSV para abrir con Excel en español: separador ";" (la coma es el separador decimal),
    /// BOM UTF-8 para que respete las tildes, y montos con coma decimal.
    /// </summary>
    public static byte[] GenerarCsv(IEnumerable<MovimientoExportado> movimientos)
    {
        var csv = new StringBuilder();
        csv.Append(string.Join(';', Encabezados.Select(Campo))).Append("\r\n");

        foreach (var m in movimientos)
        {
            string[] campos =
            [
                m.Fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                m.Tipo,
                m.Cuenta,
                m.Categoria,
                m.MedioPago,
                m.Descripcion ?? "",
                Monto(m.Monto)
            ];
            csv.Append(string.Join(';', campos.Select(Campo))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    /// <summary>
    /// Escapa un campo de CSV. Además, un texto escrito por la persona que empiece con
    /// = + - @ se neutraliza con un apóstrofo: Excel lo ejecutaría como fórmula al abrir el
    /// archivo (inyección de fórmulas). Los montos no pasan por acá con signo.
    /// </summary>
    public static string Campo(string valor)
    {
        if (valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            valor = "'" + valor;
        }

        return valor.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? $"\"{valor.Replace("\"", "\"\"")}\""
            : valor;
    }

    /// <summary>Monto sin separador de miles y con coma decimal solo si tiene decimales: 150000 o 1234,5.</summary>
    public static string Monto(decimal monto)
        => monto.ToString(monto == decimal.Truncate(monto) ? "0" : "0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Etiqueta del tipo para el archivo: "Ingreso" o "Egreso".</summary>
    public static string EtiquetaTipo(string tipo)
        => tipo == TiposMovimiento.Ingreso ? "Ingreso" : tipo == TiposMovimiento.Egreso ? "Egreso" : tipo;
}
