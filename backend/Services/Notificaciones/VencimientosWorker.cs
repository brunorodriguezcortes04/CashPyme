namespace Backend.Services.Notificaciones;

/// <summary>
/// Job diario de vencimientos (VencimientosService). Corre al arrancar y después cada hora, no
/// una vez al día: cada empresa tiene su propia zona horaria, así que "pasó la medianoche" ocurre
/// a distintas horas, y como el trabajo es idempotente pasar seguido no duplica nada. No depende
/// de Notificaciones:Habilitado: el estado tiene que estar al día aunque no se envíen correos.
/// </summary>
public class VencimientosWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<VencimientosWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pequeña espera inicial para no competir con el arranque de la API.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

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
        var service = scope.ServiceProvider.GetRequiredService<VencimientosService>();

        try
        {
            var marcados = await service.MarcarVencidosAsync(ct);
            if (marcados > 0)
            {
                logger.LogInformation("Vencimientos: {Marcados} documento(s) pasaron a 'vencido'.", marcados);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // La API se está apagando.
        }
        catch (Exception ex)
        {
            // Una corrida fallida no debe matar el worker: se reintenta en la próxima.
            logger.LogError(ex, "Falló la marca de documentos vencidos.");
        }
    }
}