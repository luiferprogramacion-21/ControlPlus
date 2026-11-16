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
/// Respaldo lógico con pg_dump (formato personalizado) verificado con pg_restore --list. La copia guarda dueños y
/// permisos para que la restauración de emergencia los devuelva; la prueba de restauración los omite. Las copias se escriben en la
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
             "-Fc", "-f", target],
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

    public bool CopyExists(string path) => File.Exists(path);

    public async Task<RestoreTestResult> TestRestoreAsync(string copyPath, bool encrypted, string? expectedSha256,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var connection = configuration.GetConnectionString(BackupOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connection))
            return new RestoreTestResult(false, false, watch.Elapsed, null, "Falta la conexión de respaldos (ConnectionStrings:ControlPlusBackup).");
        var builder = new NpgsqlConnectionStringBuilder(connection);
        Directory.CreateDirectory(Options.ResolvedStagingDirectory);
        var plainPath = Path.Combine(Options.ResolvedStagingDirectory, $"prueba-restauracion-{Guid.NewGuid():N}.dump");
        // Base temporal con nombre propio; la base real nunca se usa como destino de la restauración.
        var database = $"controlplus_restore_{Guid.NewGuid():N}";
        var created = false;
        try
        {
            var bytes = await File.ReadAllBytesAsync(copyPath, cancellationToken);
            if (encrypted) bytes = BackupCrypto.Decrypt(bytes, Options.EncryptionPassphrase ?? string.Empty);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (expectedSha256 is not null && !string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return new RestoreTestResult(false, false, watch.Elapsed, null, "La copia no coincide con la huella del respaldo original.");
            await File.WriteAllBytesAsync(plainPath, bytes, cancellationToken);

            await ExecuteOnMaintenanceAsync(builder, $"CREATE DATABASE \"{database}\"", cancellationToken);
            created = true;
            var exit = await RunAsync("pg_restore.exe",
                ["-w", "-h", builder.Host ?? "127.0.0.1", "-p", builder.Port.ToString(CultureInfo.InvariantCulture),
                 "-U", builder.Username ?? string.Empty, "-d", database, "--no-owner", "--no-acl", "--exit-on-error", plainPath],
                builder.Password, cancellationToken);
            if (exit != 0)
                return new RestoreTestResult(true, false, watch.Elapsed, null, $"pg_restore terminó con error (código {exit}).");

            var target = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { Database = database, Pooling = false };
            await using var restored = new NpgsqlConnection(target.ConnectionString);
            await restored.OpenAsync(cancellationToken);
            var migrations = Convert.ToInt32(await ScalarAsync(restored, "SELECT count(*) FROM public.\"__EFMigrationsHistory\"", cancellationToken), CultureInfo.InvariantCulture);
            var establishments = Convert.ToInt64(await ScalarAsync(restored, "SELECT count(*) FROM configuracion.establecimiento", cancellationToken), CultureInfo.InvariantCulture);
            // Las tablas operativas deben poder consultarse aunque estén vacías.
            _ = await ScalarAsync(restored, "SELECT count(*) FROM catalogo.producto", cancellationToken);
            _ = await ScalarAsync(restored, "SELECT count(*) FROM ventas.venta", cancellationToken);
            var applicationOk = migrations > 0 && establishments > 0;
            return new RestoreTestResult(true, applicationOk, watch.Elapsed, migrations,
                applicationOk ? null : "La base restaurada no tiene historial de migraciones o establecimiento.");
        }
        catch (CryptographicException)
        {
            return new RestoreTestResult(false, false, watch.Elapsed, null,
                "No se pudo descifrar la copia: la contraseña de cifrado es distinta o el archivo está alterado.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return new RestoreTestResult(true, false, watch.Elapsed, null,
                "El usuario de respaldo no puede crear bases temporales: ejecute de nuevo la preparación de respaldos.");
        }
        catch (Exception exception) when (exception is NpgsqlException or IOException or UnauthorizedAccessException)
        {
            var code = exception is PostgresException postgres ? $" (código {postgres.SqlState})" : string.Empty;
            return new RestoreTestResult(false, false, watch.Elapsed, null, "No se pudo completar la prueba de restauración" + code + ".");
        }
        finally
        {
            if (created)
            {
                try { await ExecuteOnMaintenanceAsync(builder, $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", CancellationToken.None); }
                catch (NpgsqlException) { /* Queda con nombre controlplus_restore_* y se puede borrar a mano. */ }
            }
            try { if (File.Exists(plainPath)) File.Delete(plainPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task ExecuteOnMaintenanceAsync(NpgsqlConnectionStringBuilder builder, string sql, CancellationToken cancellationToken)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { Database = "postgres", Pooling = false };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(cancellationToken);
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
