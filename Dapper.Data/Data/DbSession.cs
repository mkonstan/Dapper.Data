using Dapper;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Dapper.Data;

/// <summary>Runs commands on the batch's open connection and active transaction.</summary>
/// <remarks>Not thread-safe. All use, including streaming and grid reads, must finish inside the Batch or BatchAsync callback.</remarks>
public sealed class DbSession<TConnection> where TConnection : DbConnection, new()
{
    private readonly TConnection _connection;
    private readonly StringComparer _comparer;
    private DbTransaction? _transaction;
    private bool _closed;

    /// <summary>Accepts the connection owned by the enclosing context callback.</summary>
    internal DbSession(TConnection connection, ColumnNameMatching columnNames)
    {
        _connection = connection;
        _comparer = RowReader.Comparer(columnNames);
    }

    /// <summary>Gets the open typed connection for operations this library does not wrap.</summary>
    public TConnection Connection { get { EnsureOpen(); return _connection; } }

    /// <summary>Gets the active transaction, or null when none is active.</summary>
    public DbTransaction? Transaction { get { EnsureOpen(); return _transaction; } }

    /// <summary>Begins a transaction with the provider's default isolation level.</summary>
    public void BeginTransaction() { EnsureCanBegin(); _transaction = _connection.BeginTransaction(); }

    /// <summary>Begins a transaction at the specified provider isolation level.</summary>
    public void BeginTransaction(IsolationLevel isolationLevel) { EnsureCanBegin(); _transaction = _connection.BeginTransaction(isolationLevel); }

    /// <summary>Asynchronously begins a transaction with the provider's default isolation.</summary>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        EnsureCanBegin();
        _transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously begins a transaction at the specified isolation level.</summary>
    public async Task BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        EnsureCanBegin();
        _transaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Commits the active transaction and always clears and disposes it.</summary>
    /// <remarks>A failed commit also disposes the transaction, so a catch may safely call RollbackTransaction.</remarks>
    public void CommitTransaction()
    {
        EnsureOpen();
        var transaction = _transaction ?? throw new InvalidOperationException("CommitTransaction: no transaction is active in this session.");
        _transaction = null;
        using (transaction) { transaction.Commit(); }
    }

    /// <summary>Rolls back and always disposes and clears the active transaction.</summary>
    /// <remarks>With no active transaction this does nothing, preserving rollback-and-rethrow catch patterns.</remarks>
    public void RollbackTransaction()
    {
        EnsureOpen();
        var transaction = _transaction;
        _transaction = null;
        if (transaction is not null)
            using (transaction) { transaction.Rollback(); }
    }

