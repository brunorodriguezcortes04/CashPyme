using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class MovimientosService(AppDbContext db)
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
    /// Todos los filtros son opcionales y combinables. Se filtra primero por empresa y
    /// fecha_movimiento (en ese orden) para que Postgres pueda usar ix_movimiento_empresa_fecha;
    /// sin coincidencias se devuelve una lista vacía, nunca un error.
    /// </summary>
    public async Task<IReadOnlyList<MovimientoResponse>> ListMovimientosAsync(
        long companyId,
        string? tipo,
        DateOnly? fechaDesde = null,
        DateOnly? fechaHasta = null,
        long? idCategoria = null,
        long? idCuenta = null)
    {
        var query = db.MovimientosFinancieros
            .Include(m => m.Account)
            .Include(m => m.Category)
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

        var movimientos = await query
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.Id)
            .ToListAsync();

        return movimientos.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Registra un movimiento de caja ('ingreso' o 'egreso'). El signo sobre el saldo lo
    /// determina tipo_movimiento en vista_saldo_cuenta, no el monto: el CHECK monto > 0 aplica
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

        var cuentaValida = await db.CuentasFinancieras
            .AnyAsync(c => c.Id == request.IdCuenta && c.CompanyId == companyId && c.IsActive);
        if (!cuentaValida)
        {
            throw new CuentaNoEncontradaException();
        }

        var categoriaValida = await db.CategoriasMovimiento
            .AnyAsync(c => c.Id == request.IdCategoria && c.CompanyId == companyId
                && c.Type == tipoMovimiento && c.IsActive);
        if (!categoriaValida)
        {
            throw new CategoriaInvalidaException();
        }

        if (request.IdTercero is { } idTercero)
        {
            var terceroValido = await db.Terceros
                .AnyAsync(t => t.Id == idTercero && t.CompanyId == companyId && t.IsActive);
            if (!terceroValido)
            {
                throw new TerceroNoEncontradoException();
            }
        }

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

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);

        db.MovimientosFinancieros.Add(movimiento);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await db.Entry(movimiento).Reference(m => m.Account).LoadAsync();
        await db.Entry(movimiento).Reference(m => m.Category).LoadAsync();

        return ToResponse(movimiento);
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
        var movimiento = await BuscarAsync(companyId, idMovimiento);

        if (movimiento.Status == MovimientoFinanciero.Anulado)
        {
            throw new MovimientoYaAnuladoException();
        }

        if (await TienePagosAplicadosAsync(idMovimiento))
        {
            throw new MovimientoConPagosEditException();
        }

        var cuentaValida = await db.CuentasFinancieras
            .AnyAsync(c => c.Id == request.IdCuenta && c.CompanyId == companyId && c.IsActive);
        if (!cuentaValida)
        {
            throw new CuentaNoEncontradaException();
        }

        var categoriaValida = await db.CategoriasMovimiento
            .AnyAsync(c => c.Id == request.IdCategoria && c.CompanyId == companyId
                && c.Type == movimiento.Type && c.IsActive);
        if (!categoriaValida)
        {
            throw new CategoriaInvalidaException();
        }

        if (request.IdTercero is { } idTercero)
        {
            var terceroValido = await db.Terceros
                .AnyAsync(t => t.Id == idTercero && t.CompanyId == companyId && t.IsActive);
            if (!terceroValido)
            {
                throw new TerceroNoEncontradoException();
            }
        }

        movimiento.AccountId = request.IdCuenta;
        movimiento.CategoryId = request.IdCategoria;
        movimiento.ThirdPartyId = request.IdTercero;
        // El signo sobre el saldo lo recalcula vista_saldo_cuenta al vuelo a partir de este
        // Amount: no hay ningún saldo guardado que actualizar aparte.
        movimiento.Amount = request.Monto;
        movimiento.MovementDate = request.Fecha;
        movimiento.PaymentMethod = request.MedioPago;
        movimiento.Description = request.Descripcion;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await db.Entry(movimiento).Reference(m => m.Account).LoadAsync();
        await db.Entry(movimiento).Reference(m => m.Category).LoadAsync();

        return ToResponse(movimiento);
    }

    /// <summary>
    /// "Eliminar" nunca borra la fila: pasa estado_movimiento a 'anulado' para conservar la
    /// trazabilidad (regla del diccionario de datos). Si el movimiento ya tiene pagos
    /// aplicados, la primera llamada (Confirmar = false) se rechaza con el detalle para que
    /// la persona decida; solo anula cuando Confirmar llega en true. El trigger
    /// trg_movimiento_estado se encarga de recalcular el estado de los documentos afectados.
    /// </summary>
    public async Task<MovimientoResponse> AnularMovimientoAsync(
        long companyId,
        long userId,
        long idMovimiento,
        AnularMovimientoRequest request)
    {
        var movimiento = await BuscarAsync(companyId, idMovimiento);

        if (movimiento.Status == MovimientoFinanciero.Anulado)
        {
            throw new MovimientoYaAnuladoException();
        }

        var cantidadPagos = await db.PagosDocumento.CountAsync(p => p.MovementId == idMovimiento);
        if (cantidadPagos > 0 && !request.Confirmar)
        {
            throw new MovimientoConPagosAnularException(cantidadPagos);
        }

        movimiento.Status = MovimientoFinanciero.Anulado;
        movimiento.CanceledAtUtc = DateTime.UtcNow;
        movimiento.CanceledByUserId = userId;
        movimiento.CancellationReason = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return ToResponse(movimiento);
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

    private static MovimientoResponse ToResponse(MovimientoFinanciero m) => new(
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
        m.CanceledAtUtc,
        m.CreatedAtUtc
    );
}