using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>Debe coincidir con ck_tercero_tipo en la base.</summary>
public static class TiposTercero
{
    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new("cliente", "Cliente"),
        new("proveedor", "Proveedor"),
        new("ambos", "Cliente y proveedor")
    ];

    public static bool EsValido(string? valor) => Todos.Any(o => o.Valor == valor);
}

public sealed class TipoTerceroValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposTercero.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo debe ser uno de: {string.Join(", ", TiposTercero.Todos.Select(o => o.Valor))}.";
}