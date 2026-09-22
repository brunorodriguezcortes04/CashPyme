using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

public record OpcionCatalogo(string Valor, string Etiqueta);

/// <summary>
/// Única fuente de los medios de pago en el backend: alimenta el endpoint que llena el
/// dropdown y la validación del request. Debe coincidir con ck_mov_medio_pago en la base.
/// </summary>
public static class MediosPago
{
    public static readonly IReadOnlyList<OpcionCatalogo> Todos =
    [
        new("efectivo", "Efectivo"),
        new("transferencia", "Transferencia"),
        new("cheque", "Cheque"),
        new("tarjeta", "Tarjeta"),
        new("otro", "Otro")
    ];

    public static bool EsValido(string? valor) => Todos.Any(o => o.Valor == valor);
}

public sealed class MedioPagoValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => MediosPago.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El medio de pago debe ser uno de: {string.Join(", ", MediosPago.Todos.Select(o => o.Valor))}.";
}
