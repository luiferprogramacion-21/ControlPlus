namespace ControlPlus.Application.Credits.Ports;

/// <summary>Saldo pendiente y vencido de los créditos abiertos de un cliente.</summary>
public sealed record CustomerCreditExposure(decimal OutstandingBalance, decimal OverdueBalance, int OverdueCredits);

/// <summary>
/// Lectura para avisar o bloquear créditos nuevos a morosos o por encima del cupo. «Vencido» usa la misma regla que
/// <c>ControlPlus.Domain.Credits.CreditStatusPolicy</c>; si esa regla cambia (por ejemplo, con cuotas), esta consulta
/// debe cambiar con ella.
/// </summary>
public interface ICustomerCreditExposureReader
{
    Task<CustomerCreditExposure> GetAsync(Guid customerId, DateOnly today, CancellationToken cancellationToken = default);
}
