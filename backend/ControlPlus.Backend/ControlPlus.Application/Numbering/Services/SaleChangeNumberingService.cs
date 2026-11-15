using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Numbering.Services;

public sealed partial class DocumentNumberingService
{
    public Task<Result<SalesDocumentNumberingDto>> GetSaleChangesAsync(
        ActorContext actor, CancellationToken cancellationToken = default) =>
        GetLayawayNumberingAsync(actor, "cambios de venta", DocumentNumberingDefaults.SaleChanges,
            repository.GetSaleChangeNumberingAsync, cancellationToken);

    public Task<Result<SalesDocumentNumberingDto>> CreateSaleChangesAsync(
        ActorContext actor, CreateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default) =>
        CreateLayawayNumberingAsync(actor,
            new CreateLayawayDocumentNumberingRequest(request.Prefix, request.FirstNumber),
            "CAMBIO_VENTA", "CAM", "cambios de venta",
            AuditAction.SaleChangeNumberingCreated, repository.GetSaleChangeNumberingAsync,
            cancellationToken);
}
