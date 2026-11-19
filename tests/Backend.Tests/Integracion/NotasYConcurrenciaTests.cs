using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Exceptions;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Backend.Tests.Integracion;

/// <summary>
/// Notas de crédito que no pueden dejar un saldo a favor, bloqueos que no alcanzan filas de otra
/// empresa y la regla del último administrador con dos cambios a la vez.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class NotasYConcurrenciaTests(BaseDePruebas baseDePruebas)
{
    private const string Cobrar = TiposCuentaDocumento.PorCobrar;

    [SkippableFact]
    public async Task Una_factura_cobrada_entera_no_admite_notas_de_credito()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA nota sobre pagada");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 100_000m), 100_000m);

        var error = await Assert.ThrowsAsync<NotasCreditoExcedenDocumentoException>(
            () => e.NotaAsync(TiposDocumento.NotaCredito, factura, 100_000m));
        Assert.Equal(0m, error.Disponible);
    }

    [SkippableFact]
    public async Task El_tope_de_una_nota_de_credito_es_lo_que_todavia_se_debe()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA tope nota");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 30_000m), 30_000m);

        // Debe 70.000: un peso más no entra.
        var error = await Assert.ThrowsAsync<NotasCreditoExcedenDocumentoException>(
            () => e.NotaAsync(TiposDocumento.NotaCredito, factura, 70_001m));
        Assert.Equal(70_000m, error.Disponible);

        // Una nota de débito sube lo que se debe, y con eso el tope.
        await e.NotaAsync(TiposDocumento.NotaDebito, factura, 10_000m);
        await e.NotaAsync(TiposDocumento.NotaCredito, factura, 80_000m);

        var cerrada = await e.DocumentoActualAsync(factura);
        Assert.Equal((DocumentoFinanciero.Pagado, 0m), (cerrada.EstadoDocumento, cerrada.SaldoPendiente));
    }

    [SkippableFact]
    public async Task Anular_una_nota_de_debito_ya_cobrada_se_rechaza()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA anular débito");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        var debito = await e.NotaAsync(TiposDocumento.NotaDebito, factura, 20_000m);
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 120_000m), 120_000m);

        var error = await Assert.ThrowsAsync<AnularNotaDebitoDejaSaldoAFavorException>(
            () => e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, debito.Id));
        Assert.Equal(20_000m, error.Sobrante);

        // Sin cobrar lo que sumó la nota, se puede anular.
        var otra = await e.DocumentoAsync(Cobrar, cliente, 50_000m, e.Hoy.AddDays(30));
        var debitoOtra = await e.NotaAsync(TiposDocumento.NotaDebito, otra, 5_000m);
        await e.PagarAsync(otra.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 50_000m), 50_000m);
        await e.Servicio<DocumentosService>().AnularAsync(e.Id, e.UsuarioId, debitoOtra.Id);
        Assert.Equal(DocumentoFinanciero.Pagado, (await e.DocumentoActualAsync(otra)).EstadoDocumento);
    }

    [SkippableFact]
    public async Task Bajar_el_monto_cuenta_las_notas_de_debito()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA monto con débito");
        var cliente = await e.TerceroAsync("cliente", "Cliente");
        var factura = await e.DocumentoAsync(Cobrar, cliente, 100_000m, e.Hoy.AddDays(30));
        await e.NotaAsync(TiposDocumento.NotaDebito, factura, 10_000m);
        await e.PagarAsync(factura.Id, await e.MovimientoAsync(TiposMovimiento.Ingreso, 100_000m), 100_000m);

        // Pagó 100.000 de 110.000: el monto puede bajar hasta 90.000 (90.000 + 10.000 de débito).
        await Assert.ThrowsAsync<MontoMenorAComprometidoException>(() => e.ActualizarAsync(factura, monto: 89_999m));
        var ajustada = await e.ActualizarAsync(factura, monto: 90_000m);
        Assert.Equal((DocumentoFinanciero.Pagado, 0m), (ajustada.EstadoDocumento, ajustada.SaldoPendiente));
    }

    [SkippableFact]
    public async Task Una_empresa_no_puede_anular_ni_bloquear_movimientos_de_otra()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var a = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA empresa A");
        var b = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA empresa B");
        var ingresoDeA = await a.MovimientoAsync(TiposMovimiento.Ingreso, 10_000m);

        await Assert.ThrowsAsync<MovimientoNoEncontradoException>(() => b.Servicio<MovimientosService>()
            .AnularMovimientoAsync(b.Id, b.UsuarioId, ingresoDeA, new AnularMovimientoRequest("ajeno", Confirmar: true)));

        // Con la transacción de B abierta y su "bloqueo" pedido, la fila de A sigue libre.
        using var deB = sp.CreateScope();
        var dbB = deB.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaccion = await dbB.Database.BeginTransactionAsync();
        await dbB.BloquearDeEmpresaAsync(b.Id, "movimiento_financiero", "id_movimiento", [ingresoDeA]);

        using var otro = sp.CreateScope();
        var dbOtro = otro.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaccionOtro = await dbOtro.Database.BeginTransactionAsync();
        // NOWAIT falla al instante si la fila estuviera bloqueada.
        await dbOtro.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM movimiento_financiero WHERE id_movimiento = @id FOR UPDATE NOWAIT",
            new NpgsqlParameter("id", ingresoDeA));
        Assert.Equal(MovimientoFinanciero.Registrado,
            await dbOtro.MovimientosFinancieros.Where(m => m.Id == ingresoDeA).Select(m => m.Status).SingleAsync());
    }

    [SkippableFact]
    public async Task Dos_administradores_que_se_quitan_el_rol_a_la_vez_dejan_uno()
    {
        await using var sp = baseDePruebas.CrearServicios();
        long empresaId, primero, segundo;
        short idContador;
        using (var scope = sp.CreateScope())
        {
            var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA último admin");
            var roles = await e.Db.Roles.ToDictionaryAsync(r => r.Name, r => r.Id);
            var alta = await e.Servicio<EmpresaService>().AgregarUsuarioAsync(e.Id,
                new CrearUsuarioEmpresaRequest("Segunda Admin", $"{Guid.NewGuid():N}@pruebas.cashpyme.cl", roles[Role.Administrator]));
            (empresaId, primero, segundo, idContador) = (e.Id, e.UsuarioId, alta.Usuario.IdUsuario, roles[Role.Accountant]);
        }

        using var a = sp.CreateScope();
        using var b = sp.CreateScope();
        var resultados = await Task.WhenAll(
            Intentar(() => a.ServiceProvider.GetRequiredService<EmpresaService>().CambiarRolAsync(empresaId, primero, idContador)),
            Intentar(() => b.ServiceProvider.GetRequiredService<EmpresaService>().CambiarRolAsync(empresaId, segundo, idContador)));

        Assert.Single(resultados, r => r is null);
        Assert.IsType<UltimoAdministradorException>(Assert.Single(resultados, r => r is not null));

        using var lectura = sp.CreateScope();
        var administradores = await lectura.ServiceProvider.GetRequiredService<AppDbContext>().CompanyMemberships
            .CountAsync(m => m.CompanyId == empresaId && m.IsActive && m.Role.Name == Role.Administrator);
        Assert.Equal(1, administradores);
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
