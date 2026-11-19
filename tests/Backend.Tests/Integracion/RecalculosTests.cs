using Backend.Dominio;
using Backend.Dtos;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Alertas y proyección solo se recalculan cuando cambió algo (RecalculosPendientes), y la tabla
/// de proyecciones no crece sin límite.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class RecalculosTests(BaseDePruebas baseDePruebas)
{
    [SkippableFact]
    public async Task Las_alertas_se_recalculan_solo_si_cambio_algo_o_el_dia()
    {
        var reloj = new RelojAjustable(DateTimeOffset.UtcNow);
        await using var sp = baseDePruebas.CrearServicios(reloj);
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA recálculo alertas");
        var alertas = e.Servicio<AlertasService>();
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var vencida = await e.DocumentoAsync(TiposCuentaDocumento.PorCobrar, cliente, 10_000m, e.Hoy.AddDays(-3), emision: e.Hoy.AddDays(-10));

        Assert.True(await alertas.GenerarAsync(e.Id) > 0);
        var abierta = Assert.Single(await e.AlertasAsync(vencida.Id, TiposAlerta.DocumentoVencido));

        // Por fuera de la API (sin auditoría) la alerta se cierra: si se recalculara, volvería a
        // abrirse una. Sin cambios en la empresa no se recalcula.
        await e.Db.Alertas.Where(a => a.Id == abierta.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, Alerta.Resuelta));
        Assert.Equal(0, await alertas.GenerarAsync(e.Id));

        // Un cambio de verdad (un documento nuevo) sí recalcula todo.
        await e.DocumentoAsync(TiposCuentaDocumento.PorCobrar, cliente, 5_000m, e.Hoy.AddDays(30));
        Assert.True(await alertas.GenerarAsync(e.Id) > 0);
        Assert.Equal(2, (await e.AlertasAsync(vencida.Id, TiposAlerta.DocumentoVencido)).Count);

        // Igual con el cambio de día, aunque nadie toque nada.
        await e.Db.Alertas.Where(a => a.CompanyId == e.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, Alerta.Resuelta));
        Assert.Equal(0, await alertas.GenerarAsync(e.Id));
        reloj.Avanzar(TimeSpan.FromDays(1));
        Assert.True(await alertas.GenerarAsync(e.Id) > 0);
    }

    [SkippableFact]
    public async Task Una_empresa_inactiva_no_genera_alertas()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA empresa inactiva");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var vencida = await e.DocumentoAsync(TiposCuentaDocumento.PorCobrar, cliente, 10_000m, e.Hoy.AddDays(-3), emision: e.Hoy.AddDays(-10));
        await e.Db.Companies.Where(c => c.Id == e.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

        await e.Servicio<AlertasService>().GenerarAsync();

        Assert.Empty(await e.AlertasAsync(vencida.Id, TiposAlerta.DocumentoVencido));
    }

    [SkippableFact]
    public async Task Actualizar_la_proyeccion_sin_cambios_devuelve_la_misma()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA actualizar proyección");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();

        var primera = await servicio.ActualizarAsync(e.Id);
        var segunda = await servicio.ActualizarAsync(e.Id);
        Assert.Equal(primera.FechaGeneracion, segunda.FechaGeneracion);
        Assert.Equal(1, await GeneracionesAsync(e));

        // Con un dato nuevo, una nueva.
        await e.MovimientoAsync(TiposMovimiento.Ingreso, 50_000m);
        var tercera = await servicio.ActualizarAsync(e.Id);
        Assert.NotEqual(primera.FechaGeneracion, tercera.FechaGeneracion);
        Assert.Equal(2, await GeneracionesAsync(e));
    }

    [SkippableFact]
    public async Task Se_conservan_solo_las_ultimas_generaciones()
    {
        var reloj = new RelojAjustable(DateTimeOffset.UtcNow);
        await using var sp = baseDePruebas.CrearServicios(reloj);
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA limpieza proyecciones");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();

        ProyeccionResponse? ultima = null;
        for (var i = 0; i < ProyeccionFlujoCajaService.GeneracionesConservadas + 3; i++)
        {
            ultima = await servicio.GenerarAsync(e.Id);
            reloj.Avanzar(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(ProyeccionFlujoCajaService.GeneracionesConservadas, await GeneracionesAsync(e));
        Assert.Equal(ultima!.FechaGeneracion, (await servicio.ObtenerUltimaAsync(e.Id))!.FechaGeneracion);
    }

    private static Task<int> GeneracionesAsync(EmpresaDePrueba e)
        => e.Db.ProyeccionesFlujoCaja.Where(p => p.CompanyId == e.Id).Select(p => p.GeneratedAtUtc).Distinct().CountAsync();
}
