using System.Text.Json;
using ControlPlus.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class ConcurrencyExceptionHandlerTests
{
    [Fact]
    public async Task DbUpdateConcurrencyException_BecomesUniformConflictProblemDetails()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        using var serviceProvider = services.BuildServiceProvider();
        context.RequestServices = serviceProvider;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(
            context,
            new DbUpdateConcurrencyException("internal database detail"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);

        responseBody.Position = 0;
        using var json = await JsonDocument.ParseAsync(responseBody);
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("concurrency.conflict", json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("internal database detail", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonConcurrencyException_IsNotHandled()
    {
        var context = new DefaultHttpContext();

        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(
            context,
            new InvalidOperationException("not a concurrency error"),
            CancellationToken.None);

        Assert.False(handled);
    }
}
