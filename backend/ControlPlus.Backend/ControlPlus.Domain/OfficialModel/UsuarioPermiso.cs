namespace ControlPlus.Domain.OfficialModel;

public sealed class UsuarioPermiso
{
    public Guid UsuarioId { get; set; }
    public Guid PermisoId { get; set; }
    public string Efecto { get; set; } = null!;
    public DateTime FechaAsignacion { get; set; }
    public Guid? AsignadoPorId { get; set; }

    public Usuario Usuario { get; set; } = null!;
    public Permiso Permiso { get; set; } = null!;
    public Usuario? AsignadoPor { get; set; }

    public bool IsGranted => Efecto == UserPermissionEffects.Grant;
    public bool IsRevoked => Efecto == UserPermissionEffects.Revoke;
}

public static class UserPermissionEffects
{
    public const string Grant = "CONCEDER";
    public const string Revoke = "REVOCAR";
}
