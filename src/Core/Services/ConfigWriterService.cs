using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.IO;
using APISwitch.Models;

namespace APISwitch.Services;

public class ConfigWriterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _codexConfigPath;
    private readonly string _codexAuthPath;
    private readonly string _claudeSettingsPath;
    private readonly string _grokConfigPath;

    public ConfigWriterService()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _codexConfigPath = Path.Combine(userProfile, ".codex", "config.toml");
        _codexAuthPath = Path.Combine(userProfile, ".codex", "auth.json");
        _claudeSettingsPath = Path.Combine(userProfile, ".claude", "settings.json");
        _grokConfigPath = Path.Combine(userProfile, ".grok", "config.toml");
    }

    public void ApplyProvider(Provider provider)
    {
        if (provider.ToolType == 0)
        {
            WriteCodexConfig(provider.BaseUrl);
            WriteCodexAuth(provider.ApiKey);
            return;
        }

        if (provider.ToolType == 1)
        {
            WriteClaudeSettings(provider.BaseUrl, provider.ApiKey);
            return;
        }

        if (provider.ToolType == 2)
        {
            WriteGrokConfig(provider.BaseUrl, provider.ApiKey);
            return;
        }

        throw new InvalidOperationException("未知的工具类型");
    }

    private void WriteCodexConfig(string baseUrl)
    {
        if (!File.Exists(_codexConfigPath))
        {
            throw new InvalidOperationException("请先安装 Codex");
        }

        BackupFile(_codexConfigPath);

        var content = File.ReadAllText(_codexConfigPath, Encoding.UTF8);
        var sectionMatch = Regex.Match(content, @"(?ms)^\[model_providers\.OpenAI\]\s*$.*?(?=^\[|\z)");
        if (!sectionMatch.Success)
        {
            throw new InvalidOperationException("config.toml 中未找到 [model_providers.OpenAI] 段");
        }

        var escapedBaseUrl = EscapeToml(baseUrl);
        var replacementLine = $"base_url = \"{escapedBaseUrl}\"";

        var updatedSection = Regex.Replace(
            sectionMatch.Value,
            @"(?m)^\s*base_url\s*=\s*""[^""]*""\s*$",
            replacementLine);

        if (updatedSection == sectionMatch.Value)
        {
            updatedSection = sectionMatch.Value.TrimEnd() + Environment.NewLine + replacementLine + Environment.NewLine;
        }

        var updatedContent = content.Remove(sectionMatch.Index, sectionMatch.Length)
            .Insert(sectionMatch.Index, updatedSection);
        File.WriteAllText(_codexConfigPath, updatedContent, new UTF8Encoding(false));
    }

    private void WriteCodexAuth(string apiKey)
    {
        EnsureDirectory(_codexAuthPath);
        if (File.Exists(_codexAuthPath))
        {
            BackupFile(_codexAuthPath);
        }

        var root = ReadJsonFile(_codexAuthPath);
        root["OPENAI_API_KEY"] = apiKey;
        WriteJsonFile(_codexAuthPath, root);
    }

    private void WriteClaudeSettings(string baseUrl, string apiKey)
    {
        EnsureDirectory(_claudeSettingsPath);
        if (File.Exists(_claudeSettingsPath))
        {
            BackupFile(_claudeSettingsPath);
        }

        var root = ReadJsonFile(_claudeSettingsPath);
        if (root["env"] is not JsonObject env)
        {
            env = new JsonObject();
            root["env"] = env;
        }

        env["ANTHROPIC_AUTH_TOKEN"] = apiKey;
        env["ANTHROPIC_BASE_URL"] = baseUrl;

        WriteJsonFile(_claudeSettingsPath, root);
    }

    private void WriteGrokConfig(string baseUrl, string apiKey)
    {
        EnsureDirectory(_grokConfigPath);

        string content;
        if (File.Exists(_grokConfigPath))
        {
            BackupFile(_grokConfigPath);
            content = File.ReadAllText(_grokConfigPath, Encoding.UTF8);
        }
        else
        {
            content = string.Empty;
        }

        content = UpsertTomlSectionValue(content, "endpoints", "models_base_url", baseUrl);
        content = UpsertTomlSectionValue(content, "endpoints", "xai_api_base_url", baseUrl);
        content = UpsertTomlSectionValue(content, "model.\"grok-4.5\"", "api_key", apiKey);

        File.WriteAllText(_grokConfigPath, content, new UTF8Encoding(false));
    }

    private static JsonObject ReadJsonFile(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var raw = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new JsonObject();
        }

        var node = JsonNode.Parse(raw) as JsonObject;
        return node ?? new JsonObject();
    }

    private static void WriteJsonFile(string path, JsonObject jsonObject)
    {
        var payload = jsonObject.ToJsonString(JsonOptions);
        File.WriteAllText(path, payload, new UTF8Encoding(false));
    }

    private static void BackupFile(string path)
    {
        File.Copy(path, path + ".bak", true);
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string EscapeToml(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string UpsertTomlSectionValue(string content, string sectionName, string key, string value)
    {
        var line = $"{key} = \"{EscapeToml(value)}\"";
        if (string.IsNullOrWhiteSpace(content))
        {
            return $"[{sectionName}]" + Environment.NewLine + line + Environment.NewLine;
        }

        var sectionPattern = $@"(?m)^\s*\[\s*{Regex.Escape(sectionName)}\s*\]\s*$";
        var sectionMatch = Regex.Match(content, sectionPattern);
        if (!sectionMatch.Success)
        {
            return content.TrimEnd()
                + Environment.NewLine
                + Environment.NewLine
                + $"[{sectionName}]"
                + Environment.NewLine
                + line
                + Environment.NewLine;
        }

        var bodyStart = sectionMatch.Index + sectionMatch.Length;
        var nextSectionMatch = Regex.Match(content[bodyStart..], @"(?m)^\s*\[");
        var bodyEnd = nextSectionMatch.Success
            ? bodyStart + nextSectionMatch.Index
            : content.Length;
        var beforeSection = content[..sectionMatch.Index];
        var section = content[sectionMatch.Index..bodyEnd];
        var afterSection = content[bodyEnd..];

        var pattern = $@"(?m)^\s*{Regex.Escape(key)}\s*=.*$";
        if (Regex.IsMatch(section, pattern))
        {
            section = Regex.Replace(section, pattern, line);
        }
        else
        {
            section = section.TrimEnd() + Environment.NewLine + line + Environment.NewLine;
        }

        // 只在目标 section 内更新，避免误改其他 model 的同名字段。
        return beforeSection + section + afterSection;
    }
}


