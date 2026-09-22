namespace Backend.Models;

/// <summary>Catálogo de roles de acceso. Tabla: rol.</summary>
public class Role
{
    public const string Administrator = "Administrador";
    public const string Accountant = "Contador";
    public const string Operator = "Operador";

    public short Id { get; set; }
    public required string Name { get; set; }
}
