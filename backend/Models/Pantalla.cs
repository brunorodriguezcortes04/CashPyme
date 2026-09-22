namespace Backend.Models;

/// <summary>Pantalla publicada del backoffice. Tabla: pantalla.</summary>
public class Pantalla
{
    public short Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Route { get; set; }
    public short Order { get; set; }
    public string? RequiredPermission { get; set; }
    public bool IsActive { get; set; } = true;
}
