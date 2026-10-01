namespace ControlPlus.Domain.Common;

/// <summary>
/// Indicates that an operation would violate a business rule in the domain.
/// </summary>
public sealed class DomainRuleViolationException : InvalidOperationException
{
    public DomainRuleViolationException(string message) : base(message)
    {
    }

    public DomainRuleViolationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
