using Backend.Dominio;
using Backend.Models;

namespace Backend.Tests.Dominio;

public class CalculoDocumentoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 14);

    [Fact]
    public void El_neto_resta_las_notas_de_credito_y_suma_las_de_debito()
        => Assert.Equal(115_000m, CalculoDocumento.Neto(119_000m, notasCredito: 10_000m, notasDebito: 6_000m));

    [Fact]
    public void El_neto_nunca_queda_negativo()
        => Assert.Equal(0m, CalculoDocumento.Neto(100m, notasCredito: 150m, notasDebito: 0m));

    [Fact]
    public void El_saldo_descuenta_lo_pagado_y_netea_las_notas()
        => Assert.Equal(59_000m, CalculoDocumento.Saldo(
            "factura", DocumentoFinanciero.PagadoParcial, 119_000m, pagado: 50_000m, notasCredito: 10_000m, notasDebito: 0m));

    [Theory]
    [InlineData(TiposDocumento.NotaCredito)]
    [InlineData(TiposDocumento.NotaDebito)]
    public void Una_nota_no_tiene_saldo_propio(string tipo)
        => Assert.Equal(0m, CalculoDocumento.Saldo(tipo, DocumentoFinanciero.Pendiente, 50_000m, 0m, 0m, 0m));

    [Fact]
    public void Un_anulado_no_tiene_saldo()
        => Assert.Equal(0m, CalculoDocumento.Saldo("factura", DocumentoFinanciero.Anulado, 50_000m, 0m, 0m, 0m));

    [Theory]
    [InlineData(100, 0, DocumentoFinanciero.Pendiente)]
    [InlineData(100, 40, DocumentoFinanciero.PagadoParcial)]
    [InlineData(100, 100, DocumentoFinanciero.Pagado)]
    // Cubierto entero por notas de crédito: no queda nada por cobrar aunque no haya entrado plata.
    [InlineData(0, 0, DocumentoFinanciero.Pagado)]
    public void El_estado_sale_de_lo_pagado_contra_el_neto(decimal neto, decimal pagado, string esperado)
        => Assert.Equal(esperado, CalculoDocumento.Estado(DocumentoFinanciero.Pendiente, neto, pagado));

    [Fact]
    public void Un_anulado_no_cambia_de_estado()
        => Assert.Equal(DocumentoFinanciero.Anulado, CalculoDocumento.Estado(DocumentoFinanciero.Anulado, 100m, 100m));

    [Fact]
    public void Vencido_es_tener_saldo_con_la_fecha_ya_pasada()
    {
        Assert.True(CalculoDocumento.EstaVencido(1m, Hoy.AddDays(-1), Hoy));
        Assert.False(CalculoDocumento.EstaVencido(1m, Hoy, Hoy));
        Assert.False(CalculoDocumento.EstaVencido(0m, Hoy.AddDays(-30), Hoy));
        Assert.Equal(3, CalculoDocumento.DiasAtraso(1m, Hoy.AddDays(-3), Hoy));
        Assert.Equal(0, CalculoDocumento.DiasAtraso(0m, Hoy.AddDays(-3), Hoy));
    }

    [Theory]
    [InlineData("factura", DocumentoFinanciero.Pendiente, 0, "Pendiente")]
    [InlineData("factura", DocumentoFinanciero.PagadoParcial, 10, "Pago parcial")]
    [InlineData("factura", DocumentoFinanciero.Pagado, 10, "Pagada")]
    [InlineData("factura", DocumentoFinanciero.Pagado, 0, "Cerrada por nota")]
    [InlineData("factura", DocumentoFinanciero.Anulado, 0, "Anulada")]
    [InlineData(TiposDocumento.NotaCredito, DocumentoFinanciero.Pendiente, 0, "Vigente")]
    [InlineData(TiposDocumento.NotaDebito, DocumentoFinanciero.Anulado, 0, "Anulada")]
    public void La_etiqueta_del_estado_es_la_misma_para_las_dos_pantallas(
        string tipo, string estado, decimal pagado, string esperada)
        => Assert.Equal(esperada, CalculoDocumento.EtiquetaEstado(tipo, estado, pagado));

    [Fact]
    public void El_saldo_de_una_cuenta_cuenta_movimientos_y_transferencias()
        => Assert.Equal(1_150m, CalculoCuenta.SaldoActual(
            saldoInicial: 1_000m, ingresos: 500m, egresos: 200m, transferidoEntrante: 100m, transferidoSaliente: 250m));
}
