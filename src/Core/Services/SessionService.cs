using System.IO;
using System.Text;
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

    // 三个 CLI 的会话都是"目录树下的 JSONL",只有根目录、文件名模式和要跳过的文件不同。
    private List<SessionMeta> Scan(
        string directory,
        string searchPattern,
        Func<string, SessionMeta?> parse,
        Func<string, bool>? shouldSkip = null)
    {
        if (!Directory.Exists(directory))
        {
            return new List<SessionMeta>();
        }

        var sessions = new List<SessionMeta>();
        foreach (var filePath in Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories))
        {
            if (shouldSkip?.Invoke(filePath) == true)
            {
                continue;
            }

            try
            {
                var session = parse(filePath);
                if (session is not null)
                {
                    sessions.Add(session);
                }
            }
            // 单个损坏或占用的会话文件不应中断整次扫描。
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                continue;
            }
        }

        return sessions
            .OrderByDescending(item => item.LastActiveAt)
            .ToList();
    }

    public List<SessionMeta> ScanCodexSessions() =>
        Scan(_codexSessionsDirectory, "*.jsonl", _codexParser.ParseSession);

    public List<SessionMeta> ScanClaudeSessions() =>
        Scan(
            _claudeProjectsDirectory,
            "*.jsonl",
            _claudeParser.ParseSession,
            // agent-*.jsonl 是子代理日志,不作为独立会话列出。
            filePath => Path.GetFileName(filePath).StartsWith("agent-", StringComparison.OrdinalIgnoreCase));

    public List<SessionMeta> ScanGrokSessions() =>
        Scan(_grokSessionsDirectory, "chat_history.jsonl", _grokParser.ParseSession);

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

    public (int RemovedRecords, string? BackupPath) CleanSession(string providerId, string sourcePath)
    {
        if (!IsCodex(providerId))
        {
            throw new InvalidOperationException("只有 Codex 会话支持清洗。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        sourcePath = Path.GetFullPath(sourcePath);

        // 保持读取句柄直到替换完成，禁止并发写入，同时允许原子替换文件。
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var originalLength = source.Length;
        var originalWriteTime = File.GetLastWriteTimeUtc(sourcePath);
        var temporaryPath = $"{sourcePath}.{Guid.NewGuid():N}.tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var removedRecords = 0;

        try
        {
            using (var reader = new StreamReader(source, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            using (var writer = new StreamWriter(new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write), encoding)
                   { NewLine = "\n" })
            {
                var lineNumber = 0;
                while (reader.ReadLine() is { } line)
                {
                    lineNumber++;
                    // 只在解析首行时忽略 UTF-8 BOM，保留的记录仍使用原文。
                    var jsonLine = lineNumber == 1 && line.StartsWith('\uFEFF') ? line[1..] : line;
                    if (!string.IsNullOrWhiteSpace(jsonLine))
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(jsonLine);
                            if (IsEncryptedCodexRecord(document.RootElement))
                            {
                                removedRecords++;
                                continue;
                            }
                        }
                        catch (JsonException ex)
                        {
                            // 原文件必须完整解析成功后才可替换，不能静默丢弃损坏的对话。
                            throw new InvalidDataException($"会话第 {lineNumber} 行不是有效 JSON，已取消清洗。", ex);
                        }
                    }

                    writer.WriteLine(line);
                }
            }

            if (removedRecords == 0)
            {
                return (0, null);
            }

            var currentFile = new FileInfo(sourcePath);
            if (currentFile.Length != originalLength || currentFile.LastWriteTimeUtc != originalWriteTime)
            {
                throw new IOException("会话文件在清洗期间发生变化，请关闭该会话后重试。");
            }

            // 清洗不应改变会话活动时间；每次保留独立备份，避免覆盖更早的原始内容。
            File.SetLastWriteTimeUtc(temporaryPath, originalWriteTime);
            var backupPath = $"{sourcePath}.{Guid.NewGuid():N}.bak";
            File.Replace(temporaryPath, sourcePath, backupPath);
            return (removedRecords, backupPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static bool IsEncryptedCodexRecord(JsonElement record)
    {
        if (!JsonFieldExtractor.TryGetString(record, "type", out var recordType) ||
            !JsonFieldExtractor.TryGetProperty(record, "payload", out var payload))
        {
            return false;
        }

        var isEncryptedItem = recordType == "response_item" &&
            JsonFieldExtractor.TryGetString(payload, "type", out var itemType) &&
            itemType is "reasoning" or "compaction";

        // 压缩快照会覆盖前文；整条移除后才能重新使用原始历史，且不能留下缺少密文的 compaction 项。
        return (isEncryptedItem || recordType == "compacted") && ContainsEncryptedContent(payload);
    }

    private static bool ContainsEncryptedContent(JsonElement element)
    {
        // 按 JSON 属性匹配，避免误删正文中仅提到 encrypted_content 的记录。
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(property =>
                property.NameEquals("encrypted_content") || ContainsEncryptedContent(property.Value)),
            JsonValueKind.Array => element.EnumerateArray().Any(ContainsEncryptedContent),
            _ => false
        };
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

        if (IsCodex(providerId))
        {
            DeleteCodexSession(sessionId, sourcePath);
            return;
        }

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
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

    private void DeleteCodexSession(string sessionId, string sourcePath)
    {
        var roots = CodexSessionHierarchy.Build(ScanCodexSessions());
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var sourcePaths = new List<string>();
        var collectedPaths = new HashSet<string>(pathComparer);
        var target = FindCodexSessionNode(roots, sourcePath, pathComparer);

        if (target is not null)
        {
            CollectCodexSessionSourcePaths(target, sourcePaths, collectedPaths);
        }
        else if (!string.IsNullOrWhiteSpace(sessionId))
        {
            // 主文件若在删除前已消失，扫描结果中的直属孤儿仍应随原主会话删除。
            foreach (var root in roots)
            {
                if (root.Session.IsSubagent &&
                    string.Equals(root.Session.ParentSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    CollectCodexSessionSourcePaths(root, sourcePaths, collectedPaths);
                }
            }
        }

        if (collectedPaths.Add(sourcePath))
        {
            sourcePaths.Add(sourcePath);
        }

        // 后代优先删除；任一子代理删除失败时保留尚未删除的父会话。
        foreach (var path in sourcePaths)
        {
            // 先删备份，失败时保留会话文件，避免列表项消失后无法重试。
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var backupPrefix = Path.GetFileName(path) + ".";
            if (Directory.Exists(directory))
            {
                foreach (var backupPath in Directory.EnumerateFiles(directory, backupPrefix + "*.bak"))
                {
                    var backupStem = Path.GetFileNameWithoutExtension(backupPath);
                    // 精确匹配原文件名和本工具的 GUID，避免误删相近文件名的备份。
                    if (backupStem.Length > backupPrefix.Length &&
                        pathComparer.Equals(backupStem[..backupPrefix.Length], backupPrefix) &&
                        Guid.TryParseExact(backupStem.AsSpan(backupPrefix.Length), "N", out _))
                    {
                        File.Delete(backupPath);
                    }
                }
            }

            File.Delete(path);
        }
    }

    private static CodexSessionNode? FindCodexSessionNode(
        IReadOnlyList<CodexSessionNode> nodes,
        string sourcePath,
        StringComparer pathComparer)
    {
        foreach (var node in nodes)
        {
            if (pathComparer.Equals(node.Session.SourcePath, sourcePath))
            {
                return node;
            }

            var descendant = FindCodexSessionNode(node.Children, sourcePath, pathComparer);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void CollectCodexSessionSourcePaths(
        CodexSessionNode node,
        ICollection<string> sourcePaths,
        ISet<string> collectedPaths)
    {
        foreach (var child in node.Children)
        {
            CollectCodexSessionSourcePaths(child, sourcePaths, collectedPaths);
        }

        if (!string.IsNullOrWhiteSpace(node.Session.SourcePath) && collectedPaths.Add(node.Session.SourcePath))
        {
            sourcePaths.Add(node.Session.SourcePath);
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
