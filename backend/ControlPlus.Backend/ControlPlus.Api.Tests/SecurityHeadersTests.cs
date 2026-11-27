using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SecurityHeadersTests
{
    [Fact]
    public async Task Responses_IncludeBaselineBrowserSecurityHeaders()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "security-headers-tests");
            builder.UseSetting("Jwt:Audience", "security-headers-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var healthResponse = await client.GetAsync("/api/health");
        using var notFoundResponse = await client.GetAsync("/api/route-that-does-not-exist");

        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, notFoundResponse.StatusCode);
        AssertSecurityHeaders(healthResponse);
        AssertSecurityHeaders(notFoundResponse);
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        AssertHeader(response, "X-Content-Type-Options", "nosniff");
        AssertHeader(response, "X-Frame-Options", "DENY");
        AssertHeader(response, "Referrer-Policy", "no-referrer");
        AssertHeader(response, "Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    }

    private static void AssertHeader(HttpResponseMessage response, string name, string expected) =>
        Assert.Equal([expected], response.Headers.GetValues(name));
}
