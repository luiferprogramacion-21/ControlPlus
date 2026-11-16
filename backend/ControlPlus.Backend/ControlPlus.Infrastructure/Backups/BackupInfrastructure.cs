using System.Threading.Channels;
using ControlPlus.Application.Backups;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ControlPlus.Infrastructure.Backups;

/// <summary>
/// Configuración local del servicio de respaldos (sección "Backup" y la cadena "ControlPlusBackup").
/// Apagado por defecto: un equipo sin configurar, y las pruebas, nunca ejecutan pg_dump.
/// </summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    public const string ConnectionStringName = "ControlPlusBackup";

    public bool Enabled { get; init; }
    public int IntervalMinutes { get; init; } = 30;
    public string PgBinDirectory { get; init; } = @"C:\Program Files\PostgreSQL\18\bin";
    public string? StagingDirectory { get; init; }
    /// <summary>Raíz del disco donde PostgreSQL guarda la base; la copia local debe estar en otro disco.</summary>
    public string DatabaseDriveRoot { get; init; } = @"C:\";
    /// <summary>Secreto local (User Secrets o variable de entorno); nunca se guarda en la base ni en el repositorio.</summary>
    public string? EncryptionPassphrase { get; init; }

    public string ResolvedStagingDirectory => string.IsNullOrWhiteSpace(StagingDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControlPlus", "respaldos-trabajo")
        : StagingDirectory;
}

public sealed class BackupRuntime(IOptions<BackupOptions> options, IConfiguration configuration) : IBackupRuntime
{
    public BackupRuntimeSettings Settings
    {
        get
        {
            var value = options.Value;
            var connectionConfigured = !string.IsNullOrWhiteSpace(configuration.GetConnectionString(BackupOptions.ConnectionStringName));
            return new BackupRuntimeSettings(value.Enabled && connectionConfigured, Math.Max(5, value.IntervalMinutes),
                (value.EncryptionPassphrase?.Length ?? 0) >= BackupCrypto.MinimumPassphraseLength, value.DatabaseDriveRoot);
        }
    }
}

/// <summary>Cola en memoria con pocos lugares: varios pedidos seguidos se resuelven con un solo respaldo reciente.</summary>
public sealed class BackupRequestQueue : IBackupRequestQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(4) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool Request(string trigger) => _channel.Writer.TryWrite(trigger);

    /// <summary>Espera un pedido hasta el tiempo indicado; devuelve null si no llegó ninguno.</summary>
    public async Task<string?> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            return await _channel.Reader.WaitToReadAsync(timer.Token) && _channel.Reader.TryRead(out var trigger) ? trigger : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
