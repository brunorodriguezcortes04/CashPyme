namespace Backend.Models;

/// <summary>Persona que accede a la plataforma. Tabla: usuario.</summary>
public class User
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime? LastAccessAtUtc { get; set; }
    /// <summary>Null mientras la persona no confirme su correo con el enlace de verificación.</summary>
    public DateTime? EmailVerifiedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}
