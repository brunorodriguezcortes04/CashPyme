using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Cuentas por cobrar y por pagar (documento_financiero), incluidas las notas de crédito y
/// débito que las corrigen. Un documento nace 'pendiente' y la API no vuelve a escribir su
/// estado: lo mueve el trigger de la base cuando se le aplican pagos.
///
/// El saldo que se devuelve sale siempre de vista_documento_saldo, nunca de un cálculo en C#:
/// es la misma fuente que lee fn_proyeccion_caja, así que la pantalla no puede mostrar un
/// número distinto del que la proyección está contando.
/// </summary>
public class DocumentosService(AppDbContext db)
{
    /// <summary>
    /// Listado paginado. Se filtra primero por empresa y fecha_vencimiento para que Postgres
    /// pueda usar ix_documento_empresa_venc; sin coincidencias se devuelve una página vacía,
    /// nunca un error. Ordena por vencimiento ascendente a propósito: lo que vence primero es
    /// lo que hay que cobrar primero.
    /// </summary>
    public async Task<DocumentosPaginadosResponse> ListarAsync(
        long companyId,
        string tipoCuenta,
        string? estado,
        long? idTercero,
        int pagina = 1,
        int tamanoPagina = 20)
    {
        pagina = pagina < 1 ? 1 : pagina;
        tamanoPagina = tamanoPagina is < 1 or > 100 ? 20 : tamanoPagina;

        var query = db.DocumentosFinancieros
            .Where(d => d.CompanyId == companyId && d.AccountType == tipoCuenta);

        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(d => d.Status == estado);
        }

        if (idTercero is not null)
        {
            query = query.Where(d => d.ThirdPartyId == idTercero.Value);
        }

        var total = await query.CountAsync();

        var documentos = await query
            .Include(d => d.ThirdParty)
            .Include(d => d.Category)
            .OrderBy(d => d.DueDate)
            .ThenByDescending(d => d.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync();

        var saldos = await SaldosDeAsync(documentos.Select(d => d.Id).ToList());

        return new DocumentosPaginadosResponse(
            documentos.Select(d => ToResponse(d, saldos.GetValueOrDefault(d.Id))).ToList(),
            total, pagina, tamanoPagina);
    }

    /// <summary>
    /// Registra un documento, sea una cuenta por cobrar/pagar o una nota que corrige a otra.
    /// Todo lo que la base impide con un CHECK, una FK o un trigger se valida antes para
    /// responder 400/404/409 con un mensaje que la persona entienda, en vez del 500 genérico
    /// que dejaría escapar la violación.
    /// </summary>
    public async Task<DocumentoResponse> CrearAsync(
        long companyId,
        long userId,
        CreateDocumentoRequest request)
    {
        ValidarFechasYMontos(
            request.FechaEmision, request.FechaVencimiento,
            request.MontoNeto, request.MontoIva, request.MontoTotal);

        var tercero = await ValidarTerceroAsync(companyId, request.IdTercero, request.TipoCuenta);
        var categoria = await ValidarCategoriaAsync(companyId, request.IdCategoria, request.TipoCuenta);

        var referencia = await ValidarReferenciaAsync(
            companyId, request.TipoDocumento, request.TipoCuenta,
            request.IdTercero, request.MontoTotal, request.IdDocumentoReferencia);

        var documento = new DocumentoFinanciero
        {
            CompanyId = companyId,
            ThirdPartyId = tercero.Id,
            CategoryId = categoria?.Id,
            ReferenceDocumentId = referencia?.Id,
            AccountType = request.TipoCuenta,
            DocumentType = request.TipoDocumento,
            DocumentNumber = Limpiar(request.NumeroDocumento),
            NetAmount = request.MontoNeto,
            VatAmount = request.MontoIva,
            TotalAmount = request.MontoTotal,
            IssueDate = request.FechaEmision,
            DueDate = request.FechaVencimiento,
            Description = Limpiar(request.Descripcion),
            CreatedByUserId = userId
        };

        db.DocumentosFinancieros.Add(documento);
        await db.SaveChangesAuditadoAsync(userId, () => new DocumentoDuplicadoException());

        // Las relaciones se asignan a mano en vez de releer la fila: ya se cargaron al validar.
        documento.ThirdParty = tercero;
        documento.Category = categoria;

        return ToResponse(documento, await SaldoDeAsync(documento.Id));
    }

