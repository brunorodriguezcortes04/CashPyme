using Backend.Data;
using Backend.Dominio;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.Reportes;

/// <summary>
/// Reporte PDF de un período: saldo de las cuentas al inicio y al cierre, los movimientos del
/// período y las cuentas por cobrar y por pagar pendientes que vencen en él. Usa los mismos
/// cálculos que el dashboard y el listado de movimientos (SaldosService, CalculoDocumento), así
/// el PDF no puede decir algo distinto de lo que muestra la pantalla.
/// </summary>
public class ReportesService(AppDbContext db, SaldosService saldos, TimeProvider reloj)
{
    public async Task<(byte[] Pdf, string NombreArchivo)> GenerarPdfAsync(long companyId, DateOnly? desde, DateOnly? hasta)
    {
        var datos = await ObtenerDatosAsync(companyId, desde, hasta);
        return (ReportePdf.Generar(datos), ReglasReporte.NombreArchivo(datos.Desde, datos.Hasta));
    }

    public async Task<ReporteCaja> ObtenerDatosAsync(long companyId, DateOnly? desde, DateOnly? hasta)
    {
        switch (ReglasReporte.Validar(desde, hasta))
        {
            case ReglasReporte.ResultadoRango.Faltan:
                throw new RangoReporteRequeridoException();
            case ReglasReporte.ResultadoRango.Invertido:
                throw new RangoFechasInvalidoException();
            case ReglasReporte.ResultadoRango.MuyLargo:
                throw new RangoReporteMuyLargoException();
        }

        var (inicio, fin) = (desde!.Value, hasta!.Value);

        var empresa = await db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => new { c.LegalName, c.Rut })
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();

        return new ReporteCaja(
            empresa.LegalName,
            empresa.Rut,
            inicio,
            fin,
            reloj.GetUtcNow().UtcDateTime,
            await saldos.HoyDeEmpresaAsync(companyId),
            await CuentasAsync(companyId, inicio, fin),
            await MovimientosAsync(companyId, inicio, fin),
            await PendientesAsync(companyId, inicio, fin));
    }

    /// <summary>
    /// Cuentas activas, igual que el saldo total del dashboard. El saldo de inicio es el del
    /// cierre del día anterior al período.
    /// </summary>
    private async Task<IReadOnlyList<CuentaReporte>> CuentasAsync(long companyId, DateOnly desde, DateOnly hasta)
    {
        var activas = db.CuentasFinancieras.Where(c => c.CompanyId == companyId && c.IsActive);
        var alInicio = await saldos.DeCuentasAsync(activas, desde.AddDays(-1));
        var alCierre = await saldos.DeCuentasAsync(activas, hasta);

        var cuentas = await activas.OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Type }).ToListAsync();
        return cuentas
            .Select(c => new CuentaReporte(
                c.Name, Etiqueta(TiposCuenta.Todos, c.Type), alInicio[c.Id], alCierre[c.Id]))
            .ToList();
    }

    /// <summary>Movimientos registrados del período, como en el listado de movimientos.</summary>
    private async Task<IReadOnlyList<MovimientoReporte>> MovimientosAsync(long companyId, DateOnly desde, DateOnly hasta)
    {
        var filas = await db.MovimientosFinancieros
            .Where(m => m.CompanyId == companyId
                        && m.Status == MovimientoFinanciero.Registrado
                        && m.MovementDate >= desde && m.MovementDate <= hasta)
            .OrderBy(m => m.MovementDate).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.MovementDate, m.Type, Cuenta = m.Account.Name, Categoria = m.Category.Name,
                m.PaymentMethod, m.Description, m.Amount
            })
            .ToListAsync();

        return filas
            .Select(m => new MovimientoReporte(
                m.MovementDate, m.Type, m.Cuenta, m.Categoria,
                Etiqueta(MediosPago.Todos, m.PaymentMethod), m.Description, m.Amount))
            .ToList();
    }

    /// <summary>
    /// Documentos (no notas) que vencen dentro del período y todavía tienen saldo, con el saldo y
    /// el estado de hoy: lo que sigue pendiente de ese período al momento de sacar el reporte.
    /// </summary>
    private async Task<IReadOnlyList<PendienteReporte>> PendientesAsync(long companyId, DateOnly desde, DateOnly hasta)
    {
        var consulta = db.DocumentosFinancieros.Where(d =>
            d.CompanyId == companyId
            && d.Status != DocumentoFinanciero.Anulado
            && d.DocumentType != TiposDocumento.NotaCredito
            && d.DocumentType != TiposDocumento.NotaDebito
            && d.DueDate >= desde && d.DueDate <= hasta);

        var saldosDoc = await saldos.DeDocumentosAsync(consulta);
        var documentos = await consulta
            .OrderBy(d => d.AccountType).ThenBy(d => d.DueDate).ThenBy(d => d.Id)
            .Select(d => new
            {
                d.Id, d.AccountType, d.DocumentType, d.DocumentNumber, d.DueDate, d.Status,
                Tercero = d.ThirdParty.Name
            })
            .ToListAsync();

        return documentos
            .Where(d => saldosDoc[d.Id].OutstandingAmount > 0)
            .Select(d =>
            {
                var s = saldosDoc[d.Id];
                var tipo = TiposDocumento.Todos.FirstOrDefault(t => t.Valor == d.DocumentType)?.Etiqueta ?? d.DocumentType;
                return new PendienteReporte(
                    d.AccountType,
                    d.Tercero,
                    string.IsNullOrWhiteSpace(d.DocumentNumber) ? tipo : $"{tipo} N° {d.DocumentNumber}",
                    d.DueDate,
                    s.OutstandingAmount,
                    CalculoDocumento.EtiquetaEstado(d.DocumentType, d.Status, s.PaidAmount),
                    s.DaysOverdue);
            })
            .ToList();
    }

    private static string Etiqueta(IEnumerable<OpcionCatalogo> opciones, string valor)
        => opciones.FirstOrDefault(o => o.Valor == valor)?.Etiqueta ?? valor;
}
