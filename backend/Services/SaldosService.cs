using Backend.Data;
using Backend.Dominio;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Saldos de documentos y de cuentas, y el estado de los documentos. Reemplaza a las vistas
/// vista_documento_saldo, vista_saldo_cuenta y vista_flujo_diario, y a la función
/// fn_recalcular_estado_documento: la base solo devuelve sumas, y las fórmulas que las combinan
/// viven en Dominio (CalculoDocumento, CalculoCuenta), un solo lugar para todas las pantallas,
/// la proyección, las alertas y los correos.
/// </summary>
public class SaldosService(AppDbContext db, TimeProvider reloj)
{
    /// <summary>"Hoy" en la zona horaria de la empresa, no la del servidor.</summary>
    public DateOnly HoyDe(string? zonaHoraria) => ZonasHorarias.Hoy(zonaHoraria, reloj.GetUtcNow().UtcDateTime);

    public async Task<DateOnly> HoyDeEmpresaAsync(long companyId)
    {
        var zona = await db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.TimeZone)
            .SingleOrDefaultAsync();
        return HoyDe(zona);
    }

    public Task<Dictionary<long, SaldoDocumento>> DeDocumentosAsync(IReadOnlyCollection<long> ids)
        => DeDocumentosAsync(db.DocumentosFinancieros.Where(d => ids.Contains(d.Id)));

    /// <summary>
    /// Saldo neteado de cada documento de la consulta: monto, lo pagado con movimientos
    /// registrados (un pago cuyo movimiento se anuló no cuenta), y las notas de crédito y débito
    /// que lo corrigen (sin las anuladas).
    /// </summary>
    public async Task<Dictionary<long, SaldoDocumento>> DeDocumentosAsync(IQueryable<DocumentoFinanciero> documentos)
    {
        var filas = await documentos
            .Select(d => new
            {
                d.Id, d.CompanyId, d.DocumentType, d.Status, d.TotalAmount, d.DueDate,
                Zona = db.Companies.Where(c => c.Id == d.CompanyId).Select(c => c.TimeZone).FirstOrDefault()
            })
            .ToListAsync();

        if (filas.Count == 0)
        {
            return [];
        }

        var pagado = await (
            from p in db.PagosDocumento
            join m in db.MovimientosFinancieros on p.MovementId equals m.Id
            join d in documentos on p.DocumentId equals d.Id
            where m.Status == MovimientoFinanciero.Registrado
            group p by p.DocumentId into g
            select new { Id = g.Key, Total = g.Sum(x => x.AppliedAmount) }
        ).ToDictionaryAsync(x => x.Id, x => x.Total);

        var notas = await (
            from n in db.DocumentosFinancieros
            join d in documentos on n.ReferenceDocumentId equals d.Id
            where n.Status != DocumentoFinanciero.Anulado
            group n by n.ReferenceDocumentId into g
            select new
            {
                Id = g.Key!.Value,
                Credito = g.Sum(x => x.DocumentType == TiposDocumento.NotaCredito ? x.TotalAmount : 0m),
                Debito = g.Sum(x => x.DocumentType == TiposDocumento.NotaDebito ? x.TotalAmount : 0m)
            }
        ).ToDictionaryAsync(x => x.Id);

        return filas.ToDictionary(f => f.Id, f =>
        {
            var pagadoDoc = pagado.GetValueOrDefault(f.Id);
            var credito = notas.TryGetValue(f.Id, out var n) ? n.Credito : 0m;
            var debito = notas.TryGetValue(f.Id, out n) ? n.Debito : 0m;
            var saldo = CalculoDocumento.Saldo(f.DocumentType, f.Status, f.TotalAmount, pagadoDoc, credito, debito);
            var hoy = HoyDe(f.Zona);

            return new SaldoDocumento
            {
                DocumentId = f.Id,
                CompanyId = f.CompanyId,
                TotalAmount = f.TotalAmount,
                PaidAmount = pagadoDoc,
                CreditNotesAmount = credito,
                DebitNotesAmount = debito,
                NetAmount = CalculoDocumento.Neto(f.TotalAmount, credito, debito),
                OutstandingAmount = saldo,
                IsOverdue = CalculoDocumento.EstaVencido(saldo, f.DueDate, hoy),
                DaysOverdue = CalculoDocumento.DiasAtraso(saldo, f.DueDate, hoy)
            };
        });
    }

    /// <summary>
    /// Saldo actual de cada cuenta de la consulta: saldo inicial más ingresos menos egresos
    /// registrados, más lo que le transfirieron y menos lo que transfirió.
    /// </summary>
    public async Task<Dictionary<long, decimal>> DeCuentasAsync(IQueryable<CuentaFinanciera> cuentas)
    {
        var iniciales = await cuentas
            .Select(c => new { c.Id, c.InitialBalance })
            .ToDictionaryAsync(c => c.Id, c => c.InitialBalance);

        if (iniciales.Count == 0)
        {
            return [];
        }

        var movimientos = await (
            from m in db.MovimientosFinancieros
            join c in cuentas on m.AccountId equals c.Id
            where m.Status == MovimientoFinanciero.Registrado
            group m by m.AccountId into g
            select new
            {
                Id = g.Key,
                Ingresos = g.Sum(x => x.Type == TiposMovimiento.Ingreso ? x.Amount : 0m),
                Egresos = g.Sum(x => x.Type == TiposMovimiento.Egreso ? x.Amount : 0m)
            }
        ).ToDictionaryAsync(x => x.Id);

        var entrantes = await (
            from t in db.TransferenciasCuentas
            join c in cuentas on t.TargetAccountId equals c.Id
            where t.Status == TransferenciaCuentas.Registrada
            group t by t.TargetAccountId into g
            select new { Id = g.Key, Total = g.Sum(x => x.Amount) }
        ).ToDictionaryAsync(x => x.Id, x => x.Total);

        var salientes = await (
            from t in db.TransferenciasCuentas
            join c in cuentas on t.SourceAccountId equals c.Id
            where t.Status == TransferenciaCuentas.Registrada
            group t by t.SourceAccountId into g
            select new { Id = g.Key, Total = g.Sum(x => x.Amount) }
        ).ToDictionaryAsync(x => x.Id, x => x.Total);

        return iniciales.ToDictionary(c => c.Key, c =>
        {
            var m = movimientos.GetValueOrDefault(c.Key);
            return CalculoCuenta.SaldoActual(
                c.Value,
                m?.Ingresos ?? 0m,
                m?.Egresos ?? 0m,
                entrantes.GetValueOrDefault(c.Key),
                salientes.GetValueOrDefault(c.Key));
        });
    }

    /// <summary>
    /// Plata en caja de la empresa: la suma de sus cuentas activas. Una cuenta desactivada no
    /// entra, igual que antes en fn_proyeccion_caja y el resumen semanal.
    /// </summary>
    public async Task<decimal> CajaDeEmpresaAsync(long companyId)
    {
        var saldos = await DeCuentasAsync(db.CuentasFinancieras.Where(c => c.CompanyId == companyId && c.IsActive));
        return saldos.Values.Sum();
    }

    /// <summary>
    /// Pone al día el estado de los documentos según lo pagado, sus notas y la fecha de vencimiento
    /// (CalculoDocumento.Estado). Hace lo que antes
    /// encadenaban los triggers (trg_pago_recalcula, trg_documento_recalcula,
    /// trg_nota_recalcula_padre y trg_movimiento_estado): quien cambia un pago, una nota, un monto
    /// o un movimiento llama a esto después de guardar ese cambio, dentro de la misma
    /// transacción, y vuelve a guardar. Las sumas se leen de la base, así que el cambio ya tiene
    /// que estar guardado (aunque la transacción siga abierta).
    /// </summary>
    public async Task RecalcularEstadosAsync(IEnumerable<long> idsDocumento)
    {
        var ids = idsDocumento.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var saldos = await DeDocumentosAsync(ids);
        var documentos = await db.DocumentosFinancieros.Where(d => ids.Contains(d.Id)).ToListAsync();

        // "Hoy" de cada empresa: decide si un documento sin pagos ya quedó vencido.
        var idsEmpresa = documentos.Select(d => d.CompanyId).Distinct().ToList();
        var hoyPorEmpresa = (await db.Companies
                .Where(c => idsEmpresa.Contains(c.Id))
                .Select(c => new { c.Id, c.TimeZone })
                .ToListAsync())
            .ToDictionary(c => c.Id, c => HoyDe(c.TimeZone));

        foreach (var documento in documentos)
        {
            // Una nota no se cobra ni vence: su estado solo cambia al anularla.
            if (TiposDocumento.EsNota(documento.DocumentType))
            {
                continue;
            }

            var saldo = saldos[documento.Id];
            documento.Status = CalculoDocumento.Estado(
                documento.Status, saldo.NetAmount, saldo.PaidAmount, documento.DueDate, hoyPorEmpresa[documento.CompanyId]);
        }
    }

    /// <summary>Ingresos y egresos registrados de la empresa entre dos fechas (incluidas).</summary>
    public async Task<(decimal Ingresos, decimal Egresos)> FlujoEntreAsync(long companyId, DateOnly desde, DateOnly hasta)
    {
        var totales = await db.MovimientosFinancieros
            .Where(m => m.CompanyId == companyId
                        && m.Status == MovimientoFinanciero.Registrado
                        && m.MovementDate >= desde && m.MovementDate <= hasta)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Ingresos = g.Sum(m => m.Type == TiposMovimiento.Ingreso ? m.Amount : 0m),
                Egresos = g.Sum(m => m.Type == TiposMovimiento.Egreso ? m.Amount : 0m)
            })
            .FirstOrDefaultAsync();

        return (totales?.Ingresos ?? 0m, totales?.Egresos ?? 0m);
    }
}
