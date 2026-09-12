using System.IO.Compression;
using System.Reflection;

namespace ControlPlus.Infrastructure.Persistence.Migrations;

internal static class OfficialDatabaseScript
{
    private const string ResourceName =
        "ControlPlus.Infrastructure.Database.ControlPlus_Fase4_Documento_y_Complementos.zip";

    private const string ScriptFileName = "Script_Base_de_Datos_PostgreSQL_V3.sql";

    public static string Load()
    {
        var assembly = typeof(OfficialDatabaseScript).Assembly;
        using var resource = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"No se encontró el recurso oficial {ResourceName}.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.SingleOrDefault(candidate =>
            candidate.FullName.EndsWith(ScriptFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"El paquete oficial no contiene {ScriptFileName}.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
