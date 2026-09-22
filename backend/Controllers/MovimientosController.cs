using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/movimientos")]
public class MovimientosController(MovimientosService movimientosService) : ControllerBase
{
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<IReadOnlyList<MovimientoResponse>>> List([FromQuery] string? tipo)
    {
        var result = await movimientosService.ListMovimientosAsync(User.GetCompanyId(), tipo);
        return Ok(result);
    }

    /// <summary>
    /// Un solo endpoint para ingresos y egresos: el tipo viaja en el cuerpo y lo valida
    /// TipoMovimientoValido. El signo sobre el saldo lo decide vista_saldo_cuenta.
    /// </summary>
    [HttpPost]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Crear(CreateMovimientoRequest request)
    {
        var result = await movimientosService.CrearMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
