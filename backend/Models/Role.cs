namespace Backend.Models;

/// <summary>Catálogo de roles de acceso. Tabla: rol.</summary>
public class Role
{
    public const string Administrator = "Administrador";

    public short Id { get; set; }
    public required string Name { get; set; }
}