    /// <summary>
    /// Edita un documento. Bajar MontoTotal es el mecanismo para reflejar una rebaja parcial
    /// cuando no se quiere emitir una nota: vista_documento_saldo calcula
    /// saldo_pendiente = monto_total - pagado - notas_credito + notas_debito.
    ///
    /// Una nota no se edita: cambiarle el monto movería el saldo del documento que corrige sin
    /// dejar rastro de que la nota original decía otra cosa, y una nota es un documento
    /// tributario, no un borrador. Se anula y se emite otra.
    /// </summary>
    public async Task<DocumentoResponse> ActualizarAsync(
        long companyId,
        long userId,
        long idDocumento,
        UpdateDocumentoRequest request)
    {
        var documento = await BuscarAsync(companyId, idDocumento);

        if (documento.Status == DocumentoFinanciero.Anulado)
        {
            throw new DocumentoYaAnuladoException();
        }

        if (TiposDocumento.EsNota(documento.DocumentType))
        {
            throw new NotaNoEditableException();
        }

        if (TiposDocumento.EsNota(request.TipoDocumento))
        {
            // Convertir un documento normal en nota lo dejaría sin referencia y rompería
            // ck_doc_nota_referencia. Una nota se emite, no se transforma.
            throw new NotaNoEditableException();
        }

        ValidarFechasYMontos(
            request.FechaEmision, request.FechaVencimiento,
            request.MontoNeto, request.MontoIva, request.MontoTotal);

        // El documento no puede valer menos de lo que ya tiene comprometido. Lo pagado lo
        // rechaza también trg_documento_validar; lo acreditado en notas solo se controla acá.
        var saldoActual = await SaldoDeAsync(idDocumento);
        var pagado = saldoActual?.PaidAmount ?? 0m;
        var notasCredito = saldoActual?.CreditNotesAmount ?? 0m;
        if (request.MontoTotal < pagado + notasCredito)
        {
            throw new MontoMenorAComprometidoException(pagado, notasCredito);
        }

        // El tipo de cuenta no cambia al editar, así que el tercero y la categoría se validan
        // contra el que el documento ya tiene.
        var tercero = await ValidarTerceroAsync(companyId, request.IdTercero, documento.AccountType);
        var categoria = await ValidarCategoriaAsync(companyId, request.IdCategoria, documento.AccountType);

        documento.ThirdPartyId = tercero.Id;
        documento.CategoryId = categoria?.Id;
        documento.DocumentType = request.TipoDocumento;
        documento.DocumentNumber = Limpiar(request.NumeroDocumento);
        documento.NetAmount = request.MontoNeto;
        documento.VatAmount = request.MontoIva;
        documento.TotalAmount = request.MontoTotal;
        documento.IssueDate = request.FechaEmision;
        documento.DueDate = request.FechaVencimiento;
        documento.Description = Limpiar(request.Descripcion);

        await db.SaveChangesAuditadoAsync(userId, () => new DocumentoDuplicadoException());

        // Si cambió el monto, trg_documento_recalcula ya movió estado_documento en la base
        // (puede pasar a 'pagado' si el monto nuevo iguala lo pagado). Se relee para no
        // devolver el estado viejo que quedó en memoria.
        await db.Entry(documento).ReloadAsync();
        documento.ThirdParty = tercero;
        documento.Category = categoria;

        return ToResponse(documento, await SaldoDeAsync(idDocumento));
    }

    /// <summary>
    /// "Eliminar" un documento es anularlo: la fila se conserva y vista_documento_saldo le pone
    /// saldo_pendiente en 0, así que sale solo de la proyección de caja y sus alertas se
    /// resuelven. Quién lo anuló queda en la auditoría (trg_audit_documento): la tabla no tiene
    /// columnas de anulación propias.
    ///
    /// Con pagos registrados se rechaza y no hay forma de forzarlo, a diferencia de anular un
    /// movimiento: hay que anular primero esos movimientos.
    ///
    /// Anular una nota devuelve al documento que corregía el saldo que la nota le había
    /// quitado, porque la vista solo suma notas que no estén anuladas.
    /// </summary>
    public async Task<DocumentoResponse> AnularAsync(long companyId, long userId, long idDocumento)
    {
        var documento = await BuscarAsync(companyId, idDocumento);

        if (documento.Status == DocumentoFinanciero.Anulado)
        {
            throw new DocumentoYaAnuladoException();
        }

        var (cantidadPagos, _) = await PagosVigentesAsync(idDocumento);
        if (cantidadPagos > 0)
        {
            throw new DocumentoConPagosAnularException(cantidadPagos);
        }

        documento.Status = DocumentoFinanciero.Anulado;
        await db.SaveChangesAuditadoAsync(userId);

        return ToResponse(documento, await SaldoDeAsync(idDocumento));
    }

