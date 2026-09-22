namespace Backend.Models;

/// <summary>Caja o cuenta bancaria de la empresa. Tabla: cuenta_financiera.</summary>
public class CuentaFinanciera
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? Bank { get; set; }
    public string? Number { get; set; }
    public decimal InitialBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
