using Backend.Data;
using Backend.Dominio;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Marca como 'vencido' los documentos sin pagos cuya fecha ya pasó. Un pago, una nota o una
/// edición ponen el estado al día solos (SaldosService.RecalcularEstadosAsync), pero un documento
/// que simplemente vence sin que nadie lo toque no tiene quién lo actualice: para eso está esto,
/// que corre VencimientosWorker.
///
/// Es idempotente: correrlo dos veces el mismo día no cambia nada la segunda vez.
/// </summary>
public class VencimientosService(AppDbContext db, SaldosService saldos, TimeProvider reloj)
{
    /// <summary>Devuelve cuántos documentos pasaron a 'vencido'.</summary>
    public async Task<int> MarcarVencidosAsync(CancellationToken ct = default)
    {
        // Cota amplia: en ninguna zona horaria "hoy" pasa de mañana UTC. La fecha exacta de cada
        // empresa la decide RecalcularEstadosAsync, que deja 'pendiente' lo que en su zona
        // todavía no vence.
        var limite = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime).AddDays(1);

        var candidatos = await db.DocumentosFinancieros
            .Where(d => d.Status == DocumentoFinanciero.Pendiente
                        && d.DueDate < limite
                        && d.DocumentType != TiposDocumento.NotaCredito
                        && d.DocumentType != TiposDocumento.NotaDebito)
            .Select(d => d.Id)
            .ToListAsync(ct);

        if (candidatos.Count == 0)
        {
            return 0;
        }

        return await db.EnTransaccionAsync(async () =>
        {
            // Bloqueados igual que al aplicar un pago: si entra uno mientras corre esto, uno de
            // los dos espera y el estado final sale de lo que de verdad quedó guardado.
            await db.BloquearAsync("documento_financiero", "id_documento", candidatos);
            await saldos.RecalcularEstadosAsync(candidatos);

            var marcados = db.ChangeTracker.Entries<DocumentoFinanciero>()
                .Count(e => e.State == EntityState.Modified && e.Entity.Status == DocumentoFinanciero.Vencido);

            // Sin usuario: en auditoría queda como un cambio del sistema.
            await db.SaveChangesAuditadoAsync(userId: null);
            return marcados;
        });
    }
}
