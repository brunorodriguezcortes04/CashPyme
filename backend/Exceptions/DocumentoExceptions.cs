using Backend.Dominio;

namespace Backend.Exceptions;

/// <summary>
/// La base ya lo impide con ck_doc_fechas, pero ese CHECK se escaparía como 500. Se valida
/// antes para que la persona lea qué corregir.
/// </summary>
public class FechasDocumentoInvalidasException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "La fecha de vencimiento no puede ser anterior a la fecha de emisión.");

/// <summary>
/// Una cuenta por cobrar es contra un cliente y una por pagar contra un proveedor. Un tercero
/// de tipo 'ambos' sirve para las dos.
/// </summary>
public class TerceroNoEsClienteException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "El tercero seleccionado no es un cliente: para una cuenta por cobrar elige uno de tipo cliente o cliente y proveedor.");

public class TerceroNoEsProveedorException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "El tercero seleccionado no es un proveedor: para una cuenta por pagar elige uno de tipo proveedor o cliente y proveedor.");

/// <summary>
/// Un documento normal necesita saber contra quién es. Solo las notas pueden omitirlo, porque
/// heredan el tercero del documento que corrigen.
/// </summary>
public class TerceroRequeridoException()
    : ApiException(StatusCodes.Status400BadRequest, "Selecciona a quién corresponde el documento.");

/// <summary>Solo las notas pueden omitir el vencimiento: no vencen por su cuenta.</summary>
public class FechaVencimientoRequeridaException()
    : ApiException(StatusCodes.Status400BadRequest, "Indica la fecha de vencimiento del documento.");

/// <summary>Replica ck_doc_iva para no devolver un 500 cuando el desglose no cuadra.</summary>
public class MontosDocumentoInconsistentesException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "El monto neto más el IVA tiene que dar exactamente el monto total.");

/// <summary>Choque contra ux_documento_numero: el mismo documento ya está cargado.</summary>
public class DocumentoDuplicadoException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "Ya cargaste un documento de este tipo con ese número para este cliente o proveedor.");

public class DocumentoNoEncontradoException()
    : ApiException(StatusCodes.Status404NotFound, "El documento no existe o no pertenece a tu empresa.");

public class DocumentoYaAnuladoException()
    : ApiException(StatusCodes.Status409Conflict, "Este documento ya está anulado.");

/// <summary>
/// A diferencia de anular un movimiento, acá no hay "confirmar de todas formas": no se puede
/// anular un documento con pagos registrados, porque esos pagos quedarían aplicados a algo que
/// ya no existe. El orden correcto es al revés: primero se anulan los movimientos, lo que libera
/// el documento.
/// </summary>
public class DocumentoConPagosAnularException(int cantidadPagos) : ApiException(
    StatusCodes.Status409Conflict,
    cantidadPagos == 1
        ? "Este documento tiene 1 pago registrado. Anula primero ese movimiento de caja y después el documento."
        : $"Este documento tiene {cantidadPagos} pagos registrados. Anula primero esos movimientos de caja y después el documento.")
{
    public int CantidadPagos { get; } = cantidadPagos;
}

// ----- Notas de crédito y débito --------------------------------------------

public class NotaSinReferenciaException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "Una nota de crédito o débito tiene que indicar el documento que corrige.");

public class ReferenciaEnDocumentoNormalException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "Solo las notas de crédito y débito corrigen a otro documento.");

public class DocumentoReferenciadoNoEncontradoException()
    : ApiException(
        StatusCodes.Status404NotFound,
        "El documento que la nota corrige no existe o no pertenece a tu empresa.");

public class NotaSobreNotaException()
    : ApiException(StatusCodes.Status400BadRequest, "Una nota no puede corregir a otra nota.");

public class NotaSobreDocumentoAnuladoException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "No se puede emitir una nota sobre un documento anulado.");

public class NotaDeOtroTerceroException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "La nota tiene que ser del mismo cliente o proveedor que el documento que corrige.");

/// <summary>
/// Una nota de crédito baja lo que el cliente debe; pasarse de lo que todavía se debe (el monto
/// más las notas de débito, menos lo ya acreditado y lo ya pagado) dejaría un saldo negativo, o
/// sea plata que la empresa le debe al cliente, que este MVP no modela.
/// </summary>
public class NotasCreditoExcedenDocumentoException(decimal disponible) : ApiException(
    StatusCodes.Status409Conflict,
    disponible <= 0
        ? "Este documento ya no tiene saldo pendiente: entre lo pagado y las notas de crédito está cubierto entero."
        : $"La nota de crédito no puede pasar de {Clp.Formatear(disponible)}, que es lo que queda pendiente del documento.")
{
    public decimal Disponible { get; } = disponible;
}