    /// <summary>Notas emitidas sobre un documento, para mostrar de dónde sale su saldo.</summary>
    public async Task<IReadOnlyList<DocumentoResponse>> ListarNotasAsync(long companyId, long idDocumento)
    {
        var notas = await db.DocumentosFinancieros
            .Include(d => d.ThirdParty)
            .Include(d => d.Category)
            .Where(d => d.CompanyId == companyId && d.ReferenceDocumentId == idDocumento)
            .OrderBy(d => d.Id)
            .ToListAsync();

        var saldos = await SaldosDeAsync(notas.Select(d => d.Id).ToList());
        return notas.Select(d => ToResponse(d, saldos.GetValueOrDefault(d.Id))).ToList();
    }

    /// <summary>
    /// Una nota necesita un documento que corregir y el resto no puede tenerlo
    /// (ck_doc_nota_referencia). El documento padre se busca filtrando también por tipo de
    /// cuenta: una nota de una cuenta por cobrar no puede colgarse de una por pagar, y así
    /// simplemente "no existe" en vez de necesitar un error aparte.
    ///
    /// Todo esto lo vuelve a comprobar fn_validar_nota en la base, con FOR UPDATE sobre el
    /// padre; acá se adelanta para dar el mensaje bueno.
    /// </summary>
    private async Task<DocumentoFinanciero?> ValidarReferenciaAsync(
        long companyId,
        string tipoDocumento,
        string tipoCuenta,
        long idTercero,
        decimal montoTotal,
        long? idReferencia)
    {
        if (!TiposDocumento.EsNota(tipoDocumento))
        {
            if (idReferencia is not null)
            {
                throw new ReferenciaEnDocumentoNormalException();
            }

            return null;
        }

        if (idReferencia is null)
        {
            throw new NotaSinReferenciaException();
        }

        var padre = await db.DocumentosFinancieros
            .SingleOrDefaultAsync(d =>
                d.Id == idReferencia.Value
                && d.CompanyId == companyId
                && d.AccountType == tipoCuenta)
            ?? throw new DocumentoReferenciadoNoEncontradoException();

        if (TiposDocumento.EsNota(padre.DocumentType))
        {
            throw new NotaSobreNotaException();
        }

        if (padre.Status == DocumentoFinanciero.Anulado)
        {
            throw new NotaSobreDocumentoAnuladoException();
        }

        if (padre.ThirdPartyId != idTercero)
        {
            throw new NotaDeOtroTerceroException();
        }

        // Una nota de débito sube el saldo y no tiene tope. Una de crédito no puede dejarlo
        // negativo: eso sería plata que la empresa le debe al cliente, que este MVP no modela.
        if (tipoDocumento == TiposDocumento.NotaCredito)
        {
            var acreditado = await db.DocumentosFinancieros
                .Where(d => d.ReferenceDocumentId == padre.Id
                            && d.DocumentType == TiposDocumento.NotaCredito
                            && d.Status != DocumentoFinanciero.Anulado)
                .SumAsync(d => (decimal?)d.TotalAmount) ?? 0m;

            var disponible = padre.TotalAmount - acreditado;
            if (montoTotal > disponible)
            {
                throw new NotasCreditoExcedenDocumentoException(disponible);
            }
        }

        return padre;
    }

    private async Task<DocumentoFinanciero> BuscarAsync(long companyId, long idDocumento)
    {
        return await db.DocumentosFinancieros
            .Include(d => d.ThirdParty)
            .Include(d => d.Category)
            .SingleOrDefaultAsync(d => d.Id == idDocumento && d.CompanyId == companyId)
            ?? throw new DocumentoNoEncontradoException();
    }

    private async Task<SaldoDocumento?> SaldoDeAsync(long idDocumento)
        => await db.SaldosDocumento.SingleOrDefaultAsync(s => s.DocumentId == idDocumento);

    private async Task<Dictionary<long, SaldoDocumento>> SaldosDeAsync(List<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.SaldosDocumento
            .Where(s => ids.Contains(s.DocumentId))
            .ToDictionaryAsync(s => s.DocumentId);
    }

