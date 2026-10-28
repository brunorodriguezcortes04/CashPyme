using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Proyección de caja guardada para el dashboard. Generar calcula con ProyeccionCajaService
/// (documentos con saldo + flujos recurrentes), guarda un juego de días por horizonte en
/// proyeccion_flujo_caja y, con ese mismo resultado, actualiza la alerta de caja negativa.
/// Consultar lee la última generación tal como quedó, sin recalcular.
///
/// Se genera al pedirlo (POST /api/proyeccion) y una vez al día por empresa (ProyeccionesWorker).
/// </summary>
public class ProyeccionFlujoCajaService(
    AppDbContext db,
    ProyeccionCajaService proyeccion,
    AlertasService alertas,
    TimeProvider reloj,
    ILogger<ProyeccionFlujoCajaService> logger)
{
    public async Task<ProyeccionResponse> GenerarAsync(long companyId)
    {
        var dias = await proyeccion.CalcularAsync(companyId, ProyeccionGuardada.HorizonteMayor);

        // Postgres guarda microsegundos: se recorta antes, para que lo guardado sea idéntico a lo
        // que se devuelve y a lo que identifica la generación.
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var generada = ahora.AddTicks(-(ahora.Ticks % TimeSpan.TicksPerMicrosecond));
        const string modelo = ModelosProyeccion.DocumentosRecurrentes;

        var filas = ProyeccionGuardada.Horizontes
            .SelectMany(h => ProyeccionGuardada.Recortar(dias, h).Select(d => new ProyeccionFlujoCaja
            {
                CompanyId = companyId,
                GeneratedAtUtc = generada,
                HorizonDays = (short)h,
                ProjectedDate = d.Dia,
                ProjectedIncome = d.TotalIngresos,
                ProjectedExpense = d.TotalEgresos,
                ProjectedBalance = d.SaldoProyectado,
                Model = modelo
            }))
            .ToList();

        db.ProyeccionesFlujoCaja.AddRange(filas);
        await db.SaveChangesAsync();

        // Mismo resultado que se acaba de guardar, en todo el horizonte guardado.
        await alertas.ActualizarCajaNegativaAsync(companyId, dias);

        return Armar(generada, modelo, filas);
    }

    /// <summary>La última generación de la empresa, o null si todavía no se generó ninguna.</summary>
    public async Task<ProyeccionResponse?> ObtenerUltimaAsync(long companyId)
    {
        var ultima = await db.ProyeccionesFlujoCaja
            .Where(p => p.CompanyId == companyId)
            .MaxAsync(p => (DateTime?)p.GeneratedAtUtc);

        if (ultima is null)
        {
            return null;
        }

        var filas = await db.ProyeccionesFlujoCaja
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.GeneratedAtUtc == ultima)
            .ToListAsync();

        return Armar(ultima.Value, filas[0].Model, filas);
    }

    /// <summary>
    /// Lo corre el job: genera la del día para cada empresa activa que todavía no la tiene. Es
    /// idempotente dentro del día (de la empresa), así que el job puede pasar cada hora. Una
    /// empresa que falla no frena a las demás. Devuelve cuántas generó.
    /// </summary>
    public async Task<int> GenerarDiariasAsync(CancellationToken ct = default)
    {
        var empresas = await db.Companies
            .Where(c => c.IsActive)
            .Select(c => new
            {
                c.Id,
                c.TimeZone,
                Ultima = db.ProyeccionesFlujoCaja.Where(p => p.CompanyId == c.Id).Max(p => (DateTime?)p.GeneratedAtUtc)
            })
            .ToListAsync(ct);

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var generadas = 0;
        foreach (var e in empresas.Where(e => ProyeccionGuardada.TocaGenerar(e.Ultima, e.TimeZone, ahora)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await GenerarAsync(e.Id);
                generadas++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo generar la proyección de la empresa {Empresa}.", e.Id);
                // Lo que quedó a medio guardar de esta empresa no debe viajar con la siguiente.
                db.ChangeTracker.Clear();
            }
        }

        return generadas;
    }

    private static ProyeccionResponse Armar(DateTime generada, string modelo, IEnumerable<ProyeccionFlujoCaja> filas)
    {
        var horizontes = filas
            .GroupBy(f => (int)f.HorizonDays)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dias = g
                    .OrderBy(f => f.ProjectedDate)
                    .Select(f => new DiaProyectado(f.ProjectedDate, f.ProjectedIncome, f.ProjectedExpense, f.ProjectedBalance))
                    .ToList();
                var r = ProyeccionGuardada.Resumir(g.Key, dias);

                return new HorizonteProyeccionResponse(
                    r.HorizonteDias, r.Desde, r.Hasta, r.SaldoInicial, r.SaldoFinal, r.TotalIngresos,
                    r.TotalEgresos, r.SaldoMinimo, r.FechaSaldoMinimo, r.PrimerDiaNegativo,
                    dias.Select(d => new DiaProyeccionResponse(d.Dia, d.TotalIngresos, d.TotalEgresos, d.SaldoProyectado)).ToList());
            })
            .ToList();

        return new ProyeccionResponse(generada, modelo, ModelosProyeccion.Etiqueta(modelo), horizontes);
    }
}
