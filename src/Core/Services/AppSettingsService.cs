using APISwitch.Models;

namespace APISwitch.Services;

// 全局应用设置:每个工具(Codex / Claude)各有一份"默认测试模型 / 端点路径 / prompt 文本"。
// 持久化到 SQLite 的 Settings 表,key 为 "<工具>.<字段>"。
// 内存缓存避免每次测试都查库;Save 时刷新缓存。
public class AppSettingsService
{
    private readonly DatabaseService _databaseService;
    private readonly object _syncRoot = new();
    private AppSettings? _cached;

    public AppSettingsService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public AppSettings Load()
    {
        lock (_syncRoot)
        {
            if (_cached is not null)
            {
                return Clone(_cached);
            }

            var defaults = AppSettings.CreateDefault();
            var stored = _databaseService.GetSettings(AllKeys);
            var loaded = new AppSettings
            {
                CodexTestModel = stored.GetValueOrDefault(SettingKeys.CodexTestModel) ?? defaults.CodexTestModel,
                CodexEndpointPath = stored.GetValueOrDefault(SettingKeys.CodexEndpointPath) ?? defaults.CodexEndpointPath,
                CodexPromptText = stored.GetValueOrDefault(SettingKeys.CodexPromptText) ?? defaults.CodexPromptText,
                CodexVersion = stored.GetValueOrDefault(SettingKeys.CodexVersion) ?? defaults.CodexVersion,
                ClaudeTestModel = stored.GetValueOrDefault(SettingKeys.ClaudeTestModel) ?? defaults.ClaudeTestModel,
                ClaudeEndpointPath = stored.GetValueOrDefault(SettingKeys.ClaudeEndpointPath) ?? defaults.ClaudeEndpointPath,
                ClaudePromptText = stored.GetValueOrDefault(SettingKeys.ClaudePromptText) ?? defaults.ClaudePromptText,
                ClaudeVersion = stored.GetValueOrDefault(SettingKeys.ClaudeVersion) ?? defaults.ClaudeVersion
            };
            _cached = loaded;
            return Clone(loaded);
        }
    }

    public void Save(AppSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        lock (_syncRoot)
        {
            // 空白回退到默认值,避免用户清空后留下无意义记录
            var defaults = AppSettings.CreateDefault();
            var normalized = new AppSettings
            {
                CodexTestModel = Coalesce(settings.CodexTestModel, defaults.CodexTestModel),
                CodexEndpointPath = NormalizePath(settings.CodexEndpointPath, defaults.CodexEndpointPath),
                CodexPromptText = Coalesce(settings.CodexPromptText, defaults.CodexPromptText),
                CodexVersion = Coalesce(settings.CodexVersion, defaults.CodexVersion),
                ClaudeTestModel = Coalesce(settings.ClaudeTestModel, defaults.ClaudeTestModel),
                ClaudeEndpointPath = NormalizePath(settings.ClaudeEndpointPath, defaults.ClaudeEndpointPath),
                ClaudePromptText = Coalesce(settings.ClaudePromptText, defaults.ClaudePromptText),
                ClaudeVersion = Coalesce(settings.ClaudeVersion, defaults.ClaudeVersion)
            };

            var updates = new Dictionary<string, string>
            {
                [SettingKeys.CodexTestModel] = normalized.CodexTestModel,
                [SettingKeys.CodexEndpointPath] = normalized.CodexEndpointPath,
                [SettingKeys.CodexPromptText] = normalized.CodexPromptText,
                [SettingKeys.CodexVersion] = normalized.CodexVersion,
                [SettingKeys.ClaudeTestModel] = normalized.ClaudeTestModel,
                [SettingKeys.ClaudeEndpointPath] = normalized.ClaudeEndpointPath,
                [SettingKeys.ClaudePromptText] = normalized.ClaudePromptText,
                [SettingKeys.ClaudeVersion] = normalized.ClaudeVersion
            };
            _databaseService.SetSettings(updates);

            _cached = normalized;
        }
    }

    private static string Coalesce(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    // 端点路径强制以 '/' 开头,允许带 query string(例如 /v1/messages?beta=true)。
    private static string NormalizePath(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var trimmed = value.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static AppSettings Clone(AppSettings src) => new()
    {
        CodexTestModel = src.CodexTestModel,
        CodexEndpointPath = src.CodexEndpointPath,
        CodexPromptText = src.CodexPromptText,
        CodexVersion = src.CodexVersion,
        ClaudeTestModel = src.ClaudeTestModel,
        ClaudeEndpointPath = src.ClaudeEndpointPath,
        ClaudePromptText = src.ClaudePromptText,
        ClaudeVersion = src.ClaudeVersion
    };

    private static class SettingKeys
    {
        public const string CodexTestModel = "Codex.TestModel";
        public const string CodexEndpointPath = "Codex.EndpointPath";
        public const string CodexPromptText = "Codex.PromptText";
        public const string CodexVersion = "Codex.Version";
        public const string ClaudeTestModel = "Claude.TestModel";
        public const string ClaudeEndpointPath = "Claude.EndpointPath";
        public const string ClaudePromptText = "Claude.PromptText";
        public const string ClaudeVersion = "Claude.Version";
    }

    private static readonly string[] AllKeys =
    {
        SettingKeys.CodexTestModel,
        SettingKeys.CodexEndpointPath,
        SettingKeys.CodexPromptText,
        SettingKeys.CodexVersion,
        SettingKeys.ClaudeTestModel,
        SettingKeys.ClaudeEndpointPath,
        SettingKeys.ClaudePromptText,
        SettingKeys.ClaudeVersion
    };
}
