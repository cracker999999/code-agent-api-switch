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

    public bool IsTestSuccess => TestStatus == 1;

    public bool IsTestFailed => TestStatus == 2;

    public string ActivateButtonText => IsActive ? "使用中" : "启用";

    public string TestModel { get; set; } = string.Empty;

    public string Remark { get; set; } = string.Empty;

    public bool CanMoveUp { get; set; }

    public bool CanMoveDown { get; set; }
}
