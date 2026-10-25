using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dapper.Data.Tests;

public sealed class FailurePrecedenceTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CallbackFailureSurvivesProviderCleanupFailure()
    {
        var ctx = new DbContext<CleanupFailureConnection>(_db.ConnectionString);
        var boom = new ApplicationException("original callback error");
        var error = Assert.Throws<ApplicationException>(() => ctx.Batch((Action<DbSession<CleanupFailureConnection>>)(s =>
        {
            s.BeginTransaction();
            s.Execute("INSERT INTO People(Name) VALUES('C')");
            s.Connection.FailCleanup = true;
            throw boom;
        })));
        Assert.Same(boom, error);
        Assert.Equal(2L, _db.Context.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
        var asyncError = await Assert.ThrowsAsync<ApplicationException>(() => ctx.BatchAsync(async s =>
        {
            await s.BeginTransactionAsync();
            await s.ExecuteAsync("INSERT INTO People(Name) VALUES('C')");
            s.Connection.FailCleanup = true;
            throw boom;
        }));
        Assert.Same(boom, asyncError);
        Assert.Equal(2L, _db.Context.ExecuteScalar<long>("SELECT COUNT(*) FROM People"));
    }

    [Fact]
    public void RecorderDistinguishesImplicitFromExplicitEnlistment()
    {
        var ctx = new DbContext<RecordingSqliteConnection>(_db.ConnectionString);
        ctx.Batch(s =>
        {
            s.BeginTransaction();
            using (var command = ((DbConnection)s.Connection).CreateCommand())
            {
                command.CommandText = "SELECT 1 /* implicit */";
                Assert.NotNull(command.Transaction);
                command.ExecuteScalar();
            }
            s.Connection.ExecuteScalar<long>("SELECT 1 /* explicit */", transaction: s.Transaction);
            Assert.Contains(s.Connection.Executions, r => r.Sql.Contains("implicit") && !r.Assigned);
            Assert.Contains(s.Connection.Executions, r => r.Sql.Contains("explicit") && r.Assigned);
            s.RollbackTransaction();
        });
    }

    public sealed class CleanupFailureConnection : SqliteConnection
    {
        public bool FailCleanup { get; set; }
        public CleanupFailureConnection() { }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && FailCleanup) throw new InvalidOperationException("cleanup failure");
        }
        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
