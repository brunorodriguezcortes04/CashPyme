using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(DashboardService dashboardService) : ControllerBase
{
    /// <summary>
    /// Saldo en caja + por cobrar pendiente - por pagar pendiente, con el desglose. Es una foto
    /// de hoy, no una proyección en el tiempo (eso es ProyeccionCajaService).
    ///
    /// Pide DocumentosVer y no MovimientosVer: muestra totales de cuentas por cobrar y por
    /// pagar, que el Operador no puede ver en ninguna otra pantalla.
    /// </summary>
    [HttpGet("saldo-neto")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<SaldoNetoResponse>> SaldoNeto()
    {
        var result = await dashboardService.ObtenerSaldoNetoAsync(User.GetCompanyId());
        return Ok(result);
    }
}
