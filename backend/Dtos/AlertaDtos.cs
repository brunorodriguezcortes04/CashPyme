namespace Backend.Dtos;

/// <summary>
/// TipoCuenta viene del documento, si la alerta es de uno ('por_cobrar' o 'por_pagar'); es null
/// en las alertas de saldo. EtiquetaSeveridad y Ruta (la pantalla donde se resuelve, null si no
/// hay una) los decide ReglasAlertas: el front solo los muestra.
/// </summary>
public record AlertaResponse(
    long Id,
    string TipoAlerta,
    string NivelSeveridad,
    string EtiquetaSeveridad,
    string Mensaje,
    DateOnly? FechaReferencia,
    long? IdDocumento,
    string? TipoCuenta,
    string EstadoAlerta,
    DateTime FechaGeneracion,
    string? Ruta
);
