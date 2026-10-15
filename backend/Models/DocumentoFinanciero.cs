namespace Backend.Models;

/// <summary>
/// Cuenta por cobrar o por pagar. Tabla: documento_financiero.
/// Los valores de AccountType los define Dominio.TiposCuentaDocumento y los de DocumentType
/// Dominio.TiposDocumento. Nace 'pendiente' y después el estado lo pone siempre
/// SaldosService.RecalcularEstadosAsync, según lo pagado y las notas: ningún servicio lo escribe
/// a mano, salvo para anular.
/// </summary>
public class DocumentoFinanciero
{
    public const string Pendiente = "pendiente";
    public const string PagadoParcial = "pagado_parcial";
    public const string Pagado = "pagado";
    public const string Anulado = "anulado";

    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long ThirdPartyId { get; set; }
    public long? CategoryId { get; set; }

    /// <summary>
    /// Documento que esta nota corrige. Obligatorio para nota_credito y nota_debito, prohibido
    /// para el resto (ck_doc_nota_referencia).
    /// </summary>
    public long? ReferenceDocumentId { get; set; }

    public required string AccountType { get; set; }
    public required string DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Se inicializa acá además del DEFAULT de la base, igual que MovimientoFinanciero.Status:
    /// así el objeto que se devuelve al crear ya trae el estado correcto sin tener que volver
    /// a leer la fila.
    /// </summary>
    public string Status { get; set; } = Pendiente;

    public string? Description { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Tercero ThirdParty { get; set; } = null!;
    public CategoriaMovimiento? Category { get; set; }
}
