using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using System.Text.Json.Serialization;

namespace ControlPlus.Application.Numbering.Contracts;

public sealed record SalesDocumentNumberingDto(
    string DocumentType,
    string Series,
    string Prefix,
    long LastNumber,
    long NextNumber,
    long Version);

public sealed record PurchaseDocumentNumberingDto(
    string DocumentType,
    string Series,
    string Prefix,
    long LastNumber,
    long NextNumber,
    long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSalesDocumentNumberingRequest(string Prefix, long FirstNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSalesDocumentNumberingRequest(string Prefix, long FirstNumber, long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePurchaseDocumentNumberingRequest(string Prefix, long FirstNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateLayawayDocumentNumberingRequest(string Prefix, long FirstNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePurchaseDocumentNumberingRequest(string Prefix, long FirstNumber, long Version);

public interface IDocumentNumberingService
{
    Task<Result<SalesDocumentNumberingDto>> GetSaleChangesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<SalesDocumentNumberingDto>> CreateSaleChangesAsync(
        ActorContext actor, CreateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<SalesDocumentNumberingDto>> GetLayawaysAsync(
        ActorContext actor, CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> CreateLayawaysAsync(
        ActorContext actor, CreateLayawayDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> GetLayawayPaymentsAsync(
        ActorContext actor, CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> CreateLayawayPaymentsAsync(
        ActorContext actor, CreateLayawayDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> GetSalesAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> CreateSalesAsync(
        ActorContext actor,
        CreateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> UpdateSalesAsync(
        ActorContext actor,
        UpdateSalesDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SalesDocumentNumberingDto>> GetCreditPaymentsAsync(ActorContext actor,
        CancellationToken cancellationToken = default);
    Task<Result<SalesDocumentNumberingDto>> CreateCreditPaymentsAsync(ActorContext actor,
        CreateSalesDocumentNumberingRequest request, CancellationToken cancellationToken = default);
    Task<Result<SalesDocumentNumberingDto>> UpdateCreditPaymentsAsync(ActorContext actor,
        UpdateSalesDocumentNumberingRequest request, CancellationToken cancellationToken = default);

    Task<Result<PurchaseDocumentNumberingDto>> GetPurchasesAsync(
        ActorContext actor, CancellationToken cancellationToken = default);

    Task<Result<PurchaseDocumentNumberingDto>> CreatePurchasesAsync(
        ActorContext actor, CreatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseDocumentNumberingDto>> UpdatePurchasesAsync(
        ActorContext actor, UpdatePurchaseDocumentNumberingRequest request,
        CancellationToken cancellationToken = default);
}
