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
        // Se ponen al día antes de listar: un documento recién creado tiene que aparecer al abrir
        // la vista, sin esperar la próxima vuelta del job (que además solo corre con
        // Notificaciones:Habilitado). Es idempotente, así que no duplica alertas; el correo lo
        // sigue mandando el job, que solo envía las que aún no salieron.
        var companyId = User.GetCompanyId();
        await alertasService.GenerarAsync(companyId);
        var result = await alertasService.ListarPendientesAsync(companyId);
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
