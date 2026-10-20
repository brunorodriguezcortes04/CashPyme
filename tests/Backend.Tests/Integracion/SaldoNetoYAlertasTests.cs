using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Los mismos escenarios que probaban database/pruebas/*.sql contra fn_saldo_neto_proyectado y
/// fn_generar_alertas, ahora contra los servicios que las reemplazan.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class SaldoNetoYAlertasTests(BaseDePruebas baseDePruebas)
{
    private const string Cobrar = TiposCuentaDocumento.PorCobrar;
    private const string Pagar = TiposCuentaDocumento.PorPagar;

    [SkippableFact]
    public async Task Saldo_neto_criterios_y_bordes()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA saldo neto");
        var dashboard = e.Servicio<DashboardService>();

        // Criterio 2: sin documentos el neto es la caja.
        await e.SaldoInicialAsync(e.CajaId, 500_000m);
        Assert.Equal(new SaldoNetoResponse(500_000m, 0m, 0m, 500_000m, 0, 0), await dashboard.ObtenerSaldoNetoAsync(e.Id));

        // Criterio 1: 500.000 + 300.000 - 200.000 = 600.000.
        var cliente = await e.TerceroAsync("cliente", "Cliente de prueba");
        var proveedor = await e.TerceroAsync("proveedor", "Proveedor de prueba");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 300_000m, e.Hoy.AddDays(30));
        var cxp = await e.DocumentoAsync(Pagar, proveedor, 200_000m, e.Hoy.AddDays(30));
        Assert.Equal(new SaldoNetoResponse(500_000m, 300_000m, 200_000m, 600_000m, 1, 1),
            await dashboard.ObtenerSaldoNetoAsync(e.Id));

        // Un anulado no cuenta, por grande que sea.
        var grande = await e.DocumentoAsync(Cobrar, cliente, 999_999m, e.Hoy.AddDays(30), tipoDocumento: "otro");
        await e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, grande.Id);
        Assert.Equal(600_000m, (await dashboard.ObtenerSaldoNetoAsync(e.Id)).SaldoNeto);

        // Cobrar no mueve el neto: la plata sale de "por cobrar" y entra a la caja.
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 100_000m), 100_000m);
        var cobrado = await dashboard.ObtenerSaldoNetoAsync(e.Id);
        Assert.Equal((600_000m, 200_000m, 600_000m), (cobrado.SaldoCaja, cobrado.PorCobrar, cobrado.SaldoNeto));

        // Pagar tampoco.
        await e.PagarAsync(cxp.Id, await e.MovimientoAsync(TiposMovimiento.Egreso, 200_000m), 200_000m);
        var pagado = await dashboard.ObtenerSaldoNetoAsync(e.Id);
        Assert.Equal((400_000m, 0m, 600_000m), (pagado.SaldoCaja, pagado.PorPagar, pagado.SaldoNeto));

        // Una nota de crédito resta y no cuenta como documento.
        await e.NotaAsync(TiposDocumento.NotaCredito, factura, 50_000m);
        var conNota = await dashboard.ObtenerSaldoNetoAsync(e.Id);
        Assert.Equal((150_000m, 550_000m, 1), (conNota.PorCobrar, conNota.SaldoNeto, conNota.DocumentosPorCobrar));

        // Una cuenta desactivada no entra a la caja.
        var cuentas = e.Servicio<CuentasService>();
        var cerrada = await cuentas.CrearAsync(e.Id, e.UsuarioId,
            new CreateCuentaRequest("Cuenta cerrada", "cuenta_corriente", null, null, 1_000_000m));
        await cuentas.CambiarEstadoAsync(e.Id, e.UsuarioId, cerrada.Id, activo: false);
        Assert.Equal(550_000m, (await dashboard.ObtenerSaldoNetoAsync(e.Id)).SaldoNeto);
    }

    [SkippableFact]
    public async Task Caja_actual_criterios_y_bordes()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA caja actual");
        var dashboard = e.Servicio<DashboardService>();
        var cuentas = e.Servicio<CuentasService>();
        var inicioMes = new DateOnly(e.Hoy.Year, e.Hoy.Month, 1);

        await e.SaldoInicialAsync(e.CajaId, 100_000m);
        var banco = await cuentas.CrearAsync(e.Id, e.UsuarioId,
            new CreateCuentaRequest("Banco", "cuenta_corriente", null, null, 400_000m));

        // Criterio 1: los del mes anterior mueven el saldo pero no el período; lo anulado no cuenta.
        await e.MovimientoAsync(TiposMovimiento.Ingreso, 80_000m, fecha: inicioMes);
        await e.MovimientoAsync(TiposMovimiento.Egreso, 30_000m, banco.Id);
        await e.MovimientoAsync(TiposMovimiento.Ingreso, 20_000m);
        await e.MovimientoAsync(TiposMovimiento.Egreso, 999m, fecha: inicioMes.AddDays(-1));
        var anulado = await e.MovimientoAsync(TiposMovimiento.Ingreso, 777_777m);
        await e.Servicio<MovimientosService>().AnularMovimientoAsync(e.Id, e.UsuarioId, anulado, new AnularMovimientoRequest(null));

        var caja = await dashboard.ObtenerCajaActualAsync(e.Id, null, null);
        Assert.Equal((inicioMes, inicioMes.AddMonths(1).AddDays(-1)), (caja.Desde, caja.Hasta));
        Assert.Equal((100_000m, 30_000m, 70_000m), (caja.Ingresos, caja.Egresos, caja.FlujoNeto));
        Assert.Equal(caja.Ingresos, caja.FlujoDiario.Sum(d => d.Ingresos));
        Assert.Equal(caja.Egresos, caja.FlujoDiario.Sum(d => d.Egresos));

        // Criterio 2: el saldo total suma todas las cuentas activas (caja 199.001 + banco 370.000).
        Assert.Equal(2, caja.Cuentas.Count);
        Assert.Equal(569_001m, caja.SaldoTotal);
        Assert.Equal(caja.SaldoTotal, caja.Cuentas.Sum(c => c.SaldoActual));
        Assert.Equal(await e.Servicio<SaldosService>().CajaDeEmpresaAsync(e.Id), caja.SaldoTotal);

        // Una cuenta desactivada no entra.
        await cuentas.CambiarEstadoAsync(e.Id, e.UsuarioId, banco.Id, activo: false);
        Assert.Equal(199_001m, (await dashboard.ObtenerCajaActualAsync(e.Id, null, null)).SaldoTotal);

        // El mes anterior, pedido a mano.
        var anterior = inicioMes.AddDays(-1);
        var mesAnterior = await dashboard.ObtenerCajaActualAsync(e.Id, anterior.Year, anterior.Month);
        Assert.Equal((0m, 999m), (mesAnterior.Ingresos, mesAnterior.Egresos));

        await Assert.ThrowsAsync<PeriodoInvalidoException>(() => dashboard.ObtenerCajaActualAsync(e.Id, 2026, 13));
    }

    [SkippableFact]
    public async Task Alertas_de_vencimiento_criterios_y_bordes()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA alertas");
        var alertas = e.Servicio<AlertasService>();
        var hoy = e.Hoy;
        const string proximo = TiposAlerta.VencimientoProximo;

        // La historia pide "próximos 7 días": es lo que trae una empresa nueva.
        Assert.Equal(7, await e.Db.Companies.Where(c => c.Id == e.Id).Select(c => c.DueDateWarningDays).SingleAsync());

        var cliente = await e.TerceroAsync("cliente", "Distribuidora Los Andes");
        var proveedor = await e.TerceroAsync("proveedor", "Insumos del Sur");

        // Criterio 1: vence en 5 días -> alerta descriptiva.
        var doc = await e.DocumentoAsync(Cobrar, cliente, 119_000m, hoy.AddDays(5), "1045");
        await alertas.GenerarAsync(e.Id);
        var alerta = Assert.Single(await e.AlertasAsync(doc.Id, proximo));
        Assert.Equal((SeveridadesAlerta.Advertencia, (DateOnly?)hoy.AddDays(5)), (alerta.Severity, alerta.ReferenceDate));
        Assert.Equal(ReglasAlertas.MensajeVencimientoProximo(Cobrar, "Distribuidora Los Andes", "1045", hoy.AddDays(5), 119_000m),
            alerta.Message);
        Assert.Contains("$119.000", alerta.Message);

        // Criterio 2: correr el job otra vez no duplica.
        Assert.Equal(0, await alertas.GenerarAsync(e.Id));
        await alertas.GenerarAsync(e.Id);
        Assert.Single(await e.AlertasAsync(doc.Id, proximo));

        // Bordes de la ventana: hoy y a 7 días sí, a 8 no.
        var aSiete = await e.DocumentoAsync(Cobrar, cliente, 1_000m, hoy.AddDays(7), tipoDocumento: "boleta");
        var aOcho = await e.DocumentoAsync(Cobrar, cliente, 1_000m, hoy.AddDays(8), tipoDocumento: "boleta");
        var deHoy = await e.DocumentoAsync(Cobrar, cliente, 1_000m, hoy, tipoDocumento: "boleta");
        await alertas.GenerarAsync(e.Id);
        Assert.Single(await e.AlertasAsync(aSiete.Id, proximo));
        Assert.Empty(await e.AlertasAsync(aOcho.Id, proximo));
        Assert.Single(await e.AlertasAsync(deHoy.Id, proximo));

        // Lo vencido no avisa como próximo: tiene su alerta crítica. Lo anulado y las notas no avisan.
        var vencido = await e.DocumentoAsync(Cobrar, cliente, 50_000m, hoy.AddDays(-2), emision: hoy.AddDays(-10));
        var anulado = await e.DocumentoAsync(Cobrar, cliente, 70_000m, hoy.AddDays(2));
        await e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, anulado.Id);
        var nota = await e.NotaAsync(TiposDocumento.NotaCredito, doc, 10_000m);
        await alertas.GenerarAsync(e.Id);
        Assert.Empty(await e.AlertasAsync(vencido.Id, proximo));
        Assert.Equal(SeveridadesAlerta.Critica,
            Assert.Single(await e.AlertasAsync(vencido.Id, TiposAlerta.DocumentoVencido)).Severity);
        Assert.Empty(await e.Db.Alertas.Where(a => a.DocumentId == anulado.Id || a.DocumentId == nota.Id).ToListAsync());

        // También avisa lo que hay que pagar.
        var cxp = await e.DocumentoAsync(Pagar, proveedor, 250_000m, hoy.AddDays(3));
        await alertas.GenerarAsync(e.Id);
        var dePago = Assert.Single(await e.AlertasAsync(cxp.Id, proximo));
        Assert.StartsWith("Pago de Insumos del Sur", dePago.Message);
        Assert.Contains("$250.000", dePago.Message);

        // Cambio de fecha: la alerta pasa a la fecha nueva, sin dejar la vieja abierta.
        doc = await e.ActualizarAsync(await e.DocumentoActualAsync(doc), vence: hoy.AddDays(6));
        await alertas.GenerarAsync(e.Id);
        var abiertas = (await e.AlertasAsync(doc.Id, proximo)).Where(a => a.Status != Alerta.Resuelta).ToList();
        Assert.Equal(hoy.AddDays(6), Assert.Single(abiertas).ReferenceDate);

        // Renegociado fuera de la ventana se resuelve; si vuelve a entrar, avisa de nuevo.
        doc = await e.ActualizarAsync(doc, vence: hoy.AddDays(20));
        await alertas.GenerarAsync(e.Id);
        Assert.DoesNotContain(await e.AlertasAsync(doc.Id, proximo), a => a.Status != Alerta.Resuelta);
        doc = await e.ActualizarAsync(doc, vence: hoy.AddDays(4));
        await alertas.GenerarAsync(e.Id);
        Assert.Single(await e.AlertasAsync(doc.Id, proximo), a => a.Status != Alerta.Resuelta && a.ReferenceDate == hoy.AddDays(4));

        // Un vencido renegociado al futuro cierra su alerta de vencido.
        await e.ActualizarAsync(await e.DocumentoActualAsync(vencido), vence: hoy.AddDays(30));
        await alertas.GenerarAsync(e.Id);
        Assert.DoesNotContain(await e.AlertasAsync(vencido.Id, TiposAlerta.DocumentoVencido), a => a.Status != Alerta.Resuelta);

        // Una alerta leída no se duplica, y cobrar el documento entero la resuelve.
        var abierta = Assert.Single(await e.AlertasAsync(doc.Id, proximo), a => a.Status == Alerta.Pendiente);
        await alertas.MarcarLeidaAsync(e.Id, e.UsuarioId, abierta.Id);
        await alertas.GenerarAsync(e.Id);
        Assert.Single(await e.AlertasAsync(doc.Id, proximo), a => a.Status != Alerta.Resuelta);

        // 119.000 menos la nota de crédito de 10.000.
        await e.PagarAsync(doc.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 109_000m), 109_000m);
        await alertas.GenerarAsync(e.Id);
        Assert.DoesNotContain(await e.AlertasAsync(doc.Id, proximo), a => a.Status != Alerta.Resuelta);
    }
}
