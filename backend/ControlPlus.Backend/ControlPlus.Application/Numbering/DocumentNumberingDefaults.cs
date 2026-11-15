namespace ControlPlus.Application.Numbering;

/// <summary>
/// Document types that carry a visible consecutive, with the fixed prefix each one starts with.
/// The backend creates these rows by itself (installation and first use), so the Administrator never
/// has to configure them; ventas (<c>VTA</c>) can still be adjusted before the first confirmed sale.
/// </summary>
public static class DocumentNumberingDefaults
{
    public const string Sales = "VENTA";
    public const string CreditPayments = "PAGO_CREDITO";
    public const string Layaways = "APARTADO";
    public const string LayawayPayments = "PAGO_APARTADO";
    public const string Purchases = "COMPRA";
    public const string SaleChanges = "CAMBIO_VENTA";
    public const string PurchaseOrders = "PEDIDO_COMPRA";

    /// <summary>Every document type with the prefix its consecutive is created with, starting at number 1.</summary>
    public static readonly IReadOnlyList<(string DocumentType, string Prefix)> All =
    [
        (Sales, "VTA"),
        (CreditPayments, "ABO"),
        (Layaways, "APT"),
        (LayawayPayments, "PAP"),
        (Purchases, "CMP"),
        (SaleChanges, "CAM"),
        (PurchaseOrders, "PED")
    ];

    public static string PrefixFor(string documentType)
    {
        foreach (var (type, prefix) in All)
            if (type == documentType) return prefix;
        throw new ArgumentOutOfRangeException(nameof(documentType), documentType,
            "The document type has no default consecutive.");
    }
}
