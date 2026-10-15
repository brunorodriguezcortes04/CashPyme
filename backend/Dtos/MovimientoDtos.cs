using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

namespace Backend.Dtos;

public record CreateMovimientoRequest(
    [Required, TipoMovimientoValido] string TipoMovimiento,
    DateOnly Fecha,
    [Range(0.01, 999999999999.99, ErrorMessage = "El monto debe ser mayor a 0.")]
    decimal Monto,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona una cuenta.")]
    long IdCuenta,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona una categoría.")]
    long IdCategoria,
    long? IdTercero,
    [Required, MedioPagoValido] string MedioPago,
    [MaxLength(250)] string? Descripcion
);

public record MovimientoResponse(
    long Id,
    string TipoMovimiento,
    decimal Monto,
    DateOnly Fecha,
    long IdCuenta,
    string NombreCuenta,
    long IdCategoria,
    string NombreCategoria,
    string MedioPago,
    string? Descripcion,
    string EstadoMovimiento,
    /// <summary>Texto del estado para mostrar (ReglasMovimiento.EtiquetaEstado).</summary>
    string EtiquetaEstado,
    DateTime? FechaAnulacion,
    DateTime FechaCreacion,
    /// <summary>Qué botones ofrece la fila (ReglasMovimiento.Acciones).</summary>
    AccionesMovimiento Acciones,
    /// <summary>
    /// Solo al crear o editar: aviso para mostrar junto al éxito, por ejemplo que la cuenta
    /// quedó sobregirada. null si no hay nada que avisar.
    /// </summary>
    string? Advertencia = null
);

public record CategoriaResponse(long Id, string NombreCategoria, string TipoCategoria);

/// <summary>
/// El tipo de movimiento no se puede cambiar al editar (cada pantalla ya está fijada a
/// 'ingreso' o 'egreso'); solo se editan los datos del movimiento en sí.
/// </summary>
public record UpdateMovimientoRequest(
    DateOnly Fecha,
    [Range(0.01, 999999999999.99, ErrorMessage = "El monto debe ser mayor a 0.")]
    decimal Monto,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona una cuenta.")]
    long IdCuenta,
    [Range(1, long.MaxValue, ErrorMessage = "Selecciona una categoría.")]
    long IdCategoria,
    long? IdTercero,
    [Required, MedioPagoValido] string MedioPago,
    [MaxLength(250)] string? Descripcion
);

/// <summary>
/// Confirmar en false es la primera pasada: si el movimiento tiene pagos aplicados el
/// backend rechaza con un mensaje explicando cuántos, en vez de anular de una. El cliente
/// reintenta con Confirmar en true recién cuando la persona decide seguir igual.
/// </summary>
public record AnularMovimientoRequest(
    [MaxLength(250)] string? Motivo,
    bool Confirmar = false
);

/// <summary>
/// Respuesta paginada del listado de movimientos, con el total para calcular las páginas.
/// FechaDesde y FechaHasta son el rango que de verdad se aplicó (sin rango pedido, el mes en
/// curso de la empresa), y PeriodoPorDefecto dice si ese rango es el mes en curso: así el front
/// no tiene que calcular "el mes actual" con la fecha del navegador.
/// </summary>
public record MovimientosPaginadosResponse(
    IReadOnlyList<MovimientoResponse> Items,
    int TotalResultados,
    int Pagina,
    int TamanoPagina,
    DateOnly? FechaDesde,
    DateOnly? FechaHasta,
    bool PeriodoPorDefecto
);
