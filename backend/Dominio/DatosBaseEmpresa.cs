namespace Backend.Dominio;

/// <summary>
/// Lo que toda empresa nueva trae al registrarse, para que pueda cargar movimientos desde el
/// primer minuto. Antes lo creaban los triggers trg_empresa_categorias_base y
/// trg_empresa_cuenta_base; ahora lo hace AuthService en la misma transacción del registro.
/// </summary>
public static class DatosBaseEmpresa
{
    public const string NombreCuentaBase = "Caja";
    public const string TipoCuentaBase = "efectivo";

    public static readonly IReadOnlyList<(string Nombre, string Tipo)> Categorias =
    [
        ("Ventas", TiposMovimiento.Ingreso),
        ("Otros ingresos", TiposMovimiento.Ingreso),
        ("Proveedores", TiposMovimiento.Egreso),
        ("Remuneraciones", TiposMovimiento.Egreso),
        ("Arriendo", TiposMovimiento.Egreso),
        ("Servicios básicos", TiposMovimiento.Egreso),
        ("Impuestos", TiposMovimiento.Egreso),
        ("Marketing y publicidad", TiposMovimiento.Egreso),
        ("Otros egresos", TiposMovimiento.Egreso)
    ];
}
