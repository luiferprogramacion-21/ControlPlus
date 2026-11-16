using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ControlPlus.Application.Backups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ControlPlus.Infrastructure.Backups;

/// <summary>
/// Respaldo lógico con pg_dump (formato personalizado) verificado con pg_restore --list. Las copias se escriben en la
/// subcarpeta «ControlPlus» del destino, primero con un nombre temporal y luego se renombran, y se comprueban leyéndolas
/// de nuevo. Solo se listan y borran archivos con el nombre exacto que genera ControlPlus.
/// </summary>
public sealed partial class PgDumpBackupEngine(IOptions<BackupOptions> options, IConfiguration configuration) : IBackupEngine
{
    public const string FolderName = "ControlPlus";
    public const string EncryptedExtension = ".cpbk";

    [GeneratedRegex(@"^controlplus-(?<at>\d{8}T\d{6}Z)-(?<trigger>[A-Z_]+)\.dump(?<encrypted>\.cpbk)?$")]
    private static partial Regex BackupName();

    private BackupOptions Options => options.Value;

    public async Task<DumpResult> DumpAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!BackupName().IsMatch(fileName)) throw new BackupStepException("Nombre de respaldo no válido.");
        var connection = configuration.GetConnectionString(BackupOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connection))
            throw new BackupStepException("Falta la conexión de respaldos (ConnectionStrings:ControlPlusBackup).");
        var builder = new NpgsqlConnectionStringBuilder(connection);
        Directory.CreateDirectory(Options.ResolvedStagingDirectory);
        var target = Path.Combine(Options.ResolvedStagingDirectory, fileName);

        var dumpExit = await RunAsync("pg_dump.exe",
            ["-w", "-h", builder.Host ?? "127.0.0.1", "-p", builder.Port.ToString(CultureInfo.InvariantCulture),
             "-U", builder.Username ?? string.Empty, "-d", builder.Database ?? string.Empty,
             "-Fc", "--no-owner", "--no-acl", "-f", target],
            builder.Password, cancellationToken);
        if (dumpExit != 0 || !File.Exists(target))
            throw new BackupStepException($"pg_dump terminó con error (código {dumpExit}).");
        var size = new FileInfo(target).Length;
        if (size == 0) throw new BackupStepException("pg_dump generó un archivo vacío.");

        var verifyExit = await RunAsync("pg_restore.exe", ["--list", target], null, cancellationToken);
        return new DumpResult(target, size, await HashAsync(target, cancellationToken), verifyExit == 0);
    }

    public async Task<CopyResult> CopyAsync(DumpResult dump, string destinationRoot, bool encrypt, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(destinationRoot, FolderName);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(dump.StagingPath) + (encrypt ? EncryptedExtension : string.Empty);
        var final = Path.Combine(folder, name);
        var partial = final + ".partial";
        if (encrypt)
        {
            var passphrase = Options.EncryptionPassphrase ?? string.Empty;
            var encrypted = BackupCrypto.Encrypt(await File.ReadAllBytesAsync(dump.StagingPath, cancellationToken), passphrase);
            await File.WriteAllBytesAsync(partial, encrypted, cancellationToken);
            File.Move(partial, final, overwrite: true);
            // Se descifra lo escrito para comprobar que se puede recuperar con la contraseña actual.
            var roundTrip = BackupCrypto.Decrypt(await File.ReadAllBytesAsync(final, cancellationToken), passphrase);
            return new CopyResult(final, Convert.ToHexString(SHA256.HashData(roundTrip)) == dump.Sha256);
        }
        File.Copy(dump.StagingPath, partial, overwrite: true);
        File.Move(partial, final, overwrite: true);
        return new CopyResult(final, await HashAsync(final, cancellationToken) == dump.Sha256);
    }

    public IReadOnlyList<RetainedCopy> ListCopies(string destinationRoot)
    {
        var folder = Path.Combine(destinationRoot, FolderName);
        if (!Directory.Exists(folder)) return [];
        var copies = new List<RetainedCopy>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var match = BackupName().Match(Path.GetFileName(path));
            if (!match.Success) continue;
            var at = DateTime.ParseExact(match.Groups["at"].Value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            copies.Add(new RetainedCopy(Path.GetFileName(path), at, match.Groups["trigger"].Value == BackupCodes.PreUpdate));
        }
        return copies;
    }

    public void Delete(string destinationRoot, string fileName)
    {
        if (!BackupName().IsMatch(fileName)) return;
        var path = Path.Combine(destinationRoot, FolderName, fileName);
        if (File.Exists(path)) File.Delete(path);
    }

    public (long? FreeBytes, long? TotalBytes) GetSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return (null, null);
            var drive = new DriveInfo(root);
            return drive.IsReady ? (drive.AvailableFreeSpace, drive.TotalSize) : (null, null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, null);
        }
    }

    public string? ValidateDestination(string type, string path)
    {
        if (!Path.IsPathFullyQualified(path)) return "Indique una carpeta completa, por ejemplo E:\\Respaldos.";
        string root;
        try { root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "La carpeta indicada no es válida.";
        }
        if (type == BackupCodes.LocalDestination &&
            string.Equals(root.TrimEnd('\\', '/'), Options.DatabaseDriveRoot.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return $"La copia local debe estar en un disco distinto al de la base de datos ({Options.DatabaseDriveRoot}): use un disco externo u otro disco interno.";
        try
        {
            var folder = Path.Combine(path, FolderName);
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".prueba-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ControlPlus");
            File.Delete(probe);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "No se puede escribir en la carpeta indicada. Verifique que el disco esté conectado y tenga permisos.";
        }
    }

    public void PruneStaging(int keep)
    {
        var folder = Options.ResolvedStagingDirectory;
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder)
                     .Where(x => BackupName().IsMatch(Path.GetFileName(x)))
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(keep))
        {
            try { File.Delete(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task<int> RunAsync(string program, IReadOnlyList<string> arguments, string? password, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(Options.PgBinDirectory, program))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("PGPASSWORD");
        if (password is not null) start.Environment["PGPASSWORD"] = password;
        start.Environment["PGCONNECT_TIMEOUT"] = "10";
        if (!File.Exists(start.FileName)) throw new BackupStepException($"No se encontró {program} en la carpeta de PostgreSQL configurada.");
        using var process = Process.Start(start) ?? throw new BackupStepException($"No se pudo iniciar {program}.");
        // Se leen las salidas para que el proceso no se bloquee; no se guardan porque pueden traer rutas internas.
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(output, error);
        return process.ExitCode;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
