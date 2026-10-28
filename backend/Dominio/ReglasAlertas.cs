using System.Globalization;
using Backend.Models;

namespace Backend.Dominio;

/// <summary>Debe coincidir con ck_alerta_tipo en la base.</summary>
public static class TiposAlerta
{
    public const string VencimientoProximo = "vencimiento_proximo";
    public const string DocumentoVencido = "documento_vencido";
    public const string SaldoBajo = "saldo_bajo";
    public const string SaldoNegativoProyectado = "saldo_negativo_proyectado";
}

/// <summary>Debe coincidir con ck_alerta_severidad en la base.</summary>
public static class SeveridadesAlerta
{
    public const string Info = "info";
    public const string Advertencia = "advertencia";
    public const string Critica = "critica";
}

/// <summary>
/// Cuándo se crea, cuándo se cierra y qué dice cada alerta. Antes era fn_generar_alertas; acá
/// quedan solo las decisiones (funciones puras, sin base de datos) y AlertasService las aplica.
/// </summary>
public static class ReglasAlertas
{
    /// <summary>
    /// Avisa lo que vence entre hoy y hoy + días de aviso. Saldo > 0 deja fuera lo pagado, lo
    /// anulado y las notas (su saldo propio es 0); lo ya vencido tiene su propia alerta.
    /// </summary>
    public static bool AvisarVencimientoProximo(decimal saldo, DateOnly fechaVencimiento, DateOnly hoy, int diasAviso)
        => saldo > 0
           && !CalculoDocumento.EstaVencido(saldo, fechaVencimiento, hoy)
           && fechaVencimiento <= hoy.AddDays(diasAviso);

    public static bool AvisarVencido(decimal saldo, DateOnly fechaVencimiento, DateOnly hoy)
        => CalculoDocumento.EstaVencido(saldo, fechaVencimiento, hoy);

    /// <summary>
    /// Una alerta de documento abierta se cierra cuando deja de decir la verdad: el documento se
    /// pagó o anuló, cambió de fecha (la alerta habla de otro vencimiento), salió de la ventana de
    /// aviso, o según el tipo ya venció / dejó de estar vencido. Si el documento sigue dentro de
    /// la ventana, en la misma corrida se crea la alerta nueva con la fecha correcta.
    /// </summary>
    public static bool ResolverAlertaDocumento(
        string tipoAlerta,
        DateOnly? fechaReferencia,
        string estadoDocumento,
        decimal saldo,
        DateOnly fechaVencimiento,
        DateOnly hoy,
        int diasAviso)
    {
        var vencido = CalculoDocumento.EstaVencido(saldo, fechaVencimiento, hoy);

        return saldo <= 0
               || estadoDocumento == DocumentoFinanciero.Anulado
               || fechaReferencia != fechaVencimiento
               || (tipoAlerta == TiposAlerta.VencimientoProximo
                   && (vencido || fechaVencimiento > hoy.AddDays(diasAviso)))
               || (tipoAlerta == TiposAlerta.DocumentoVencido && !vencido);
    }

    public static bool AvisarSaldoBajo(decimal saldo, decimal umbral, bool cuentaActiva)
        => cuentaActiva && saldo < umbral;

    public static bool ResolverSaldoBajo(decimal saldo, decimal umbral, bool cuentaActiva)
        => saldo >= umbral || !cuentaActiva;

    /// <summary>Bajo el umbral es para estar atento; bajo cero ya es urgente.</summary>
    public static string SeveridadSaldoBajo(decimal saldo)
        => saldo < 0 ? SeveridadesAlerta.Critica : SeveridadesAlerta.Advertencia;

    /// <summary>Primer día en que la caja proyectada queda bajo cero, si lo hay.</summary>
    public static DateOnly? PrimerDiaNegativo(IEnumerable<DiaProyectado> proyeccion)
        => proyeccion.Where(d => d.SaldoProyectado < 0).Select(d => (DateOnly?)d.Dia).FirstOrDefault();

    /// <summary>Ruta de la pantalla Cuentas en el backoffice (tabla pantalla).</summary>
    public const string RutaCuentas = "cuentas";

    /// <summary>Cómo se anuncia cada severidad en la pantalla.</summary>
    public static string EtiquetaSeveridad(string severidad) => severidad switch
    {
        SeveridadesAlerta.Critica => "Urgente",
        SeveridadesAlerta.Advertencia => "Atención",
        _ => "Aviso"
    };

    /// <summary>
    /// Pantalla donde se resuelve la alerta: la del documento (cobrar o pagar) o la de cuentas si
    /// es de saldo bajo. La de caja negativa proyectada no tiene una pantalla propia: null.
    /// </summary>
    public static string? RutaDondeSeResuelve(string tipoAlerta, string? tipoCuentaDocumento)
    {
        if (tipoCuentaDocumento is not null && TiposCuentaDocumento.EsValido(tipoCuentaDocumento))
        {
            return TiposCuentaDocumento.Pantalla(tipoCuentaDocumento).Ruta;
        }

        return tipoAlerta == TiposAlerta.SaldoBajo ? RutaCuentas : null;
    }

    public static string MensajeVencimientoProximo(
        string tipoCuenta, string tercero, string? numeroDocumento, DateOnly fechaVencimiento, decimal saldo)
        => $"{Concepto(tipoCuenta)} de {tercero}{Numero(numeroDocumento)} vence el {Fecha(fechaVencimiento)} (saldo {Clp.Formatear(saldo)})";

    /// <summary>
    /// Dice la fecha en que venció y no "hace N días": el texto queda guardado al crear la alerta,
    /// y una cuenta de días ya sería falsa al día siguiente.
    /// </summary>
    public static string MensajeVencido(
        string tipoCuenta, string tercero, string? numeroDocumento, DateOnly fechaVencimiento, decimal saldo)
        => $"{Concepto(tipoCuenta)} de {tercero}{Numero(numeroDocumento)} venció el {Fecha(fechaVencimiento)} (saldo {Clp.Formatear(saldo)})";

    public static string MensajeSaldoBajo(string cuenta, decimal saldo, decimal umbral)
        => $"La cuenta {cuenta} tiene un saldo de {Clp.Formatear(saldo)}, bajo el mínimo de {Clp.Formatear(umbral)}";

    public static string MensajeSaldoNegativo(DateOnly fecha)
        => $"Tu caja podría quedar en negativo el {Fecha(fecha)}";

    private static string Concepto(string tipoCuenta)
        => tipoCuenta == TiposCuentaDocumento.PorCobrar ? "Cobro" : "Pago";

    /// <summary>Un cliente puede tener varias facturas abiertas: el número dice cuál es.</summary>
    private static string Numero(string? numeroDocumento)
        => string.IsNullOrWhiteSpace(numeroDocumento) ? "" : $" (N° {numeroDocumento})";

    private static string Fecha(DateOnly fecha) => fecha.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
}
