namespace APISwitch.Models;

public class SessionMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> ImageDataUrls { get; set; } = new();

    public List<SessionPlanStep>? Plan { get; set; }

    public DateTime? Timestamp { get; set; }
}

public sealed record SessionPlanStep(string Step, string Status)
{
    public bool IsInProgress => Status == "in_progress";

    public string Marker => Status == "completed" ? "☑" : "☐";
}
