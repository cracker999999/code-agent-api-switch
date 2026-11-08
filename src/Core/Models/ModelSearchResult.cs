namespace APISwitch.Models;

public sealed record ModelSearchResult(Provider Provider, IReadOnlyList<string> MatchingModels, string ErrorMessage)
{
    public bool IsMatch => MatchingModels.Count > 0;

    public string MatchingModelsText => string.Join(", ", MatchingModels);

    public string ToolName => Provider.ToolType switch
    {
        0 => "Codex",
        1 => "Claude Code",
        2 => "Grok",
        _ => "未知"
    };
}
