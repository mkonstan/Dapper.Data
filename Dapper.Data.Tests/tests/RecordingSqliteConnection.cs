using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Dapper.Data.Tests;

/// <summary>Observes Dapper transaction assignment without mistaking SQLite's implicit enlistment for it.</summary>
public sealed class RecordingSqliteConnection : SqliteConnection
{
    public List<(string Sql, bool Assigned)> Executions { get; } = [];
    public RecordingSqliteConnection() { }

    protected override DbCommand CreateDbCommand()
    {
        using var original = base.CreateCommand();
        return new RecordingCommand(this, original.Transaction);
    }

    private sealed class RecordingCommand : SqliteCommand
    {
        private readonly RecordingSqliteConnection _owner;
        private bool _assigned;

        public RecordingCommand(RecordingSqliteConnection owner, SqliteTransaction? transaction)
        {
            _owner = owner;
            Connection = owner;
            Transaction = transaction;
        }

        protected override DbTransaction? DbTransaction
        {
            get => base.DbTransaction;
            set { _assigned = value is not null; base.DbTransaction = value; }
        }

        private void Record() => _owner.Executions.Add((CommandText, _assigned));
        public override int ExecuteNonQuery() { Record(); return base.ExecuteNonQuery(); }
        public override object? ExecuteScalar() { Record(); return base.ExecuteScalar(); }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        { Record(); return base.ExecuteDbDataReader(behavior); }
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        { Record(); return base.ExecuteNonQueryAsync(cancellationToken); }
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        { Record(); return base.ExecuteScalarAsync(cancellationToken); }
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        { Record(); return base.ExecuteDbDataReaderAsync(behavior, cancellationToken); }
    }
}
