using System.Data;
using System.Dynamic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

public sealed class LifecycleTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AsyncTransactionsCommitRollbackAndReuse()
    {
        var ctx = _db.Context;
        await ctx.BatchAsync(async s =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.CommitTransactionAsync());
            await s.RollbackTransactionAsync();
            await s.BeginTransactionAsync(IsolationLevel.Serializable);
            Assert.Equal(IsolationLevel.Serializable, s.Transaction!.IsolationLevel);
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.BeginTransactionAsync());
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('C')");
            Assert.Equal(3L, await s.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM People"));
            await s.CommitTransactionAsync();
            Assert.Null(s.Transaction);
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('D')");
            await s.RollbackTransactionAsync();
            Assert.Null(s.Transaction);
        });
        Assert.Equal(3L, ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.BatchAsync(async s =>
        {
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('uncommitted')");
        }));
        Assert.Equal(3L, ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
        var boom = new ApplicationException("async boom");
        SqliteConnection? connection = null;
        var error = await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(async s =>
        {
            connection = s.Connection;
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('failed')");
            throw boom;
        }));
        Assert.Same(boom, error);
        Assert.Equal(ConnectionState.Closed, connection!.State);
        Assert.Equal(3L, ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public async Task AsyncDeferredCommitFailurePreservesProviderException()
    {
        var ctx = new DbContext<SqliteConnection>(_db.ConnectionString + ";Foreign Keys=True");
        await ctx.ExecuteAsync("CREATE TABLE P(Id INTEGER PRIMARY KEY); CREATE TABLE C(Id INTEGER PRIMARY KEY, PId INTEGER REFERENCES P(Id) DEFERRABLE INITIALLY DEFERRED)");
        SqliteException? caught = null;
        var error = await Assert.ThrowsAsync<SqliteException>(() => ctx.BatchAsync(async s =>
        {
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO C(PId) VALUES(99)");
            try { await s.CommitTransactionAsync(); }
            catch (SqliteException providerError)
            {
                caught = providerError;
                Assert.Null(s.Transaction);
                await s.RollbackTransactionAsync();
                throw;
            }
        }));
        Assert.Same(caught, error);
        Assert.Equal(0L, await ctx.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM C"));
    }

    [Fact]
    public async Task EveryGridCardinalityConsumesAndAdvances()
    {
        const string sets = "SELECT Id FROM People ORDER BY Id; SELECT Id FROM People WHERE 0; SELECT Id FROM People WHERE Id=2; SELECT Id FROM People";
        _db.Context.Batch(s =>
        {
            using var grid = s.QueryMultiple(sets);
            Assert.Equal(1L, (long)grid.ReadFirst().Id);
            Assert.Null((object?)grid.ReadFirstOrDefault());
            Assert.Equal(2L, (long)grid.ReadSingle().Id);
            Assert.Throws<InvalidOperationException>(() => grid.ReadSingleOrDefault());
            Assert.Throws<InvalidOperationException>(() => grid.Read());
            using var empty = s.QueryMultiple("SELECT Id FROM People WHERE 0; SELECT Id FROM People WHERE 0");
            Assert.Throws<InvalidOperationException>(() => empty.ReadFirst());
            Assert.Throws<InvalidOperationException>(() => empty.ReadSingle());
            using var single = s.QueryMultiple("SELECT Id FROM People WHERE 0; SELECT Id FROM People WHERE Id=1; SELECT Id FROM People");
            Assert.Null((object?)single.ReadSingleOrDefault());
            Assert.Equal(1L, (long)single.ReadSingleOrDefault()!.Id);
            Assert.Throws<InvalidOperationException>(() => single.ReadSingle());
        });
        await _db.Insensitive.BatchAsync(async s =>
        {
            await using var grid = await s.QueryMultipleAsync(sets);
            Assert.Equal(1L, (long)(await grid.ReadFirstAsync()).id);
            Assert.Null((object?)await grid.ReadFirstOrDefaultAsync());
            Assert.Equal(2L, (long)(await grid.ReadSingleAsync()).id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => grid.ReadSingleOrDefaultAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => grid.ReadAsync());
            await using var empty = await s.QueryMultipleAsync("SELECT Id FROM People WHERE 0; SELECT Id FROM People WHERE 0; SELECT Id FROM People WHERE 0");
            await Assert.ThrowsAsync<InvalidOperationException>(() => empty.ReadFirstAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => empty.ReadSingleAsync());
            Assert.Null((object?)await empty.ReadSingleOrDefaultAsync());
            await using var single = await s.QueryMultipleAsync("SELECT Id FROM People WHERE Id=1; SELECT Id FROM People");
            Assert.Equal(1L, (long)(await single.ReadSingleOrDefaultAsync())!.id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => single.ReadSingleAsync());
            await using var all = await s.QueryMultipleAsync("SELECT Id FROM People");
            Assert.Equal(2, (await all.ReadAsync()).Count);
        });
    }

    [Fact]
    public async Task ClosedSessionsRejectAllOperations()
    {
        DbSession<SqliteConnection>? s = null;
        IAsyncEnumerable<dynamic>? sequence = null;
        DbGridReader? grid = null;
        await _db.Context.BatchAsync(async session =>
        {
            s = session;
            sequence = session.QueryUnbufferedAsync("SELECT Id FROM People");
            grid = await session.QueryMultipleAsync("SELECT Id FROM People");
        });
        Assert.Throws<ObjectDisposedException>(() => s!.Transaction);
        Assert.Throws<ObjectDisposedException>(() => s!.Execute("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.ExecuteScalar<long>("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QueryFirst("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QueryFirstOrDefault("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QuerySingle("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QuerySingleOrDefault("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QueryUnbuffered("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.QueryMultiple("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => s!.BeginTransaction(IsolationLevel.Serializable));
        Assert.Throws<ObjectDisposedException>(() => s!.CommitTransaction());
        Assert.Throws<ObjectDisposedException>(() => s!.RollbackTransaction());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.ExecuteAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.ExecuteScalarAsync<long>("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QueryAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QueryFirstAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QueryFirstOrDefaultAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QuerySingleAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QuerySingleOrDefaultAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.QueryMultipleAsync("SELECT 1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.BeginTransactionAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.BeginTransactionAsync(IsolationLevel.Serializable));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.CommitTransactionAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => s!.RollbackTransactionAsync());
        await using var enumerator = sequence!.GetAsyncEnumerator();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => { await enumerator.MoveNextAsync(); });
        await Assert.ThrowsAsync<ObjectDisposedException>(() => grid!.ReadAsync());
        await grid!.DisposeAsync();
    }

    [Fact]
    public void LazyQueriesStartAtFirstMoveAndStopAtScopeEnd()
    {
        _db.Context.Batch(s =>
        {
            var sequence = s.QueryUnbuffered("SELECT Id FROM People ORDER BY Id");
            s.Execute("INSERT INTO People(Name) VALUES('C')");
            Assert.Equal([1L, 2L, 3L], sequence.Select(r => (long)r.Id));
            var invalid = s.QueryUnbuffered("SELECT Id FROM Missing");
            Assert.Throws<SqliteException>(() => invalid.ToList());
        });
        IEnumerator<dynamic>? sync = null;
        _db.Context.Batch(s =>
        {
            sync = s.QueryUnbuffered("SELECT Id FROM People").GetEnumerator();
            Assert.True(sync.MoveNext());
        });
        Assert.Throws<ObjectDisposedException>(() => sync!.MoveNext());
        sync!.Dispose();
    }

    [Fact]
    public void ExplicitDictionaryMembersAndOrdinalCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            dynamic row = _db.Insensitive.QueryFirst("SELECT 1 AS Id, 'a' AS Name");
            Assert.Equal(1L, (long)row.id);
            var dict = (IDictionary<string, object?>)row;
            Assert.False(dict.IsReadOnly);
            dict.Add("Other", 3);
            Assert.True(dict.TryGetValue("OTHER", out var value));
            Assert.Equal(3, value);
            Assert.False(dict.TryGetValue("missing", out _));
            Assert.Equal(3, dict.Count);
            Assert.True(dict.Contains(new KeyValuePair<string, object?>("OTHER", 3)));
            var copy = new KeyValuePair<string, object?>[3];
            dict.CopyTo(copy, 0);
            Assert.Equal(["Id", "Name", "Other"], copy.Select(pair => pair.Key));
            Assert.Equal([1L, "a", 3], dict.Values);
            Assert.True(dict.Remove(new KeyValuePair<string, object?>("other", 3)));
            dict.Add(new KeyValuePair<string, object?>("Added", 4));
            dict["name"] = "changed";
            Assert.Equal("changed", (string)row.NAME);
            Assert.Equal(["Id", "Name", "Added"], ((DynamicObject)row).GetDynamicMemberNames());
            Assert.True(dict.Remove("ADDED"));
            Assert.False(dict.Remove("missing"));
            Assert.Equal(2, ((System.Collections.IEnumerable)dict).Cast<object>().Count());
            dict.Clear();
            Assert.Empty(dict);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task SharedContextHandlesConcurrentBufferedCalls()
    {
        var ctx = _db.Context;
        var counts = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            var rows = await ctx.QueryAsync("SELECT Id, Name FROM People ORDER BY Id");
            Assert.Equal("Ann", (string)rows[0].Name);
            return rows.Count;
        })));
        Assert.All(counts, count => Assert.Equal(2, count));
    }
}
