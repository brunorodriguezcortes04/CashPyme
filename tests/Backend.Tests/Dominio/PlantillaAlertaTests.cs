using Backend.Dominio;
using Backend.Services.Correo;

namespace Backend.Tests.Dominio;

/// <summary>Cada alerta sale en su propio correo, con el mensaje en el asunto.</summary>
public class PlantillaAlertaTests
{
    [Fact]
    public void El_asunto_lleva_la_etiqueta_y_el_mensaje_de_la_alerta()
    {
        var correo = PlantillasCorreo.Alerta("ana@pyme.cl", "Pyme SpA", SeveridadesAlerta.Critica,
            "Cobro de juan venció el 11-10-2026 (saldo $15.000)", "https://cashpyme.cl/backoffice/cuentas-por-cobrar");

        Assert.Equal("ana@pyme.cl", correo.Para);
        Assert.Equal("Urgente: Cobro de juan venció el 11-10-2026 (saldo $15.000)", correo.Asunto);
        Assert.Contains("Nueva alerta en Pyme SpA", correo.Html);
        Assert.Contains("https://cashpyme.cl/backoffice/cuentas-por-cobrar", correo.Html);
        Assert.Contains("[Urgente] Cobro de juan venció el 11-10-2026", correo.Texto);
    }

    [Fact]
    public void El_mensaje_no_puede_inyectar_html()
    {
        var correo = PlantillasCorreo.Alerta("ana@pyme.cl", "Pyme SpA", SeveridadesAlerta.Advertencia,
            "Pago de <b>juan</b> vence pronto", "https://cashpyme.cl/backoffice");

        Assert.StartsWith("Atención: ", correo.Asunto);
        Assert.DoesNotContain("<b>juan</b>", correo.Html);
    }
}
