namespace Backend.Models;

/// <summary>Pyme cliente (raíz multiempresa). Tabla: empresa.</summary>
public class Company
{
    public long Id { get; set; }
    public required string LegalName { get; set; }
    public string? Rut { get; set; }
    public string? BusinessActivity { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    // Los valores por defecto los pone la tabla empresa (ver AppDbContext: ValueGeneratedOnAdd),
    // así que no se repiten acá ni se mandan en el INSERT.
    public string TimeZone { get; set; } = null!;
    public short DueDateWarningDays { get; set; }
    public decimal LowBalanceThreshold { get; set; }
    public bool IsActive { get; set; } = true;
}
