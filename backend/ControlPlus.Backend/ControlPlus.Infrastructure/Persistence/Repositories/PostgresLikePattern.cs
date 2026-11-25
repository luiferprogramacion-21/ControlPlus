namespace ControlPlus.Infrastructure.Persistence.Repositories;

internal static class PostgresLikePattern
{
    internal const string EscapeCharacter = "\\";

    internal static string Escape(string value) => value
        .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
        .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
        .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
