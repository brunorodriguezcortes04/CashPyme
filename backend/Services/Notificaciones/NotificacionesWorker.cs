using Microsoft.Extensions.Options;

namespace Backend.Services.Notificaciones;

/// <summary>
/// Tarea de fondo que cada IntervaloMinutos genera y envía las alertas, y revisa si a alguna
/// empresa le toca el resumen semanal. Corre dentro de la misma API: para un MVP evita
/// montar un servicio aparte (Hangfire, cron externo) y todo el estado de "ya enviado" vive
/// en la base, así que reiniciar la API no duplica correos.
/// </summary>
public class NotificacionesWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificacionesOptions> options,
    ILogger<NotificacionesWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.Habilitado)
        {
            logger.LogInformation("Notificaciones por correo desactivadas (Notificaciones:Habilitado = false).");
            return;
        }

        // Pequeña espera inicial para no competir con el arranque de la API.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, o.IntervaloMinutos)));
        do
        {
            await EjecutarCicloAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EjecutarCicloAsync(CancellationToken ct)
    {
        // DbContext es scoped: cada ciclo crea el suyo, como si fuera un request.
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NotificacionesService>();

        try
        {
            var alertas = await service.ProcesarAlertasAsync(ct);
            var reportes = await service.ProcesarReportesSemanalesAsync(ct: ct);
            logger.LogInformation("Notificaciones: {Alertas} alerta(s) y {Reportes} reporte(s) enviados.", alertas, reportes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // La API se está apagando.
        }
        catch (Exception ex)
        {
            // Un ciclo fallido (base caída, etc.) no debe matar el worker: se reintenta en el próximo.
            logger.LogError(ex, "Falló el ciclo de notificaciones.");
        }
    }
}
