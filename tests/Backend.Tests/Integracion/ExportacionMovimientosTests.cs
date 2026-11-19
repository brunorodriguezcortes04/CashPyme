using System.Text;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Services;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>Criterio: el archivo trae exactamente los movimientos filtrados, sin datos de otra empresa.</summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class ExportacionMovimientosIntegracionTests(BaseDePruebas baseDePruebas)
{
    [SkippableFact]
    public async Task La_exportacion_trae_exactamente_el_listado_filtrado_y_solo_de_la_empresa()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA export A");
        var otra = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA export B");
        var movimientos = e.Servicio<MovimientosService>();

        var inicio = new DateOnly(e.Hoy.Year, e.Hoy.Month, 1);
        var banco = await e.Servicio<CuentasService>().CrearAsync(e.Id, e.UsuarioId,
            new CreateCuentaRequest("Banco", "cuenta_corriente", null, null, 0m));
        var egresos = await movimientos.ListCategoriasAsync(e.Id, TiposMovimiento.Egreso);
        var arriendo = egresos.Single(c => c.NombreCategoria == "Arriendo").Id;
        var proveedores = egresos.Single(c => c.NombreCategoria == "Proveedores").Id;

        async Task<long> Mov(EmpresaDePrueba quien, string tipo, decimal monto, DateOnly fecha, string descripcion,
            long? cuenta = null, long? categoria = null)
            => (await quien.Servicio<MovimientosService>().CrearMovimientoAsync(quien.Id, quien.UsuarioId,
                new CreateMovimientoRequest(tipo, fecha, monto, cuenta ?? quien.CajaId,
                    categoria ?? (tipo == TiposMovimiento.Ingreso ? quien.CategoriaIngresoId : quien.CategoriaEgresoId),
                    null, "transferencia", descripcion))).Id;

        // Entran al filtro (egresos de Arriendo en la Caja, este mes):
        await Mov(e, TiposMovimiento.Egreso, 120_000m, inicio, "SI arriendo 1", categoria: arriendo);
        await Mov(e, TiposMovimiento.Egreso, 80_000m, inicio.AddDays(1), "SI arriendo 2", categoria: arriendo);
        // No entran: otro tipo, otra categoría, otra cuenta, otro mes, anulado, otra empresa.
        await Mov(e, TiposMovimiento.Ingreso, 500_000m, inicio, "NO ingreso");
        await Mov(e, TiposMovimiento.Egreso, 9_000m, inicio, "NO otra categoria", categoria: proveedores);
        await Mov(e, TiposMovimiento.Egreso, 7_000m, inicio, "NO otra cuenta", banco.Id, arriendo);
        await Mov(e, TiposMovimiento.Egreso, 6_000m, inicio.AddDays(-1), "NO otro mes", categoria: arriendo);
        var anulado = await Mov(e, TiposMovimiento.Egreso, 5_000m, inicio, "NO anulado", categoria: arriendo);
        await movimientos.AnularMovimientoAsync(e.Id, e.UsuarioId, anulado, new AnularMovimientoRequest(null));
        await Mov(otra, TiposMovimiento.Egreso, 4_000m, inicio, "NO otra empresa");

        var hasta = inicio.AddMonths(1).AddDays(-1);
        var listado = await movimientos.ListMovimientosAsync(
            e.Id, TiposMovimiento.Egreso, inicio, hasta, arriendo, e.CajaId, 1, 100);

        // CSV
        var (csv, nombre, contentType) = await movimientos.ExportarAsync(
            e.Id, "csv", TiposMovimiento.Egreso, inicio, hasta, arriendo, e.CajaId);
        Assert.StartsWith("text/csv", contentType);
        Assert.Equal($"movimientos-egreso-{inicio:yyyy-MM-dd}-a-{hasta:yyyy-MM-dd}.csv", nombre);
        var filasCsv = Encoding.UTF8.GetString(csv[3..])
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(f => f.Split(';')[5])
            .ToList();
        Assert.Equal(listado.Items.Select(m => m.Descripcion), filasCsv);
        Assert.Equal(["SI arriendo 2", "SI arriendo 1"], filasCsv);

        // Excel: lo mismo.
        var (xlsx, _, _) = await movimientos.ExportarAsync(
            e.Id, "xlsx", TiposMovimiento.Egreso, inicio, hasta, arriendo, e.CajaId);
        using var libro = new XLWorkbook(new MemoryStream(xlsx));
        var hoja = libro.Worksheet("Movimientos");
        var filasXlsx = Enumerable.Range(2, hoja.LastRowUsed()!.RowNumber() - 1)
            .Select(f => hoja.Cell(f, 6).GetString())
            .ToList();
        Assert.Equal(filasCsv, filasXlsx);

        // Sin filtros: el mes en curso de la empresa, nada de la otra.
        var (todo, _, _) = await movimientos.ExportarAsync(e.Id, null, null, null, null, null, null);
        var texto = Encoding.UTF8.GetString(todo);
        Assert.Contains("NO ingreso", texto);
        Assert.DoesNotContain("NO otra empresa", texto);
        Assert.DoesNotContain("NO otro mes", texto);
        Assert.DoesNotContain("NO anulado", texto);

        await Assert.ThrowsAsync<FormatoExportacionInvalidoException>(
            () => movimientos.ExportarAsync(e.Id, "pdf", null, null, null, null, null));
        await Assert.ThrowsAsync<RangoFechasInvalidoException>(
            () => movimientos.ExportarAsync(e.Id, "csv", null, hasta, inicio, null, null));
    }
}
