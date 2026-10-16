using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Lo que antes validaban y recalculaban los triggers de documento, pago y movimiento.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class DocumentosYPagosTests(BaseDePruebas baseDePruebas)
{
    private const string Cobrar = TiposCuentaDocumento.PorCobrar;
    private const string Pagar = TiposCuentaDocumento.PorPagar;

    [SkippableFact]
    public async Task Los_pagos_mueven_el_estado_y_no_pasan_del_saldo_neto()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA pagos");
        var cliente = await e.TerceroAsync("cliente", "Distribuidora Los Andes");

        var factura = await e.DocumentoAsync(Cobrar, cliente, 119_000m, e.Hoy.AddDays(30), "1045");
        await e.NotaAsync(TiposDocumento.NotaCredito, factura, 10_000m);
        var ingreso = await e.MovimientoAsync(TiposMovimiento.Ingreso, 200_000m);

        // El tope es el saldo neto (119.000 - 10.000), no el monto bruto.
        var excede = await Assert.ThrowsAsync<PagoExcedeSaldoException>(() => e.PagarAsync(factura.Id, ingreso, 110_000m));
        Assert.Contains("$109.000", excede.Message);

        await e.PagarAsync(factura.Id, ingreso, 50_000m);
        var parcial = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.PagadoParcial, "Pago parcial", 59_000m),
            (parcial.EstadoDocumento, parcial.EtiquetaEstado, parcial.SaldoPendiente));

        // El mismo movimiento no se aplica dos veces al mismo documento.
        await Assert.ThrowsAsync<PagoDuplicadoException>(() => e.PagarAsync(factura.Id, ingreso, 1_000m));
        // En la API cada petición trae su propio contexto: el pago rechazado no sobrevive a la
        // petición que falló. Acá se comparte el contexto, así que se descarta a mano.
        e.Db.ChangeTracker.Clear();

        var otroIngreso = await e.MovimientoAsync(TiposMovimiento.Ingreso, 59_000m);
        var pagoFinal = await e.PagarAsync(factura.Id, otroIngreso, 59_000m);
        var pagada = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pagado, "Pagada", 0m),
            (pagada.EstadoDocumento, pagada.EtiquetaEstado, pagada.SaldoPendiente));

        // Con pagos no se anula.
        await Assert.ThrowsAsync<DocumentoConPagosAnularException>(
            () => e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, factura.Id));

        // Quitar un pago le devuelve el saldo.
        await e.Servicio<PagosService>().EliminarAsync(e.Id, e.UsuarioId, pagoFinal.Id);
        Assert.Equal(DocumentoFinanciero.PagadoParcial, (await e.DocumentoActualAsync(factura)).EstadoDocumento);

        // Anular el movimiento que pagaba, también: un pago de un movimiento anulado no cuenta.
        await Assert.ThrowsAsync<MovimientoConPagosAnularException>(() => e.Servicio<MovimientosService>()
            .AnularMovimientoAsync(e.Id, e.UsuarioId, ingreso, new AnularMovimientoRequest("prueba")));
        await e.Servicio<MovimientosService>()
            .AnularMovimientoAsync(e.Id, e.UsuarioId, ingreso, new AnularMovimientoRequest("prueba", Confirmar: true));
        var devuelta = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pendiente, 109_000m), (devuelta.EstadoDocumento, devuelta.SaldoPendiente));
    }

    [SkippableFact]
    public async Task El_listado_agrupa_por_situacion_y_el_job_marca_los_vencidos_sin_pagos()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA situacion");
        var cliente = await e.TerceroAsync("cliente", "Cliente");

        var alDia = await e.DocumentoAsync(Cobrar, cliente, 50_000m, e.Hoy.AddDays(10));
        // Venció hace 5 días y nunca se le registró un pago.
        var vencida = await e.DocumentoAsync(Cobrar, cliente, 30_000m, e.Hoy.AddDays(-5), emision: e.Hoy.AddDays(-35));
        // Atrasada pero con abono: sigue en pago parcial, y en el listado cae en vencidas.
        var abonada = await e.DocumentoAsync(Cobrar, cliente, 40_000m, e.Hoy.AddDays(-3), emision: e.Hoy.AddDays(-33));
        await e.PagarAsync(abonada.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 10_000m), 10_000m);
        var pagada = await e.DocumentoAsync(Cobrar, cliente, 20_000m, e.Hoy.AddDays(10));
        await e.PagarAsync(pagada.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 20_000m), 20_000m);

        // Cargada con la fecha ya pasada, nace vencida sin esperar al job.
        Assert.Equal(DocumentoFinanciero.Vencido, vencida.EstadoDocumento);
        Assert.Equal(DocumentoFinanciero.PagadoParcial, (await e.DocumentoActualAsync(abonada)).EstadoDocumento);

        e.Db.ChangeTracker.Clear();
        var servicio = e.Servicio<DocumentosService>();
        async Task<long[]> Ids(string situacion) =>
            (await servicio.ListarAsync(e.Id, Cobrar, null, null, 1, 100, situacion)).Items.Select(d => d.Id).Order().ToArray();

        // Criterio 1: se agrupan bien.
        Assert.Equal([alDia.Id], await Ids(SituacionesDocumento.Pendiente));
        Assert.Equal(new[] { vencida.Id, abonada.Id }.Order(), await Ids(SituacionesDocumento.Vencida));
        Assert.Equal([pagada.Id], await Ids(SituacionesDocumento.Pagada));

        var resumen = await servicio.ResumenAsync(e.Id, Cobrar);
        var vencidas = resumen.Single(r => r.Situacion == SituacionesDocumento.Vencida);
        Assert.Equal((2, 60_000m), (vencidas.Cantidad, vencidas.SaldoPendiente));

        // Criterio 2: un documento que ayer estaba pendiente y hoy vence sin que nadie lo toque.
        // Se simula dejándolo en 'pendiente' por fuera de la API.
        await e.Db.DocumentosFinancieros.Where(d => d.Id == vencida.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DocumentoFinanciero.Pendiente));
        e.Db.ChangeTracker.Clear();

        var job = e.Servicio<VencimientosService>();
        Assert.True(await job.MarcarVencidosAsync() >= 1);
        var marcada = await e.DocumentoActualAsync(vencida);
        Assert.Equal((DocumentoFinanciero.Vencido, "Vencida", SituacionesDocumento.Vencida),
            (marcada.EstadoDocumento, marcada.EtiquetaEstado, marcada.Situacion));

        // Lo al día y lo abonado no se tocan.
        Assert.Equal(DocumentoFinanciero.Pendiente, (await e.DocumentoActualAsync(alDia)).EstadoDocumento);
        Assert.Equal(DocumentoFinanciero.PagadoParcial, (await e.DocumentoActualAsync(abonada)).EstadoDocumento);

        // Pagarlo entero lo saca de vencidas.
        await e.PagarAsync(vencida.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 30_000m), 30_000m);
        e.Db.ChangeTracker.Clear();
        Assert.Contains(vencida.Id, await Ids(SituacionesDocumento.Pagada));
    }

    [SkippableFact]
    public async Task Un_movimiento_cubre_varios_documentos_hasta_su_monto()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA movimiento compartido");
        var cliente = await e.TerceroAsync("cliente", "Cliente");

        var a = await e.DocumentoAsync(Cobrar, cliente, 50m, e.Hoy.AddDays(10));
        var b = await e.DocumentoAsync(Cobrar, cliente, 50m, e.Hoy.AddDays(10));
        var c = await e.DocumentoAsync(Cobrar, cliente, 50m, e.Hoy.AddDays(10));
        var ingreso = await e.MovimientoAsync(TiposMovimiento.Ingreso, 100m);

        await e.PagarAsync(a.Id, ingreso, 50m);
        await e.PagarAsync(b.Id, ingreso, 50m);
        await Assert.ThrowsAsync<MovimientoSinMontoDisponibleException>(() => e.PagarAsync(c.Id, ingreso, 1m));
    }

    [SkippableFact]
    public async Task Las_notas_heredan_tercero_y_fecha_y_no_pasan_del_documento()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA notas");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));

        var nota = await e.NotaAsync(TiposDocumento.NotaCredito, factura, 40_000m);
        Assert.Equal((cliente, e.Hoy, "Vigente"), (nota.IdTercero, nota.FechaVencimiento, nota.EtiquetaEstado));
        Assert.Equal(60_000m, (await e.DocumentoActualAsync(factura)).SaldoPendiente);

        await Assert.ThrowsAsync<NotasCreditoExcedenDocumentoException>(
            () => e.NotaAsync(TiposDocumento.NotaCredito, factura, 60_001m));
        await Assert.ThrowsAsync<NotaSobreNotaException>(() => e.NotaAsync(TiposDocumento.NotaCredito, nota, 1m));

        // Una nota que cubre el resto cierra el documento sin que entre plata.
        var cierre = await e.NotaAsync(TiposDocumento.NotaCredito, factura, 60_000m);
        var cerrada = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pagado, "Cerrada por nota"), (cerrada.EstadoDocumento, cerrada.EtiquetaEstado));

        // Anular la nota le devuelve el saldo al documento.
        await e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, cierre.Id);
        var reabierta = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pendiente, 60_000m), (reabierta.EstadoDocumento, reabierta.SaldoPendiente));

        // Una de débito sube el saldo.
        await e.NotaAsync(TiposDocumento.NotaDebito, factura, 5_000m);
        Assert.Equal(65_000m, (await e.DocumentoActualAsync(factura)).SaldoPendiente);
    }

    [SkippableFact]
    public async Task Un_documento_no_baja_de_lo_que_ya_tiene_comprometido()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA editar monto");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        await e.NotaAsync(TiposDocumento.NotaCredito, factura, 20_000m);
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 50_000m), 50_000m);

        await Assert.ThrowsAsync<MontoMenorAComprometidoException>(() => e.ActualizarAsync(factura, monto: 69_999m));

        // Bajar justo a lo comprometido lo deja pagado.
        var ajustada = await e.ActualizarAsync(factura, monto: 70_000m);
        Assert.Equal((DocumentoFinanciero.Pagado, 0m), (ajustada.EstadoDocumento, ajustada.SaldoPendiente));
    }

    [SkippableFact]
    public async Task Una_cuenta_por_pagar_es_con_proveedores_y_se_paga_con_egresos()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA por pagar");
        var cliente = await e.TerceroAsync("cliente", "Solo cliente");
        var proveedor = await e.TerceroAsync("proveedor", "Insumos del Sur");
        var ambos = await e.TerceroAsync("ambos", "Socio comercial");
        var documentos = e.Servicio<DocumentosService>();

        var elegibles = await documentos.ListarTercerosAsync(e.Id, Pagar);
        Assert.Equal([proveedor, ambos], elegibles.Select(t => t.Id).Order());
        var paraCobrar = await documentos.ListarTercerosAsync(e.Id, Cobrar);
        Assert.Equal([cliente, ambos], paraCobrar.Select(t => t.Id).Order());

        await Assert.ThrowsAsync<TerceroNoEsProveedorException>(
            () => e.DocumentoAsync(Pagar, cliente, 10_000m, e.Hoy.AddDays(5)));

        var factura = await e.DocumentoAsync(Pagar, proveedor, 250_000m, e.Hoy.AddDays(5));
        var otra = await e.DocumentoAsync(Pagar, ambos, 1_000m, e.Hoy.AddDays(5));
        await e.NotaAsync(TiposDocumento.NotaCredito, factura, 1_000m);

        // Corregibles: solo las cuentas por pagar que no son notas ni están anuladas, del tercero pedido.
        var corregibles = await documentos.ListarCorregiblesAsync(e.Id, Pagar, proveedor);
        var corregible = Assert.Single(corregibles);
        Assert.Equal((factura.Id, 249_000m), (corregible.Id, corregible.SaldoPendiente));
        Assert.Equal(2, (await documentos.ListarCorregiblesAsync(e.Id, Pagar, null)).Count);
        await documentos.AnularAsync(e.Id, e.UsuarioId, otra.Id);
        Assert.Single(await documentos.ListarCorregiblesAsync(e.Id, Pagar, null));
        Assert.Empty(await documentos.ListarCorregiblesAsync(e.Id, Cobrar, null));

        // Un ingreso no paga una cuenta por pagar.
        var ingreso = await e.MovimientoAsync(TiposMovimiento.Ingreso, 249_000m);
        await Assert.ThrowsAsync<MovimientoNoAplicableException>(() => e.PagarAsync(factura.Id, ingreso, 1_000m));

        var egreso = await e.MovimientoAsync(TiposMovimiento.Egreso, 249_000m);
        await e.PagarAsync(factura.Id, egreso, 249_000m);
        var pagada = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pagado, "Pagada"), (pagada.EstadoDocumento, pagada.EtiquetaEstado));
    }

    [SkippableFact]
    public async Task Dos_pagos_simultaneos_no_pasan_ambos_el_control_de_saldo()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA concurrencia");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        var ingresoA = await e.MovimientoAsync(TiposMovimiento.Ingreso, 60_000m);
        var ingresoB = await e.MovimientoAsync(TiposMovimiento.Ingreso, 60_000m);

        // Dos peticiones, cada una con su conexión: 60.000 + 60.000 pasa del saldo de 100.000.
        // Sin el bloqueo del documento las dos verían saldo 100.000 y pasarían.
        for (var intento = 0; intento < 3; intento++)
        {
            using var scopeA = sp.CreateScope();
            using var scopeB = sp.CreateScope();
            var a = e.En(scopeA.ServiceProvider);
            var b = e.En(scopeB.ServiceProvider);

            var resultados = await Task.WhenAll(
                Intentar(() => a.PagarAsync(factura.Id, ingresoA, 60_000m)),
                Intentar(() => b.PagarAsync(factura.Id, ingresoB, 60_000m)));

            Assert.Single(resultados, r => r is null);
            Assert.IsType<PagoExcedeSaldoException>(Assert.Single(resultados, r => r is not null));

            var pagada = await e.DocumentoActualAsync(factura);
            Assert.Equal(40_000m, pagada.SaldoPendiente);

            // Se deshace para repetir la carrera.
            var pagos = await e.Servicio<PagosService>().ListarDeDocumentoAsync(e.Id, factura.Id);
            await e.Servicio<PagosService>().EliminarAsync(e.Id, e.UsuarioId, Assert.Single(pagos).Id);
        }
    }

    private static async Task<Exception?> Intentar(Func<Task> accion)
    {
        try
        {
            await accion();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
