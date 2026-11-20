using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext
{
    public DbSet<CorreccionApartado> CorreccionApartado => Set<CorreccionApartado>();
    public DbSet<ReembolsoCorreccionApartado> ReembolsoCorreccionApartado => Set<ReembolsoCorreccionApartado>();

    private static void ConfigureLayawayCorrections(ModelBuilder model)
    {
        model.Entity<DetalleApartado>().Property(x => x.Vigente).HasColumnName("vigente").HasDefaultValue(true);
        model.Entity<CorreccionApartado>(e =>
        {
            e.ToTable("correccion_apartado", "ventas"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Antes).HasColumnType("jsonb"); e.Property(x => x.Despues).HasColumnType("jsonb"); e.Property(x => x.Motivo).HasMaxLength(500);
            foreach (var property in e.Metadata.GetProperties())
            { property.SetColumnName(Snake(property.Name)); if (property.ClrType == typeof(decimal)) { property.SetPrecision(18); property.SetScale(0); } }
            e.HasOne<Apartado>().WithMany().HasForeignKey(x => x.ApartadoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AutorizacionOperacion>().WithMany().HasForeignKey(x => x.AutorizacionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ReembolsoCorreccionApartado>(e =>
        {
            e.ToTable("reembolso_correccion_apartado", "ventas"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Valor).HasPrecision(18, 0); e.Property(x => x.Referencia).HasMaxLength(200);
            foreach (var property in e.Metadata.GetProperties()) property.SetColumnName(Snake(property.Name));
            e.HasOne(x => x.Correccion).WithMany().HasForeignKey(x => x.CorreccionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.MovimientoCaja).WithMany().HasForeignKey(x => x.MovimientoCajaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TurnoCaja>().WithMany().HasForeignKey(x => x.TurnoCajaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MetodoPago>().WithMany().HasForeignKey(x => x.MetodoPagoId).OnDelete(DeleteBehavior.Restrict);
        });
        static string Snake(string value) => string.Concat(value.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
