using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Customers.Ports;

/// <summary>
/// Clientes con deuda por etapa, para mostrarlos primero o filtrarlos (pruebas del usuario del 26/11/2026).
/// <c>Filter</c>: DEBEN, MOROSOS, POR_VENCER, AL_DIA, SIN_DEUDA o null para todos.
/// </summary>
public sealed record CustomerCreditOrder(Guid[] Overdue, Guid[] DueSoon, Guid[] Current, string? Filter);

public interface ICustomerRepository
{
    Task<PagedResult<Cliente>> ListAsync(CustomerListQuery query, string status, CustomerCreditOrder? credit,
        CancellationToken cancellationToken = default);
    Task<Cliente?> GetAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<bool> DocumentExistsAsync(string documentType, string documentNumber, Guid? excludedId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Cliente customer, CancellationToken cancellationToken = default);
}
