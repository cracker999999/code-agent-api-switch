namespace APISwitch.Models;

public class SessionMessage
{
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }
}
