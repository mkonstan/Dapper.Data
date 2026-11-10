# Dapper.Data

Micro-orm implemented on Dapper, streamlines connection and transaction management while preserving flexibility and usability of Dapper.

Version 3.1.2 targets **net10.0** and uses Dapper 2.1.89. All library types are in `Dapper.Data`.
Install `Dapper.Data` and the provider your application uses, such as `Microsoft.Data.Sqlite` or
`Microsoft.Data.SqlClient`.

```csharp
using Dapper.Data;
using Microsoft.Data.Sqlite;

var db = new DbContext<SqliteConnection>("Data Source=orders.db");
var sqlServer = new DbContext<Microsoft.Data.SqlClient.SqlConnection>(
    "Server=localhost;Database=Orders;Integrated Security=true");
```

The constructor validates and parses the connection string without opening a connection. Each context
call creates, opens and disposes its own connection. The context holds no open connection or mutable
state: share one instance concurrently or register it as a singleton. A session is **not thread-safe**;
finish its work, readers and enumerations inside the callback. Do not dispose `session.Connection`.

When a connection needs more than a string (a provider data source, a sign-in token set on each
connection, a test connection that records what it ran), create the context with `FromFactory`. It calls
the factory once per call or batch, never at creation; the factory must return a new, closed connection, which the
context opens and disposes. Null or a connection that is not closed fails that call with
`InvalidOperationException`, and the returned connection is left alone.

```csharp
await using var source = NpgsqlDataSource.Create(connectionString);
var pg = DbContext<NpgsqlConnection>.FromFactory(source.CreateConnection);
```

```csharp
db.Execute("INSERT INTO People(Name) VALUES (@Name)", new { Name = "Ann" });
IReadOnlyList<dynamic> people = db.Query("SELECT Id, Name FROM People ORDER BY Id");
dynamic ann = db.QuerySingle("SELECT Id, Name FROM People WHERE Name=@Name", new { Name = "Ann" });
long count = db.ExecuteScalar<long>("SELECT COUNT(*) FROM People");
```

`Query` buffers every row. `QueryFirst` requires at least one row; `QueryFirstOrDefault` returns null
for no rows. `QuerySingle` requires exactly one row; `QuerySingleOrDefault` returns null for no rows
and rejects multiple rows. Scalar queries return `default(T)` for no row or SQL NULL. Execute accepts
enumerable parameters and sums the affected counts, just like Dapper. Anonymous objects,
`DynamicParameters`, dictionaries and `IN @ids` list expansion are supported.

The common parameter order is `sql, param, commandType, commandTimeout`; asynchronous methods add a
final optional `cancellationToken`. A null timeout uses the provider default; pass 0 for infinite.
Provider and Dapper exceptions propagate to the caller without wrapping or library logging.

```csharp
db.Batch(s =>
{
    s.BeginTransaction();
    try
    {
        s.Execute("INSERT INTO People(Name) VALUES (@Name)", new { Name = "Bob" });
        long inside = s.ExecuteScalar<long>("SELECT COUNT(*) FROM People");
        s.CommitTransaction();
    }
    catch
    {
        s.RollbackTransaction();
        throw;
    }
});
```

Every command automatically receives the session's active transaction. Beginning a second transaction
or committing with none active throws. Rollback with none active does nothing, so the catch pattern
above is safe after a failed commit. Commit and rollback always clear and dispose the transaction.
You can begin another transaction in the same session. If the callback throws, the batch rolls back
and propagates the exception. If it returns with a transaction still active, the batch rolls back and
throws `InvalidOperationException` reporting the uncommitted transaction.

```csharp
await db.BatchAsync(async s =>
{
    await s.BeginTransactionAsync(cancellationToken);
    await s.ExecuteAsync("INSERT INTO People(Name) VALUES (@Name)", new { Name = "C" },
        cancellationToken: cancellationToken);
    await s.CommitTransactionAsync(cancellationToken);
}, cancellationToken);

var rows = await db.QueryAsync("SELECT Id, Name FROM People", cancellationToken: cancellationToken);
```

Use `BatchAsync` for async callbacks; async lambdas passed to `Batch` fail at compile time. Batch's
token controls opening the connection; pass a token to each session operation to cancel it too.
Cleanup uses an uncancelled token so a cancelled operation can still roll back.

Rows support dynamic access and `IDictionary<string, object?>`. SQL NULL becomes null; all other
values retain the provider's types. Column order is preserved. Column matching defaults to ordinal
case-sensitive comparison; opt into ordinal case-insensitive comparison when needed:

