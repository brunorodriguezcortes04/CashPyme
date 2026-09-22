using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>Configuración de la empresa activa. Un Operador no tiene acceso (403).</summary>
[ApiController]
[Authorize]
[Route("api/empresa")]
public class EmpresaController(EmpresaService empresaService) : ControllerBase
{
    [HttpGet]
    [RequierePermiso(Permiso.ConfiguracionEmpresaVer)]
    public async Task<ActionResult<EmpresaResponse>> Get()
    {
        var result = await empresaService.ObtenerAsync(User.GetCompanyId());
        return Ok(result);
    }

    [HttpPut]
    [RequierePermiso(Permiso.ConfiguracionEmpresaEditar)]
    public async Task<ActionResult<EmpresaResponse>> Update(UpdateEmpresaRequest request)
    {
        var result = await empresaService.ActualizarAsync(User.GetCompanyId(), request);
        return Ok(result);
    }

    [HttpGet("usuarios")]
    [RequierePermiso(Permiso.GestionUsuarios)]
    public async Task<ActionResult<IReadOnlyList<UsuarioEmpresaResponse>>> Usuarios()
    {
        var result = await empresaService.ListarUsuariosAsync(User.GetCompanyId());
        return Ok(result);
    }
}
