using ControlPlus.Application.Backups;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Backups;

namespace ControlPlus.Api.Backups;

/// <summary>
/// Respalda cada IntervalMinutes (30 por Fase 4) y cuando llega un pedido (cierre de caja o «Crear respaldo»). Al
/// encender, si el último respaldo exitoso es más viejo que el intervalo, respalda de inmediato. Con el servicio
/// apagado (configuración por defecto) no hace nada.
/// </summary>
public sealed class BackupWorker(IServiceScopeFactory scopes, BackupRequestQueue queue, IBackupRuntime runtime,
    ILogger<BackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            string? trigger;
            try { trigger = await queue.WaitAsync(CheckEvery, stoppingToken); }
            catch (OperationCanceledException) { break; }
            if (!runtime.Settings.Enabled) continue;

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
                // IClock es scoped: se pide dentro del ciclo, nunca en el constructor de este servicio singleton.
                var clock = scope.ServiceProvider.GetRequiredService<IClock>();
                if (trigger is null && await backups.IsDueAsync(clock.UtcNow, stoppingToken))
                    trigger = BackupCodes.Scheduled;
                if (trigger is null) continue;
                var result = await backups.RunAsync(trigger, stoppingToken);
                if (result?.Result == BackupCodes.Failed)
                    logger.LogWarning("El respaldo {Trigger} falló: {Error}", trigger, result.Error);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // El detalle técnico queda en el registro técnico; la base guarda solo mensajes saneados.
                logger.LogError(exception, "Falló el servicio de respaldos.");
            }
        }
    }
}
