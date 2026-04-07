using APISwitch.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace APISwitch.Services;

public class DatabaseService
{
    private readonly string _databasePath;
    private readonly object _syncRoot = new();

    public DatabaseService(string? databasePath = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _databasePath = databasePath ?? Path.Combine(userProfile, ".APISwitch", "apiswitch.db");
    }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = CreateConnection();
        connection.Open();

        const string sql = @"
CREATE TABLE IF NOT EXISTS Providers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ToolType INTEGER NOT NULL,
    Name TEXT NOT NULL,
    BaseUrl TEXT NOT NULL,
    ApiKey TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    TestStatus INTEGER NOT NULL DEFAULT 0
);";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();

        EnsureTestStatusColumn(connection);
    }

    public List<Provider> GetProviders(int toolType)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder, TestStatus
FROM Providers
WHERE ToolType = $toolType
ORDER BY SortOrder ASC, Id ASC;";
            command.Parameters.AddWithValue("$toolType", toolType);

            using var reader = command.ExecuteReader();
            var providers = new List<Provider>();
            while (reader.Read())
            {
                providers.Add(new Provider
                {
                    Id = reader.GetInt32(0),
                    ToolType = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    BaseUrl = reader.GetString(3),
                    ApiKey = reader.GetString(4),
                    IsActive = reader.GetInt32(5) == 1,
                    SortOrder = reader.GetInt32(6),
                    TestStatus = reader.GetInt32(7)
                });
            }

            return providers;
        }
    }

    public int AddProvider(Provider provider)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO Providers (ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder, TestStatus)
VALUES ($toolType, $name, $baseUrl, $apiKey, $isActive, $sortOrder, $testStatus);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$toolType", provider.ToolType);
            command.Parameters.AddWithValue("$name", provider.Name);
            command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
            command.Parameters.AddWithValue("$apiKey", provider.ApiKey);
            command.Parameters.AddWithValue("$isActive", provider.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
            command.Parameters.AddWithValue("$testStatus", provider.TestStatus);

            var insertedId = command.ExecuteScalar();
            return Convert.ToInt32(insertedId);
        }
    }

    public void UpdateProvider(Provider provider)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE Providers
SET Name = $name,
    BaseUrl = $baseUrl,
    ApiKey = $apiKey,
    SortOrder = $sortOrder,
    TestStatus = $testStatus
WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", provider.Id);
            command.Parameters.AddWithValue("$name", provider.Name);
            command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
            command.Parameters.AddWithValue("$apiKey", provider.ApiKey);
            command.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
            command.Parameters.AddWithValue("$testStatus", provider.TestStatus);
            command.ExecuteNonQuery();
        }
    }

    public void DeleteProvider(int id)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Providers WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void ActivateProvider(int id, int toolType)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            using (var resetCommand = connection.CreateCommand())
            {
                resetCommand.Transaction = transaction;
                resetCommand.CommandText = "UPDATE Providers SET IsActive = 0 WHERE ToolType = $toolType;";
                resetCommand.Parameters.AddWithValue("$toolType", toolType);
                resetCommand.ExecuteNonQuery();
            }

            using (var activateCommand = connection.CreateCommand())
            {
                activateCommand.Transaction = transaction;
                activateCommand.CommandText = "UPDATE Providers SET IsActive = 1 WHERE Id = $id AND ToolType = $toolType;";
                activateCommand.Parameters.AddWithValue("$id", id);
                activateCommand.Parameters.AddWithValue("$toolType", toolType);
                var affected = activateCommand.ExecuteNonQuery();
                if (affected == 0)
                {
                    throw new InvalidOperationException("未找到可激活的供应商记录");
                }
            }

            transaction.Commit();
        }
    }

    public void UpdateTestStatus(int id, int testStatus)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Providers SET TestStatus = $testStatus WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$testStatus", testStatus);
            command.ExecuteNonQuery();
        }
    }

    private static void EnsureTestStatusColumn(SqliteConnection connection)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "PRAGMA table_info(Providers);";

        var hasColumn = false;
        using (var reader = checkCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "TestStatus", StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn)
        {
            return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE Providers ADD COLUMN TestStatus INTEGER NOT NULL DEFAULT 0;";
        alterCommand.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }
}