    /// <summary>Asynchronously commits, then always disposes and clears the active transaction.</summary>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var transaction = _transaction ?? throw new InvalidOperationException("CommitTransactionAsync: no transaction is active in this session.");
        _transaction = null;
        await using (transaction.ConfigureAwait(false)) { await transaction.CommitAsync(cancellationToken).ConfigureAwait(false); }
    }

    /// <summary>Asynchronously rolls back and disposes the active transaction, if any.</summary>
    /// <remarks>With no active transaction this does nothing.</remarks>
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var transaction = _transaction;
        _transaction = null;
        if (transaction is not null)
            await using (transaction.ConfigureAwait(false)) { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); }
    }

    /// <summary>Rejects nested transactions before asking the provider to begin.</summary>
    private void EnsureCanBegin()
    {
        EnsureOpen();
        if (_transaction is not null)
            throw new InvalidOperationException("BeginTransaction: a transaction is already active in this session.");
    }

    /// <summary>Checks the callback lifetime, including deferred enumeration and grid reads.</summary>
    internal void EnsureOpen() => ObjectDisposedException.ThrowIf(_closed, this);

    /// <summary>Builds every Dapper command with the current transaction and caller options.</summary>
    private CommandDefinition Command(string sql, object? param, CommandType? commandType, int? commandTimeout, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        RowReader.ValidateSql(sql);
        return new CommandDefinition(sql, param, _transaction, commandTimeout, commandType, cancellationToken: cancellationToken);
    }

    /// <summary>Closes the session and reports an accidentally uncommitted successful callback.</summary>
    internal void Close()
    {
        var uncommitted = _transaction is not null;
        try { RollbackTransaction(); }
        finally { _closed = true; }
        if (uncommitted)
            throw new InvalidOperationException("Batch: the batch ended with an uncommitted transaction that was rolled back.");
    }

    /// <summary>Rolls back on the callback's exceptional path, then closes the session.</summary>
    internal void CloseAfterFailure()
    {
        try { RollbackTransaction(); }
        finally { _closed = true; }
    }

    /// <summary>Asynchronously rolls back using a noncancelled cleanup token, then reports an uncommitted batch.</summary>
    internal async Task CloseAsync()
    {
        var uncommitted = _transaction is not null;
        try { await RollbackTransactionAsync().ConfigureAwait(false); }
        finally { _closed = true; }
        if (uncommitted)
            throw new InvalidOperationException("BatchAsync: the batch ended with an uncommitted transaction that was rolled back.");
    }

    /// <summary>Asynchronously closes an exceptional callback without reporting an uncommitted batch.</summary>
    internal async Task CloseAfterFailureAsync()
    {
        try { await RollbackTransactionAsync().ConfigureAwait(false); }
        finally { _closed = true; }
    }

    /// <summary>Executes a command and sums affected rows for enumerable parameters.</summary>
    public int Execute(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        var command = Command(sql, param, commandType, commandTimeout);
        return _connection.Execute(command);
    }

    /// <summary>Executes a command and sums affected rows for enumerable parameters.</summary>
    public async Task<int> ExecuteAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        var command = Command(sql, param, commandType, commandTimeout, cancellationToken);
        return await _connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    /// <summary>Returns the first scalar value, or default when absent or NULL.</summary>
    public T? ExecuteScalar<T>(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        var command = Command(sql, param, commandType, commandTimeout);
        return _connection.ExecuteScalar<T>(command);
    }

    /// <summary>Returns the first scalar value, or default when absent or NULL.</summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        var command = Command(sql, param, commandType, commandTimeout, cancellationToken);
        return await _connection.ExecuteScalarAsync<T>(command).ConfigureAwait(false);
    }

    /// <summary>Buffers every row before the connection scope ends.</summary>
    public IReadOnlyList<dynamic> Query(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        using var grid = QueryMultiple(sql, param, commandType, commandTimeout);
        return grid.Read();
    }

    /// <summary>Buffers every row before the connection scope ends.</summary>
    public async Task<IReadOnlyList<dynamic>> QueryAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        await using var grid = await QueryMultipleAsync(sql, param, commandType, commandTimeout, cancellationToken).ConfigureAwait(false);
        return await grid.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first row; throws when no row exists.</summary>
    public dynamic QueryFirst(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        using var grid = QueryMultiple(sql, param, commandType, commandTimeout);
        return grid.ReadFirst();
    }

    /// <summary>Returns the first row; throws when no row exists.</summary>
    public async Task<dynamic> QueryFirstAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        await using var grid = await QueryMultipleAsync(sql, param, commandType, commandTimeout, cancellationToken).ConfigureAwait(false);
        return await grid.ReadFirstAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the first row, or null when no row exists.</summary>
    public dynamic? QueryFirstOrDefault(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        using var grid = QueryMultiple(sql, param, commandType, commandTimeout);
        return grid.ReadFirstOrDefault();
    }

    /// <summary>Returns the first row, or null when no row exists.</summary>
    public async Task<dynamic?> QueryFirstOrDefaultAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        await using var grid = await QueryMultipleAsync(sql, param, commandType, commandTimeout, cancellationToken).ConfigureAwait(false);
        return await grid.ReadFirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns exactly one row; throws for zero or multiple rows.</summary>
    public dynamic QuerySingle(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        using var grid = QueryMultiple(sql, param, commandType, commandTimeout);
        return grid.ReadSingle();
    }

    /// <summary>Returns exactly one row; throws for zero or multiple rows.</summary>
    public async Task<dynamic> QuerySingleAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        await using var grid = await QueryMultipleAsync(sql, param, commandType, commandTimeout, cancellationToken).ConfigureAwait(false);
        return await grid.ReadSingleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns one row or null; throws for multiple rows.</summary>
    public dynamic? QuerySingleOrDefault(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        using var grid = QueryMultiple(sql, param, commandType, commandTimeout);
        return grid.ReadSingleOrDefault();
    }

    /// <summary>Returns one row or null; throws for multiple rows.</summary>
    public async Task<dynamic?> QuerySingleOrDefaultAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        await using var grid = await QueryMultipleAsync(sql, param, commandType, commandTimeout, cancellationToken).ConfigureAwait(false);
        return await grid.ReadSingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Streams rows lazily; the command starts on the first MoveNext.</summary>
    public IEnumerable<dynamic> QueryUnbuffered(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        EnsureOpen();
        RowReader.ValidateSql(sql);
        return Stream();

        IEnumerable<dynamic> Stream()
        {
            var command = Command(sql, param, commandType, commandTimeout);
            using var reader = (DbDataReader)_connection.ExecuteReader(command, CommandBehavior.SequentialAccess);
            var rows = new RowReader(reader, _comparer);
            while (true)
            {
                EnsureOpen();
                if (!reader.Read()) yield break;
                yield return rows.ReadRow(reader);
            }
        }
    }

    /// <summary>Streams rows asynchronously inside the callback's connection lifetime.</summary>
    public async IAsyncEnumerable<dynamic> QueryUnbufferedAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var command = Command(sql, param, commandType, commandTimeout, cancellationToken);
        await using var reader = await _connection.ExecuteReaderAsync(command, CommandBehavior.SequentialAccess).ConfigureAwait(false);
        var rows = new RowReader(reader, _comparer);
        while (true)
        {
            EnsureOpen();
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) yield break;
            yield return rows.ReadRow(reader);
        }
    }

    /// <summary>Opens a reader for multiple result sets, to be consumed inside this session.</summary>
    public DbGridReader QueryMultiple(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null)
    {
        var command = Command(sql, param, commandType, commandTimeout);
        var reader = (DbDataReader)_connection.ExecuteReader(command, CommandBehavior.SequentialAccess);
        return new DbGridReader(reader, _comparer, EnsureOpen);
    }

    /// <summary>Asynchronously opens a reader for multiple result sets inside this session.</summary>
    public async Task<DbGridReader> QueryMultipleAsync(string sql, object? param = null, CommandType? commandType = null, int? commandTimeout = null, CancellationToken cancellationToken = default)
    {
        var command = Command(sql, param, commandType, commandTimeout, cancellationToken);
        var reader = await _connection.ExecuteReaderAsync(command, CommandBehavior.SequentialAccess).ConfigureAwait(false);
        return new DbGridReader(reader, _comparer, EnsureOpen);
    }
}
