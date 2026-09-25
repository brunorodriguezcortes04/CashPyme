using Backend.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.Data;

/// <summary>
/// Las dos formas de guardar que usan los servicios: con registro de auditoría y con
/// traducción de los choques contra índices únicos. Estaban copiadas en cada servicio; acá
/// viven una sola vez, con el motivo de cada una escrito al lado.
/// </summary>
public static class PersistenciaExtensions
{
    /// <summary>
    /// Guarda dejando registrado quién hizo el cambio. La transacción no es decorativa:
    /// SET LOCAL muere en el commit, así que un SaveChanges fuera de ella haría que el
    /// trigger de auditoría guardara la fila sin autor.
    /// </summary>
    public static async Task SaveChangesAuditadoAsync(this AppDbContext db, long userId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.SetUsuarioAuditoriaAsync(userId);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Igual que la anterior, pero además convierte el choque contra un índice único en el
    /// error de negocio que corresponda.
    /// </summary>
    public static Task SaveChangesAuditadoAsync(
        this AppDbContext db,
        long userId,
        Func<ApiException> siDuplicado)
        => TraducirUnicidadAsync(() => db.SaveChangesAuditadoAsync(userId), siDuplicado);

    /// <summary>
    /// Guarda traduciendo el choque contra un índice único, sin registro de auditoría: para
    /// las tablas que no tienen trigger de auditoría (usuario, empresa, usuario_empresa).
    /// </summary>
    public static Task SaveChangesTraducidoAsync(this AppDbContext db, Func<ApiException> siDuplicado)
        => TraducirUnicidadAsync(async () => await db.SaveChangesAsync(), siDuplicado);

    /// <summary>
    /// Comprobar antes de guardar no alcanza: dos peticiones simultáneas pueden pasar ambas
    /// la comprobación y solo una sobrevivir al UNIQUE. Esta es la red que atrapa esa carrera
    /// y la devuelve como un mensaje entendible en vez de un 500.
    /// </summary>
    private static async Task TraducirUnicidadAsync(Func<Task> guardar, Func<ApiException> siDuplicado)
    {
        try
        {
            await guardar();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw siDuplicado();
        }
    }
}
