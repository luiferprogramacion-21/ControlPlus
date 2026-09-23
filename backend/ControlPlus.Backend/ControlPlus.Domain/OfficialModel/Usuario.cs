using System;
using System.Collections.Generic;

namespace ControlPlus.Domain.OfficialModel;

public partial class Usuario
{
    public Guid Id { get; set; }

    public string NombreUsuario { get; set; } = null!;

    public string NombreUsuarioNormalizado { get; set; } = null!;

    public string NombreCompleto { get; set; } = null!;

    public string? Correo { get; set; }

    public string PasswordHash { get; set; } = null!;

    public string SelloSeguridad { get; set; } = null!;

    public string SelloConcurrencia { get; set; } = null!;

    public bool Activo { get; set; }

    public int IntentosFallidos { get; set; }

    public DateTime? BloqueoHasta { get; set; }

    public DateTime? UltimoAcceso { get; set; }

    public DateTime FechaCreacion { get; set; }

    public DateTime? FechaModificacion { get; set; }

    public long Version { get; set; }

    public virtual ICollection<AjusteCreditoCambio> AjusteCreditoCambio { get; set; } = new List<AjusteCreditoCambio>();

    public virtual ICollection<AnulacionVenta> AnulacionVentaUsuarioAutorizador { get; set; } = new List<AnulacionVenta>();

    public virtual ICollection<AnulacionVenta> AnulacionVentaUsuarioSolicitante { get; set; } = new List<AnulacionVenta>();

    public virtual ICollection<Apartado> ApartadoUsuario { get; set; } = new List<Apartado>();

    public virtual ICollection<Apartado> ApartadoUsuarioAnulacion { get; set; } = new List<Apartado>();

    public virtual ICollection<Apartado> ApartadoUsuarioEntrega { get; set; } = new List<Apartado>();

    public virtual ICollection<AutorizacionOperacion> AutorizacionOperacionUsuarioAutorizador { get; set; } = new List<AutorizacionOperacion>();

    public virtual ICollection<AutorizacionOperacion> AutorizacionOperacionUsuarioSolicitante { get; set; } = new List<AutorizacionOperacion>();

    public virtual ICollection<BorradorVenta> BorradorVenta { get; set; } = new List<BorradorVenta>();

    public virtual ICollection<CambioVenta> CambioVentaUsuario { get; set; } = new List<CambioVenta>();

    public virtual ICollection<CambioVenta> CambioVentaUsuarioAnulacion { get; set; } = new List<CambioVenta>();

    public virtual ICollection<Categoria> CategoriaUsuarioCreacion { get; set; } = new List<Categoria>();

    public virtual ICollection<Categoria> CategoriaUsuarioModificacion { get; set; } = new List<Categoria>();

    public virtual ICollection<Cliente> ClienteUsuarioCreacion { get; set; } = new List<Cliente>();

    public virtual ICollection<Cliente> ClienteUsuarioModificacion { get; set; } = new List<Cliente>();

    public virtual ICollection<Compra> CompraUsuario { get; set; } = new List<Compra>();

    public virtual ICollection<Compra> CompraUsuarioAnulacion { get; set; } = new List<Compra>();

    public virtual ICollection<CredencialUsuario> CredencialUsuarioEmitidaPor { get; set; } = new List<CredencialUsuario>();

    public virtual ICollection<CredencialUsuario> CredencialUsuarioRevocadaPor { get; set; } = new List<CredencialUsuario>();

    public virtual ICollection<CredencialUsuario> CredencialUsuarioUsuario { get; set; } = new List<CredencialUsuario>();

    public virtual ICollection<DocumentoEmitido> DocumentoEmitido { get; set; } = new List<DocumentoEmitido>();

    public virtual ICollection<EventoAuditoria> EventoAuditoriaUsuario { get; set; } = new List<EventoAuditoria>();

    public virtual ICollection<EventoAuditoria> EventoAuditoriaUsuarioAutorizador { get; set; } = new List<EventoAuditoria>();

    public virtual ICollection<MovimientoCaja> MovimientoCaja { get; set; } = new List<MovimientoCaja>();

    public virtual ICollection<MovimientoInventario> MovimientoInventario { get; set; } = new List<MovimientoInventario>();

    public virtual ICollection<OperacionIdempotente> OperacionIdempotente { get; set; } = new List<OperacionIdempotente>();

    public virtual ICollection<PagoApartado> PagoApartadoUsuario { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoApartado> PagoApartadoUsuarioAnulacion { get; set; } = new List<PagoApartado>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVentaUsuario { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCambioVenta> PagoCambioVentaUsuarioAnulacion { get; set; } = new List<PagoCambioVenta>();

    public virtual ICollection<PagoCredito> PagoCreditoUsuario { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoCredito> PagoCreditoUsuarioAnulacion { get; set; } = new List<PagoCredito>();

    public virtual ICollection<PagoVenta> PagoVentaUsuario { get; set; } = new List<PagoVenta>();

    public virtual ICollection<PagoVenta> PagoVentaUsuarioAnulacion { get; set; } = new List<PagoVenta>();

    public virtual ICollection<PedidoCompra> PedidoCompraUsuarioCancelacion { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<PedidoCompra> PedidoCompraUsuarioCierre { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<PedidoCompra> PedidoCompraUsuarioCreacion { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<PedidoCompra> PedidoCompraUsuarioEnvio { get; set; } = new List<PedidoCompra>();

    public virtual ICollection<Producto> ProductoUsuarioCreacion { get; set; } = new List<Producto>();

    public virtual ICollection<Producto> ProductoUsuarioModificacion { get; set; } = new List<Producto>();

    public virtual ICollection<Proveedor> ProveedorUsuarioCreacion { get; set; } = new List<Proveedor>();

    public virtual ICollection<Proveedor> ProveedorUsuarioModificacion { get; set; } = new List<Proveedor>();

    public virtual ICollection<PruebaRestauracion> PruebaRestauracion { get; set; } = new List<PruebaRestauracion>();

    public virtual ICollection<ReembolsoApartado> ReembolsoApartado { get; set; } = new List<ReembolsoApartado>();

    public virtual ICollection<SesionOperador> SesionOperador { get; set; } = new List<SesionOperador>();

    public virtual ICollection<TrabajoImpresion> TrabajoImpresion { get; set; } = new List<TrabajoImpresion>();

    public virtual ICollection<TurnoCaja> TurnoCajaUsuarioApertura { get; set; } = new List<TurnoCaja>();

    public virtual ICollection<TurnoCaja> TurnoCajaUsuarioCierre { get; set; } = new List<TurnoCaja>();

    public virtual ICollection<TurnoCaja> TurnoCajaUsuarioResponsable { get; set; } = new List<TurnoCaja>();

    public virtual ICollection<UsuarioClaim> UsuarioClaim { get; set; } = new List<UsuarioClaim>();

    public virtual ICollection<UsuarioLogin> UsuarioLogin { get; set; } = new List<UsuarioLogin>();

    public virtual ICollection<UsuarioRol> UsuarioRolAsignadoPor { get; set; } = new List<UsuarioRol>();

    public virtual UsuarioRol? UsuarioRolUsuario { get; set; }

    public virtual ICollection<UsuarioToken> UsuarioToken { get; set; } = new List<UsuarioToken>();

    public virtual ICollection<UsuarioPermiso> UsuarioPermiso { get; set; } = new List<UsuarioPermiso>();

    public virtual ICollection<UsuarioPermiso> UsuarioPermisoAsignadoPor { get; set; } = new List<UsuarioPermiso>();

    public virtual ICollection<Venta> Venta { get; set; } = new List<Venta>();
}