```csharp
var ci = new DbContext<SqliteConnection>("Data Source=orders.db", ColumnNameMatching.CaseInsensitive);
dynamic row = ci.QueryFirst("SELECT Id, Name FROM People ORDER BY Id");
Console.WriteLine(row.name);
var dictionary = (IDictionary<string, object?>)row;
Console.WriteLine(dictionary["NAME"]);
row.Extra = 3;                  // adds a column
row.Name = "Updated";           // replaces a value in this returned row only
```

A missing dynamic member throws `RuntimeBinderException`; a missing dictionary key throws
`KeyNotFoundException`. Duplicate column names under the chosen comparison throw with the column
name: alias them, for example `SELECT p.Id AS PersonId, o.Id AS OrderId`. Dictionary members are
explicit, so columns named `Count`, `Keys` or `Values` remain reachable dynamically.

Streaming and multiple result sets require an open session:

```csharp
db.Batch(s =>
{
    foreach (dynamic person in s.QueryUnbuffered("SELECT Id, Name FROM People"))
        Console.WriteLine(person.Name);

    using var grid = s.QueryMultiple("SELECT Id, Name FROM People; SELECT COUNT(*) AS N FROM People");
    var people = grid.Read();
    long count = grid.ReadSingle().N;
});

await db.BatchAsync(async s =>
{
    await foreach (dynamic person in s.QueryUnbufferedAsync("SELECT Id, Name FROM People",
        cancellationToken: cancellationToken))
        Console.WriteLine(person.Name);

    await using var grid = await s.QueryMultipleAsync("SELECT COUNT(*) AS N FROM People",
        cancellationToken: cancellationToken);
    long count = (await grid.ReadSingleAsync(cancellationToken)).N;
}, cancellationToken);
```

Unbuffered commands start on the first `MoveNext`; enumerate while the batch is open. Each grid read
consumes the whole current result set and advances. `ReadFirst`, `ReadFirstOrDefault`, `ReadSingle`
and `ReadSingleOrDefault` follow the corresponding query rules. All have async forms. A read after
the last set throws `InvalidOperationException`. Using a closed session or reading an escaped grid
or stream throws `ObjectDisposedException`.

When an unbuffered stream ends (fully read, stopped early, or failed), it disposes its reader and
then its command, each once; `QueryUnbufferedAsync` disposes both asynchronously. A failure while
reading rows (including cancellation and a duplicate column name) is the exception you get, even if
closing the reader or command also fails; otherwise the first closing failure is thrown.

For typed queries, multi-mapping or other Dapper features, use the open typed connection and its
transaction directly within a batch:

```csharp
using Dapper;

var names = db.Batch(s => s.Connection.Query<string>("SELECT Name FROM People",
    transaction: s.Transaction).AsList());
```

Migration from 2.x:

| 2.x | 3.0.0 |
| --- | --- |
| Old framework targets and `System.Data` / `System.Data.Common` library namespaces | net10.0 only, `Dapper.Data` namespace |
| `IDbContext` / `ISession` / `IDbCommand` | `DbContext<T>` / `DbSession<T>` |
| Abstract `DbContext(IDbConnectionFactory)` | `new DbContext<T>(connectionString, columnNames)` |
| `SqlDbContext(cs)` | `DbContext<SqlConnection>(cs)` with `Microsoft.Data.SqlClient.SqlConnection` |
| `Single` / `Single<T>` / `Single(Type)` | `QuerySingleOrDefault` for dynamic rows; typed calls use Dapper directly |
| `Query<T>`, `Query(Type)`, multi-map `Query<T1..T5,TReturn>`, `CommandDefinition` overloads | Dapper on `session.Connection` with `session.Transaction` |
| `Query<IDictionary<string,object>>` | `Query`, then cast each row |
| `commandTimeout = 0` (infinite) | Default is null (provider default); pass 0 to keep infinite |
| Second `BeginTransaction` silently ignored; `Commit` without a transaction did nothing | Both now throw `InvalidOperationException` |
| Batch ended with an active transaction | Rollback, then an error if the callback returned normally |
| Dapper's missing dynamic member returned null; duplicate columns kept the first | Missing member throws; duplicate columns throw and require aliases |
| `DbServiceProvider`, `DbService`, `ConnectionStringBuilder`, `SqlConnectionFactory` | Removed; use the provider's `DbConnectionStringBuilder` for connection strings |

Build and test with `dotnet test Dapper.Data.sln -c Release`. Tests use isolated SQLite shared-memory
databases; no database server is needed. To try the API, start with a context query, then use a batch
when several calls need one connection or transaction.
