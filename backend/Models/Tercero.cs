namespace Backend.Models;

/// <summary>Cliente y/o proveedor de la empresa. Tabla: tercero.</summary>
public class Tercero
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Type { get; set; }   // 'cliente' | 'proveedor' | 'ambos'
    public required string Name { get; set; }
    public string? Rut { get; set; }
    public string? BusinessActivity { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}