namespace APISwitch.Models;

public class ApiTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public long? ResponseTimeMs { get; set; }
}
