using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

/// <summary>Pins DbContext.FromFactory: when the factory runs, what it must return, and that it changes nothing else.</summary>
public sealed class ConnectionFactoryTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly List<CountingConnection> _made = [];
    public void Dispose() => _db.Dispose();

    /// <summary>A SQLite connection that counts its own disposals, so each instance's cleanup is asserted on its own.</summary>
    private sealed class CountingConnection : SqliteConnection
    {
        public int Disposals { get; private set; }
        public CountingConnection() { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }

    /// <summary>Makes a new connection per call and keeps each one, so a test can assert on the exact instance.</summary>
    private CountingConnection Make()
    {
        var connection = new CountingConnection { ConnectionString = _db.ConnectionString };
        _made.Add(connection);
        return connection;
    }

    [Fact]
    public void F01_NullFactory()
    {
        Assert.Equal("connectionFactory", Assert.Throws<ArgumentNullException>(() => DbContext<SqliteConnection>.FromFactory(null!)).ParamName);
    }

    [Fact]
    public void F02_UndefinedEnum()
    {
        Assert.Equal("columnNames", Assert.Throws<ArgumentOutOfRangeException>(() => DbContext<CountingConnection>.FromFactory(Make, (ColumnNameMatching)7)).ParamName);
        Assert.Equal("connectionFactory", Assert.Throws<ArgumentNullException>(() => DbContext<SqliteConnection>.FromFactory(null!, (ColumnNameMatching)7)).ParamName);
    }

    [Fact]
    public void F03_ConstructionDoesNotCallTheFactory()
    {
        _ = DbContext<CountingConnection>.FromFactory(Make);
        Assert.Empty(_made);
    }

    [Fact]
    public async Task F04_EachCallUsesOneNewConnectionAndDisposesIt()
    {
        var ctx = DbContext<CountingConnection>.FromFactory(Make);
        Assert.Equal(2, ctx.Query("SELECT Id FROM People").Count);
        Assert.Equal(1, await ctx.ExecuteAsync("INSERT INTO People(Name) VALUES('Cy')"));
        CountingConnection? seen = null;
        ctx.Batch(s => { seen = s.Connection; Assert.Equal(ConnectionState.Open, s.Connection.State); });
        CountingConnection? seenAsync = null;
        await ctx.BatchAsync(s => { seenAsync = s.Connection; return Task.CompletedTask; });
        Assert.Equal(4, _made.Count);
        Assert.Same(_made[2], seen);
        Assert.Same(_made[3], seenAsync);
        Assert.All(_made, c => Assert.Equal(ConnectionState.Closed, c.State));
        Assert.All(_made, c => Assert.Equal(1, c.Disposals));
        Assert.Equal(4, _made.Distinct().Count());
    }

    [Fact]
    public async Task F13_AFailedCallbackStillDisposesItsConnectionOnce()
    {
        var ctx = DbContext<CountingConnection>.FromFactory(Make);
        var boom = new ApplicationException("callback boom");
        Assert.Same(boom, Assert.Throws<ApplicationException>(() => ctx.Batch(new Action<DbSession<CountingConnection>>(_ => throw boom))));
        Assert.Same(boom, await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(_ => throw boom)));
        Assert.Equal(2, _made.Count);
        Assert.All(_made, c => Assert.Equal(1, c.Disposals));
    }

    [Fact]
    public async Task F05_NullFromTheFactoryFailsTheCall()
    {
        var ctx = DbContext<SqliteConnection>.FromFactory(() => null!);
        var error = Assert.Throws<InvalidOperationException>(() => ctx.Query("SELECT 1"));
        Assert.Equal("The connection factory returned null. It must return a new, closed connection for each call.", error.Message);
        var asyncError = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryAsync("SELECT 1"));
        Assert.Equal(error.Message, asyncError.Message);
    }

    [Fact]
    public async Task F06_AnOpenConnectionFromTheFactoryFailsAndIsLeftToItsOwner()
    {
        using var shared = new CountingConnection { ConnectionString = _db.ConnectionString };
        shared.Open();
        var ctx = DbContext<CountingConnection>.FromFactory(() => shared);
        var error = Assert.Throws<InvalidOperationException>(() => ctx.Query("SELECT 1"));
        Assert.Equal("The connection factory returned a connection that is not closed (Open). It must return a new, closed connection for each call.", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryAsync("SELECT 1"));
        Assert.Equal(ConnectionState.Open, shared.State);
        Assert.Equal(0, shared.Disposals);
        Assert.Equal(1L, shared.ExecuteScalar<long>("SELECT 1"));
    }

    [Fact]
    public async Task F07_FactoryExceptionsPropagateUnchanged()
    {
        var boom = new ApplicationException("factory boom");
        var ctx = DbContext<SqliteConnection>.FromFactory(() => throw boom);
        Assert.Same(boom, Assert.Throws<ApplicationException>(() => ctx.Query("SELECT 1")));
        Assert.Same(boom, await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(_ => Task.CompletedTask)));
    }

    [Fact]
    public async Task F08_CancelledCallsNeverCallTheFactory()
    {
        var ctx = DbContext<CountingConnection>.FromFactory(Make);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ctx.QueryAsync("SELECT 1", cancellationToken: cancelled.Token));
        Assert.Empty(_made);
    }

    [Fact]
    public void F09_ColumnNameMatchingStillApplies()
    {
        var sensitive = DbContext<CountingConnection>.FromFactory(Make);
        var insensitive = DbContext<CountingConnection>.FromFactory(Make, ColumnNameMatching.CaseInsensitive);
        Assert.Equal("Ann", (string)insensitive.QueryFirst("SELECT Name FROM People WHERE Id = 1").name);
        Assert.ThrowsAny<Exception>(() => (string)sensitive.QueryFirst("SELECT Name FROM People WHERE Id = 1").name);
    }

    [Fact]
    public void F10_ASubclassConnectionKeepsItsOwnState()
    {
        var made = new List<RecordingSqliteConnection>();
        var ctx = DbContext<RecordingSqliteConnection>.FromFactory(() =>
        {
            var connection = new RecordingSqliteConnection { ConnectionString = _db.ConnectionString };
            made.Add(connection);
            return connection;
        });
        ctx.Execute("UPDATE People SET Note = 'y' WHERE Id = 1");
        ctx.Execute("UPDATE People SET Note = 'z' WHERE Id = 2");
        Assert.Equal(2, made.Count);
        Assert.Equal([("UPDATE People SET Note = 'y' WHERE Id = 1", false)], made[0].Executions);
        Assert.Equal([("UPDATE People SET Note = 'z' WHERE Id = 2", false)], made[1].Executions);
    }

    [Fact]
    public void F11_TheStringConstructorStaysTheOnlyPublicOne()
    {
        var constructors = typeof(DbContext<SqliteConnection>).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal([typeof(string), typeof(ColumnNameMatching)], Assert.Single(constructors).GetParameters().Select(p => p.ParameterType));
        var fromFactory = typeof(DbContext<SqliteConnection>).GetMethod(nameof(DbContext<SqliteConnection>.FromFactory))!;
        Assert.True(fromFactory.IsStatic);
        Assert.Equal(typeof(DbContext<SqliteConnection>), fromFactory.ReturnType);
        Assert.Equal(["connectionFactory", "columnNames"], fromFactory.GetParameters().Select(p => p.Name));
        Assert.Equal(typeof(Func<SqliteConnection>), fromFactory.GetParameters()[0].ParameterType);
        Assert.Equal(ColumnNameMatching.CaseSensitive, fromFactory.GetParameters()[1].DefaultValue);
    }

    [Fact]
    public void F12_ALiteralNullStillMeansTheConnectionString()
    {
        Assert.Equal("connectionString", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>(null!)).ParamName);
        Assert.Equal("connectionString", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>(null!, ColumnNameMatching.CaseInsensitive)).ParamName);
    }

    [Fact]
    public void F14_TheStringConstructorNeverReadsStateAndDisposesItsParseConnection()
    {
        StateProbe.Reset();
        _ = new DbContext<StateProbe>("probe");
        Assert.Equal(0, StateProbe.StateReads);
        Assert.Equal(1, StateProbe.Disposals);
    }

    /// <summary>A provider whose State getter throws: 3.0.0's string constructor never read it, so 3.1.0 must not either.</summary>
    /// <remarks>Static counters because the string constructor creates the connection itself with new(); only F14 uses them.</remarks>
    public sealed class StateProbe : DbConnection
    {
        public static int StateReads;
        public static int Disposals;
        public static void Reset() { StateReads = 0; Disposals = 0; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State
        {
            get { StateReads++; throw new ApplicationException("state getter failure"); }
        }
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task F15_OverlappingCallsOnOneContextEachOwnTheirConnection()
    {
        const int calls = 8;
        var made = new System.Collections.Concurrent.ConcurrentBag<CountingConnection>();
        var used = new System.Collections.Concurrent.ConcurrentBag<CountingConnection>();
        var ctx = DbContext<CountingConnection>.FromFactory(() =>
        {
            var connection = new CountingConnection { ConnectionString = _db.ConnectionString };
            made.Add(connection);
            return connection;
        });
        var entered = 0;
        var allIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Task.WhenAll(Enumerable.Range(0, calls).Select(_ => Task.Run(() => ctx.BatchAsync(async s =>
        {
            used.Add(s.Connection);
            if (Interlocked.Increment(ref entered) == calls) allIn.SetResult();
            await allIn.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2L, await s.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM People"));
        }))));
        Assert.Equal(calls, made.Count);
        Assert.Equal(calls, made.Distinct().Count());
        Assert.Equal(made.OrderBy(RuntimeHelpers.GetHashCode), used.OrderBy(RuntimeHelpers.GetHashCode));
        Assert.All(made, c => Assert.Equal(1, c.Disposals));
    }

    [Fact]
    public async Task F16_TheCallbackFailureSurvivesACleanupFailure()
    {
        var ctx = DbContext<FailurePrecedenceTests.CleanupFailureConnection>.FromFactory(
            () => new FailurePrecedenceTests.CleanupFailureConnection { ConnectionString = _db.ConnectionString });
        var boom = new ApplicationException("original callback error");
        Assert.Same(boom, Assert.Throws<ApplicationException>(() => ctx.Batch(new Action<DbSession<FailurePrecedenceTests.CleanupFailureConnection>>(s =>
        {
            s.BeginTransaction();
            s.Execute("INSERT INTO People(Name) VALUES('C')");
            s.Connection.FailCleanup = true;
            throw boom;
        }))));
        Assert.Same(boom, await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(async s =>
        {
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('C')");
            s.Connection.FailCleanup = true;
            throw boom;
        })));
        Assert.Equal(2L, new DbContext<SqliteConnection>(_db.ConnectionString).ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Theory]
    [InlineData(ConnectionState.Broken)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Executing)]
    [InlineData(ConnectionState.Fetching)]
    public async Task F17_EveryStateButClosedIsRefusedUntouched(ConnectionState state)
    {
        var connection = new FixedStateConnection { FixedState = state };
        var ctx = DbContext<FixedStateConnection>.FromFactory(() => connection);
        var expected = $"The connection factory returned a connection that is not closed ({state}). It must return a new, closed connection for each call.";
        Assert.Equal(expected, Assert.Throws<InvalidOperationException>(() => ctx.Query("SELECT 1")).Message);
        Assert.Equal(expected, (await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryAsync("SELECT 1"))).Message);
        Assert.Equal(0, connection.Opens);
        Assert.Equal(0, connection.Disposals);
    }

    /// <summary>A connection that reports whatever state a test sets and counts opens and disposals.</summary>
    public sealed class FixedStateConnection : DbConnection
    {
        public ConnectionState FixedState { get; set; }
        public int Opens { get; private set; }
        public int Disposals { get; private set; }
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => FixedState;
        public override void Open() => Opens++;
        public override Task OpenAsync(CancellationToken cancellationToken) { Opens++; return Task.CompletedTask; }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
