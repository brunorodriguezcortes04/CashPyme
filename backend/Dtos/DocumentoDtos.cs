using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

namespace Backend.Dtos;

/// <summary>
/// Un solo request para cuentas por cobrar y por pagar: el tipo viaja en el cuerpo, igual que
/// en CreateMovimientoRequest. MontoNeto y MontoIva son opcionales (una boleta no los desglosa);
/// si viene MontoNeto, MontoNeto + MontoIva tiene que dar MontoTotal.
///
/// Para una nota (crédito o débito) IdTercero y FechaVencimiento sobran: la nota es del mismo
/// tercero que el documento que corrige y no vence por su cuenta, así que el backend los toma de
/// ahí. Si igual vienen, el tercero tiene que coincidir. Para cualquier otro documento son
/// obligatorios.
/// </summary>
public record CreateDocumentoRequest(
    [Required, TipoCuentaDocumentoValido] string TipoCuenta,
    [Required, TipoDocumentoValido] string TipoDocumento,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona a quién corresponde el documento.")]
    long? IdTercero,
    [Range(0.01, 999999999999.99, ErrorMessage = "El monto total debe ser mayor a 0.")]
    decimal MontoTotal,
    DateOnly FechaEmision,
    DateOnly? FechaVencimiento,
    [MaxLength(50)] string? NumeroDocumento,
    long? IdCategoria,
    [Range(0, 999999999999.99, ErrorMessage = "El monto neto no puede ser negativo.")]
    decimal? MontoNeto,
    [Range(0, 999999999999.99, ErrorMessage = "El IVA no puede ser negativo.")]
    decimal MontoIva = 0,
    [MaxLength(250)] string? Descripcion = null,
    /// <summary>
    /// Documento que esta nota corrige. Obligatorio si TipoDocumento es nota_credito o
    /// nota_debito, y prohibido en cualquier otro caso (ck_doc_nota_referencia).
    /// </summary>
    long? IdDocumentoReferencia = null
);

/// <summary>
/// TipoCuenta no se puede cambiar al editar: cada pantalla ya está fijada a por cobrar o por
/// pagar, y cambiarlo movería tipo_flujo (columna generada), dejando la categoría del documento
/// del lado equivocado de fk_documento_categoria. Una nota no se edita (se anula y se emite
/// otra), así que acá no hay referencia.
///
/// Bajar MontoTotal es también el mecanismo para reflejar una rebaja parcial sin emitir nota
/// —una devolución, un descuento dado después—: el saldo es monto menos lo pagado y lo neteado
/// por notas, así que baja solo. No se puede bajar por debajo de lo ya pagado más lo acreditado.
/// </summary>
public record UpdateDocumentoRequest(
    [Required, TipoDocumentoValido] string TipoDocumento,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona a quién corresponde el documento.")]
    long IdTercero,
    [Range(0.01, 999999999999.99, ErrorMessage = "El monto total debe ser mayor a 0.")]
    decimal MontoTotal,
    DateOnly FechaEmision,
    DateOnly FechaVencimiento,
    [MaxLength(50)] string? NumeroDocumento,
    long? IdCategoria,
    [Range(0, 999999999999.99, ErrorMessage = "El monto neto no puede ser negativo.")]
    decimal? MontoNeto,
    [Range(0, 999999999999.99, ErrorMessage = "El IVA no puede ser negativo.")]
    decimal MontoIva = 0,
    [MaxLength(250)] string? Descripcion = null
);

public record DocumentoResponse(
    long Id,
    string TipoCuenta,
    string TipoDocumento,
    string? NumeroDocumento,
    long IdTercero,
    string NombreTercero,
    long? IdCategoria,
    string? NombreCategoria,
    decimal? MontoNeto,
    decimal MontoIva,
    decimal MontoTotal,
    DateOnly FechaEmision,
    DateOnly FechaVencimiento,
    string EstadoDocumento,
    /// <summary>
    /// Texto del estado para mostrar ("Pago parcial", "Cerrada por nota", "Vigente"…). Lo arma
    /// CalculoDocumento.EtiquetaEstado para que las dos pantallas digan exactamente lo mismo.
    /// </summary>
    string EtiquetaEstado,
    string? Descripcion,
    DateTime FechaCreacion,
    // Lo que sigue lo calcula SaldosService con la misma fórmula que usan la proyección, las
    // alertas y el saldo neto: la pantalla no puede mostrar un saldo distinto del que cuentan ellos.
    decimal MontoPagado,
    decimal MontoNotasCredito,
    decimal MontoNotasDebito,
    decimal SaldoPendiente,
    bool EstaVencido,
    int DiasAtraso,
    /// <summary>Documento que esta nota corrige; null si no es una nota.</summary>
    long? IdDocumentoReferencia,
    /// <summary>Qué botones ofrece la fila (CalculoDocumento.Acciones).</summary>
    AccionesDocumento Acciones
);

/// <summary>
/// Documento al que se le puede emitir una nota: del mismo tipo de cuenta, no anulado y que no
/// sea a su vez una nota. Lo arma el backend para que el formulario no tenga que conocer la regla
/// ni limitarse a los documentos de la página que está mostrando.
/// </summary>
public record DocumentoCorregibleResponse(
    long Id,
    string TipoDocumento,
    string? NumeroDocumento,
    long IdTercero,
    string NombreTercero,
    decimal SaldoPendiente
);

/// <summary>
/// Aplica un movimiento de caja ya registrado a un documento. El flujo no viaja: lo decide el
/// documento (una cuenta por cobrar solo se paga con un ingreso) y PagosService lo comprueba.
/// </summary>
public record CreatePagoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona un documento.")]
    long IdDocumento,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona un movimiento de caja.")]
    long IdMovimiento,
    [Range(0.01, 999999999999.99, ErrorMessage = "El monto aplicado debe ser mayor a 0.")]
    decimal MontoAplicado
);

/// <summary>
/// Movimiento de caja que todavía tiene monto sin aplicar a ningún documento. MontoDisponible
/// es lo que queda libre: un ingreso de 100.000 ya aplicado en 30.000 muestra 70.000.
/// </summary>
public record MovimientoAplicableResponse(
    long Id,
    DateOnly Fecha,
    decimal Monto,
    decimal MontoDisponible,
    string MedioPago,
    string NombreCuenta,
    string? Descripcion
);

public record PagoResponse(
    long Id,
    long IdDocumento,
    long IdMovimiento,
    decimal MontoAplicado,
    DateOnly FechaMovimiento,
    string MedioPago,
    string? DescripcionMovimiento
);

/// <summary>Respuesta paginada del listado, con el total para calcular las páginas.</summary>
public record DocumentosPaginadosResponse(
    IReadOnlyList<DocumentoResponse> Items,
    int TotalResultados,
    int Pagina,
    int TamanoPagina
);
