using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    public virtual DbSet<UsuarioPermiso> UsuarioPermiso { get; set; }

    public virtual DbSet<DetalleArqueoMedioPago> DetalleArqueoMedioPago { get; set; }

    public virtual DbSet<AutorizacionCierreTurno> AutorizacionCierreTurno { get; set; }

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

        modelBuilder.Entity<DetalleArqueoMedioPago>(entity =>
        {
            entity.HasKey(x => x.Id).HasName("pk_detalle_arqueo_medio_pago");
            entity.ToTable("detalle_arqueo_medio_pago", "caja");
            entity.HasIndex(x => new { x.TurnoCajaId, x.MetodoPagoId }, "uq_detalle_arqueo_turno_metodo").IsUnique();
            entity.HasIndex(x => x.MetodoPagoId, "ix_detalle_arqueo_metodo_pago");
            entity.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(x => x.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(x => x.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(x => x.ValorEsperado).HasPrecision(18).HasColumnName("valor_esperado");
            entity.Property(x => x.ValorContado).HasPrecision(18).HasColumnName("valor_contado");
            entity.Property(x => x.Diferencia).HasPrecision(18).HasColumnName("diferencia");
            entity.HasOne(x => x.TurnoCaja).WithMany(x => x.DetalleArqueoMedioPago)
                .HasForeignKey(x => x.TurnoCajaId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_arqueo_turno");
            entity.HasOne(x => x.MetodoPago).WithMany(x => x.DetalleArqueoMedioPago)
                .HasForeignKey(x => x.MetodoPagoId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_arqueo_metodo_pago");
        });

        modelBuilder.Entity<TurnoCaja>(entity =>
        {
            entity.Property(x => x.DiferenciaTotal).HasPrecision(18, 0).HasColumnName("diferencia_total");
        });

        modelBuilder.Entity<AutorizacionCierreTurno>(entity =>
        {
            entity.ToTable("autorizacion_cierre_turno", "caja");
            entity.HasKey(x => x.TurnoCajaId).HasName("pk_autorizacion_cierre_turno");
            entity.HasIndex(x => x.AutorizacionId, "uq_autorizacion_cierre_autorizacion").IsUnique();
            entity.Property(x => x.TurnoCajaId).ValueGeneratedNever().HasColumnName("turno_caja_id");
            entity.Property(x => x.AutorizacionId).HasColumnName("autorizacion_id");
            entity.Property(x => x.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(x => x.UsuarioEjecutorId).HasColumnName("usuario_ejecutor_id");
            entity.Property(x => x.FechaVinculacion).HasColumnName("fecha_vinculacion");
            entity.Property(x => x.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(x => x.EstablecimientoId).HasColumnName("establecimiento_id");
            entity.HasOne(x => x.TurnoCaja).WithOne(x => x.AutorizacionCierre)
                .HasForeignKey<AutorizacionCierreTurno>(x => x.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_autorizacion_cierre_turno");
            entity.HasOne(x => x.Autorizacion).WithOne(x => x.CierreTurno)
                .HasForeignKey<AutorizacionCierreTurno>(x => x.AutorizacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_autorizacion_cierre_autorizacion");
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioEjecutorId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_autorizacion_cierre_ejecutor");
            entity.HasOne<Instalacion>().WithMany().HasForeignKey(x => x.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_autorizacion_cierre_instalacion");
            entity.HasOne<Establecimiento>().WithMany().HasForeignKey(x => x.EstablecimientoId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_autorizacion_cierre_establecimiento");
        });

        OfficialConcurrencyConfiguration.Apply(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareConcurrencyVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareConcurrencyVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareConcurrencyVersions()
    {
        ChangeTracker.DetectChanges();

        foreach (var entry in ChangeTracker.Entries().Where(candidate => candidate.State == EntityState.Modified))
        {
            var metadata = entry.Metadata.FindProperty("Version");
            if (metadata is not { IsConcurrencyToken: true } || metadata.ClrType != typeof(long))
            {
                continue;
            }

            var version = entry.Property("Version");
            var original = (long)(version.OriginalValue ?? 0L);
            version.CurrentValue = checked(original + 1);
            version.IsModified = true;
        }
    }
}
