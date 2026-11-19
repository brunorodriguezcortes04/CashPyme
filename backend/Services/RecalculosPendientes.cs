using System.Collections.Concurrent;
using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Todo lo que decide el resultado de las alertas y de la proyección de una empresa, resumido en
/// pocos valores baratos de leer:
///   * UltimoCambio: el último registro de auditoría de la empresa. Cuentas, documentos,
///     movimientos, pagos y transferencias no se pueden guardar sin auditoría (AppDbContext lo
///     exige), así que cualquier cambio en lo que usan alertas y proyección lo mueve.
///   * Hoy, en la zona de la empresa: los vencimientos y la proyección cambian con el día.
///   * Días de aviso y umbral de saldo bajo: son de la tabla empresa, que no se audita.
/// Si la huella no cambió, recalcular daría exactamente lo mismo.
/// </summary>
public readonly record struct HuellaEmpresa(long UltimoCambio, DateOnly Hoy, int DiasAviso, decimal UmbralSaldoBajo);

public class HuellasEmpresaService(AppDbContext db, SaldosService saldos)
{
    /// <summary>La huella de las empresas activas (o solo de una), en una sola consulta.</summary>
    public async Task<Dictionary<long, HuellaEmpresa>> DeEmpresasActivasAsync(long? companyId = null, CancellationToken ct = default)
    {
        var empresas = await db.Companies
            .Where(c => c.IsActive && (companyId == null || c.Id == companyId))
            .Select(c => new
            {
                c.Id,
                c.TimeZone,
                c.DueDateWarningDays,
                c.LowBalanceThreshold,
                UltimoCambio = db.Auditorias.Where(a => a.CompanyId == c.Id).Max(a => (long?)a.Id) ?? 0
            })
            .ToListAsync(ct);

        return empresas.ToDictionary(
            e => e.Id,
            e => new HuellaEmpresa(e.UltimoCambio, saldos.HoyDe(e.TimeZone), e.DueDateWarningDays, e.LowBalanceThreshold));
    }
}

/// <summary>
/// Recuerda con qué huella se generaron por última vez las alertas o la proyección de cada
/// empresa, para no recalcular lo que no cambió. Antes las alertas de todas las empresas se
/// recalculaban enteras cada minuto (el worker) y en cada GET /api/alertas.
///
/// Vive en memoria: tras un reinicio cada empresa se recalcula una vez y listo. Aun con la huella
/// igual se recalcula pasada VigenciaMaxima: dos transacciones que terminan en distinto orden del
/// que empezaron pueden dejar un cambio con un id de auditoría menor al ya visto, y así ese caso
/// raro se corrige solo en minutos.
///
/// Singleton por contenedor y no estático: las pruebas de integración usan un esquema por
/// contenedor, con ids de empresa que se repiten entre esquemas.
/// </summary>
public class RecalculosPendientes(TimeProvider reloj)
{
    public const string Alertas = "alertas";
    public const string Proyeccion = "proyeccion";

    public static readonly TimeSpan VigenciaMaxima = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(string Tarea, long Empresa), (HuellaEmpresa Huella, DateTimeOffset En)> _ultimos = new();

    public bool HaceFalta(string tarea, long idEmpresa, HuellaEmpresa huella)
        => !_ultimos.TryGetValue((tarea, idEmpresa), out var ultimo)
           || ultimo.Huella != huella
           || reloj.GetUtcNow() - ultimo.En >= VigenciaMaxima;

    /// <summary>
    /// Se llama después de recalcular, con la huella leída ANTES de empezar: si algo cambió
    /// mientras se recalculaba, la próxima vez la huella ya no coincide y se vuelve a hacer.
    /// </summary>
    public void Registrar(string tarea, long idEmpresa, HuellaEmpresa huella)
        => _ultimos[(tarea, idEmpresa)] = (huella, reloj.GetUtcNow());
}
