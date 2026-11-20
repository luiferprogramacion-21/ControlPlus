using Xunit;
using ControlPlus.Infrastructure.Persistence.Repositories;

namespace ControlPlus.Infrastructure.Tests;

/// <summary>Card code (19 November 2026): 40 digits so the barcode fits the 80 mm receipt roll with 0.25 mm bars.</summary>
public sealed class CredentialTokenGeneratorTests
{
    [Fact]
    public void Generates_forty_digit_unique_codes()
    {
        var generator = new Code128CredentialTokenGenerator();
        var tokens = Enumerable.Range(0, 200).Select(_ => generator.Generate()).ToList();
        Assert.All(tokens, token => Assert.Matches("^[0-9]{40}$", token));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }
}
