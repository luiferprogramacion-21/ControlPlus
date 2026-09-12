namespace ControlPlus.Domain.Common;

internal static class DomainGuard
{
    public static string RequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }

    public static string NormalizeCode(string? value, string parameterName) =>
        RequiredText(value, parameterName).ToUpperInvariant();

    public static Guid RequiredId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An empty identifier is not allowed.", parameterName);
        }

        return value;
    }

    public static Guid? OptionalId(Guid? value, string parameterName)
    {
        if (value is Guid id)
        {
            RequiredId(id, parameterName);
        }

        return value;
    }

    public static DateTimeOffset Utc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The date and time must use UTC (offset +00:00).", parameterName);
        }

        return value;
    }
}
