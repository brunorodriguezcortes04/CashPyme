using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>
/// Tipo de movimiento con los textos de su pantalla. Es la única definición de 'ingreso' y
/// 'egreso' en el backend: la usan la validación, las consultas y el endpoint que arma la
/// pantalla, para que el front no arme copy ni conozca el vocabulario del dominio.
/// Debe coincidir con ck_mov_tipo y ck_categoria_tipo en la base.
/// </summary>
public record TipoMovimientoResponse(
    string Valor,
    string Titulo,
    string Subtitulo,
    string Boton,
    string TituloListado,
    string MensajeVacio,
    string MensajeExito,
    string EjemploDescripcion
);

public static class TiposMovimiento
{
    public const string Ingreso = "ingreso";
    public const string Egreso = "egreso";

    public static readonly IReadOnlyList<TipoMovimientoResponse> Todos =
    [
        new(
            Ingreso,
            Titulo: "Registrar ingreso",
            Subtitulo: "Registra tus ventas, cobros y otros ingresos para llevar el control de tu caja.",
            Boton: "Registrar ingreso",
            TituloListado: "Últimos ingresos",
            MensajeVacio: "Todavía no registras ningún ingreso.",
            MensajeExito: "Ingreso registrado correctamente.",
            EjemploDescripcion: "Venta mostrador"),
        new(
            Egreso,
            Titulo: "Registrar egreso",
            Subtitulo: "Registra tus pagos, compras y otros gastos para llevar el control de tu caja.",
            Boton: "Registrar egreso",
            TituloListado: "Últimos egresos",
            MensajeVacio: "Todavía no registras ningún egreso.",
            MensajeExito: "Egreso registrado correctamente.",
            EjemploDescripcion: "Pago proveedor")
    ];

    public static bool EsValido(string? valor) => Todos.Any(t => t.Valor == valor);
}

public sealed class TipoMovimientoValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposMovimiento.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo de movimiento debe ser uno de: {string.Join(", ", TiposMovimiento.Todos.Select(t => t.Valor))}.";
}
