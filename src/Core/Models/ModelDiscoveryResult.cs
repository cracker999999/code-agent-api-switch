namespace APISwitch.Models;

public class ModelDiscoveryResult
{
    public bool Success { get; set; }

    public string ErrorMessage { get; set; } = string.Empty;

    public List<string> Models { get; set; } = new();
}
