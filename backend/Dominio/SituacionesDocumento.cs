using Backend.Models;

namespace Backend.Dominio;

/// <summary>
/// Cómo se agrupa un documento en el listado: pendiente (con saldo y al día), vencida (con saldo
/// y la fecha ya pasada, tenga o no abonos) o pagada. Agrupa más que el estado: un 'pagado_parcial'
/// atrasado cae en vencidas. Las notas y los anulados no caen en ningún grupo.
/// </summary>
public static class SituacionesDocumento
{
    public const string Pendiente = "pendiente";
    public const string Vencida = "vencida";
    public const string Pagada = "pagada";

    /// <summary>En el orden en que se muestran: lo urgente primero.</summary>
    public static readonly string[] Todas = [Vencida, Pendiente, Pagada];

    public static bool EsValida(string? situacion) => situacion is not null && Todas.Contains(situacion);

    public static string? De(string tipoDocumento, string estado, decimal saldo, bool estaVencido)
    {
        if (TiposDocumento.EsNota(tipoDocumento) || estado == DocumentoFinanciero.Anulado)
        {
            return null;
        }

        if (saldo <= 0)
        {
            return Pagada;
        }

        return estaVencido ? Vencida : Pendiente;
    }

    public static string Etiqueta(string situacion) => situacion switch
    {
        Pendiente => "Pendientes",
        Vencida => "Vencidas",
        Pagada => "Pagadas",
        _ => situacion
    };
}