/// <summary>Editar una nota se deja fuera a propósito: ver DocumentosService.ActualizarAsync.</summary>
public class NotaNoEditableException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "Una nota de crédito o débito no se edita: anúlala y emite otra, para que el documento que corrige quede con el rastro correcto.");

// ----- Pagos ----------------------------------------------------------------

public class PagoDocumentoAnuladoException()
    : ApiException(StatusCodes.Status409Conflict, "No se puede aplicar un pago a un documento anulado.");

/// <summary>
/// El saldo propio de una nota es 0: su efecto ya está en el documento que corrige. Cobrarla
/// aparte sería cobrar dos veces lo mismo.
/// </summary>
public class PagoSobreNotaException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "Una nota de crédito o débito no se cobra por separado: su efecto ya está en el documento que corrige.");

public class MovimientoNoAplicableException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        "El movimiento no existe, está anulado o no es del tipo que corresponde: una cuenta por cobrar se paga con un ingreso y una por pagar con un egreso.");

public class PagoExcedeSaldoException(decimal saldo) : ApiException(
    StatusCodes.Status409Conflict,
    saldo <= 0
        ? "Este documento ya no tiene saldo pendiente."
        : $"El pago no puede pasar de {Clp.Formatear(saldo)}, que es el saldo que queda del documento.");

public class MovimientoSinMontoDisponibleException(decimal disponible) : ApiException(
    StatusCodes.Status409Conflict,
    disponible <= 0
        ? "Este movimiento ya está completamente aplicado a otros documentos."
        : $"De este movimiento quedan {Clp.Formatear(disponible)} por aplicar.");

public class PagoDuplicadoException()
    : ApiException(
        StatusCodes.Status409Conflict,
        "Este movimiento ya está aplicado a este documento. Edita o anula el pago existente.");

public class PagoNoEncontradoException()
    : ApiException(StatusCodes.Status404NotFound, "El pago no existe o no pertenece a tu empresa.");

/// <summary>
/// Un documento no puede valer menos de lo que ya tiene comprometido: lo cobrado más lo
/// acreditado en notas, menos lo que le suman sus notas de débito. Si no, quedaría plata cobrada
/// o acreditada por encima de lo que vale.
/// </summary>
public class MontoMenorAComprometidoException(decimal pagado, decimal notasCredito, decimal notasDebito) : ApiException(
    StatusCodes.Status409Conflict,
    (notasCredito, notasDebito) switch
    {
        ( > 0, > 0) => $"El monto total no puede quedar por debajo de {Clp.Formatear(pagado + notasCredito - notasDebito)}: {Clp.Formatear(pagado)} ya pagados y {Clp.Formatear(notasCredito)} en notas de crédito, menos {Clp.Formatear(notasDebito)} en notas de débito.",
        ( > 0, _) => $"El monto total no puede quedar por debajo de {Clp.Formatear(pagado + notasCredito)}: {Clp.Formatear(pagado)} ya pagados y {Clp.Formatear(notasCredito)} en notas de crédito.",
        _ => $"El monto total no puede quedar por debajo de {Clp.Formatear(Math.Max(pagado - notasDebito, 0m))}, por lo ya pagado. Anula primero los pagos que correspondan."
    })
{
    public decimal Pagado { get; } = pagado;
    public decimal NotasCredito { get; } = notasCredito;
    public decimal NotasDebito { get; } = notasDebito;
}

/// <summary>
/// Anular una nota de débito baja lo que vale el documento. Si lo ya pagado y acreditado pasa
/// de lo que valdría sin la nota, quedaría un saldo a favor del cliente, que este MVP no modela.
/// </summary>
public class AnularNotaDebitoDejaSaldoAFavorException(decimal sobrante) : ApiException(
    StatusCodes.Status409Conflict,
    $"No se puede anular esta nota de débito: el documento quedaría con {Clp.Formatear(sobrante)} pagados o acreditados de más. Anula primero los pagos o las notas de crédito que correspondan.")
{
    public decimal Sobrante { get; } = sobrante;
}
