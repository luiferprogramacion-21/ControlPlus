using System.Net.Sockets;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ControlPlus.Tests;

internal enum PostgresProbePhase
{
    Open,
    Query
}

internal static class PostgresAvailability
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    public static Task WaitAsync(PostgreSqlContainer container, CancellationToken cancellationToken = default) =>
        WaitAsync(container.GetConnectionString(),
            token => GetLiveContainerStateAsync(container, token), ProbeAsync,
            Timeout, Interval, cancellationToken, TimeProvider.System);

    internal static async Task WaitAsync(
        string connectionString,
        Func<CancellationToken, Task<TestcontainersStates>> getLiveState,
        Func<string, CancellationToken, Action<PostgresProbePhase>, Task> probe,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var startedAt = clock.GetTimestamp();
        var attempts = 0;
        var phase = PostgresProbePhase.Open;
        SocketError? lastSocketError = null;
        var lastState = TestcontainersStates.Undefined;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDeadlineExpired(clock, startedAt, timeout);

                lastState = await GetLiveStateWithinDeadlineAsync(
                    getLiveState, clock, startedAt, timeout, cancellationToken);
                EnsureRunning(lastState);
                ThrowIfDeadlineExpired(clock, startedAt, timeout);

                attempts++;
                phase = PostgresProbePhase.Open;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task probeTask;
                try
                {
                    probeTask = probe(connectionString, attempt.Token, value => phase = value);
                }
                catch (Exception error)
                {
                    probeTask = Task.FromException(error);
                }

                try
                {
                    await AwaitWithinDeadlineAsync(
                        probeTask, attempt, clock, startedAt, timeout, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfDeadlineExpired(clock, startedAt, timeout);

                    lastState = await GetLiveStateWithinDeadlineAsync(
                        getLiveState, clock, startedAt, timeout, cancellationToken);
                    EnsureRunning(lastState);
                    ThrowIfDeadlineExpired(clock, startedAt, timeout);
                    return;
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    attempt.Cancel();
                    ObserveLateFailure(probeTask);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }
                catch (Exception error) when (GetTransientSocketError(error) is { } socketError)
                {
                    lastSocketError = socketError;
                    lastState = await GetLiveStateWithinDeadlineAsync(
                        getLiveState, clock, startedAt, timeout, cancellationToken);
                    EnsureRunning(lastState);
                }

                ThrowIfDeadlineExpired(clock, startedAt, timeout);
                var remaining = GetRemaining(clock, startedAt, timeout);
                await Task.Delay(remaining < interval ? remaining : interval, clock, cancellationToken);
            }
        }
        catch (GlobalDeadlineExpiredException) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateTimeoutException(timeout, attempts, phase, lastSocketError, lastState);
        }
    }

    internal static string CreateProbeConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Timeout = 2,
            CommandTimeout = 2
        };
        return builder.ConnectionString;
    }

    private static async Task<TestcontainersStates> GetLiveContainerStateAsync(
        PostgreSqlContainer container,
        CancellationToken cancellationToken)
    {
        try
        {
            // IContainer.State is cached. A no-op exec reaches Docker and only succeeds
            // while the container is actually running.
            var result = await container.ExecAsync(["true"], cancellationToken);
            return result.ExitCode == 0 ? TestcontainersStates.Running : TestcontainersStates.Undefined;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return TestcontainersStates.Undefined;
        }
    }

    private static async Task<TestcontainersStates> GetLiveStateWithinDeadlineAsync(
        Func<CancellationToken, Task<TestcontainersStates>> getLiveState,
        TimeProvider clock,
        long startedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<TestcontainersStates> stateTask;
        try
        {
            stateTask = getLiveState(operation.Token);
        }
        catch (Exception error)
        {
            stateTask = Task.FromException<TestcontainersStates>(error);
        }

        await AwaitWithinDeadlineAsync(
            stateTask, operation, clock, startedAt, timeout, cancellationToken);
        return await stateTask;
    }

    private static async Task AwaitWithinDeadlineAsync(
        Task operationTask,
        CancellationTokenSource operationCancellation,
        TimeProvider clock,
        long startedAt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var remaining = GetRemaining(clock, startedAt, timeout);
        if (remaining <= TimeSpan.Zero)
            throw new GlobalDeadlineExpiredException();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var deadlineTask = Task.Delay(remaining, clock, deadline.Token);
        var completed = await Task.WhenAny(operationTask, deadlineTask);
        if (ReferenceEquals(completed, operationTask))
        {
            await deadline.CancelAsync();
            await operationTask;
            return;
        }

        operationCancellation.Cancel();
        ObserveLateFailure(operationTask);
        cancellationToken.ThrowIfCancellationRequested();
        throw new GlobalDeadlineExpiredException();
    }

    private static void ObserveLateFailure(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(static completed => _ = completed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void EnsureRunning(TestcontainersStates state)
    {
        if (state != TestcontainersStates.Running)
            throw new InvalidOperationException(
                $"Testcontainers PostgreSQL no está operativo. Estado vivo: {state}.");
    }

    private static void ThrowIfDeadlineExpired(
        TimeProvider clock,
        long startedAt,
        TimeSpan timeout)
    {
        if (GetRemaining(clock, startedAt, timeout) <= TimeSpan.Zero)
            throw new GlobalDeadlineExpiredException();
    }

    private static TimeSpan GetRemaining(TimeProvider clock, long startedAt, TimeSpan timeout) =>
        timeout - clock.GetElapsedTime(startedAt);

    private static TimeoutException CreateTimeoutException(
        TimeSpan timeout,
        int attempts,
        PostgresProbePhase phase,
        SocketError? lastSocketError,
        TestcontainersStates lastState) =>
        new($"Testcontainers PostgreSQL no estuvo disponible en {timeout.TotalSeconds:0} segundos. " +
            $"Intentos: {attempts}. Fase: {phase}. " +
            $"Último SocketError: {lastSocketError?.ToString() ?? "Ninguno"}. " +
            $"Último estado vivo: {lastState}.");

    private static SocketError? GetTransientSocketError(Exception error)
    {
        if (error is PostgresException || error is not (SocketException or NpgsqlException))
            return null;

        for (var cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is not SocketException socketError)
                continue;

            return socketError.SocketErrorCode is SocketError.ConnectionRefused
                or SocketError.TimedOut
                or SocketError.ConnectionReset
                or SocketError.ConnectionAborted
                    ? socketError.SocketErrorCode
                    : null;
        }

        return null;
    }

    private static async Task ProbeAsync(
        string connectionString,
        CancellationToken cancellationToken,
        Action<PostgresProbePhase> setPhase)
    {
        await using var connection = new NpgsqlConnection(CreateProbeConnectionString(connectionString));
        setPhase(PostgresProbePhase.Open);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        setPhase(PostgresProbePhase.Query);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private sealed class GlobalDeadlineExpiredException : Exception;
}
