using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Pantallas que el front puede mostrar. No exige un permiso de la matriz: el propio
/// listado ya viene filtrado por el rol del usuario en la empresa activa.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pantallas")]
public class PantallasController(PantallasService pantallasService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PantallaResponse>>> List()
    {
        var result = await pantallasService.ListarAsync(User.GetUserId(), User.GetCompanyId());
        return Ok(result);
    }
}
