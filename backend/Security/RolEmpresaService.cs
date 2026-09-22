using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Security;

/// <summary>
/// Rol del usuario en la empresa activa, leído desde usuario_empresa en cada request.
/// Es el único lugar donde se resuelve el rol: lo usan la autorización de endpoints y
/// el filtrado de pantallas, para que no puedan quedar desalineados.
/// </summary>
public class RolEmpresaService(AppDbContext db)
{
    public async Task<string?> ObtenerRolAsync(long userId, long companyId)
    {
        return await db.CompanyMemberships
            .Where(m => m.UserId == userId && m.CompanyId == companyId && m.IsActive && m.Company.IsActive)
            .Select(m => m.Role.Name)
            .SingleOrDefaultAsync();
    }
}
