using System.Net.Mime;
using System.Text;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Infrastructure.Email;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class SecurityEmailPresentationTests
{
    private static readonly EmailOptions Options = new() { FromAddress = "controlplus@example.test", FromName = "ControlPlus" };

    [Fact]
    public void VerificationEmail_HasPlainTextAlternativeAndEmbeddedPngLogo()
    {
        var content = new EmailVerificationCode("Cambia tu contraseña", "Persona de prueba", "operador.prueba", "042681", 10,
            "Escribe este código en la pantalla de recuperación para elegir tu nueva contraseña.");
        const string fallback = "Tu código es 042681. Vale 10 minutos y sirve una sola vez.";
        using var mail = SecurityEmailPresentation.Create(new EmailMessage("operador@example.test", "Código de ControlPlus", fallback, content), Options);

        Assert.Equal(2, mail.AlternateViews.Count);
        Assert.Equal(MediaTypeNames.Text.Plain, mail.AlternateViews[0].ContentType.MediaType);
        using var plain = new StreamReader(mail.AlternateViews[0].ContentStream, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(fallback, plain.ReadToEnd());
        var htmlView = mail.AlternateViews[1];
        Assert.Equal(MediaTypeNames.Text.Html, htmlView.ContentType.MediaType);
        using var htmlReader = new StreamReader(htmlView.ContentStream, Encoding.UTF8, leaveOpen: true);
        var html = htmlReader.ReadToEnd();
        Assert.Contains("042681", html);
        Assert.Contains("10 minutos", html);
        Assert.Contains("font-size:40px", html);
        Assert.Contains("#0F2C4D", html);
        Assert.Contains("#FD6D32", html);
        Assert.Contains("cid:" + SecurityEmailPresentation.LogoContentId, html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("http://", html);
        var image = Assert.Single(htmlView.LinkedResources);
        Assert.Equal(SecurityEmailPresentation.LogoContentId, image.ContentId);
        Assert.Equal(MediaTypeNames.Image.Png, image.ContentType.MediaType);
        using var bytes = new MemoryStream();
        image.ContentStream.CopyTo(bytes);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.ToArray()[..8]);

        // Artefacto seguro para inspección visual: identidad ficticia y código de ejemplo, sin SMTP ni base de datos.
        var preview = html.Replace("cid:" + SecurityEmailPresentation.LogoContentId,
            "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray()), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "password-recovery-email-preview.html"), preview, Encoding.UTF8);
    }

    [Fact]
    public void Html_EncodesEveryDynamicValue()
    {
        var html = SecurityEmailPresentation.Render(new EmailVerificationCode("Cambio <script>", "Ana <img src=x>", "user&name",
            "<123456>", 10, "Paso <b>uno</b> & dos"));
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.DoesNotContain("<b>uno</b>", html);
        Assert.Contains("user&amp;name", html);
        Assert.Contains("&lt;123456&gt;", html);
    }

    [Fact]
    public void PlainMessage_PreservesItsBodyWithoutAddingHtmlOrImages()
    {
        using var mail = SecurityEmailPresentation.Create(new EmailMessage("persona@example.test", "Aviso", "Mensaje de texto."), Options);
        Assert.Equal("Mensaje de texto.", mail.Body);
        Assert.False(mail.IsBodyHtml);
        Assert.Empty(mail.AlternateViews);
        Assert.Empty(mail.Attachments);
    }
}
