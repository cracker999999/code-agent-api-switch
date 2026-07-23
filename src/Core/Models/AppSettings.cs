namespace APISwitch.Models;

// 应用全局设置:Codex / Claude / Grok 各一套可定制的"默认测试模型 / 端点路径 / prompt 文本 / 客户端版本"。
public class AppSettings
{
    public string CodexTestModel { get; set; } = string.Empty;
    public string CodexEndpointPath { get; set; } = string.Empty;
    public string CodexPromptText { get; set; } = string.Empty;
    public string CodexVersion { get; set; } = string.Empty;
    public string ClaudeTestModel { get; set; } = string.Empty;
    public string ClaudeEndpointPath { get; set; } = string.Empty;
    public string ClaudePromptText { get; set; } = string.Empty;
    public string ClaudeVersion { get; set; } = string.Empty;
    public string GrokTestModel { get; set; } = string.Empty;
    public string GrokEndpointPath { get; set; } = string.Empty;
    public string GrokPromptText { get; set; } = string.Empty;
    public string GrokVersion { get; set; } = string.Empty;

    // 内置默认值:与历史硬编码完全一致,保证未配置时行为不变。
    public static AppSettings CreateDefault() => new()
    {
        CodexTestModel = "gpt-5.3-codex",
        CodexEndpointPath = "/responses",
        CodexPromptText = "你是什么模型",
        CodexVersion = "0.144.0",
        ClaudeTestModel = "claude-opus-4-6",
        ClaudeEndpointPath = "/v1/messages?beta=true",
        ClaudePromptText = "你是什么模型",
        ClaudeVersion = "2.1.152",
        GrokTestModel = "grok-4.5",
        GrokEndpointPath = "/responses",
        GrokPromptText = "你是什么模型",
        GrokVersion = "0.2.111"
    };
}
