using Backend.Dominio;
using Backend.Services.Reportes;
using UglyToad.PdfPig;

namespace Backend.Tests.Dominio;

/// <summary>Criterio de aceptación del reporte PDF, con datos conocidos.</summary>
public class ReportePdfTests
{
    private static readonly DateOnly Desde = new(2026, 10, 1);
    private static readonly DateOnly Hasta = new(2026, 10, 31);

    private static ReporteCaja Datos() => new(
        "Ferretería Los Andes SpA", "12345678-5", Desde, Hasta,
        new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 11, 2),
        [
            new("Caja", "Efectivo", 100_000m, 150_000m),
            new("Banco Estado", "Cuenta corriente", 400_000m, 370_000m)
        ],
        [
            new(new DateOnly(2026, 10, 3), TiposMovimiento.Ingreso, "Caja", "Ventas", "Efectivo", "Venta mostrador", 80_000m),
            new(new DateOnly(2026, 10, 9), TiposMovimiento.Egreso, "Banco Estado", "Arriendo", "Transferencia", "Arriendo local", 30_000m),
            new(new DateOnly(2026, 10, 20), TiposMovimiento.Egreso, "Caja", "Proveedores", "Efectivo", null, 30_000m)
        ],
        [
            new(TiposCuentaDocumento.PorCobrar, "Distribuidora Sur", "Factura N° 1045", new DateOnly(2026, 10, 15), 119_000m, "Vencida", 18),
            new(TiposCuentaDocumento.PorPagar, "Insumos del Norte", "Factura N° 778", new DateOnly(2026, 10, 28), 250_000m, "Pendiente", 0)
        ]);

    /// <summary>
    /// Texto del PDF sin espacios: dónde corta QuestPDF una línea dentro de una celda depende del
    /// ancho de la columna, y no es lo que se prueba.
    /// </summary>
    private static string Texto(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        return string.Concat(documento.GetPages().Select(p => p.Text).SelectMany(t => t).Where(c => !char.IsWhiteSpace(c)));
    }

    private static string SinEspacios(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public void Es_un_pdf()
        => Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ReportePdf.Generar(Datos()), 0, 4));

    [Fact]
    public void Criterio_el_pdf_trae_saldo_movimientos_y_cuentas_pendientes_del_periodo()
    {
        var texto = Texto(ReportePdf.Generar(Datos()));

        // Encabezado con el período.
        Assert.Contains(SinEspacios("Ferretería Los Andes SpA"), texto);
        Assert.Contains(SinEspacios("01-10-2026 al 31-10-2026"), texto);

        // Saldo: al inicio 500.000, al cierre 520.000, con el detalle por cuenta.
        Assert.Contains(SinEspacios("Saldo al inicio"), texto);
        Assert.Contains(SinEspacios("$500.000"), texto);
        Assert.Contains(SinEspacios("$520.000"), texto);
        Assert.Contains(SinEspacios("Banco Estado"), texto);

        // Movimientos: los egresos con signo negativo.
        Assert.Contains(SinEspacios("Venta mostrador"), texto);
        Assert.Contains(SinEspacios("$80.000"), texto);
        Assert.Contains(SinEspacios("-$30.000"), texto);

        // Cuentas pendientes, con su estado y el total por tipo.
        Assert.Contains(SinEspacios("Distribuidora Sur"), texto);
        Assert.Contains(SinEspacios("Factura N° 1045"), texto);        Assert.Contains(SinEspacios("Vencida (18 d. atraso)"), texto);
        Assert.Contains(SinEspacios("$119.000"), texto);
        Assert.Contains(SinEspacios("Insumos del Norte"), texto);
        Assert.Contains(SinEspacios("$250.000"), texto);
    }

    [Fact]
    public void Un_periodo_sin_datos_lo_dice_en_vez_de_dejar_tablas_vacias()
    {
        var vacio = Datos() with { Movimientos = [], Pendientes = [] };
        var texto = Texto(ReportePdf.Generar(vacio));

        Assert.Contains(SinEspacios("No hay movimientos en el período."), texto);
        Assert.Contains(SinEspacios("No hay cuentas por cobrar ni por pagar pendientes"), texto);
    }

    [Fact]
    public void El_resumen_suma_lo_que_trae()
    {
        var d = Datos();

        Assert.Equal((500_000m, 520_000m, 80_000m, 60_000m, 119_000m, 250_000m),
            (d.SaldoInicio, d.SaldoCierre, d.Ingresos, d.Egresos, d.PorCobrar, d.PorPagar));
    }

    [Theory]
    [InlineData(null, "2026-10-31", ReglasReporte.ResultadoRango.Faltan)]
    [InlineData("2026-10-01", null, ReglasReporte.ResultadoRango.Faltan)]
    [InlineData("2026-10-31", "2026-10-01", ReglasReporte.ResultadoRango.Invertido)]
    [InlineData("2026-01-01", "2026-12-31", ReglasReporte.ResultadoRango.Valido)]
    [InlineData("2026-01-01", "2027-01-02", ReglasReporte.ResultadoRango.MuyLargo)]
    [InlineData("2026-10-15", "2026-10-15", ReglasReporte.ResultadoRango.Valido)]
    public void El_rango_es_obligatorio_ordenado_y_de_hasta_un_anio(string? desde, string? hasta, ReglasReporte.ResultadoRango esperado)
        => Assert.Equal(esperado, ReglasReporte.Validar(
            desde is null ? null : DateOnly.Parse(desde), hasta is null ? null : DateOnly.Parse(hasta)));

    [Fact]
    public void El_archivo_se_nombra_con_el_periodo()
        => Assert.Equal("reporte-caja-2026-10-01-a-2026-10-31.pdf", ReglasReporte.NombreArchivo(Desde, Hasta));
}
