using Backend.Security;

namespace Backend.Tests.Dominio;

/// <summary>Bloqueo progresivo del login por cuenta (IntentosLoginService).</summary>
public class IntentosLoginTests
{
    private const string Correo = "ana@pyme.cl";

    private static readonly DateTimeOffset Inicio = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bloquea_al_quinto_fallo_seguido_y_no_antes()
    {
        var reloj = new RelojAjustable(Inicio);
        var intentos = new IntentosLoginService(reloj);

        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear - 1);
        Assert.Null(intentos.BloqueadaHasta(Correo));

        intentos.RegistrarFallo(Correo);
        Assert.Equal(Inicio.AddMinutes(1), intentos.BloqueadaHasta(Correo));
    }

    [Fact]
    public void Cada_bloqueo_seguido_dura_mas_hasta_el_tope()
    {
        var reloj = new RelojAjustable(Inicio);
        var intentos = new IntentosLoginService(reloj);
        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear);

        var duraciones = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            var hasta = intentos.BloqueadaHasta(Correo)!.Value;
            duraciones.Add(hasta - reloj.GetUtcNow());
            reloj.Avanzar(hasta - reloj.GetUtcNow());
            Assert.Null(intentos.BloqueadaHasta(Correo));
            // Pasado el bloqueo, un solo fallo más vuelve a bloquear.
            intentos.RegistrarFallo(Correo);
        }

        Assert.Equal(
            [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)],
            duraciones);
    }

    [Fact]
    public void Los_fallos_viejos_se_olvidan()
    {
        var reloj = new RelojAjustable(Inicio);
        var intentos = new IntentosLoginService(reloj);

        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear - 1);
        reloj.Avanzar(IntentosLoginService.OlvidoDeFallos + TimeSpan.FromSeconds(1));
        intentos.RegistrarFallo(Correo);

        Assert.Null(intentos.BloqueadaHasta(Correo));
    }

    [Fact]
    public void Entrar_bien_borra_los_fallos()
    {
        var intentos = new IntentosLoginService(new RelojAjustable(Inicio));

        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear - 1);
        intentos.RegistrarExito(Correo);
        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear - 1);

        Assert.Null(intentos.BloqueadaHasta(Correo));
    }

    [Fact]
    public void Cada_correo_lleva_su_cuenta()
    {
        var intentos = new IntentosLoginService(new RelojAjustable(Inicio));

        Fallar(intentos, IntentosLoginService.FallosAntesDeBloquear);

        Assert.NotNull(intentos.BloqueadaHasta(Correo));
        Assert.Null(intentos.BloqueadaHasta("otra@pyme.cl"));
    }

    [Fact]
    public void Inventar_correos_no_hace_crecer_la_memoria_sin_tope()
    {
        var intentos = new IntentosLoginService(new RelojAjustable(Inicio));

        for (var i = 0; i < IntentosLoginService.MaximoCuentas + 10; i++)
        {
            intentos.RegistrarFallo($"falso{i}@spam.cl");
        }

        Assert.True(intentos.CuentasRecordadas <= IntentosLoginService.MaximoCuentas);
    }

    private static void Fallar(IntentosLoginService intentos, int veces)
    {
        for (var i = 0; i < veces; i++)
        {
            intentos.RegistrarFallo(Correo);
        }
    }
}
