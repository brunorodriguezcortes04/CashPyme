using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Lo que las pantallas antes calculaban por su cuenta y ahora reciben armado del backend.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class PantallasDesdeElBackendTests(BaseDePruebas baseDePruebas)
{
    [SkippableFact]
    public async Task Movimientos_avisa_sobregiro_aplica_el_mes_en_curso_y_dice_que_se_puede_hacer()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA movimientos");
        var movimientos = e.Servicio<MovimientosService>();

        // La Caja parte en 0: un egreso la deja sobregirada y la respuesta lo avisa.
        var egreso = await movimientos.CrearMovimientoAsync(e.Id, e.UsuarioId, new CreateMovimientoRequest(
            TiposMovimiento.Egreso, e.Hoy, 1_000m, e.CajaId, e.CategoriaEgresoId, null, "transferencia", null));
        Assert.Equal("La cuenta \"Caja\" quedó con saldo negativo. El movimiento se guardó igual.", egreso.Advertencia);

        var ingreso = await movimientos.CrearMovimientoAsync(e.Id, e.UsuarioId, new CreateMovimientoRequest(
            TiposMovimiento.Ingreso, e.Hoy, 5_000m, e.CajaId, e.CategoriaIngresoId, null, "transferencia", null));
        Assert.Null(ingreso.Advertencia);

        // Un ingreso aplicado a un documento ya no se edita, pero se puede anular.
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(TiposCuentaDocumento.PorCobrar, cliente, 5_000m, e.Hoy.AddDays(10));
        await e.PagarAsync(factura.Id, ingreso.Id, 5_000m);

        // Sin rango, el backend aplica el mes en curso de la empresa y lo informa.
        var pagina = await movimientos.ListMovimientosAsync(e.Id, null, null, null, null, null);
        var (desde, hasta) = ReglasMovimiento.MesEnCurso(e.Hoy);
        Assert.Equal((desde, hasta, true), (pagina.FechaDesde!.Value, pagina.FechaHasta!.Value, pagina.PeriodoPorDefecto));
        Assert.Equal(new AccionesMovimiento(false, true), pagina.Items.Single(m => m.Id == ingreso.Id).Acciones);
        Assert.Equal(new AccionesMovimiento(true, true), pagina.Items.Single(m => m.Id == egreso.Id).Acciones);
        Assert.All(pagina.Items, m => Assert.Equal("Registrado", m.EtiquetaEstado));

        // Otro rango no es el período por defecto; uno al revés se rechaza con mensaje.
        var otro = await movimientos.ListMovimientosAsync(e.Id, null, desde.AddMonths(-1), hasta, null, null);
        Assert.False(otro.PeriodoPorDefecto);
        await Assert.ThrowsAsync<RangoFechasInvalidoException>(
            () => movimientos.ListMovimientosAsync(e.Id, null, hasta, desde, null, null));
    }

    [SkippableFact]
    public async Task Documentos_y_alertas_traen_sus_acciones_etiqueta_y_ruta()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA acciones");
        var proveedor = await e.TerceroAsync("proveedor", "Insumos del Sur");

        var factura = await e.DocumentoAsync(TiposCuentaDocumento.PorPagar, proveedor, 10_000m, e.Hoy.AddDays(3));
        Assert.Equal(new AccionesDocumento(true, true, true, true), factura.Acciones);
        var nota = await e.NotaAsync(TiposDocumento.NotaCredito, factura, 1_000m);
        Assert.Equal(new AccionesDocumento(false, false, false, true), nota.Acciones);

        await e.Servicio<AlertasService>().GenerarAsync(e.Id);
        var alerta = Assert.Single(await e.Servicio<AlertasService>().ListarPendientesAsync(e.Id),
            a => a.TipoAlerta == TiposAlerta.VencimientoProximo);
        Assert.Equal(("Atención", "cuentas-por-pagar"), (alerta.EtiquetaSeveridad, alerta.Ruta));
    }

    [SkippableFact]
    public async Task Los_usuarios_traen_el_id_de_su_rol()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA usuarios");
        var empresas = e.Servicio<EmpresaService>();

        var roles = await empresas.ListarRolesAsync();
        var usuario = Assert.Single(await empresas.ListarUsuariosAsync(e.Id));
        Assert.Equal(roles.Single(r => r.NombreRol == usuario.Rol).Id, usuario.IdRol);
    }
}
