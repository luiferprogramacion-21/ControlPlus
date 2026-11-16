using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Backups;

public sealed record DumpResult(string StagingPath, long SizeBytes, string Sha256, bool Verified);

public sealed record CopyResult(string FinalPath, bool Verified);

/// <summary>Volcado con pg_dump, copia a los destinos (cifrada si corresponde) y manejo de archivos.</summary>
public interface IBackupEngine
{
    /// <summary>Genera el respaldo en la carpeta de trabajo y lo verifica con pg_restore --list.</summary>
    Task<DumpResult> DumpAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Copia (y cifra si se pide) el respaldo y comprueba que la copia escrita coincide con el original.</summary>
    Task<CopyResult> CopyAsync(DumpResult dump, string destinationRoot, bool encrypt, CancellationToken cancellationToken);

    /// <summary>Copias de ControlPlus que existen en el destino; ignora cualquier otro archivo de la carpeta.</summary>
    IReadOnlyList<RetainedCopy> ListCopies(string destinationRoot);

    void Delete(string destinationRoot, string fileName);

    (long? FreeBytes, long? TotalBytes) GetSpace(string path);

    /// <summary>Devuelve un mensaje si la carpeta no sirve como destino (ruta, escritura o disco); null si sirve.</summary>
    string? ValidateDestination(string type, string path);

    /// <summary>Conserva solo los últimos archivos de la carpeta de trabajo.</summary>
    void PruneStaging(int keep);
}

/// <summary>Configuración técnica del equipo: si el servicio está activo, cada cuánto y si hay contraseña de cifrado.</summary>
public interface IBackupRuntime
{
    BackupRuntimeSettings Settings { get; }
}

public interface IBackupRepository
{
    Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<DestinoRespaldo>> GetDestinationsAsync(Guid installationId, CancellationToken cancellationToken);
    Task<DestinoRespaldo?> GetDestinationForUpdateAsync(Guid installationId, string type, CancellationToken cancellationToken);
    Task AddDestinationAsync(DestinoRespaldo destination, CancellationToken cancellationToken);
    Task AddExecutionAsync(EjecucionRespaldo execution, CancellationToken cancellationToken);
    Task AddCopyAsync(CopiaRespaldo copy, CancellationToken cancellationToken);
    Task<EjecucionRespaldo?> GetLastExecutionAsync(Guid installationId, bool onlySuccessful, CancellationToken cancellationToken);
    Task<CopiaRespaldo?> GetLastCopyAsync(Guid destinationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EjecucionRespaldo>> GetRecentExecutionsAsync(Guid installationId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<(CopiaRespaldo Copy, string DestinationType)>> GetRecentCopiesAsync(Guid installationId, int limit,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
