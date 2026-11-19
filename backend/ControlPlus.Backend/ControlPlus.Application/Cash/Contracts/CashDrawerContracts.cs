using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Cash.Contracts;

public sealed record CashDrawerStatusDto(bool Enabled, string? ProfileName, bool CanOpen);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpenCashDrawerRequest(string Reason);

/// <summary>ENVIADO means the Windows queue accepted the pulse, not that the drawer opened.</summary>
public sealed record CashDrawerOpenResultDto(Guid AttemptId, string Status, string Message);

public enum CashDrawerOperationKind
{
    Sale,
    CreditPayment,
    LayawayInitial,
    LayawayPayment,
    LayawayRefund,
    CashMovement,
    SaleChange
}

public interface ICashDrawerService
{
    Task<Result<CashDrawerStatusDto>> GetStatusAsync(ActorContext actor,
        CancellationToken ct = default);
    Task<Result<CashDrawerOpenResultDto>> OpenManualAsync(ActorContext actor,
        Guid idempotencyId, OpenCashDrawerRequest request, CancellationToken ct = default);
    Task<CashDrawerOpenResultDto?> TryOpenAutomaticAsync(ActorContext actor,
        CashDrawerOperationKind kind, Guid operationId, CancellationToken ct = default);
}
