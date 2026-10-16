using Backend.Dominio;
using Backend.Models;

namespace Backend.Tests.Dominio;

public class SituacionesDocumentoTests
{
    [Theory]
    [InlineData(DocumentoFinanciero.Pendiente, 100, false, SituacionesDocumento.Pendiente)]
    [InlineData(DocumentoFinanciero.Vencido, 100, true, SituacionesDocumento.Vencida)]
    // Con abonos y atrasado: el estado dice pago parcial, pero en el listado es una vencida.
    [InlineData(DocumentoFinanciero.PagadoParcial, 60, true, SituacionesDocumento.Vencida)]
    [InlineData(DocumentoFinanciero.Pagado, 0, false, SituacionesDocumento.Pagada)]
    public void Agrupa_por_saldo_y_vencimiento(string estado, decimal saldo, bool vencido, string esperada)
        => Assert.Equal(esperada, SituacionesDocumento.De("factura", estado, saldo, vencido));

    [Fact]
    public void Las_notas_y_los_anulados_no_caen_en_ningun_grupo()
    {
        Assert.Null(SituacionesDocumento.De(TiposDocumento.NotaCredito, DocumentoFinanciero.Pendiente, 0m, false));
        Assert.Null(SituacionesDocumento.De("factura", DocumentoFinanciero.Anulado, 0m, false));
    }

    [Fact]
    public void Solo_acepta_las_tres_situaciones()
    {
        Assert.True(SituacionesDocumento.EsValida(SituacionesDocumento.Vencida));
        Assert.False(SituacionesDocumento.EsValida("vencido"));
        Assert.False(SituacionesDocumento.EsValida(null));
    }
}