    /// <summary>
    /// Pagos que de verdad cuentan: los de movimientos registrados. Un pago cuyo movimiento se
    /// anuló no cuenta, y así lo mira también la base (fn_validar_documento y
    /// vista_documento_saldo filtran por estado_movimiento = 'registrado'). Si acá se contara
    /// distinto, se rechazarían anulaciones que la base sí permite.
    /// </summary>
    private async Task<(int Cantidad, decimal Total)> PagosVigentesAsync(long idDocumento)
    {
        var pagos = from p in db.PagosDocumento
                    join m in db.MovimientosFinancieros on p.MovementId equals m.Id
                    where p.DocumentId == idDocumento && m.Status == MovimientoFinanciero.Registrado
                    select p.AppliedAmount;

        return (await pagos.CountAsync(), await pagos.SumAsync(monto => (decimal?)monto) ?? 0m);
    }

    /// <summary>Replica ck_doc_fechas y ck_doc_iva para no devolver un 500 desde un CHECK.</summary>
    private static void ValidarFechasYMontos(
        DateOnly fechaEmision,
        DateOnly fechaVencimiento,
        decimal? montoNeto,
        decimal montoIva,
        decimal montoTotal)
    {
        if (fechaVencimiento < fechaEmision)
        {
            throw new FechasDocumentoInvalidasException();
        }

        if (montoNeto is not null && montoNeto.Value + montoIva != montoTotal)
        {
            throw new MontosDocumentoInconsistentesException();
        }
    }

    private static string? Limpiar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// El tercero tiene que existir en esta empresa, estar activo y servir para el tipo de
    /// cuenta: 'cliente' o 'ambos' para una por cobrar, 'proveedor' o 'ambos' para una por
    /// pagar. La FK compuesta solo garantiza la empresa, no el tipo.
    /// </summary>
    private async Task<Tercero> ValidarTerceroAsync(long companyId, long idTercero, string tipoCuenta)
    {
        var tercero = await db.Terceros
            .SingleOrDefaultAsync(t => t.Id == idTercero && t.CompanyId == companyId && t.IsActive)
            ?? throw new TerceroNoEncontradoException();

        var sirve = tipoCuenta == TiposCuentaDocumento.PorCobrar
            ? tercero.Type is "cliente" or "ambos"
            : tercero.Type is "proveedor" or "ambos";

        if (!sirve)
        {
            throw tipoCuenta == TiposCuentaDocumento.PorCobrar
                ? new TerceroNoEsClienteException()
                : new TerceroNoEsProveedorException();
        }

        return tercero;
    }

    /// <summary>
    /// La categoría es opcional. Si viene, tiene que ser de esta empresa y de un tipo que
    /// calce con el tipo_flujo que la base va a generar (ver fk_documento_categoria).
    /// </summary>
    private async Task<CategoriaMovimiento?> ValidarCategoriaAsync(
        long companyId,
        long? idCategoria,
        string tipoCuenta)
    {
        if (idCategoria is null)
        {
            return null;
        }

        var flujo = TiposCuentaDocumento.FlujoDe(tipoCuenta);

        return await db.CategoriasMovimiento
            .SingleOrDefaultAsync(c =>
                c.Id == idCategoria.Value
                && c.CompanyId == companyId
                && c.Type == flujo
                && c.IsActive)
            ?? throw new CategoriaInvalidaException();
    }

    /// <summary>
    /// El saldo sale de la vista. El respaldo (saldo null) solo aplica si la vista no trajo la
    /// fila, que no debería pasar: se arma el peor caso honesto en vez de inventar un saldo.
    /// </summary>
    private static DocumentoResponse ToResponse(DocumentoFinanciero d, SaldoDocumento? saldo) => new(
        d.Id,
        d.AccountType,
        d.DocumentType,
        d.DocumentNumber,
        d.ThirdPartyId,
        d.ThirdParty.Name,
        d.CategoryId,
        d.Category?.Name,
        d.NetAmount,
        d.VatAmount,
        d.TotalAmount,
        d.IssueDate,
        d.DueDate,
        d.Status,
        d.Description,
        d.CreatedAtUtc,
        saldo?.PaidAmount ?? 0m,
        saldo?.CreditNotesAmount ?? 0m,
        saldo?.DebitNotesAmount ?? 0m,
        saldo?.OutstandingAmount ?? SaldoDeRespaldo(d),
        saldo?.IsOverdue ?? false,
        saldo?.DaysOverdue ?? 0,
        d.ReferenceDocumentId);

    private static decimal SaldoDeRespaldo(DocumentoFinanciero d)
        => d.Status == DocumentoFinanciero.Anulado || TiposDocumento.EsNota(d.DocumentType)
            ? 0m
            : d.TotalAmount;
}
