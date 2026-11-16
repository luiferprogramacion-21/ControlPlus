namespace ControlPlus.Application.Security.Ports;

/// <summary>Nombres de usuario para mostrar quién hizo y quién autorizó cada evento de auditoría.</summary>
public interface IAuditUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetUserNamesAsync(IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
