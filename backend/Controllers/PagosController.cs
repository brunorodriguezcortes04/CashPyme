using Backend.Dominio;
using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Pagos: enlazan un movimiento de caja ya registrado con el documento que cubre. No mueven
/// plata por su cuenta, así que piden el mismo permiso que gestionar documentos y no uno nuevo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pagos")]
public class PagosController(PagosService pagosService) : ControllerBase
{
    /// <summary>
    /// Movimientos con monto libre que se pueden aplicar a un documento de este tipo de cuenta.
    /// </summary>
    [HttpGet("movimientos-aplicables")]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<IReadOnlyList<MovimientoAplicableResponse>>> MovimientosAplicables(
        [FromQuery, TipoCuentaDocumentoValido] string tipoCuenta)
    {
        var result = await pagosService.ListarMovimientosAplicablesAsync(User.GetCompanyId(), tipoCuenta);
        return Ok(result);
    }

    [HttpGet("documento/{idDocumento:long}")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<IReadOnlyList<PagoResponse>>> DeDocumento(long idDocumento)
    {
        var result = await pagosService.ListarDeDocumentoAsync(User.GetCompanyId(), idDocumento);
        return Ok(result);
    }

    /// <summary>
    /// Aplica un movimiento a un documento. El movimiento tiene que existir y ser del tipo que
    /// corresponde: una cuenta por cobrar se cubre con un ingreso, una por pagar con un egreso.
    /// </summary>
    [HttpPost]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<PagoResponse>> Crear(CreatePagoRequest request)
    {
        var result = await pagosService.CrearAsync(User.GetCompanyId(), User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Quita el pago. El movimiento y el documento quedan intactos: lo que se borra es el enlace
    /// entre los dos, y el estado del documento vuelve atrás solo.
    /// </summary>
    [HttpDelete("{id:long}")]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<IActionResult> Eliminar(long id)
    {
        await pagosService.EliminarAsync(User.GetCompanyId(), User.GetUserId(), id);
        return NoContent();
    }
}
