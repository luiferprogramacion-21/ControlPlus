using ControlPlus.Application.Printing;

namespace ControlPlus.Api.Printing;

/// <summary>Processes pending jobs; IMPRIMIENDO is deliberately never reclaimed on startup.</summary>
public sealed class PrintQueueWorker(IServiceScopeFactory scopes, ILogger<PrintQueueWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var printing = scope.ServiceProvider.GetRequiredService<IReceiptPrintingService>();
                if (await printing.ProcessOneAsync(stoppingToken)) continue;
                if (await printing.RecoverOneOriginalAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Technical details stay in the technical log, never in a print job or HTTP response.
                logger.LogError(exception, "Falló el procesador de trabajos de impresión.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
