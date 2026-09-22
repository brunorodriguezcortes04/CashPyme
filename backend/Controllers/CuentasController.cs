using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/cuentas")]
public class CuentasController(CuentasService cuentasService) : ControllerBase
{
    /// <summary>
    /// Listar solo pide MovimientosVer porque los formularios de ingreso/egreso necesitan
    /// elegir cuenta. Crear, editar y activar/desactivar exigen CuentasGestionar.
    /// </summary>
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<IReadOnlyList<CuentaResponse>>> List([FromQuery] bool incluirInactivas = false)
    {
        var result = await cuentasService.ListarAsync(User.GetCompanyId(), incluirInactivas);
        return Ok(result);
    }

    [HttpPost]
    [RequierePermiso(Permiso.CuentasGestionar)]
    public async Task<ActionResult<CuentaResponse>> Crear(CreateCuentaRequest request)
    {
        var result = await cuentasService.CrearAsync(User.GetCompanyId(), User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:long}")]
    [RequierePermiso(Permiso.CuentasGestionar)]
    public async Task<ActionResult<CuentaResponse>> Actualizar(long id, UpdateCuentaRequest request)
    {
        var result = await cuentasService.ActualizarAsync(User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpPatch("{id:long}/estado")]
    [RequierePermiso(Permiso.CuentasGestionar)]
    public async Task<ActionResult<CuentaResponse>> CambiarEstado(long id, CambiarEstadoCuentaRequest request)
    {
        var result = await cuentasService.CambiarEstadoAsync(
            User.GetCompanyId(), User.GetUserId(), id, request.Activo);
        return Ok(result);
    }
}
