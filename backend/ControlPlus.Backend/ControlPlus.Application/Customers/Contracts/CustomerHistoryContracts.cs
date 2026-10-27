using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
namespace ControlPlus.Application.Customers.Contracts;
public sealed record CustomerHistoryItem(Guid Id, string Type, Guid? ParentId, string Number, DateTimeOffset OccurredAt, string Status, decimal Amount, decimal? OutstandingAmount);
public sealed record CustomerHistoryQuery(int Page = 1, int PageSize = 20);
public interface ICustomerHistoryService
{
    Task<Result<PagedResult<CustomerHistoryItem>>> ListAsync(ActorContext actor, Guid customerId, CustomerHistoryQuery query, CancellationToken cancellationToken = default);
}
