using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
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
            && (d.Status == DocumentoFinanciero.Pendiente
                || d.Status == DocumentoFinanciero.PagadoParcial
                || d.Status == DocumentoFinanciero.Vencido));

        var documentos = await pendientes
            .Select(d => new { d.Id, d.AccountType, d.Status })
            .ToListAsync();
        var saldosDoc = await saldos.DeDocumentosAsync(documentos.Select(d => d.Id).ToList());

        var r = SaldoNeto.Calcular(caja, documentos.Select(d =>
            new DocumentoParaSaldoNeto(d.AccountType, d.Status, saldosDoc[d.Id].OutstandingAmount)));

        return new SaldoNetoResponse(
            r.SaldoCaja, r.PorCobrar, r.PorPagar, r.SaldoNeto, r.DocumentosPorCobrar, r.DocumentosPorPagar);
    }

    /// <summary>
    /// Saldo total de la empresa (todas sus cuentas activas) y lo que entró y salió en el mes
    /// pedido, o en el mes en curso si no se pide ninguno.
    /// </summary>
    public async Task<CajaActualResponse> ObtenerCajaActualAsync(long companyId, int? anio, int? mes)
    {
        var hoy = await saldos.HoyDeEmpresaAsync(companyId);
        var (desde, hasta) = CajaActual.Periodo(anio, mes, hoy) ?? throw new PeriodoInvalidoException();

        // Criterio 2: solo las cuentas activas entran al saldo total.
        var activas = db.CuentasFinancieras.Where(c => c.CompanyId == companyId && c.IsActive);
        var cuentas = await activas
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.Type })
            .ToListAsync();
        var saldosCuenta = await saldos.DeCuentasAsync(cuentas.Select(c => c.Id).ToList());

        // Criterio 1: ingresos y egresos salen del mismo flujo diario que se muestra.
        var flujo = await saldos.FlujoDiarioAsync(companyId, desde, hasta);
        var t = CajaActual.Calcular(saldosCuenta.Values, flujo);

        return new CajaActualResponse(
            desde, hasta, t.SaldoTotal, t.Ingresos, t.Egresos, t.FlujoNeto,
            cuentas.Select(c => new SaldoCuentaResponse(c.Id, c.Name, c.Type, saldosCuenta[c.Id])).ToList(),
            flujo.Select(d => new FlujoDiaResponse(d.Fecha, d.Ingresos, d.Egresos)).ToList());
    }

    /// <summary>
    /// Variación del mes pedido (o el en curso) contra el mes anterior, y ranking de categorías
    /// del mes. Solo cuentan los movimientos registrados, igual que en el saldo y el flujo diario.
    /// </summary>
    public async Task<IndicadoresResponse> ObtenerIndicadoresAsync(long companyId, int? anio, int? mes)
    {
        var hoy = await saldos.HoyDeEmpresaAsync(companyId);
        var (desde, hasta) = CajaActual.Periodo(anio, mes, hoy) ?? throw new PeriodoInvalidoException();
        var (desdeAnterior, hastaAnterior) = Indicadores.MesAnterior(desde);

        var actual = await saldos.FlujoEntreAsync(companyId, desde, hasta);
        var anterior = await saldos.FlujoEntreAsync(companyId, desdeAnterior, hastaAnterior);

        var porCategoria = await db.MovimientosFinancieros
            .Where(m => m.CompanyId == companyId
                        && m.Status == MovimientoFinanciero.Registrado
                        && m.MovementDate >= desde && m.MovementDate <= hasta)
            .GroupBy(m => new { m.Type, m.CategoryId, m.Category.Name })
            .Select(g => new
            {
                g.Key.Type,
                Categoria = new MontoCategoria(g.Key.CategoryId, g.Key.Name, g.Sum(m => m.Amount), g.Count())
            })
            .ToListAsync();

        IReadOnlyList<CategoriaRanking> Ranking(string tipo)
            => Indicadores.Ranking(porCategoria.Where(c => c.Type == tipo).Select(c => c.Categoria));

        return new IndicadoresResponse(
            desde, hasta, desdeAnterior, hastaAnterior,
            Indicadores.Calcular(actual.Ingresos, anterior.Ingresos, masEsMejor: true),
            Indicadores.Calcular(actual.Egresos, anterior.Egresos, masEsMejor: false),
            Indicadores.Calcular(actual.Ingresos - actual.Egresos, anterior.Ingresos - anterior.Egresos, masEsMejor: true),
            Ranking(TiposMovimiento.Ingreso),
            Ranking(TiposMovimiento.Egreso));
    }
}
