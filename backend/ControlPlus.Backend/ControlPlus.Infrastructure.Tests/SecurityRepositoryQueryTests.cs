using System.Data;
using System.Data.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class SecurityRepositoryQueryTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("role")]
    [InlineData("override")]
    [InlineData("list")]
    public async Task UserReads_LoadRoleSummaryAndOverridesWithoutMultiplyingRolePermissions(string read)
    {
        var commands = new QueryCapture();
        await using var context = CreateContext(commands);
        var repository = new EfUserRepository(context);
        var id = Guid.NewGuid();

        switch (read)
        {
            case "id": Assert.Null(await repository.GetByIdAsync(id)); break;
            case "name": Assert.Null(await repository.GetByUserNameAsync("  cashier  ")); break;
            case "role": Assert.Empty(await repository.GetByRoleIdAsync(id)); break;
            case "override": Assert.Empty(await repository.GetByPermissionOverrideIdAsync(id)); break;
            case "list": Assert.Empty((await repository.ListAsync(new UserListQuery(Page: 2, PageSize: 10))).Items); break;
        }

        Assert.Equal(read == "list" ? 2 : 1, commands.Sql.Count);
        var sql = commands.Sql[^1];
        Assert.Contains("seguridad.rol", sql);
        Assert.Contains("seguridad.usuario_permiso", sql);
        Assert.DoesNotContain("seguridad.rol_permiso", sql);
        if (read == "name") Assert.Contains("CASHIER", commands.ParameterValues);
        if (read == "list")
        {
            Assert.Contains("LIMIT", sql);
            Assert.Contains("OFFSET", sql);
            Assert.Contains("nombre_usuario,", sql);
        }
    }

    [Fact]
    public async Task Authorization_ProjectsOnePermissionCollectionInOneStatementWithoutPasswordData()
    {
        var commands = new QueryCapture();
        await using var context = CreateContext(commands);

        Assert.Null(await new EfUserRepository(context).GetAuthorizationSnapshotAsync(Guid.NewGuid()));

        var sql = Assert.Single(commands.Sql);
        Assert.Contains("sello_seguridad", sql);
        Assert.Contains("bloqueo_hasta IS NULL", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("CONCEDER", sql);
        Assert.Contains("REVOCAR", sql);
        Assert.Contains("seguridad.rol_permiso", sql);
        Assert.DoesNotContain("password_hash", sql);
        Assert.DoesNotContain("JOIN seguridad.rol_permiso", sql);
        Assert.DoesNotContain("JOIN seguridad.usuario_permiso", sql);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static OfficialControlPlusDbContext CreateContext(QueryCapture capture) => new(
        new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=controlplus_test_query_shape;Username=test;Password=test")
            // This is a query translation test, not a database test. Opening is
            // always suppressed and every command receives an in-memory reader.
            .AddInterceptors(new NoConnection(), capture)
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options);

    private sealed class NoConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionOpening(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result) => InterceptionResult.Suppress();
    }

    private sealed class QueryCapture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?> ParameterValues { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            ParameterValues.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
            var table = new DataTable();
            if (command.CommandText.StartsWith("SELECT count(*)", StringComparison.Ordinal))
            {
                table.Columns.Add("count", typeof(int));
                table.Rows.Add(0);
            }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
