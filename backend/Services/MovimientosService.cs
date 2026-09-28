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

        if (fechaDesde is null && fechaHasta is null)
        {
            var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
            fechaDesde = new DateOnly(hoy.Year, hoy.Month, 1);
            fechaHasta = fechaDesde.Value.AddMonths(1).AddDays(-1);
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

        return new MovimientosPaginadosResponse(
            movimientos.Select(ToResponse).ToList(), total, pagina, tamanoPagina);
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

        return await ToResponseConRelacionesAsync(movimiento);
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

        // El tipo no se puede cambiar al editar, así que la categoría se valida contra el que
        // el movimiento ya tiene, no contra uno que venga en el request.
        await ValidarReferenciasAsync(
            companyId, request.IdCuenta, request.IdCategoria, movimiento.Type, request.IdTercero);

        movimiento.AccountId = request.IdCuenta;
        movimiento.CategoryId = request.IdCategoria;
        movimiento.ThirdPartyId = request.IdTercero;
        movimiento.Amount = request.Monto;
        movimiento.MovementDate = request.Fecha;
        movimiento.PaymentMethod = request.MedioPago;
        movimiento.Description = request.Descripcion;

        await db.SaveChangesAuditadoAsync(userId);

        return await ToResponseConRelacionesAsync(movimiento);
    }

    /// <summary>
    /// "Eliminar" nunca borra la fila: pasa estado_movimiento a 'anulado' para conservar la
    /// trazabilidad (regla del diccionario de datos). Si el movimiento ya tiene pagos
    /// aplicados, la primera llamada (Confirmar = false) se rechaza con el detalle para que
    /// la persona decida; solo anula cuando Confirmar llega en true.
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

        await db.SaveChangesAuditadoAsync(userId);

        return ToResponse(movimiento);
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