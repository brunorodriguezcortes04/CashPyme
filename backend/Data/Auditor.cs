using System.Text.Json;
using Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Backend.Data;

/// <summary>
/// Arma las filas de auditoria a partir de lo que EF va a guardar. Reemplaza al trigger
/// fn_auditar con el mismo formato de detalle, para que el historial anterior y el nuevo se lean
/// igual: la fila completa con los nombres de columna de la base, en "despues" al crear, en
/// "antes" y "despues" al modificar y en "antes" al borrar.
///
/// Se hace en dos tiempos porque un INSERT recién tiene id (y fecha_creacion, que pone la base)
/// después de guardar, pero lo que había antes de un UPDATE o DELETE solo se sabe antes.
/// </summary>
internal static class Auditor
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    internal sealed class Pendiente
    {
        public required EntityEntry Entrada { get; init; }
        public required string Accion { get; init; }
        public Dictionary<string, object?>? Antes { get; init; }
    }

    /// <summary>Antes de guardar: qué se va a tocar y cómo estaba.</summary>
    public static List<Pendiente> Capturar(AppDbContext db)
    {
        return db.ChangeTracker.Entries()
            .Where(e => AppDbContext.TiposAuditados.Contains(e.Entity.GetType())
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => new Pendiente
            {
                Entrada = e,
                Accion = e.State switch
                {
                    EntityState.Added => "INSERT",
                    EntityState.Modified => "UPDATE",
                    _ => "DELETE"
                },
                Antes = e.State == EntityState.Added ? null : Fila(e, e.OriginalValues)
            })
            .ToList();
    }

    /// <summary>Después de guardar: ya están los id y los valores que puso la base.</summary>
    public static IEnumerable<Auditoria> Construir(IEnumerable<Pendiente> pendientes, long? userId)
    {
        foreach (var p in pendientes)
        {
            var despues = p.Accion == "DELETE" ? null : Fila(p.Entrada, p.Entrada.CurrentValues);
            var referencia = despues ?? p.Antes!;

            var detalle = p.Accion switch
            {
                "INSERT" => new Dictionary<string, object?> { ["despues"] = despues },
                "UPDATE" => new Dictionary<string, object?> { ["antes"] = p.Antes, ["despues"] = despues },
                _ => new Dictionary<string, object?> { ["antes"] = p.Antes }
            };

            yield return new Auditoria
            {
                CompanyId = referencia.GetValueOrDefault("id_empresa") as long?,
                UserId = userId,
                Table = p.Entrada.Metadata.GetTableName()!,
                RecordId = ClavePrimaria(p.Entrada, referencia),
                Action = p.Accion,
                Detail = JsonSerializer.Serialize(detalle, Json)
            };
        }
    }

    /// <summary>La fila con nombres de columna (id_documento, monto_total…), no de propiedad C#.</summary>
    private static Dictionary<string, object?> Fila(EntityEntry entrada, PropertyValues valores)
    {
        var fila = new Dictionary<string, object?>();
        foreach (var propiedad in entrada.Metadata.GetProperties())
        {
            var columna = propiedad.GetColumnName();
            if (columna is not null)
            {
                fila[columna] = valores[propiedad];
            }
        }
        return fila;
    }

    private static long? ClavePrimaria(EntityEntry entrada, IReadOnlyDictionary<string, object?> fila)
    {
        var columna = entrada.Metadata.FindPrimaryKey()?.Properties.Single().GetColumnName();
        return columna is not null ? fila.GetValueOrDefault(columna) as long? : null;
    }
}
