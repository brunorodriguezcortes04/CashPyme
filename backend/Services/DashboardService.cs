using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Indicadores del Resumen. La cuenta la hace SaldoNeto (Dominio), que se prueba con los mismos
/// datos conocidos de los criterios de aceptación; acá solo se juntan la caja y los saldos.
/// </summary>
public class DashboardService(AppDbContext db, SaldosService saldos)
{
    public async Task<SaldoNetoResponse> ObtenerSaldoNetoAsync(long companyId)
    {
        var caja = await saldos.CajaDeEmpresaAsync(companyId);

        // Se filtra por estado ya en la consulta para no traer lo pagado o anulado; la regla de qué
        // entra sigue siendo la de SaldoNeto.Calcular, que vuelve a filtrar.
        var pendientes = db.DocumentosFinancieros.Where(d =>
            d.CompanyId == companyId
            && (d.Status == DocumentoFinanciero.Pendiente || d.Status == DocumentoFinanciero.PagadoParcial));

        var saldosDoc = await saldos.DeDocumentosAsync(pendientes);
        var documentos = await pendientes
            .Select(d => new { d.Id, d.AccountType, d.Status })
            .ToListAsync();

        var r = SaldoNeto.Calcular(caja, documentos.Select(d =>
            new DocumentoParaSaldoNeto(d.AccountType, d.Status, saldosDoc[d.Id].OutstandingAmount)));

        return new SaldoNetoResponse(
            r.SaldoCaja, r.PorCobrar, r.PorPagar, r.SaldoNeto, r.DocumentosPorCobrar, r.DocumentosPorPagar);
    }
}
