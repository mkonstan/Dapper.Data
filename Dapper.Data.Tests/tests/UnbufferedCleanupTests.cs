using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

/// <summary>Pins 3.1.2: a streamed query disposes its reader and then its command exactly once (asynchronously on the
/// async path), and a failure while reading is never replaced by a failure while closing. Every case runs sync and
/// async.</summary>
/// <remarks>Before 3.1.2 the session relied on Dapper's wrapped reader: its DisposeAsync never disposed the command,
/// its Dispose skipped the command when the reader's own disposal threw, and either disposal failure replaced a read
/// failure already in flight.</remarks>
public sealed class UnbufferedCleanupTests
{
    private const string Rows = "SELECT 1 AS Id UNION ALL SELECT 2 UNION ALL SELECT 3";
    private readonly List<FaultConnection> _made = [];

    /// <summary>A SQLite connection whose commands and readers record their disposals and can be made to fail.</summary>
    private sealed class FaultConnection : SqliteConnection
    {
        public int ReadFailAt { get; set; } = -1;
        public Exception? ReadFailure { get; set; }
        public int ValueFailAt { get; set; } = -1;
        public Exception? ValueFailure { get; set; }
        public Exception? ReaderDisposeFailure { get; set; }
        public Exception? CommandDisposeFailure { get; set; }
        /// <summary>The token each ReadAsync call received.</summary>
        public List<CancellationToken> ReadTokens { get; } = [];
        /// <summary>"reader" or "command", with " async" when disposed through DisposeAsync, in disposal order.</summary>
        public List<string> DisposalLog { get; } = [];
        public FaultConnection() { }
        protected override DbCommand CreateDbCommand() => new FaultCommand(base.CreateDbCommand(), this);

        public void Record(string what, Exception? failure)
        {
            DisposalLog.Add(what);
            if (failure is not null) throw failure;
        }
    }

