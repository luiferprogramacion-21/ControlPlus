using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
namespace ControlPlus.Application.Customers.Ports;
public interface ICustomerHistoryRepository
{
    Task<bool?> GetCustomerStatusAsync(Guid customerId, CancellationToken cancellationToken);
    Task<PagedResult<CustomerHistoryItem>> ListAsync(Guid customerId, Guid actorId, bool allSales, bool ownSales, bool credits, bool layaways, int page, int pageSize, int offset, CancellationToken cancellationToken);
}
