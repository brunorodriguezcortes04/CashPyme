using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>
/// Una cuenta por cobrar y una por pagar son la misma tabla (documento_financiero) con
/// distinto tipo_cuenta, igual que un ingreso y un egreso comparten movimiento_financiero.
/// De tipo_cuenta depende tipo_flujo, que la base genera sola y que decide contra qué
/// tipo de categoría valida fk_documento_categoria.
/// Debe coincidir con ck_doc_tipo_cuenta en la base.
/// </summary>
public static class TiposCuentaDocumento
{
    public const string PorCobrar = "por_cobrar";
    public const string PorPagar = "por_pagar";

    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new(PorCobrar, "Por cobrar"),
        new(PorPagar, "Por pagar")
    ];

    public static bool EsValido(string? valor) => Todos.Any(o => o.Valor == valor);

    /// <summary>
    /// Mismo CASE que la columna generada tipo_flujo: una cuenta por cobrar termina siendo un
    /// ingreso de caja, así que su categoría tiene que ser de ingreso. Se replica acá para
    /// poder rechazar la categoría equivocada con un 400 claro, en vez de dejar que explote
    /// la FK compuesta como un 500.
    /// </summary>
    public static string FlujoDe(string tipoCuenta)
        => tipoCuenta == PorCobrar ? TiposMovimiento.Ingreso : TiposMovimiento.Egreso;
}

public sealed class TipoCuentaDocumentoValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposCuentaDocumento.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo de cuenta debe ser uno de: {string.Join(", ", TiposCuentaDocumento.Todos.Select(o => o.Valor))}.";
}
