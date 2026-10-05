using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>
/// Tipos de documento tributario que acepta documento_financiero.
/// Debe coincidir con ck_doc_tipo_documento en la base (ver migración 011).
///
/// Las notas no son documentos que se cobren: corrigen a uno ya emitido y apuntan a él con
/// id_documento_referencia. Guardan monto positivo, igual que el resto; el signo lo aplica
/// vista_documento_saldo al netearlas contra el documento padre, y el saldo propio de una nota
/// es 0 para que la proyección de caja no la cuente aparte.
/// </summary>
/// <summary>
/// EsNota y Signo viajan al front para que no tenga que saber que 'nota_credito' resta: el
/// vocabulario del dominio vive en el backend, igual que con TipoMovimientoResponse.
/// Signo es +1 para un documento normal o una nota de débito y -1 para una de crédito.
/// </summary>
public record TipoDocumentoResponse(string Valor, string Etiqueta, bool EsNota, int Signo);

public static class TiposDocumento
{
    public const string NotaCredito = "nota_credito";
    public const string NotaDebito = "nota_debito";

    public static readonly IReadOnlyList<TipoDocumentoResponse> Todos =
    [
        new("factura", "Factura", false, 1),
        new("factura_exenta", "Factura exenta", false, 1),
        new("boleta", "Boleta", false, 1),
        new("otro", "Otro", false, 1),
        new(NotaCredito, "Nota de crédito", true, -1),
        new(NotaDebito, "Nota de débito", true, 1)
    ];

    public static bool EsValido(string? valor) => Todos.Any(o => o.Valor == valor);

    /// <summary>
    /// Una nota necesita documento de referencia y el resto no puede tenerlo
    /// (ck_doc_nota_referencia). Esto decide por cuál de las dos ramas va la validación.
    /// </summary>
    public static bool EsNota(string? valor) => valor is NotaCredito or NotaDebito;
}

public sealed class TipoDocumentoValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposDocumento.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo de documento debe ser uno de: {string.Join(", ", TiposDocumento.Todos.Select(o => o.Valor))}.";
}
