using Backend.Data;
using Backend.Dtos;
using Backend.Security;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public class PantallasService(AppDbContext db, RolEmpresaService rolEmpresaService)
{
    /// <summary>
    /// Pantallas que el usuario puede ver en la empresa activa: activas en la tabla y cuyo
    /// permiso tiene su rol. Una pantalla con un permiso_requerido que no existe en el enum
    /// Permiso se descarta (falla cerrado) en vez de quedar visible para todos.
    /// </summary>
    public async Task<IReadOnlyList<PantallaResponse>> ListarAsync(long userId, long companyId)
    {
        var rol = await rolEmpresaService.ObtenerRolAsync(userId, companyId);
        if (rol is null)
        {
            return [];
        }

        var pantallas = await db.Pantallas
            .Where(p => p.IsActive)
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Name)
            .ToListAsync();

        return pantallas
            .Where(p => PuedeVer(rol, p.RequiredPermission))
            .Select(p => new PantallaResponse(p.Code, p.Name, p.Route, p.Order))
            .ToList();
    }

    private static bool PuedeVer(string rol, string? permisoRequerido)
    {
        if (string.IsNullOrWhiteSpace(permisoRequerido))
        {
            return true;
        }

        return Enum.TryParse<Permiso>(permisoRequerido, out var permiso)
            && MatrizPermisos.Tiene(rol, permiso);
    }
}
