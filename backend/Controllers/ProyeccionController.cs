using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Proyección de caja a 30 y 60 días guardada para el dashboard. Verla pide DocumentosVer, como
/// el Resumen y las alertas: la proyección sale de las cuentas por cobrar y por pagar, que el
/// Operador no ve en ninguna otra pantalla. Generarla escribe en la base, así que pide el permiso
/// de gestionar documentos.
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
    /// Pone al día la proyección con los datos de ahora y la devuelve. Si nada cambió desde la
    /// última, devuelve esa en vez de guardar otra igual. También actualiza la alerta de caja
    /// negativa con ese resultado.
    /// </summary>
    [HttpPost]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<ProyeccionResponse>> Generar()
    {
        var result = await proyeccionService.ActualizarAsync(User.GetCompanyId());
        return Ok(result);
    }
}
