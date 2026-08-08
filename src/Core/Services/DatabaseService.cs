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
);
CREATE TABLE IF NOT EXISTS Settings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Prompts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Content TEXT NOT NULL
);";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();

        EnsureProviderColumns(connection);
    }

    // Settings 批量读取:单连接、单 SELECT。未命中的键不出现在结果中,由上层兜底默认值。
    public Dictionary<string, string> GetSettings(IReadOnlyCollection<string> keys)
    {
        var result = new Dictionary<string, string>(keys.Count);
        if (keys.Count == 0) return result;

        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            var paramNames = new List<string>(keys.Count);
            var index = 0;
            foreach (var key in keys)
            {
                var name = $"$k{index++}";
                paramNames.Add(name);
                command.Parameters.AddWithValue(name, key);
            }
            command.CommandText = $"SELECT Key, Value FROM Settings WHERE Key IN ({string.Join(",", paramNames)});";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return result;
    }

    // Settings 批量写入:单连接、单事务,保证多键更新的原子性。
    public void SetSettings(IReadOnlyDictionary<string, string> updates)
    {
        if (updates.Count == 0) return;

        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO Settings (Key, Value) VALUES ($key, $value)
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";

            var keyParam = command.Parameters.Add("$key", Microsoft.Data.Sqlite.SqliteType.Text);
            var valueParam = command.Parameters.Add("$value", Microsoft.Data.Sqlite.SqliteType.Text);

            foreach (var (key, value) in updates)
            {
                keyParam.Value = key;
                valueParam.Value = value;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public List<PromptItem> GetPrompts()
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Content FROM Prompts ORDER BY Id DESC;";

            using var reader = command.ExecuteReader();
            var prompts = new List<PromptItem>();
            while (reader.Read())
            {
                prompts.Add(new PromptItem
                {
                    Id = reader.GetInt32(0),
                    Content = reader.GetString(1)
                });
            }

            return prompts;
        }
    }

    public int AddPrompt(PromptItem prompt)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO Prompts (Content) VALUES ($content);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$content", prompt.Content);
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public void UpdatePrompt(PromptItem prompt)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Prompts SET Content = $content WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", prompt.Id);
            command.Parameters.AddWithValue("$content", prompt.Content);
            command.ExecuteNonQuery();
        }
    }

    public void DeletePrompt(int id)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Prompts WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
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
        MoveProvider(id, toolType, currentIndex => currentIndex - 1);
    }

    public void MoveProviderDown(int id, int toolType)
    {
        MoveProvider(id, toolType, currentIndex => currentIndex + 1);
    }

    public void MoveProviderToIndex(int id, int toolType, int destinationIndex)
    {
        MoveProvider(id, toolType, _ => destinationIndex);
    }

    private void MoveProvider(int id, int toolType, Func<int, int> getDestinationIndex)
    {
        lock (_syncRoot)
        {
            using var connection = CreateConnection();
            connection.Open();

            using var transaction = connection.BeginTransaction();
            var providerOrders = new List<(int Id, int SortOrder)>();

            using (var queryCommand = connection.CreateCommand())
            {
                queryCommand.Transaction = transaction;
                queryCommand.CommandText = @"
SELECT Id, SortOrder
FROM Providers
WHERE ToolType = $toolType
ORDER BY SortOrder ASC, Id ASC;";
                queryCommand.Parameters.AddWithValue("$toolType", toolType);

                using var reader = queryCommand.ExecuteReader();
                while (reader.Read())
                {
                    providerOrders.Add((reader.GetInt32(0), reader.GetInt32(1)));
                }
            }

            var currentIndex = providerOrders.FindIndex(provider => provider.Id == id);
            if (currentIndex < 0 || providerOrders.Count < 2)
            {
                transaction.Rollback();
                return;
            }

            var destinationIndex = Math.Clamp(getDestinationIndex(currentIndex), 0, providerOrders.Count - 1);
            if (destinationIndex == currentIndex)
            {
                transaction.Rollback();
                return;
            }

            var movedProvider = providerOrders[currentIndex];
            providerOrders.RemoveAt(currentIndex);
            providerOrders.Insert(destinationIndex, movedProvider);

            // 移动（包括拖拽）可能跨越多条记录，统一归一化顺序可同时消除历史重复 SortOrder。
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = @"
UPDATE Providers
SET SortOrder = $sortOrder
WHERE Id = $id AND ToolType = $toolType;";
            var sortOrderParameter = updateCommand.Parameters.Add("$sortOrder", SqliteType.Integer);
            var idParameter = updateCommand.Parameters.Add("$id", SqliteType.Integer);
            updateCommand.Parameters.AddWithValue("$toolType", toolType);

            for (var index = 0; index < providerOrders.Count; index++)
            {
                var sortOrder = index + 1;
                if (providerOrders[index].SortOrder == sortOrder)
                {
                    continue;
                }

                // 正常顺序下只更新移动范围；历史重复或断号仍会被归一化。
                sortOrderParameter.Value = sortOrder;
                idParameter.Value = providerOrders[index].Id;
                updateCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
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

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_databasePath}");
    }
}
