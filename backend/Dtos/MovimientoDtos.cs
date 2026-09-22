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
    DateTime FechaCreacion
);

public record CategoriaResponse(long Id, string NombreCategoria, string TipoCategoria);
