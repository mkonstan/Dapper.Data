using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

/// <summary>Pins 3.1.2: a streamed query disposes its reader and then its command exactly once, sync and async, and a
/// failure while reading is never replaced by a failure while closing.</summary>
/// <remarks>Before 3.1.2 the session relied on Dapper's wrapped reader: its DisposeAsync never disposed the command,
/// its Dispose skipped the command when the reader's own disposal threw, and either disposal failure replaced a read
/// failure already in flight.</remarks>
public sealed class UnbufferedCleanupTests
{
    private const string Rows = "SELECT 1 AS Id UNION ALL SELECT 2 UNION ALL SELECT 3";
    private readonly List<FaultConnection> _made = [];

    /// <summary>A SQLite connection whose commands and readers count their disposals and can be made to fail.</summary>
    private sealed class FaultConnection : SqliteConnection
    {
        public int ReadFailAt { get; set; } = -1;
        public Exception? ReadFailure { get; set; }
        public Exception? ReaderDisposeFailure { get; set; }
        public Exception? CommandDisposeFailure { get; set; }
        public List<string> Disposed { get; } = [];
        public int ReaderDisposals => Disposed.Count(d => d == "reader");
        public int CommandDisposals => Disposed.Count(d => d == "command");
        public FaultConnection() { }
        protected override DbCommand CreateDbCommand() => new FaultCommand(base.CreateDbCommand(), this);
    }

    /// <summary>Forwards to the provider command; counts and optionally fails its disposal.</summary>
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
            owner.Disposed.Add("command");
            if (owner.CommandDisposeFailure is { } failure) throw failure;
        }
    }

    /// <summary>Forwards to the provider reader; can fail one read and its disposal, and counts disposals.</summary>
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
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
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
            if (++_reads == owner.ReadFailAt) throw owner.ReadFailure!;
            return inner.ReadAsync(cancellationToken);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            inner.Dispose();
            owner.Disposed.Add("reader");
            if (owner.ReaderDisposeFailure is { } failure) throw failure;
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

    private static Task StreamAsync(DbContext<FaultConnection> context, string sql = Rows, int take = int.MaxValue) =>
        context.BatchAsync(async s =>
        {
            var seen = 0;
            await foreach (var _ in s.QueryUnbufferedAsync(sql))
                if (++seen == take) break;
        });

    private static void Stream(DbContext<FaultConnection> context, string sql = Rows, int take = int.MaxValue) =>
        context.Batch(new Action<DbSession<FaultConnection>>(s =>
        {
            var seen = 0;
            foreach (var _ in s.QueryUnbuffered(sql))
                if (++seen == take) break;
        }));

    [Fact]
    public async Task U01_async_read_failure_survives_reader_disposal_failure()
    {
        var read = new IOException("read failed");
        var thrown = await Assert.ThrowsAsync<IOException>(() => StreamAsync(Context(c =>
        {
            c.ReadFailAt = 2;
            c.ReadFailure = read;
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
        })));
        Assert.Same(read, thrown);
        Assert.Contains("FaultReader.ReadAsync(", thrown.StackTrace);
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }

    [Fact]
    public async Task U02_async_duplicate_columns_survive_reader_disposal_failure()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => StreamAsync(
            Context(c => c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed")), "SELECT 1 AS a, 2 AS a"));
        Assert.StartsWith("RowReader: duplicate column 'a'", thrown.Message);
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }

    [Fact]
    public async Task U03_async_read_failure_survives_command_disposal_failure()
    {
        var read = new IOException("read failed");
        var thrown = await Assert.ThrowsAsync<IOException>(() => StreamAsync(Context(c =>
        {
            c.ReadFailAt = 1;
            c.ReadFailure = read;
            c.CommandDisposeFailure = new ApplicationException("command cleanup failed");
        })));
        Assert.Same(read, thrown);
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    public async Task U04_async_full_or_early_stop_disposes_reader_then_command_once(int take)
    {
        await StreamAsync(Context(), take: take);
        Assert.Equal(["reader", "command"], Only().Disposed);
    }

    [Fact]
    public async Task U05_async_after_success_the_first_cleanup_failure_surfaces_and_both_are_attempted()
    {
        var readerCleanup = new ApplicationException("reader cleanup failed");
        Assert.Same(readerCleanup, await Assert.ThrowsAsync<ApplicationException>(() => StreamAsync(Context(c =>
        {
            c.ReaderDisposeFailure = readerCleanup;
            c.CommandDisposeFailure = new InvalidOperationException("command cleanup failed");
        }))));
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
        _made.Clear();
        var commandCleanup = new ApplicationException("command cleanup failed");
        Assert.Same(commandCleanup, await Assert.ThrowsAsync<ApplicationException>(() =>
            StreamAsync(Context(c => c.CommandDisposeFailure = commandCleanup))));
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }

    [Fact]
    public void U06_sync_read_failure_survives_reader_disposal_failure()
    {
        var read = new IOException("read failed");
        var thrown = Assert.Throws<IOException>(() => Stream(Context(c =>
        {
            c.ReadFailAt = 2;
            c.ReadFailure = read;
            c.ReaderDisposeFailure = new ApplicationException("reader cleanup failed");
        })));
        Assert.Same(read, thrown);
        Assert.Contains("FaultReader.Read(", thrown.StackTrace);
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    public void U07_sync_full_or_early_stop_disposes_reader_then_command_once(int take)
    {
        Stream(Context(), take: take);
        Assert.Equal(["reader", "command"], Only().Disposed);
    }

    [Fact]
    public void U08_sync_reader_disposal_failure_after_success_surfaces_and_the_command_is_still_disposed()
    {
        var readerCleanup = new ApplicationException("reader cleanup failed");
        Assert.Same(readerCleanup, Assert.Throws<ApplicationException>(() =>
            Stream(Context(c => c.ReaderDisposeFailure = readerCleanup))));
        Assert.Equal((1, 1), (Only().ReaderDisposals, Only().CommandDisposals));
    }
}
