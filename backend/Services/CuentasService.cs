using Backend.Data;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class CuentasService(AppDbContext db, SaldosService saldos)
{
    /// <summary>
    /// El saldo actual no es una columna: lo calcula SaldosService a partir de lo registrado.
    /// Los selectores de movimientos piden solo las activas; la pantalla de cuentas, todas.
    /// </summary>
    public async Task<IReadOnlyList<CuentaResponse>> ListarAsync(long companyId, bool incluirInactivas)
    {
        var consulta = db.CuentasFinancieras
            .Where(c => c.CompanyId == companyId && (incluirInactivas || c.IsActive));

        var saldosCuenta = await saldos.DeCuentasAsync(consulta);
        var cuentas = await consulta.OrderBy(c => c.Name).ToListAsync();

        return cuentas.Select(c => ToResponse(c, saldosCuenta[c.Id])).ToList();
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

        db.CuentasFinancieras.Add(cuenta);
        await db.SaveChangesAuditadoAsync(userId, () => new CuentaDuplicadaException());

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

        await db.SaveChangesAuditadoAsync(userId, () => new CuentaDuplicadaException());

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

        await db.SaveChangesAuditadoAsync(userId, () => new CuentaDuplicadaException());

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
        var saldosCuenta = await saldos.DeCuentasAsync(db.CuentasFinancieras.Where(c => c.Id == idCuenta));
        return saldosCuenta[idCuenta];
    }

    private static CuentaResponse ToResponse(CuentaFinanciera c, decimal saldoActual) => new(
        c.Id, c.Name, c.Type, c.Bank, c.Number, c.InitialBalance, saldoActual, c.IsActive
    );
}
