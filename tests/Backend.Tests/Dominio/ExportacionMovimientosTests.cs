using System.Text;
using Backend.Dominio;
using Backend.Services.Exportacion;
using ClosedXML.Excel;

namespace Backend.Tests.Dominio;

public class ExportacionMovimientosTests
{
    private static readonly MovimientoExportado[] Movimientos =
    [
        new(new DateOnly(2026, 10, 3), "Ingreso", "Caja", "Ventas", "Efectivo", "Venta; mostrador \"especial\"", 150_000m),
        new(new DateOnly(2026, 10, 9), "Egreso", "Banco Estado", "Arriendo", "Transferencia", "=HYPERLINK(\"x\")", 1_234.5m),
        new(new DateOnly(2026, 10, 20), "Egreso", "Caja", "Servicios básicos", "Efectivo", null, 35_000m)
    ];

    [Theory]
    [InlineData(null, "csv")]
    [InlineData("CSV", "csv")]
    [InlineData(" xlsx ", "xlsx")]
    public void Reconoce_el_formato_y_por_defecto_es_csv(string? valor, string esperado)
        => Assert.Equal(esperado, ExportacionMovimientos.Formato(valor)!.Valor);

    [Fact]
    public void Un_formato_desconocido_no_es_valido()
        => Assert.Null(ExportacionMovimientos.Formato("pdf"));

    [Fact]
    public void El_nombre_dice_tipo_y_rango()
    {
        Assert.Equal("movimientos-egreso-2026-10-01-a-2026-10-31.xlsx", ExportacionMovimientos.NombreArchivo(
            "egreso", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), ExportacionMovimientos.Xlsx));
        Assert.Equal("movimientos-2026-10-01.csv", ExportacionMovimientos.NombreArchivo(
            null, new DateOnly(2026, 10, 1), null, ExportacionMovimientos.Csv));
    }

    [Fact]
    public void El_csv_trae_bom_encabezado_y_una_fila_por_movimiento()
    {
        var bytes = ExportacionMovimientos.GenerarCsv(Movimientos);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lineas = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lineas.Length);
        Assert.Equal("Fecha;Tipo;Cuenta;Categoría;Medio de pago;Descripción;Monto", lineas[0]);
        // El ";" y las comillas de la descripción quedan escapados dentro del campo.
        Assert.Equal("03-10-2026;Ingreso;Caja;Ventas;Efectivo;\"Venta; mostrador \"\"especial\"\"\";150000", lineas[1]);
        // Una descripción que Excel ejecutaría como fórmula se neutraliza; el monto va con coma decimal.
        Assert.Equal("09-10-2026;Egreso;Banco Estado;Arriendo;Transferencia;\"'=HYPERLINK(\"\"x\"\")\";1234,5", lineas[2]);
        Assert.Equal("20-10-2026;Egreso;Caja;Servicios básicos;Efectivo;;35000", lineas[3]);
    }

    [Fact]
    public void El_excel_trae_fechas_y_montos_como_valores_no_texto()
    {
        using var libro = new XLWorkbook(new MemoryStream(ExcelMovimientos.Generar(Movimientos)));
        var hoja = libro.Worksheet("Movimientos");

        Assert.Equal("Fecha", hoja.Cell(1, 1).GetString());
        Assert.Equal(new DateTime(2026, 10, 3), hoja.Cell(2, 1).GetDateTime());
        Assert.Equal(150_000m, hoja.Cell(2, 7).GetValue<decimal>());
        Assert.Equal(1_234.5m, hoja.Cell(3, 7).GetValue<decimal>());
        // La descripción con "=" queda como texto, no como fórmula.
        Assert.False(hoja.Cell(3, 6).HasFormula);
        Assert.Equal("=HYPERLINK(\"x\")", hoja.Cell(3, 6).GetString());
        Assert.Equal(4, hoja.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Un_listado_vacio_exporta_solo_el_encabezado()
    {
        var csv = Encoding.UTF8.GetString(ExportacionMovimientos.GenerarCsv([])[3..]);
        Assert.Equal("Fecha;Tipo;Cuenta;Categoría;Medio de pago;Descripción;Monto\r\n", csv);

        using var libro = new XLWorkbook(new MemoryStream(ExcelMovimientos.Generar([])));
        Assert.Equal(1, libro.Worksheet("Movimientos").LastRowUsed()!.RowNumber());
    }
}
