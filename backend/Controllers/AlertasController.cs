using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Alertas generadas por el job. Piden DocumentosVer: la mayoría hablan de cuentas por cobrar y
/// por pagar, que el Operador no ve en ninguna pantalla.
/// </summary>
[ApiController]
[Authorize]
[Route("api/alertas")]
public class AlertasController(AlertasService alertasService) : ControllerBase
{
    [HttpGet]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<IReadOnlyList<AlertaResponse>>> List()
    {
        var result = await alertasService.ListarPendientesAsync(User.GetCompanyId());
        return Ok(result);
    }

    [HttpPost("{id:long}/leida")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<IActionResult> MarcarLeida(long id)
    {
        await alertasService.MarcarLeidaAsync(User.GetCompanyId(), User.GetUserId(), id);
        return NoContent();
    }
}
