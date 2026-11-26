using ControlPlus.Application.Security.Services;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Correo tapado a medias al pedir el código de recuperación (20 de noviembre de 2026).</summary>
public sealed class PasswordMaskTests
{
    [Theory]
    [InlineData("camilarojas21@gmail.com", "ca•••21@gm•••.com")]
    [InlineData("ana@hotmail.es", "a•••@ho•••.es")]
    [InlineData("pedro.p@x.co", "pe•••.p@x•••.co")]
    [InlineData("sin-arroba", "•••")]
    public void MaskEmail_ShowsOnlyTheStartAndEnd(string email, string expected) =>
        Assert.Equal(expected, PasswordRecoveryService.MaskEmail(email));
}
