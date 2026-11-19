using Backend.Data;
using Backend.Dominio;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Junta lo que la proyección necesita (caja de hoy, documentos con saldo, flujos recurrentes,
/// ajustes del escenario) y se lo pasa a ProyeccionCaja, que hace la cuenta. Antes todo esto era
/// fn_proyeccion_caja. La usan las alertas (caja negativa a 90 días) y el resumen semanal.
/// </summary>
public class ProyeccionCajaService(AppDbContext db, SaldosService saldos)
{
    public async Task<IReadOnlyList<DiaProyectado>> CalcularAsync(long companyId, int dias = 90, long? idEscenario = null)
    {
        var zona = await db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.TimeZone)
            .SingleOrDefaultAsync()
            ?? throw new EmpresaNoEncontradaException();
        var hoy = saldos.HoyDe(zona);

        var retraso = 0;
        var extras = new List<FlujoExtraParaProyeccion>();
        if (idEscenario is not null)
        {
            var esDeLaEmpresa = await db.Escenarios.AnyAsync(s => s.Id == idEscenario && s.CompanyId == companyId);
            if (!esDeLaEmpresa)
            {
                throw new EscenarioNoEncontradoException();
            }

            var ajustes = await db.EscenarioAjustes.Where(a => a.ScenarioId == idEscenario).ToListAsync();
            retraso = ajustes
                .Where(a => a.Type == EscenarioAjuste.RetrasoCobros)
                .Select(a => (int)(a.DelayDays ?? 0))
                .DefaultIfEmpty(0)
                .Max();
            extras = ajustes
                .Where(a => a.Type == EscenarioAjuste.FlujoExtra && a.Date is not null && a.Amount is not null)
                .Select(a => new FlujoExtraParaProyeccion(a.FlowType!, a.Date!.Value, a.Amount!.Value))
                .ToList();
        }

        var caja = await saldos.CajaDeEmpresaAsync(companyId);

        var datosDocumentos = await db.DocumentosFinancieros
            .Where(d => d.CompanyId == companyId)
            .Select(d => new { d.Id, d.AccountType, d.DueDate })
            .ToListAsync();
        var saldosDocumentos = await saldos.DeDocumentosAsync(datosDocumentos.Select(d => d.Id).ToList());

        var paraProyeccion = datosDocumentos
            .Select(d => new DocumentoParaProyeccion(
                d.AccountType, d.DueDate, saldosDocumentos[d.Id].OutstandingAmount))
            .ToList();

        var recurrentes = await db.FlujosRecurrentes
            .Where(f => f.CompanyId == companyId && f.IsActive)
            .Select(f => new FlujoRecurrenteParaProyeccion(f.FlowType, f.Amount, f.Frequency, f.StartDate, f.EndDate))
            .ToListAsync();

        return ProyeccionCaja.Calcular(caja, hoy, dias, paraProyeccion, recurrentes, extras, retraso);
    }
}
