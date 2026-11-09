using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Reflection;
using Dapper;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

public sealed class ContractTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private DbContext<SqliteConnection> Ctx => _db.Context;
    private DbContext<SqliteConnection> Ci => _db.Insensitive;
    private const string All = "SELECT Id, Name, Amount, Note FROM People ORDER BY Id";
    private const string Empty = "SELECT Id FROM People WHERE Id > 5";
    private const string Many = "SELECT Id FROM People ORDER BY Id";
    public void Dispose() => _db.Dispose();

    [Fact]
    public void T01_NullConnectionString()
    {
        Assert.Equal("connectionString", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>(null!)).ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void T02_BlankConnectionString(string value)
    {
        Assert.Equal("connectionString", Assert.Throws<ArgumentException>(() => new DbContext<SqliteConnection>(value)).ParamName);
    }

    [Fact]
    public void T03_ProviderParsingErrorsPropagate()
    {
        Assert.Throws<ArgumentException>(() => new DbContext<SqliteConnection>("Foo=bar"));
        Assert.Throws<ArgumentException>(() => new DbContext<SqlConnection>("Foo=bar"));
    }

    [Fact]
    public void T04_SqlServerConstructionDoesNotConnect()
    {
        _ = new DbContext<SqlConnection>("Server=localhost;Database=x;Integrated Security=true");
    }

    [Fact]
    public void T05_UndefinedEnum()
    {
        Assert.Equal("columnNames", Assert.Throws<ArgumentOutOfRangeException>(() => new DbContext<SqliteConnection>(_db.ConnectionString, (ColumnNameMatching)7)).ParamName);
        Assert.Equal("connectionString", Assert.Throws<ArgumentNullException>(() => new DbContext<SqliteConnection>(null!, (ColumnNameMatching)7)).ParamName);
    }

    [Fact]
    public void T06_ProviderValuesAndNull()
    {
        var rows = Ctx.Query(All);
        Assert.Equal(2, rows.Count);
        Assert.IsType<long>((object)rows[0].Id);
        Assert.Equal(1L, (long)rows[0].Id);
        Assert.Equal("Ann", (string)rows[0].Name);
        Assert.IsType<double>((object)rows[0].Amount);
        Assert.Equal(1.5, (double)rows[0].Amount);
        Assert.Null((object?)rows[0].Note);
        Assert.Equal("x", (string)rows[1].Note);
    }

    [Fact]
    public void T07_DictionaryColumnOrder()
    {
        var row = (IDictionary<string, object?>)Ctx.Query(All)[0];
        Assert.Equal(["Id", "Name", "Amount", "Note"], row.Keys);
        Assert.Equal(4, row.Count);
    }

    [Fact]
    public void T08_OrdinalCaseSensitive()
    {
        dynamic row = Ctx.Query(All)[0];
        var dict = (IDictionary<string, object?>)row;
        Assert.Throws<RuntimeBinderException>(() => { object x = row.name; });
        Assert.Throws<KeyNotFoundException>(() => dict["NAME"]);
        Assert.False(dict.ContainsKey("name"));
        Assert.Equal("Ann", (string)row.Name);
    }

    [Fact]
    public void T09_OrdinalCaseInsensitive()
    {
        dynamic row = Ci.Query(All)[0];
        var dict = (IDictionary<string, object?>)row;
        Assert.Equal("Ann", (string)row.name);
        Assert.Equal("Ann", (string)row.NAME);
        Assert.Equal("Ann", dict["nAmE"]);
        Assert.True(dict.ContainsKey("ID"));
    }

    [Fact]
    public void T10_MissingDynamicMembersThrow()
    {
        foreach (var context in new[] { Ctx, Ci })
        {
            dynamic row = context.Query(All)[0];
            Assert.Throws<RuntimeBinderException>(() => { object x = row.Nope; });
        }
    }

    [Fact]
    public void T11_DictionaryMembersDoNotHideColumns()
    {
        dynamic row = Ctx.QueryFirst("SELECT 5 AS Count, 'k' AS Keys, 'v' AS [Values]");
        Assert.Equal(5L, (long)row.Count);
        Assert.Equal("k", (string)row.Keys);
        Assert.Equal("v", (string)row.Values);
        Assert.Equal(3, ((IDictionary<string, object?>)row).Count);
        dynamic exact = Ctx.QueryFirst("SELECT 5 AS Count, 'k' AS Keys");
        Assert.Equal(2, ((IDictionary<string, object?>)exact).Count);
    }

    [Fact]
    public void T12_DuplicateColumnsRequireAliases()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Ci.Query("SELECT 1 AS a, 2 AS A"));
        Assert.Contains("'A'", error.Message);
        Assert.Contains("alias", error.Message);
        dynamic row = Ctx.Query("SELECT 1 AS a, 2 AS A")[0];
        Assert.Equal(1L, (long)row.a);
        Assert.Equal(2L, (long)row.A);
        Assert.Throws<InvalidOperationException>(() => Ctx.Query("SELECT 1 AS a, 2 AS a"));
        Assert.Throws<InvalidOperationException>(() => Ci.Query("SELECT 1 AS a, 2 AS A WHERE 0"));
    }

    [Fact]
    public void T13_DynamicMutation()
    {
        dynamic row = Ctx.Query(All)[0];
        var dict = (IDictionary<string, object?>)row;
        row.Extra = 3;
        row.Name = "Zed";
        Assert.Equal(3, dict["Extra"]);
        Assert.Equal("Zed", dict["Name"]);
        Assert.Equal(5, dict.Count);
        Assert.Equal(dict.Keys, ((DynamicObject)row).GetDynamicMemberNames());
    }

    [Fact]
    public void T14_FirstCardinality()
    {
        Assert.Throws<InvalidOperationException>(() => Ctx.QueryFirst(Empty));
        Assert.Equal(1L, (long)Ctx.QueryFirst(Many).Id);
    }

    [Fact]
    public void T15_FirstOrDefaultCardinality() => Assert.Null((object?)Ctx.QueryFirstOrDefault(Empty));

    [Fact]
    public void T16_SingleCardinality()
    {
        Assert.Throws<InvalidOperationException>(() => Ctx.QuerySingle(Empty));
        Assert.Throws<InvalidOperationException>(() => Ctx.QuerySingle(Many));
        Assert.Equal("Bob", (string)Ctx.QuerySingle("SELECT Name FROM People WHERE Id = @id", new { id = 2 }).Name);
    }

    [Fact]
    public void T17_SingleOrDefaultRetainsLegacySingleRules()
    {
        Assert.Null((object?)Ctx.QuerySingleOrDefault(Empty));
        Assert.Throws<InvalidOperationException>(() => Ctx.QuerySingleOrDefault(Many));
        Assert.Equal(1L, (long)Ctx.QuerySingleOrDefault("SELECT Id FROM People WHERE Id=1")!.Id);
    }

    [Fact]
    public void T18_EnumerableExecute()
    {
        Assert.Equal(2, Ctx.Execute("INSERT INTO People(Name) VALUES (@Name)", new[] { new { Name = "C" }, new { Name = "D" } }));
        Assert.Equal(4L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T19_ScalarSemantics()
    {
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
        Assert.Null(Ctx.ExecuteScalar<string>("SELECT Name FROM People WHERE Id=@id", new { id = 99 }));
        Assert.Equal(0L, Ctx.ExecuteScalar<long>("SELECT NULL"));
    }

    [Fact]
    public void T20_ListExpansion()
    {
        var row = Assert.Single(Ctx.Query("SELECT Id FROM People WHERE Id IN @ids", new { ids = new[] { 2, 3 } }));
        Assert.Equal(2L, (long)row.Id);
    }

    [Fact]
    public void T21_SqlValidation()
    {
        Assert.Equal("sql", Assert.Throws<ArgumentNullException>(() => Ctx.Query(null!)).ParamName);
        Assert.Equal("sql", Assert.Throws<ArgumentNullException>(() => Ctx.Execute(null!)).ParamName);
        Assert.Equal("sql", Assert.Throws<ArgumentException>(() => Ctx.Query("  ")).ParamName);
        var unreachable = new DbContext<SqliteConnection>("Data Source=missing/path/no.db;Mode=ReadOnly");
        Assert.Throws<ArgumentException>(() => unreachable.Query("  "));
    }

    [Fact]
    public void T22_ProviderErrorDoesNotPoisonContext()
    {
        Assert.Throws<SqliteException>(() => Ctx.Execute("INSERT INTO Missing VALUES(1)"));
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public async Task T23_AsyncQueryFamily()
    {
        var ctx = Ctx;
        var rows = await ctx.QueryAsync(All);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ann", (string)rows[0].Name);
        Assert.Null((object?)rows[0].Note);
        Assert.Equal(1.5, (double)rows[0].Amount);
        Assert.Equal(1L, (long)(await ctx.QueryFirstAsync(Many)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QueryFirstAsync(Empty));
        Assert.Null((object?)await ctx.QueryFirstOrDefaultAsync(Empty));
        Assert.Equal(1L, (long)(await ctx.QueryFirstOrDefaultAsync(Many))!.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QuerySingleAsync(Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QuerySingleAsync(Many));
        Assert.Equal("Bob", (string)(await ctx.QuerySingleAsync("SELECT Name FROM People WHERE Id=@id", new { id = 2 })).Name);
        Assert.Null((object?)await ctx.QuerySingleOrDefaultAsync(Empty));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.QuerySingleOrDefaultAsync(Many));
        Assert.Equal(1L, (long)(await ctx.QuerySingleOrDefaultAsync("SELECT Id FROM People WHERE Id=1"))!.Id);
        Assert.Equal(2L, await ctx.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM People"));
        Assert.Null(await ctx.ExecuteScalarAsync<string>("SELECT Name FROM People WHERE Id=@id", new { id = 99 }));
        Assert.Equal(2, await ctx.ExecuteAsync("INSERT INTO People(Name) VALUES (@Name)", new[] { new { Name = "C" }, new { Name = "D" } }));
        Assert.Equal(4L, await ctx.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM People"));
        await Assert.ThrowsAsync<SqliteException>(() => ctx.ExecuteAsync("INSERT INTO Missing VALUES(1)"));
    }

    [Fact]
    public async Task T24_CancellationSkipsCallback()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ctx.QueryAsync(All, cancellationToken: source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ctx.ExecuteAsync("DELETE FROM People", cancellationToken: source.Token));
        var invoked = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ctx.BatchAsync(s => { invoked = true; return Task.CompletedTask; }, source.Token));
        Assert.False(invoked);
        await Ctx.BatchAsync(async s =>
        {
            await using var stream = s.QueryUnbufferedAsync("SELECT Id FROM People").GetAsyncEnumerator(source.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await stream.MoveNextAsync(); });
        });
    }

    [Fact]
    public void T25_ConnectionScope()
    {
        SqliteConnection? first = null;
        var ctx = Ctx;
        ctx.Batch(s =>
        {
            first = s.Connection;
            Assert.Equal(ConnectionState.Open, first.State);
            s.Query(All);
            Assert.Same(first, s.Connection);
        });
        Assert.Equal(ConnectionState.Closed, first!.State);
        ctx.Batch(s => Assert.NotSame(first, s.Connection));
    }

    [Fact]
    public void T26_CommitSpansCommands()
    {
        Ctx.Batch(s =>
        {
            s.BeginTransaction();
            s.Execute("INSERT INTO People(Name) VALUES ('C')");
            Assert.Equal(3L, s.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
            Assert.Equal(3, s.Query(All).Count);
            s.CommitTransaction();
        });
        Assert.Equal(3L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T27_RollbackAndCallbackException()
    {
        Ctx.Batch(s => { s.BeginTransaction(); s.Execute("INSERT INTO People(Name) VALUES ('C')"); s.RollbackTransaction(); });
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
        var boom = new ApplicationException("boom");
        SqliteConnection? connection = null;
        var thrown = Assert.Throws<ApplicationException>(() => Ctx.Batch((Action<DbSession<SqliteConnection>>)(s =>
        {
            connection = s.Connection;
            s.BeginTransaction();
            s.Execute("INSERT INTO People(Name) VALUES ('C')");
            throw boom;
        })));
        Assert.Same(boom, thrown);
        Assert.Equal(ConnectionState.Closed, connection!.State);
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T28_UncommittedBatchRollsBackAndReports()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Ctx.Batch(s => { s.BeginTransaction(); s.Execute("INSERT INTO People(Name) VALUES ('C')"); }));
        Assert.Contains("uncommitted transaction", error.Message);
        Assert.Contains("rolled back", error.Message);
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T29_TransactionMisuseAndReuse()
    {
        Ctx.Batch(s =>
        {
            Assert.Throws<InvalidOperationException>(() => s.CommitTransaction());
            s.RollbackTransaction();
            s.BeginTransaction();
            Assert.Throws<InvalidOperationException>(() => s.BeginTransaction());
            s.Execute("INSERT INTO People(Name) VALUES ('C')");
            s.CommitTransaction();
            s.BeginTransaction();
            s.Execute("INSERT INTO People(Name) VALUES ('D')");
            s.CommitTransaction();
        });
        Assert.Equal(4L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T30_TransactionStateAndIsolation()
    {
        Ctx.Batch(s =>
        {
            Assert.Null(s.Transaction);
            s.BeginTransaction(IsolationLevel.Serializable);
            Assert.NotNull(s.Transaction);
            Assert.Equal(IsolationLevel.Serializable, s.Transaction.IsolationLevel);
            s.CommitTransaction();
            Assert.Null(s.Transaction);
            s.BeginTransaction();
            s.RollbackTransaction();
            Assert.Null(s.Transaction);
        });
    }

    [Fact]
    public void T31_DeferredCommitFailureClearsAndRollsBack()
    {
        var ctx = new DbContext<SqliteConnection>(_db.ConnectionString + ";Foreign Keys=True");
        ctx.Execute("CREATE TABLE P(Id INTEGER PRIMARY KEY); CREATE TABLE C(Id INTEGER PRIMARY KEY, PId INTEGER REFERENCES P(Id) DEFERRABLE INITIALLY DEFERRED)");
        SqliteException? caught = null;
        var thrown = Assert.Throws<SqliteException>(() => ctx.Batch((Action<DbSession<SqliteConnection>>)(s =>
        {
            s.BeginTransaction();
            s.Execute("INSERT INTO C(PId) VALUES(99)");
            try { s.CommitTransaction(); }
            catch (SqliteException error)
            {
                caught = error;
                Assert.Null(s.Transaction);
                s.RollbackTransaction();
                throw;
            }
        })));
        Assert.Same(caught, thrown);
        Assert.Equal(0L, ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM C"));
    }

    [Fact]
    public async Task T32_EveryCommandExplicitlyReceivesTransaction()
    {
        var ctx = new DbContext<RecordingSqliteConnection>(_db.ConnectionString);
        var expected = Enumerable.Range(0, 9).Select(i => $"SELECT Id FROM People WHERE Id=1 /* sync-{i} */").ToArray();
        List<(string Sql, bool Assigned)> sync = [];
        ctx.Batch(s =>
        {
            s.BeginTransaction();
            s.Execute(expected[0]);
            s.ExecuteScalar<long>(expected[1]);
            s.Query(expected[2]);
            s.QueryFirst(expected[3]);
            s.QueryFirstOrDefault(expected[4]);
            s.QuerySingle(expected[5]);
            s.QuerySingleOrDefault(expected[6]);
            _ = s.QueryUnbuffered(expected[7]).ToList();
            using (var grid = s.QueryMultiple(expected[8])) grid.Read();
            sync = s.Connection.Executions;
            s.RollbackTransaction();
        });
        foreach (var sql in expected)
        {
            var records = sync.Where(r => r.Sql == sql).ToArray();
            Assert.NotEmpty(records);
            Assert.All(records, r => Assert.True(r.Assigned, r.Sql));
        }
        var asyncSql = expected.Select(s => s.Replace("sync-", "async-")).ToArray();
        List<(string Sql, bool Assigned)> asyncRecords = [];
        await ctx.BatchAsync(async s =>
        {
            await s.BeginTransactionAsync();
            await s.ExecuteAsync(asyncSql[0]);
            await s.ExecuteScalarAsync<long>(asyncSql[1]);
            await s.QueryAsync(asyncSql[2]);
            await s.QueryFirstAsync(asyncSql[3]);
            await s.QueryFirstOrDefaultAsync(asyncSql[4]);
            await s.QuerySingleAsync(asyncSql[5]);
            await s.QuerySingleOrDefaultAsync(asyncSql[6]);
            await foreach (var row in s.QueryUnbufferedAsync(asyncSql[7])) { Assert.Equal(1L, (long)row.Id); }
            await using (var grid = await s.QueryMultipleAsync(asyncSql[8])) await grid.ReadAsync();
            asyncRecords = s.Connection.Executions;
            await s.RollbackTransactionAsync();
        });
        foreach (var sql in asyncSql)
        {
            var records = asyncRecords.Where(r => r.Sql == sql).ToArray();
            Assert.NotEmpty(records);
            Assert.All(records, r => Assert.True(r.Assigned, r.Sql));
        }
        Assert.Equal(2L, Ctx.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void T33_SessionAndDeferredSequenceCannotEscape()
    {
        DbSession<SqliteConnection>? captured = null;
        Ctx.Batch(s => { captured = s; });
        Assert.Throws<ObjectDisposedException>(() => captured!.Query("SELECT 1"));
        Assert.Throws<ObjectDisposedException>(() => captured!.BeginTransaction());
        Assert.Throws<ObjectDisposedException>(() => captured!.Connection);
        var escaped = Ctx.Batch(s => s.QueryUnbuffered(Many));
        Assert.Throws<ObjectDisposedException>(() => escaped.ToList());
        var grid = Ctx.Batch(s => s.QueryMultiple(Many));
        Assert.Throws<ObjectDisposedException>(() => grid.Read());
        grid.Dispose();
    }

    [Fact]
    public async Task T34_StreamingSyncAndAsync()
    {
        Ctx.Batch(s => Assert.Equal([1L, 2L], s.QueryUnbuffered(Many).Select(r => (long)r.Id)));
        await Ctx.BatchAsync(async s =>
        {
            var ids = new List<long>();
            await foreach (var row in s.QueryUnbufferedAsync(Many)) ids.Add((long)row.Id);
            Assert.Equal([1L, 2L], ids);
        });
    }

    [Fact]
    public void T35_MultipleResultsAndNameComparison()
    {
        foreach (var context in new[] { Ctx, Ci }) context.Batch(s =>
        {
            using var grid = s.QueryMultiple("SELECT Id FROM People ORDER BY Id; SELECT COUNT(*) AS N FROM People");
            Assert.Equal(2, grid.Read().Count);
            dynamic row = grid.ReadSingle();
            Assert.Equal(2L, (long)row.N);
            Assert.Equal(2L, (long)((IDictionary<string, object?>)row)["N"]!);
            Assert.Throws<InvalidOperationException>(() => grid.Read());
        });
        Ci.Batch(s => { using var grid = s.QueryMultiple("SELECT COUNT(*) AS N FROM People"); Assert.Equal(2L, (long)grid.ReadSingle().n); });
    }

    [Fact]
    public async Task T36_BatchResultsAndNullCallbacks()
    {
        Assert.Equal(42, Ctx.Batch(s => 42));
        Assert.Equal(2, await Ctx.BatchAsync(async s => (await s.QueryAsync(Many)).Count));
        Assert.Throws<ArgumentNullException>(() => Ctx.Batch((Action<DbSession<SqliteConnection>>)null!));
        Assert.Throws<ArgumentNullException>(() => Ctx.Batch((Func<DbSession<SqliteConnection>, int>)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ctx.BatchAsync((Func<DbSession<SqliteConnection>, Task>)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Ctx.BatchAsync((Func<DbSession<SqliteConnection>, Task<int>>)null!));
    }

    [Fact]
    public void T37_PublicSurfaceAndCompileTimeGuards()
    {
        var exported = typeof(ColumnNameMatching).Assembly.GetExportedTypes();
        Assert.Equal(["ColumnNameMatching", "DbContext`1", "DbGridReader", "DbSession`1"], exported.Select(t => t.Name).OrderBy(n => n));
        Assert.All(exported, t => Assert.Equal("Dapper.Data", t.Namespace));
        var guards = typeof(DbContext<SqliteConnection>).GetMethods().Where(m => m.Name == "Batch" && m.GetCustomAttribute<ObsoleteAttribute>() is not null).ToArray();
        Assert.Equal(2, guards.Length);
        Assert.Contains(guards, m => !m.IsGenericMethod && m.GetParameters()[0].ParameterType == typeof(Func<DbSession<SqliteConnection>, Task>));
        Assert.Contains(guards, m => m.IsGenericMethod && m.GetParameters()[0].ParameterType.GetGenericArguments()[1].GetGenericTypeDefinition() == typeof(Task<>));
        Assert.All(guards, m =>
        {
            Assert.True(m.GetCustomAttribute<ObsoleteAttribute>()!.IsError);
            Assert.Equal(EditorBrowsableState.Never, m.GetCustomAttribute<EditorBrowsableAttribute>()!.State);
        });
        Assert.False(typeof(DbContext<SqliteConnection>).IsSealed);
        Assert.False(typeof(DbContext<SqliteConnection>).IsAbstract);
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(DbContext<SqliteConnection>)));
        Assert.DoesNotContain(typeof(DbContext<SqliteConnection>).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance), m => m.IsVirtual);
        Assert.True(typeof(DbSession<SqliteConnection>).IsSealed);
        Assert.True(typeof(DbGridReader).IsSealed);
        Assert.Empty(typeof(DbSession<SqliteConnection>).GetConstructors());
        Assert.Empty(typeof(DbGridReader).GetConstructors());
    }
}
