namespace APISwitch.Models;

public class SessionMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> ImageDataUrls { get; set; } = new();

    public DateTime Timestamp { get; set; }
}
