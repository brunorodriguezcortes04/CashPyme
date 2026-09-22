namespace Backend.Models;

/// <summary>
/// Clasificación de ingresos/egresos, propia de cada empresa. Tabla: categoria_movimiento.
/// Los valores de Type los define Dominio.TiposMovimiento.
/// </summary>
public class CategoriaMovimiento
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public bool IsActive { get; set; } = true;
}
