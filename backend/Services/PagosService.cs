using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Aplica movimientos de caja ya registrados a documentos (pago_documento). No crea plata: el
/// movimiento tiene que existir antes. Un pago es el enlace entre "entró plata" y "esta factura
/// se cobró".
///
/// Casi toda la regla vive en fn_validar_pago, que bloquea documento y movimiento con FOR UPDATE
/// e impide sobrepagar o aplicar dos veces el mismo movimiento. Acá se valida antes para dar un
/// mensaje entendible, no para reemplazarla: con dos pagos simultáneos el que pierde la carrera
/// lo rechaza la base, no este código.
/// </summary>
public class PagosService(AppDbContext db)
{
    public async Task<IReadOnlyList<PagoResponse>> ListarDeDocumentoAsync(long companyId, long idDocumento)
    {
        _ = await BuscarDocumentoAsync(companyId, idDocumento);

        return await (
            from p in db.PagosDocumento
            join m in db.MovimientosFinancieros on p.MovementId equals m.Id
            where p.CompanyId == companyId
                  && p.DocumentId == idDocumento
                  && m.Status == MovimientoFinanciero.Registrado
            orderby m.MovementDate, p.Id
            select new PagoResponse(
                p.Id, p.DocumentId, p.MovementId, p.AppliedAmount,
                m.MovementDate, m.PaymentMethod, m.Description)
        ).ToListAsync();
    }

    /// <summary>
    /// Movimientos que todavía se pueden aplicar a un documento de este tipo de cuenta: del flujo
    /// que corresponde, registrados, y con monto sin aplicar. Alimenta el selector de cobro para
    /// que la persona no elija un movimiento ya ocupado y se coma un 409.
    /// </summary>
    public async Task<IReadOnlyList<MovimientoAplicableResponse>> ListarMovimientosAplicablesAsync(
        long companyId,
        string tipoCuenta)
    {
        var flujo = TiposCuentaDocumento.FlujoDe(tipoCuenta);

        return await (
            from m in db.MovimientosFinancieros
            join c in db.CuentasFinancieras on m.AccountId equals c.Id
            where m.CompanyId == companyId
                  && m.Type == flujo
                  && m.Status == MovimientoFinanciero.Registrado
            let aplicado = db.PagosDocumento
                .Where(p => p.MovementId == m.Id)
                .Sum(p => (decimal?)p.AppliedAmount) ?? 0m
            where m.Amount - aplicado > 0
            orderby m.MovementDate descending, m.Id descending
            select new MovimientoAplicableResponse(
                m.Id, m.MovementDate, m.Amount, m.Amount - aplicado,
                m.PaymentMethod, c.Name, m.Description)
        ).ToListAsync();
    }

    /// <summary>
    /// Aplica un movimiento a un documento. El tipo de flujo lo completa la base desde el
    /// documento, pero igual se comprueba acá: una cuenta por cobrar se paga con un ingreso y
    /// una por pagar con un egreso, y si no calza la FK compuesta reventaría como 500.
    /// </summary>
    public async Task<PagoResponse> CrearAsync(long companyId, long userId, CreatePagoRequest request)
    {
        var documento = await BuscarDocumentoAsync(companyId, request.IdDocumento);

        if (documento.Status == DocumentoFinanciero.Anulado)
        {
            throw new PagoDocumentoAnuladoException();
        }

        if (TiposDocumento.EsNota(documento.DocumentType))
        {
            throw new PagoSobreNotaException();
        }

        var flujoEsperado = TiposCuentaDocumento.FlujoDe(documento.AccountType);

        var movimiento = await db.MovimientosFinancieros
            .SingleOrDefaultAsync(m =>
                m.Id == request.IdMovimiento
                && m.CompanyId == companyId
                && m.Type == flujoEsperado
                && m.Status == MovimientoFinanciero.Registrado)
            ?? throw new MovimientoNoAplicableException();

        // El saldo sale de la vista, que ya descontó los pagos y neteó las notas de crédito y
        // débito. Usar monto_total a secas dejaría cobrar más de lo que el cliente debe cuando
        // el documento tiene notas: es el mismo tope que aplica fn_validar_pago.
        var saldo = await db.SaldosDocumento
            .Where(s => s.DocumentId == documento.Id)
            .Select(s => s.OutstandingAmount)
            .SingleOrDefaultAsync();

        if (request.MontoAplicado > saldo)
        {
            throw new PagoExcedeSaldoException(saldo);
        }

        // Y lo que le queda libre al movimiento: un ingreso de 100 puede cubrir dos facturas de
        // 50, pero no tres.
        var aplicadoMovimiento = await db.PagosDocumento
            .Where(p => p.MovementId == request.IdMovimiento)
            .SumAsync(p => (decimal?)p.AppliedAmount) ?? 0m;
        var disponible = movimiento.Amount - aplicadoMovimiento;
        if (request.MontoAplicado > disponible)
        {
            throw new MovimientoSinMontoDisponibleException(disponible);
        }

        var pago = new PagoDocumento
        {
            CompanyId = companyId,
            DocumentId = documento.Id,
            MovementId = movimiento.Id,
            AppliedAmount = request.MontoAplicado
        };

        db.PagosDocumento.Add(pago);
        // uq_pago (id_documento, id_movimiento): el mismo movimiento no se aplica dos veces al
        // mismo documento. Si hace falta cambiar el monto, se anula el pago y se crea otro.
        await db.SaveChangesAuditadoAsync(userId, () => new PagoDuplicadoException());

        return new PagoResponse(
            pago.Id, pago.DocumentId, pago.MovementId, pago.AppliedAmount,
            movimiento.MovementDate, movimiento.PaymentMethod, movimiento.Description);
    }

    /// <summary>
    /// Quita un pago. Acá sí se borra la fila, a diferencia de documentos y movimientos: un pago
    /// no es un hecho económico propio, es un enlace entre dos que sí se conservan. El rastro de
    /// quién lo quitó queda en la auditoría (trg_audit_pago), y trg_pago_recalcula recalcula el
    /// estado del documento, que puede volver de 'pagado' a 'pendiente'.
    /// </summary>
    public async Task EliminarAsync(long companyId, long userId, long idPago)
    {
        var pago = await db.PagosDocumento
            .SingleOrDefaultAsync(p => p.Id == idPago && p.CompanyId == companyId)
            ?? throw new PagoNoEncontradoException();

        db.PagosDocumento.Remove(pago);
        await db.SaveChangesAuditadoAsync(userId);
    }

    private async Task<DocumentoFinanciero> BuscarDocumentoAsync(long companyId, long idDocumento)
        => await db.DocumentosFinancieros
               .SingleOrDefaultAsync(d => d.Id == idDocumento && d.CompanyId == companyId)
           ?? throw new DocumentoNoEncontradoException();
}
