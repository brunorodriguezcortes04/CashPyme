using Backend.Dominio;
using Backend.Exceptions;
using Backend.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Tests.Dominio;

/// <summary>
/// Lo que antes decidía el front: qué botones ofrece cada fila, cómo se nombran estados y
/// severidades, a qué pantalla lleva una alerta y cuándo avisar sobregiro.
/// </summary>
public class AccionesYEtiquetasTests
{
    [Fact]
    public void Un_documento_con_saldo_admite_todo()
        => Assert.Equal(new AccionesDocumento(true, true, true, true),
            CalculoDocumento.Acciones("factura", DocumentoFinanciero.Pendiente, 100m));

    [Fact]
    public void Un_documento_sin_saldo_no_se_cobra_pero_se_corrige_edita_y_anula()
        => Assert.Equal(new AccionesDocumento(false, true, true, true),
            CalculoDocumento.Acciones("factura", DocumentoFinanciero.Pagado, 0m));

    [Theory]
    [InlineData(TiposDocumento.NotaCredito)]
    [InlineData(TiposDocumento.NotaDebito)]
    public void Una_nota_solo_se_anula(string tipo)
        => Assert.Equal(new AccionesDocumento(false, false, false, true),
            CalculoDocumento.Acciones(tipo, DocumentoFinanciero.Pendiente, 0m));

    [Fact]
    public void Un_documento_anulado_no_admite_nada()
        => Assert.Equal(new AccionesDocumento(false, false, false, false),
            CalculoDocumento.Acciones("factura", DocumentoFinanciero.Anulado, 100m));

    [Fact]
    public void Un_movimiento_con_pagos_no_se_edita_pero_si_se_anula()
    {
        Assert.Equal(new AccionesMovimiento(true, true), ReglasMovimiento.Acciones(MovimientoFinanciero.Registrado, false));
        Assert.Equal(new AccionesMovimiento(false, true), ReglasMovimiento.Acciones(MovimientoFinanciero.Registrado, true));
        Assert.Equal(new AccionesMovimiento(false, false), ReglasMovimiento.Acciones(MovimientoFinanciero.Anulado, false));
        Assert.Equal("Registrado", ReglasMovimiento.EtiquetaEstado(MovimientoFinanciero.Registrado));
        Assert.Equal("Anulado", ReglasMovimiento.EtiquetaEstado(MovimientoFinanciero.Anulado));
    }

    [Fact]
    public void Avisa_sobregiro_solo_bajo_cero()
    {
        Assert.Null(ReglasMovimiento.AdvertenciaSobregiro("Caja", 0m));
        Assert.Equal("La cuenta \"Caja\" quedó con saldo negativo. El movimiento se guardó igual.",
            ReglasMovimiento.AdvertenciaSobregiro("Caja", -1m));
    }

    [Fact]
    public void El_mes_en_curso_va_del_primero_al_ultimo_dia()
    {
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), ReglasMovimiento.MesEnCurso(new DateOnly(2026, 2, 15)));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), ReglasMovimiento.MesEnCurso(new DateOnly(2026, 10, 31)));
    }

    [Theory]
    [InlineData(SeveridadesAlerta.Critica, "Urgente")]
    [InlineData(SeveridadesAlerta.Advertencia, "Atención")]
    [InlineData(SeveridadesAlerta.Info, "Aviso")]
    public void Cada_severidad_tiene_su_etiqueta(string severidad, string etiqueta)
        => Assert.Equal(etiqueta, ReglasAlertas.EtiquetaSeveridad(severidad));

    [Theory]
    [InlineData(TiposAlerta.VencimientoProximo, TiposCuentaDocumento.PorCobrar, "cuentas-por-cobrar")]
    [InlineData(TiposAlerta.DocumentoVencido, TiposCuentaDocumento.PorPagar, "cuentas-por-pagar")]
    [InlineData(TiposAlerta.SaldoBajo, null, "cuentas")]
    [InlineData(TiposAlerta.SaldoNegativoProyectado, null, null)]
    public void Cada_alerta_lleva_a_la_pantalla_donde_se_resuelve(string tipo, string? tipoCuenta, string? ruta)
        => Assert.Equal(ruta, ReglasAlertas.RutaDondeSeResuelve(tipo, tipoCuenta));

    [Fact]
    public void Los_errores_de_validacion_salen_con_su_mensaje()
    {
        var modelo = new ModelStateDictionary();
        modelo.AddModelError("Rut", "El RUT no es válido: revisa el dígito verificador.");
        Assert.Equal("El RUT no es válido: revisa el dígito verificador.", RespuestaValidacion.Mensaje(modelo));
    }

    [Fact]
    public void Un_json_mal_formado_sale_con_un_mensaje_generico()
    {
        var modelo = new ModelStateDictionary();
        modelo.AddModelError("$.montoTotal", "The JSON value could not be converted to System.Decimal.");
        modelo.AddModelError("request", "The request field is required.");
        Assert.Equal(RespuestaValidacion.MensajeGenerico, RespuestaValidacion.Mensaje(modelo));
    }
}
