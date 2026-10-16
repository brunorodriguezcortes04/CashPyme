namespace Backend.Models;

/// <summary>
/// Quién cambió qué y cuándo. Tabla: auditoria. La llena PersistenciaExtensions al guardar
/// (antes lo hacía el trigger fn_auditar). Detalle guarda la fila completa como JSON con los
/// nombres de columna de la base: {"despues": ...} al crear, {"antes": ..., "despues": ...} al
/// modificar y {"antes": ...} al borrar, igual que el trigger.
/// </summary>
public class Auditoria
{
    public long Id { get; set; }
    public long? CompanyId { get; set; }
    public long? UserId { get; set; }
    public required string Table { get; set; }
    public long? RecordId { get; set; }
    public required string Action { get; set; }   // 'INSERT' | 'UPDATE' | 'DELETE'
    public string? Detail { get; set; }
    public DateTime EventAtUtc { get; set; }
}
