using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Empresas a las que pertenece el usuario y cambio de empresa activa. No exige un permiso
/// de la matriz: el filtro es la propia membresía del usuario.
/// </summary>
[ApiController]
[Authorize]
[Route("api/empresas")]
public class EmpresasController(EmpresaService empresaService, AuthService authService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmpresaMembresiaResponse>>> List()
    {
        var result = await empresaService.ListarMisEmpresasAsync(User.GetUserId(), User.GetCompanyId());
        return Ok(result);
    }

    [HttpPost("{id:long}/activar")]
    public async Task<ActionResult<AuthResponse>> Activar(long id)
    {
        var result = await authService.CambiarEmpresaActivaAsync(User.GetUserId(), id);
        return Ok(result);
    }
}
