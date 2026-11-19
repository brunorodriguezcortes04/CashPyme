using Backend.Dominio;
using Backend.Models;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Historias "ver una proyección de mi flujo de caja a 30 y 60 días" y "alerta de riesgo de
/// iliquidez", contra la base. Los datos son sintéticos y están escritos en cada prueba, con el
/// saldo esperado día por día:
///
///   Pyme que cruza a negativo: caja $500.000; cobra $300.000 en 10 días, paga $900.000 en 20
///   días y cobra $400.000 en 45 días. Saldo: 500.000 → 800.000 (día 10) → −100.000 (día 20)
///   → 300.000 (día 45).
///
///   Pyme que no cruza: caja $500.000; cobra $300.000 en 10 días y paga $200.000 en 20 días.
///   Saldo: 500.000 → 800.000 → 600.000. Nunca baja de cero.
/// </summary>
[Collection(ColeccionBase.Nombre)]
[Trait("Tipo", "Integracion")]
public class ProyeccionYAlertaTests(BaseDePruebas baseDePruebas)
{
    private const string Cobrar = TiposCuentaDocumento.PorCobrar;
    private const string Pagar = TiposCuentaDocumento.PorPagar;
    private const string CajaNegativa = TiposAlerta.SaldoNegativoProyectado;

    [SkippableFact]
    public async Task Genera_guarda_y_devuelve_la_proyeccion_a_30_y_60_dias()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA proyeccion guardada");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();
        var hoy = e.Hoy;

        // Sin generar todavía, no hay nada que mostrar.
        Assert.Null(await servicio.ObtenerUltimaAsync(e.Id));

        await CargarPymeQueCruzaAsync(e);
        var generada = await servicio.GenerarAsync(e.Id);

        // Criterio: queda la fecha de generación y el modelo usado.
        Assert.Equal(ModelosProyeccion.DocumentosRecurrentes, generada.ModeloUsado);
        Assert.Equal(ModelosProyeccion.Etiqueta(ModelosProyeccion.DocumentosRecurrentes), generada.EtiquetaModelo);
        Assert.InRange(generada.FechaGeneracion, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

        var treinta = generada.Horizontes.Single(h => h.HorizonteDias == 30);
        var sesenta = generada.Horizontes.Single(h => h.HorizonteDias == 60);
        Assert.Equal(2, generada.Horizontes.Count);

        // 30 días: parte en 500.000, cae a −100.000 el día 20 y termina ahí.
        Assert.Equal((hoy, hoy.AddDays(30), 31), (treinta.Desde, treinta.Hasta, treinta.Dias.Count));
        Assert.Equal((500_000m, -100_000m), (treinta.SaldoInicial, treinta.SaldoFinal));
        Assert.Equal((-100_000m, hoy.AddDays(20)), (treinta.SaldoMinimo, treinta.FechaSaldoMinimo));
        Assert.Equal(hoy.AddDays(20), treinta.PrimerDiaNegativo);
        Assert.Equal(800_000m, treinta.Dias.Single(d => d.Fecha == hoy.AddDays(10)).Saldo);
        Assert.Equal(900_000m, treinta.Dias.Single(d => d.Fecha == hoy.AddDays(20)).Egresos);

        // 60 días: el de 30 es su comienzo, y el cobro del día 45 lo deja en 300.000.
        Assert.Equal((hoy.AddDays(60), 61), (sesenta.Hasta, sesenta.Dias.Count));
        Assert.Equal(treinta.Dias, sesenta.Dias.Take(31));
        Assert.Equal((300_000m, 700_000m, 900_000m), (sesenta.SaldoFinal, sesenta.TotalIngresos, sesenta.TotalEgresos));

        // En la base: una fila por día y horizonte, todas con la misma fecha de generación y modelo.
        var filas = await e.Db.ProyeccionesFlujoCaja.AsNoTracking().Where(p => p.CompanyId == e.Id).ToListAsync();
        Assert.Equal(31 + 61, filas.Count);
        Assert.Equal(31, filas.Count(f => f.HorizonDays == 30));
        Assert.Equal(61, filas.Count(f => f.HorizonDays == 60));
        Assert.All(filas, f => Assert.Equal((generada.FechaGeneracion, ModelosProyeccion.DocumentosRecurrentes), (f.GeneratedAtUtc, f.Model)));

        // La consulta devuelve lo guardado tal cual, sin recalcular: aunque llegue un documento nuevo.
        await e.DocumentoAsync(Cobrar, await e.TerceroAsync("cliente", "Cliente tardío"), 1_000_000m, hoy.AddDays(5));
        var leida = await servicio.ObtenerUltimaAsync(e.Id);
        Assert.NotNull(leida);
        Assert.Equal(generada.FechaGeneracion, leida.FechaGeneracion);
        Assert.Equal(generada.Horizontes.Select(h => h.Dias), leida.Horizontes.Select(h => h.Dias));
        Assert.Equal(-100_000m, leida.Horizontes[0].SaldoMinimo);

        // Generar de nuevo agrega otra generación, y la última pasa a ser esa.
        var nueva = await servicio.GenerarAsync(e.Id);
        Assert.True(nueva.FechaGeneracion > generada.FechaGeneracion);
        var ultima = await servicio.ObtenerUltimaAsync(e.Id);
        Assert.Equal(nueva.FechaGeneracion, ultima!.FechaGeneracion);
        Assert.Null(ultima.Horizontes[0].PrimerDiaNegativo); // el cobro de 1.000.000 lo cubre
    }

