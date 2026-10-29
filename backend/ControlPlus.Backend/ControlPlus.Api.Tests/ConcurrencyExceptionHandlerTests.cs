using System.Text.Json;
using ControlPlus.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class ConcurrencyExceptionHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PostgreSqlCheckViolation_IncludingDeferredCommit_BecomesSanitizedResourceConflict(bool wrapped) =>
        AssertSanitizedResourceConflictAsync(wrapped, PostgresErrorCodes.CheckViolation);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PostgreSqlRaiseException_IncludingDeferredCommit_BecomesSanitizedResourceConflict(bool wrapped) =>
        AssertSanitizedResourceConflictAsync(wrapped, "P0001");

    private static async Task AssertSanitizedResourceConflictAsync(bool wrapped, string sqlState)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        using var serviceProvider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        context.Request.Path = "/api/sales";
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        // SaveChanges may wrap this exception; CommitAsync can throw it directly.
        var postgres = new PostgresException("Forced isolated persistence failure",
            "ERROR", "ERROR", sqlState,
            detail: "internal database detail", schemaName: "configuracion", tableName: "documento_emitido",
            constraintName: "tg_rechazar_snapshot_prueba");
        Exception exception = wrapped ? new DbUpdateException("internal EF detail", postgres) : postgres;

        var handled = await new ConcurrencyExceptionHandler().TryHandleAsync(
            context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        responseBody.Position = 0;
        using var json = await JsonDocument.ParseAsync(responseBody);
        var problem = json.RootElement;
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal("resource.conflict", problem.GetProperty("code").GetString());
        Assert.Equal("https://controlplus.local/problems/resource-conflict", problem.GetProperty("type").GetString());
        Assert.Equal("La operación entra en conflicto con el estado actual del recurso.",
            problem.GetProperty("title").GetString());
        Assert.Equal(context.Request.Path.Value, problem.GetProperty("instance").GetString());
        foreach (var detail in new[] { "Forced isolated", "internal", sqlState, "configuracion", "documento_emitido",
                     "tg_rechazar_snapshot_prueba" })
            Assert.DoesNotContain(detail, problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

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
