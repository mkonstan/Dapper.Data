using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

/// <summary>Pins the connection-factory constructor: when the factory runs, what it must return, and that it changes nothing else.</summary>
public sealed class ConnectionFactoryTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly List<SqliteConnection> _made = [];
    public void Dispose() => _db.Dispose();

    /// <summary>Makes a new connection per call and keeps each one, so a test can assert on the exact instance.</summary>
    private SqliteConnection Make()
    {
        var connection = new SqliteConnection(_db.ConnectionString);
        _made.Add(connection);
        return connection;
    }

    [Fact]
    public void F01_NullFactory()
    {
        Assert.Equal("connectionFactory", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>((Func<SqliteConnection>)null!)).ParamName);
    }

    [Fact]
    public void F02_UndefinedEnum()
    {
        Assert.Equal("columnNames", Assert.Throws<ArgumentOutOfRangeException>(() => new DbContext<SqliteConnection>(Make, (ColumnNameMatching)7)).ParamName);
        Assert.Equal("connectionFactory", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>((Func<SqliteConnection>)null!, (ColumnNameMatching)7)).ParamName);
    }

    [Fact]
    public void F03_ConstructionDoesNotCallTheFactory()
    {
        _ = new DbContext<SqliteConnection>(Make);
        Assert.Empty(_made);
    }

    [Fact]
    public async Task F04_EachCallUsesOneNewConnectionAndDisposesIt()
    {
        var ctx = new DbContext<SqliteConnection>(Make);
        Assert.Equal(2, ctx.Query("SELECT Id FROM People").Count);
        Assert.Equal(1, await ctx.ExecuteAsync("INSERT INTO People(Name) VALUES('Cy')"));
        SqliteConnection? seen = null;
        ctx.Batch(s => { seen = s.Connection; Assert.Equal(ConnectionState.Open, s.Connection.State); });
        SqliteConnection? seenAsync = null;
        await ctx.BatchAsync(s => { seenAsync = s.Connection; return Task.CompletedTask; });
        Assert.Equal(4, _made.Count);
        Assert.Same(_made[2], seen);
        Assert.Same(_made[3], seenAsync);
        Assert.All(_made, c => Assert.Equal(ConnectionState.Closed, c.State));
        Assert.Equal(4, _made.Distinct().Count());
    }

    [Fact]
    public async Task F05_NullFromTheFactoryFailsTheCall()
    {
        var ctx = new DbContext<SqliteConnection>(() => null!);
        var error = Assert.Throws<InvalidOperationException>(() => ctx.Query("SELECT 1"));
        Assert.Equal("The connection factory returned null. It must return a new, closed connection for each call.", error.Message);
        var asyncError = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryAsync("SELECT 1"));
        Assert.Equal(error.Message, asyncError.Message);
    }

    [Fact]
    public async Task F06_AnOpenConnectionFromTheFactoryFailsAndIsLeftToItsOwner()
    {
        using var shared = new SqliteConnection(_db.ConnectionString);
        shared.Open();
        var ctx = new DbContext<SqliteConnection>(() => shared);
        var error = Assert.Throws<InvalidOperationException>(() => ctx.Query("SELECT 1"));
        Assert.Equal("The connection factory returned a connection that is not closed (Open). It must return a new, closed connection for each call.", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryAsync("SELECT 1"));
        Assert.Equal(ConnectionState.Open, shared.State);
        Assert.Equal(1L, shared.ExecuteScalar<long>("SELECT 1"));
    }

    [Fact]
    public async Task F07_FactoryExceptionsPropagateUnchanged()
    {
        var boom = new ApplicationException("factory boom");
        var ctx = new DbContext<SqliteConnection>(() => throw boom);
        Assert.Same(boom, Assert.Throws<ApplicationException>(() => ctx.Query("SELECT 1")));
        Assert.Same(boom, await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(_ => Task.CompletedTask)));
    }

    [Fact]
    public async Task F08_CancelledCallsNeverCallTheFactory()
    {
        var ctx = new DbContext<SqliteConnection>(Make);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ctx.QueryAsync("SELECT 1", cancellationToken: cancelled.Token));
        Assert.Empty(_made);
    }

    [Fact]
    public void F09_ColumnNameMatchingStillApplies()
    {
        var sensitive = new DbContext<SqliteConnection>(Make);
        var insensitive = new DbContext<SqliteConnection>(Make, ColumnNameMatching.CaseInsensitive);
        Assert.Equal("Ann", (string)insensitive.QueryFirst("SELECT Name FROM People WHERE Id = 1").name);
        Assert.ThrowsAny<Exception>(() => (string)sensitive.QueryFirst("SELECT Name FROM People WHERE Id = 1").name);
    }

    [Fact]
    public void F10_ASubclassConnectionKeepsItsOwnState()
    {
        var made = new List<RecordingSqliteConnection>();
        var ctx = new DbContext<RecordingSqliteConnection>(() =>
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
    public void F11_PublicConstructorsAreTheStringAndTheFactory()
    {
        var shapes = typeof(DbContext<SqliteConnection>).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name} = {p.DefaultValue}")))
            .OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(
            ["Func`1 connectionFactory = , ColumnNameMatching columnNames = CaseSensitive",
             "String connectionString = , ColumnNameMatching columnNames = CaseSensitive"],
            shapes);
    }
}
