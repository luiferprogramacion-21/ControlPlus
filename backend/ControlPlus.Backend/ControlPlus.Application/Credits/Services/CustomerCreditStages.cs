using ControlPlus.Application.Credits.Ports;
using ControlPlus.Application.Sales.Ports;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Domain.Credits;

namespace ControlPlus.Application.Credits.Services;

/// <summary>Etapa del crédito de un cliente con deuda y sus saldos.</summary>
public sealed record CustomerCreditStageEntry(string Stage, CustomerCreditExposure Exposure);

/// <summary>
/// Todos los clientes con créditos abiertos y su etapa (pruebas del usuario del 26 de noviembre de 2026): sirve para el
/// resumen de Clientes y para mostrar primero, o filtrar, a los que deben. Quien no aparece no debe nada.
/// </summary>
public interface ICustomerCreditStages
{
    Task<IReadOnlyDictionary<Guid, CustomerCreditStageEntry>?> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class CustomerCreditStages(ISalesRepository sales, IClock clock, ICustomerCreditExposureReader exposureReader)
    : ICustomerCreditStages
{
    public async Task<IReadOnlyDictionary<Guid, CustomerCreditStageEntry>?> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var establishment = await sales.GetActiveEstablishmentAsync(cancellationToken);
        if (establishment is null) return null;
        var today = CreditSettingsService.LocalToday(establishment, clock);
        var exposures = await exposureReader.GetAllAsync(today, cancellationToken);
        return exposures
            .Where(pair => pair.Value.OutstandingBalance > 0)
            .ToDictionary(pair => pair.Key, pair => new CustomerCreditStageEntry(
                CustomerCreditStagePolicy.Resolve(pair.Value.OutstandingBalance, pair.Value.OverdueBalance, pair.Value.NextDueOn, today),
                pair.Value));
    }
}
