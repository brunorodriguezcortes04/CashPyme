using Backend.Models;

namespace Backend.Dominio;

/// <summary>Botones que la pantalla muestra en la fila de un documento.</summary>
public record AccionesDocumento(bool PuedeAplicar, bool PuedeEmitirNota, bool PuedeEditar, bool PuedeAnular);

/// <summary>
/// Saldo y estado de una cuenta por cobrar o por pagar. Es la única definición de estas fórmulas:
/// las usan la pantalla, la proyección de caja, las alertas, el saldo neto y el resumen semanal.
/// Antes vivían repartidas en la base (vista_documento_saldo, fn_monto_neto_documento y
/// fn_recalcular_estado_documento); se portaron sin cambiar el resultado.
///
/// Las notas guardan monto positivo: el signo lo pone el cálculo al netearlas contra el documento
/// que corrigen.
/// </summary>
public static class CalculoDocumento
{
    /// <summary>
    /// Lo que de verdad hay que cobrar o pagar: el monto del documento menos las notas de crédito
    /// más las de débito (solo las no anuladas). Nunca negativo: una nota de crédito no puede
    /// dejar al cliente con saldo a favor, que este MVP no modela.
    /// </summary>
    public static decimal Neto(decimal montoTotal, decimal notasCredito, decimal notasDebito)
        => Math.Max(montoTotal - notasCredito + notasDebito, 0m);

    /// <summary>
    /// Lo que queda pendiente. Una nota tiene saldo 0: su efecto ya está en el documento que
    /// corrige, y si contara aparte la proyección la sumaría dos veces. Un anulado, también 0.
    /// </summary>
    public static decimal Saldo(
        string tipoDocumento,
        string estado,
        decimal montoTotal,
        decimal pagado,
        decimal notasCredito,
        decimal notasDebito)
    {
        if (TiposDocumento.EsNota(tipoDocumento) || estado == DocumentoFinanciero.Anulado)
        {
            return 0m;
        }

        return Math.Max(montoTotal - pagado - notasCredito + notasDebito, 0m);
    }

    /// <summary>
    /// Estado según lo pagado contra el neto. Un anulado no se mueve. Un documento cubierto entero
    /// por notas de crédito queda 'pagado' aunque no haya entrado plata: no queda nada por cobrar,
    /// y no existe un estado "cerrado por nota" (la pantalla lo distingue con EtiquetaEstado).
    /// </summary>
    public static string Estado(string estadoActual, decimal neto, decimal pagado)
    {
        if (estadoActual == DocumentoFinanciero.Anulado)
        {
            return DocumentoFinanciero.Anulado;
        }

        if (neto <= 0 || pagado >= neto)
        {
            return DocumentoFinanciero.Pagado;
        }

        return pagado > 0 ? DocumentoFinanciero.PagadoParcial : DocumentoFinanciero.Pendiente;
    }

    /// <summary>"Vencido" no es un estado guardado: es tener saldo con la fecha ya pasada.</summary>
    public static bool EstaVencido(decimal saldo, DateOnly fechaVencimiento, DateOnly hoy)
        => saldo > 0 && fechaVencimiento < hoy;

    public static int DiasAtraso(decimal saldo, DateOnly fechaVencimiento, DateOnly hoy)
        => EstaVencido(saldo, fechaVencimiento, hoy) ? hoy.DayNumber - fechaVencimiento.DayNumber : 0;

    /// <summary>
    /// Qué se le puede hacer a un documento desde la pantalla, con las mismas reglas que aplican
    /// los servicios al guardar: la pantalla solo ofrece lo que el backend va a aceptar.
    ///   * Anulado: nada.
    ///   * Nota: solo anularla. No se cobra, no se edita y no admite otra nota encima.
    ///   * El resto: editar, emitirle una nota y anularlo; cobrar o pagar solo si le queda saldo.
    /// Anular un documento con pagos se ofrece igual: el backend lo rechaza diciendo qué
    /// movimientos hay que anular antes, que es justo lo que la persona necesita saber.
    /// </summary>
    public static AccionesDocumento Acciones(string tipoDocumento, string estado, decimal saldo)
    {
        if (estado == DocumentoFinanciero.Anulado)
        {
            return new AccionesDocumento(false, false, false, false);
        }

        if (TiposDocumento.EsNota(tipoDocumento))
        {
            return new AccionesDocumento(PuedeAplicar: false, PuedeEmitirNota: false, PuedeEditar: false, PuedeAnular: true);
        }

        return new AccionesDocumento(PuedeAplicar: saldo > 0, PuedeEmitirNota: true, PuedeEditar: true, PuedeAnular: true);
    }

    /// <summary>
    /// Texto del estado para la pantalla. Vive acá y no en el front para que las dos pantallas
    /// (por cobrar y por pagar) y cualquier otra que muestre documentos digan lo mismo.
    /// </summary>
    public static string EtiquetaEstado(string tipoDocumento, string estado, decimal pagado)
    {
        if (TiposDocumento.EsNota(tipoDocumento))
        {
            // Una nota no se cobra: lo único que cambia su efecto es si sigue vigente o se anuló.
            return estado == DocumentoFinanciero.Anulado ? "Anulada" : "Vigente";
        }

        return estado switch
        {
            DocumentoFinanciero.Pendiente => "Pendiente",
            DocumentoFinanciero.PagadoParcial => "Pago parcial",
            // 'pagado' sin plata aplicada es un documento que se cerró con notas de crédito.
            DocumentoFinanciero.Pagado => pagado > 0 ? "Pagada" : "Cerrada por nota",
            DocumentoFinanciero.Anulado => "Anulada",
            _ => estado
        };
    }
}
