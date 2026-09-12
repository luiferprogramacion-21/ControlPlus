namespace ControlPlus.Domain.Common;

/// <summary>
/// Indicates that an operation would violate a business rule in the domain.
/// </summary>
public sealed class DomainRuleViolationException(string message) : InvalidOperationException(message);
