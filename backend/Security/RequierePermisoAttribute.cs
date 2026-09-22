using Microsoft.AspNetCore.Authorization;

namespace Backend.Security;

/// <summary>
/// Exige un permiso concreto sobre la empresa activa del token.
/// PermisoAuthorizationHandler resuelve el rol en cada request desde usuario_empresa,
/// por lo que el rol nunca viaja en el JWT y cambiar de empresa recalcula los permisos.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequierePermisoAttribute(Permiso permiso)
    : AuthorizeAttribute, IAuthorizationRequirement, IAuthorizationRequirementData
{
    public Permiso Permiso { get; } = permiso;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [this];
}
