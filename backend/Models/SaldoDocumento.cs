namespace Backend.Models;

/// <summary>
/// Saldo de un documento ya neteado: pagos aplicados y notas de crédito/débito. Lo arma
/// SaldosService con CalculoDocumento; no es una tabla ni una vista (antes era la vista
/// vista_documento_saldo, que se eliminó junto con el resto de la lógica en la base).
/// </summary>
public class SaldoDocumento
{
    public long DocumentId { get; set; }
    public long CompanyId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CreditNotesAmount { get; set; }
    public decimal DebitNotesAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
}
