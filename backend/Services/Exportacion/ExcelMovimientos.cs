using Backend.Dominio;
using ClosedXML.Excel;

namespace Backend.Services.Exportacion;

/// <summary>
/// Planilla Excel del listado de movimientos (ClosedXML). Celdas con su tipo (fecha y número),
/// no texto, para que se puedan sumar y filtrar; encabezado fijo y autofiltro.
/// </summary>
public static class ExcelMovimientos
{
    public static byte[] Generar(IReadOnlyList<MovimientoExportado> movimientos)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Movimientos");

        for (var c = 0; c < ExportacionMovimientos.Encabezados.Count; c++)
        {
            hoja.Cell(1, c + 1).Value = ExportacionMovimientos.Encabezados[c];
        }

        var fila = 2;
        foreach (var m in movimientos)
        {
            hoja.Cell(fila, 1).Value = m.Fecha.ToDateTime(TimeOnly.MinValue);
            hoja.Cell(fila, 2).Value = m.Tipo;
            hoja.Cell(fila, 3).Value = m.Cuenta;
            hoja.Cell(fila, 4).Value = m.Categoria;
            hoja.Cell(fila, 5).Value = m.MedioPago;
            // Como texto explícito: una descripción que empiece con "=" no se vuelve fórmula.
            hoja.Cell(fila, 6).SetValue(m.Descripcion ?? "");
            hoja.Cell(fila, 7).Value = m.Monto;
            fila++;
        }

        var encabezado = hoja.Range(1, 1, 1, ExportacionMovimientos.Encabezados.Count);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");

        hoja.Column(1).Style.DateFormat.Format = "dd-mm-yyyy";
        hoja.Column(7).Style.NumberFormat.Format = "$#,##0;-$#,##0";

        if (movimientos.Count > 0)
        {
            hoja.Range(1, 1, fila - 1, ExportacionMovimientos.Encabezados.Count).SetAutoFilter();
        }

        hoja.SheetView.FreezeRows(1);
        hoja.Columns().AdjustToContents();

        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }
}
