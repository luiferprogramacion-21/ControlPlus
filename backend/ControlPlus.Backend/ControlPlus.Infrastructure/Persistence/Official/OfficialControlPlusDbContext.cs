using System;
using System.Collections.Generic;
using ControlPlus.Domain.OfficialModel;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Official;

public partial class OfficialControlPlusDbContext : DbContext
{
    public OfficialControlPlusDbContext(DbContextOptions<OfficialControlPlusDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AjusteCreditoCambio> AjusteCreditoCambio { get; set; }

    public virtual DbSet<AnulacionVenta> AnulacionVenta { get; set; }

    public virtual DbSet<Apartado> Apartado { get; set; }

    public virtual DbSet<AutorizacionOperacion> AutorizacionOperacion { get; set; }

    public virtual DbSet<BorradorVenta> BorradorVenta { get; set; }

    public virtual DbSet<Caja> Caja { get; set; }

    public virtual DbSet<CambioVenta> CambioVenta { get; set; }

    public virtual DbSet<Categoria> Categoria { get; set; }

    public virtual DbSet<Cliente> Cliente { get; set; }

    public virtual DbSet<Compra> Compra { get; set; }

    public virtual DbSet<ConsecutivoDocumento> ConsecutivoDocumento { get; set; }

    public virtual DbSet<CopiaRespaldo> CopiaRespaldo { get; set; }

    public virtual DbSet<CredencialUsuario> CredencialUsuario { get; set; }

    public virtual DbSet<Credito> Credito { get; set; }

    public virtual DbSet<DestinoRespaldo> DestinoRespaldo { get; set; }

    public virtual DbSet<DetalleApartado> DetalleApartado { get; set; }

    public virtual DbSet<DetalleBorradorVenta> DetalleBorradorVenta { get; set; }

    public virtual DbSet<DetalleCambioVenta> DetalleCambioVenta { get; set; }

    public virtual DbSet<DetalleCompra> DetalleCompra { get; set; }

    public virtual DbSet<DetallePedidoCompra> DetallePedidoCompra { get; set; }

    public virtual DbSet<DetalleVenta> DetalleVenta { get; set; }

    public virtual DbSet<DocumentoEmitido> DocumentoEmitido { get; set; }

    public virtual DbSet<EjecucionRespaldo> EjecucionRespaldo { get; set; }

    public virtual DbSet<Establecimiento> Establecimiento { get; set; }

    public virtual DbSet<EventoAuditoria> EventoAuditoria { get; set; }

    public virtual DbSet<Instalacion> Instalacion { get; set; }

    public virtual DbSet<LimiteOperacionRol> LimiteOperacionRol { get; set; }

    public virtual DbSet<MetodoPago> MetodoPago { get; set; }

    public virtual DbSet<MotivoOperacion> MotivoOperacion { get; set; }

    public virtual DbSet<MovimientoCaja> MovimientoCaja { get; set; }

    public virtual DbSet<MovimientoInventario> MovimientoInventario { get; set; }

    public virtual DbSet<OperacionIdempotente> OperacionIdempotente { get; set; }

    public virtual DbSet<PagoApartado> PagoApartado { get; set; }

    public virtual DbSet<PagoCambioVenta> PagoCambioVenta { get; set; }

    public virtual DbSet<PagoCredito> PagoCredito { get; set; }

    public virtual DbSet<PagoVenta> PagoVenta { get; set; }

    public virtual DbSet<PedidoCompra> PedidoCompra { get; set; }

    public virtual DbSet<PerfilImpresora> PerfilImpresora { get; set; }

    public virtual DbSet<Permiso> Permiso { get; set; }

    public virtual DbSet<PlantillaImpresion> PlantillaImpresion { get; set; }

    public virtual DbSet<Producto> Producto { get; set; }

    public virtual DbSet<Proveedor> Proveedor { get; set; }

    public virtual DbSet<PruebaRestauracion> PruebaRestauracion { get; set; }

    public virtual DbSet<ReembolsoApartado> ReembolsoApartado { get; set; }

    public virtual DbSet<Rol> Rol { get; set; }

    public virtual DbSet<RolClaim> RolClaim { get; set; }

    public virtual DbSet<RolPermiso> RolPermiso { get; set; }

    public virtual DbSet<SesionOperador> SesionOperador { get; set; }

    public virtual DbSet<Terminal> Terminal { get; set; }

    public virtual DbSet<TrabajoImpresion> TrabajoImpresion { get; set; }

    public virtual DbSet<TurnoCaja> TurnoCaja { get; set; }

    public virtual DbSet<UnidadMedida> UnidadMedida { get; set; }

    public virtual DbSet<Usuario> Usuario { get; set; }

    public virtual DbSet<UsuarioClaim> UsuarioClaim { get; set; }

    public virtual DbSet<UsuarioLogin> UsuarioLogin { get; set; }

    public virtual DbSet<UsuarioRol> UsuarioRol { get; set; }

    public virtual DbSet<UsuarioToken> UsuarioToken { get; set; }

    public virtual DbSet<Venta> Venta { get; set; }

    public virtual DbSet<VwNecesidadReposicion> VwNecesidadReposicion { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AjusteCreditoCambio>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_ajuste_credito_cambio");

            entity.ToTable("ajuste_credito_cambio", "ventas");

            entity.HasIndex(e => new { e.CreditoId, e.FechaHora }, "ix_ajuste_credito_credito_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.CambioVentaId, "uq_ajuste_credito_cambio").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CambioVentaId).HasColumnName("cambio_venta_id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.CreditoId).HasColumnName("credito_id");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MontoAjustadoAnterior)
                .HasPrecision(18)
                .HasColumnName("monto_ajustado_anterior");
            entity.Property(e => e.MontoAjustadoPosterior)
                .HasPrecision(18)
                .HasColumnName("monto_ajustado_posterior");
            entity.Property(e => e.SaldoAnterior)
                .HasPrecision(18)
                .HasColumnName("saldo_anterior");
            entity.Property(e => e.SaldoPosterior)
                .HasPrecision(18)
                .HasColumnName("saldo_posterior");
            entity.Property(e => e.TipoAjuste)
                .HasMaxLength(20)
                .HasColumnName("tipo_ajuste");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Valor)
                .HasPrecision(18)
                .HasColumnName("valor");

            entity.HasOne(d => d.CambioVenta).WithOne(p => p.AjusteCreditoCambio)
                .HasForeignKey<AjusteCreditoCambio>(d => d.CambioVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_ajuste_credito_cambio");

            entity.HasOne(d => d.Credito).WithMany(p => p.AjusteCreditoCambio)
                .HasForeignKey(d => d.CreditoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_ajuste_credito_credito");

            entity.HasOne(d => d.Usuario).WithMany(p => p.AjusteCreditoCambio)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_ajuste_credito_usuario");
        });

        modelBuilder.Entity<AnulacionVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_anulacion_venta");

            entity.ToTable("anulacion_venta", "ventas");

            entity.HasIndex(e => e.UsuarioAutorizadorId, "ix_anulacion_venta_autorizador");

            entity.HasIndex(e => e.UsuarioSolicitanteId, "ix_anulacion_venta_solicitante");

            entity.HasIndex(e => e.AutorizacionOperacionId, "uq_anulacion_venta_autorizacion").IsUnique();

