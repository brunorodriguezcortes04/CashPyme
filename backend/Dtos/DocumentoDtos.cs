using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

namespace Backend.Dtos;

/// <summary>
/// Un solo request para cuentas por cobrar y por pagar: el tipo viaja en el cuerpo, igual que
/// en CreateMovimientoRequest. MontoNeto y MontoIva son opcionales (una boleta no los
/// desglosa); si viene MontoNeto, la base exige que MontoNeto + MontoIva = MontoTotal.
/// </summary>
public record CreateDocumentoRequest(
    [Required, TipoCuentaDocumentoValido] string TipoCuenta,
    [Required, TipoDocumentoValido] string TipoDocumento,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona un cliente.")]
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
    [MaxLength(250)] string? Descripcion = null,
    /// <summary>
    /// Documento que esta nota corrige. Obligatorio si TipoDocumento es nota_credito o
    /// nota_debito, y prohibido en cualquier otro caso (ck_doc_nota_referencia).
    /// </summary>
    long? IdDocumentoReferencia = null
);

/// <summary>
/// TipoCuenta no se puede cambiar al editar: cada pantalla ya está fijada a por cobrar o por
/// pagar, y cambiarlo movería tipo_flujo (columna generada), dejando la categoría del
/// documento del lado equivocado de fk_documento_categoria.
///
/// Bajar MontoTotal es también el mecanismo para reflejar una rebaja parcial —una devolución,
/// un descuento dado después—, porque vista_documento_saldo calcula
/// saldo_pendiente = monto_total - pagado. La base no deja bajarlo por debajo de lo ya pagado
/// (trg_documento_validar) y recalcula el estado sola (trg_documento_recalcula).
/// </summary>
public record UpdateDocumentoRequest(
    [Required, TipoDocumentoValido] string TipoDocumento,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona un cliente.")]
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
    string? Descripcion,
    DateTime FechaCreacion,
    // Lo que sigue sale de vista_documento_saldo, no de la fila: es la misma fuente que usa
    // fn_proyeccion_caja, así que la pantalla no puede mostrar un saldo distinto del que la
    // proyección está contando.
    decimal MontoPagado,
    decimal MontoNotasCredito,
    decimal MontoNotasDebito,
    decimal SaldoPendiente,
    bool EstaVencido,
    int DiasAtraso,
    /// <summary>Documento que esta nota corrige; null si no es una nota.</summary>
    long? IdDocumentoReferencia
);

/// <summary>
/// Aplica un movimiento de caja ya registrado a un documento. El tipo de flujo lo completa la
/// base desde el documento (fn_validar_pago), así que acá no viaja: una cuenta por cobrar solo
/// se puede pagar con un ingreso, y eso lo garantiza fk_pago_movimiento.
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
