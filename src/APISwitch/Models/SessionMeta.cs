namespace APISwitch.Models;

public class SessionMeta
{
    public string ProviderId { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ProjectDir { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime LastActiveAt { get; set; }

    public string SourcePath { get; set; } = string.Empty;
}
