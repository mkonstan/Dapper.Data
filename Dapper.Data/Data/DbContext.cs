using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;

namespace Dapper.Data;

/// <summary>Owns a connection per call or batch and returns buffered dynamic rows.</summary>
/// <remarks>Immutable and safe to share concurrently, including as a singleton. Subclasses may name a database without overriding lifecycle rules.</remarks>
public class DbContext<TConnection> where TConnection : DbConnection, new()
{
    private readonly Func<TConnection> _connectionFactory;
    private readonly ColumnNameMatching _columnNames;

    /// <summary>Validates the settings and lets the provider parse the connection string without opening it.</summary>
    public DbContext(string connectionString, ColumnNameMatching columnNames = ColumnNameMatching.CaseSensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _columnNames = ValidColumnNames(columnNames);
        _connectionFactory = () => new() { ConnectionString = connectionString };
        using var connection = CreateConnection();
    }

    /// <summary>Creates a context from a factory that returns a new, closed connection each time a call or batch needs one.</summary>
    /// <remarks>For connections a string cannot describe: one made by a provider's data source, one that carries a sign-in token, or a test's recording connection.
    /// <para>A named method, not a second public constructor: a constructor taking a delegate would make <c>new DbContext&lt;T&gt;(null)</c> ambiguous and break callers that compile against 3.0.0.</para>
    /// <para>The factory is not called here, unlike the string constructor's parse: making a connection is the factory's own code and may have effects its owner counts. A factory that returns null or a connection that is not closed fails the call that asked for it, and that connection is left to its owner.</para></remarks>
    public static DbContext<TConnection> FromFactory(Func<TConnection> connectionFactory, ColumnNameMatching columnNames = ColumnNameMatching.CaseSensitive)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        return new DbContext<TConnection>(() => NewAndClosed(connectionFactory), ValidColumnNames(columnNames));
    }

    /// <summary>Holds already-validated settings for <see cref="FromFactory"/>.</summary>
    private DbContext(Func<TConnection> connectionFactory, ColumnNameMatching columnNames)
    {
        _connectionFactory = connectionFactory;
        _columnNames = columnNames;
    }

    /// <summary>Refuses a column-name rule outside the enum, for both constructors.</summary>
    private static ColumnNameMatching ValidColumnNames(ColumnNameMatching columnNames) =>
        Enum.IsDefined(columnNames)
            ? columnNames
            : throw new ArgumentOutOfRangeException(nameof(columnNames), columnNames, "Use CaseSensitive or CaseInsensitive.");

    /// <summary>Runs a callback once on one open connection, closing its session on return.</summary>
    /// <remarks>An active transaction is rolled back; normal return without committing is an error.</remarks>
    public void Batch(Action<DbSession<TConnection>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Batch(s => { action(s); return 0; });
    }

    /// <summary>Returns a callback's value after closing its connection and session.</summary>
    /// <remarks>The callback's original exception takes precedence even if provider cleanup also fails; ExceptionDispatchInfo preserves its stack through both cleanup scopes.</remarks>
    public TResult Batch<TResult>(Func<DbSession<TConnection>, TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        ExceptionDispatchInfo? callbackFailure = null;
        try
        {
            using var connection = CreateConnection();
            connection.Open();
            var session = new DbSession<TConnection>(connection, _columnNames);
            var completed = false;
            try
            {
                var result = func(session);
                completed = true;
                return result;
            }
            catch (Exception error)
            {
                callbackFailure = ExceptionDispatchInfo.Capture(error);
                throw;
            }
            finally
            {
                if (completed) session.Close();
                else session.CloseAfterFailure();
            }
        }
        finally { callbackFailure?.Throw(); }
    }

    /// <summary>Awaits a callback once on one open connection, then closes its session.</summary>
    public async Task BatchAsync(Func<DbSession<TConnection>, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await BatchAsync(async s => { await action(s).ConfigureAwait(false); return 0; }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns an awaited callback's value after asynchronously closing its resources.</summary>
    /// <remarks>The callback's original exception survives failures during rollback or asynchronous connection disposal.</remarks>
    public async Task<TResult> BatchAsync<TResult>(Func<DbSession<TConnection>, Task<TResult>> func, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        cancellationToken.ThrowIfCancellationRequested();
        ExceptionDispatchInfo? callbackFailure = null;
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var session = new DbSession<TConnection>(connection, _columnNames);
            var completed = false;
            try
            {
                var result = await func(session).ConfigureAwait(false);
                completed = true;
                return result;
            }
            catch (Exception error)
            {
                callbackFailure = ExceptionDispatchInfo.Capture(error);
                throw;
            }
            finally
            {
                if (completed) await session.CloseAsync().ConfigureAwait(false);
                else await session.CloseAfterFailureAsync().ConfigureAwait(false);
            }
        }
        finally { callbackFailure?.Throw(); }
    }

    /// <summary>Rejects asynchronous callbacks on the synchronous lifetime scope.</summary>
    /// <remarks>Use BatchAsync so the connection remains open until the callback completes.</remarks>
    [Obsolete("Use BatchAsync for asynchronous callbacks.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Batch(Func<DbSession<TConnection>, Task> action) => throw new NotSupportedException();

    /// <summary>Rejects asynchronous callbacks on the synchronous value-returning scope.</summary>
    /// <remarks>Use BatchAsync so asynchronous work cannot escape the session lifetime.</remarks>
    [Obsolete("Use BatchAsync for asynchronous callbacks.", error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TResult Batch<TResult>(Func<DbSession<TConnection>, Task<TResult>> func) => throw new NotSupportedException();

    /// <summary>Creates every connection in one place without exposing the secret-bearing string.</summary>
    private TConnection CreateConnection() => _connectionFactory();

    /// <summary>Calls a caller's factory and refuses a result the context cannot own.</summary>
    /// <remarks>Wraps only <see cref="FromFactory"/>'s factory: the string constructor's connection is made here and is new and closed by construction, and 3.0.0 never read its State.
    /// <para>The check runs before any caller's using statement owns the connection, so a shared open connection is refused, never disposed.</para></remarks>
    private static TConnection NewAndClosed(Func<TConnection> connectionFactory)
    {
        const string rule = "It must return a new, closed connection for each call.";
        var connection = connectionFactory()
            ?? throw new InvalidOperationException($"The connection factory returned null. {rule}");
        return connection.State == ConnectionState.Closed
            ? connection
            : throw new InvalidOperationException($"The connection factory returned a connection that is not closed ({connection.State}). {rule}");
    }

    /// <summary>Executes a command and sums affected rows for enumerable parameters.</summary>
    public int Execute(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.Execute(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously executes a command and sums affected rows for enumerable parameters.</summary>
    public Task<int> ExecuteAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.ExecuteAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Returns the first scalar value, or default when absent or NULL.</summary>
    public T? ExecuteScalar<T>(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.ExecuteScalar<T>(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously returns the first scalar value, or default when absent or NULL.</summary>
    public Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.ExecuteScalarAsync<T>(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Buffers every row before the connection scope ends.</summary>
    public IReadOnlyList<dynamic> Query(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.Query(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously buffers every row before the connection scope ends.</summary>
    public Task<IReadOnlyList<dynamic>> QueryAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.QueryAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Returns the first row; throws when no row exists.</summary>
    public dynamic QueryFirst(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.QueryFirst(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously returns the first row; throws when no row exists.</summary>
    public Task<dynamic> QueryFirstAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.QueryFirstAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Returns the first row, or null when no row exists.</summary>
    public dynamic? QueryFirstOrDefault(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.QueryFirstOrDefault(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously returns the first row, or null when no row exists.</summary>
    public Task<dynamic?> QueryFirstOrDefaultAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.QueryFirstOrDefaultAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Returns exactly one row; throws for zero or multiple rows.</summary>
    public dynamic QuerySingle(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.QuerySingle(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously returns exactly one row; throws for zero or multiple rows.</summary>
    public Task<dynamic> QuerySingleAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.QuerySingleAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }

    /// <summary>Returns one row or null; throws for multiple rows.</summary>
    public dynamic? QuerySingleOrDefault(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        RowReader.ValidateSql(sql);
        return Batch(s => s.QuerySingleOrDefault(sql, param, commandType, commandTimeout));
    }

    /// <summary>Asynchronously returns one row or null; throws for multiple rows.</summary>
    public Task<dynamic?> QuerySingleOrDefaultAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        RowReader.ValidateSql(sql);
        return BatchAsync(s => s.QuerySingleOrDefaultAsync(sql, param, commandType, commandTimeout, cancellationToken), cancellationToken);
    }
}
