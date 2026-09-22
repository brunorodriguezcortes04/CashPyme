using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>
/// Única fuente de los tipos de cuenta en el backend: alimenta el endpoint que llena el
/// dropdown y la validación del request. Debe coincidir con ck_cuenta_tipo en la base.
/// </summary>
public static class TiposCuenta
{
    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new("efectivo", "Efectivo"),
        new("cuenta_corriente", "Cuenta corriente"),
        new("cuenta_vista", "Cuenta vista"),
        new("otro", "Otro")
    ];

    public static bool EsValido(string? valor) => Todos.Any(o => o.Valor == valor);
}

public sealed class TipoCuentaValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposCuenta.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo de cuenta debe ser uno de: {string.Join(", ", TiposCuenta.Todos.Select(o => o.Valor))}.";
}
