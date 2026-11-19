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
            [new ProductLabelPrintItem(Guid.NewGuid(), "Cable eléctrico 10 m", 48_000m, "000123456789", 4)], "Tienda Variedades");
        Assert.True(ProductLabelPayloadCodec.TryDecode(content, out var restored));
        var item = Assert.Single(restored!.Items);
        Assert.Equal("000123456789", item.Barcode);
        Assert.Equal(4, item.Quantity);
        Assert.Equal(48_000m, item.RetailPrice);
        Assert.Equal("Tienda Variedades", restored.EstablishmentName);
    }

    [Fact]
    public void Payload_StillAcceptsSavedV1WithoutBusinessName()
    {
        var json = $$"""{"schemaVersion":1,"items":[{"productId":"{{Guid.NewGuid()}}","name":"Cable","retailPrice":1000,"barcode":"000123","quantity":1}]}""";
        Assert.True(ProductLabelPayloadCodec.TryDecode(System.Text.Encoding.UTF8.GetBytes(json), out var payload));
        Assert.Null(payload!.EstablishmentName);
    }
}
