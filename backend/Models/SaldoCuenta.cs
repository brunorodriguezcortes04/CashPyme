namespace Backend.Models;

/// <summary>Saldo actual por cuenta. Vista sin clave primaria: vista_saldo_cuenta.</summary>
public class SaldoCuenta
{
    public long AccountId { get; set; }
    public long CompanyId { get; set; }
    public required string AccountName { get; set; }
    public bool IsActive { get; set; }
    public decimal CurrentBalance { get; set; }
}
