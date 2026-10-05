namespace Backend.Models;

/// <summary>
/// Saldo de un documento ya neteado: pagos aplicados y notas de crédito/débito.
/// Vista: vista_documento_saldo. Sin clave porque es una vista de solo lectura, igual que
/// SaldoCuenta.
///
/// Se lee de la vista en vez de recalcular el neto en C#: es la misma fuente que usan
/// fn_proyeccion_caja, fn_generar_alertas y el resumen semanal, así que la pantalla no puede
/// mostrar un saldo distinto del que usa la proyección.
/// </summary>
public class SaldoDocumento
{
    public long DocumentId { get; set; }
    public long CompanyId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CreditNotesAmount { get; set; }
    public decimal DebitNotesAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
}
