using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

namespace Backend.Dtos;

public record TerceroResponse(
    long Id,
    string TipoTercero,
    string NombreRazonSocial,
    string? Rut,
    string? Giro,
    string? Email,
    string? Telefono,
    string? Direccion,
    bool Activo
);

public record CreateTerceroRequest(
    [Required, TipoTerceroValido] string TipoTercero,
    [Required, MaxLength(150)] string NombreRazonSocial,
    [RutValido, MaxLength(12)] string? Rut,
    [MaxLength(150)] string? Giro,
    [EmailAddress, MaxLength(150)] string? Email,
    [MaxLength(20)] string? Telefono,
    [MaxLength(200)] string? Direccion
);

public record UpdateTerceroRequest(
    [Required, TipoTerceroValido] string TipoTercero,
    [Required, MaxLength(150)] string NombreRazonSocial,
    [RutValido, MaxLength(12)] string? Rut,
    [MaxLength(150)] string? Giro,
    [EmailAddress, MaxLength(150)] string? Email,
    [MaxLength(20)] string? Telefono,
    [MaxLength(200)] string? Direccion
);

public record CambiarEstadoTerceroRequest(bool Activo);