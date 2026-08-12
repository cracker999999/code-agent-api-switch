using APISwitch.Models;

namespace APISwitch.Extensions;

public static class AppSettingsExtensions
{
    public static string GetTestModelByToolType(this AppSettings settings, int toolType)
    {
        return toolType switch
        {
            0 => settings.CodexTestModel,
            1 => settings.ClaudeTestModel,
            2 => settings.GrokTestModel,
            _ => string.Empty
        };
    }
}
