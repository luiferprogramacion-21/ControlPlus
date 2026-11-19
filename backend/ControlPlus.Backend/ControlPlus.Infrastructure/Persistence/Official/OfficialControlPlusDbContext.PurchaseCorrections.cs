using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    private static void ConfigurePurchaseCorrectionsModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Venta>().Property(x => x.VentaOrigenId).HasColumnName("venta_origen_id");
        modelBuilder.Entity<Venta>().HasOne<Venta>().WithMany().HasForeignKey(x => x.VentaOrigenId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CorreccionRecepcion>(entity =>
        {
            entity.ToTable("correccion_recepcion", "compras");
            entity.HasKey(x => x.Id).HasName("pk_correccion_recepcion");
            entity.Property(x => x.Id).ValueGeneratedNever().HasColumnName("id");
            entity.Property(x => x.CompraId).HasColumnName("compra_id");
            entity.Property(x => x.PedidoCompraId).HasColumnName("pedido_compra_id");
            entity.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            entity.Property(x => x.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(x => x.VersionPedidoAnterior).HasColumnName("version_pedido_anterior");
            entity.Property(x => x.VersionPedidoPosterior).HasColumnName("version_pedido_posterior");
            entity.Property(x => x.EstadoAnterior).HasMaxLength(30).HasColumnName("estado_anterior");
            entity.Property(x => x.EstadoPosterior).HasMaxLength(30).HasColumnName("estado_posterior");
            entity.Property(x => x.FechaHora).HasColumnName("fecha_hora");
            entity.Property(x => x.CorrelacionId).HasColumnName("correlacion_id");
            entity.HasIndex(x => x.CompraId).IsUnique();
            entity.HasOne<Compra>().WithMany().HasForeignKey(x => x.CompraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PedidoCompra>().WithMany().HasForeignKey(x => x.PedidoCompraId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AutorizacionOperacion>().WithMany().HasForeignKey(x => x.AutorizacionOperacionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
