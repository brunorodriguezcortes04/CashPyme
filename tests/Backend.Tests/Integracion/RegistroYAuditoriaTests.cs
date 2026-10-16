using Backend.Dominio;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Lo que antes hacían los triggers de la tabla empresa y fn_auditar, ahora en la API.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class RegistroYAuditoriaTests(BaseDePruebas baseDePruebas)
{
    [SkippableFact]
    public async Task Una_empresa_nueva_nace_con_caja_y_categorias_y_queda_auditada()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var empresa = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA registro");

        var categorias = await empresa.Db.CategoriasMovimiento.Where(c => c.CompanyId == empresa.Id).ToListAsync();
        Assert.Equal(DatosBaseEmpresa.Categorias.Count, categorias.Count);

        var cuentas = await empresa.Servicio<CuentasService>().ListarAsync(empresa.Id, incluirInactivas: true);
        var caja = Assert.Single(cuentas);
        Assert.Equal((DatosBaseEmpresa.NombreCuentaBase, DatosBaseEmpresa.TipoCuentaBase, 0m),
            (caja.NombreCuenta, caja.TipoCuenta, caja.SaldoActual));

        var auditoria = await empresa.Db.Auditorias
            .SingleAsync(a => a.Table == "cuenta_financiera" && a.RecordId == empresa.CajaId);
        Assert.Equal("INSERT", auditoria.Action);
        Assert.Equal(empresa.UsuarioId, auditoria.UserId);
        Assert.Equal(empresa.Id, auditoria.CompanyId);
        Assert.Contains("\"despues\"", auditoria.Detail);
        Assert.Contains("\"nombre_cuenta\"", auditoria.Detail);
    }

    [SkippableFact]
    public async Task Un_cambio_queda_auditado_con_el_antes_y_el_despues()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var empresa = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA auditoría");

        await empresa.SaldoInicialAsync(empresa.CajaId, 500_000m);

        var auditoria = await empresa.Db.Auditorias
            .SingleAsync(a => a.Table == "cuenta_financiera" && a.RecordId == empresa.CajaId && a.Action == "UPDATE");
        Assert.Equal(empresa.UsuarioId, auditoria.UserId);
        Assert.Contains("\"antes\"", auditoria.Detail);
        Assert.Contains("500000", auditoria.Detail);
    }

    [SkippableFact]
    public async Task Guardar_una_tabla_auditada_sin_pasar_por_la_auditoria_falla()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var empresa = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA sin auditoría");

        var caja = await empresa.Db.CuentasFinancieras.SingleAsync(c => c.Id == empresa.CajaId);
        caja.InitialBalance = 1m;

        // Es la garantía que daba el trigger: nadie escribe en una tabla auditada sin dejar rastro.
        await Assert.ThrowsAsync<InvalidOperationException>(() => empresa.Db.SaveChangesAsync());
    }
}
