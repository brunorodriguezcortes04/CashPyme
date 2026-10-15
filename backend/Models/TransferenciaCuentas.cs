namespace Backend.Models;

/// <summary>
/// Traspaso de plata entre dos cuentas propias. Tabla: transferencia_cuentas. No es un ingreso
/// ni un egreso de la empresa: solo mueve saldo de una cuenta a otra. Hoy la API solo la lee para
/// calcular saldos (no hay pantalla que la cree).
/// </summary>
public class TransferenciaCuentas
{
    public const string Registrada = "registrada";
    public const string Anulada = "anulada";

    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long SourceAccountId { get; set; }
    public long TargetAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly TransferDate { get; set; }
    public string Status { get; set; } = Registrada;
}
