using System.Data.Common;

namespace Dapper.Data;

/// <summary>Consumes buffered result sets from one reader within its originating batch.</summary>
/// <remarks>Each read consumes the whole current set and advances. Duplicate columns throw; alias them to unique names. Missing dynamic members throw a runtime binder exception.</remarks>
public sealed class DbGridReader : IDisposable, IAsyncDisposable
{
    private readonly DbDataReader _reader;
    private readonly StringComparer _comparer;
    private readonly Action _ensureSessionOpen;
    private bool _exhausted;
    private bool _disposed;

    /// <summary>Takes ownership of a Dapper reader and its command.</summary>
    internal DbGridReader(DbDataReader reader, StringComparer comparer, Action ensureSessionOpen)
    {
        _reader = reader;
        _comparer = comparer;
        _ensureSessionOpen = ensureSessionOpen;
    }

    /// <summary>Buffers the current result set and advances to the next.</summary>
    public IReadOnlyList<dynamic> Read()
    {
        EnsureReadable();
        var rows = new RowReader(_reader, _comparer);
        var result = new List<dynamic>();
        while (_reader.Read()) result.Add(rows.ReadRow(_reader));
        _exhausted = !_reader.NextResult();
        return result;
    }

    /// <summary>Asynchronously buffers the current set and advances to the next.</summary>
    public async Task<IReadOnlyList<dynamic>> ReadAsync(CancellationToken cancellationToken = default)
    {
        EnsureReadable();
        var rows = new RowReader(_reader, _comparer);
        var result = new List<dynamic>();
        while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(rows.ReadRow(_reader));
        _exhausted = !await _reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Consumes the current set using constant memory and applies its cardinality rule.</summary>
    private dynamic? ReadOne(bool single, bool allowDefault, string member)
    {
        EnsureReadable();
        var rows = new RowReader(_reader, _comparer);
        DbRow? first = null;
        var many = false;
        while (_reader.Read())
        {
            if (first is null) first = rows.ReadRow(_reader);
            else many = true;
        }
        _exhausted = !_reader.NextResult();
        return RowReader.Select(first, many, single, allowDefault, member);
    }

    /// <summary>Asynchronously consumes one set using constant memory and checks cardinality.</summary>
    private async Task<dynamic?> ReadOneAsync(bool single, bool allowDefault, string member, CancellationToken cancellationToken)
    {
        EnsureReadable();
        var rows = new RowReader(_reader, _comparer);
        DbRow? first = null;
        var many = false;
        while (await _reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (first is null) first = rows.ReadRow(_reader);
            else many = true;
        }
        _exhausted = !await _reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        return RowReader.Select(first, many, single, allowDefault, member);
    }

    /// <summary>Distinguishes a closed session, a disposed grid and a fully consumed grid.</summary>
    private void EnsureReadable()
    {
        _ensureSessionOpen();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_exhausted)
            throw new InvalidOperationException("DbGridReader.Read: no result sets remain.");
    }

    /// <summary>Closes the reader and its Dapper command; repeated disposal is safe.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }

    /// <summary>Asynchronously closes the reader and its Dapper command.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _reader.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Returns the first row; throws when no row exists. Consumes the whole set and advances.</summary>
    public dynamic ReadFirst() => ReadOne(false, false, nameof(ReadFirst))!;

    /// <summary>Returns the first row; throws when no row exists. Asynchronously consumes the whole set and advances.</summary>
    public async Task<dynamic> ReadFirstAsync(CancellationToken cancellationToken = default) =>
        (await ReadOneAsync(false, false, nameof(ReadFirstAsync), cancellationToken).ConfigureAwait(false))!;

    /// <summary>Returns the first row, or null when no row exists. Consumes the whole set and advances.</summary>
    public dynamic? ReadFirstOrDefault() => ReadOne(false, true, nameof(ReadFirstOrDefault));

    /// <summary>Returns the first row, or null when no row exists. Asynchronously consumes the whole set and advances.</summary>
    public async Task<dynamic?> ReadFirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        (await ReadOneAsync(false, true, nameof(ReadFirstOrDefaultAsync), cancellationToken).ConfigureAwait(false));

    /// <summary>Returns exactly one row; throws for zero or multiple rows. Consumes the whole set and advances.</summary>
    public dynamic ReadSingle() => ReadOne(true, false, nameof(ReadSingle))!;

    /// <summary>Returns exactly one row; throws for zero or multiple rows. Asynchronously consumes the whole set and advances.</summary>
    public async Task<dynamic> ReadSingleAsync(CancellationToken cancellationToken = default) =>
        (await ReadOneAsync(true, false, nameof(ReadSingleAsync), cancellationToken).ConfigureAwait(false))!;

    /// <summary>Returns one row or null; throws for multiple rows. Consumes the whole set and advances.</summary>
    public dynamic? ReadSingleOrDefault() => ReadOne(true, true, nameof(ReadSingleOrDefault));

    /// <summary>Returns one row or null; throws for multiple rows. Asynchronously consumes the whole set and advances.</summary>
    public async Task<dynamic?> ReadSingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        (await ReadOneAsync(true, true, nameof(ReadSingleOrDefaultAsync), cancellationToken).ConfigureAwait(false));
}
