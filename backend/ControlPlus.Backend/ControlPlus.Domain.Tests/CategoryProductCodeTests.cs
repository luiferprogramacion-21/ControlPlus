using ControlPlus.Domain.OfficialModel;
using Xunit;

namespace ControlPlus.Domain.Tests;

public sealed class CategoryProductCodeTests
{
    [Theory]
    [InlineData("pap", "PAP")]
    [InlineData(" Jug ", "JUG")]
    public void AbbreviationIsNormalizedOnce(string input, string expected) =>
        Assert.Equal(expected, Categoria.NormalizeAbbreviation(input));

    [Theory]
    [InlineData("PAP-000001", "CP000001")]
    [InlineData("JUG-1000000", "CP1000000")]
    public void OwnedBarcodeDoesNotContainCategoryPrefix(string reference, string expected) =>
        Assert.Equal(expected, ProductCode.GenerateOwnedBarcode(reference));
}
