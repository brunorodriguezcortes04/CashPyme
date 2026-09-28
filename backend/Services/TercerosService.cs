using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Services;

public class TercerosService(AppDbContext db)
{
    /// <summary>
    /// Los selectores de movimientos piden solo los activos; la pantalla de terceros, todos.
    /// </summary>
    public async Task<IReadOnlyList<TerceroResponse>> ListarAsync(long companyId, bool incluirInactivos)
    {
        return await db.Terceros
            .Where(t => t.CompanyId == companyId && (incluirInactivos || t.IsActive))
            .OrderBy(t => t.Name)
            .Select(t => new TerceroResponse(
                t.Id, t.Type, t.Name, t.Rut, t.BusinessActivity, t.Email, t.Phone, t.Address, t.IsActive))
            .ToListAsync();
    }

    public async Task<TerceroResponse> CrearAsync(long companyId, CreateTerceroRequest request)
    {
        var tercero = new Tercero
        {
            CompanyId = companyId,
            Type = request.TipoTercero,
            Name = request.NombreRazonSocial.Trim(),
            Rut = NormalizarRut(request.Rut),
            BusinessActivity = request.Giro?.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Telefono?.Trim(),
            Address = request.Direccion?.Trim()
        };

        db.Terceros.Add(tercero);
        await GuardarAsync();

        return ToResponse(tercero);
    }

    public async Task<TerceroResponse> ActualizarAsync(long companyId, long idTercero, UpdateTerceroRequest request)
    {
        var tercero = await BuscarAsync(companyId, idTercero);

        tercero.Type = request.TipoTercero;
        tercero.Name = request.NombreRazonSocial.Trim();
        tercero.Rut = NormalizarRut(request.Rut);
        tercero.BusinessActivity = request.Giro?.Trim();
        tercero.Email = request.Email?.Trim();
        tercero.Phone = request.Telefono?.Trim();
        tercero.Address = request.Direccion?.Trim();

        await GuardarAsync();
        return ToResponse(tercero);
    }

    private static string? NormalizarRut(string? rut)
    => string.IsNullOrWhiteSpace(rut) ? null : Rut.Normalizar(rut);

    /// <summary>
    /// No hay borrado físico: un tercero puede tener movimientos y documentos. Desactivarlo
    /// lo saca de los selectores sin tocar su historial.
    /// </summary>
    public async Task<TerceroResponse> CambiarEstadoAsync(long companyId, long idTercero, bool activo)
    {
        var tercero = await BuscarAsync(companyId, idTercero);
        tercero.IsActive = activo;

        await GuardarAsync();
        return ToResponse(tercero);
    }

    private async Task<Tercero> BuscarAsync(long companyId, long idTercero)
    {
        return await db.Terceros
            .SingleOrDefaultAsync(t => t.Id == idTercero && t.CompanyId == companyId)
            ?? throw new TerceroNoEncontradoException();
    }

    private async Task GuardarAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
                  { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_tercero_rut" })
        {
            throw new TerceroRutDuplicadoException();
        }
    }

    private static TerceroResponse ToResponse(Tercero t) => new(
        t.Id, t.Type, t.Name, t.Rut, t.BusinessActivity, t.Email, t.Phone, t.Address, t.IsActive);
}