    /// <summary>Forwards to the provider command; records and optionally fails its disposal.</summary>
    private sealed class FaultCommand(DbCommand inner, FaultConnection owner) : DbCommand
    {
        [AllowNull] public override string CommandText { get => inner.CommandText; set => inner.CommandText = value; }
        public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
        public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
        public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
        public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => inner.Connection; set => inner.Connection = value; }
        protected override DbTransaction? DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();
        public override void Cancel() => inner.Cancel();
        public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => inner.ExecuteScalar();
        public override void Prepare() => inner.Prepare();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            new FaultReader(inner.ExecuteReader(behavior), owner);
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
            new FaultReader(await inner.ExecuteReaderAsync(behavior, cancellationToken), owner);
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            inner.Dispose();
            owner.Record("command", owner.CommandDisposeFailure);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
            owner.Record("command async", owner.CommandDisposeFailure);
        }
    }

    /// <summary>Forwards to the provider reader; can fail one read, one row's values and its disposal.</summary>
    private sealed class FaultReader(DbDataReader inner, FaultConnection owner) : DbDataReader
    {
        private int _reads;
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override IEnumerator GetEnumerator() => new DbEnumerator(this);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal)
        {
            if (_reads == owner.ValueFailAt) throw owner.ValueFailure!;
            return inner.GetValue(ordinal);
        }
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override bool Read()
        {
            if (++_reads == owner.ReadFailAt) throw owner.ReadFailure!;
            return inner.Read();
        }
        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            owner.ReadTokens.Add(cancellationToken);
            if (++_reads == owner.ReadFailAt) throw owner.ReadFailure!;
            return inner.ReadAsync(cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            inner.Dispose();
            owner.Record("reader", owner.ReaderDisposeFailure);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
            owner.Record("reader async", owner.ReaderDisposeFailure);
        }
    }

    private DbContext<FaultConnection> Context(Action<FaultConnection>? faults = null) =>
        DbContext<FaultConnection>.FromFactory(() =>
        {
            var connection = new FaultConnection { ConnectionString = "Data Source=:memory:" };
            faults?.Invoke(connection);
            _made.Add(connection);
            return connection;
        });

    private FaultConnection Only() => Assert.Single(_made);

    /// <summary>Reader then command, each once, through DisposeAsync on the async path and Dispose on the sync one.</summary>
    private static string[] ReaderThenCommand(bool async) =>
        async ? ["reader async", "command async"] : ["reader", "command"];

    /// <summary>Streams up to <paramref name="take"/> rows inside one batch; a sync failure comes back as a faulted task.</summary>
    private static Task Consume(bool async, DbContext<FaultConnection> context, string sql = Rows, int take = int.MaxValue)
    {
        if (async)
            return context.BatchAsync(async s =>
            {
                var seen = 0;
                await foreach (var _ in s.QueryUnbufferedAsync(sql))
                    if (++seen == take) break;
            });
        try
        {
            context.Batch(new Action<DbSession<FaultConnection>>(s =>
            {
                var seen = 0;
                foreach (var _ in s.QueryUnbuffered(sql))
                    if (++seen == take) break;
            }));
            return Task.CompletedTask;
        }
        catch (Exception error) { return Task.FromException(error); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U01_read_failure_survives_reader_disposal_failure(bool async)
    {
        var read = new IOException("read failed");
        var thrown = await Assert.ThrowsAsync<IOException>(() => Consume(async, Context(c =>
        {
            c.ReadFailAt = 2;
            c.ReadFailure = read;
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
        })));
        Assert.Same(read, thrown);
        Assert.Contains(async ? "FaultReader.ReadAsync(" : "FaultReader.Read(", thrown.StackTrace);
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U02_read_failure_survives_command_disposal_failure(bool async)
    {
        var read = new IOException("read failed");
        var thrown = await Assert.ThrowsAsync<IOException>(() => Consume(async, Context(c =>
        {
            c.ReadFailAt = 1;
            c.ReadFailure = read;
            c.CommandDisposeFailure = new ApplicationException("command cleanup failed");
        })));
        Assert.Same(read, thrown);
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U03_duplicate_columns_keep_their_exact_message_over_disposal_failures(bool async)
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Consume(async, Context(c =>
        {
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
            c.CommandDisposeFailure = new ApplicationException("command cleanup failed");
        }), "SELECT 1 AS a, 2 AS a"));
        Assert.Equal("RowReader: duplicate column 'a'; alias it to a unique name.", thrown.Message);
        Assert.Contains("RowReader..ctor(", thrown.StackTrace);
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U04_row_conversion_failure_survives_reader_disposal_failure(bool async)
    {
        var conversion = new InvalidCastException("value failed");
        var thrown = await Assert.ThrowsAsync<InvalidCastException>(() => Consume(async, Context(c =>
        {
            c.ValueFailAt = 2;
            c.ValueFailure = conversion;
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
        })));
        Assert.Same(conversion, thrown);
        Assert.Contains("FaultReader.GetValue(", thrown.StackTrace);
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U05_closed_session_failure_survives_reader_disposal_failure(bool async)
    {
        var context = Context(c => c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed"));
        if (async)
        {
            IAsyncEnumerator<dynamic>? rows = null;
            await context.BatchAsync(async s =>
            {
                rows = s.QueryUnbufferedAsync(Rows).GetAsyncEnumerator();
                Assert.True(await rows.MoveNextAsync());
            });
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await rows!.MoveNextAsync());
        }
        else
        {
            IEnumerator<dynamic>? rows = null;
            context.Batch(new Action<DbSession<FaultConnection>>(s =>
            {
                rows = s.QueryUnbuffered(Rows).GetEnumerator();
                Assert.True(rows.MoveNext());
            }));
            Assert.Throws<ObjectDisposedException>(() => rows!.MoveNext());
        }
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true, int.MaxValue)]
    [InlineData(true, 1)]
    [InlineData(false, int.MaxValue)]
    [InlineData(false, 1)]
    public async Task U06_full_or_early_stop_disposes_reader_then_command_once(bool async, int take)
    {
        await Consume(async, Context(), take: take);
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U07_after_success_the_first_cleanup_failure_surfaces_and_both_are_attempted(bool async)
    {
        var readerCleanup = new ApplicationException("reader cleanup failed");
        Assert.Same(readerCleanup, await Assert.ThrowsAsync<ApplicationException>(() => Consume(async, Context(c =>
        {
            c.ReaderDisposeFailure = readerCleanup;
            c.CommandDisposeFailure = new InvalidOperationException("command cleanup failed");
        }))));
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
        _made.Clear();
        var commandCleanup = new ApplicationException("command cleanup failed");
        Assert.Same(commandCleanup, await Assert.ThrowsAsync<ApplicationException>(() =>
            Consume(async, Context(c => c.CommandDisposeFailure = commandCleanup))));
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Fact]
    public async Task U08_async_cancellation_mid_stream_reaches_the_read_and_survives_disposal_failures()
    {
        using var cancel = new CancellationTokenSource();
        var context = Context(c =>
        {
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
            c.CommandDisposeFailure = new ApplicationException("command cleanup failed");
        });
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.BatchAsync(async s =>
        {
            await foreach (var _ in s.QueryUnbufferedAsync(Rows, cancellationToken: cancel.Token))
                if (++seen == 1) cancel.Cancel();
        }));
        Assert.Equal(1, seen);
        Assert.Equal([cancel.Token, cancel.Token], Only().ReadTokens);
        Assert.Equal(ReaderThenCommand(true), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U09_after_an_early_stop_the_first_cleanup_failure_surfaces_and_both_are_attempted(bool async)
    {
        var readerCleanup = new ApplicationException("reader cleanup failed");
        Assert.Same(readerCleanup, await Assert.ThrowsAsync<ApplicationException>(() => Consume(async, Context(c =>
        {
            c.ReaderDisposeFailure = readerCleanup;
            c.CommandDisposeFailure = new InvalidOperationException("command cleanup failed");
        }), take: 1)));
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
        _made.Clear();
        var commandCleanup = new ApplicationException("command cleanup failed");
        Assert.Same(commandCleanup, await Assert.ThrowsAsync<ApplicationException>(() =>
            Consume(async, Context(c => c.CommandDisposeFailure = commandCleanup), take: 1)));
        Assert.Equal(ReaderThenCommand(async), Only().DisposalLog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task U10_rows_keep_column_order_values_and_types(bool async)
    {
        const string sql = "SELECT 3 AS c, 'x' AS a, NULL AS b UNION ALL SELECT 4, 'y', 2.5";
        var rows = new List<IDictionary<string, object?>>();
        var context = Context();
        if (async)
            await context.BatchAsync(async s =>
            {
                await foreach (var row in s.QueryUnbufferedAsync(sql)) rows.Add((IDictionary<string, object?>)row);
            });
        else
            context.Batch(new Action<DbSession<FaultConnection>>(s =>
            {
                foreach (var row in s.QueryUnbuffered(sql)) rows.Add((IDictionary<string, object?>)row);
            }));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(["c", "a", "b"], row.Keys));
        Assert.Equal(new object?[] { 3L, "x", null }, rows[0].Values);
        Assert.Equal(new object?[] { 4L, "y", 2.5 }, rows[1].Values);
    }
}
