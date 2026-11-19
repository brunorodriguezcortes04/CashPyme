namespace Backend.Services.Notificaciones;

/// <summary>
/// Job diario de proyecciones (ProyeccionFlujoCajaService.GenerarDiariasAsync): deja a cada
/// empresa activa con la proyección del día y, con ella, la alerta de caja negativa al día. Corre
/// al arrancar y después cada hora por lo mismo que VencimientosWorker: cada empresa cambia de
/// día a su hora, y como solo genera la que falta, pasar seguido no duplica nada. No depende de
/// Notificaciones:Habilitado.
/// </summary>
public class ProyeccionesWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ProyeccionesWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Después de VencimientosWorker (20 s), para no competir con el arranque ni con él.
        await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            await EjecutarAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EjecutarAsync(CancellationToken ct)
    {
        // DbContext es scoped: cada corrida crea el suyo, como si fuera un request.
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ProyeccionFlujoCajaService>();

        try
        {
            var generadas = await service.GenerarDiariasAsync(ct);
            if (generadas > 0)
            {
                logger.LogInformation("Proyecciones: {Generadas} empresa(s) con proyección nueva.", generadas);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // La API se está apagando.
        }
        catch (Exception ex)
        {
            // Una corrida fallida no debe matar el worker: se reintenta en la próxima.
            logger.LogError(ex, "Falló la generación diaria de proyecciones.");
        }
    }
}
