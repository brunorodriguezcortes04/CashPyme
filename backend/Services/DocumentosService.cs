using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Cuentas por cobrar y por pagar (documento_financiero), incluidas las notas de crédito y
/// débito que las corrigen. Toda la regla vive acá: antes la repetían los triggers de la base
/// (fn_validar_documento, fn_validar_nota y los de recálculo), que ya no existen.
///
/// El saldo que se devuelve sale de SaldosService, con la misma fórmula que usan la proyección,
/// las alertas y el saldo neto: la pantalla no puede mostrar un número distinto del que ellos
/// cuentan.
/// </summary>
public class DocumentosService(AppDbContext db, SaldosService saldos)
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
        int tamanoPagina = 20,
        string? situacion = null)
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

        if (SituacionesDocumento.EsValida(situacion))
        {
            query = await FiltrarPorSituacionAsync(query, companyId, situacion!);
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

        var saldosDoc = await saldos.DeDocumentosAsync(documentos.Select(d => d.Id).ToList());

        return new DocumentosPaginadosResponse(
            documentos.Select(d => ToResponse(d, saldosDoc[d.Id])).ToList(),
            total, pagina, tamanoPagina);
    }

    /// <summary>
    /// Mismo criterio que SituacionesDocumento.De, escrito para que lo traduzca Postgres y la
    /// paginación cuente bien: los estados abiertos son justo los que tienen saldo. Vencida se
    /// decide por la fecha y no por el estado 'vencido', para que entren también los que tienen
    /// abonos y los que el job todavía no alcanzó a marcar.
    /// </summary>
    private async Task<IQueryable<DocumentoFinanciero>> FiltrarPorSituacionAsync(
        IQueryable<DocumentoFinanciero> query, long companyId, string situacion)
    {
        query = query.Where(d =>
            d.DocumentType != TiposDocumento.NotaCredito && d.DocumentType != TiposDocumento.NotaDebito);

        if (situacion == SituacionesDocumento.Pagada)
        {
            return query.Where(d => d.Status == DocumentoFinanciero.Pagado);
        }

        var hoy = await saldos.HoyDeEmpresaAsync(companyId);
        var abiertos = query.Where(d =>
            d.Status == DocumentoFinanciero.Pendiente
            || d.Status == DocumentoFinanciero.PagadoParcial
            || d.Status == DocumentoFinanciero.Vencido);

        return situacion == SituacionesDocumento.Vencida
            ? abiertos.Where(d => d.DueDate < hoy)
            : abiertos.Where(d => d.DueDate >= hoy);
    }

    /// <summary>
    /// Cuántos documentos hay en cada grupo del listado y cuánto suman. Siempre devuelve los tres
    /// grupos, con 0 si alguno está vacío, para que la pantalla no tenga que completarlos.
    /// </summary>
    public async Task<IReadOnlyList<ResumenSituacionResponse>> ResumenAsync(long companyId, string tipoCuenta)
    {
        var consulta = db.DocumentosFinancieros.Where(d =>
            d.CompanyId == companyId
            && d.AccountType == tipoCuenta
            && d.Status != DocumentoFinanciero.Anulado
            && d.DocumentType != TiposDocumento.NotaCredito
            && d.DocumentType != TiposDocumento.NotaDebito);

        var documentos = await consulta.Select(d => new { d.Id, d.DocumentType, d.Status }).ToListAsync();
        var saldosDoc = await saldos.DeDocumentosAsync(consulta);

        var grupos = documentos
            .Select(d => new
            {
                Situacion = SituacionesDocumento.De(
                    d.DocumentType, d.Status, saldosDoc[d.Id].OutstandingAmount, saldosDoc[d.Id].IsOverdue),
                Saldo = saldosDoc[d.Id].OutstandingAmount
            })
            .Where(x => x.Situacion is not null)
            .GroupBy(x => x.Situacion!)
            .ToDictionary(g => g.Key, g => (Cantidad: g.Count(), Saldo: g.Sum(x => x.Saldo)));

        return SituacionesDocumento.Todas
            .Select(s => grupos.TryGetValue(s, out var g)
                ? new ResumenSituacionResponse(s, SituacionesDocumento.Etiqueta(s), g.Cantidad, g.Saldo)
                : new ResumenSituacionResponse(s, SituacionesDocumento.Etiqueta(s), 0, 0m))
            .ToList();
    }

    /// <summary>
    /// Terceros con los que se puede registrar este tipo de cuenta: activos y del tipo que sirve
    /// (cliente o 'ambos' para cobrar, proveedor o 'ambos' para pagar). Es la misma regla que se
    /// valida al guardar, así que el formulario nunca ofrece a alguien que después se rechaza.
    /// </summary>
    public async Task<IReadOnlyList<TerceroResponse>> ListarTercerosAsync(long companyId, string tipoCuenta)
    {
        var tipos = TiposCuentaDocumento.TiposTerceroQueSirven(tipoCuenta);

        return await db.Terceros
            .Where(t => t.CompanyId == companyId && t.IsActive && tipos.Contains(t.Type))
            .OrderBy(t => t.Name)
            .Select(t => new TerceroResponse(
                t.Id, t.Type, t.Name, t.Rut, t.BusinessActivity, t.Email, t.Phone, t.Address, t.IsActive))
            .ToListAsync();
    }

    /// <summary>
    /// Documentos a los que se les puede emitir una nota: del mismo tipo de cuenta, no anulados y
    /// que no sean notas. Con idTercero se limita a los de ese cliente o proveedor. Antes el front
    /// lo filtraba solo entre los documentos de la página que estaba mostrando.
    /// </summary>
    public async Task<IReadOnlyList<DocumentoCorregibleResponse>> ListarCorregiblesAsync(
        long companyId, string tipoCuenta, long? idTercero)
    {
        var consulta = db.DocumentosFinancieros.Where(d =>
            d.CompanyId == companyId
            && d.AccountType == tipoCuenta
            && d.Status != DocumentoFinanciero.Anulado
            && d.DocumentType != TiposDocumento.NotaCredito
            && d.DocumentType != TiposDocumento.NotaDebito
            && (idTercero == null || d.ThirdPartyId == idTercero));

        var saldosDoc = await saldos.DeDocumentosAsync(consulta);
        var documentos = await consulta
            .OrderBy(d => d.DueDate).ThenByDescending(d => d.Id)
            .Select(d => new { d.Id, d.DocumentType, d.DocumentNumber, d.ThirdPartyId, Tercero = d.ThirdParty.Name })
            .ToListAsync();

        return documentos
            .Select(d => new DocumentoCorregibleResponse(
                d.Id, d.DocumentType, d.DocumentNumber, d.ThirdPartyId, d.Tercero,
                saldosDoc[d.Id].OutstandingAmount))
            .ToList();
    }

    /// <summary>
    /// Registra un documento, sea una cuenta por cobrar/pagar o una nota que corrige a otra.
    /// Todo lo que la base impide con un CHECK o una FK se valida antes para responder 400/404/409
    /// con un mensaje que la persona entienda, en vez del 500 genérico que dejaría escapar la
    /// violación.
    /// </summary>
    public async Task<DocumentoResponse> CrearAsync(long companyId, long userId, CreateDocumentoRequest request)
    {
        return await db.EnTransaccionAsync(async () =>
        {
            var esNota = TiposDocumento.EsNota(request.TipoDocumento);

            var referencia = await ValidarReferenciaAsync(
                companyId, request.TipoDocumento, request.TipoCuenta, request.IdTercero,
                request.MontoTotal, request.IdDocumentoReferencia);

            // Una nota es del mismo tercero que el documento que corrige y no vence por su cuenta:
            // se le toma el tercero al padre y la fecha de vencimiento es la de emisión (la
            // columna es obligatoria y tiene que ser >= emisión).
            var idTercero = esNota ? referencia!.ThirdPartyId : request.IdTercero ?? throw new TerceroRequeridoException();
            var vencimiento = esNota
                ? request.FechaEmision
                : request.FechaVencimiento ?? throw new FechaVencimientoRequeridaException();

            ValidarFechasYMontos(
                request.FechaEmision, vencimiento, request.MontoNeto, request.MontoIva, request.MontoTotal);

            var tercero = await ValidarTerceroAsync(companyId, idTercero, request.TipoCuenta);
            var categoria = await ValidarCategoriaAsync(companyId, request.IdCategoria, request.TipoCuenta);

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
                DueDate = vencimiento,
                Description = Limpiar(request.Descripcion),
                CreatedByUserId = userId
            };

            db.DocumentosFinancieros.Add(documento);
            await db.SaveChangesAuditadoAsync(userId, () => new DocumentoDuplicadoException());

            // Una nota cambia el neto del documento que corrige: se pone al día su estado (puede
            // pasar a 'pagado' si la nota lo deja cubierto). Antes era trg_nota_recalcula_padre.
            // Un documento cargado con la fecha ya pasada nace 'vencido' en vez de esperar al job.
            await saldos.RecalcularEstadosAsync(referencia is not null ? [referencia.Id] : [documento.Id]);
            await db.SaveChangesAuditadoAsync(userId);

            documento.ThirdParty = tercero;
            documento.Category = categoria;
            return await ConSaldoAsync(documento);
        });
    }

    /// <summary>
    /// Edita un documento. Bajar MontoTotal es el mecanismo para reflejar una rebaja parcial sin
    /// emitir nota: el saldo es monto menos lo pagado y lo neteado por notas, así que baja solo.
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
        return await db.EnTransaccionAsync(async () =>
        {
            // Bloqueado: un pago o una nota sobre este documento esperan a que termine, y el
            // control de "no bajar de lo comprometido" ve los montos definitivos.
            await db.BloquearAsync("documento_financiero", "id_documento", [idDocumento]);
            var documento = await BuscarAsync(companyId, idDocumento);

            if (documento.Status == DocumentoFinanciero.Anulado)
            {
                throw new DocumentoYaAnuladoException();
            }

            if (TiposDocumento.EsNota(documento.DocumentType) || TiposDocumento.EsNota(request.TipoDocumento))
            {
                // Tampoco se convierte un documento en nota: quedaría sin referencia y rompería
                // ck_doc_nota_referencia. Una nota se emite, no se transforma.
                throw new NotaNoEditableException();
            }

            ValidarFechasYMontos(
                request.FechaEmision, request.FechaVencimiento,
                request.MontoNeto, request.MontoIva, request.MontoTotal);

            // El documento no puede valer menos de lo que ya tiene comprometido: lo cobrado más lo
            // acreditado en notas.
            var actual = (await saldos.DeDocumentosAsync([idDocumento]))[idDocumento];
            if (request.MontoTotal < actual.PaidAmount + actual.CreditNotesAmount)
            {
                throw new MontoMenorAComprometidoException(actual.PaidAmount, actual.CreditNotesAmount);
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

            // Si cambió el monto, el estado puede moverse (por ejemplo a 'pagado' si el monto nuevo
            // iguala lo pagado). Antes era trg_documento_recalcula.
            await saldos.RecalcularEstadosAsync([idDocumento]);
            await db.SaveChangesAuditadoAsync(userId);

            documento.ThirdParty = tercero;
            documento.Category = categoria;
            return await ConSaldoAsync(documento);
        });
    }

    /// <summary>
    /// "Eliminar" un documento es anularlo: la fila se conserva, su saldo pasa a 0 y sale solo
    /// de la proyección de caja; sus alertas se cierran en la próxima corrida del job.
    ///
    /// Con pagos registrados se rechaza y no hay forma de forzarlo, a diferencia de anular un
    /// movimiento: hay que anular primero esos movimientos.
    ///
    /// Anular una nota le devuelve al documento que corregía el saldo que la nota le quitaba.
    /// </summary>
    public async Task<DocumentoResponse> AnularAsync(long companyId, long userId, long idDocumento)
    {
        return await db.EnTransaccionAsync(async () =>
        {
            await db.BloquearAsync("documento_financiero", "id_documento", [idDocumento]);
            var documento = await BuscarAsync(companyId, idDocumento);

            if (documento.Status == DocumentoFinanciero.Anulado)
            {
                throw new DocumentoYaAnuladoException();
            }

            var cantidadPagos = await PagosVigentesAsync(idDocumento);
            if (cantidadPagos > 0)
            {
                throw new DocumentoConPagosAnularException(cantidadPagos);
            }

            documento.Status = DocumentoFinanciero.Anulado;
            await db.SaveChangesAuditadoAsync(userId);

            if (documento.ReferenceDocumentId is { } padre)
            {
                await saldos.RecalcularEstadosAsync([padre]);
                await db.SaveChangesAuditadoAsync(userId);
            }

            return await ConSaldoAsync(documento);
        });
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

        var saldosDoc = await saldos.DeDocumentosAsync(notas.Select(d => d.Id).ToList());
        return notas.Select(d => ToResponse(d, saldosDoc[d.Id])).ToList();
    }

    /// <summary>
    /// Una nota necesita un documento que corregir y el resto no puede tenerlo
    /// (ck_doc_nota_referencia). El documento padre se busca filtrando también por tipo de cuenta:
    /// una nota de una cuenta por cobrar no puede colgarse de una por pagar, y así simplemente "no
    /// existe" en vez de necesitar un error aparte.
    ///
    /// El padre queda bloqueado hasta el fin de la transacción: dos notas de crédito simultáneas
    /// sobre el mismo documento se ordenan, y la segunda ve lo que ya acreditó la primera. Sin el
    /// bloqueo, las dos podrían pasar el tope y dejar el documento sobreacreditado.
    /// </summary>
    private async Task<DocumentoFinanciero?> ValidarReferenciaAsync(
        long companyId,
        string tipoDocumento,
        string tipoCuenta,
        long? idTercero,
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

        await db.BloquearAsync("documento_financiero", "id_documento", [idReferencia.Value]);

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

        // La nota hereda el tercero del padre; si el request trae uno distinto es un error, no se
        // corrige en silencio.
        if (idTercero is not null && padre.ThirdPartyId != idTercero)
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

    private async Task<DocumentoResponse> ConSaldoAsync(DocumentoFinanciero documento)
        => ToResponse(documento, (await saldos.DeDocumentosAsync([documento.Id]))[documento.Id]);

    /// <summary>
    /// Pagos que de verdad cuentan: los de movimientos registrados. Un pago cuyo movimiento se
    /// anuló no cuenta, igual que en el saldo. Si acá se contara distinto, se rechazarían
    /// anulaciones válidas.
    /// </summary>
    private Task<int> PagosVigentesAsync(long idDocumento)
        => (from p in db.PagosDocumento
            join m in db.MovimientosFinancieros on p.MovementId equals m.Id
            where p.DocumentId == idDocumento && m.Status == MovimientoFinanciero.Registrado
            select p).CountAsync();

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
    /// cuenta (TiposCuentaDocumento.TiposTerceroQueSirven). La FK compuesta solo garantiza la
    /// empresa, no el tipo.
    /// </summary>
    private async Task<Tercero> ValidarTerceroAsync(long companyId, long idTercero, string tipoCuenta)
    {
        var tercero = await db.Terceros
            .SingleOrDefaultAsync(t => t.Id == idTercero && t.CompanyId == companyId && t.IsActive)
            ?? throw new TerceroNoEncontradoException();

        if (!TiposCuentaDocumento.TiposTerceroQueSirven(tipoCuenta).Contains(tercero.Type))
        {
            throw tipoCuenta == TiposCuentaDocumento.PorCobrar
                ? new TerceroNoEsClienteException()
                : new TerceroNoEsProveedorException();
        }

        return tercero;
    }

    /// <summary>
    /// La categoría es opcional. Si viene, tiene que ser de esta empresa y de un tipo que calce
    /// con el flujo del documento (ver fk_documento_categoria).
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

    private static DocumentoResponse ToResponse(DocumentoFinanciero d, SaldoDocumento saldo) => new(
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
        CalculoDocumento.EtiquetaEstado(d.DocumentType, d.Status, saldo.PaidAmount),
        d.Description,
        d.CreatedAtUtc,
        saldo.PaidAmount,
        saldo.CreditNotesAmount,
        saldo.DebitNotesAmount,
        saldo.OutstandingAmount,
        saldo.IsOverdue,
        saldo.DaysOverdue,
        d.ReferenceDocumentId,
        CalculoDocumento.Acciones(d.DocumentType, d.Status, saldo.OutstandingAmount),
        SituacionesDocumento.De(d.DocumentType, d.Status, saldo.OutstandingAmount, saldo.IsOverdue));
}
