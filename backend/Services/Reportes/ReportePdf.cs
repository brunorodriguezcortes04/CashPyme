using System.Globalization;
using Backend.Dominio;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Backend.Services.Reportes;

/// <summary>
/// Dibuja el reporte con QuestPDF. No calcula nada: todo viene armado en ReporteCaja.
/// Montos con Clp.Formatear y fechas dd-MM-yyyy, igual que los correos y las alertas.
/// </summary>
public static class ReportePdf
{
    private const string ColorTitulo = "#1F2937";
    private const string ColorSuave = "#6B7280";
    private const string ColorLinea = "#E5E7EB";
    private const string ColorFondoEncabezado = "#F3F4F6";

    static ReportePdf()
    {
        // Licencia Community: gratuita para empresas con ingresos bajo el umbral de QuestPDF.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generar(ReporteCaja r)
    {
        return Document.Create(documento =>
        {
            documento.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(32);
                pagina.DefaultTextStyle(t => t.FontFamily(Fonts.Lato).FontSize(9).FontColor(ColorTitulo));

                pagina.Header().Element(c => Encabezado(c, r));
                pagina.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(16);
                    col.Item().Element(c => Resumen(c, r));
                    col.Item().Element(c => Cuentas(c, r));
                    col.Item().Element(c => Movimientos(c, r));
                    col.Item().Element(c => Pendientes(c, r));
                });
                pagina.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(ColorSuave));
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void Encabezado(IContainer c, ReporteCaja r)
    {
        c.BorderBottom(1).BorderColor(ColorLinea).PaddingBottom(8).Column(col =>
        {
            col.Item().Text("Reporte de caja").FontSize(16).Bold();
            col.Item().Text(string.IsNullOrWhiteSpace(r.Rut) ? r.Empresa : $"{r.Empresa} · RUT {r.Rut}");
            col.Item().Text($"Período: {Fecha(r.Desde)} al {Fecha(r.Hasta)} · Generado el {Fecha(r.GeneradoEl)}")
                .FontColor(ColorSuave);
        });
    }

    private static void Resumen(IContainer c, ReporteCaja r)
    {
        c.Column(col =>
        {
            col.Item().PaddingBottom(4).Text("Resumen del período").FontSize(11).Bold();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(); d.RelativeColumn(2); d.RelativeColumn(); });

                void Par(string etiqueta, decimal monto)
                {
                    t.Cell().Element(Celda).Text(etiqueta);
                    t.Cell().Element(Celda).AlignRight().Text(Clp.Formatear(monto)).Bold();
                }

                Par("Saldo al inicio", r.SaldoInicio);
                Par("Por cobrar pendiente", r.PorCobrar);
                Par("Ingresos", r.Ingresos);
                Par("Por pagar pendiente", r.PorPagar);
                Par("Egresos", r.Egresos);
                t.Cell().Element(Celda).Text("");
                t.Cell().Element(Celda).Text("");
                Par("Saldo al cierre", r.SaldoCierre);
                t.Cell().Element(Celda).Text("");
                t.Cell().Element(Celda).Text("");
            });
        });
    }

    private static void Cuentas(IContainer c, ReporteCaja r)
    {
        Seccion(c, "Saldo por cuenta", r.Cuentas.Count == 0 ? "No hay cuentas activas." : null, t =>
        {
            t.ColumnsDefinition(d => { d.RelativeColumn(3); d.RelativeColumn(2); d.RelativeColumn(2); d.RelativeColumn(2); });
            Encabezados(t, ("Cuenta", false), ("Tipo", false), ("Saldo al inicio", true), ("Saldo al cierre", true));

            foreach (var cuenta in r.Cuentas)
            {
                t.Cell().Element(Celda).Text(cuenta.Nombre);
                t.Cell().Element(Celda).Text(cuenta.Tipo);
                t.Cell().Element(Celda).AlignRight().Text(Clp.Formatear(cuenta.SaldoInicio));
                t.Cell().Element(Celda).AlignRight().Text(Clp.Formatear(cuenta.SaldoCierre));
            }
        });
    }

    private static void Movimientos(IContainer c, ReporteCaja r)
    {
        Seccion(c, "Movimientos del período", r.Movimientos.Count == 0 ? "No hay movimientos en el período." : null, t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(58); d.ConstantColumn(48); d.RelativeColumn(2); d.RelativeColumn(2);
                d.RelativeColumn(3); d.RelativeColumn(2);
            });
            Encabezados(t, ("Fecha", false), ("Tipo", false), ("Cuenta", false), ("Categoría", false),
                ("Descripción", false), ("Monto", true));

            foreach (var m in r.Movimientos)
            {
                var esEgreso = m.Tipo == TiposMovimiento.Egreso;
                t.Cell().Element(Celda).Text(Fecha(m.Fecha));
                t.Cell().Element(Celda).Text(esEgreso ? "Egreso" : "Ingreso");
                t.Cell().Element(Celda).Text(m.Cuenta);
                t.Cell().Element(Celda).Text(m.Categoria);
                t.Cell().Element(Celda).Text(string.IsNullOrWhiteSpace(m.Descripcion) ? m.MedioPago : $"{m.Descripcion} ({m.MedioPago})");
                t.Cell().Element(Celda).AlignRight().Text(Clp.Formatear(esEgreso ? -m.Monto : m.Monto));
            }
        });
    }

    private static void Pendientes(IContainer c, ReporteCaja r)
    {
        Seccion(c, "Cuentas pendientes que vencen en el período",
            r.Pendientes.Count == 0 ? "No hay cuentas por cobrar ni por pagar pendientes que venzan en el período." : null, t =>
        {
            t.ColumnsDefinition(d =>
            {
                d.ConstantColumn(58); d.RelativeColumn(3); d.RelativeColumn(2); d.ConstantColumn(58);
                d.RelativeColumn(2); d.RelativeColumn(2);
            });
            Encabezados(t, ("Tipo", false), ("Cliente / proveedor", false), ("Documento", false),
                ("Vence", false), ("Estado", false), ("Saldo", true));

            foreach (var p in r.Pendientes)
            {
                t.Cell().Element(Celda).Text(p.TipoCuenta == TiposCuentaDocumento.PorCobrar ? "Por cobrar" : "Por pagar");
                t.Cell().Element(Celda).Text(p.Tercero);
                t.Cell().Element(Celda).Text(p.Documento);
                t.Cell().Element(Celda).Text(Fecha(p.Vencimiento));
                t.Cell().Element(Celda).Text(p.DiasAtraso > 0 ? $"{p.Estado} ({p.DiasAtraso} d. atraso)" : p.Estado);
                t.Cell().Element(Celda).AlignRight().Text(Clp.Formatear(p.Saldo));
            }
        }, nota: "Saldo pendiente a la fecha en que se generó el reporte.");
    }

    /// <summary>Título de sección y tabla, o un texto si la sección no tiene filas.</summary>
    private static void Seccion(IContainer c, string titulo, string? vacio, Action<TableDescriptor> tabla, string? nota = null)
    {
        c.Column(col =>
        {
            col.Item().PaddingBottom(4).Text(titulo).FontSize(11).Bold();
            if (vacio is not null)
            {
                col.Item().Text(vacio).FontColor(ColorSuave);
                return;
            }

            col.Item().Table(tabla);
            if (nota is not null)
            {
                col.Item().PaddingTop(4).Text(nota).FontSize(8).FontColor(ColorSuave);
            }
        });
    }

    private static void Encabezados(TableDescriptor t, params (string Texto, bool Derecha)[] columnas)
    {
        t.Header(h =>
        {
            foreach (var (texto, derecha) in columnas)
            {
                var celda = h.Cell().Background(ColorFondoEncabezado).Padding(4);
                (derecha ? celda.AlignRight() : celda).Text(texto).Bold();
            }
        });
    }

    private static IContainer Celda(IContainer c) => c.BorderBottom(0.5f).BorderColor(ColorLinea).PaddingVertical(3).PaddingHorizontal(4);

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
}