    [SkippableFact]
    public async Task Una_proyeccion_que_cruza_a_negativo_crea_la_alerta_critica()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA proyeccion negativa");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();
        var hoy = e.Hoy;

        var (cliente, pago) = await CargarPymeQueCruzaAsync(e);

        // El job de alertas ya no la calcula por su cuenta: sale de la proyección.
        await e.Servicio<AlertasService>().GenerarAsync(e.Id);
        Assert.Empty(await AlertasDeCajaAsync(e));

        // Criterio: al generar la proyección se crea la alerta crítica, con el primer día negativo.
        await servicio.GenerarAsync(e.Id);
        var alerta = Assert.Single(await AlertasDeCajaAsync(e));
        Assert.Equal((SeveridadesAlerta.Critica, Alerta.Pendiente, (DateOnly?)hoy.AddDays(20)),
            (alerta.Severity, alerta.Status, alerta.ReferenceDate));
        Assert.Equal(ReglasAlertas.MensajeSaldoNegativo(hoy.AddDays(20)), alerta.Message);

        // Generar otra vez con lo mismo no la duplica, y el job de alertas no la cierra.
        await servicio.GenerarAsync(e.Id);
        await e.Servicio<AlertasService>().GenerarAsync(e.Id);
        Assert.Single(await AlertasDeCajaAsync(e), a => a.Status != Alerta.Resuelta);

        // Si el pago se mueve, la alerta pasa al nuevo primer día negativo.
        await e.ActualizarAsync(await e.DocumentoActualAsync(pago), vence: hoy.AddDays(25));
        await servicio.GenerarAsync(e.Id);
        var abierta = Assert.Single(await AlertasDeCajaAsync(e), a => a.Status != Alerta.Resuelta);
        Assert.Equal(hoy.AddDays(25), abierta.ReferenceDate);

