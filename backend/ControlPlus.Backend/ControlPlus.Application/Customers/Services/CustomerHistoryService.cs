using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Customers.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
namespace ControlPlus.Application.Customers.Services;
public sealed class CustomerHistoryService(ICustomerHistoryRepository repository, IUserRepository users) : ICustomerHistoryService
{
    public async Task<Result<PagedResult<CustomerHistoryItem>>> ListAsync(ActorContext actor, Guid customerId, CustomerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<PagedResult<CustomerHistoryItem>>(ApplicationError.Unauthorized());
        var codes = snapshot.PermissionCodes;
        bool Has(string code) => codes.Contains(code, StringComparer.OrdinalIgnoreCase);
        if (!Has(PermissionCodes.CustomersHistoryRead) || !(Has(PermissionCodes.CustomersBasicManage) || Has(PermissionCodes.CustomersManage)))
            return Result.Failure<PagedResult<CustomerHistoryItem>>(ApplicationError.Forbidden());
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || ((long)query.Page - 1) * query.PageSize > int.MaxValue)
            return Result.Failure<PagedResult<CustomerHistoryItem>>(ApplicationError.Validation("Paginación inválida."));
        var active = await repository.GetCustomerStatusAsync(customerId, cancellationToken);
        if (active is null || (!active.Value && !Has(PermissionCodes.CustomersManage)))
            return Result.Failure<PagedResult<CustomerHistoryItem>>(ApplicationError.NotFound("el cliente"));
        var result = await repository.ListAsync(customerId, actor.UserId, Has(PermissionCodes.SalesAllRead), Has(PermissionCodes.SalesOwnRead), Has(PermissionCodes.CreditManage), Has(PermissionCodes.LayawayManage), query.Page, query.PageSize, (query.Page - 1) * query.PageSize, cancellationToken);
        return Result.Success(result);
    }
}
