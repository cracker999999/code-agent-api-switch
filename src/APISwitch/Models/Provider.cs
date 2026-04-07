namespace APISwitch.Models;

public class Provider
{
    public int Id { get; set; }

    public int ToolType { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public int TestStatus { get; set; }
}
