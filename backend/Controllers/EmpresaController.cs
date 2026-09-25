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

    /// <summary>Catálogo de roles asignables. Se consulta al armar el formulario de alta.</summary>
    [HttpGet("roles")]
    [RequierePermiso(Permiso.GestionUsuarios)]
    public async Task<ActionResult<IReadOnlyList<RolResponse>>> Roles()
    {
        var result = await empresaService.ListarRolesAsync();
        return Ok(result);
    }

    /// <summary>
    /// Da acceso a una persona con un rol. La contraseña provisional de la respuesta se
    /// muestra una sola vez: no queda guardada en claro en ninguna parte.
    /// </summary>
    [HttpPost("usuarios")]
    [RequierePermiso(Permiso.GestionUsuarios)]
    public async Task<ActionResult<UsuarioCreadoResponse>> AgregarUsuario(CrearUsuarioEmpresaRequest request)
    {
        var result = await empresaService.AgregarUsuarioAsync(User.GetCompanyId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("usuarios/{idUsuario:long}/rol")]
    [RequierePermiso(Permiso.GestionUsuarios)]
    public async Task<ActionResult<UsuarioEmpresaResponse>> CambiarRol(
        long idUsuario, CambiarRolUsuarioRequest request)
    {
        var result = await empresaService.CambiarRolAsync(User.GetCompanyId(), idUsuario, request.IdRol);
        return Ok(result);
    }

    /// <summary>Quitar el acceso desactiva la membresía; nunca borra al usuario.</summary>
    [HttpPatch("usuarios/{idUsuario:long}/estado")]
    [RequierePermiso(Permiso.GestionUsuarios)]
    public async Task<ActionResult<UsuarioEmpresaResponse>> CambiarEstadoUsuario(
        long idUsuario, CambiarEstadoUsuarioRequest request)
    {
        var result = await empresaService.CambiarEstadoUsuarioAsync(
            User.GetCompanyId(), idUsuario, request.Activo);
        return Ok(result);
    }
}
