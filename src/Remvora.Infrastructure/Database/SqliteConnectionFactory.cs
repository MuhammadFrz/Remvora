using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Remvora.Infrastructure.Database;

/// <summary>
/// Factory creating database connections for SQLite.
/// </summary>
public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}

/// <summary>
/// Production SQLite connection factory.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var remvoraDir = Path.Combine(localAppData, "Remvora");
            Directory.CreateDirectory(remvoraDir);
            var dbPath = Path.Combine(remvoraDir, "remvora.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }
        else
        {
            if (databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                _connectionString = "Data Source=:memory:;Mode=Memory;Cache=Shared";
            }
            else
            {
                var dir = Path.GetDirectoryName(databasePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                _connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString();
            }
        }
    }

    public DbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
