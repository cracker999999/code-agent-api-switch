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

        EnsureProviderColumns(connection);
    }

    public List<Provider> GetProviders(int toolType)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT Id, ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder, TestStatus, TestModel, Remark
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
                    TestStatus = reader.GetInt32(7),
                    TestModel = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    Remark = reader.IsDBNull(9) ? string.Empty : reader.GetString(9)
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
INSERT INTO Providers (ToolType, Name, BaseUrl, ApiKey, IsActive, SortOrder, TestStatus, TestModel, Remark)
VALUES ($toolType, $name, $baseUrl, $apiKey, $isActive, $sortOrder, $testStatus, $testModel, $remark);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$toolType", provider.ToolType);
            command.Parameters.AddWithValue("$name", provider.Name);
            command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
            command.Parameters.AddWithValue("$apiKey", provider.ApiKey);
            command.Parameters.AddWithValue("$isActive", provider.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
            command.Parameters.AddWithValue("$testStatus", provider.TestStatus);
            command.Parameters.AddWithValue("$testModel", provider.TestModel);
            command.Parameters.AddWithValue("$remark", provider.Remark);

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
    TestStatus = $testStatus,
    TestModel = $testModel,
    Remark = $remark
WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", provider.Id);
            command.Parameters.AddWithValue("$name", provider.Name);
            command.Parameters.AddWithValue("$baseUrl", provider.BaseUrl);
            command.Parameters.AddWithValue("$apiKey", provider.ApiKey);
            command.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
            command.Parameters.AddWithValue("$testStatus", provider.TestStatus);
            command.Parameters.AddWithValue("$testModel", provider.TestModel);
            command.Parameters.AddWithValue("$remark", provider.Remark);
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

    public void MoveProviderUp(int id, int toolType)
    {
        MoveProvider(id, toolType, moveUp: true);
    }

    public void MoveProviderDown(int id, int toolType)
    {
        MoveProvider(id, toolType, moveUp: false);
    }

    private static void EnsureProviderColumns(SqliteConnection connection)
    {
        EnsureProviderColumn(
            connection,
            "TestStatus",
            "ALTER TABLE Providers ADD COLUMN TestStatus INTEGER NOT NULL DEFAULT 0;");
        EnsureProviderColumn(
            connection,
            "TestModel",
            "ALTER TABLE Providers ADD COLUMN TestModel TEXT;");
        EnsureProviderColumn(
            connection,
            "Remark",
            "ALTER TABLE Providers ADD COLUMN Remark TEXT;");
    }

    private static void EnsureProviderColumn(SqliteConnection connection, string columnName, string alterSql)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "PRAGMA table_info(Providers);";

        using (var reader = checkCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = alterSql;
        alterCommand.ExecuteNonQuery();
    }

    private void MoveProvider(int id, int toolType, bool moveUp)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();

            int? currentSortOrder;
            using (var currentCommand = connection.CreateCommand())
            {
                currentCommand.Transaction = transaction;
                currentCommand.CommandText = @"
SELECT SortOrder
FROM Providers
WHERE Id = $id AND ToolType = $toolType;";
                currentCommand.Parameters.AddWithValue("$id", id);
                currentCommand.Parameters.AddWithValue("$toolType", toolType);
                var result = currentCommand.ExecuteScalar();
                currentSortOrder = result is null || result is DBNull ? null : Convert.ToInt32(result);
            }

            if (currentSortOrder is null)
            {
                transaction.Rollback();
                return;
            }

            int? neighborId = null;
            int? neighborSortOrder = null;

            using (var neighborCommand = connection.CreateCommand())
            {
                neighborCommand.Transaction = transaction;
                neighborCommand.CommandText = moveUp
                    ? @"
SELECT Id, SortOrder
FROM Providers
WHERE ToolType = $toolType AND SortOrder < $sortOrder
ORDER BY SortOrder DESC, Id DESC
LIMIT 1;"
                    : @"
SELECT Id, SortOrder
FROM Providers
WHERE ToolType = $toolType AND SortOrder > $sortOrder
ORDER BY SortOrder ASC, Id ASC
LIMIT 1;";

                neighborCommand.Parameters.AddWithValue("$toolType", toolType);
                neighborCommand.Parameters.AddWithValue("$sortOrder", currentSortOrder.Value);

                using var neighborReader = neighborCommand.ExecuteReader();
                if (neighborReader.Read())
                {
                    neighborId = neighborReader.GetInt32(0);
                    neighborSortOrder = neighborReader.GetInt32(1);
                }
            }

            if (neighborId is null || neighborSortOrder is null)
            {
                transaction.Rollback();
                return;
            }

            using (var updateCurrent = connection.CreateCommand())
            {
                updateCurrent.Transaction = transaction;
                updateCurrent.CommandText = "UPDATE Providers SET SortOrder = $sortOrder WHERE Id = $id;";
                updateCurrent.Parameters.AddWithValue("$sortOrder", neighborSortOrder.Value);
                updateCurrent.Parameters.AddWithValue("$id", id);
                updateCurrent.ExecuteNonQuery();
            }

            using (var updateNeighbor = connection.CreateCommand())
            {
                updateNeighbor.Transaction = transaction;
                updateNeighbor.CommandText = "UPDATE Providers SET SortOrder = $sortOrder WHERE Id = $id;";
                updateNeighbor.Parameters.AddWithValue("$sortOrder", currentSortOrder.Value);
                updateNeighbor.Parameters.AddWithValue("$id", neighborId.Value);
                updateNeighbor.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }
}


