using Backend.Dominio;
using Backend.Models;

namespace Backend.Tests.Dominio;

/// <summary>Criterios de aceptación de la historia "alerta cuando una cuenta esté por vencer".</summary>
public class ReglasAlertasTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 14);
    private const int DiasAviso = 7;

    [Theory]
    [InlineData(0, true)]   // vence hoy
    [InlineData(5, true)]   // criterio 1
    [InlineData(7, true)]   // último día de la ventana
    [InlineData(8, false)]
    [InlineData(-2, false)] // ya vencido: tiene su propia alerta
    public void Avisa_lo_que_vence_dentro_de_la_ventana(int dias, bool avisa)
        => Assert.Equal(avisa, ReglasAlertas.AvisarVencimientoProximo(119_000m, Hoy.AddDays(dias), Hoy, DiasAviso));

    [Fact]
    public void Sin_saldo_no_avisa()
        // Pagado, anulado o nota: los tres tienen saldo 0.
        => Assert.False(ReglasAlertas.AvisarVencimientoProximo(0m, Hoy.AddDays(3), Hoy, DiasAviso));

    [Fact]
    public void Lo_vencido_con_saldo_tiene_alerta_de_vencido()
    {
        Assert.True(ReglasAlertas.AvisarVencido(50_000m, Hoy.AddDays(-2), Hoy));
        Assert.False(ReglasAlertas.AvisarVencido(50_000m, Hoy, Hoy));
        Assert.False(ReglasAlertas.AvisarVencido(0m, Hoy.AddDays(-2), Hoy));
    }

    private static bool Resolver(
        string tipo, DateOnly referencia, DateOnly vencimiento, decimal saldo = 119_000m,
        string estado = DocumentoFinanciero.Pendiente)
        => ReglasAlertas.ResolverAlertaDocumento(tipo, referencia, estado, saldo, vencimiento, Hoy, DiasAviso);

    [Fact]
    public void Una_alerta_que_sigue_siendo_cierta_queda_abierta()
        => Assert.False(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(5), Hoy.AddDays(5)));

    [Fact]
    public void Se_resuelve_al_pagar_o_anular()
    {
        Assert.True(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(5), Hoy.AddDays(5), saldo: 0m));
        Assert.True(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(5), Hoy.AddDays(5),
            estado: DocumentoFinanciero.Anulado));
    }

    [Fact]
    public void Se_resuelve_si_el_documento_cambio_de_fecha()
        // La alerta habla del 19 y el documento ahora vence el 20: la misma corrida crea la nueva.
        => Assert.True(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(5), Hoy.AddDays(6)));

    [Fact]
    public void Se_resuelve_si_lo_renegociaron_fuera_de_la_ventana()
        => Assert.True(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(20), Hoy.AddDays(20)));

    [Fact]
    public void La_de_vencimiento_proximo_se_resuelve_cuando_vence()
        => Assert.True(Resolver(TiposAlerta.VencimientoProximo, Hoy.AddDays(-1), Hoy.AddDays(-1)));

    [Fact]
    public void La_de_vencido_se_resuelve_si_deja_de_estar_vencido()
    {
        Assert.False(Resolver(TiposAlerta.DocumentoVencido, Hoy.AddDays(-2), Hoy.AddDays(-2)));
        Assert.True(Resolver(TiposAlerta.DocumentoVencido, Hoy.AddDays(30), Hoy.AddDays(30)));
    }

    [Fact]
    public void El_mensaje_dice_quien_cual_cuando_y_cuanto()
        => Assert.Equal(
            "Cobro de Distribuidora Los Andes (N° 1045) vence el 19-10-2026 (saldo $119.000)",
            ReglasAlertas.MensajeVencimientoProximo(
                TiposCuentaDocumento.PorCobrar, "Distribuidora Los Andes", "1045", Hoy.AddDays(5), 119_000m));

    [Fact]
    public void Una_cuenta_por_pagar_avisa_como_pago_y_sin_numero_no_pone_parentesis()
        => Assert.Equal(
            "Pago de Insumos del Sur vence el 17-10-2026 (saldo $250.000)",
            ReglasAlertas.MensajeVencimientoProximo(
                TiposCuentaDocumento.PorPagar, "Insumos del Sur", null, Hoy.AddDays(3), 250_000m));

    [Fact]
    public void El_mensaje_de_vencido_dice_la_fecha_y_no_hace_cuantos_dias()
        => Assert.Equal(
            "Cobro de Distribuidora Los Andes venció el 12-10-2026 (saldo $50.000)",
            ReglasAlertas.MensajeVencido(
                TiposCuentaDocumento.PorCobrar, "Distribuidora Los Andes", " ", Hoy.AddDays(-2), 50_000m));

    [Fact]
    public void Saldo_bajo_solo_en_cuentas_activas_y_critico_bajo_cero()
    {
        Assert.True(ReglasAlertas.AvisarSaldoBajo(50m, 100m, cuentaActiva: true));
        Assert.False(ReglasAlertas.AvisarSaldoBajo(50m, 100m, cuentaActiva: false));
        Assert.True(ReglasAlertas.ResolverSaldoBajo(100m, 100m, cuentaActiva: true));
        Assert.True(ReglasAlertas.ResolverSaldoBajo(50m, 100m, cuentaActiva: false));
        Assert.Equal(SeveridadesAlerta.Advertencia, ReglasAlertas.SeveridadSaldoBajo(50m));
        Assert.Equal(SeveridadesAlerta.Critica, ReglasAlertas.SeveridadSaldoBajo(-1m));
        Assert.Equal("La cuenta Caja tiene un saldo de $50.000, bajo el mínimo de $100.000",
            ReglasAlertas.MensajeSaldoBajo("Caja", 50_000m, 100_000m));
    }

    [Fact]
    public void Primer_dia_negativo_de_la_proyeccion()
    {
        DiaProyectado Dia(int d, decimal saldo) => new(Hoy.AddDays(d), 0m, 0m, saldo);

        Assert.Equal(Hoy.AddDays(2), ReglasAlertas.PrimerDiaNegativo([Dia(0, 10m), Dia(1, 0m), Dia(2, -1m), Dia(3, -5m)]));
        Assert.Null(ReglasAlertas.PrimerDiaNegativo([Dia(0, 10m), Dia(1, 0m)]));
        Assert.Equal("Tu caja podría quedar en negativo el 16-10-2026", ReglasAlertas.MensajeSaldoNegativo(Hoy.AddDays(2)));
    }
}
