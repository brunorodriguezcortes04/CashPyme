using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Proyección de caja a 30 y 60 días guardada para el dashboard. Pide DocumentosVer, como el
/// Resumen y las alertas: la proyección sale de las cuentas por cobrar y por pagar, que el
/// Operador no ve en ninguna otra pantalla.
/// </summary>
[ApiController]
[Authorize]
[Route("api/proyeccion")]
public class ProyeccionController(ProyeccionFlujoCajaService proyeccionService) : ControllerBase
{
    /// <summary>Última proyección generada, tal como se guardó. 204 si la empresa todavía no tiene ninguna.</summary>
    [HttpGet("ultima")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<ProyeccionResponse>> Ultima()
    {
        var result = await proyeccionService.ObtenerUltimaAsync(User.GetCompanyId());
        return result is null ? NoContent() : Ok(result);
    }

    /// <summary>
    /// Genera una proyección nueva con los datos de ahora, la guarda y la devuelve. También
    /// actualiza la alerta de caja negativa con ese resultado.
    /// </summary>
    [HttpPost]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<ProyeccionResponse>> Generar()
    {
        var result = await proyeccionService.GenerarAsync(User.GetCompanyId());
        return Ok(result);
    }
}
