using System.Net.Sockets;
using ControlPlus.Tests;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class PostgresAvailabilityTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Theory]
    [InlineData(SocketError.ConnectionRefused, false)]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.TimedOut, false)]
    [InlineData(SocketError.TimedOut, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.ConnectionReset, true)]
    [InlineData(SocketError.ConnectionAborted, false)]
    [InlineData(SocketError.ConnectionAborted, true)]
    public async Task RetriesOnlyTransientSocketErrors(SocketError code, bool wrapped)
    {
        var attempts = 0;
        await PostgresAvailability.WaitAsync("published connection", Running, (_, _, _) =>
        {
            if (++attempts == 1)
                throw SocketFailure(code, wrapped);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(SocketError.AccessDenied, false)]
    [InlineData(SocketError.AccessDenied, true)]
    [InlineData(SocketError.InvalidArgument, false)]
    [InlineData(SocketError.InvalidArgument, true)]
    [InlineData(SocketError.HostNotFound, false)]
    [InlineData(SocketError.HostNotFound, true)]
    public async Task DoesNotRetryOtherSocketErrors(SocketError code, bool wrapped)
    {
        var attempts = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => PostgresAvailability.WaitAsync("published connection", Running,
            (_, _, _) => { attempts++; throw SocketFailure(code, wrapped); },
            TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(1)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task DoesNotRetryServerErrors()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<PostgresException>(() => PostgresAvailability.WaitAsync("published connection", Running,
            (_, _, _) => { attempts++; throw new PostgresException("invalid", "invalid", "invalid", "28P01"); },
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExitedContainerFailsBeforeProbe()
    {
        var attempts = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAvailability.WaitAsync("secret",
            _ => Task.FromResult(TestcontainersStates.Exited),
            (_, _, _) => { attempts++; return Task.CompletedTask; },
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));
        Assert.Equal(0, attempts);
        Assert.Contains("Exited", error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task ContainerExitAfterTransientFailureStopsBeforeSecondProbe()
    {
        var state = TestcontainersStates.Running;
        var attempts = 0;
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAvailability.WaitAsync("secret",
            _ => Task.FromResult(state), (_, _, _) =>
            {
                attempts++;
                state = TestcontainersStates.Exited;
                throw new SocketException((int)SocketError.ConnectionRefused);
            }, TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(1), watchdog.Token));
        Assert.Equal(1, attempts);
        Assert.Contains("Exited", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.False(watchdog.IsCancellationRequested);
    }

    [Fact]
    public async Task TimeoutIsBoundedAndSanitized()
    {
        var clock = new ManualTimeProvider();
        var error = await Assert.ThrowsAsync<TimeoutException>(() => PostgresAvailability.WaitAsync("secret", Running,
            (_, _, _) =>
            {
                clock.Advance(TestTimeout);
                throw new SocketException((int)SocketError.ConnectionRefused);
            }, TestTimeout, TimeSpan.Zero, timeProvider: clock));
        Assert.DoesNotContain("secret", error.Message);
    }

    [Fact]
    public async Task CancellationStopsWaiting()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresAvailability.WaitAsync(
            "published connection", Running,
            (_, _, _) => throw new SocketException((int)SocketError.ConnectionRefused),
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), cancellation.Token));
    }

    [Fact]
    public async Task CancellationWinsWhenConnectionFailsAtTheSameTime()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresAvailability.WaitAsync(
            "published connection", Running, (_, _, _) =>
            {
                attempts++;
                cancellation.Cancel();
                throw new SocketException((int)SocketError.ConnectionRefused);
            }, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), cancellation.Token));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ProbeIgnoringCancellationIsCutOffByExternalDeadline()
    {
        var clock = new ManualTimeProvider();
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken attemptToken = default;

        var wait = PostgresAvailability.WaitAsync("sentinel-password", Running, (_, token, setPhase) =>
        {
            attemptToken = token;
            setPhase(PostgresProbePhase.Open);
            probeStarted.SetResult();
            return neverCompletes.Task;
        }, TestTimeout, TimeSpan.FromMilliseconds(100), timeProvider: clock);

        await probeStarted.Task;
        clock.Advance(TestTimeout);
        var error = await Assert.ThrowsAsync<TimeoutException>(() => wait);

        Assert.True(attemptToken.IsCancellationRequested);
        Assert.DoesNotContain("sentinel-password", error.Message);
        neverCompletes.SetResult();
    }

    [Fact]
    public async Task SuccessfulProbeAfterDeadlineIsNotAccepted()
    {
        var clock = new ManualTimeProvider();
        var liveChecks = 0;

        var error = await Assert.ThrowsAsync<TimeoutException>(() => PostgresAvailability.WaitAsync("secret",
            _ =>
            {
                liveChecks++;
                return Task.FromResult(TestcontainersStates.Running);
            }, (_, _, setPhase) =>
            {
                setPhase(PostgresProbePhase.Query);
                clock.Advance(TestTimeout);
                return Task.CompletedTask;
            }, TestTimeout, TimeSpan.Zero, timeProvider: clock));

        Assert.Equal(1, liveChecks);
        Assert.Contains("Fase: Query", error.Message);
    }

    [Fact]
    public async Task ContainerStoppingDuringSuccessfulProbeIsRejectedBeforeReturn()
    {
        var states = new Queue<TestcontainersStates>(
            [TestcontainersStates.Running, TestcontainersStates.Exited]);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAvailability.WaitAsync("secret",
            _ => Task.FromResult(states.Dequeue()), (_, _, _) => Task.CompletedTask,
            TimeSpan.FromSeconds(1), TimeSpan.Zero));

        Assert.Contains("Exited", error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Empty(states);
    }

    [Theory]
    [InlineData(TestcontainersStates.Undefined)]
    [InlineData(TestcontainersStates.Created)]
    [InlineData(TestcontainersStates.Restarting)]
    [InlineData(TestcontainersStates.Paused)]
    [InlineData(TestcontainersStates.Exited)]
    [InlineData(TestcontainersStates.Dead)]
    public async Task NonRunningLiveStatesAreRejected(TestcontainersStates state)
    {
        var attempts = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresAvailability.WaitAsync("secret",
            _ => Task.FromResult(state), (_, _, _) => { attempts++; return Task.CompletedTask; },
            TimeSpan.FromSeconds(1), TimeSpan.Zero));

        Assert.Equal(0, attempts);
        Assert.Contains(state.ToString(), error.Message);
    }

    [Fact]
    public async Task RunningLiveStateIsAcceptedBeforeAndAfterProbe()
    {
        var liveChecks = 0;
        var attempts = 0;
        await PostgresAvailability.WaitAsync("published connection",
            _ =>
            {
                liveChecks++;
                return Task.FromResult(TestcontainersStates.Running);
            }, (_, _, _) => { attempts++; return Task.CompletedTask; },
            TimeSpan.FromSeconds(1), TimeSpan.Zero);

        Assert.Equal(1, attempts);
        Assert.Equal(2, liveChecks);
    }

    [Fact]
    public async Task TimeoutReportsAttemptsPhaseSocketAndLiveStateWithoutSecrets()
    {
        const string connectionString =
            "Host=sentinel-host;Port=6543;Database=sentinel-database;Username=sentinel-user;Password=sentinel-password";
        var clock = new ManualTimeProvider();
        var attempts = 0;

        var error = await Assert.ThrowsAsync<TimeoutException>(() => PostgresAvailability.WaitAsync(connectionString, Running,
            (_, _, setPhase) =>
            {
                attempts++;
                setPhase(PostgresProbePhase.Query);
                if (attempts == 3)
                    clock.Advance(TestTimeout);
                throw new NpgsqlException("connection failed",
                    new SocketException((int)SocketError.ConnectionRefused));
            }, TestTimeout, TimeSpan.Zero, timeProvider: clock));

        Assert.Contains("20 segundos", error.Message);
        Assert.Contains("Intentos: 3", error.Message);
        Assert.Contains("Fase: Query", error.Message);
        Assert.Contains("SocketError: ConnectionRefused", error.Message);
        Assert.Contains("estado vivo: Running", error.Message);
        Assert.DoesNotContain(connectionString, error.Message);
        Assert.DoesNotContain("sentinel-host", error.Message);
        Assert.DoesNotContain("sentinel-password", error.Message);
    }

    [Fact]
    public void ProbeConnectionStringIsIsolatedWithoutChangingOriginal()
    {
        const string original =
            "Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=sentinel-password;Pooling=true;Timeout=15;Command Timeout=30";

        var probe = PostgresAvailability.CreateProbeConnectionString(original);
        var probeBuilder = new NpgsqlConnectionStringBuilder(probe);
        var originalBuilder = new NpgsqlConnectionStringBuilder(original);

        Assert.False(probeBuilder.Pooling);
        Assert.Equal(2, probeBuilder.Timeout);
        Assert.Equal(2, probeBuilder.CommandTimeout);
        Assert.True(originalBuilder.Pooling);
        Assert.Equal(15, originalBuilder.Timeout);
        Assert.Equal(30, originalBuilder.CommandTimeout);
        Assert.Equal(
            "Host=127.0.0.1;Port=5432;Database=test;Username=test;Password=sentinel-password;Pooling=true;Timeout=15;Command Timeout=30",
            original);
    }

    private static Task<TestcontainersStates> Running(CancellationToken _) =>
        Task.FromResult(TestcontainersStates.Running);

    private static Exception SocketFailure(SocketError code, bool wrapped)
    {
        var socket = new SocketException((int)code);
        return wrapped ? new NpgsqlException("connection failed", socket) : socket;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_gate)
                return _timestamp;
        }

        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public void Advance(TimeSpan elapsed)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_gate)
            {
                _timestamp += elapsed.Ticks;
                foreach (var timer in _timers.ToArray())
                {
                    if (timer.TryFire(_timestamp, out var callback))
                        callbacks.Add(callback);
                }
            }

            foreach (var (callback, state) in callbacks)
                callback(state);
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (_gate)
                _timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider _provider;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private long _dueAt;
            private TimeSpan _period;
            private bool _disposed;

            public ManualTimer(
                ManualTimeProvider provider,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _provider = provider;
                _callback = callback;
                _state = state;
                _period = period;
                _dueAt = DueAt(provider._timestamp, dueTime);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_provider._gate)
                {
                    if (_disposed)
                        return false;
                    _period = period;
                    _dueAt = DueAt(_provider._timestamp, dueTime);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_provider._gate)
                {
                    _disposed = true;
                    _provider._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool TryFire(long now, out (TimerCallback Callback, object? State) callback)
            {
                if (_disposed || now < _dueAt)
                {
                    callback = default;
                    return false;
                }

                callback = (_callback, _state);
                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? long.MaxValue
                    : DueAt(now, _period);
                return true;
            }

            private static long DueAt(long now, TimeSpan dueTime) =>
                dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(now + dueTime.Ticks);
        }
    }
}
