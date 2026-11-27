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
    public const string RestoreTestType = "PRUEBA_RESTAURACION";
    public const string TestRunning = "EN_PROCESO";
    public const string TestSucceeded = "EXITOSA";
    public const string TestFailed = "FALLIDA";
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

/// <summary>Copia disponible para probar restauración (maqueta 22c).</summary>
public sealed record RestorableCopyDto(Guid CopyId, DateTimeOffset BackupAt, string DestinationType, long? SizeBytes,
    bool Verified, bool Available);

public sealed record RestoreTestDto(Guid Id, Guid CopyId, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Result,
    int? RpoObservedMinutes, int? RtoObservedMinutes, bool IntegrityVerified, bool ApplicationVerified, string? Error);

/// <summary>Metas de Fase 4 del establecimiento (RPO, RTO y frecuencia de prueba), última prueba y copias disponibles.</summary>
public sealed record RestoreOverviewDto(int RpoMinutes, int RtoMinutes, int RestoreTestDays, RestoreTestDto? LastTest,
    DateOnly? NextTestDue, IReadOnlyList<RestorableCopyDto> Copies,
    // Versión del establecimiento, para cambiar las metas sin pisar otro cambio (27/11/2026).
    long GoalsVersion = 0);

/// <summary>
/// Metas de respaldo y recuperación (decisión del usuario del 27/11/2026): pérdida máxima y tiempo para recuperar en
/// minutos, y cada cuántos días se prueba una restauración. Las cambia el Administrador desde la pantalla de Restauración.
/// </summary>
public sealed record UpdateRestoreGoalsRequest(
    [property: JsonRequired] int RpoMinutes,
    [property: JsonRequired] int RtoMinutes,
    [property: JsonRequired] int RestoreTestDays,
    [property: JsonRequired] long ExpectedVersion);

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
    Task<Result<RestoreOverviewDto>> GetRestoreOverviewAsync(ActorContext actor, CancellationToken cancellationToken = default);
    Task<Result<RestoreOverviewDto>> UpdateRestoreGoalsAsync(ActorContext actor, UpdateRestoreGoalsRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Restaura la copia en una base temporal que se borra al terminar; nunca toca la base real.</summary>
    Task<Result<RestoreTestDto>> TestRestoreAsync(ActorContext actor, Guid copyId, CancellationToken cancellationToken = default);
}
