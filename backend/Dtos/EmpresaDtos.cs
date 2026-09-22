using System.ComponentModel.DataAnnotations;

namespace Backend.Dtos;

public record EmpresaResponse(
    long Id,
    string RazonSocial,
    string? Rut,
    string? Giro,
    string? Direccion,
    string? Telefono,
    string? EmailContacto,
    string ZonaHoraria,
    short DiasAvisoVencimiento,
    decimal UmbralSaldoBajo
);

/// <summary>
/// El RUT y la zona horaria quedan fuera: el RUT lo valida fn_rut_valido en la base
/// (necesita su propia pantalla con validación de dígito verificador) y la zona horaria
/// cambia el cálculo de vencimientos de toda la empresa.
/// </summary>
public record UpdateEmpresaRequest(
    [Required, MaxLength(150)] string RazonSocial,
    [MaxLength(150)] string? Giro,
    [MaxLength(200)] string? Direccion,
    [MaxLength(20)] string? Telefono,
    [EmailAddress, MaxLength(150)] string? EmailContacto,
    [Range(0, 60, ErrorMessage = "Los días de aviso deben estar entre 0 y 60.")]
    short DiasAvisoVencimiento,
    [Range(0, 999999999999.99, ErrorMessage = "El umbral de saldo bajo no puede ser negativo.")]
    decimal UmbralSaldoBajo
);

public record EmpresaMembresiaResponse(long Id, string RazonSocial, string Rol, bool EsActiva);

public record UsuarioEmpresaResponse(long IdUsuario, string Nombre, string Email, string Rol, bool Activo);
