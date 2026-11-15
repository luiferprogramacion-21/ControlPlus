using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Common;

/// <summary>
/// Reason catalogs approved for each operation. They are created automatically when the installation is
/// set up and, if any is missing, the first time the catalog is read; the explicit initialize endpoints
/// remain only as an idempotent repair. A code that already exists is never modified.
/// </summary>
public static class ApprovedReasonCatalogs
{
    public const string SaleCancellationType = "ANULACION_VENTA";
    public const string SalePaymentCancellationType = "ANULACION_PAGO";
    public const string PurchaseCancellationType = "ANULACION_COMPRA";
    public const string PurchaseOrderCloseType = "CIERRE_PEDIDO_PENDIENTE";
    public const string PurchaseOrderCancelType = "CANCELACION_PEDIDO";

    public static readonly (string Code, string Name)[] SaleCancellation =
    [
        ("ERROR_REGISTRO", "Error de registro"),
        ("SOLICITUD_CLIENTE", "Solicitud del cliente"),
        ("OTRO", "Otro")
    ];

    public static readonly (string Code, string Name)[] PurchaseCancellation =
    [
        ("ERROR_REGISTRO", "Error de registro"),
        ("COMPRA_DUPLICADA", "Compra duplicada"),
        ("DOCUMENTO_PROVEEDOR_ANULADO", "Documento de proveedor anulado"),
        ("MERCANCIA_NO_RECIBIDA", "Mercancía no recibida"),
        ("OTRO", "Otro")
    ];

    public static readonly (string Code, string Name)[] PurchaseOrderClose =
    [
        ("PROVEEDOR_NO_ENTREGARA", "Proveedor no entregará el pendiente"),
        ("PENDIENTE_NO_REQUERIDO", "Pendiente ya no requerido"),
        ("OTRO", "Otro")
    ];

    public static readonly (string Code, string Name)[] PurchaseOrderCancel =
    [
        ("ERROR_PEDIDO", "Error en el pedido"),
        ("PROVEEDOR_NO_ATIENDE", "Proveedor no atiende el pedido"),
        ("OTRO", "Otro")
    ];

    public static readonly (string Code, string Name)[] CashClosureDifference =
    [
        ("FALTANTE_EFECTIVO", "Faltante de efectivo"),
        ("SOBRANTE_EFECTIVO", "Sobrante de efectivo"),
        ("ERROR_DE_CONTEO", "Error de conteo"),
        ("OTRO", "Otro")
    ];

    /// <summary>
    /// Every approved catalog created when the installation is set up, with the operation type its reasons are
    /// stored under. The cash closure-difference reasons are not here: the cash screen and the closing flow
    /// already create them on demand, so nobody has to configure them.
    /// </summary>
    public static readonly IReadOnlyList<(string OperationType, (string Code, string Name)[] Entries)> All =
    [
        (SaleCancellationType, SaleCancellation),
        (SalePaymentCancellationType, SaleCancellation),
        (PurchaseCancellationType, PurchaseCancellation),
        (PurchaseOrderCloseType, PurchaseOrderClose),
        (PurchaseOrderCancelType, PurchaseOrderCancel)
    ];

    /// <summary>Builds the approved reason rows of one catalog exactly as the explicit initialization does.</summary>
    public static MotivoOperacion Build(Guid establishmentId, string operationType,
        (string Code, string Name)[] entries, int index, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(), EstablecimientoId = establishmentId, TipoOperacion = operationType,
        Codigo = entries[index].Code, Nombre = entries[index].Name, Descripcion = entries[index].Name,
        OrdenVisual = (index + 1) * 10, Activo = true, FechaCreacion = now, Version = 1
    };

    /// <summary>Builds every row of every approved catalog (used when the installation is created).</summary>
    public static IReadOnlyList<MotivoOperacion> BuildAll(Guid establishmentId, DateTime now)
    {
        var rows = new List<MotivoOperacion>();
        foreach (var (operationType, entries) in All)
            for (var index = 0; index < entries.Length; index++)
                rows.Add(Build(establishmentId, operationType, entries, index, now));
        return rows;
    }
}
