using System.ComponentModel.DataAnnotations;

namespace Backend.Dtos;

public record RegisterRequest(
    [Required, MaxLength(120)] string BusinessName,
    [Required, EmailAddress, MaxLength(150)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password
);

public record VerificarEmailRequest([Required, MaxLength(100)] string Token);

public record OlvidePasswordRequest([Required, EmailAddress, MaxLength(150)] string Email);

public record RestablecerPasswordRequest(
    [Required, MaxLength(100)] string Token,
    [Required, MinLength(8), MaxLength(100)] string PasswordNueva
);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    bool RememberMe
);

/// <summary>
/// Exige la contraseña actual aunque la sesión ya esté autenticada: si alguien deja el
/// equipo abierto, no debería poder quedarse con la cuenta cambiando la clave. El mínimo
/// de 8 es el mismo del registro.
/// </summary>
public record CambiarPasswordRequest(
    [Required] string PasswordActual,
    [Required, MinLength(8), MaxLength(100)] string PasswordNueva
);

/// <summary>
/// Rol viaja solo para mostrarlo en la UI (ej. el badge del backoffice). La lista de permisos
/// del rol NO se manda: el menú ya sale filtrado desde /api/pantallas y cada endpoint vuelve
/// a autorizar por su cuenta (ver PermisoAuthorizationHandler), así que exponer la matriz
/// completa de permisos en cada login no le servía a nadie, solo daba más información de más.
/// </summary>
public record AuthResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string BusinessName,
    string Email,
    string Rol
);
