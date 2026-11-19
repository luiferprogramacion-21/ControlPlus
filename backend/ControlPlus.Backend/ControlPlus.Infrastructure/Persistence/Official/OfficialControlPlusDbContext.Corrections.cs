using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    private static void ConfigureCashInventoryCorrectionsModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CorreccionMovimientoInventario>(e =>
        {
            e.ToTable("correccion_movimiento_inventario", "compras");
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
            e.Property(x => x.MovimientoOriginalId).HasColumnName("movimiento_original_id");
            e.Property(x => x.MovimientoCorrectorId).HasColumnName("movimiento_corrector_id");
            e.Property(x => x.FechaHora).HasColumnName("fecha_hora");
            e.HasIndex(x => x.MovimientoOriginalId).IsUnique();
            e.HasIndex(x => x.MovimientoCorrectorId).IsUnique();
            e.HasOne<MovimientoInventario>().WithMany().HasForeignKey(x => x.MovimientoOriginalId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MovimientoInventario>().WithMany().HasForeignKey(x => x.MovimientoCorrectorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RevisionCierreCaja>(e =>
        {
            e.ToTable("revision_cierre", "caja");
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
            e.Property(x => x.TurnoCajaId).HasColumnName("turno_caja_id");
            e.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            e.Property(x => x.EfectivoEsperado).HasPrecision(18, 0).HasColumnName("efectivo_esperado");
            e.Property(x => x.ContadoOriginal).HasPrecision(18, 0).HasColumnName("contado_original");
            e.Property(x => x.ContadoRevisado).HasPrecision(18, 0).HasColumnName("contado_revisado");
            e.Property(x => x.Motivo).HasMaxLength(500).HasColumnName("motivo");
            e.Property(x => x.FechaHora).HasColumnName("fecha_hora");
            e.Property(x => x.CorrelacionId).HasColumnName("correlacion_id");
            e.HasIndex(x => new { x.TurnoCajaId, x.FechaHora });
            e.HasOne<TurnoCaja>().WithMany().HasForeignKey(x => x.TurnoCajaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
