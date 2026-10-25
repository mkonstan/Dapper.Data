using Dapper;
using Microsoft.Data.Sqlite;

namespace Dapper.Data.Tests;

/// <summary>Owns an isolated shared-memory database for exactly one test instance.</summary>
public sealed class DatabaseFixture : IDisposable
{
    public string ConnectionString { get; } = $"Data Source={Guid.NewGuid()};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    public DbContext<SqliteConnection> Context => new(ConnectionString);
    public DbContext<SqliteConnection> Insensitive => new(ConnectionString, ColumnNameMatching.CaseInsensitive);

    public DatabaseFixture()
    {
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();
        _keepAlive.Execute("""
            CREATE TABLE People(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Amount REAL, Note TEXT);
            INSERT INTO People VALUES(1,'Ann',1.5,NULL),(2,'Bob',2.0,'x');
            """);
    }

    public void Dispose() => _keepAlive.Dispose();
}
