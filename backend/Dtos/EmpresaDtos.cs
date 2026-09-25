using System.ComponentModel.DataAnnotations;
using Backend.Dominio;

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
/// La zona horaria queda fuera: cambia el cálculo de vencimientos de toda la empresa.
/// El RUT sí se puede editar; es opcional, se guarda normalizado (12345678-5) y su dígito
/// verificador lo valida RutValido antes de que el CHECK de la base tenga que rechazarlo.
/// </summary>
public record UpdateEmpresaRequest(
    [Required, MaxLength(150)] string RazonSocial,
    [RutValido, MaxLength(12)] string? Rut,
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

public record RolResponse(short Id, string NombreRol);

/// <summary>
/// Alta de una persona en la empresa activa. No pide contraseña: si el correo todavía no
/// existe en CashPyme, el sistema genera una provisional y la devuelve UNA sola vez para
/// que el administrador se la entregue por fuera (no hay envío de correo todavía).
/// </summary>
public record CrearUsuarioEmpresaRequest(
    [Required, MaxLength(100)] string Nombre,
    [Required, EmailAddress, MaxLength(150)] string Email,
    [Range(1, short.MaxValue, ErrorMessage = "Selecciona un rol.")] short IdRol
);

/// <summary>
/// PasswordProvisional viene en null cuando la persona ya tenía cuenta en CashPyme: en ese
/// caso solo se le dio acceso a esta empresa y conserva la contraseña que ya usaba.
/// </summary>
public record UsuarioCreadoResponse(UsuarioEmpresaResponse Usuario, string? PasswordProvisional);

public record CambiarRolUsuarioRequest(
    [Range(1, short.MaxValue, ErrorMessage = "Selecciona un rol.")] short IdRol
);

public record CambiarEstadoUsuarioRequest(bool Activo);
