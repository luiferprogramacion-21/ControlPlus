using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    public virtual DbSet<AperturaCajon> AperturaCajon { get; set; }

    private static void ConfigureCashDrawerModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PerfilImpresora>(entity =>
        {
            entity.Property(e => e.CajonHabilitado)
                .HasDefaultValue(false).HasColumnName("cajon_habilitado");
            entity.Property(e => e.CajonPin).HasColumnName("cajon_pin");
            entity.Property(e => e.CajonPulsoOnMs)
                .HasDefaultValue((short)100).HasColumnName("cajon_pulso_on_ms");
            entity.Property(e => e.CajonPulsoOffMs)
                .HasDefaultValue((short)100).HasColumnName("cajon_pulso_off_ms");
        });

        modelBuilder.Entity<AperturaCajon>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_apertura_cajon");
            entity.ToTable("apertura_cajon", "caja", tb => tb.HasComment(
                "Intentos de pulso del cajon separados de ventas e impresion, sin reintentos automaticos."));
            entity.HasIndex(e => new { e.TipoOperacion, e.OperacionId }, "uq_apertura_cajon_operacion")
                .IsUnique();
            entity.HasIndex(e => new { e.TerminalId, e.FechaSolicitud }, "ix_apertura_cajon_terminal_fecha")
                .IsDescending(false, true);
            entity.HasIndex(e => e.PerfilImpresoraId, "ix_apertura_cajon_perfil");
            entity.HasIndex(e => e.TurnoCajaId, "ix_apertura_cajon_turno");
            entity.HasIndex(e => e.SolicitadoPorId, "ix_apertura_cajon_usuario");

            entity.Property(e => e.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(e => e.PerfilImpresoraId).HasColumnName("perfil_impresora_id");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.SolicitadoPorId).HasColumnName("solicitado_por_id");
            entity.Property(e => e.TipoOperacion).HasMaxLength(30).HasColumnName("tipo_operacion");
            entity.Property(e => e.OperacionId).HasColumnName("operacion_id");
            entity.Property(e => e.Estado).HasMaxLength(20)
                .HasDefaultValue("INTENTANDO").HasColumnName("estado");
            entity.Property(e => e.Motivo).HasMaxLength(500).HasColumnName("motivo");
            entity.Property(e => e.FechaSolicitud).HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_solicitud");
            entity.Property(e => e.FechaResultado).HasColumnName("fecha_resultado");
            entity.Property(e => e.ErrorSanitizado).HasMaxLength(300)
                .HasColumnName("error_sanitizado");

            entity.HasOne(e => e.PerfilImpresora).WithMany()
                .HasForeignKey(e => e.PerfilImpresoraId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apertura_cajon_perfil");
            entity.HasOne(e => e.Terminal).WithMany()
                .HasForeignKey(e => e.TerminalId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apertura_cajon_terminal");
            entity.HasOne(e => e.TurnoCaja).WithMany()
                .HasForeignKey(e => e.TurnoCajaId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apertura_cajon_turno");
            entity.HasOne(e => e.SolicitadoPor).WithMany()
                .HasForeignKey(e => e.SolicitadoPorId).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apertura_cajon_usuario");
        });
    }
}
