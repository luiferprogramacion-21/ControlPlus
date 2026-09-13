using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    public virtual DbSet<UsuarioPermiso> UsuarioPermiso { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsuarioPermiso>(entity =>
        {
            entity.HasKey(x => new { x.UsuarioId, x.PermisoId }).HasName("pk_usuario_permiso");
            entity.ToTable("usuario_permiso", "seguridad");
            entity.HasIndex(x => x.PermisoId).HasDatabaseName("ix_usuario_permiso_permiso");
            entity.HasIndex(x => x.AsignadoPorId).HasDatabaseName("ix_usuario_permiso_asignado_por");
            entity.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            entity.Property(x => x.PermisoId).HasColumnName("permiso_id");
            entity.Property(x => x.Efecto).HasMaxLength(10).HasColumnName("efecto");
            entity.Property(x => x.FechaAsignacion).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnName("fecha_asignacion");
            entity.Property(x => x.AsignadoPorId).HasColumnName("asignado_por_id");
            entity.HasOne(x => x.Usuario).WithMany(x => x.UsuarioPermiso)
                .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_permiso_usuario");
            entity.HasOne(x => x.Permiso).WithMany(x => x.UsuarioPermiso)
                .HasForeignKey(x => x.PermisoId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_permiso_permiso");
            entity.HasOne(x => x.AsignadoPor).WithMany(x => x.UsuarioPermisoAsignadoPor)
                .HasForeignKey(x => x.AsignadoPorId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_permiso_asignado_por");
        });
    }
}
