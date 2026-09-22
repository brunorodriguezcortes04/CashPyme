using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Services;

public class CuentasService(AppDbContext db)
{
    /// <summary>
    /// El saldo actual sale de vista_saldo_cuenta, no de una columna: por eso el join.
    /// Los selectores de movimientos piden solo las activas; la pantalla de cuentas, todas.
    /// </summary>
    public async Task<IReadOnlyList<CuentaResponse>> ListarAsync(long companyId, bool incluirInactivas)
    {
        return await (
            from c in db.CuentasFinancieras
            join s in db.SaldosCuenta on c.Id equals s.AccountId
            where c.CompanyId == companyId && (incluirInactivas || c.IsActive)
            orderby c.Name
            select new CuentaResponse(
                c.Id, c.Name, c.Type, c.Bank, c.Number, c.InitialBalance, s.CurrentBalance, c.IsActive)
        ).ToListAsync();
    }

    public async Task<CuentaResponse> CrearAsync(long companyId, long userId, CreateCuentaRequest request)
    {
        var cuenta = new CuentaFinanciera
        {
            CompanyId = companyId,
            Name = request.NombreCuenta.Trim(),
            Type = request.TipoCuenta,
            Bank = request.Banco?.Trim(),
            Number = request.NumeroCuenta?.Trim(),
            InitialBalance = request.SaldoInicial
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);

        db.CuentasFinancieras.Add(cuenta);
        await GuardarAsync();
        await transaction.CommitAsync();

        // Cuenta recién creada: aún no tiene movimientos, el saldo actual es el inicial.
        return ToResponse(cuenta, cuenta.InitialBalance);
    }

    public async Task<CuentaResponse> ActualizarAsync(
        long companyId, long userId, long idCuenta, UpdateCuentaRequest request)
    {
        var cuenta = await BuscarAsync(companyId, idCuenta);

        cuenta.Name = request.NombreCuenta.Trim();
        cuenta.Type = request.TipoCuenta;
        cuenta.Bank = request.Banco?.Trim();
        cuenta.Number = request.NumeroCuenta?.Trim();
        cuenta.InitialBalance = request.SaldoInicial;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);

        await GuardarAsync();
        await transaction.CommitAsync();

        return ToResponse(cuenta, await SaldoActualAsync(idCuenta));
    }

    /// <summary>
    /// No hay borrado físico: las FK del modelo son RESTRICT y una cuenta con movimientos no
    /// se puede eliminar. Desactivarla la saca de los selectores sin tocar su historial.
    /// </summary>
    public async Task<CuentaResponse> CambiarEstadoAsync(long companyId, long userId, long idCuenta, bool activo)
    {
        var cuenta = await BuscarAsync(companyId, idCuenta);
        cuenta.IsActive = activo;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);

        await GuardarAsync();
        await transaction.CommitAsync();

        return ToResponse(cuenta, await SaldoActualAsync(idCuenta));
    }

    private async Task<CuentaFinanciera> BuscarAsync(long companyId, long idCuenta)
    {
        return await db.CuentasFinancieras
            .SingleOrDefaultAsync(c => c.Id == idCuenta && c.CompanyId == companyId)
            ?? throw new CuentaNoEncontradaException();
    }

    private async Task<decimal> SaldoActualAsync(long idCuenta)
    {
        return await db.SaldosCuenta
            .Where(s => s.AccountId == idCuenta)
            .Select(s => s.CurrentBalance)
            .SingleAsync();
    }

    private async Task GuardarAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // uq_cuenta_nombre: mismo nombre de cuenta dentro de la empresa.
            throw new CuentaDuplicadaException();
        }
    }

    private static CuentaResponse ToResponse(CuentaFinanciera c, decimal saldoActual) => new(
        c.Id, c.Name, c.Type, c.Bank, c.Number, c.InitialBalance, saldoActual, c.IsActive
    );
}
