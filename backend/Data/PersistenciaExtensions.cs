using Backend.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Backend.Data;

/// <summary>
/// Las formas de guardar que usan los servicios: con auditoría, con traducción de los choques
/// contra índices únicos, y con filas bloqueadas para las reglas que no aguantan dos peticiones
/// simultáneas. Viven una sola vez acá, con el motivo de cada una escrito al lado.
/// </summary>
public static class PersistenciaExtensions
{
    /// <summary>
    /// Guarda y deja registrado quién hizo cada cambio en las tablas auditadas
    /// (AppDbContext.TiposAuditados). El cambio y su registro van en la misma transacción: o
    /// quedan los dos o ninguno. Si ya hay una transacción abierta (por ejemplo, la de un pago con
    /// filas bloqueadas) se usa esa y el commit lo hace quien la abrió.
    /// </summary>
    public static async Task SaveChangesAuditadoAsync(this AppDbContext db, long? userId)
    {
        await EnTransaccionAsync(db, async () =>
        {
            var pendientes = Auditor.Capturar(db);

            db.GuardandoConAuditoria = true;
            try
            {
                await db.SaveChangesAsync();
            }
            finally
            {
                db.GuardandoConAuditoria = false;
            }

            if (pendientes.Count > 0)
            {
                db.Auditorias.AddRange(Auditor.Construir(pendientes, userId));
                await db.SaveChangesAsync();
            }
        });
    }

    /// <summary>
    /// Igual que la anterior, pero además convierte el choque contra un índice único en el
    /// error de negocio que corresponda.
    /// </summary>
    public static Task SaveChangesAuditadoAsync(
        this AppDbContext db,
        long? userId,
        Func<ApiException> siDuplicado)
        => TraducirUnicidadAsync(() => db.SaveChangesAuditadoAsync(userId), siDuplicado);

    /// <summary>
    /// Guarda traduciendo el choque contra un índice único, sin auditoría: para las tablas que
    /// no se auditan (usuario, empresa, usuario_empresa, tercero, alerta). Con restriccion, solo
    /// traduce el choque contra ese índice: cualquier otro sigue siendo un error inesperado.
    /// </summary>
    public static Task SaveChangesTraducidoAsync(
        this AppDbContext db, Func<ApiException> siDuplicado, string? restriccion = null)
        => TraducirUnicidadAsync(async () => await db.SaveChangesAsync(), siDuplicado, restriccion);

    /// <summary>
    /// Corre el trabajo dentro de una transacción: la que ya esté abierta o una nueva que se
    /// confirma al final. Es lo que permite componer pasos (guardar, recalcular, volver a
    /// guardar) sin que una falla a la mitad deje la mitad escrita.
    /// </summary>
    public static async Task EnTransaccionAsync(this AppDbContext db, Func<Task> trabajo)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await trabajo();
            return;
        }

        await using IDbContextTransaction transaccion = await db.Database.BeginTransactionAsync();
        await trabajo();
        await transaccion.CommitAsync();
    }

    public static async Task<T> EnTransaccionAsync<T>(this AppDbContext db, Func<Task<T>> trabajo)
    {
        var resultado = default(T)!;
        await db.EnTransaccionAsync(async () => { resultado = await trabajo(); });
        return resultado;
    }

    /// <summary>
    /// Bloquea filas hasta el fin de la transacción (SELECT … FOR UPDATE). Es lo que hacían los
    /// triggers para que dos pagos simultáneos no pasen ambos el control de saldo: el segundo
    /// espera a que el primero termine y recién entonces ve lo que ya se pagó. Tiene que
    /// llamarse dentro de EnTransaccionAsync; fuera de una transacción el bloqueo duraría lo que
    /// dura la consulta y no protegería nada.
    ///
    /// Bloquea filas de cualquier empresa: es para los jobs que recorren todas. Lo que llega
    /// desde un request usa BloquearDeEmpresaAsync.
    /// </summary>
    public static Task BloquearAsync(this AppDbContext db, string tabla, string columnaId, IEnumerable<long> ids)
        => BloquearAsync(db, tabla, columnaId, ids, idEmpresa: null);

    /// <summary>
    /// Igual que BloquearAsync, pero solo bloquea las filas de esa empresa. Los id vienen del
    /// cliente: sin el filtro, alguien de otra empresa podía mandar ids ajenos y dejar
    /// esperando los pagos de esa empresa hasta que su propio 404 deshiciera la transacción.
    /// Una fila de otra empresa simplemente no se bloquea, y la búsqueda que viene después (que
    /// también filtra por empresa) responde que no existe.
    /// </summary>
    public static Task BloquearDeEmpresaAsync(
        this AppDbContext db, long idEmpresa, string tabla, string columnaId, IEnumerable<long> ids)
        => BloquearAsync(db, tabla, columnaId, ids, idEmpresa);

    private static async Task BloquearAsync(
        AppDbContext db, string tabla, string columnaId, IEnumerable<long> ids, long? idEmpresa)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("BloquearAsync tiene que correr dentro de una transacción.");
        }

        var lista = ids.Distinct().OrderBy(id => id).ToArray();
        if (lista.Length == 0)
        {
            return;
        }

        // Tabla y columna son constantes del código, nunca entrada del usuario; los id van como
        // parámetro. Se bloquean siempre en el mismo orden (por id) para no provocar deadlocks
        // entre dos operaciones que toquen las mismas filas.
#pragma warning disable EF1002
        if (idEmpresa is null)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT 1 FROM {tabla} WHERE {columnaId} = ANY(@ids) ORDER BY {columnaId} FOR UPDATE",
                new NpgsqlParameter("ids", lista));
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT 1 FROM {tabla} WHERE {columnaId} = ANY(@ids) AND id_empresa = @empresa ORDER BY {columnaId} FOR UPDATE",
                new NpgsqlParameter("ids", lista),
                new NpgsqlParameter("empresa", idEmpresa.Value));
        }
#pragma warning restore EF1002
    }

    /// <summary>
    /// Comprobar antes de guardar no alcanza: dos peticiones simultáneas pueden pasar ambas
    /// la comprobación y solo una sobrevivir al UNIQUE. Esta es la red que atrapa esa carrera
    /// y la devuelve como un mensaje entendible en vez de un 500.
    /// </summary>
    private static async Task TraducirUnicidadAsync(
        Func<Task> guardar, Func<ApiException> siDuplicado, string? restriccion = null)
    {
        try
        {
            await guardar();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                  && (restriccion is null || pg.ConstraintName == restriccion))
        {
            throw siDuplicado();
        }
    }
}