            entity.HasIndex(e => e.VentaId, "uq_anulacion_venta_venta").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MotivoOperacionId).HasColumnName("motivo_operacion_id");
            entity.Property(e => e.Observacion).HasColumnName("observacion");
            entity.Property(e => e.UsuarioAutorizadorId).HasColumnName("usuario_autorizador_id");
            entity.Property(e => e.UsuarioSolicitanteId).HasColumnName("usuario_solicitante_id");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");

            entity.HasOne(d => d.AutorizacionOperacion).WithOne(p => p.AnulacionVenta)
                .HasForeignKey<AnulacionVenta>(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_anulacion_venta_autorizacion");

            entity.HasOne(d => d.MotivoOperacion).WithMany(p => p.AnulacionVenta)
                .HasForeignKey(d => d.MotivoOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_anulacion_venta_motivo");

            entity.HasOne(d => d.UsuarioAutorizador).WithMany(p => p.AnulacionVentaUsuarioAutorizador)
                .HasForeignKey(d => d.UsuarioAutorizadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_anulacion_venta_autorizador");

            entity.HasOne(d => d.UsuarioSolicitante).WithMany(p => p.AnulacionVentaUsuarioSolicitante)
                .HasForeignKey(d => d.UsuarioSolicitanteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_anulacion_venta_solicitante");

            entity.HasOne(d => d.Venta).WithOne(p => p.AnulacionVenta)
                .HasForeignKey<AnulacionVenta>(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_anulacion_venta_venta");
        });

        modelBuilder.Entity<Apartado>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_apartado");

            entity.ToTable("apartado", "ventas");

            entity.HasIndex(e => e.AutorizacionDescuentoId, "ix_apartado_autorizacion_descuento");

            entity.HasIndex(e => new { e.ClienteId, e.FechaHora }, "ix_apartado_cliente_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.Estado, e.FechaLimite }, "ix_apartado_estado_limite");

            entity.HasIndex(e => e.SesionOperadorId, "ix_apartado_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_apartado_turno");

            entity.HasIndex(e => e.UsuarioId, "ix_apartado_usuario");

            entity.HasIndex(e => e.UsuarioAnulacionId, "ix_apartado_usuario_anulacion");

            entity.HasIndex(e => e.UsuarioEntregaId, "ix_apartado_usuario_entrega");

            entity.HasIndex(e => e.AutorizacionOperacionId, "uq_apartado_autorizacion").IsUnique();

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_apartado_instalacion_serie_numero").IsUnique();

            entity.HasIndex(e => e.VentaEntregaId, "uq_apartado_venta_entrega").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AbonoInicial)
                .HasPrecision(18)
                .HasColumnName("abono_inicial");
            entity.Property(e => e.AmbitoDescuento)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NINGUNO'::character varying")
                .HasColumnName("ambito_descuento");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.AutorizacionDescuentoId).HasColumnName("autorizacion_descuento_id");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.BaseGravableTotal)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable_total");
            entity.Property(e => e.ClienteId).HasColumnName("cliente_id");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.DescuentoTotal)
                .HasPrecision(18)
                .HasColumnName("descuento_total");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaEntrega).HasColumnName("fecha_entrega");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.FechaLimite).HasColumnName("fecha_limite");
            entity.Property(e => e.FechaPago).HasColumnName("fecha_pago");
            entity.Property(e => e.ImpuestoIncluidoTotal)
                .HasPrecision(18, 4)
                .HasColumnName("impuesto_incluido_total");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.IvaDiscriminadoCompleto).HasColumnName("iva_discriminado_completo");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.MotivoDescuento).HasColumnName("motivo_descuento");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.PorcentajeMinimoInicial)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_minimo_inicial");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.SaldoPendiente)
                .HasPrecision(18)
                .HasColumnName("saldo_pendiente");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.Total)
                .HasPrecision(18)
                .HasColumnName("total");
            entity.Property(e => e.TotalAbonadoCancelacion)
                .HasPrecision(18)
                .HasColumnName("total_abonado_cancelacion");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioEntregaId).HasColumnName("usuario_entrega_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorDescuentoSolicitado)
                .HasPrecision(18, 4)
                .HasColumnName("valor_descuento_solicitado");
            entity.Property(e => e.ValorDevueltoCancelacion)
                .HasPrecision(18)
                .HasColumnName("valor_devuelto_cancelacion");
            entity.Property(e => e.ValorRetenidoCancelacion)
                .HasPrecision(18)
                .HasColumnName("valor_retenido_cancelacion");
            entity.Property(e => e.VentaEntregaId).HasColumnName("venta_entrega_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.ApartadoAutorizacionAnulacion)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_autorizacion_anulacion");

            entity.HasOne(d => d.AutorizacionDescuento).WithMany(p => p.ApartadoAutorizacionDescuento)
                .HasForeignKey(d => d.AutorizacionDescuentoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_autorizacion_descuento");

            entity.HasOne(d => d.AutorizacionOperacion).WithOne(p => p.ApartadoAutorizacionOperacion)
                .HasForeignKey<Apartado>(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_autorizacion");

            entity.HasOne(d => d.Cliente).WithMany(p => p.Apartado)
                .HasForeignKey(d => d.ClienteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_cliente");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.Apartado)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_instalacion");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.Apartado)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_motivo_anulacion");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.Apartado)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.Apartado)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_turno");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.ApartadoUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_usuario_anulacion");

            entity.HasOne(d => d.UsuarioEntrega).WithMany(p => p.ApartadoUsuarioEntrega)
                .HasForeignKey(d => d.UsuarioEntregaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_usuario_entrega");

            entity.HasOne(d => d.Usuario).WithMany(p => p.ApartadoUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_usuario");

            entity.HasOne(d => d.VentaEntrega).WithOne(p => p.Apartado)
                .HasForeignKey<Apartado>(d => d.VentaEntregaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_apartado_venta_entrega");
        });

        modelBuilder.Entity<AutorizacionOperacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_autorizacion_operacion");

            entity.ToTable("autorizacion_operacion", "seguridad");

            entity.HasIndex(e => new { e.UsuarioAutorizadorId, e.FechaSolicitud }, "ix_autorizacion_autorizador_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.CorrelacionId, "ix_autorizacion_correlacion");

            entity.HasIndex(e => e.CredencialAutorizadorId, "ix_autorizacion_credencial");

            entity.HasIndex(e => new { e.Estado, e.FechaExpiracion }, "ix_autorizacion_estado_expiracion");

            entity.HasIndex(e => e.PermisoId, "ix_autorizacion_permiso");

            entity.HasIndex(e => new { e.UsuarioSolicitanteId, e.FechaSolicitud }, "ix_autorizacion_solicitante_fecha").IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.CredencialAutorizadorId).HasColumnName("credencial_autorizador_id");
            entity.Property(e => e.DescripcionOperacion).HasColumnName("descripcion_operacion");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDIENTE'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaExpiracion).HasColumnName("fecha_expiracion");
            entity.Property(e => e.FechaSolicitud)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_solicitud");
            entity.Property(e => e.FechaUtilizacion).HasColumnName("fecha_utilizacion");
            entity.Property(e => e.MetodoAutenticacion)
                .HasMaxLength(30)
                .HasColumnName("metodo_autenticacion");
            entity.Property(e => e.Motivo).HasColumnName("motivo");
            entity.Property(e => e.PermisoId).HasColumnName("permiso_id");
            entity.Property(e => e.Resultado)
                .HasMaxLength(20)
                .HasColumnName("resultado");
            entity.Property(e => e.TipoOperacion)
                .HasMaxLength(100)
                .HasColumnName("tipo_operacion");
            entity.Property(e => e.UsuarioAutorizadorId).HasColumnName("usuario_autorizador_id");
            entity.Property(e => e.UsuarioSolicitanteId).HasColumnName("usuario_solicitante_id");

            entity.HasOne(d => d.CredencialAutorizador).WithMany(p => p.AutorizacionOperacion)
                .HasForeignKey(d => d.CredencialAutorizadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_autorizacion_credencial");

            entity.HasOne(d => d.Permiso).WithMany(p => p.AutorizacionOperacion)
                .HasForeignKey(d => d.PermisoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_autorizacion_permiso");

            entity.HasOne(d => d.UsuarioAutorizador).WithMany(p => p.AutorizacionOperacionUsuarioAutorizador)
                .HasForeignKey(d => d.UsuarioAutorizadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_autorizacion_autorizador");

            entity.HasOne(d => d.UsuarioSolicitante).WithMany(p => p.AutorizacionOperacionUsuarioSolicitante)
                .HasForeignKey(d => d.UsuarioSolicitanteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_autorizacion_solicitante");
        });

        modelBuilder.Entity<BorradorVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_borrador_venta");

            entity.ToTable("borrador_venta", "ventas", tb => tb.HasComment("Carrito recuperable despues de una interrupcion. No constituye una venta ni afecta caja o inventario."));

            entity.HasIndex(e => e.SesionOperadorId, "ix_borrador_venta_sesion");

            entity.HasIndex(e => new { e.TurnoCajaId, e.FechaModificacion }, "ix_borrador_venta_turno_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.UsuarioId, "ix_borrador_venta_usuario");

            entity.HasIndex(e => e.TerminalId, "uq_borrador_venta_activo_terminal")
                .IsUnique()
                .HasFilter("((estado)::text = ANY ((ARRAY['ACTIVO'::character varying, 'RECUPERADO'::character varying])::text[]))");

            entity.HasIndex(e => e.VentaConfirmadaId, "uq_borrador_venta_confirmada").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AmbitoDescuento)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NINGUNO'::character varying")
                .HasColumnName("ambito_descuento");
            entity.Property(e => e.ClienteId).HasColumnName("cliente_id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaCierre).HasColumnName("fecha_cierre");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.MotivoDescuento).HasColumnName("motivo_descuento");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.TipoVenta)
                .HasMaxLength(20)
                .HasDefaultValueSql("'CONTADO'::character varying")
                .HasColumnName("tipo_venta");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorDescuentoSolicitado)
                .HasPrecision(18, 4)
                .HasColumnName("valor_descuento_solicitado");
            entity.Property(e => e.VentaConfirmadaId).HasColumnName("venta_confirmada_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Cliente).WithMany(p => p.BorradorVenta)
                .HasForeignKey(d => d.ClienteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_cliente");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.BorradorVenta)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_instalacion");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.BorradorVenta)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_sesion");

            entity.HasOne(d => d.Terminal).WithOne(p => p.BorradorVenta)
                .HasForeignKey<BorradorVenta>(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_terminal");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.BorradorVenta)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_turno");

            entity.HasOne(d => d.Usuario).WithMany(p => p.BorradorVenta)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_usuario");

            entity.HasOne(d => d.VentaConfirmada).WithOne(p => p.BorradorVenta)
                .HasForeignKey<BorradorVenta>(d => d.VentaConfirmadaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_borrador_venta_confirmada");
        });

        modelBuilder.Entity<Caja>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_caja");

            entity.ToTable("caja", "caja");

            entity.HasIndex(e => new { e.InstalacionId, e.Codigo }, "uq_caja_instalacion_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.Caja)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_caja_instalacion");
        });

        modelBuilder.Entity<CambioVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_cambio_venta");

            entity.ToTable("cambio_venta", "ventas");

            entity.HasIndex(e => e.CreditoId, "ix_cambio_venta_credito");

            entity.HasIndex(e => e.MotivoAnulacionId, "ix_cambio_venta_motivo_anulacion");

            entity.HasIndex(e => e.SesionOperadorId, "ix_cambio_venta_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_cambio_venta_turno");

            entity.HasIndex(e => e.UsuarioId, "ix_cambio_venta_usuario");

            entity.HasIndex(e => new { e.VentaId, e.FechaHora }, "ix_cambio_venta_venta_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.AutorizacionOperacionId, "uq_cambio_venta_autorizacion").IsUnique();

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_cambio_venta_inst_serie_numero").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.CreditoId).HasColumnName("credito_id");
            entity.Property(e => e.Diferencia)
                .HasPrecision(18)
                .HasColumnName("diferencia");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'CONFIRMADO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Motivo).HasColumnName("motivo");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TotalDevuelto)
                .HasPrecision(18)
                .HasColumnName("total_devuelto");
            entity.Property(e => e.TotalEntregado)
                .HasPrecision(18)
                .HasColumnName("total_entregado");
            entity.Property(e => e.TratamientoDiferencia)
                .HasMaxLength(40)
                .HasColumnName("tratamiento_diferencia");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorAjusteCredito)
                .HasPrecision(18)
                .HasColumnName("valor_ajuste_credito");
            entity.Property(e => e.ValorPagoODevolucion)
                .HasPrecision(18)
                .HasColumnName("valor_pago_o_devolucion");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.CambioVentaAutorizacionAnulacion)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_autorizacion_anulacion");

            entity.HasOne(d => d.AutorizacionOperacion).WithOne(p => p.CambioVentaAutorizacionOperacion)
                .HasForeignKey<CambioVenta>(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_autorizacion");

            entity.HasOne(d => d.Credito).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.CreditoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_credito");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_instalacion");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_motivo_anulacion");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_turno");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.CambioVentaUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.CambioVentaUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_usuario");

            entity.HasOne(d => d.Venta).WithMany(p => p.CambioVenta)
                .HasForeignKey(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cambio_venta_venta");
        });

        modelBuilder.Entity<Categoria>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_categoria");

            entity.ToTable("categoria", "catalogo");

            entity.HasIndex(e => e.UsuarioCreacionId, "ix_categoria_usuario_creacion");

            entity.HasIndex(e => e.UsuarioModificacionId, "ix_categoria_usuario_modificacion");

            entity.HasIndex(e => e.Nombre, "uq_categoria_nombre").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(500)
                .HasColumnName("descripcion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
            entity.Property(e => e.UsuarioModificacionId).HasColumnName("usuario_modificacion_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.UsuarioCreacion).WithMany(p => p.CategoriaUsuarioCreacion)
                .HasForeignKey(d => d.UsuarioCreacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_categoria_usuario_creacion");

            entity.HasOne(d => d.UsuarioModificacion).WithMany(p => p.CategoriaUsuarioModificacion)
                .HasForeignKey(d => d.UsuarioModificacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_categoria_usuario_modificacion");
        });

        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_cliente");

            entity.ToTable("cliente", "catalogo");

            entity.HasIndex(e => e.UsuarioCreacionId, "ix_cliente_usuario_creacion");

            entity.HasIndex(e => e.UsuarioModificacionId, "ix_cliente_usuario_modificacion");

            entity.HasIndex(e => new { e.TipoDocumento, e.NumeroDocumento }, "uq_cliente_documento").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Correo)
                .HasMaxLength(254)
                .HasColumnName("correo");
            entity.Property(e => e.Direccion)
                .HasMaxLength(300)
                .HasColumnName("direccion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.NombreCompleto)
                .HasMaxLength(200)
                .HasColumnName("nombre_completo");
            entity.Property(e => e.NumeroDocumento)
                .HasMaxLength(50)
                .HasColumnName("numero_documento");
            entity.Property(e => e.Observaciones).HasColumnName("observaciones");
            entity.Property(e => e.Telefono)
                .HasMaxLength(30)
                .HasColumnName("telefono");
            entity.Property(e => e.TipoDocumento)
                .HasMaxLength(20)
                .HasColumnName("tipo_documento");
            entity.Property(e => e.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
            entity.Property(e => e.UsuarioModificacionId).HasColumnName("usuario_modificacion_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.UsuarioCreacion).WithMany(p => p.ClienteUsuarioCreacion)
                .HasForeignKey(d => d.UsuarioCreacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cliente_usuario_creacion");

            entity.HasOne(d => d.UsuarioModificacion).WithMany(p => p.ClienteUsuarioModificacion)
                .HasForeignKey(d => d.UsuarioModificacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cliente_usuario_modificacion");
        });

        modelBuilder.Entity<Compra>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_compra");

            entity.ToTable("compra", "compras");

            entity.HasIndex(e => new { e.FechaDocumento, e.Estado }, "ix_compra_fecha_documento_estado").IsDescending(true, false);

            entity.HasIndex(e => e.PedidoCompraId, "ix_compra_pedido");

            entity.HasIndex(e => e.SesionOperadorId, "ix_compra_sesion");

            entity.HasIndex(e => e.UsuarioId, "ix_compra_usuario");

            entity.HasIndex(e => e.UsuarioAnulacionId, "ix_compra_usuario_anulacion");

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_compra_instalacion_serie_numero").IsUnique();

            entity.HasIndex(e => new { e.ProveedorId, e.NumeroDocumentoProveedor }, "uq_compra_proveedor_documento").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'BORRADOR'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaDocumento).HasColumnName("fecha_documento");
            entity.Property(e => e.FechaHoraConfirmacion).HasColumnName("fecha_hora_confirmacion");
            entity.Property(e => e.FechaHoraRegistro)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora_registro");
            entity.Property(e => e.ImpuestoTotal)
                .HasPrecision(18, 2)
                .HasColumnName("impuesto_total");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.ModoIva)
                .HasMaxLength(30)
                .HasDefaultValueSql("'NO_DISCRIMINADO'::character varying")
                .HasColumnName("modo_iva");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.NumeroDocumentoProveedor)
                .HasMaxLength(100)
                .HasComment("Factura o referencia del proveedor opcional; si se informa, es unica para ese proveedor.")
                .HasColumnName("numero_documento_proveedor");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.PedidoCompraId).HasColumnName("pedido_compra_id");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.ProveedorId).HasColumnName("proveedor_id");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18, 2)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.Total)
                .HasPrecision(18, 2)
                .HasColumnName("total");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.Compra)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_autorizacion_anulacion");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.Compra)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_instalacion");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.Compra)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_motivo_anulacion");

            entity.HasOne(d => d.PedidoCompra).WithMany(p => p.Compra)
                .HasForeignKey(d => d.PedidoCompraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_pedido");

            entity.HasOne(d => d.Proveedor).WithMany(p => p.Compra)
                .HasForeignKey(d => d.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_proveedor");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.Compra)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_sesion");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.CompraUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.CompraUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_compra_usuario");
        });

        modelBuilder.Entity<ConsecutivoDocumento>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_consecutivo_documento");

            entity.ToTable("consecutivo_documento", "configuracion");

            entity.HasIndex(e => new { e.InstalacionId, e.TipoDocumento, e.Serie }, "uq_consecutivo_inst_tipo_serie").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.TipoDocumento)
                .HasMaxLength(30)
                .HasColumnName("tipo_documento");
            entity.Property(e => e.UltimoNumero).HasColumnName("ultimo_numero");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.ConsecutivoDocumento)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_consecutivo_instalacion");
        });

        modelBuilder.Entity<CopiaRespaldo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_copia_respaldo");

            entity.ToTable("copia_respaldo", "configuracion");

            entity.HasIndex(e => e.EjecucionRespaldoId, "ix_copia_respaldo_ejecucion");

            entity.HasIndex(e => new { e.Estado, e.FechaUltimoIntento }, "ix_copia_respaldo_pendiente").HasFilter("((estado)::text = ANY ((ARRAY['PENDIENTE'::character varying, 'FALLIDA'::character varying])::text[]))");

            entity.HasIndex(e => new { e.EjecucionRespaldoId, e.DestinoRespaldoId }, "uq_copia_respaldo_ejecucion_destino").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Cifrado).HasColumnName("cifrado");
            entity.Property(e => e.DestinoRespaldoId).HasColumnName("destino_respaldo_id");
            entity.Property(e => e.EjecucionRespaldoId).HasColumnName("ejecucion_respaldo_id");
            entity.Property(e => e.ErrorSanitizado).HasColumnName("error_sanitizado");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDIENTE'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaCompletada).HasColumnName("fecha_completada");
            entity.Property(e => e.FechaUltimoIntento).HasColumnName("fecha_ultimo_intento");
            entity.Property(e => e.HashVerificado).HasColumnName("hash_verificado");
            entity.Property(e => e.Intentos).HasColumnName("intentos");
            entity.Property(e => e.UbicacionFinal).HasColumnName("ubicacion_final");

            entity.HasOne(d => d.DestinoRespaldo).WithMany(p => p.CopiaRespaldo)
                .HasForeignKey(d => d.DestinoRespaldoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_copia_respaldo_destino");

            entity.HasOne(d => d.EjecucionRespaldo).WithMany(p => p.CopiaRespaldo)
                .HasForeignKey(d => d.EjecucionRespaldoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_copia_respaldo_ejecucion");
        });

        modelBuilder.Entity<CredencialUsuario>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_credencial_usuario");

            entity.ToTable("credencial_usuario", "seguridad", tb => tb.HasComment("Credencial rapida Code 128. Solo conserva el hash y un fragmento no secreto; el token se imprime directamente y nunca entra en snapshots ni colas persistentes."));

            entity.HasIndex(e => new { e.UsuarioId, e.FechaEmision }, "ix_credencial_usuario_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.UsuarioId, "uq_credencial_usuario_activa")
                .IsUnique()
                .HasFilter("((estado)::text = 'ACTIVA'::text)");

            entity.HasIndex(e => e.TokenHash, "uq_credencial_usuario_hash").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.EmitidaPorId).HasColumnName("emitida_por_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVA'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaEmision)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_emision");
            entity.Property(e => e.FechaRevocacion).HasColumnName("fecha_revocacion");
            entity.Property(e => e.FechaVencimiento).HasColumnName("fecha_vencimiento");
            entity.Property(e => e.FormatoCodigo)
                .HasMaxLength(20)
                .HasDefaultValueSql("'CODE128'::character varying")
                .HasColumnName("formato_codigo");
            entity.Property(e => e.FragmentoVisible)
                .HasMaxLength(12)
                .HasColumnName("fragmento_visible");
            entity.Property(e => e.MotivoRevocacion).HasColumnName("motivo_revocacion");
            entity.Property(e => e.RevocadaPorId).HasColumnName("revocada_por_id");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(128)
                .HasColumnName("token_hash");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.EmitidaPor).WithMany(p => p.CredencialUsuarioEmitidaPor)
                .HasForeignKey(d => d.EmitidaPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_credencial_usuario_emisor");

            entity.HasOne(d => d.RevocadaPor).WithMany(p => p.CredencialUsuarioRevocadaPor)
                .HasForeignKey(d => d.RevocadaPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_credencial_usuario_revocador");

            entity.HasOne(d => d.Usuario).WithOne(p => p.CredencialUsuarioUsuario)
                .HasForeignKey<CredencialUsuario>(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_credencial_usuario_usuario");
        });

        modelBuilder.Entity<Credito>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_credito");

            entity.ToTable("credito", "ventas");

            entity.HasIndex(e => new { e.Estado, e.FechaVencimiento }, "ix_credito_estado_vencimiento");

            entity.HasIndex(e => e.AutorizacionOperacionId, "uq_credito_autorizacion").IsUnique();

            entity.HasIndex(e => e.VentaId, "uq_credito_venta").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDIENTE'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaOtorgamiento).HasColumnName("fecha_otorgamiento");
            entity.Property(e => e.FechaPago).HasColumnName("fecha_pago");
            entity.Property(e => e.FechaVencimiento).HasColumnName("fecha_vencimiento");
            entity.Property(e => e.MontoAjustado)
                .HasPrecision(18)
                .HasColumnName("monto_ajustado");
            entity.Property(e => e.MontoOriginal)
                .HasPrecision(18)
                .HasColumnName("monto_original");
            entity.Property(e => e.SaldoPendiente)
                .HasPrecision(18)
                .HasColumnName("saldo_pendiente");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionOperacion).WithOne(p => p.Credito)
                .HasForeignKey<Credito>(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_credito_autorizacion");

            entity.HasOne(d => d.Venta).WithOne(p => p.Credito)
                .HasForeignKey<Credito>(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_credito_venta");
        });

        modelBuilder.Entity<DestinoRespaldo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_destino_respaldo");

            entity.ToTable("destino_respaldo", "configuracion", tb => tb.HasComment("Configura copia local fisicamente separada y copia externa cifrada con retencion diaria, semanal y mensual."));

            entity.HasIndex(e => new { e.InstalacionId, e.TipoDestino, e.Activo }, "ix_destino_respaldo_instalacion_tipo");

            entity.HasIndex(e => new { e.InstalacionId, e.Nombre }, "uq_destino_respaldo_nombre").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.ConservarPreActualizacion)
                .HasDefaultValue(true)
                .HasColumnName("conservar_pre_actualizacion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Nombre)
                .HasMaxLength(150)
                .HasColumnName("nombre");
            entity.Property(e => e.ReferenciaSecreto)
                .HasMaxLength(200)
                .HasColumnName("referencia_secreto");
            entity.Property(e => e.RequiereCifrado).HasColumnName("requiere_cifrado");
            entity.Property(e => e.RetencionDiaria)
                .HasDefaultValue(7)
                .HasColumnName("retencion_diaria");
            entity.Property(e => e.RetencionMensual)
                .HasDefaultValue(12)
                .HasColumnName("retencion_mensual");
            entity.Property(e => e.RetencionSemanal)
                .HasDefaultValue(4)
                .HasColumnName("retencion_semanal");
            entity.Property(e => e.TipoDestino)
                .HasMaxLength(30)
                .HasColumnName("tipo_destino");
            entity.Property(e => e.UbicacionBase).HasColumnName("ubicacion_base");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.DestinoRespaldo)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_destino_respaldo_instalacion");
        });

        modelBuilder.Entity<DetalleApartado>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_apartado");

            entity.ToTable("detalle_apartado", "ventas");

            entity.HasIndex(e => e.ApartadoId, "ix_detalle_apartado_apartado");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_apartado_producto");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ApartadoId).HasColumnName("apartado_id");
            entity.Property(e => e.BaseGravable)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.CodigoProducto)
                .HasMaxLength(100)
                .HasColumnName("codigo_producto");
            entity.Property(e => e.CostoUnitario)
                .HasPrecision(18, 4)
                .HasColumnName("costo_unitario");
            entity.Property(e => e.NombreProducto)
                .HasMaxLength(200)
                .HasColumnName("nombre_producto");
            entity.Property(e => e.OrigenDescuento)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NINGUNO'::character varying")
                .HasColumnName("origen_descuento");
            entity.Property(e => e.PorcentajeDescuento)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_descuento");
            entity.Property(e => e.PorcentajeIva)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_iva");
            entity.Property(e => e.PrecioReserva)
                .HasPrecision(18)
                .HasColumnName("precio_reserva");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.SubtotalNeto)
                .HasPrecision(18)
                .HasColumnName("subtotal_neto");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.UnidadMedida)
                .HasMaxLength(30)
                .HasColumnName("unidad_medida");
            entity.Property(e => e.ValorDescuento)
                .HasPrecision(18)
                .HasColumnName("valor_descuento");
            entity.Property(e => e.ValorIva)
                .HasPrecision(18, 4)
                .HasColumnName("valor_iva");

            entity.HasOne(d => d.Apartado).WithMany(p => p.DetalleApartado)
                .HasForeignKey(d => d.ApartadoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_apartado_apartado");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetalleApartado)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_apartado_producto");
        });

        modelBuilder.Entity<DetalleBorradorVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_borrador_venta");

            entity.ToTable("detalle_borrador_venta", "ventas");

            entity.HasIndex(e => e.BorradorVentaId, "ix_detalle_borrador_borrador");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_borrador_producto");

            entity.HasIndex(e => new { e.BorradorVentaId, e.Orden }, "uq_detalle_borrador_orden").IsUnique();

            entity.HasIndex(e => new { e.BorradorVentaId, e.ProductoId }, "uq_detalle_borrador_producto").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.BorradorVentaId).HasColumnName("borrador_venta_id");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.FechaModificacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_modificacion");
            entity.Property(e => e.Orden).HasColumnName("orden");
            entity.Property(e => e.PorcentajeDescuento)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_descuento");
            entity.Property(e => e.PrecioUnitario)
                .HasPrecision(18)
                .HasColumnName("precio_unitario");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.TipoPrecio)
                .HasMaxLength(20)
                .HasColumnName("tipo_precio");
            entity.Property(e => e.ValorDescuento)
                .HasPrecision(18)
                .HasColumnName("valor_descuento");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.BorradorVenta).WithMany(p => p.DetalleBorradorVenta)
                .HasForeignKey(d => d.BorradorVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_borrador_borrador");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetalleBorradorVenta)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_borrador_producto");
        });

        modelBuilder.Entity<DetalleCambioVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_cambio_venta");

            entity.ToTable("detalle_cambio_venta", "ventas");

            entity.HasIndex(e => e.CambioVentaId, "ix_detalle_cambio_cambio");

            entity.HasIndex(e => e.DetalleVentaOrigenId, "ix_detalle_cambio_origen");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_cambio_producto");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.BaseGravable)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable");
            entity.Property(e => e.CambioVentaId).HasColumnName("cambio_venta_id");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.CodigoProducto)
                .HasMaxLength(100)
                .HasColumnName("codigo_producto");
            entity.Property(e => e.DetalleVentaOrigenId).HasColumnName("detalle_venta_origen_id");
            entity.Property(e => e.NombreProducto)
                .HasMaxLength(200)
                .HasColumnName("nombre_producto");
            entity.Property(e => e.PorcentajeIva)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_iva");
            entity.Property(e => e.PrecioUnitario)
                .HasPrecision(18)
                .HasColumnName("precio_unitario");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.SubtotalNeto)
                .HasPrecision(18)
                .HasColumnName("subtotal_neto");
            entity.Property(e => e.TipoDetalle)
                .HasMaxLength(20)
                .HasColumnName("tipo_detalle");
            entity.Property(e => e.UnidadMedida)
                .HasMaxLength(30)
                .HasColumnName("unidad_medida");
            entity.Property(e => e.ValorDescuento)
                .HasPrecision(18)
                .HasColumnName("valor_descuento");
            entity.Property(e => e.ValorIva)
                .HasPrecision(18, 4)
                .HasColumnName("valor_iva");

            entity.HasOne(d => d.CambioVenta).WithMany(p => p.DetalleCambioVenta)
                .HasForeignKey(d => d.CambioVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_cambio_cambio");

            entity.HasOne(d => d.DetalleVentaOrigen).WithMany(p => p.DetalleCambioVenta)
                .HasForeignKey(d => d.DetalleVentaOrigenId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_cambio_detalle_origen");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetalleCambioVenta)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_cambio_producto");
        });

        modelBuilder.Entity<DetalleCompra>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_compra");

            entity.ToTable("detalle_compra", "compras");

            entity.HasIndex(e => e.CompraId, "ix_detalle_compra_compra");

            entity.HasIndex(e => e.DetallePedidoCompraId, "ix_detalle_compra_detalle_pedido");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_compra_producto");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionExcepcionId).HasColumnName("autorizacion_excepcion_id");
            entity.Property(e => e.BaseGravable)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.CompraId).HasColumnName("compra_id");
            entity.Property(e => e.CostoPromedioAnterior)
                .HasPrecision(18, 4)
                .HasColumnName("costo_promedio_anterior");
            entity.Property(e => e.CostoPromedioResultante)
                .HasPrecision(18, 4)
                .HasColumnName("costo_promedio_resultante");
            entity.Property(e => e.CostoUnitarioDocumento)
                .HasPrecision(18, 4)
                .HasColumnName("costo_unitario_documento");
            entity.Property(e => e.DetallePedidoCompraId).HasColumnName("detalle_pedido_compra_id");
            entity.Property(e => e.EsSustituto).HasColumnName("es_sustituto");
            entity.Property(e => e.ModoIva)
                .HasMaxLength(30)
                .HasColumnName("modo_iva");
            entity.Property(e => e.PorcentajeIva)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_iva");
            entity.Property(e => e.PrecioVentaAnterior)
                .HasPrecision(18)
                .HasColumnName("precio_venta_anterior");
            entity.Property(e => e.PrecioVentaNuevo)
                .HasPrecision(18)
                .HasColumnName("precio_venta_nuevo");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18, 4)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.TotalLinea)
                .HasPrecision(18, 4)
                .HasColumnName("total_linea");
            entity.Property(e => e.ValorIva)
                .HasPrecision(18, 4)
                .HasColumnName("valor_iva");

            entity.HasOne(d => d.AutorizacionExcepcion).WithMany(p => p.DetalleCompra)
                .HasForeignKey(d => d.AutorizacionExcepcionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_compra_autorizacion_excepcion");

            entity.HasOne(d => d.Compra).WithMany(p => p.DetalleCompra)
                .HasForeignKey(d => d.CompraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_compra_compra");

            entity.HasOne(d => d.DetallePedidoCompra).WithMany(p => p.DetalleCompra)
                .HasForeignKey(d => d.DetallePedidoCompraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_compra_detalle_pedido");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetalleCompra)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_compra_producto");
        });

        modelBuilder.Entity<DetallePedidoCompra>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_pedido_compra");

            entity.ToTable("detalle_pedido_compra", "compras");

            entity.HasIndex(e => e.PedidoCompraId, "ix_detalle_pedido_pedido");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_pedido_producto");

            entity.HasIndex(e => new { e.PedidoCompraId, e.ProductoId }, "uq_detalle_pedido_producto").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionExcesoId).HasColumnName("autorizacion_exceso_id");
            entity.Property(e => e.CantidadExcedente).HasColumnName("cantidad_excedente");
            entity.Property(e => e.CantidadRecibida)
                .HasComment("Cantidad recibida historicamente. Una anulacion posterior de la compra revierte inventario, pero no reescribe el pedido enviado.")
                .HasColumnName("cantidad_recibida");
            entity.Property(e => e.CantidadSolicitada).HasColumnName("cantidad_solicitada");
            entity.Property(e => e.CantidadSugerida).HasColumnName("cantidad_sugerida");
            entity.Property(e => e.CodigoProducto)
                .HasMaxLength(100)
                .HasColumnName("codigo_producto");
            entity.Property(e => e.CostoEstimado)
                .HasPrecision(18, 4)
                .HasColumnName("costo_estimado");
            entity.Property(e => e.NombreProducto)
                .HasMaxLength(200)
                .HasColumnName("nombre_producto");
            entity.Property(e => e.Observacion).HasColumnName("observacion");
            entity.Property(e => e.OrigenNecesidad)
                .HasMaxLength(20)
                .HasColumnName("origen_necesidad");
            entity.Property(e => e.PedidoCompraId).HasColumnName("pedido_compra_id");
            entity.Property(e => e.PendienteOtrosPedidos).HasColumnName("pendiente_otros_pedidos");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.StockDisponibleOrigen).HasColumnName("stock_disponible_origen");
            entity.Property(e => e.StockMinimoOrigen).HasColumnName("stock_minimo_origen");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionExceso).WithMany(p => p.DetallePedidoCompra)
                .HasForeignKey(d => d.AutorizacionExcesoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_pedido_autorizacion_exceso");

            entity.HasOne(d => d.PedidoCompra).WithMany(p => p.DetallePedidoCompra)
                .HasForeignKey(d => d.PedidoCompraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_pedido_pedido");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetallePedidoCompra)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_pedido_producto");
        });

        modelBuilder.Entity<DetalleVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_detalle_venta");

            entity.ToTable("detalle_venta", "ventas");

            entity.HasIndex(e => e.ProductoId, "ix_detalle_venta_producto");

            entity.HasIndex(e => e.VentaId, "ix_detalle_venta_venta");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.BaseGravable)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable");
            entity.Property(e => e.Cantidad).HasColumnName("cantidad");
            entity.Property(e => e.CodigoProducto)
                .HasMaxLength(100)
                .HasColumnName("codigo_producto");
            entity.Property(e => e.CostoUnitario)
                .HasPrecision(18, 4)
                .HasColumnName("costo_unitario");
            entity.Property(e => e.NombreProducto)
                .HasMaxLength(200)
                .HasColumnName("nombre_producto");
            entity.Property(e => e.OrigenDescuento)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NINGUNO'::character varying")
                .HasColumnName("origen_descuento");
            entity.Property(e => e.PorcentajeDescuento)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_descuento");
            entity.Property(e => e.PorcentajeIva)
                .HasPrecision(5, 2)
                .HasColumnName("porcentaje_iva");
            entity.Property(e => e.PrecioUnitario)
                .HasPrecision(18)
                .HasColumnName("precio_unitario");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.SubtotalNeto)
                .HasPrecision(18)
                .HasColumnName("subtotal_neto");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.TipoPrecio)
                .HasMaxLength(20)
                .HasColumnName("tipo_precio");
            entity.Property(e => e.UnidadMedida)
                .HasMaxLength(30)
                .HasColumnName("unidad_medida");
            entity.Property(e => e.ValorDescuento)
                .HasPrecision(18)
                .HasColumnName("valor_descuento");
            entity.Property(e => e.ValorIva)
                .HasPrecision(18, 4)
                .HasColumnName("valor_iva");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");

            entity.HasOne(d => d.Producto).WithMany(p => p.DetalleVenta)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_venta_producto");

            entity.HasOne(d => d.Venta).WithMany(p => p.DetalleVenta)
                .HasForeignKey(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_detalle_venta_venta");
        });

        modelBuilder.Entity<DocumentoEmitido>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_documento_emitido");

            entity.ToTable("documento_emitido", "configuracion", tb => tb.HasComment("Snapshot inmutable de datos y plantilla para que una reimpresion no cambie el documento historico."));

            entity.HasIndex(e => new { e.EntidadTipo, e.EntidadId, e.FechaEmision }, "ix_documento_emitido_entidad").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.InstalacionId, e.FechaEmision }, "ix_documento_emitido_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.InstalacionId, e.TipoDocumento, e.EntidadId, e.VersionDocumento }, "uq_documento_emitido_origen").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.DatosSnapshot)
                .HasColumnType("jsonb")
                .HasColumnName("datos_snapshot");
            entity.Property(e => e.EmitidoPorId).HasColumnName("emitido_por_id");
            entity.Property(e => e.EntidadId).HasColumnName("entidad_id");
            entity.Property(e => e.EntidadTipo)
                .HasMaxLength(100)
                .HasColumnName("entidad_tipo");
            entity.Property(e => e.FechaEmision)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_emision");
            entity.Property(e => e.HashSnapshot)
                .HasMaxLength(128)
                .HasColumnName("hash_snapshot");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.NumeroVisible)
                .HasMaxLength(100)
                .HasColumnName("numero_visible");
            entity.Property(e => e.PlantillaSnapshot)
                .HasColumnType("jsonb")
                .HasColumnName("plantilla_snapshot");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TipoDocumento)
                .HasMaxLength(40)
                .HasColumnName("tipo_documento");
            entity.Property(e => e.VersionDocumento)
                .HasDefaultValue(1)
                .HasColumnName("version_documento");

            entity.HasOne(d => d.EmitidoPor).WithMany(p => p.DocumentoEmitido)
                .HasForeignKey(d => d.EmitidoPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_documento_emitido_usuario");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.DocumentoEmitido)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_documento_emitido_instalacion");

            entity.HasOne(d => d.Terminal).WithMany(p => p.DocumentoEmitido)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_documento_emitido_terminal");
        });

        modelBuilder.Entity<EjecucionRespaldo>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_ejecucion_respaldo");

            entity.ToTable("ejecucion_respaldo", "configuracion");

            entity.HasIndex(e => new { e.InstalacionId, e.FechaInicio }, "ix_respaldo_instalacion_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.Resultado, e.FechaInicio }, "ix_respaldo_resultado_fecha").IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Disparador)
                .HasMaxLength(30)
                .HasColumnName("disparador");
            entity.Property(e => e.ErrorSanitizado).HasColumnName("error_sanitizado");
            entity.Property(e => e.Estrategia)
                .HasMaxLength(30)
                .HasColumnName("estrategia");
            entity.Property(e => e.FechaFin).HasColumnName("fecha_fin");
            entity.Property(e => e.FechaInicio)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_inicio");
            entity.Property(e => e.FinWal)
                .HasMaxLength(64)
                .HasColumnName("fin_wal");
            entity.Property(e => e.HashIntegridad)
                .HasMaxLength(128)
                .HasColumnName("hash_integridad");
            entity.Property(e => e.InicioWal)
                .HasMaxLength(64)
                .HasColumnName("inicio_wal");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Resultado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'EN_PROCESO'::character varying")
                .HasColumnName("resultado");
            entity.Property(e => e.RutaArtefactoLocal).HasColumnName("ruta_artefacto_local");
            entity.Property(e => e.TamanoBytes).HasColumnName("tamano_bytes");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.EjecucionRespaldo)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_respaldo_instalacion");
        });

        modelBuilder.Entity<Establecimiento>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_establecimiento");

            entity.ToTable("establecimiento", "configuracion");

            entity.HasIndex(e => e.Identificacion, "uq_establecimiento_identificacion").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.DiasLimiteApartado)
                .HasComment("Opcional. Si es NULL, el usuario debe escoger la fecha limite de cada apartado.")
                .HasColumnName("dias_limite_apartado");
            entity.Property(e => e.DiasLimiteCambio)
                .HasDefaultValue(15)
                .HasColumnName("dias_limite_cambio");
            entity.Property(e => e.DiasPruebaRestauracion)
                .HasDefaultValue(90)
                .HasComment("Frecuencia maxima inicial de 90 dias, ademas de pruebas posteriores a cambios mayores.")
                .HasColumnName("dias_prueba_restauracion");
            entity.Property(e => e.Direccion)
                .HasMaxLength(300)
                .HasColumnName("direccion");
            entity.Property(e => e.EncabezadoComprobante).HasColumnName("encabezado_comprobante");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.Identificacion)
                .HasMaxLength(30)
                .HasColumnName("identificacion");
            entity.Property(e => e.IvaPredeterminado)
                .HasPrecision(5, 2)
                .HasComment("Opcional. NULL significa que no existe un IVA sugerido al crear productos.")
                .HasColumnName("iva_predeterminado");
            entity.Property(e => e.ModoTurnoPredeterminado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'INDIVIDUAL'::character varying")
                .HasComment("Modo aplicado al siguiente turno. No puede cambiarse mientras exista operacion activa.")
                .HasColumnName("modo_turno_predeterminado");
            entity.Property(e => e.NombreComercial)
                .HasMaxLength(200)
                .HasColumnName("nombre_comercial");
            entity.Property(e => e.PieComprobante).HasColumnName("pie_comprobante");
            entity.Property(e => e.PorcentajeMinimoApartado)
                .HasPrecision(5, 2)
                .HasDefaultValue(20m)
                .HasComment("Porcentaje minimo configurable; el valor inicial aprobado es 20 por ciento.")
                .HasColumnName("porcentaje_minimo_apartado");
            entity.Property(e => e.RpoMinutos)
                .HasDefaultValue(30)
                .HasColumnName("rpo_minutos");
            entity.Property(e => e.RtoMinutos)
                .HasDefaultValue(120)
                .HasColumnName("rto_minutos");
            entity.Property(e => e.Telefono)
                .HasMaxLength(30)
                .HasColumnName("telefono");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");
            entity.Property(e => e.ZonaHoraria)
                .HasMaxLength(100)
                .HasDefaultValueSql("'America/Bogota'::character varying")
                .HasColumnName("zona_horaria");
        });

        modelBuilder.Entity<EventoAuditoria>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_evento_auditoria");

            entity.ToTable("evento_auditoria", "auditoria");

            entity.HasIndex(e => e.UsuarioAutorizadorId, "ix_evento_auditoria_autorizador");

            entity.HasIndex(e => e.CorrelacionId, "ix_evento_auditoria_correlacion");

            entity.HasIndex(e => new { e.Entidad, e.EntidadId, e.FechaHora }, "ix_evento_auditoria_entidad").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.InstalacionId, e.FechaHora }, "ix_evento_auditoria_instalacion_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.SesionOperadorId, "ix_evento_auditoria_sesion");

            entity.HasIndex(e => e.TerminalId, "ix_evento_auditoria_terminal");

            entity.HasIndex(e => new { e.UsuarioId, e.FechaHora }, "ix_evento_auditoria_usuario_fecha").IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Accion)
                .HasMaxLength(100)
                .HasColumnName("accion");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.DatosAnteriores)
                .HasColumnType("jsonb")
                .HasColumnName("datos_anteriores");
            entity.Property(e => e.DatosNuevos)
                .HasColumnType("jsonb")
                .HasColumnName("datos_nuevos");
            entity.Property(e => e.Entidad)
                .HasMaxLength(100)
                .HasColumnName("entidad");
            entity.Property(e => e.EntidadId).HasColumnName("entidad_id");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Motivo).HasColumnName("motivo");
            entity.Property(e => e.Resultado)
                .HasMaxLength(20)
                .HasColumnName("resultado");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.UsuarioAutorizadorId).HasColumnName("usuario_autorizador_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.EventoAuditoria)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evento_auditoria_instalacion");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.EventoAuditoria)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evento_auditoria_sesion");

            entity.HasOne(d => d.Terminal).WithMany(p => p.EventoAuditoria)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evento_auditoria_terminal");

            entity.HasOne(d => d.UsuarioAutorizador).WithMany(p => p.EventoAuditoriaUsuarioAutorizador)
                .HasForeignKey(d => d.UsuarioAutorizadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evento_auditoria_autorizador");

            entity.HasOne(d => d.Usuario).WithMany(p => p.EventoAuditoriaUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evento_auditoria_usuario");
        });

        modelBuilder.Entity<Instalacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_instalacion");

            entity.ToTable("instalacion", "configuracion");

            entity.HasIndex(e => e.EstablecimientoId, "ix_instalacion_establecimiento");

            entity.HasIndex(e => e.Codigo, "uq_instalacion_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.EstablecimientoId).HasColumnName("establecimiento_id");
            entity.Property(e => e.FechaInstalacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_instalacion");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");
            entity.Property(e => e.VersionAplicacion)
                .HasMaxLength(50)
                .HasColumnName("version_aplicacion");
            entity.Property(e => e.VersionEsquema)
                .HasMaxLength(100)
                .HasColumnName("version_esquema");

            entity.HasOne(d => d.Establecimiento).WithMany(p => p.Instalacion)
                .HasForeignKey(d => d.EstablecimientoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_instalacion_establecimiento");
        });

        modelBuilder.Entity<LimiteOperacionRol>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_limite_operacion_rol");

            entity.ToTable("limite_operacion_rol", "seguridad", tb => tb.HasComment("Limites configurables por rol. Para DESCUENTO_PORCENTAJE los valores aprobados son 80, 20 y 5 para Administrador, Supervisor y Cajero."));

            entity.HasIndex(e => new { e.RolId, e.Activo }, "ix_limite_operacion_rol_activo");

            entity.HasIndex(e => new { e.RolId, e.CodigoOperacion }, "uq_limite_operacion_rol").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.CodigoOperacion)
                .HasMaxLength(100)
                .HasColumnName("codigo_operacion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.RolId).HasColumnName("rol_id");
            entity.Property(e => e.TipoLimite)
                .HasMaxLength(20)
                .HasColumnName("tipo_limite");
            entity.Property(e => e.ValorMaximo)
                .HasPrecision(18, 4)
                .HasColumnName("valor_maximo");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Rol).WithMany(p => p.LimiteOperacionRol)
                .HasForeignKey(d => d.RolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_limite_operacion_rol_rol");
        });

        modelBuilder.Entity<MetodoPago>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_metodo_pago");

            entity.ToTable("metodo_pago", "catalogo");

            entity.HasIndex(e => e.Codigo, "uq_metodo_pago_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.AfectaEfectivo).HasColumnName("afecta_efectivo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.OrdenVisual).HasColumnName("orden_visual");
            entity.Property(e => e.RequiereReferencia).HasColumnName("requiere_referencia");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");
        });

        modelBuilder.Entity<MotivoOperacion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_motivo_operacion");

            entity.ToTable("motivo_operacion", "configuracion");

            entity.HasIndex(e => new { e.EstablecimientoId, e.TipoOperacion, e.Activo, e.OrdenVisual }, "ix_motivo_operacion_tipo_activo");

            entity.HasIndex(e => new { e.EstablecimientoId, e.TipoOperacion, e.Codigo }, "uq_motivo_operacion_tipo_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(500)
                .HasColumnName("descripcion");
            entity.Property(e => e.EstablecimientoId).HasColumnName("establecimiento_id");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(150)
                .HasColumnName("nombre");
            entity.Property(e => e.OrdenVisual).HasColumnName("orden_visual");
            entity.Property(e => e.TipoOperacion)
                .HasMaxLength(40)
                .HasColumnName("tipo_operacion");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Establecimiento).WithMany(p => p.MotivoOperacion)
                .HasForeignKey(d => d.EstablecimientoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_motivo_operacion_establecimiento");
        });

        modelBuilder.Entity<MovimientoCaja>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_movimiento_caja");

            entity.ToTable("movimiento_caja", "caja");

            entity.HasIndex(e => e.CorrelacionId, "ix_movimiento_caja_correlacion");

            entity.HasIndex(e => e.MovimientoRevertidoId, "ix_movimiento_caja_revertido");

            entity.HasIndex(e => e.SesionOperadorId, "ix_movimiento_caja_sesion");

            entity.HasIndex(e => new { e.TurnoCajaId, e.FechaHora }, "ix_movimiento_caja_turno_fecha");

            entity.HasIndex(e => e.UsuarioId, "ix_movimiento_caja_usuario");

            entity.HasIndex(e => e.MovimientoRevertidoId, "uq_movimiento_caja_una_reversion")
                .IsUnique()
                .HasFilter("(movimiento_revertido_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CategoriaMovimiento)
                .HasMaxLength(50)
                .HasColumnName("categoria_movimiento");
            entity.Property(e => e.Concepto).HasColumnName("concepto");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MovimientoRevertidoId).HasColumnName("movimiento_revertido_id");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.Tipo)
                .HasMaxLength(20)
                .HasColumnName("tipo");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Valor)
                .HasPrecision(18)
                .HasColumnName("valor");

            entity.HasOne(d => d.MovimientoRevertido).WithOne(p => p.InverseMovimientoRevertido)
                .HasForeignKey<MovimientoCaja>(d => d.MovimientoRevertidoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_caja_revertido");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.MovimientoCaja)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_caja_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.MovimientoCaja)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_caja_turno");

            entity.HasOne(d => d.Usuario).WithMany(p => p.MovimientoCaja)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_caja_usuario");
        });

        modelBuilder.Entity<MovimientoInventario>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_movimiento_inventario");

            entity.ToTable("movimiento_inventario", "compras");

            entity.HasIndex(e => e.AnulacionVentaId, "ix_mov_inventario_anulacion");

            entity.HasIndex(e => e.ApartadoId, "ix_mov_inventario_apartado");

            entity.HasIndex(e => e.AutorizacionOperacionId, "ix_mov_inventario_autorizacion");

            entity.HasIndex(e => e.CambioVentaId, "ix_mov_inventario_cambio");

            entity.HasIndex(e => e.CompraId, "ix_mov_inventario_compra");

            entity.HasIndex(e => e.CorrelacionId, "ix_mov_inventario_correlacion");

            entity.HasIndex(e => new { e.ProductoId, e.FechaHora }, "ix_mov_inventario_producto_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.MovimientoRevertidoId, "ix_mov_inventario_revertido");

            entity.HasIndex(e => e.SesionOperadorId, "ix_mov_inventario_sesion");

            entity.HasIndex(e => e.UsuarioId, "ix_mov_inventario_usuario");

            entity.HasIndex(e => e.VentaId, "ix_mov_inventario_venta");

            entity.HasIndex(e => e.MovimientoRevertidoId, "uq_mov_inventario_una_reversion")
                .IsUnique()
                .HasFilter("(movimiento_revertido_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AnulacionVentaId).HasColumnName("anulacion_venta_id");
            entity.Property(e => e.ApartadoId).HasColumnName("apartado_id");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.CambioVentaId).HasColumnName("cambio_venta_id");
            entity.Property(e => e.CantidadReservada).HasColumnName("cantidad_reservada");
            entity.Property(e => e.CantidadStock).HasColumnName("cantidad_stock");
            entity.Property(e => e.CompraId).HasColumnName("compra_id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.CostoPromedioAnterior)
                .HasPrecision(18, 4)
                .HasColumnName("costo_promedio_anterior");
            entity.Property(e => e.CostoPromedioPosterior)
                .HasPrecision(18, 4)
                .HasColumnName("costo_promedio_posterior");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.Motivo).HasColumnName("motivo");
            entity.Property(e => e.MotivoOperacionId).HasColumnName("motivo_operacion_id");
            entity.Property(e => e.MovimientoRevertidoId).HasColumnName("movimiento_revertido_id");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.ReservadoAnterior).HasColumnName("reservado_anterior");
            entity.Property(e => e.ReservadoPosterior).HasColumnName("reservado_posterior");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.StockAnterior).HasColumnName("stock_anterior");
            entity.Property(e => e.StockPosterior).HasColumnName("stock_posterior");
            entity.Property(e => e.TipoMovimiento)
                .HasMaxLength(30)
                .HasColumnName("tipo_movimiento");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");

            entity.HasOne(d => d.AnulacionVenta).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.AnulacionVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_anulacion");

            entity.HasOne(d => d.Apartado).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.ApartadoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_apartado");

            entity.HasOne(d => d.AutorizacionOperacion).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_autorizacion");

            entity.HasOne(d => d.CambioVenta).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.CambioVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_cambio");

            entity.HasOne(d => d.Compra).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.CompraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_compra");

            entity.HasOne(d => d.MotivoOperacion).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.MotivoOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_motivo");

            entity.HasOne(d => d.MovimientoRevertido).WithOne(p => p.InverseMovimientoRevertido)
                .HasForeignKey<MovimientoInventario>(d => d.MovimientoRevertidoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_revertido");

            entity.HasOne(d => d.Producto).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.ProductoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_producto");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_sesion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_usuario");

            entity.HasOne(d => d.Venta).WithMany(p => p.MovimientoInventario)
                .HasForeignKey(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_movimiento_inventario_venta");
        });

        modelBuilder.Entity<OperacionIdempotente>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_operacion_idempotente");

            entity.ToTable("operacion_idempotente", "auditoria");

            entity.HasIndex(e => e.FechaRegistro, "ix_idempotente_fecha").IsDescending();

            entity.HasIndex(e => e.SesionOperadorId, "ix_idempotente_sesion");

            entity.HasIndex(e => e.TerminalId, "ix_idempotente_terminal");

            entity.HasIndex(e => e.UsuarioId, "ix_idempotente_usuario");

            entity.HasIndex(e => new { e.InstalacionId, e.ClaveIdempotencia }, "uq_idempotente_instalacion_clave").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ClaveIdempotencia)
                .HasMaxLength(100)
                .HasColumnName("clave_idempotencia");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.Entidad)
                .HasMaxLength(100)
                .HasColumnName("entidad");
            entity.Property(e => e.EntidadId).HasColumnName("entidad_id");
            entity.Property(e => e.FechaRegistro)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_registro");
            entity.Property(e => e.HashSolicitud)
                .HasMaxLength(128)
                .HasColumnName("hash_solicitud");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Resultado)
                .HasColumnType("jsonb")
                .HasColumnName("resultado");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TipoOperacion)
                .HasMaxLength(100)
                .HasColumnName("tipo_operacion");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.OperacionIdempotente)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_idempotente_instalacion");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.OperacionIdempotente)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_idempotente_sesion");

            entity.HasOne(d => d.Terminal).WithMany(p => p.OperacionIdempotente)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_idempotente_terminal");

            entity.HasOne(d => d.Usuario).WithMany(p => p.OperacionIdempotente)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_idempotente_usuario");
        });

        modelBuilder.Entity<PagoApartado>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_pago_apartado");

            entity.ToTable("pago_apartado", "ventas");

            entity.HasIndex(e => new { e.ApartadoId, e.FechaHora }, "ix_pago_apartado_apartado_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.MetodoPagoId, "ix_pago_apartado_metodo");

            entity.HasIndex(e => e.MotivoAnulacionId, "ix_pago_apartado_motivo_anulacion");

            entity.HasIndex(e => e.SesionOperadorId, "ix_pago_apartado_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_pago_apartado_turno");

            entity.HasIndex(e => e.UsuarioId, "ix_pago_apartado_usuario");

            entity.HasIndex(e => e.UsuarioAnulacionId, "ix_pago_apartado_usuario_anulacion");

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_pago_apartado_inst_serie_numero").IsUnique();

            entity.HasIndex(e => e.MovimientoCajaId, "uq_pago_apartado_movimiento_caja").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ApartadoId).HasColumnName("apartado_id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.Cambio)
                .HasPrecision(18)
                .HasColumnName("cambio");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'APLICADO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.MovimientoCajaId).HasColumnName("movimiento_caja_id");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.Referencia)
                .HasMaxLength(200)
                .HasColumnName("referencia");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorAplicado)
                .HasPrecision(18)
                .HasColumnName("valor_aplicado");
            entity.Property(e => e.ValorRecibido)
                .HasPrecision(18)
                .HasColumnName("valor_recibido");

            entity.HasOne(d => d.Apartado).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.ApartadoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_apartado");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_autorizacion_anulacion");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_instalacion");

            entity.HasOne(d => d.MetodoPago).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.MetodoPagoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_metodo");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_motivo_anulacion");

            entity.HasOne(d => d.MovimientoCaja).WithOne(p => p.PagoApartado)
                .HasForeignKey<PagoApartado>(d => d.MovimientoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_movimiento_caja");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.PagoApartado)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_turno");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.PagoApartadoUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.PagoApartadoUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_apartado_usuario");
        });

        modelBuilder.Entity<PagoCambioVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_pago_cambio_venta");

            entity.ToTable("pago_cambio_venta", "ventas");

            entity.HasIndex(e => e.CambioVentaId, "ix_pago_cambio_cambio");

            entity.HasIndex(e => e.MetodoPagoId, "ix_pago_cambio_metodo");

            entity.HasIndex(e => e.SesionOperadorId, "ix_pago_cambio_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_pago_cambio_turno");

            entity.HasIndex(e => e.UsuarioId, "ix_pago_cambio_usuario");

            entity.HasIndex(e => e.MovimientoCajaId, "uq_pago_cambio_movimiento_caja").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.Cambio)
                .HasPrecision(18)
                .HasColumnName("cambio");
            entity.Property(e => e.CambioVentaId).HasColumnName("cambio_venta_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'APLICADO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.MovimientoCajaId).HasColumnName("movimiento_caja_id");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.Referencia)
                .HasMaxLength(200)
                .HasColumnName("referencia");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TipoMovimiento)
                .HasMaxLength(20)
                .HasColumnName("tipo_movimiento");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Valor)
                .HasPrecision(18)
                .HasColumnName("valor");
            entity.Property(e => e.ValorRecibido)
                .HasPrecision(18)
                .HasColumnName("valor_recibido");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_autorizacion_anulacion");

            entity.HasOne(d => d.CambioVenta).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.CambioVentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_cambio");

            entity.HasOne(d => d.MetodoPago).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.MetodoPagoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_metodo");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_motivo_anulacion");

            entity.HasOne(d => d.MovimientoCaja).WithOne(p => p.PagoCambioVenta)
                .HasForeignKey<PagoCambioVenta>(d => d.MovimientoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_movimiento_caja");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.PagoCambioVenta)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_turno");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.PagoCambioVentaUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.PagoCambioVentaUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_cambio_usuario");
        });

        modelBuilder.Entity<PagoCredito>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_pago_credito");

            entity.ToTable("pago_credito", "ventas");

            entity.HasIndex(e => new { e.CreditoId, e.FechaHora }, "ix_pago_credito_credito_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.MetodoPagoId, "ix_pago_credito_metodo");

            entity.HasIndex(e => e.MotivoAnulacionId, "ix_pago_credito_motivo_anulacion");

            entity.HasIndex(e => e.SesionOperadorId, "ix_pago_credito_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_pago_credito_turno");

            entity.HasIndex(e => e.UsuarioId, "ix_pago_credito_usuario");

            entity.HasIndex(e => e.UsuarioAnulacionId, "ix_pago_credito_usuario_anulacion");

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_pago_credito_inst_serie_numero").IsUnique();

            entity.HasIndex(e => e.MovimientoCajaId, "uq_pago_credito_movimiento_caja").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.Cambio)
                .HasPrecision(18)
                .HasColumnName("cambio");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.CreditoId).HasColumnName("credito_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'APLICADO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.MovimientoCajaId).HasColumnName("movimiento_caja_id");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.Referencia)
                .HasMaxLength(200)
                .HasColumnName("referencia");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorAplicado)
                .HasPrecision(18)
                .HasColumnName("valor_aplicado");
            entity.Property(e => e.ValorRecibido)
                .HasPrecision(18)
                .HasColumnName("valor_recibido");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_autorizacion_anulacion");

            entity.HasOne(d => d.Credito).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.CreditoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_credito");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_instalacion");

            entity.HasOne(d => d.MetodoPago).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.MetodoPagoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_metodo");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_motivo_anulacion");

            entity.HasOne(d => d.MovimientoCaja).WithOne(p => p.PagoCredito)
                .HasForeignKey<PagoCredito>(d => d.MovimientoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_movimiento_caja");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.PagoCredito)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_turno");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.PagoCreditoUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.PagoCreditoUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_credito_usuario");
        });

        modelBuilder.Entity<PagoVenta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_pago_venta");

            entity.ToTable("pago_venta", "ventas");

            entity.HasIndex(e => e.MetodoPagoId, "ix_pago_venta_metodo");

            entity.HasIndex(e => e.MotivoAnulacionId, "ix_pago_venta_motivo_anulacion");

            entity.HasIndex(e => e.SesionOperadorId, "ix_pago_venta_sesion");

            entity.HasIndex(e => e.UsuarioId, "ix_pago_venta_usuario");

            entity.HasIndex(e => e.UsuarioAnulacionId, "ix_pago_venta_usuario_anulacion");

            entity.HasIndex(e => e.VentaId, "ix_pago_venta_venta");

            entity.HasIndex(e => e.MovimientoCajaId, "uq_pago_venta_movimiento_caja").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionAnulacionId).HasColumnName("autorizacion_anulacion_id");
            entity.Property(e => e.Cambio)
                .HasPrecision(18)
                .HasColumnName("cambio");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'APLICADO'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaAnulacion).HasColumnName("fecha_anulacion");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(e => e.MotivoAnulacionId).HasColumnName("motivo_anulacion_id");
            entity.Property(e => e.MovimientoCajaId).HasColumnName("movimiento_caja_id");
            entity.Property(e => e.ObservacionAnulacion).HasColumnName("observacion_anulacion");
            entity.Property(e => e.Referencia)
                .HasMaxLength(200)
                .HasColumnName("referencia");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.UsuarioAnulacionId).HasColumnName("usuario_anulacion_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorAplicado)
                .HasPrecision(18)
                .HasColumnName("valor_aplicado");
            entity.Property(e => e.ValorRecibido)
                .HasPrecision(18)
                .HasColumnName("valor_recibido");
            entity.Property(e => e.VentaId).HasColumnName("venta_id");

            entity.HasOne(d => d.AutorizacionAnulacion).WithMany(p => p.PagoVenta)
                .HasForeignKey(d => d.AutorizacionAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_autorizacion_anulacion");

            entity.HasOne(d => d.MetodoPago).WithMany(p => p.PagoVenta)
                .HasForeignKey(d => d.MetodoPagoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_metodo");

            entity.HasOne(d => d.MotivoAnulacion).WithMany(p => p.PagoVenta)
                .HasForeignKey(d => d.MotivoAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_motivo_anulacion");

            entity.HasOne(d => d.MovimientoCaja).WithOne(p => p.PagoVenta)
                .HasForeignKey<PagoVenta>(d => d.MovimientoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_movimiento_caja");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.PagoVenta)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_sesion");

            entity.HasOne(d => d.UsuarioAnulacion).WithMany(p => p.PagoVentaUsuarioAnulacion)
                .HasForeignKey(d => d.UsuarioAnulacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_usuario_anulacion");

            entity.HasOne(d => d.Usuario).WithMany(p => p.PagoVentaUsuario)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_usuario");

            entity.HasOne(d => d.Venta).WithMany(p => p.PagoVenta)
                .HasForeignKey(d => d.VentaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pago_venta_venta");
        });

        modelBuilder.Entity<PedidoCompra>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_pedido_compra");

            entity.ToTable("pedido_compra", "compras", tb => tb.HasComment("Pedido generado por el proveedor actualmente asignado a los productos; cada recepcion confirmada crea una compra."));

            entity.HasIndex(e => new { e.InstalacionId, e.FechaCreacion }, "ix_pedido_compra_instalacion_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.ProveedorId, e.Estado, e.FechaCreacion }, "ix_pedido_compra_proveedor_estado").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.InstalacionId, e.ProveedorId }, "uq_pedido_compra_borrador_proveedor")
                .IsUnique()
                .HasFilter("((estado)::text = 'BORRADOR'::text)");

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_pedido_compra_inst_serie_numero").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AutorizacionCancelacionId).HasColumnName("autorizacion_cancelacion_id");
            entity.Property(e => e.AutorizacionCierreId).HasColumnName("autorizacion_cierre_id");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.Estado)
                .HasMaxLength(30)
                .HasDefaultValueSql("'BORRADOR'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaCancelacion).HasColumnName("fecha_cancelacion");
            entity.Property(e => e.FechaCierre).HasColumnName("fecha_cierre");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaEnvio).HasColumnName("fecha_envio");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.MotivoCancelacionId).HasColumnName("motivo_cancelacion_id");
            entity.Property(e => e.MotivoCierreId).HasColumnName("motivo_cierre_id");
            entity.Property(e => e.ObservacionCancelacion).HasColumnName("observacion_cancelacion");
            entity.Property(e => e.ObservacionCierre).HasColumnName("observacion_cierre");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.ProveedorId).HasColumnName("proveedor_id");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.UsuarioCancelacionId).HasColumnName("usuario_cancelacion_id");
            entity.Property(e => e.UsuarioCierreId).HasColumnName("usuario_cierre_id");
            entity.Property(e => e.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
            entity.Property(e => e.UsuarioEnvioId).HasColumnName("usuario_envio_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionCancelacion).WithMany(p => p.PedidoCompraAutorizacionCancelacion)
                .HasForeignKey(d => d.AutorizacionCancelacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_autorizacion_cancelacion");

            entity.HasOne(d => d.AutorizacionCierre).WithMany(p => p.PedidoCompraAutorizacionCierre)
                .HasForeignKey(d => d.AutorizacionCierreId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_autorizacion_cierre");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.PedidoCompra)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_instalacion");

            entity.HasOne(d => d.MotivoCancelacion).WithMany(p => p.PedidoCompraMotivoCancelacion)
                .HasForeignKey(d => d.MotivoCancelacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_motivo_cancelacion");

            entity.HasOne(d => d.MotivoCierre).WithMany(p => p.PedidoCompraMotivoCierre)
                .HasForeignKey(d => d.MotivoCierreId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_motivo_cierre");

            entity.HasOne(d => d.Proveedor).WithMany(p => p.PedidoCompra)
                .HasForeignKey(d => d.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_proveedor");

            entity.HasOne(d => d.UsuarioCancelacion).WithMany(p => p.PedidoCompraUsuarioCancelacion)
                .HasForeignKey(d => d.UsuarioCancelacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_usuario_cancelacion");

            entity.HasOne(d => d.UsuarioCierre).WithMany(p => p.PedidoCompraUsuarioCierre)
                .HasForeignKey(d => d.UsuarioCierreId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_usuario_cierre");

            entity.HasOne(d => d.UsuarioCreacion).WithMany(p => p.PedidoCompraUsuarioCreacion)
                .HasForeignKey(d => d.UsuarioCreacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_usuario_creacion");

            entity.HasOne(d => d.UsuarioEnvio).WithMany(p => p.PedidoCompraUsuarioEnvio)
                .HasForeignKey(d => d.UsuarioEnvioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_pedido_compra_usuario_envio");
        });

        modelBuilder.Entity<PerfilImpresora>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_perfil_impresora");

            entity.ToTable("perfil_impresora", "configuracion");

            entity.HasIndex(e => new { e.TerminalId, e.Activo, e.TipoImpresora }, "ix_perfil_impresora_terminal_activo");

            entity.HasIndex(e => new { e.TerminalId, e.Nombre }, "uq_perfil_impresora_nombre").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.AltoMm)
                .HasPrecision(8, 2)
                .HasColumnName("alto_mm");
            entity.Property(e => e.AnchoMm)
                .HasPrecision(8, 2)
                .HasColumnName("ancho_mm");
            entity.Property(e => e.EsPredeterminada).HasColumnName("es_predeterminada");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(150)
                .HasColumnName("nombre");
            entity.Property(e => e.NombreSistema)
                .HasMaxLength(300)
                .HasColumnName("nombre_sistema");
            entity.Property(e => e.ResolucionDpi).HasColumnName("resolucion_dpi");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TipoImpresora)
                .HasMaxLength(20)
                .HasColumnName("tipo_impresora");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Terminal).WithMany(p => p.PerfilImpresora)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_perfil_impresora_terminal");
        });

        modelBuilder.Entity<Permiso>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_permiso");

            entity.ToTable("permiso", "seguridad");

            entity.HasIndex(e => new { e.Modulo, e.Activo }, "ix_permiso_modulo_activo");

            entity.HasIndex(e => e.Codigo, "uq_permiso_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(100)
                .HasColumnName("codigo");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(500)
                .HasColumnName("descripcion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.Modulo)
                .HasMaxLength(50)
                .HasColumnName("modulo");
            entity.Property(e => e.Nombre)
                .HasMaxLength(150)
                .HasColumnName("nombre");
        });

        modelBuilder.Entity<PlantillaImpresion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_plantilla_impresion");

            entity.ToTable("plantilla_impresion", "configuracion");

            entity.HasIndex(e => new { e.EstablecimientoId, e.TipoSalida, e.Activa }, "ix_plantilla_impresion_tipo_activa");

            entity.HasIndex(e => new { e.EstablecimientoId, e.Codigo }, "uq_plantilla_impresion_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activa)
                .HasDefaultValue(true)
                .HasColumnName("activa");
            entity.Property(e => e.AltoMm)
                .HasPrecision(8, 2)
                .HasColumnName("alto_mm");
            entity.Property(e => e.AnchoMm)
                .HasPrecision(8, 2)
                .HasColumnName("ancho_mm");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.Configuracion)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("configuracion");
            entity.Property(e => e.EstablecimientoId).HasColumnName("establecimiento_id");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.FormatoCodigo)
                .HasMaxLength(20)
                .HasColumnName("formato_codigo");
            entity.Property(e => e.Nombre)
                .HasMaxLength(150)
                .HasColumnName("nombre");
            entity.Property(e => e.TipoSalida)
                .HasMaxLength(30)
                .HasColumnName("tipo_salida");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Establecimiento).WithMany(p => p.PlantillaImpresion)
                .HasForeignKey(d => d.EstablecimientoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_plantilla_impresion_establecimiento");
        });

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_producto");

            entity.ToTable("producto", "catalogo");

            entity.HasIndex(e => new { e.CategoriaId, e.Activo }, "ix_producto_categoria_activo");

            entity.HasIndex(e => new { e.ProveedorId, e.Activo }, "ix_producto_proveedor_activo");

            entity.HasIndex(e => e.Id, "ix_producto_stock_bajo").HasFilter("((activo = true) AND (stock_actual <= stock_minimo))");

            entity.HasIndex(e => e.UnidadMedidaId, "ix_producto_unidad");

            entity.HasIndex(e => e.UsuarioCreacionId, "ix_producto_usuario_creacion");

            entity.HasIndex(e => e.UsuarioModificacionId, "ix_producto_usuario_modificacion");

            entity.HasIndex(e => e.CodigoBarras, "uq_producto_codigo_barras").IsUnique();

            entity.HasIndex(e => e.CodigoInterno, "uq_producto_codigo_interno").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.CategoriaId).HasColumnName("categoria_id");
            entity.Property(e => e.CodigoBarras)
                .HasMaxLength(100)
                .HasColumnName("codigo_barras");
            entity.Property(e => e.CodigoBarrasGenerado).HasColumnName("codigo_barras_generado");
            entity.Property(e => e.CodigoInterno)
                .HasMaxLength(50)
                .HasColumnName("codigo_interno");
            entity.Property(e => e.CostoPromedio)
                .HasPrecision(18, 4)
                .HasComment("NULL significa costo aun desconocido; cero representa un costo conocido de cero.")
                .HasColumnName("costo_promedio");
            entity.Property(e => e.Descripcion).HasColumnName("descripcion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.FormatoCodigoBarras)
                .HasMaxLength(20)
                .HasColumnName("formato_codigo_barras");
            entity.Property(e => e.Nombre)
                .HasMaxLength(200)
                .HasColumnName("nombre");
            entity.Property(e => e.PorcentajeIva)
                .HasPrecision(5, 2)
                .HasComment("NULL significa IVA no especificado; 0 significa IVA conocido de cero por ciento.")
                .HasColumnName("porcentaje_iva");
            entity.Property(e => e.PrecioMayorista)
                .HasPrecision(18)
                .HasComment("Precio final mayorista opcional en COP enteros; nunca se le suma IVA en caja.")
                .HasColumnName("precio_mayorista");
            entity.Property(e => e.PrecioMinorista)
                .HasPrecision(18)
                .HasComment("Precio final de venta en COP enteros con IVA incluido cuando este se conoce.")
                .HasColumnName("precio_minorista");
            entity.Property(e => e.ProveedorId)
                .HasComment("Unico proveedor asignado al producto en V1. NULL se muestra como Sin proveedor.")
                .HasColumnName("proveedor_id");
            entity.Property(e => e.StockActual).HasColumnName("stock_actual");
            entity.Property(e => e.StockMinimo).HasColumnName("stock_minimo");
            entity.Property(e => e.StockReservado).HasColumnName("stock_reservado");
            entity.Property(e => e.UnidadMedidaId).HasColumnName("unidad_medida_id");
            entity.Property(e => e.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
            entity.Property(e => e.UsuarioModificacionId).HasColumnName("usuario_modificacion_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Categoria).WithMany(p => p.Producto)
                .HasForeignKey(d => d.CategoriaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_producto_categoria");

            entity.HasOne(d => d.Proveedor).WithMany(p => p.Producto)
                .HasForeignKey(d => d.ProveedorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_producto_proveedor");

            entity.HasOne(d => d.UnidadMedida).WithMany(p => p.Producto)
                .HasForeignKey(d => d.UnidadMedidaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_producto_unidad");

            entity.HasOne(d => d.UsuarioCreacion).WithMany(p => p.ProductoUsuarioCreacion)
                .HasForeignKey(d => d.UsuarioCreacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_producto_usuario_creacion");

            entity.HasOne(d => d.UsuarioModificacion).WithMany(p => p.ProductoUsuarioModificacion)
                .HasForeignKey(d => d.UsuarioModificacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_producto_usuario_modificacion");
        });

        modelBuilder.Entity<Proveedor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_proveedor");

            entity.ToTable("proveedor", "catalogo");

            entity.HasIndex(e => e.UsuarioCreacionId, "ix_proveedor_usuario_creacion");

            entity.HasIndex(e => e.UsuarioModificacionId, "ix_proveedor_usuario_modificacion");

            entity.HasIndex(e => new { e.TipoDocumento, e.NumeroDocumento }, "uq_proveedor_documento").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Correo)
                .HasMaxLength(254)
                .HasColumnName("correo");
            entity.Property(e => e.Direccion)
                .HasMaxLength(300)
                .HasColumnName("direccion");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.NombreRazonSocial)
                .HasMaxLength(200)
                .HasColumnName("nombre_razon_social");
            entity.Property(e => e.NumeroDocumento)
                .HasMaxLength(50)
                .HasColumnName("numero_documento");
            entity.Property(e => e.Observaciones).HasColumnName("observaciones");
            entity.Property(e => e.Telefono)
                .HasMaxLength(30)
                .HasColumnName("telefono");
            entity.Property(e => e.TipoDocumento)
                .HasMaxLength(20)
                .HasColumnName("tipo_documento");
            entity.Property(e => e.UsuarioCreacionId).HasColumnName("usuario_creacion_id");
            entity.Property(e => e.UsuarioModificacionId).HasColumnName("usuario_modificacion_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.UsuarioCreacion).WithMany(p => p.ProveedorUsuarioCreacion)
                .HasForeignKey(d => d.UsuarioCreacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_proveedor_usuario_creacion");

            entity.HasOne(d => d.UsuarioModificacion).WithMany(p => p.ProveedorUsuarioModificacion)
                .HasForeignKey(d => d.UsuarioModificacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_proveedor_usuario_modificacion");
        });

        modelBuilder.Entity<PruebaRestauracion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_prueba_restauracion");

            entity.ToTable("prueba_restauracion", "configuracion");

            entity.HasIndex(e => e.AutorizacionOperacionId, "ix_prueba_restauracion_autorizacion");

            entity.HasIndex(e => new { e.CopiaRespaldoId, e.FechaInicio }, "ix_prueba_restauracion_copia_fecha").IsDescending(false, true);

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AplicacionVerificada).HasColumnName("aplicacion_verificada");
            entity.Property(e => e.AutorizacionOperacionId).HasColumnName("autorizacion_operacion_id");
            entity.Property(e => e.CopiaRespaldoId).HasColumnName("copia_respaldo_id");
            entity.Property(e => e.EjecutadaPorId).HasColumnName("ejecutada_por_id");
            entity.Property(e => e.ErrorSanitizado).HasColumnName("error_sanitizado");
            entity.Property(e => e.FechaFin).HasColumnName("fecha_fin");
            entity.Property(e => e.FechaInicio)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_inicio");
            entity.Property(e => e.IntegridadVerificada).HasColumnName("integridad_verificada");
            entity.Property(e => e.Observaciones).HasColumnName("observaciones");
            entity.Property(e => e.Resultado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'EN_PROCESO'::character varying")
                .HasColumnName("resultado");
            entity.Property(e => e.RpoObservadoMinutos).HasColumnName("rpo_observado_minutos");
            entity.Property(e => e.RtoObservadoMinutos).HasColumnName("rto_observado_minutos");

            entity.HasOne(d => d.AutorizacionOperacion).WithMany(p => p.PruebaRestauracion)
                .HasForeignKey(d => d.AutorizacionOperacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_prueba_restauracion_autorizacion");

            entity.HasOne(d => d.CopiaRespaldo).WithMany(p => p.PruebaRestauracion)
                .HasForeignKey(d => d.CopiaRespaldoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_prueba_restauracion_copia");

            entity.HasOne(d => d.EjecutadaPor).WithMany(p => p.PruebaRestauracion)
                .HasForeignKey(d => d.EjecutadaPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_prueba_restauracion_usuario");
        });

        modelBuilder.Entity<ReembolsoApartado>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_reembolso_apartado");

            entity.ToTable("reembolso_apartado", "ventas");

            entity.HasIndex(e => new { e.ApartadoId, e.FechaHora }, "ix_reembolso_apartado_apartado_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.SesionOperadorId, "ix_reembolso_apartado_sesion");

            entity.HasIndex(e => e.TurnoCajaId, "ix_reembolso_apartado_turno");

            entity.HasIndex(e => e.MovimientoCajaId, "uq_reembolso_apartado_movimiento").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ApartadoId).HasColumnName("apartado_id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.MetodoPagoId).HasColumnName("metodo_pago_id");
            entity.Property(e => e.MovimientoCajaId).HasColumnName("movimiento_caja_id");
            entity.Property(e => e.Referencia)
                .HasMaxLength(200)
                .HasColumnName("referencia");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.Valor)
                .HasPrecision(18)
                .HasColumnName("valor");

            entity.HasOne(d => d.Apartado).WithMany(p => p.ReembolsoApartado)
                .HasForeignKey(d => d.ApartadoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_apartado");

            entity.HasOne(d => d.MetodoPago).WithMany(p => p.ReembolsoApartado)
                .HasForeignKey(d => d.MetodoPagoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_metodo");

            entity.HasOne(d => d.MovimientoCaja).WithOne(p => p.ReembolsoApartado)
                .HasForeignKey<ReembolsoApartado>(d => d.MovimientoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_movimiento");

            entity.HasOne(d => d.SesionOperador).WithMany(p => p.ReembolsoApartado)
                .HasForeignKey(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_sesion");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.ReembolsoApartado)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_turno");

            entity.HasOne(d => d.Usuario).WithMany(p => p.ReembolsoApartado)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_reembolso_apartado_usuario");
        });

        modelBuilder.Entity<Rol>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_rol");

            entity.ToTable("rol", "seguridad", tb => tb.HasComment("La aplicacion crea Administrador, Supervisor y Cajero como plantillas iniciales; sus permisos son editables."));

            entity.HasIndex(e => e.Codigo, "uq_rol_codigo").IsUnique();

            entity.HasIndex(e => e.Nombre, "uq_rol_nombre").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.Descripcion)
                .HasMaxLength(500)
                .HasColumnName("descripcion");
            entity.Property(e => e.EsPredefinido).HasColumnName("es_predefinido");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.PermisosEditables)
                .HasDefaultValue(true)
                .HasColumnName("permisos_editables");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");
        });

        modelBuilder.Entity<RolClaim>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_rol_claim");

            entity.ToTable("rol_claim", "seguridad");

            entity.HasIndex(e => e.RolId, "ix_rol_claim_rol");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.RolId).HasColumnName("rol_id");
            entity.Property(e => e.TipoClaim)
                .HasMaxLength(100)
                .HasColumnName("tipo_claim");
            entity.Property(e => e.ValorClaim).HasColumnName("valor_claim");

            entity.HasOne(d => d.Rol).WithMany(p => p.RolClaim)
                .HasForeignKey(d => d.RolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_rol_claim_rol");
        });

        modelBuilder.Entity<RolPermiso>(entity =>
        {
            entity.HasKey(e => new { e.RolId, e.PermisoId }).HasName("pk_rol_permiso");

            entity.ToTable("rol_permiso", "seguridad");

            entity.HasIndex(e => e.PermisoId, "ix_rol_permiso_permiso");

            entity.Property(e => e.RolId).HasColumnName("rol_id");
            entity.Property(e => e.PermisoId).HasColumnName("permiso_id");
            entity.Property(e => e.FechaAsignacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_asignacion");

            entity.HasOne(d => d.Permiso).WithMany(p => p.RolPermiso)
                .HasForeignKey(d => d.PermisoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_rol_permiso_permiso");

            entity.HasOne(d => d.Rol).WithMany(p => p.RolPermiso)
                .HasForeignKey(d => d.RolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_rol_permiso_rol");
        });

        modelBuilder.Entity<SesionOperador>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_sesion_operador");

            entity.ToTable("sesion_operador", "caja");

            entity.HasIndex(e => e.CredencialUsuarioId, "ix_sesion_operador_credencial");

            entity.HasIndex(e => new { e.TurnoCajaId, e.FechaInicio }, "ix_sesion_operador_turno_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.UsuarioId, e.FechaInicio }, "ix_sesion_operador_usuario_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.TerminalId, "uq_sesion_operador_activa_terminal")
                .IsUnique()
                .HasFilter("((estado)::text = 'ACTIVA'::text)");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CorrelacionId).HasColumnName("correlacion_id");
            entity.Property(e => e.CredencialUsuarioId).HasColumnName("credencial_usuario_id");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVA'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaFin).HasColumnName("fecha_fin");
            entity.Property(e => e.FechaInicio)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_inicio");
            entity.Property(e => e.FechaUltimoUso)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_ultimo_uso");
            entity.Property(e => e.MotivoCierre)
                .HasMaxLength(30)
                .HasColumnName("motivo_cierre");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");

            entity.HasOne(d => d.CredencialUsuario).WithMany(p => p.SesionOperador)
                .HasForeignKey(d => d.CredencialUsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sesion_operador_credencial");

            entity.HasOne(d => d.Terminal).WithOne(p => p.SesionOperador)
                .HasForeignKey<SesionOperador>(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sesion_operador_terminal");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.SesionOperador)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sesion_operador_turno");

            entity.HasOne(d => d.Usuario).WithMany(p => p.SesionOperador)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_sesion_operador_usuario");
        });

        modelBuilder.Entity<Terminal>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_terminal");

            entity.ToTable("terminal", "configuracion");

            entity.HasIndex(e => new { e.InstalacionId, e.Codigo }, "uq_terminal_instalacion_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(50)
                .HasColumnName("codigo");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.Terminal)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_terminal_instalacion");
        });

        modelBuilder.Entity<TrabajoImpresion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_trabajo_impresion");

            entity.ToTable("trabajo_impresion", "configuracion", tb => tb.HasComment("Cola reintentable separada de la transaccion comercial; un fallo de impresion no revierte la venta."));

            entity.HasIndex(e => new { e.DocumentoEmitidoId, e.FechaSolicitud }, "ix_trabajo_impresion_documento").IsDescending(false, true);

            entity.HasIndex(e => new { e.TerminalId, e.Estado, e.FechaSolicitud }, "ix_trabajo_impresion_pendiente").HasFilter("((estado)::text = ANY ((ARRAY['PENDIENTE'::character varying, 'IMPRIMIENDO'::character varying, 'FALLIDO'::character varying])::text[]))");

            entity.HasIndex(e => new { e.DocumentoEmitidoId, e.TipoCopia, e.NumeroReimpresion }, "uq_trabajo_impresion_copia").IsUnique();

            entity.HasIndex(e => new { e.TerminalId, e.ClaveIdempotencia }, "uq_trabajo_impresion_idempotencia").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.ClaveIdempotencia)
                .HasMaxLength(100)
                .HasColumnName("clave_idempotencia");
            entity.Property(e => e.ContenidoRenderizado).HasColumnName("contenido_renderizado");
            entity.Property(e => e.DocumentoEmitidoId).HasColumnName("documento_emitido_id");
            entity.Property(e => e.ErrorSanitizado).HasColumnName("error_sanitizado");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'PENDIENTE'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaImpresion).HasColumnName("fecha_impresion");
            entity.Property(e => e.FechaSolicitud)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_solicitud");
            entity.Property(e => e.FechaUltimoIntento).HasColumnName("fecha_ultimo_intento");
            entity.Property(e => e.HashContenido)
                .HasMaxLength(128)
                .HasColumnName("hash_contenido");
            entity.Property(e => e.Intentos).HasColumnName("intentos");
            entity.Property(e => e.NumeroReimpresion).HasColumnName("numero_reimpresion");
            entity.Property(e => e.PerfilImpresoraId).HasColumnName("perfil_impresora_id");
            entity.Property(e => e.PlantillaImpresionId).HasColumnName("plantilla_impresion_id");
            entity.Property(e => e.SolicitadoPorId).HasColumnName("solicitado_por_id");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.TipoCopia)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ORIGINAL'::character varying")
                .HasColumnName("tipo_copia");

            entity.HasOne(d => d.DocumentoEmitido).WithMany(p => p.TrabajoImpresion)
                .HasForeignKey(d => d.DocumentoEmitidoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_trabajo_impresion_documento");

            entity.HasOne(d => d.PerfilImpresora).WithMany(p => p.TrabajoImpresion)
                .HasForeignKey(d => d.PerfilImpresoraId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_trabajo_impresion_perfil");

            entity.HasOne(d => d.PlantillaImpresion).WithMany(p => p.TrabajoImpresion)
                .HasForeignKey(d => d.PlantillaImpresionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_trabajo_impresion_plantilla");

            entity.HasOne(d => d.SolicitadoPor).WithMany(p => p.TrabajoImpresion)
                .HasForeignKey(d => d.SolicitadoPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_trabajo_impresion_usuario");

            entity.HasOne(d => d.Terminal).WithMany(p => p.TrabajoImpresion)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_trabajo_impresion_terminal");
        });

        modelBuilder.Entity<TurnoCaja>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_turno_caja");

            entity.ToTable("turno_caja", "caja");

            entity.HasIndex(e => new { e.CajaId, e.FechaHoraApertura }, "ix_turno_caja_caja_fecha").IsDescending(false, true);

            entity.HasIndex(e => e.FechaOperativa, "ix_turno_caja_fecha_operativa").IsDescending();

            entity.HasIndex(e => e.TerminalId, "ix_turno_caja_terminal");

            entity.HasIndex(e => e.UsuarioAperturaId, "ix_turno_caja_usuario_apertura");

            entity.HasIndex(e => e.UsuarioCierreId, "ix_turno_caja_usuario_cierre");

            entity.HasIndex(e => e.UsuarioResponsableId, "ix_turno_caja_usuario_responsable");

            entity.HasIndex(e => e.CajaId, "uq_turno_caja_abierto_por_caja")
                .IsUnique()
                .HasFilter("((estado)::text = 'ABIERTA'::text)");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.CajaId).HasColumnName("caja_id");
            entity.Property(e => e.Diferencia)
                .HasPrecision(18)
                .HasColumnName("diferencia");
            entity.Property(e => e.EfectivoContado)
                .HasPrecision(18)
                .HasColumnName("efectivo_contado");
            entity.Property(e => e.EfectivoEsperado)
                .HasPrecision(18)
                .HasColumnName("efectivo_esperado");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ABIERTA'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaHoraApertura)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora_apertura");
            entity.Property(e => e.FechaHoraCierre).HasColumnName("fecha_hora_cierre");
            entity.Property(e => e.FechaOperativa).HasColumnName("fecha_operativa");
            entity.Property(e => e.ModoOperacion)
                .HasMaxLength(20)
                .HasColumnName("modo_operacion");
            entity.Property(e => e.MontoInicial)
                .HasPrecision(18)
                .HasColumnName("monto_inicial");
            entity.Property(e => e.MotivoDiferenciaId).HasColumnName("motivo_diferencia_id");
            entity.Property(e => e.ObservacionDiferencia).HasColumnName("observacion_diferencia");
            entity.Property(e => e.Observaciones).HasColumnName("observaciones");
            entity.Property(e => e.TerminalId).HasColumnName("terminal_id");
            entity.Property(e => e.UsuarioAperturaId).HasColumnName("usuario_apertura_id");
            entity.Property(e => e.UsuarioCierreId).HasColumnName("usuario_cierre_id");
            entity.Property(e => e.UsuarioResponsableId).HasColumnName("usuario_responsable_id");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.Caja).WithOne(p => p.TurnoCaja)
                .HasForeignKey<TurnoCaja>(d => d.CajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_caja");

            entity.HasOne(d => d.MotivoDiferencia).WithMany(p => p.TurnoCaja)
                .HasForeignKey(d => d.MotivoDiferenciaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_motivo_diferencia");

            entity.HasOne(d => d.Terminal).WithMany(p => p.TurnoCaja)
                .HasForeignKey(d => d.TerminalId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_terminal");

            entity.HasOne(d => d.UsuarioApertura).WithMany(p => p.TurnoCajaUsuarioApertura)
                .HasForeignKey(d => d.UsuarioAperturaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_usuario_apertura");

            entity.HasOne(d => d.UsuarioCierre).WithMany(p => p.TurnoCajaUsuarioCierre)
                .HasForeignKey(d => d.UsuarioCierreId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_usuario_cierre");

            entity.HasOne(d => d.UsuarioResponsable).WithMany(p => p.TurnoCajaUsuarioResponsable)
                .HasForeignKey(d => d.UsuarioResponsableId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_turno_caja_usuario_responsable");
        });

        modelBuilder.Entity<UnidadMedida>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_unidad_medida");

            entity.ToTable("unidad_medida", "catalogo");

            entity.HasIndex(e => e.Codigo, "uq_unidad_medida_codigo").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.Codigo)
                .HasMaxLength(30)
                .HasColumnName("codigo");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.Nombre)
                .HasMaxLength(100)
                .HasColumnName("nombre");
            entity.Property(e => e.Simbolo)
                .HasMaxLength(20)
                .HasColumnName("simbolo");
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_usuario");

            entity.ToTable("usuario", "seguridad");

            entity.HasIndex(e => e.NombreUsuario, "uq_usuario_nombre").IsUnique();

            entity.HasIndex(e => e.NombreUsuarioNormalizado, "uq_usuario_nombre_normalizado").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Activo)
                .HasDefaultValue(true)
                .HasColumnName("activo");
            entity.Property(e => e.BloqueoHasta).HasColumnName("bloqueo_hasta");
            entity.Property(e => e.Correo)
                .HasMaxLength(254)
                .HasColumnName("correo");
            entity.Property(e => e.FechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_creacion");
            entity.Property(e => e.FechaModificacion).HasColumnName("fecha_modificacion");
            entity.Property(e => e.IntentosFallidos).HasColumnName("intentos_fallidos");
            entity.Property(e => e.NombreCompleto)
                .HasMaxLength(200)
                .HasColumnName("nombre_completo");
            entity.Property(e => e.NombreUsuario)
                .HasMaxLength(100)
                .HasColumnName("nombre_usuario");
            entity.Property(e => e.NombreUsuarioNormalizado)
                .HasMaxLength(100)
                .HasColumnName("nombre_usuario_normalizado");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.SelloConcurrencia)
                .HasMaxLength(100)
                .HasColumnName("sello_concurrencia");
            entity.Property(e => e.SelloSeguridad)
                .HasMaxLength(100)
                .HasColumnName("sello_seguridad");
            entity.Property(e => e.UltimoAcceso).HasColumnName("ultimo_acceso");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");
        });

        modelBuilder.Entity<UsuarioClaim>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_usuario_claim");

            entity.ToTable("usuario_claim", "seguridad");

            entity.HasIndex(e => e.UsuarioId, "ix_usuario_claim_usuario");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.TipoClaim)
                .HasMaxLength(100)
                .HasColumnName("tipo_claim");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorClaim).HasColumnName("valor_claim");

            entity.HasOne(d => d.Usuario).WithMany(p => p.UsuarioClaim)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_claim_usuario");
        });

        modelBuilder.Entity<UsuarioLogin>(entity =>
        {
            entity.HasKey(e => new { e.ProveedorLogin, e.ClaveProveedor }).HasName("pk_usuario_login");

            entity.ToTable("usuario_login", "seguridad");

            entity.HasIndex(e => e.UsuarioId, "ix_usuario_login_usuario");

            entity.Property(e => e.ProveedorLogin)
                .HasMaxLength(100)
                .HasColumnName("proveedor_login");
            entity.Property(e => e.ClaveProveedor)
                .HasMaxLength(200)
                .HasColumnName("clave_proveedor");
            entity.Property(e => e.NombreProveedor)
                .HasMaxLength(200)
                .HasColumnName("nombre_proveedor");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");

            entity.HasOne(d => d.Usuario).WithMany(p => p.UsuarioLogin)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_login_usuario");
        });

        modelBuilder.Entity<UsuarioRol>(entity =>
        {
            entity.HasKey(e => new { e.UsuarioId, e.RolId }).HasName("pk_usuario_rol");

            entity.ToTable("usuario_rol", "seguridad");

            entity.HasIndex(e => e.AsignadoPorId, "ix_usuario_rol_asignado_por");

            entity.HasIndex(e => e.RolId, "ix_usuario_rol_rol");

            entity.HasIndex(e => e.UsuarioId, "uq_usuario_rol_usuario").IsUnique();

            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.RolId).HasColumnName("rol_id");
            entity.Property(e => e.AsignadoPorId).HasColumnName("asignado_por_id");
            entity.Property(e => e.FechaAsignacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_asignacion");

            entity.HasOne(d => d.AsignadoPor).WithMany(p => p.UsuarioRolAsignadoPor)
                .HasForeignKey(d => d.AsignadoPorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_rol_asignado_por");

            entity.HasOne(d => d.Rol).WithMany(p => p.UsuarioRol)
                .HasForeignKey(d => d.RolId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_rol_rol");

            entity.HasOne(d => d.Usuario).WithOne(p => p.UsuarioRolUsuario)
                .HasForeignKey<UsuarioRol>(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_rol_usuario");
        });

        modelBuilder.Entity<UsuarioToken>(entity =>
        {
            entity.HasKey(e => new { e.UsuarioId, e.ProveedorLogin, e.NombreToken }).HasName("pk_usuario_token");

            entity.ToTable("usuario_token", "seguridad");

            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ProveedorLogin)
                .HasMaxLength(100)
                .HasColumnName("proveedor_login");
            entity.Property(e => e.NombreToken)
                .HasMaxLength(100)
                .HasColumnName("nombre_token");
            entity.Property(e => e.ValorToken).HasColumnName("valor_token");

            entity.HasOne(d => d.Usuario).WithMany(p => p.UsuarioToken)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_usuario_token_usuario");
        });

        modelBuilder.Entity<Venta>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("pk_venta");

            entity.ToTable("venta", "ventas");

            entity.HasIndex(e => e.AutorizacionDescuentoId, "ix_venta_autorizacion_descuento");

            entity.HasIndex(e => new { e.ClienteId, e.FechaHora }, "ix_venta_cliente_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.FechaOperativa, e.Estado }, "ix_venta_fecha_operativa_estado").IsDescending(true, false);

            entity.HasIndex(e => e.SesionOperadorId, "ix_venta_sesion");

            entity.HasIndex(e => new { e.TurnoCajaId, e.FechaHora }, "ix_venta_turno_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.UsuarioId, e.FechaHora }, "ix_venta_usuario_fecha").IsDescending(false, true);

            entity.HasIndex(e => new { e.InstalacionId, e.Serie, e.Consecutivo }, "uq_venta_instalacion_serie_numero").IsUnique();

            entity.HasIndex(e => e.SesionOperadorId, "uq_venta_sesion_operador")
                .IsUnique()
                .HasFilter("(sesion_operador_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.AmbitoDescuento)
                .HasMaxLength(20)
                .HasDefaultValueSql("'NINGUNO'::character varying")
                .HasColumnName("ambito_descuento");
            entity.Property(e => e.AutorizacionDescuentoId).HasColumnName("autorizacion_descuento_id");
            entity.Property(e => e.BaseGravableTotal)
                .HasPrecision(18, 4)
                .HasColumnName("base_gravable_total");
            entity.Property(e => e.ClienteId).HasColumnName("cliente_id");
            entity.Property(e => e.Consecutivo).HasColumnName("consecutivo");
            entity.Property(e => e.DescuentoTotal)
                .HasPrecision(18)
                .HasColumnName("descuento_total");
            entity.Property(e => e.Estado)
                .HasMaxLength(20)
                .HasDefaultValueSql("'CONFIRMADA'::character varying")
                .HasColumnName("estado");
            entity.Property(e => e.FechaHora)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnName("fecha_hora");
            entity.Property(e => e.FechaLimiteCambio).HasColumnName("fecha_limite_cambio");
            entity.Property(e => e.FechaOperativa).HasColumnName("fecha_operativa");
            entity.Property(e => e.ImpuestoIncluidoTotal)
                .HasPrecision(18, 4)
                .HasColumnName("impuesto_incluido_total");
            entity.Property(e => e.InstalacionId).HasColumnName("instalacion_id");
            entity.Property(e => e.IvaDiscriminadoCompleto).HasColumnName("iva_discriminado_completo");
            entity.Property(e => e.MotivoDescuento).HasColumnName("motivo_descuento");
            entity.Property(e => e.Prefijo)
                .HasMaxLength(10)
                .HasColumnName("prefijo");
            entity.Property(e => e.Serie)
                .HasMaxLength(10)
                .HasColumnName("serie");
            entity.Property(e => e.SesionOperadorId).HasColumnName("sesion_operador_id");
            entity.Property(e => e.SubtotalBruto)
                .HasPrecision(18)
                .HasColumnName("subtotal_bruto");
            entity.Property(e => e.TipoDescuento)
                .HasMaxLength(20)
                .HasColumnName("tipo_descuento");
            entity.Property(e => e.TipoVenta)
                .HasMaxLength(20)
                .HasColumnName("tipo_venta");
            entity.Property(e => e.Total)
                .HasPrecision(18)
                .HasComment("Precio final en COP enteros. El IVA ya esta incluido y nunca se suma nuevamente.")
                .HasColumnName("total");
            entity.Property(e => e.TurnoCajaId).HasColumnName("turno_caja_id");
            entity.Property(e => e.UsuarioId).HasColumnName("usuario_id");
            entity.Property(e => e.ValorDescuentoSolicitado)
                .HasPrecision(18, 4)
                .HasColumnName("valor_descuento_solicitado");
            entity.Property(e => e.Version)
                .HasDefaultValue(1L)
                .HasColumnName("version");

            entity.HasOne(d => d.AutorizacionDescuento).WithMany(p => p.Venta)
                .HasForeignKey(d => d.AutorizacionDescuentoId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_autorizacion_descuento");

            entity.HasOne(d => d.Cliente).WithMany(p => p.Venta)
                .HasForeignKey(d => d.ClienteId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_cliente");

            entity.HasOne(d => d.Instalacion).WithMany(p => p.Venta)
                .HasForeignKey(d => d.InstalacionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_instalacion");

            entity.HasOne(d => d.SesionOperador).WithOne(p => p.Venta)
                .HasForeignKey<Venta>(d => d.SesionOperadorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_sesion_operador");

            entity.HasOne(d => d.TurnoCaja).WithMany(p => p.Venta)
                .HasForeignKey(d => d.TurnoCajaId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_turno_caja");

            entity.HasOne(d => d.Usuario).WithMany(p => p.Venta)
                .HasForeignKey(d => d.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_venta_usuario");
        });

        modelBuilder.Entity<VwNecesidadReposicion>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_necesidad_reposicion", "compras");

            entity.Property(e => e.CantidadSugerida).HasColumnName("cantidad_sugerida");
            entity.Property(e => e.CodigoInterno)
                .HasMaxLength(50)
                .HasColumnName("codigo_interno");
            entity.Property(e => e.EstadoProveedor).HasColumnName("estado_proveedor");
            entity.Property(e => e.PendienteEnPedidos).HasColumnName("pendiente_en_pedidos");
            entity.Property(e => e.Producto)
                .HasMaxLength(200)
                .HasColumnName("producto");
            entity.Property(e => e.ProductoId).HasColumnName("producto_id");
            entity.Property(e => e.Proveedor)
                .HasMaxLength(200)
                .HasColumnName("proveedor");
            entity.Property(e => e.ProveedorId).HasColumnName("proveedor_id");
            entity.Property(e => e.PuedeGenerarPedido).HasColumnName("puede_generar_pedido");
            entity.Property(e => e.StockDisponible).HasColumnName("stock_disponible");
            entity.Property(e => e.StockMinimo).HasColumnName("stock_minimo");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
