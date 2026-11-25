using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "SYSLIB1045:Convert to GeneratedRegexAttribute",
    Justification = "Las expresiones se ejecutan una vez sobre scripts SQL controlados dentro de cada prueba de migración.")]
