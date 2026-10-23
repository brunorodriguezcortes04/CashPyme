using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Services;

/// <summary>
/// Alertas: las genera el job (NotificacionesWorker → GenerarAsync), también al pedir GET /alertas, y las lee la pantalla de
/// Resumen. Antes la generación era fn_generar_alertas en la base; ahora las decisiones (cuándo
/// se crea, cuándo se cierra y qué dice cada una) viven en ReglasAlertas y esto las aplica.
/// </summary>
public class AlertasService(AppDbContext db, SaldosService saldos, ProyeccionCajaService proyeccion)
{
    /// <summary>
    /// Alertas sin leer de la empresa: primero las críticas, después por fecha de referencia (lo
    /// que vence antes, arriba). Las leídas y las resueltas no aparecen.
    /// </summary>
    public async Task<IReadOnlyList<AlertaResponse>> ListarPendientesAsync(long companyId)
    {
        var filas = await (
            from a in db.Alertas
            join d in db.DocumentosFinancieros on a.DocumentId equals d.Id into documentos
            from d in documentos.DefaultIfEmpty()
            where a.CompanyId == companyId && a.Status == Alerta.Pendiente
            orderby a.Severity == SeveridadesAlerta.Critica ? 0 : a.Severity == SeveridadesAlerta.Advertencia ? 1 : 2,
                    a.ReferenceDate,
                    a.GeneratedAtUtc
            select new { Alerta = a, TipoCuenta = d == null ? null : d.AccountType }
        ).ToListAsync();

        return filas
            .Select(f => new AlertaResponse(
                f.Alerta.Id, f.Alerta.Type, f.Alerta.Severity,
                ReglasAlertas.EtiquetaSeveridad(f.Alerta.Severity),
                f.Alerta.Message, f.Alerta.ReferenceDate, f.Alerta.DocumentId, f.TipoCuenta,
                f.Alerta.Status, f.Alerta.GeneratedAtUtc,
                ReglasAlertas.RutaDondeSeResuelve(f.Alerta.Type, f.TipoCuenta)))
            .ToList();
    }

