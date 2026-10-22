using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Services;
using Backend.Services.Reportes;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace Backend.Tests.Integracion;

/// <summary>Indicadores del dashboard y reporte PDF contra la base, con datos de dos meses.</summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class IndicadoresYReporteTests(BaseDePruebas baseDePruebas)
{
    private sealed record Escenario(EmpresaDePrueba Empresa, DateOnly InicioMes, DateOnly InicioAnterior, long IdArriendo);

    /// <summary>
    /// Mes anterior: ingresos 100.000 y egresos 40.000. Mes en curso: ingresos 125.000 y egresos
    /// 50.000 repartidos en tres categorías, más un movimiento anulado que no debe contar.
    /// </summary>
    private static async Task<Escenario> ArmarAsync(BaseDePruebas b, IServiceProvider sp)
    {
        var e = await EmpresaDePrueba.RegistrarAsync(b, sp, "PRUEBA indicadores");
        var inicioMes = new DateOnly(e.Hoy.Year, e.Hoy.Month, 1);
        var inicioAnterior = inicioMes.AddMonths(-1);
        var categorias = await e.Servicio<MovimientosService>().ListCategoriasAsync(e.Id, TiposMovimiento.Egreso);
        long Cat(string nombre) => categorias.Single(c => c.NombreCategoria == nombre).Id;

        async Task Mov(string tipo, decimal monto, DateOnly fecha, long? categoria = null, string? descripcion = null)
            => await e.Servicio<MovimientosService>().CrearMovimientoAsync(e.Id, e.UsuarioId, new CreateMovimientoRequest(
                tipo, fecha, monto, e.CajaId,
                categoria ?? (tipo == TiposMovimiento.Ingreso ? e.CategoriaIngresoId : e.CategoriaEgresoId),
                null, "transferencia", descripcion));

        await Mov(TiposMovimiento.Ingreso, 100_000m, inicioAnterior);
        await Mov(TiposMovimiento.Egreso, 40_000m, inicioAnterior.AddDays(3), Cat("Arriendo"));

        await Mov(TiposMovimiento.Ingreso, 125_000m, inicioMes, descripcion: "Venta del mes");
        await Mov(TiposMovimiento.Egreso, 30_000m, inicioMes, Cat("Proveedores"), "Compra insumos");
        await Mov(TiposMovimiento.Egreso, 15_000m, inicioMes, Cat("Arriendo"));
        await Mov(TiposMovimiento.Egreso, 5_000m, inicioMes, Cat("Servicios básicos"));

        var anulado = await e.MovimientoAsync(TiposMovimiento.Egreso, 999_999m, fecha: inicioMes);
        await e.Servicio<MovimientosService>().AnularMovimientoAsync(e.Id, e.UsuarioId, anulado, new AnularMovimientoRequest(null));

        return new Escenario(e, inicioMes, inicioAnterior, Cat("Arriendo"));
    }

    [SkippableFact]
    public async Task Indicadores_con_datos_de_dos_meses()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var s = await ArmarAsync(baseDePruebas, scope.ServiceProvider);
        var dashboard = s.Empresa.Servicio<DashboardService>();

        var r = await dashboard.ObtenerIndicadoresAsync(s.Empresa.Id, null, null);

        Assert.Equal((s.InicioMes, s.InicioAnterior), (r.Desde, r.DesdeAnterior));
        // Criterio 1: (125.000 − 100.000) / 100.000 = 25 %; (50.000 − 40.000) / 40.000 = 25 %.
        Assert.Equal(new Variacion(125_000m, 100_000m, 25_000m, 25.0m, true), r.Ingresos);
        Assert.Equal(new Variacion(50_000m, 40_000m, 10_000m, 25.0m, false), r.Egresos);
        // Flujo neto: 75.000 contra 60.000 = 25 %.
        Assert.Equal(25.0m, r.FlujoNeto.Porcentaje);

        // Criterio 2: de mayor a menor; el anulado no entra.
        Assert.Equal(["Proveedores", "Arriendo", "Servicios básicos"], r.TopEgresos.Select(c => c.NombreCategoria));
        Assert.Equal([30_000m, 15_000m, 5_000m], r.TopEgresos.Select(c => c.Monto));
        Assert.Equal(60.0m, r.TopEgresos[0].Participacion);
        Assert.Single(r.TopIngresos);

        // El mes anterior pedido a mano no tiene base para comparar.
        var anterior = await dashboard.ObtenerIndicadoresAsync(s.Empresa.Id, s.InicioAnterior.Year, s.InicioAnterior.Month);
        Assert.Null(anterior.Ingresos.Porcentaje);
        Assert.Equal(s.IdArriendo, Assert.Single(anterior.TopEgresos).IdCategoria);
    }

    [SkippableFact]
    public async Task El_reporte_pdf_trae_saldo_movimientos_y_pendientes_del_rango()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var s = await ArmarAsync(baseDePruebas, scope.ServiceProvider);
        var e = s.Empresa;
        var finMes = s.InicioMes.AddMonths(1).AddDays(-1);

        var cliente = await e.TerceroAsync("cliente", "Distribuidora del Sur");
        var proveedor = await e.TerceroAsync("proveedor", "Proveedor Fuera de Rango");
        await e.DocumentoAsync(TiposCuentaDocumento.PorCobrar, cliente, 119_000m, finMes, "1045", emision: s.InicioMes);
        await e.DocumentoAsync(TiposCuentaDocumento.PorPagar, proveedor, 77_000m, finMes.AddDays(5));

        var reportes = e.Servicio<ReportesService>();
        var (pdf, nombre) = await reportes.GenerarPdfAsync(e.Id, s.InicioMes, finMes);
        Assert.Equal(ReglasReporte.NombreArchivo(s.InicioMes, finMes), nombre);

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Concat(documento.GetPages().SelectMany(p => p.Text).Where(c => !char.IsWhiteSpace(c)));
        string Sin(string t) => string.Concat(t.Where(c => !char.IsWhiteSpace(c)));

        // Saldo: al inicio del mes 60.000 (100.000 − 40.000), al cierre 135.000.
        Assert.Contains(Sin("Saldo al inicio $60.000"), texto);
        Assert.Contains(Sin("Saldo al cierre $135.000"), texto);
        // Movimientos del rango, sin los del mes anterior ni el anulado.
        Assert.Contains(Sin("Venta del mes"), texto);
        Assert.Contains(Sin("Compra insumos"), texto);
        Assert.DoesNotContain("999.999", texto);
        Assert.DoesNotContain("$100.000", texto);
        // Pendientes que vencen en el rango; el que vence después no aparece.
        Assert.Contains(Sin("Distribuidora del Sur"), texto);
        Assert.Contains(Sin("Factura N° 1045"), texto);
        Assert.DoesNotContain(Sin("Proveedor Fuera de Rango"), texto);

        await Assert.ThrowsAsync<RangoReporteRequeridoException>(() => reportes.GenerarPdfAsync(e.Id, null, finMes));
        await Assert.ThrowsAsync<RangoFechasInvalidoException>(() => reportes.GenerarPdfAsync(e.Id, finMes, s.InicioMes));
    }
}
