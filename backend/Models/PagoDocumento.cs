namespace Backend.Models;

public class PagoDocumento
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long DocumentId { get; set; }
    public long MovementId { get; set; }
    public decimal AppliedAmount { get; set; }
}