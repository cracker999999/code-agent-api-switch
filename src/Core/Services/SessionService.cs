using System.IO;
using System.Text.Json;
using APISwitch.Models;
using APISwitch.Services.Parsers;

namespace APISwitch.Services;

/// <summary>
/// 会话服务 - 协调 Codex、Claude Code 和 Grok 会话的扫描、加载和删除
/// 解析逻辑已提取到独立的解析器模块
/// </summary>
public class SessionService
{
    public const string ProviderCodex = "codex";
    public const string ProviderClaude = "claude";
    public const string ProviderGrok = "grok";

    public static bool IsClaude(string? providerId) =>
        string.Equals(providerId, ProviderClaude, StringComparison.OrdinalIgnoreCase);

    public static bool IsCodex(string? providerId) =>
        string.Equals(providerId, ProviderCodex, StringComparison.OrdinalIgnoreCase);

    public static bool IsGrok(string? providerId) =>
        string.Equals(providerId, ProviderGrok, StringComparison.OrdinalIgnoreCase);

    public static int GetToolType(string? providerId)
    {
        if (IsClaude(providerId))
        {
            return 1;
        }

        if (IsGrok(providerId))
        {
            return 2;
        }

        return 0;
    }

    public static string GetProviderIdForToolType(int toolType)
    {
        return toolType switch
        {
            1 => ProviderClaude,
            2 => ProviderGrok,
            _ => ProviderCodex
        };
    }

    public static string NormalizeProviderId(string? providerId)
    {
        return GetProviderIdForToolType(GetToolType(providerId));
    }

    // 角色显示名两端共用，避免 WPF/Avalonia 各维护一份。
    public static string GetRoleDisplayName(string role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return "用户";
        }

        if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
        {
            return "developer";
        }

        if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return "工具";
        }

        if (string.Equals(role, "error", StringComparison.OrdinalIgnoreCase))
        {
            return "错误";
        }

        return "AI";
    }

    // 不同 CLI 的恢复子命令格式不同：Claude 用 `claude --resume <id>`，Codex 用 `codex resume <id>`，Grok 用 `grok -r <id>`。
    public static (string Command, string? WorkingDirectory) BuildResumeCommand(SessionMeta session)
    {
        var command = IsClaude(session.ProviderId)
            ? $"claude --resume {session.SessionId}"
            : IsGrok(session.ProviderId)
                ? $"grok -r {session.SessionId}"
                : $"codex resume {session.SessionId}";

        var workingDirectory = string.IsNullOrWhiteSpace(session.ProjectDir)
            ? null
            : session.ProjectDir;

        return (command, workingDirectory);
    }

    private readonly string _codexSessionsDirectory;
    private readonly string _claudeProjectsDirectory;
    private readonly string _grokSessionsDirectory;
    private readonly string _normalizedGrokSessionsRoot;
    private readonly CodexSessionParser _codexParser;
    private readonly ClaudeSessionParser _claudeParser;
    private readonly GrokSessionParser _grokParser;

    public SessionService(string? codexSessionsDirectory = null, string? claudeProjectsDirectory = null, string? grokSessionsDirectory = null)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _codexSessionsDirectory = codexSessionsDirectory ?? Path.Combine(userProfile, ".codex", "sessions");
        _claudeProjectsDirectory = claudeProjectsDirectory ?? Path.Combine(userProfile, ".claude", "projects");
        _grokSessionsDirectory = grokSessionsDirectory ?? Path.Combine(userProfile, ".grok", "sessions");

        // 预先规范化 Grok 会话目录路径,避免每次删除时重复计算
        _normalizedGrokSessionsRoot = Path.GetFullPath(_grokSessionsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // 初始化解析器
        _codexParser = new CodexSessionParser();
        _claudeParser = new ClaudeSessionParser();
        _grokParser = new GrokSessionParser();
    }

    public List<SessionMeta> ScanCodexSessions()
    {
        if (!Directory.Exists(_codexSessionsDirectory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(_codexSessionsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var session = _codexParser.ParseSession(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMeta> ScanClaudeSessions()
    {
        if (!Directory.Exists(_claudeProjectsDirectory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(_claudeProjectsDirectory, "*.jsonl", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var session = _claudeParser.ParseSession(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMeta> ScanGrokSessions()
    {
        if (!Directory.Exists(_grokSessionsDirectory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(_grokSessionsDirectory, "chat_history.jsonl", SearchOption.AllDirectories))
        {
            try
            {
                var session = _grokParser.ParseSession(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMessage> LoadMessages(string providerId, string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return new List<SessionMessage>();
        }

        if (IsCodex(providerId))
        {
            return _codexParser.LoadMessages(sourcePath);
        }

        if (IsClaude(providerId))
        {
            return _claudeParser.LoadMessages(sourcePath);
        }

        if (IsGrok(providerId))
        {
            return _grokParser.LoadMessages(sourcePath);
        }

        return new List<SessionMessage>();
    }

    public void DeleteSession(string providerId, string sessionId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        if (IsGrok(providerId))
        {
            DeleteGrokSession(sourcePath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }

        if (IsCodex(providerId))
        {
            return;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return;
        }

        var sidecarDirectory = Path.Combine(sourceDirectory, Path.GetFileNameWithoutExtension(sourcePath));
        if (Directory.Exists(sidecarDirectory))
        {
            Directory.Delete(sidecarDirectory, recursive: true);
        }
    }

    private void DeleteGrokSession(string sourcePath)
    {
        var sessionDirectory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }

            return;
        }

        var normalizedTarget = Path.GetFullPath(sessionDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!normalizedTarget.StartsWith(_normalizedGrokSessionsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Grok 会话目录不在允许的范围内");
        }

        if (Directory.Exists(sessionDirectory))
        {
            Directory.Delete(sessionDirectory, recursive: true);
            return;
        }

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
        }
    }
}
