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
    DateTime? FechaAnulacion,
    DateTime FechaCreacion
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