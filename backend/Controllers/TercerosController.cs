using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/terceros")]
public class TercerosController(TercerosService tercerosService) : ControllerBase
{
    /// <summary>
    /// Listar solo pide MovimientosVer porque los formularios de movimientos necesitan
    /// elegir tercero. Crear, editar y activar/desactivar exigen TercerosGestionar.
    /// </summary>
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<IReadOnlyList<TerceroResponse>>> List([FromQuery] bool incluirInactivos = false)
    {
        var result = await tercerosService.ListarAsync(User.GetCompanyId(), incluirInactivos);
        return Ok(result);
    }

    [HttpPost]
    [RequierePermiso(Permiso.TercerosGestionar)]
    public async Task<ActionResult<TerceroResponse>> Crear(CreateTerceroRequest request)
    {
        var result = await tercerosService.CrearAsync(User.GetCompanyId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:long}")]
    [RequierePermiso(Permiso.TercerosGestionar)]
    public async Task<ActionResult<TerceroResponse>> Actualizar(long id, UpdateTerceroRequest request)
    {
        var result = await tercerosService.ActualizarAsync(User.GetCompanyId(), id, request);
        return Ok(result);
    }

    [HttpPatch("{id:long}/estado")]
    [RequierePermiso(Permiso.TercerosGestionar)]
    public async Task<ActionResult<TerceroResponse>> CambiarEstado(long id, CambiarEstadoTerceroRequest request)
    {
        var result = await tercerosService.CambiarEstadoAsync(User.GetCompanyId(), id, request.Activo);
        return Ok(result);
    }
}