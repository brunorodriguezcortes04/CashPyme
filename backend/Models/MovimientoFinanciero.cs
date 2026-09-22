namespace Backend.Models;

/// <summary>
/// Ingreso o egreso real de caja. Tabla: movimiento_financiero.
/// Los valores de Type los define Dominio.TiposMovimiento.
/// </summary>
public class MovimientoFinanciero
{
    public const string Registrado = "registrado";
    public const string Anulado = "anulado";

    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long AccountId { get; set; }
    public long CategoryId { get; set; }
    public long? ThirdPartyId { get; set; }
    public required string Type { get; set; }
    public decimal Amount { get; set; }
    public DateOnly MovementDate { get; set; }
    public required string PaymentMethod { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = Registrado;
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public CuentaFinanciera Account { get; set; } = null!;
    public CategoriaMovimiento Category { get; set; } = null!;
}
