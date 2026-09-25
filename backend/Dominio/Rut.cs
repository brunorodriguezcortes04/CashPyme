using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Backend.Dominio;

/// <summary>
/// RUT chileno normalizado: sin puntos, con guion y dígito verificador en mayúscula
/// (ej. 12345678-5). Es la misma regla que fn_rut_valido y ck_empresa_rut en la base,
/// repetida acá a propósito: así un RUT con dígito verificador inválido se rechaza con
/// un mensaje claro antes de llegar a Postgres, en vez del error genérico del CHECK.
/// </summary>
public static partial class Rut
{
    [GeneratedRegex(@"^[0-9]{7,8}-[0-9K]$")]
    private static partial Regex FormatoNormalizado();

    /// <summary>
    /// Deja el RUT como 12345678-9: descarta puntos y cualquier otro separador, y sube
    /// la k del dígito verificador a mayúscula. Mismo criterio que normalizarRut() en el
    /// front, para que los dos lados guarden exactamente el mismo texto.
    /// </summary>
    public static string Normalizar(string rut)
    {
        var limpio = new string([.. rut.Where(c => char.IsAsciiDigit(c) || c is 'k' or 'K')])
            .ToUpperInvariant();

        return limpio.Length <= 1 ? limpio : $"{limpio[..^1]}-{limpio[^1]}";
    }

    /// <summary>Verifica el dígito verificador (módulo 11) de un RUT ya normalizado.</summary>
    public static bool EsValido(string? rutNormalizado)
    {
        if (rutNormalizado is null || !FormatoNormalizado().IsMatch(rutNormalizado))
        {
            return false;
        }

        var partes = rutNormalizado.Split('-');
        var cuerpo = partes[0];

        var suma = 0;
        var factor = 2;
        for (var i = cuerpo.Length - 1; i >= 0; i--)
        {
            suma += (cuerpo[i] - '0') * factor;
            factor = factor == 7 ? 2 : factor + 1;
        }

        var esperado = (11 - (suma % 11)) switch
        {
            11 => "0",
            10 => "K",
            var resto => resto.ToString()
        };

        return partes[1] == esperado;
    }
}

/// <summary>
/// El RUT es opcional (la columna admite NULL), así que vacío se considera válido: lo que
/// se rechaza es un RUT escrito con el dígito verificador equivocado.
/// </summary>
public sealed class RutValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
        => value is not string rut || string.IsNullOrWhiteSpace(rut) || Rut.EsValido(Rut.Normalizar(rut));

    public override string FormatErrorMessage(string name)
        => "El RUT no es válido: revisa el dígito verificador.";
}
