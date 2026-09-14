using ControlPlus.Domain.Common;

namespace ControlPlus.Domain.OfficialModel;

public partial class Categoria
{
    public static Categoria Create(string name, string? description, Guid actorUserId, DateTimeOffset createdAtUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        Nombre = DomainGuard.RequiredText(name, nameof(name)),
        Descripcion = NormalizeOptional(description),
        Activo = true,
        UsuarioCreacionId = actorUserId,
        FechaCreacion = createdAtUtc.UtcDateTime,
        Version = 1
    };

    public void Update(string name, string? description, Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        Nombre = DomainGuard.RequiredText(name, nameof(name));
        Descripcion = NormalizeOptional(description);
        Touch(actorUserId, changedAtUtc);
    }

    public void Activate(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        Activo = true;
        Touch(actorUserId, changedAtUtc);
    }

    public void Deactivate(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        Activo = false;
        Touch(actorUserId, changedAtUtc);
    }

    private void Touch(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        UsuarioModificacionId = actorUserId;
        FechaModificacion = changedAtUtc.UtcDateTime;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public partial class Producto
{
    public int StockDisponible => StockActual - StockReservado;

    public bool Agotado => StockDisponible <= 0;

    public static Producto Create(
        Guid categoryId,
        Guid measurementUnitId,
        Guid? supplierId,
        string internalCode,
        string? barcode,
        string? barcodeFormat,
        bool barcodeGenerated,
        string name,
        string? description,
        decimal retailPrice,
        decimal? wholesalePrice,
        decimal? taxPercentage,
        int minimumStock,
        Guid actorUserId,
        DateTimeOffset createdAtUtc) => new()
    {
        Id = Guid.CreateVersion7(),
        CategoriaId = categoryId,
        UnidadMedidaId = measurementUnitId,
        ProveedorId = supplierId,
        CodigoInterno = DomainGuard.NormalizeCode(internalCode, nameof(internalCode)),
        CodigoBarras = NormalizeOptional(barcode),
        FormatoCodigoBarras = NormalizeOptional(barcodeFormat)?.ToUpperInvariant(),
        CodigoBarrasGenerado = barcodeGenerated,
        Nombre = DomainGuard.RequiredText(name, nameof(name)),
        Descripcion = NormalizeOptional(description),
        PrecioMinorista = retailPrice,
        PrecioMayorista = wholesalePrice,
        PorcentajeIva = taxPercentage,
        StockActual = 0,
        StockReservado = 0,
        StockMinimo = minimumStock,
        Activo = true,
        UsuarioCreacionId = actorUserId,
        FechaCreacion = createdAtUtc.UtcDateTime,
        Version = 1
    };

    public void Update(
        Guid categoryId,
        Guid measurementUnitId,
        Guid? supplierId,
        string internalCode,
        string? barcode,
        string? barcodeFormat,
        bool barcodeGenerated,
        string name,
        string? description,
        decimal retailPrice,
        decimal? wholesalePrice,
        decimal? taxPercentage,
        int minimumStock,
        Guid actorUserId,
        DateTimeOffset changedAtUtc)
    {
        CategoriaId = categoryId;
        UnidadMedidaId = measurementUnitId;
        ProveedorId = supplierId;
        CodigoInterno = DomainGuard.NormalizeCode(internalCode, nameof(internalCode));
        CodigoBarras = NormalizeOptional(barcode);
        FormatoCodigoBarras = NormalizeOptional(barcodeFormat)?.ToUpperInvariant();
        CodigoBarrasGenerado = barcodeGenerated;
        Nombre = DomainGuard.RequiredText(name, nameof(name));
        Descripcion = NormalizeOptional(description);
        PrecioMinorista = retailPrice;
        PrecioMayorista = wholesalePrice;
        PorcentajeIva = taxPercentage;
        StockMinimo = minimumStock;
        Touch(actorUserId, changedAtUtc);
    }

    public void Activate(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        Activo = true;
        Touch(actorUserId, changedAtUtc);
    }

    public void Deactivate(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        Activo = false;
        Touch(actorUserId, changedAtUtc);
    }

    private void Touch(Guid actorUserId, DateTimeOffset changedAtUtc)
    {
        UsuarioModificacionId = actorUserId;
        FechaModificacion = changedAtUtc.UtcDateTime;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
