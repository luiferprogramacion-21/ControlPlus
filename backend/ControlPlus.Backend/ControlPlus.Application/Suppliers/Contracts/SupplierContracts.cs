using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Suppliers.Contracts;

public sealed record SupplierDto(Guid Id, string? DocumentType, string? DocumentNumber,
    string Name, string? Phone, string? Address, string? Email, string? Observations,
    bool IsActive, long Version);

public sealed record SupplierListQuery(bool? IsActive = null, string? Name = null,
    string? DocumentType = null, string? DocumentNumber = null, int Page = 1, int PageSize = 20);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSupplierRequest(string? DocumentType, string? DocumentNumber,
    string? Name, string? Phone, string? Address, string? Email, string? Observations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSupplierRequest(string? DocumentType, string? DocumentNumber,
    string? Name, string? Phone, string? Address, string? Email, string? Observations,
    [property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetSupplierStatusRequest([property: JsonRequired] bool IsActive,
    [property: JsonRequired] long ExpectedVersion);

public interface ISupplierService
{
    Task<Result<PagedResult<SupplierDto>>> ListAsync(ActorContext actor, SupplierListQuery query, CancellationToken ct = default);
    Task<Result<SupplierDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default);
    Task<Result<SupplierDto>> CreateAsync(ActorContext actor, CreateSupplierRequest request, CancellationToken ct = default);
    Task<Result<SupplierDto>> UpdateAsync(ActorContext actor, Guid id, UpdateSupplierRequest request, CancellationToken ct = default);
    Task<Result<SupplierDto>> SetStatusAsync(ActorContext actor, Guid id, SetSupplierStatusRequest request, CancellationToken ct = default);
}
