using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Customers.Ports;

public interface ICustomerRepository
{
    Task<PagedResult<Cliente>> ListAsync(CustomerListQuery query, string status, CancellationToken cancellationToken = default);
    Task<Cliente?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<bool> DocumentExistsAsync(string documentType, string documentNumber, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Cliente customer, CancellationToken cancellationToken = default);
}
