using Backend.Security;
using Backend.Services.Reportes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes")]
public class ReportesController(ReportesService reportesService) : ControllerBase
{
    /// <summary>
    /// Reporte PDF del período: saldo de las cuentas, movimientos y cuentas por cobrar y por pagar
    /// pendientes. Desde y hasta son obligatorios (se validan en ReportesService para responder
    /// con un mensaje claro). Pide DocumentosVer porque incluye las cuentas pendientes.
    /// </summary>
    [HttpGet("pdf")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<IActionResult> Pdf([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta)
    {
        var (pdf, nombre) = await reportesService.GenerarPdfAsync(User.GetCompanyId(), desde, hasta);
        return File(pdf, "application/pdf", nombre);
    }
}
