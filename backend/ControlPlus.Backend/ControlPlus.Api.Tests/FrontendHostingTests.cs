using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Instalada como servicio, la API sirve la pantalla compilada desde wwwroot (decisión del 15 de noviembre de 2026).</summary>
public sealed class FrontendHostingTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "controlplus-webroot-" + Guid.NewGuid().ToString("N"));

    public FrontendHostingTests()
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>ControlPlus pantalla</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "main.js"), "console.log('controlplus');");
    }

    public void Dispose() { if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, true); }

    private WebApplicationFactory<Program> Create(bool withFrontend) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Port=1;Database=controlplus_test_frontend;Username=test;Password=test");
        builder.UseSetting("Installation:MasterKey", new string('m', 32));
        builder.UseSetting("Jwt:Issuer", "controlplus-frontend-tests");
        builder.UseSetting("Jwt:Audience", "controlplus-frontend-client");
        builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        if (withFrontend) builder.UseWebRoot(_webRoot);
    });

    [Theory]
    [InlineData("/")]
    [InlineData("/operacion/dashboard")]
    [InlineData("/operacion/soporte/restauracion")]
    public async Task ScreenRoutes_ReturnTheCompiledScreen(string path)
    {
        using var factory = Create(withFrontend: true);
        using var client = factory.CreateClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ControlPlus pantalla", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StaticFiles_AreServed_AndUnknownApiRoutesNeverReturnTheScreen()
    {
        using var factory = Create(withFrontend: true);
        using var client = factory.CreateClient();
        Assert.Contains("controlplus", await client.GetStringAsync("/main.js"));
        // Una ruta de /api que no existe nunca devuelve la pantalla (la API responde como siempre: pide sesión).
        foreach (var path in new[] { "/api/no-existe", "/api" })
        {
            var response = await client.GetAsync(path);
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("ControlPlus pantalla", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task WithoutCompiledScreen_NothingIsServedOutsideTheApi()
    {
        using var factory = Create(withFrontend: false);
        using var client = factory.CreateClient();
        // Sin pantalla compilada (desarrollo con ng serve) la API no responde la pantalla: exige sesión como siempre.
        var response = await client.GetAsync("/operacion/dashboard");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("ControlPlus pantalla", await response.Content.ReadAsStringAsync());
    }
}
