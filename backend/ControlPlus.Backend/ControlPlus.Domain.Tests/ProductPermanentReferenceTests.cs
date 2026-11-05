using ControlPlus.Domain.Common;
using ControlPlus.Domain.OfficialModel;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class ProductPermanentReferenceTests
{
    [Theory]
    [InlineData(true, "P-000001", "CODE128")]
    [InlineData(false, "0000123456789", null)]
    public void Edit_PreservesReferenceAndBarcodeMetadata(bool generated, string barcode, string? format)
    {
        var category = Guid.CreateVersion7(); var unit = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var product = Producto.Create(category, unit, null, "P-000001", barcode, format, generated,
            "Original", null, 100, null, null, 0, actor, DateTimeOffset.UtcNow);
        product.Update(category, unit, null, "P-000001", barcode, format, generated,
            "Editado", null, 200, null, null, 0, actor, DateTimeOffset.UtcNow);
        Assert.Equal("P-000001", product.CodigoInterno);
        Assert.Equal(barcode, product.CodigoBarras);
        Assert.Equal(format, product.FormatoCodigoBarras);
        Assert.Throws<DomainRuleViolationException>(() => product.Update(category, unit, null, "P-000002",
            barcode, format, generated, "Otro", null, 100, null, null, 0, actor, DateTimeOffset.UtcNow));
        Assert.Equal("Editado", product.Nombre);
    }
}
