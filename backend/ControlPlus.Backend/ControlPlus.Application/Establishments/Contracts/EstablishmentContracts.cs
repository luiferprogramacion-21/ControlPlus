using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Establishments.Contracts;

/// <summary>Datos visibles del establecimiento activo usados por la interfaz y los documentos.</summary>
public sealed record EstablishmentDto(
    string CommercialName,
    string? Identification,
    string? Phone,
    string? Address,
    string? ReceiptHeader,
    string? ReceiptFooter,
    long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateEstablishmentRequest(
    [property: JsonRequired] string CommercialName,
    string? Identification,
    string? Phone,
    string? Address,
    string? ReceiptHeader,
    string? ReceiptFooter,
    [property: JsonRequired] long ExpectedVersion);

public interface IEstablishmentService
{
    Task<Result<EstablishmentDto>> GetAsync(
        ActorContext actor, CancellationToken cancellationToken = default);

    Task<Result<EstablishmentDto>> UpdateAsync(
        ActorContext actor, UpdateEstablishmentRequest request,
        CancellationToken cancellationToken = default);
}
