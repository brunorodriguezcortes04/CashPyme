using Microsoft.AspNetCore.Authorization;

namespace Backend.Security;

/// <summary>
/// Resuelve el rol del usuario para la empresa activa en cada request. Si la membresía no
/// existe, está inactiva o el rol no tiene el permiso, no se llama a Succeed y ASP.NET
/// responde 403.
/// </summary>
public class PermisoAuthorizationHandler(RolEmpresaService rolEmpresaService)
    : AuthorizationHandler<RequierePermisoAttribute>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequierePermisoAttribute requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        long userId;
        long companyId;
        try
        {
            userId = context.User.GetUserId();
            companyId = context.User.GetCompanyId();
        }
        catch (InvalidOperationException)
        {
            // Token sin los claims esperados: se trata como no autorizado.
            return;
        }

        var rol = await rolEmpresaService.ObtenerRolAsync(userId, companyId);

        if (rol is not null && MatrizPermisos.Tiene(rol, requirement.Permiso))
        {
            context.Succeed(requirement);
        }
    }
}
