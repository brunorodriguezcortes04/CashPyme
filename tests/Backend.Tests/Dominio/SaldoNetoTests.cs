using Backend.Dominio;
using Backend.Models;

namespace Backend.Tests.Dominio;

/// <summary>Criterios de aceptación de la historia "saldo neto proyectado".</summary>
public class SaldoNetoTests
{
    private static DocumentoParaSaldoNeto Cobrar(decimal saldo, string estado = DocumentoFinanciero.Pendiente)
        => new(TiposCuentaDocumento.PorCobrar, estado, saldo);

    private static DocumentoParaSaldoNeto Pagar(decimal saldo, string estado = DocumentoFinanciero.Pendiente)
        => new(TiposCuentaDocumento.PorPagar, estado, saldo);

    [Fact]
    public void Criterio_1_caja_mas_por_cobrar_menos_por_pagar()
    {
        var r = SaldoNeto.Calcular(500_000m, [Cobrar(300_000m), Pagar(200_000m)]);

        Assert.Equal(new ResultadoSaldoNeto(500_000m, 300_000m, 200_000m, 600_000m, 1, 1), r);
    }

    [Fact]
    public void Criterio_2_sin_documentos_el_neto_es_la_caja()
    {
        var r = SaldoNeto.Calcular(500_000m, []);

        Assert.Equal(500_000m, r.SaldoNeto);
        Assert.Equal(0m, r.PorCobrar);
        Assert.Equal(0m, r.PorPagar);
    }

    [Fact]
    public void Lo_anulado_y_lo_pagado_no_cuentan()
    {
        var r = SaldoNeto.Calcular(500_000m,
        [
            Cobrar(300_000m),
            Cobrar(999_999m, DocumentoFinanciero.Anulado),
            Pagar(200_000m),
            Pagar(50_000m, DocumentoFinanciero.Pagado)
        ]);

        Assert.Equal(600_000m, r.SaldoNeto);
    }

    [Fact]
    public void Suma_el_saldo_pendiente_y_no_el_monto_total()
    {
        // Factura de 300.000 con 100.000 ya cobrados: esos 100.000 están en la caja (600.000).
        // Si sumara el total, el neto subiría a 700.000 solo por cobrar.
        var r = SaldoNeto.Calcular(600_000m, [Cobrar(200_000m, DocumentoFinanciero.PagadoParcial), Pagar(200_000m)]);

        Assert.Equal(600_000m, r.SaldoNeto);
    }

    [Fact]
    public void Las_notas_no_aportan_ni_se_cuentan_como_documento()
    {
        // Una nota pasa el filtro de estado ('pendiente') pero su saldo propio es 0.
        var r = SaldoNeto.Calcular(400_000m, [Cobrar(150_000m), Cobrar(0m)]);

        Assert.Equal(150_000m, r.PorCobrar);
        Assert.Equal(550_000m, r.SaldoNeto);
        Assert.Equal(1, r.DocumentosPorCobrar);
    }
}
