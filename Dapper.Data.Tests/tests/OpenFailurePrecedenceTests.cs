using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

/// <summary>Pins 3.1.1: when opening fails and disposing the connection also fails, the open failure is the one thrown.</summary>
/// <remarks>Before 3.1.1 the disposal exception replaced the open exception, because only callback failures were
/// captured ahead of cleanup. The callback is never entered in these cases, so no caller could recover the cause.</remarks>
public sealed class OpenFailurePrecedenceTests
{
    private readonly List<OpenFailureConnection> _made = [];

    /// <summary>A SQLite connection whose open and disposal can each be made to fail; it counts both.</summary>
    private sealed class OpenFailureConnection : SqliteConnection
    {
        public Exception? OpenFailure { get; init; }
        public Exception? CleanupFailure { get; init; }
        public int Opens { get; private set; }
        public int Disposals { get; private set; }
        public OpenFailureConnection() { }
        public override void Open()
        {
            Opens++;
            if (OpenFailure is not null) throw OpenFailure;
            base.Open();
        }
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Opens++;
            if (OpenFailure is not null) return Task.FromException(OpenFailure);
            return base.OpenAsync(cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            Disposals++;
            if (CleanupFailure is not null) throw CleanupFailure;
        }
        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private DbContext<OpenFailureConnection> Context(Exception? openFailure, Exception? cleanupFailure) =>
        DbContext<OpenFailureConnection>.FromFactory(() =>
        {
            var connection = new OpenFailureConnection
            {
                ConnectionString = "Data Source=:memory:",
                OpenFailure = openFailure,
                CleanupFailure = cleanupFailure,
            };
            _made.Add(connection);
            return connection;
        });

    private OpenFailureConnection Only() => Assert.Single(_made);

    [Fact]
    public void O01_sync_open_failure_survives_disposal_failure()
    {
        var open = new IOException("open failed");
        var entered = false;
        var thrown = Assert.Throws<IOException>(() =>
            Context(open, new InvalidOperationException("cleanup failed")).Batch(_ => { entered = true; return 0; }));
        Assert.Same(open, thrown);
        Assert.False(entered);
        Assert.Equal((1, 1), (Only().Opens, Only().Disposals));
    }

    [Fact]
    public async Task O02_async_open_failure_survives_disposal_failure()
    {
        var open = new IOException("open failed");
        var entered = false;
        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            Context(open, new InvalidOperationException("cleanup failed"))
                .BatchAsync(_ => { entered = true; return Task.FromResult(0); }));
        Assert.Same(open, thrown);
        Assert.False(entered);
        Assert.Equal((1, 1), (Only().Opens, Only().Disposals));
    }

    [Fact]
    public async Task O03_cancellation_while_opening_survives_disposal_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new OperationCanceledException(cts.Token);
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Context(cancelled, new InvalidOperationException("cleanup failed")).BatchAsync(_ => Task.FromResult(0)));
        Assert.Same(cancelled, thrown);
        Assert.Equal(1, Only().Disposals);
    }

    [Fact]
    public async Task O04_one_shot_operations_keep_the_open_failure()
    {
        var open = new IOException("open failed");
        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            Context(open, new InvalidOperationException("cleanup failed")).QueryAsync("SELECT 1"));
        Assert.Same(open, thrown);
        Assert.Equal(1, Only().Disposals);
    }

    [Fact]
    public async Task O05_open_failure_alone_is_unchanged_and_disposes_once()
    {
        var syncOpen = new IOException("sync open failed");
        Assert.Same(syncOpen, Assert.Throws<IOException>(() => Context(syncOpen, null).Batch(_ => 0)));
        var asyncOpen = new IOException("async open failed");
        Assert.Same(asyncOpen, await Assert.ThrowsAsync<IOException>(() => Context(asyncOpen, null).BatchAsync(_ => Task.FromResult(0))));
        Assert.All(_made, c => Assert.Equal((1, 1), (c.Opens, c.Disposals)));
    }

    [Fact]
    public async Task O06_after_a_successful_open_and_callback_the_cleanup_failure_still_surfaces()
    {
        var syncCleanup = new InvalidOperationException("sync cleanup failed");
        Assert.Same(syncCleanup, Assert.Throws<InvalidOperationException>(() => Context(null, syncCleanup).Batch(_ => 0)));
        var asyncCleanup = new InvalidOperationException("async cleanup failed");
        Assert.Same(asyncCleanup, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Context(null, asyncCleanup).BatchAsync(_ => Task.FromResult(0))));
    }
}
