using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Customers.Contracts;

public sealed record CustomerDto(Guid Id, string? DocumentType, string? DocumentNumber,
    string FullName, string? Phone, string Address, string? Email, string? Observations,
    bool IsActive, long Version);

public sealed record CustomerListQuery(string? Search = null, string? Status = null, int Page = 1, int PageSize = 20);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCustomerRequest(string? DocumentType, string? DocumentNumber,
    string? FullName, string? Phone, string? Address, string? Email, string? Observations);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCustomerRequest(string? DocumentType, string? DocumentNumber,
    string? FullName, string? Phone, string? Address, string? Email, string? Observations,
    [property: JsonRequired] long ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetCustomerStatusRequest(
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] long ExpectedVersion);

public interface ICustomerService
{
    Task<Result<PagedResult<CustomerDto>>> ListAsync(ActorContext actor, CustomerListQuery query, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> GetAsync(ActorContext actor, Guid customerId, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> CreateAsync(ActorContext actor, CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> UpdateAsync(ActorContext actor, Guid customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<Result<CustomerDto>> SetStatusAsync(ActorContext actor, Guid customerId, SetCustomerStatusRequest request, CancellationToken cancellationToken = default);
}
