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
    /// tipo y cuenta. Sin coincidencias responde 200 con lista vacía, nunca un error: el
    /// estado vacío lo decide el front, no el backend.
    /// </summary>
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<IReadOnlyList<MovimientoResponse>>> List(
        [FromQuery] string? tipo,
        [FromQuery] DateOnly? fechaDesde,
        [FromQuery] DateOnly? fechaHasta,
        [FromQuery] long? idCategoria,
        [FromQuery] long? idCuenta)
    {
        var result = await movimientosService.ListMovimientosAsync(
            User.GetCompanyId(), tipo, fechaDesde, fechaHasta, idCategoria, idCuenta);
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

    /// <summary>Solo se puede editar mientras el movimiento no tenga pagos aplicados.</summary>
    [HttpPut("{id:long}")]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Actualizar(long id, UpdateMovimientoRequest request)
    {
        var result = await movimientosService.ActualizarMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }

    /// <summary>
    /// "Eliminar" un movimiento: nunca borra la fila, la anula. Si tiene pagos aplicados,
    /// la primera llamada sin Confirmar se rechaza con el detalle (409) para que la persona
    /// decida antes de romper la trazabilidad de esos pagos.
    /// </summary>
    [HttpPost("{id:long}/anular")]
    [RequierePermiso(Permiso.MovimientosRegistrar)]
    public async Task<ActionResult<MovimientoResponse>> Anular(long id, AnularMovimientoRequest request)
    {
        var result = await movimientosService.AnularMovimientoAsync(
            User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }
}