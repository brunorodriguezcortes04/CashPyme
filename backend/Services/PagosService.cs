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
/// Toda la regla vive acá (antes era el trigger fn_validar_pago): el documento y el movimiento se
/// bloquean con FOR UPDATE durante la transacción, así que dos pagos simultáneos se ordenan solos
/// y el segundo ya ve lo que pagó el primero. Sin ese bloqueo, los dos podrían pasar el control de
/// saldo y sobrepagar el documento entre los dos.
/// </summary>
public class PagosService(AppDbContext db, SaldosService saldos)
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
    /// Aplica un movimiento a un documento. Una cuenta por cobrar se paga con un ingreso y una
    /// por pagar con un egreso: el flujo del pago tiene que coincidir con los dos, y las FK
    /// compuestas de pago_documento lo exigen.
    /// </summary>
    public async Task<PagoResponse> CrearAsync(long companyId, long userId, CreatePagoRequest request)
    {
        return await db.EnTransaccionAsync(async () =>
        {
            // Siempre documento antes que movimiento, igual que al anular un movimiento: con el
            // orden cruzado, dos operaciones simultáneas podrían esperarse mutuamente.
            await db.BloquearAsync("documento_financiero", "id_documento", [request.IdDocumento]);
            await db.BloquearAsync("movimiento_financiero", "id_movimiento", [request.IdMovimiento]);

            var documento = await BuscarDocumentoAsync(companyId, request.IdDocumento);

            if (documento.Status == DocumentoFinanciero.Anulado)
            {
                throw new PagoDocumentoAnuladoException();
            }

            if (TiposDocumento.EsNota(documento.DocumentType))
            {
                throw new PagoSobreNotaException();
            }

            var flujo = TiposCuentaDocumento.FlujoDe(documento.AccountType);

            var movimiento = await db.MovimientosFinancieros
                .SingleOrDefaultAsync(m =>
                    m.Id == request.IdMovimiento
                    && m.CompanyId == companyId
                    && m.Type == flujo
                    && m.Status == MovimientoFinanciero.Registrado)
                ?? throw new MovimientoNoAplicableException();

            // El saldo ya descuenta lo pagado y netea las notas: con el monto bruto se podría
            // cobrar más de lo que el cliente debe cuando el documento tiene notas de crédito.
            var saldo = (await saldos.DeDocumentosAsync([documento.Id]))[documento.Id].OutstandingAmount;
            if (request.MontoAplicado > saldo)
            {
                throw new PagoExcedeSaldoException(saldo);
            }

            // Un ingreso de 100 puede cubrir dos facturas de 50, pero no tres.
            var aplicadoMovimiento = await db.PagosDocumento
                .Where(p => p.MovementId == movimiento.Id)
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
                FlowType = flujo,
                AppliedAmount = request.MontoAplicado
            };

            db.PagosDocumento.Add(pago);
            // uq_pago (id_documento, id_movimiento): el mismo movimiento no se aplica dos veces al
            // mismo documento. Para cambiar el monto se quita el pago y se crea otro.
            await db.SaveChangesAuditadoAsync(userId, () => new PagoDuplicadoException());

            await saldos.RecalcularEstadosAsync([documento.Id]);
            await db.SaveChangesAuditadoAsync(userId);

            return new PagoResponse(
                pago.Id, pago.DocumentId, pago.MovementId, pago.AppliedAmount,
                movimiento.MovementDate, movimiento.PaymentMethod, movimiento.Description);
        });
    }

    /// <summary>
    /// Quita un pago. Acá sí se borra la fila, a diferencia de documentos y movimientos: un pago
    /// no es un hecho económico propio, es un enlace entre dos que sí se conservan. Queda en la
    /// auditoría quién lo quitó, y el documento vuelve al estado que le corresponda (puede pasar
    /// de 'pagado' a 'pendiente').
    /// </summary>
    public async Task EliminarAsync(long companyId, long userId, long idPago)
    {
        await db.EnTransaccionAsync(async () =>
        {
            var pago = await db.PagosDocumento
                .SingleOrDefaultAsync(p => p.Id == idPago && p.CompanyId == companyId)
                ?? throw new PagoNoEncontradoException();

            await db.BloquearAsync("documento_financiero", "id_documento", [pago.DocumentId]);

            db.PagosDocumento.Remove(pago);
            await db.SaveChangesAuditadoAsync(userId);

            await saldos.RecalcularEstadosAsync([pago.DocumentId]);
            await db.SaveChangesAuditadoAsync(userId);
        });
    }

    private async Task<DocumentoFinanciero> BuscarDocumentoAsync(long companyId, long idDocumento)
        => await db.DocumentosFinancieros
               .SingleOrDefaultAsync(d => d.Id == idDocumento && d.CompanyId == companyId)
           ?? throw new DocumentoNoEncontradoException();
}
