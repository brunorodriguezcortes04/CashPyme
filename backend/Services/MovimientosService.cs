using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class MovimientosService(AppDbContext db, SaldosService saldos)
{
    public async Task<IReadOnlyList<CategoriaResponse>> ListCategoriasAsync(long companyId, string tipo)
    {
        return await db.CategoriasMovimiento
            .Where(c => c.CompanyId == companyId && c.Type == tipo && c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CategoriaResponse(c.Id, c.Name, c.Type))
            .ToListAsync();
    }

    /// <summary>
    /// Todos los filtros son opcionales y combinables. Si no se especifica ningún rango de
    /// fecha, se muestra el mes actual por defecto. Se filtra primero por empresa y
    /// fecha_movimiento (en ese orden) para que Postgres pueda usar ix_movimiento_empresa_fecha;
    /// sin coincidencias se devuelve una página vacía, nunca un error.
    /// </summary>
    public async Task<MovimientosPaginadosResponse> ListMovimientosAsync(
        long companyId,
        string? tipo,
        DateOnly? fechaDesde,
        DateOnly? fechaHasta,
        long? idCategoria,
        long? idCuenta,
        int pagina = 1,
        int tamanoPagina = 20)
    {
        pagina = pagina < 1 ? 1 : pagina;
        tamanoPagina = tamanoPagina is < 1 or > 100 ? 20 : tamanoPagina;

        // Un rango al revés devolvería una página vacía sin explicar por qué.
        if (fechaDesde is not null && fechaHasta is not null && fechaDesde > fechaHasta)
        {
            throw new RangoFechasInvalidoException();
        }

        // El "mes actual" es el de la empresa, en su zona horaria: con la fecha del servidor (UTC),
        // el último día del mes después de las ~21:00 en Chile ya se vería el mes siguiente.
        var mesEnCurso = ReglasMovimiento.MesEnCurso(await saldos.HoyDeEmpresaAsync(companyId));
        if (fechaDesde is null && fechaHasta is null)
        {
            (fechaDesde, fechaHasta) = mesEnCurso;
        }

        var query = db.MovimientosFinancieros
            .Where(m => m.CompanyId == companyId && m.Status == MovimientoFinanciero.Registrado);

        if (fechaDesde is not null)
        {
            query = query.Where(m => m.MovementDate >= fechaDesde.Value);
        }

        if (fechaHasta is not null)
        {
            query = query.Where(m => m.MovementDate <= fechaHasta.Value);
        }

        if (!string.IsNullOrWhiteSpace(tipo))
        {
            query = query.Where(m => m.Type == tipo);
        }

        if (idCategoria is not null)
        {
            query = query.Where(m => m.CategoryId == idCategoria.Value);
        }

        if (idCuenta is not null)
        {
            query = query.Where(m => m.AccountId == idCuenta.Value);
        }

        var total = await query.CountAsync();

        var movimientos = await query
            .Include(m => m.Account)
            .Include(m => m.Category)
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync();

        var idsMovimiento = movimientos.Select(m => m.Id).ToList();
        var conPagos = (await db.PagosDocumento
                .Where(p => idsMovimiento.Contains(p.MovementId))
                .Select(p => p.MovementId)
                .Distinct()
                .ToListAsync())
            .ToHashSet();

        return new MovimientosPaginadosResponse(
            movimientos.Select(m => ToResponse(m, conPagos.Contains(m.Id))).ToList(),
            total, pagina, tamanoPagina,
            fechaDesde, fechaHasta,
            PeriodoPorDefecto: (fechaDesde, fechaHasta) == mesEnCurso);
    }

    /// <summary>
    /// Registra un movimiento de caja ('ingreso' o 'egreso'). El signo sobre el saldo lo
    /// determina el tipo de movimiento (ver CalculoCuenta), no el monto: el CHECK monto > 0 aplica
    /// a ambos tipos. Las FK compuestas de cuenta/categoría ya protegen esto en la base; se
    /// valida antes para devolver 404/400 con un mensaje claro en vez de un 500 genérico.
    /// Un egreso que deja la cuenta en negativo se permite: el MVP no bloquea sobregiro.
    /// </summary>
    public async Task<MovimientoResponse> CrearMovimientoAsync(
        long companyId,
        long userId,
        CreateMovimientoRequest request)
    {
        var tipoMovimiento = request.TipoMovimiento;

        await ValidarReferenciasAsync(
            companyId, request.IdCuenta, request.IdCategoria, tipoMovimiento, request.IdTercero);

        var movimiento = new MovimientoFinanciero
        {
            CompanyId = companyId,
            AccountId = request.IdCuenta,
            CategoryId = request.IdCategoria,
            ThirdPartyId = request.IdTercero,
            Type = tipoMovimiento,
            Amount = request.Monto,
            MovementDate = request.Fecha,
            PaymentMethod = request.MedioPago,
            Description = request.Descripcion,
            CreatedByUserId = userId
        };

        db.MovimientosFinancieros.Add(movimiento);
        await db.SaveChangesAuditadoAsync(userId);

        return await ConAdvertenciaDeSobregiroAsync(await ToResponseConRelacionesAsync(movimiento));
    }

    /// <summary>
    /// Solo se puede editar un movimiento sin pagos aplicados: si tiene alguno en
    /// pago_documento, la trazabilidad del pago se rompería si el monto cambiara por
    /// debajo (o por arriba) de lo que ese pago dice que se aplicó, así que se bloquea
    /// la edición completa y solo queda la opción de anular.
    /// </summary>
    public async Task<MovimientoResponse> ActualizarMovimientoAsync(
        long companyId,
        long userId,
        long idMovimiento,
        UpdateMovimientoRequest request)
    {
        return await db.EnTransaccionAsync(async () =>
        {
            // Bloqueado mientras se edita: así un pago que se esté aplicando sobre este mismo
            // movimiento espera, y el control de abajo ve si quedó con pagos. Sin el bloqueo, el
            // pago podría entrar entre el control y el guardado (antes lo atajaba el trigger
            // fn_validar_movimiento).
            await db.BloquearAsync("movimiento_financiero", "id_movimiento", [idMovimiento]);
            var movimiento = await BuscarAsync(companyId, idMovimiento);

            if (movimiento.Status == MovimientoFinanciero.Anulado)
            {
                throw new MovimientoYaAnuladoException();
            }

            if (await TienePagosAplicadosAsync(idMovimiento))
            {
                throw new MovimientoConPagosEditException();
            }

            // El tipo no se puede cambiar al editar, así que la categoría se valida contra el que
            // el movimiento ya tiene, no contra uno que venga en el request.
            await ValidarReferenciasAsync(
                companyId, request.IdCuenta, request.IdCategoria, movimiento.Type, request.IdTercero);

            movimiento.AccountId = request.IdCuenta;
            movimiento.CategoryId = request.IdCategoria;
            movimiento.ThirdPartyId = request.IdTercero;
            // No hay saldo guardado que actualizar aparte: SaldosService lo calcula siempre a
            // partir de los movimientos registrados.
            movimiento.Amount = request.Monto;
            movimiento.MovementDate = request.Fecha;
            movimiento.PaymentMethod = request.MedioPago;
            movimiento.Description = request.Descripcion;

            await db.SaveChangesAuditadoAsync(userId);

            return await ConAdvertenciaDeSobregiroAsync(await ToResponseConRelacionesAsync(movimiento));
        });
    }

    /// <summary>
    /// "Eliminar" nunca borra la fila: pasa estado_movimiento a 'anulado' para conservar la
    /// trazabilidad (regla del diccionario de datos). Si el movimiento ya tiene pagos
    /// aplicados, la primera llamada (Confirmar = false) se rechaza con el detalle para que
    /// la persona decida; solo anula cuando Confirmar llega en true.
    ///
    /// Anular un movimiento que pagaba documentos les devuelve ese saldo: un pago cuyo movimiento
    /// está anulado no cuenta. Por eso, en la misma transacción, se recalcula el estado de esos
    /// documentos (antes lo hacía el trigger trg_movimiento_estado).
    /// </summary>
    public async Task<MovimientoResponse> AnularMovimientoAsync(
        long companyId,
        long userId,
        long idMovimiento,
        AnularMovimientoRequest request)
    {
        return await db.EnTransaccionAsync(async () =>
        {
            // Se bloquea siempre en el mismo orden que un pago: primero los documentos, después el
            // movimiento. Con el orden cruzado, un pago y una anulación simultáneos sobre el mismo
            // movimiento podían quedar esperándose mutuamente (deadlock). Un pago que entre en el
            // medio espera a que termine la anulación, y entonces lo ve anulado y se rechaza.
            await db.BloquearAsync("documento_financiero", "id_documento", await DocumentosPagadosAsync(idMovimiento));
            await db.BloquearAsync("movimiento_financiero", "id_movimiento", [idMovimiento]);
            var movimiento = await BuscarAsync(companyId, idMovimiento);

            if (movimiento.Status == MovimientoFinanciero.Anulado)
            {
                throw new MovimientoYaAnuladoException();
            }

            // Se vuelve a leer con el movimiento ya bloqueado: un pago que haya terminado justo
            // antes del bloqueo también cuenta (y ya soltó sus bloqueos, así que no hay espera).
            var documentosPagados = await DocumentosPagadosAsync(idMovimiento);

            if (documentosPagados.Count > 0 && !request.Confirmar)
            {
                throw new MovimientoConPagosAnularException(documentosPagados.Count);
            }

            movimiento.Status = MovimientoFinanciero.Anulado;
            movimiento.CanceledAtUtc = DateTime.UtcNow;
            movimiento.CanceledByUserId = userId;
            movimiento.CancellationReason = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();

            await db.SaveChangesAuditadoAsync(userId);

            if (documentosPagados.Count > 0)
            {
                await saldos.RecalcularEstadosAsync(documentosPagados);
                await db.SaveChangesAuditadoAsync(userId);
            }

            // Recién anulado: no admite más acciones, tenga o no pagos.
            return ToResponse(movimiento, tienePagosAplicados: false);
        });
    }

    /// <summary>
    /// Comprueba que cuenta, categoría y tercero existan, sean de esta empresa y estén activos.
    /// Las FK compuestas de la base ya lo garantizan; se valida antes para poder responder
    /// 404/400 con un mensaje que la persona entienda, en vez del 500 genérico que dejaría
    /// escapar una violación de FK.
    /// </summary>
    private async Task ValidarReferenciasAsync(
        long companyId,
        long idCuenta,
        long idCategoria,
        string tipoMovimiento,
        long? idTercero)
    {
        var cuentaValida = await db.CuentasFinancieras
            .AnyAsync(c => c.Id == idCuenta && c.CompanyId == companyId && c.IsActive);
        if (!cuentaValida)
        {
            throw new CuentaNoEncontradaException();
        }

        // La categoría tiene que ser del mismo tipo que el movimiento: una categoría de
        // egreso en un ingreso es una inconsistencia, no solo una referencia inexistente.
        var categoriaValida = await db.CategoriasMovimiento
            .AnyAsync(c => c.Id == idCategoria && c.CompanyId == companyId
                && c.Type == tipoMovimiento && c.IsActive);
        if (!categoriaValida)
        {
            throw new CategoriaInvalidaException();
        }

        // El tercero es opcional: solo se valida si vino uno.
        if (idTercero is not { } id)
        {
            return;
        }

        var terceroValido = await db.Terceros
            .AnyAsync(t => t.Id == id && t.CompanyId == companyId && t.IsActive);
        if (!terceroValido)
        {
            throw new TerceroNoEncontradoException();
        }
    }

    /// <summary>
    /// La respuesta lleva los nombres de cuenta y categoría, que no están en el movimiento
    /// recién guardado: hay que traerlos antes de mapear.
    /// </summary>
    private async Task<MovimientoResponse> ToResponseConRelacionesAsync(MovimientoFinanciero movimiento)
    {
        await db.Entry(movimiento).Reference(m => m.Account).LoadAsync();
        await db.Entry(movimiento).Reference(m => m.Category).LoadAsync();

        return ToResponse(movimiento, await TienePagosAplicadosAsync(movimiento.Id));
    }

    /// <summary>
    /// Después de crear o editar: si la cuenta quedó en negativo, la respuesta lo avisa
    /// (ReglasMovimiento.AdvertenciaSobregiro). El movimiento ya está guardado.
    /// </summary>
    private async Task<MovimientoResponse> ConAdvertenciaDeSobregiroAsync(MovimientoResponse movimiento)
    {
        var saldo = (await saldos.DeCuentasAsync(
            db.CuentasFinancieras.Where(c => c.Id == movimiento.IdCuenta)))[movimiento.IdCuenta];

        return movimiento with
        {
            Advertencia = ReglasMovimiento.AdvertenciaSobregiro(movimiento.NombreCuenta, saldo)
        };
    }

    private async Task<MovimientoFinanciero> BuscarAsync(long companyId, long idMovimiento)
    {
        return await db.MovimientosFinancieros
            .Include(m => m.Account)
            .Include(m => m.Category)
            .SingleOrDefaultAsync(m => m.Id == idMovimiento && m.CompanyId == companyId)
            ?? throw new MovimientoNoEncontradoException();
    }

    private Task<bool> TienePagosAplicadosAsync(long idMovimiento)
        => db.PagosDocumento.AnyAsync(p => p.MovementId == idMovimiento);

    private Task<List<long>> DocumentosPagadosAsync(long idMovimiento)
        => db.PagosDocumento
            .Where(p => p.MovementId == idMovimiento)
            .Select(p => p.DocumentId)
            .Distinct()
            .ToListAsync();

    private static MovimientoResponse ToResponse(MovimientoFinanciero m, bool tienePagosAplicados) => new(
        m.Id,
        m.Type,
        m.Amount,
        m.MovementDate,
        m.AccountId,
        m.Account.Name,
        m.CategoryId,
        m.Category.Name,
        m.PaymentMethod,
        m.Description,
        m.Status,
        ReglasMovimiento.EtiquetaEstado(m.Status),
        m.CanceledAtUtc,
        m.CreatedAtUtc,
        ReglasMovimiento.Acciones(m.Status, tienePagosAplicados)
    );
}