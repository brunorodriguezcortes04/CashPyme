namespace Backend.Models;

/// <summary>
/// Token de un solo uso enviado por correo. Tabla: token_usuario.
/// Se guarda solo el hash SHA-256: si alguien lee la base no puede usar los enlaces.
/// </summary>
public class TokenUsuario
{
    public const string VerificacionEmail = "verificacion_email";
    public const string ResetPassword = "reset_password";

    public long Id { get; set; }
    public long UserId { get; set; }
    public required string Type { get; set; }
    public required string TokenHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
