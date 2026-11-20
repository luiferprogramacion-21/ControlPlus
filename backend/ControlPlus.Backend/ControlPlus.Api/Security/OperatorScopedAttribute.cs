namespace ControlPlus.Api.Security;

/// <summary>
/// Operations done with a card in a shared shift (decision of 20 November 2026). While an employee's card session is
/// active on the terminal, these requests act as that employee, whoever is logged in: the sale, payment or layaway is
/// recorded in their name and checked against their own role and permissions. Reads (GET) keep the logged-in user unless
/// <see cref="IncludeReads"/> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class OperatorScopedAttribute : Attribute
{
    public bool IncludeReads { get; init; }
}
