namespace Backend.Models;

/// <summary>
/// Aviso generado por el job de alertas (AlertasService.GenerarAsync) o, la de caja negativa, al
/// generar la proyección (AlertasService.ActualizarCajaNegativaAsync). Tabla: alerta.
/// Nadie las crea a mano: los usuarios solo las leen y las marcan como leídas; el paso a
/// 'resuelta' lo hace el mismo proceso que las creó, cuando el motivo deja de aplicar.
/// </summary>
public class Alerta
{
    public const string Pendiente = "pendiente";
    public const string Leida = "leida";
    public const string Resuelta = "resuelta";

    public long Id { get; set; }
    public long CompanyId { get; set; }
    public required string Type { get; set; }
    public long? DocumentId { get; set; }
    public long? AccountId { get; set; }
    public DateOnly? ReferenceDate { get; set; }
    public required string Severity { get; set; }
    public required string Message { get; set; }
    public string Status { get; set; } = Pendiente;
    public DateTime GeneratedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public long? ReadByUserId { get; set; }

    /// <summary>null = todavía no se manda por correo.</summary>
    public DateTime? EmailSentAtUtc { get; set; }
}
