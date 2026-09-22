using Backend.Dominio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Catálogos que llenan los dropdown y los textos del front, para que no los tenga
/// hardcodeados. Son estáticos: no dependen de la empresa ni del rol.
/// </summary>
[ApiController]
[Authorize]
[Route("api/opciones")]
public class OpcionesController : ControllerBase
{
    [HttpGet("medios-pago")]
    public ActionResult<IReadOnlyList<OpcionCatalogo>> MediosPago() => Ok(Dominio.MediosPago.Todos);

    [HttpGet("tipos-cuenta")]
    public ActionResult<IReadOnlyList<OpcionCatalogo>> TiposCuenta() => Ok(Dominio.TiposCuenta.Todos);

    [HttpGet("tipos-movimiento")]
    public ActionResult<IReadOnlyList<TipoMovimientoResponse>> TiposMovimiento()
        => Ok(Dominio.TiposMovimiento.Todos);
}
