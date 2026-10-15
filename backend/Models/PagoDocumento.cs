namespace Backend.Models;

/// <summary>
/// Enlace entre un movimiento de caja y el documento que cubre. Tabla: pago_documento.
/// FlowType tiene que ser el flujo del documento y también el tipo del movimiento: las dos FK
/// compuestas de la tabla lo exigen, así que un cobro solo puede ser un ingreso y un pago un
/// egreso. Antes lo completaba el trigger fn_validar_pago; ahora lo pone PagosService.
/// </summary>
public class PagoDocumento
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long DocumentId { get; set; }
    public long MovementId { get; set; }
    public required string FlowType { get; set; }
    public decimal AppliedAmount { get; set; }
}
