namespace Backend.Models;

/// <summary>Pyme cliente (raíz multiempresa). Tabla: empresa.</summary>
public class Company
{
    public long Id { get; set; }
    public required string LegalName { get; set; }
    public bool IsActive { get; set; } = true;
}
