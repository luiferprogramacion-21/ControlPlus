namespace ControlPlus.Application.Credits.Ports;

/// <summary>
/// Saldo pendiente y vencido de los créditos abiertos de un cliente. <paramref name="NextDueOn"/> es la fecha más próxima de
/// una cuota con saldo que aún no vence y <paramref name="OldestOverdueOn"/> la de la cuota vencida más antigua (C.14).
/// </summary>
public sealed record CustomerCreditExposure(decimal OutstandingBalance, decimal OverdueBalance, int OverdueCredits,
    DateOnly? NextDueOn = null, DateOnly? OldestOverdueOn = null);

/// <summary>
/// Lectura para avisar o bloquear créditos nuevos a morosos o por encima del cupo, y para mostrar la etapa del crédito de
/// cada cliente. «Vencido» usa la misma regla que <c>ControlPlus.Domain.Credits.CreditStatusPolicy</c>; si esa regla
/// cambia, esta consulta debe cambiar con ella.
/// </summary>
public interface ICustomerCreditExposureReader
{
    Task<CustomerCreditExposure> GetAsync(Guid customerId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>La misma lectura para varios clientes a la vez (una página de Clientes); omite a quien no tiene créditos abiertos.</summary>
    Task<IReadOnlyDictionary<Guid, CustomerCreditExposure>> GetManyAsync(
        IReadOnlyCollection<Guid> customerIds, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>Todos los clientes con créditos abiertos (resumen y orden de Clientes, 26 de noviembre de 2026).</summary>
    Task<IReadOnlyDictionary<Guid, CustomerCreditExposure>> GetAllAsync(DateOnly today, CancellationToken cancellationToken = default);
}