        // Un cobro nuevo que cubre el pago: la proyección ya no cruza y la alerta se resuelve.
        await e.DocumentoAsync(Cobrar, cliente, 200_000m, hoy.AddDays(15));
        await servicio.GenerarAsync(e.Id);
        Assert.All(await AlertasDeCajaAsync(e), a => Assert.Equal(Alerta.Resuelta, a.Status));
    }

    [SkippableFact]
    public async Task Una_proyeccion_que_no_cruza_no_crea_alerta()
    {
        await using var sp = baseDePruebas.CrearServicios();
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA proyeccion positiva");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();
        var hoy = e.Hoy;

        await e.SaldoInicialAsync(e.CajaId, 500_000m);
        var cliente = await e.TerceroAsync("cliente", "Cliente al día");
        var proveedor = await e.TerceroAsync("proveedor", "Proveedor al día");
        await e.DocumentoAsync(Cobrar, cliente, 300_000m, hoy.AddDays(10));
        await e.DocumentoAsync(Pagar, proveedor, 200_000m, hoy.AddDays(20));

        var generada = await servicio.GenerarAsync(e.Id);

        Assert.All(generada.Horizontes, h => Assert.Null(h.PrimerDiaNegativo));
        Assert.Equal(500_000m, generada.Horizontes.Min(h => h.SaldoMinimo));
        Assert.Equal(600_000m, generada.Horizontes.Single(h => h.HorizonteDias == 60).SaldoFinal);
        Assert.Empty(await AlertasDeCajaAsync(e));

        // Un pago que deja la caja en negativo después del horizonte de 60 días no avisa todavía.
        await e.DocumentoAsync(Pagar, proveedor, 5_000_000m, hoy.AddDays(75));
        await servicio.GenerarAsync(e.Id);
        Assert.Empty(await AlertasDeCajaAsync(e));
    }

    [SkippableFact]
    public async Task El_job_genera_una_proyeccion_por_dia_y_empresa()
    {
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var sp = baseDePruebas.CrearServicios(reloj);
        using var scope = sp.CreateScope();
        var e = await EmpresaDePrueba.RegistrarAsync(baseDePruebas, scope.ServiceProvider, "PRUEBA proyeccion diaria");
        var servicio = e.Servicio<ProyeccionFlujoCajaService>();

        Task<int> GeneracionesAsync() => e.Db.ProyeccionesFlujoCaja
            .Where(p => p.CompanyId == e.Id).Select(p => p.GeneratedAtUtc).Distinct().CountAsync();

        // Sin proyección, el job la genera; pasar de nuevo el mismo día no agrega otra.
        await servicio.GenerarDiariasAsync();
        Assert.Equal(1, await GeneracionesAsync());
        reloj.Avanzar(TimeSpan.FromMinutes(30));
        await servicio.GenerarDiariasAsync();
        Assert.Equal(1, await GeneracionesAsync());

        // Al día siguiente, una nueva.
        reloj.Avanzar(TimeSpan.FromDays(1));
        await servicio.GenerarDiariasAsync();
        Assert.Equal(2, await GeneracionesAsync());
    }

    /// <summary>La pyme que cruza a negativo del resumen de arriba. Devuelve el cliente y el pago grande.</summary>
    private static async Task<(long Cliente, Backend.Dtos.DocumentoResponse Pago)> CargarPymeQueCruzaAsync(EmpresaDePrueba e)
    {
        await e.SaldoInicialAsync(e.CajaId, 500_000m);
        var cliente = await e.TerceroAsync("cliente", "Distribuidora Los Andes");
        var proveedor = await e.TerceroAsync("proveedor", "Insumos del Sur");
        await e.DocumentoAsync(Cobrar, cliente, 300_000m, e.Hoy.AddDays(10));
        var pago = await e.DocumentoAsync(Pagar, proveedor, 900_000m, e.Hoy.AddDays(20));
        await e.DocumentoAsync(Cobrar, cliente, 400_000m, e.Hoy.AddDays(45));
        return (cliente, pago);
    }

    private static Task<List<Alerta>> AlertasDeCajaAsync(EmpresaDePrueba e)
        => e.Db.Alertas.AsNoTracking()
            .Where(a => a.CompanyId == e.Id && a.Type == CajaNegativa)
            .OrderBy(a => a.Id)
            .ToListAsync();

    private sealed class RelojDePrueba(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;
    }
}
