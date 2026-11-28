using System.Net;
using System.Text;
using ControlPlus.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>
/// Auditoría ZAP del 27/11/2026 (ZAP-ACT-001): un usuario con el carácter nulo en el inicio de sesión llegaba a
/// PostgreSQL y respondía 500. Ahora se rechaza con 400 en la entrada, sin tocar la base (aquí no hay base real).
/// </summary>
public sealed class NullCharacterRequestTests
{
    [Fact]
    public async Task NullCharacter_InJsonBodyOrQuery_IsRejectedWithBadRequest()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ControlPlusDb", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Installation:MasterKey", new string('m', 32));
            builder.UseSetting("Jwt:Issuer", "null-character-tests");
            builder.UseSetting("Jwt:Audience", "null-character-client");
            builder.UseSetting("Jwt:SigningKey", new string('j', 64));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var body = new StringContent("{\"userName\":\"admin\\u0000x\",\"password\":\"1234\"}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync("/api/auth/login", body);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal("application/problem+json", login.Content.Headers.ContentType?.MediaType);
        Assert.Contains("validation.failed", await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var query = await client.GetAsync("/api/health?buscar=a%00b");
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Contains("validation.failed", await query.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var normal = await client.GetAsync("/api/health?buscar=normal");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
    }

    [Fact]
    public void Converter_RejectsOnlyTextWithTheNullCharacter()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new RejectNullCharacterStringConverter());

        Assert.Equal("Señor ñandú", System.Text.Json.JsonSerializer.Deserialize<string>("\"Señor ñandú\"", options));
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<string>("null", options));
        var exception = Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<string>("\"a\\u0000b\"", options));
        Assert.Equal(RejectNullCharacterStringConverter.Message, exception.Message);
    }
}
