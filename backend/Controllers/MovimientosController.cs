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

     /// <summary>
    /// Todos los filtros son opcionales y combinables entre sí: rango de fecha, categoría,
    /// tipo y cuenta. Sin filtro de fecha, muestra el mes actual por defecto. Sin
    /// coincidencias responde 200 con una página vacía, nunca un error.
    /// </summary>
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<MovimientosPaginadosResponse>> List(
        [FromQuery] string? tipo,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta,
        [FromQuery] long? idCategoria,
        [FromQuery] long? idCuenta,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 20)
    {
        var result = await movimientosService.ListMovimientosAsync(
            User.GetCompanyId(), tipo, fechaDesde, fechaHasta, idCategoria, idCuenta, pagina, tamanoPagina);
        return Ok(result);
    }

    [HttpPost]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Crear(CreateMovimientoRequest request)
    {
        var result = await movimientosService.CrearMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:long}")]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Actualizar(long id, UpdateMovimientoRequest request)
    {
        var result = await movimientosService.ActualizarMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }

    [HttpPost("{id:long}/anular")]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Anular(long id, AnularMovimientoRequest request)
    {
        var result = await movimientosService.AnularMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }
}