using Backend.Dominio;
using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/categorias")]
public class CategoriasController(MovimientosService movimientosService) : ControllerBase
{
    [HttpGet]
    [RequierePermiso(Permiso.MovimientosVer)]
    public async Task<ActionResult<IReadOnlyList<CategoriaResponse>>> List([FromQuery] string tipo)
    {
        if (!TiposMovimiento.EsValido(tipo))
        {
            return BadRequest(new
            {
                message = $"El parámetro 'tipo' debe ser uno de: {string.Join(", ", TiposMovimiento.Todos.Select(t => t.Valor))}."
            });
        }

        var result = await movimientosService.ListCategoriasAsync(User.GetCompanyId(), tipo);
        return Ok(result);
    }
}