    /// <summary>
    /// La alerta es de la empresa, no de cada persona (la tabla tiene un solo estado): leerla la
    /// saca de la lista para todos, y queda registrado quién la leyó. Marcar una que ya estaba
    /// leída o resuelta no hace nada, para que dos personas puedan hacer clic a la vez sin error.
    ///
    /// Leída no es resuelta: mientras el motivo siga vigente, el job no crea otra igual.
    /// </summary>
    public async Task MarcarLeidaAsync(long companyId, long userId, long idAlerta)
    {
        var alerta = await db.Alertas
            .SingleOrDefaultAsync(a => a.Id == idAlerta && a.CompanyId == companyId)
            ?? throw new AlertaNoEncontradaException();

        if (alerta.Status != Alerta.Pendiente)
        {
            return;
        }

        alerta.Status = Alerta.Leida;
        alerta.ReadAtUtc = DateTime.UtcNow;
        alerta.ReadByUserId = userId;

        // La tabla alerta no se audita: quién la leyó queda en la propia fila.
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Corre el job para una empresa o, sin id, para todas. Cierra lo que ya no aplica y crea lo
    /// que falta. Es idempotente: correrlo dos veces seguidas no crea nada la segunda vez,
    /// porque nunca hay dos alertas abiertas del mismo tipo para lo mismo. Devuelve cuántas creó.
    /// </summary>
    public async Task<int> GenerarAsync(long? companyId = null)
    {
        var empresas = await db.Companies
            .Where(c => companyId == null || c.Id == companyId)
            .Select(c => new { c.Id, c.TimeZone, c.DueDateWarningDays, c.LowBalanceThreshold, c.IsActive })
            .ToListAsync();

        var creadas = 0;
        foreach (var e in empresas)
        {
            var hoy = saldos.HoyDe(e.TimeZone);
            creadas += await AlertasDeDocumentosAsync(e.Id, hoy, e.DueDateWarningDays);
            creadas += await AlertasDeSaldoBajoAsync(e.Id, e.LowBalanceThreshold);

            // La proyección solo se mira para empresas activas, igual que antes.
            if (e.IsActive)
            {
                creadas += await AlertaDeCajaNegativaAsync(e.Id);
            }
        }

        return creadas;
    }

    private async Task<int> AlertasDeDocumentosAsync(long companyId, DateOnly hoy, int diasAviso)
    {
        var consulta = db.DocumentosFinancieros.Where(d => d.CompanyId == companyId);
        var saldosDoc = await saldos.DeDocumentosAsync(consulta);
        var documentos = await consulta
            .Select(d => new
            {
                d.Id, d.AccountType, d.Status, d.DueDate, d.DocumentNumber,
                Tercero = d.ThirdParty.Name
            })
            .ToDictionaryAsync(d => d.Id);

        // 1. Cerrar las que dejaron de decir la verdad.
        var abiertas = await db.Alertas
            .Where(a => a.CompanyId == companyId
                        && a.Status != Alerta.Resuelta
                        && (a.Type == TiposAlerta.VencimientoProximo || a.Type == TiposAlerta.DocumentoVencido))
            .ToListAsync();

        foreach (var alerta in abiertas)
        {
            if (alerta.DocumentId is not { } idDoc || !documentos.TryGetValue(idDoc, out var doc))
            {
                continue;
            }

            if (ReglasAlertas.ResolverAlertaDocumento(
                    alerta.Type, alerta.ReferenceDate, doc.Status, saldosDoc[idDoc].OutstandingAmount,
                    doc.DueDate, hoy, diasAviso))
            {
                alerta.Status = Alerta.Resuelta;
            }
        }
        await db.SaveChangesAsync();

        // 2. Crear las que faltan. "Abierta" se mira después de cerrar, igual que antes el
        //    INSERT ... ON CONFLICT corría después del UPDATE.
        var siguenAbiertas = abiertas
            .Where(a => a.Status != Alerta.Resuelta)
            .Select(a => (a.Type, a.DocumentId))
            .ToHashSet();

        var nuevas = new List<Alerta>();
        foreach (var doc in documentos.Values)
        {
            var saldo = saldosDoc[doc.Id].OutstandingAmount;

            if (ReglasAlertas.AvisarVencimientoProximo(saldo, doc.DueDate, hoy, diasAviso)
                && !siguenAbiertas.Contains((TiposAlerta.VencimientoProximo, doc.Id)))
            {
                nuevas.Add(NuevaDeDocumento(companyId, doc.Id, doc.DueDate, TiposAlerta.VencimientoProximo,
                    SeveridadesAlerta.Advertencia,
                    ReglasAlertas.MensajeVencimientoProximo(doc.AccountType, doc.Tercero, doc.DocumentNumber, doc.DueDate, saldo)));
            }

            if (ReglasAlertas.AvisarVencido(saldo, doc.DueDate, hoy)
                && !siguenAbiertas.Contains((TiposAlerta.DocumentoVencido, doc.Id)))
            {
                nuevas.Add(NuevaDeDocumento(companyId, doc.Id, doc.DueDate, TiposAlerta.DocumentoVencido,
                    SeveridadesAlerta.Critica,
                    ReglasAlertas.MensajeVencido(doc.AccountType, doc.Tercero, doc.DocumentNumber, doc.DueDate, saldo)));
            }
        }

        return await InsertarAsync(nuevas);
    }

    private async Task<int> AlertasDeSaldoBajoAsync(long companyId, decimal umbral)
    {
        var cuentas = await db.CuentasFinancieras
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.Name, c.IsActive })
            .ToDictionaryAsync(c => c.Id);
        var saldosCuenta = await saldos.DeCuentasAsync(db.CuentasFinancieras.Where(c => c.CompanyId == companyId));

