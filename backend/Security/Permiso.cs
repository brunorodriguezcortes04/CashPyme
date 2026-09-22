namespace Backend.Security;

/// <summary>Acciones que un rol puede o no ejecutar dentro de la empresa activa.</summary>
public enum Permiso
{
    MovimientosVer,
    MovimientosRegistrar,
    CuentasGestionar,
    ConfiguracionEmpresaVer,
    ConfiguracionEmpresaEditar,
    GestionUsuarios
}
