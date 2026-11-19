using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Api.Printing;

/// <summary>Cash drawer outcomes never undo a committed business operation.</summary>
internal static class CashDrawerControllerDispatch
{
    public static async Task TryOpenAsync(ICashDrawerService drawer, ActorContext actor,
        CashDrawerOperationKind kind, Guid operationId, HttpResponse response,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            var result = await drawer.TryOpenAutomaticAsync(actor, kind, operationId, ct);
            if (result is not null && result.Status != "OMITIDO")
                response.Headers["X-Cash-Drawer-Status"] = result.Status;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "No se pudo registrar la orden de cajón para {OperationKind} {OperationId}.",
                kind, operationId);
            response.Headers["X-Cash-Drawer-Status"] = "INCIERTO";
        }
    }
}
