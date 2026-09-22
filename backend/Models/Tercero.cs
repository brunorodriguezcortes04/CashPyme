namespace Backend.Models;

/// <summary>Cliente y/o proveedor de la empresa. Tabla: tercero.</summary>
public class Tercero
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}
