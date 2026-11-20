using Xunit;
using ControlPlus.Infrastructure.Persistence.Repositories;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>Card code (20 November 2026): 14 digits so the barcode fits across the card with 0.5 mm bars.</summary>
public sealed class CredentialTokenGeneratorTests
{
    [Fact]
    public void Generates_fourteen_digit_unique_codes()
    {
        var generator = new Code128CredentialTokenGenerator();
        var tokens = Enumerable.Range(0, 200).Select(_ => generator.Generate()).ToList();
        Assert.All(tokens, token => Assert.Matches("^[0-9]{14}$", token));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }
}
