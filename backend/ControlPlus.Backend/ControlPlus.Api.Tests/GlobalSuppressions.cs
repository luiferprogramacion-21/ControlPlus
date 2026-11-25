using System.Diagnostics.CodeAnalysis;

// Estas reglas de rendimiento no aportan valor en datos pequeños y aislados de pruebas.
[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments",
    Justification = "Las matrices son datos breves de aserciones y contratos ejecutados solo durante pruebas.")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Los helpers conservan la forma de la fixture para facilitar su lectura y mantenimiento.")]
[assembly: SuppressMessage("Performance", "SYSLIB1045:Convert to GeneratedRegexAttribute",
    Justification = "Las expresiones se ejecutan una vez sobre evidencia controlada dentro de cada prueba.")]
[assembly: SuppressMessage("Usage", "xUnit1042:Avoid untyped theory data",
    Justification = "Las matrices heterogéneas validan gramática CLI y conservan nombres descriptivos de cada caso.")]
