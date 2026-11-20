using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ControlPlus.Infrastructure.Persistence.Official;
using ControlPlus.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class SaleGraphQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadGraph_UsesOneOwnedSnapshotAndAvoidsMultiplyingCollections(bool changes)
    {
        await using var connection = new RecordingConnection();
        await using var context = CreateContext(connection);

        await ReadAsync(context, changes);

        var transaction = Assert.Single(connection.Transactions);
        Assert.Equal(IsolationLevel.RepeatableRead, transaction.IsolationLevel);
        Assert.True(transaction.Committed);
        Assert.True(transaction.Disposed);
        Assert.Null(context.Database.CurrentTransaction);
        var sql = Assert.Single(connection.Commands);
        Assert.DoesNotContain(changes ? "detalle_cambio_venta" : "detalle_venta", sql);
        Assert.DoesNotContain(changes ? "pago_cambio_venta" : "pago_venta", sql);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted, false)]
    [InlineData(IsolationLevel.RepeatableRead, true)]
    [InlineData(IsolationLevel.Serializable, true)]
    public async Task ReadGraph_PreservesCallerTransactionAndOnlySplitsStableSnapshots(
        IsolationLevel isolationLevel, bool split)
    {
        await using var connection = new RecordingConnection();
        await using var context = CreateContext(connection);
        await using var transaction = await context.Database.BeginTransactionAsync(isolationLevel);

        await ReadAsync(context, false);
        await ReadAsync(context, true);

        var recorded = Assert.Single(connection.Transactions);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.False(recorded.Committed);
        Assert.False(recorded.Disposed);
        Assert.Equal(2, connection.Commands.Count);
        Assert.Equal(!split, connection.Commands[0].Contains("detalle_venta", StringComparison.Ordinal));
        Assert.Equal(!split, connection.Commands[0].Contains("pago_venta", StringComparison.Ordinal));
        Assert.Equal(!split, connection.Commands[1].Contains("detalle_cambio_venta", StringComparison.Ordinal));
        Assert.Equal(!split, connection.Commands[1].Contains("pago_cambio_venta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadGraph_FailureDisposesOwnedTransactionWithoutCommitting()
    {
        await using var connection = new RecordingConnection { FailCommands = true };
        await using var context = CreateContext(connection);

        await Assert.ThrowsAsync<QueryFailureException>(() => ReadAsync(context, false));

        var transaction = Assert.Single(connection.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(System.Transactions.IsolationLevel.ReadCommitted, false)]
    [InlineData(System.Transactions.IsolationLevel.RepeatableRead, true)]
    public async Task ReadGraph_RespectsAmbientTransactionWithoutStartingAnother(
        System.Transactions.IsolationLevel isolationLevel, bool split)
    {
        await using var connection = new RecordingConnection();
        await using var context = CreateContext(connection);
        using var scope = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeOption.Required,
            new System.Transactions.TransactionOptions { IsolationLevel = isolationLevel },
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

        await ReadAsync(context, false);

        Assert.Empty(connection.Transactions);
        Assert.Equal(!split, Assert.Single(connection.Commands).Contains("detalle_venta", StringComparison.Ordinal));
        Assert.Equal(System.Transactions.TransactionStatus.Active,
            System.Transactions.Transaction.Current!.TransactionInformation.Status);
        scope.Complete();
    }

    [Fact]
    public async Task ReadGraph_CancelledCallDoesNotOpenConnectionOrTransaction()
    {
        await using var connection = new RecordingConnection();
        await using var context = CreateContext(connection);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfSalesRepository(context).GetSaleAsync(Guid.NewGuid(), cancellation.Token));

        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Empty(connection.Transactions);
        Assert.Empty(connection.Commands);
    }

    private static OfficialControlPlusDbContext CreateContext(RecordingConnection connection) =>
        new(new DbContextOptionsBuilder<OfficialControlPlusDbContext>()
            .UseNpgsql(connection)
            .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options);

    private static async Task ReadAsync(OfficialControlPlusDbContext context, bool changes)
    {
        var repository = new EfSalesRepository(context);
        if (changes) Assert.Empty(await repository.GetChangesAsync(Guid.NewGuid()));
        else Assert.Null(await repository.GetSaleAsync(Guid.NewGuid()));
    }

    // Real Npgsql query translation with a transport that cannot reach any database.
    // Empty results exercise query execution and transaction ownership, without fixtures.
    private sealed class RecordingConnection : DbConnection
    {
        private ConnectionState _state;
        public List<string> Commands { get; } = [];
        public List<RecordingTransaction> Transactions { get; } = [];
        public bool FailCommands { get; init; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "query_shape_test";
        public override string DataSource => "in-memory-recording-transport";
        public override string ServerVersion => "18.0";
        public override ConnectionState State => _state;
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        public override void EnlistTransaction(System.Transactions.Transaction? transaction) { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            var transaction = new RecordingTransaction(this, isolationLevel);
            Transactions.Add(transaction);
            return transaction;
        }
        protected override DbCommand CreateDbCommand() => new RecordingCommand(this);
    }

    private sealed class RecordingTransaction(RecordingConnection connection, IsolationLevel isolationLevel)
        : DbTransaction
    {
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public override IsolationLevel IsolationLevel => isolationLevel;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => Committed = true;
        public override void Rollback() { }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class RecordingCommand(RecordingConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            connection.Commands.Add(CommandText);
            if (connection.FailCommands) throw new QueryFailureException();
            var table = new DataTable();
            // The split-query buffer inspects the result shape even for zero rows.
            var projection = CommandText[..CommandText.IndexOf("\nFROM ", StringComparison.Ordinal)];
            for (var index = 0; index <= projection.Count(character => character == ','); index++)
                table.Columns.Add($"column{index}", typeof(object));
            return table.CreateDataReader();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _parameters.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class QueryFailureException : Exception;
}
