using System.Text.Json.Serialization;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;

namespace ControlPlus.Application.Backups;

/// <summary>Valores oficiales de Fase 4 (configuracion.ejecucion_respaldo, destino_respaldo y copia_respaldo).</summary>
public static class BackupCodes
{
    public const string LogicalStrategy = "LOGICO_PG_DUMP";
    public const string Scheduled = "PROGRAMADO";
    public const string CashClose = "CIERRE_CAJA";
    public const string Manual = "MANUAL";
    public const string PreUpdate = "PRE_ACTUALIZACION";
    public const string LocalDestination = "LOCAL_FISICO_SEPARADO";
    public const string ExternalDestination = "EXTERNO_CIFRADO";
    public const string Running = "EN_PROCESO";
    public const string Succeeded = "EXITOSO";
    public const string Failed = "FALLIDO";
    public const string CopyCompleted = "COMPLETADA";
    public const string CopyFailed = "FALLIDA";
    public const string CopySending = "ENVIANDO";
    /// <summary>Nombre de la configuración que guarda la contraseña de cifrado; nunca se guarda la contraseña.</summary>
    public const string PassphraseReference = "Backup:EncryptionPassphrase";
    public static readonly IReadOnlyList<string> DestinationTypes = [LocalDestination, ExternalDestination];
}

public sealed record BackupExecutionDto(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Trigger,
    string Result, long? SizeBytes, bool Verified, string? Error);

public sealed record BackupCopyDto(Guid Id, Guid ExecutionId, string DestinationType, DateTimeOffset? CompletedAt,
    string State, bool Encrypted, bool Verified, string? Error);

public sealed record BackupDestinationDto(string Type, string Name, string? Path, bool Active, bool Encrypted,
    bool PassphraseConfigured, int DailyRetention, int WeeklyRetention, int MonthlyRetention,
    long? FreeBytes, long? TotalBytes, BackupCopyDto? LastCopy, long? Version);

public sealed record BackupEventDto(DateTimeOffset At, string Kind, string Description, string Result);

/// <summary>Estado de la pantalla «Soporte y continuidad».</summary>
public sealed record BackupStatusDto(bool ServiceEnabled, int IntervalMinutes, BackupExecutionDto? LastBackup,
    BackupExecutionDto? LastSuccessfulBackup, BackupCopyDto? LastExternalCopy,
    IReadOnlyList<BackupDestinationDto> Destinations, IReadOnlyList<BackupEventDto> RecentEvents);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateBackupDestinationRequest(
    [property: JsonRequired] string Path,
    [property: JsonRequired] bool Active,
    long? ExpectedVersion);

public sealed record BackupRunRequested(Guid RequestId, string Trigger);

/// <summary>Configuración técnica del servicio, leída de la configuración local (no de la base).</summary>
public sealed record BackupRuntimeSettings(bool Enabled, int IntervalMinutes, bool PassphraseConfigured, string? DatabaseDriveRoot);

/// <summary>Cola en memoria que despierta al servicio de respaldos (cierre de caja o botón «Crear respaldo»).</summary>
public interface IBackupRequestQueue
{
    bool Request(string trigger);
}

public interface IBackupService
{
    /// <summary>Ejecuta un respaldo completo: volcado, verificación, copias y retención. Lo usa el servicio en segundo plano.</summary>
    Task<BackupExecutionDto?> RunAsync(string trigger, CancellationToken cancellationToken = default);
    Task<bool> IsDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<Result<BackupStatusDto>> GetStatusAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<BackupRunRequested>> RequestManualAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<BackupDestinationDto>> UpdateDestinationAsync(ActorContext actor, string type,
        UpdateBackupDestinationRequest request, CancellationToken cancellationToken = default);
}