        var abiertas = await db.Alertas
            .Where(a => a.CompanyId == companyId && a.Status != Alerta.Resuelta && a.Type == TiposAlerta.SaldoBajo)
            .ToListAsync();

        foreach (var alerta in abiertas)
        {
            if (alerta.AccountId is { } idCuenta
                && cuentas.TryGetValue(idCuenta, out var cuenta)
                && ReglasAlertas.ResolverSaldoBajo(saldosCuenta[idCuenta], umbral, cuenta.IsActive))
            {
                alerta.Status = Alerta.Resuelta;
            }
        }
        await db.SaveChangesAsync();

        var siguenAbiertas = abiertas.Where(a => a.Status != Alerta.Resuelta).Select(a => a.AccountId).ToHashSet();

        var nuevas = cuentas.Values
            .Where(c => ReglasAlertas.AvisarSaldoBajo(saldosCuenta[c.Id], umbral, c.IsActive)
                        && !siguenAbiertas.Contains(c.Id))
            .Select(c => new Alerta
            {
                CompanyId = companyId,
                Type = TiposAlerta.SaldoBajo,
                AccountId = c.Id,
                Severity = ReglasAlertas.SeveridadSaldoBajo(saldosCuenta[c.Id]),
                Message = ReglasAlertas.MensajeSaldoBajo(c.Name, saldosCuenta[c.Id], umbral)
            })
            .ToList();

        return await InsertarAsync(nuevas);
    }

    private async Task<int> AlertaDeCajaNegativaAsync(long companyId)
    {
        var dias = await proyeccion.CalcularAsync(companyId, ReglasAlertas.HorizonteSaldoNegativo);
        var primerNegativo = ReglasAlertas.PrimerDiaNegativo(dias);

        var abiertas = await db.Alertas
            .Where(a => a.CompanyId == companyId
                        && a.Status != Alerta.Resuelta
                        && a.Type == TiposAlerta.SaldoNegativoProyectado)
            .ToListAsync();

        // Si ya no hay día negativo, o el primero se movió, la alerta vieja habla de otra fecha.
        foreach (var alerta in abiertas.Where(a => primerNegativo is null || a.ReferenceDate != primerNegativo))
        {
            alerta.Status = Alerta.Resuelta;
        }
        await db.SaveChangesAsync();

        if (primerNegativo is not { } fecha || abiertas.Any(a => a.Status != Alerta.Resuelta && a.ReferenceDate == fecha))
        {
            return 0;
        }

        return await InsertarAsync(
        [
            new Alerta
            {
                CompanyId = companyId,
                Type = TiposAlerta.SaldoNegativoProyectado,
                ReferenceDate = fecha,
                Severity = SeveridadesAlerta.Critica,
                Message = ReglasAlertas.MensajeSaldoNegativo(fecha)
            }
        ]);
    }

    private static Alerta NuevaDeDocumento(
        long companyId, long idDocumento, DateOnly fechaReferencia, string tipo, string severidad, string mensaje)
        => new()
        {
            CompanyId = companyId,
            Type = tipo,
            DocumentId = idDocumento,
            ReferenceDate = fechaReferencia,
            Severity = severidad,
            Message = mensaje
        };

    /// <summary>
    /// Una por una y no en lote: si otra instancia del job creó la misma alerta en el medio, el
    /// índice único (una sola abierta por documento, cuenta o fecha proyectada) la rechaza, y se
    /// sigue con las demás en vez de perder el lote entero. Es lo que hacía ON CONFLICT DO NOTHING.
    /// </summary>
    private async Task<int> InsertarAsync(IEnumerable<Alerta> nuevas)
    {
        var creadas = 0;
        foreach (var alerta in nuevas)
        {
            db.Alertas.Add(alerta);
            try
            {
                await db.SaveChangesAsync();
                creadas++;
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(alerta).State = EntityState.Detached;
            }
        }
        return creadas;
    }
}
