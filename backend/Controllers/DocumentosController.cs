using Backend.Dominio;
using Backend.Dtos;
using Backend.Security;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Cuentas por cobrar y por pagar. Un solo controlador para las dos: comparten tabla y el
/// tipo viaja en la query o en el cuerpo, igual que ingresos y egresos en MovimientosController.
/// </summary>
[ApiController]
[Authorize]
[Route("api/documentos")]
public class DocumentosController(DocumentosService documentosService) : ControllerBase
{
    /// <summary>
    /// Listado paginado de un tipo de cuenta, ordenado por vencimiento más próximo. El estado
    /// y el tercero son filtros opcionales. Sin coincidencias responde 200 con una página
    /// vacía, nunca un error.
    /// </summary>
    [HttpGet]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<DocumentosPaginadosResponse>> List(
        [FromQuery] string tipoCuenta,
        [FromQuery] string? estado,
        [FromQuery] long? idTercero,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 20)
    {
        if (!TiposCuentaDocumento.EsValido(tipoCuenta))
        {
            return BadRequest(new { message = "El tipo de cuenta debe ser por_cobrar o por_pagar." });
        }

        var result = await documentosService.ListarAsync(
            User.GetCompanyId(), tipoCuenta, estado, idTercero, pagina, tamanoPagina);
        return Ok(result);
    }

    /// <summary>
    /// Terceros con los que se puede registrar este tipo de cuenta (clientes para cobrar,
    /// proveedores para pagar, y los 'ambos' en las dos). La regla es la misma que se valida al
    /// guardar: el formulario no tiene que conocerla.
    /// </summary>
    [HttpGet("terceros")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<IReadOnlyList<TerceroResponse>>> Terceros([FromQuery] string tipoCuenta)
    {
        if (!TiposCuentaDocumento.EsValido(tipoCuenta))
        {
            return BadRequest(new { message = "El tipo de cuenta debe ser por_cobrar o por_pagar." });
        }

        return Ok(await documentosService.ListarTercerosAsync(User.GetCompanyId(), tipoCuenta));
    }

    /// <summary>
    /// Documentos a los que se les puede emitir una nota de crédito o débito. Con idTercero, solo
    /// los de ese cliente o proveedor.
    /// </summary>
    [HttpGet("corregibles")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<IReadOnlyList<DocumentoCorregibleResponse>>> Corregibles(
        [FromQuery] string tipoCuenta,
        [FromQuery] long? idTercero)
    {
        if (!TiposCuentaDocumento.EsValido(tipoCuenta))
        {
            return BadRequest(new { message = "El tipo de cuenta debe ser por_cobrar o por_pagar." });
        }

        return Ok(await documentosService.ListarCorregiblesAsync(User.GetCompanyId(), tipoCuenta, idTercero));
    }

    /// <summary>
    /// Registra una cuenta por cobrar o por pagar. Nace en estado 'pendiente'; de ahí en
    /// adelante el estado lo mueven los pagos que se le apliquen, no este endpoint.
    /// </summary>
    [HttpPost]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<DocumentoResponse>> Crear(CreateDocumentoRequest request)
    {
        var result = await documentosService.CrearAsync(
            User.GetCompanyId(), User.GetUserId(), request);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Edita un documento que no esté anulado. Bajar el monto total es también la forma de
    /// reflejar una rebaja parcial (devolución, descuento posterior): el saldo pendiente se
    /// recalcula solo. No se puede bajar por debajo de lo ya pagado.
    /// </summary>
    [HttpPut("{id:long}")]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<DocumentoResponse>> Actualizar(long id, UpdateDocumentoRequest request)
    {
        var result = await documentosService.ActualizarAsync(
            User.GetCompanyId(), User.GetUserId(), id, request);
        return Ok(result);
    }

    /// <summary>
    /// "Eliminar" un documento: nunca borra la fila, la anula, y así deja de contar en la
    /// proyección de caja. Si tiene pagos registrados se rechaza con 409 y no hay forma de
    /// forzarlo: primero hay que anular esos movimientos de caja.
    /// </summary>
    [HttpPost("{id:long}/anular")]
    [RequierePermiso(Permiso.DocumentosGestionar)]
    public async Task<ActionResult<DocumentoResponse>> Anular(long id)
    {
        var result = await documentosService.AnularAsync(User.GetCompanyId(), User.GetUserId(), id);
        return Ok(result);
    }

    /// <summary>
    /// Notas de crédito y débito emitidas sobre este documento: de acá sale la diferencia entre
    /// su monto y su saldo.
    /// </summary>
    [HttpGet("{id:long}/notas")]
    [RequierePermiso(Permiso.DocumentosVer)]
    public async Task<ActionResult<IReadOnlyList<DocumentoResponse>>> Notas(long id)
    {
        var result = await documentosService.ListarNotasAsync(User.GetCompanyId(), id);
        return Ok(result);
    }
}
