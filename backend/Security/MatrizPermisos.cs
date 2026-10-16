using Backend.Models;

namespace Backend.Security;

/// <summary>
/// Qué puede hacer cada rol. Sigue las descripciones sembradas en la tabla rol:
/// Administrador tiene acceso total; Contador gestiona movimientos y consulta la
/// configuración; Operador solo registra y consulta movimientos.
/// </summary>
public static class MatrizPermisos
{
    private static readonly Dictionary<string, Permiso[]> PorRol = new()
    {
        [Role.Administrator] =
        [
            Permiso.MovimientosVer,
            Permiso.MovimientosRegistrar,
            Permiso.CuentasGestionar,
            Permiso.ConfiguracionEmpresaVer,
            Permiso.ConfiguracionEmpresaEditar,
            Permiso.GestionUsuarios,
            Permiso.TercerosGestionar,
            Permiso.DocumentosVer,
            Permiso.DocumentosGestionar
        ],
        // Las cuentas por cobrar y por pagar son trabajo contable, así que el Contador las
        // gestiona completas. El Operador no las ve: su descripción sembrada en la tabla rol
        // lo limita a registrar y consultar movimientos de caja.
        [Role.Accountant] =
        [
            Permiso.MovimientosVer,
            Permiso.MovimientosRegistrar,
            Permiso.CuentasGestionar,
            Permiso.ConfiguracionEmpresaVer,
            Permiso.TercerosGestionar,
            Permiso.DocumentosVer,
            Permiso.DocumentosGestionar
        ],
        [Role.Operator] =
        [
            Permiso.MovimientosVer,
            Permiso.MovimientosRegistrar
        ]
    };

    public static bool Tiene(string rol, Permiso permiso)
        => PorRol.TryGetValue(rol, out var permisos) && permisos.Contains(permiso);

    public static IReadOnlyList<string> De(string rol)
        => PorRol.TryGetValue(rol, out var permisos)
            ? permisos.Select(p => p.ToString()).ToList()
            : [];
}
