using ControlPlus.Application.Printing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class ProductLabelPrintingTests
{
    [Fact]
    public void Code128_UsesSubsetCForEvenDigitsAndProducesValidModuleWidth()
    {
        Assert.Equal([105, 12, 34, 56, 44, 106], Code128Encoder.Values("123456"));
        var widths = Code128Encoder.Widths("PJJ123C");
        Assert.All(widths, width => Assert.InRange(width, 1, 4));
        Assert.False(Code128Encoder.CanEncode("Código con ñ"));
    }

    [Fact]
    public void Payload_RoundTripsWithoutChangingLeadingZerosOrQuantities()
    {
        var content = ProductLabelPayloadCodec.Encode(
            [new ProductLabelPrintItem(Guid.NewGuid(), "Cable eléctrico 10 m", 48_000m, "000123456789", 4)]);
        Assert.True(ProductLabelPayloadCodec.TryDecode(content, out var restored));
        var item = Assert.Single(restored!.Items);
        Assert.Equal("000123456789", item.Barcode);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(48_000m, item.RetailPrice);
    }
}
