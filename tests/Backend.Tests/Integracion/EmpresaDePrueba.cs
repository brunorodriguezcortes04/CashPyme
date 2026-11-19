using Backend.Data;
using Backend.Dominio;
using Backend.Dtos;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integracion;

/// <summary>
/// Una empresa recién registrada y atajos para cargarle datos por los mismos servicios que usa
/// la API. Cada prueba arma la suya, así no dependen unas de otras.
/// </summary>
public sealed class EmpresaDePrueba
{
    private readonly IServiceProvider _sp;

    private EmpresaDePrueba(IServiceProvider sp) => _sp = sp;

    public long Id { get; private init; }
    public long UsuarioId { get; private init; }
    public long CajaId { get; private init; }
    public long CategoriaIngresoId { get; private init; }
    public long CategoriaEgresoId { get; private init; }

    /// <summary>"Hoy" en la zona horaria de la empresa, el mismo que usan las reglas.</summary>
    public DateOnly Hoy { get; private init; }

    public AppDbContext Db => Servicio<AppDbContext>();

    public T Servicio<T>() where T : notnull => _sp.GetRequiredService<T>();

    public static async Task<EmpresaDePrueba> RegistrarAsync(BaseDePruebas baseDePruebas, IServiceProvider sp, string nombre)
    {
        var (id, usuarioId) = await baseDePruebas.RegistrarEmpresaAsync(sp, nombre);
        var movimientos = sp.GetRequiredService<MovimientosService>();
        var cuentas = await sp.GetRequiredService<CuentasService>().ListarAsync(id, incluirInactivas: false);

        return new EmpresaDePrueba(sp)
        {
            Id = id,
            UsuarioId = usuarioId,
            CajaId = cuentas.Single(c => c.NombreCuenta == DatosBaseEmpresa.NombreCuentaBase).Id,
            CategoriaIngresoId = (await movimientos.ListCategoriasAsync(id, TiposMovimiento.Ingreso))[0].Id,
            CategoriaEgresoId = (await movimientos.ListCategoriasAsync(id, TiposMovimiento.Egreso))[0].Id,
            Hoy = await sp.GetRequiredService<SaldosService>().HoyDeEmpresaAsync(id)
        };
    }

    /// <summary>La misma empresa vista desde otra "petición" (otro contenedor, otra conexión).</summary>
    public EmpresaDePrueba En(IServiceProvider otro) => new(otro)
    {
        Id = Id,
        UsuarioId = UsuarioId,
        CajaId = CajaId,
        CategoriaIngresoId = CategoriaIngresoId,
        CategoriaEgresoId = CategoriaEgresoId,
        Hoy = Hoy
    };

    public async Task<long> TerceroAsync(string tipo, string nombre)
        => (await Servicio<TercerosService>().CrearAsync(Id,
            new CreateTerceroRequest(tipo, nombre, null, null, null, null, null))).Id;

    public Task<DocumentoResponse> DocumentoAsync(
        string tipoCuenta,
        long idTercero,
        decimal monto,
        DateOnly vence,
        string? numero = null,
        DateOnly? emision = null,
        string tipoDocumento = "factura")
        => Servicio<DocumentosService>().CrearAsync(Id, UsuarioId, new CreateDocumentoRequest(
            tipoCuenta, tipoDocumento, idTercero, monto, emision ?? Hoy, vence, numero,
            IdCategoria: null, MontoNeto: null));

    /// <summary>Una nota como la manda el formulario: sin tercero ni vencimiento, los toma del padre.</summary>
    public Task<DocumentoResponse> NotaAsync(string tipoNota, DocumentoResponse padre, decimal monto)
        => Servicio<DocumentosService>().CrearAsync(Id, UsuarioId, new CreateDocumentoRequest(
            padre.TipoCuenta, tipoNota, IdTercero: null, monto, Hoy, FechaVencimiento: null, NumeroDocumento: null,
            IdCategoria: null, MontoNeto: null, IdDocumentoReferencia: padre.Id));

    public async Task<long> MovimientoAsync(string tipo, decimal monto, long? cuenta = null, DateOnly? fecha = null)
        => (await Servicio<MovimientosService>().CrearMovimientoAsync(Id, UsuarioId, new CreateMovimientoRequest(
            tipo, fecha ?? Hoy, monto, cuenta ?? CajaId,
            tipo == TiposMovimiento.Ingreso ? CategoriaIngresoId : CategoriaEgresoId,
            null, "transferencia", null))).Id;

    public Task<PagoResponse> PagarAsync(long idDocumento, long idMovimiento, decimal monto)
        => Servicio<PagosService>().CrearAsync(Id, UsuarioId, new CreatePagoRequest(idDocumento, idMovimiento, monto));

    public Task<DocumentoResponse> ActualizarAsync(DocumentoResponse doc, decimal? monto = null, DateOnly? vence = null)
        => Servicio<DocumentosService>().ActualizarAsync(Id, UsuarioId, doc.Id, new UpdateDocumentoRequest(
            doc.TipoDocumento, doc.IdTercero, monto ?? doc.MontoTotal, doc.FechaEmision, vence ?? doc.FechaVencimiento,
            doc.NumeroDocumento, doc.IdCategoria, doc.MontoNeto, doc.MontoIva, doc.Descripcion));

    /// <summary>El documento como lo ve la pantalla ahora, leído de nuevo de la base.</summary>
    public async Task<DocumentoResponse> DocumentoActualAsync(DocumentoResponse doc)
    {
        Db.ChangeTracker.Clear();
        var pagina = await Servicio<DocumentosService>().ListarAsync(Id, doc.TipoCuenta, null, null, 1, 100);
        return pagina.Items.Single(d => d.Id == doc.Id);
    }

    public async Task SaldoInicialAsync(long idCuenta, decimal monto)
    {
        var cuenta = (await Servicio<CuentasService>().ListarAsync(Id, incluirInactivas: true)).Single(c => c.Id == idCuenta);
        await Servicio<CuentasService>().ActualizarAsync(Id, UsuarioId, idCuenta,
            new UpdateCuentaRequest(cuenta.NombreCuenta, cuenta.TipoCuenta, cuenta.Banco, cuenta.NumeroCuenta, monto));
    }

    public Task<List<Backend.Models.Alerta>> AlertasAsync(long idDocumento, string tipo)
        => Db.Alertas.AsNoTracking()
            .Where(a => a.CompanyId == Id && a.DocumentId == idDocumento && a.Type == tipo)
            .OrderBy(a => a.Id)
            .ToListAsync();
}
