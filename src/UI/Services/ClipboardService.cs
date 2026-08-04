using Avalonia.Controls;

namespace APISwitch.UI.Services;

internal static class ClipboardService
{
    public static async Task CopyTextAsync(Window owner, string content)
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is null)
        {
            await DialogService.ShowErrorAsync(owner, "错误", "复制失败：无法访问剪贴板");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(content);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(owner, "错误", $"复制失败：{ex.Message}");
        }
    }
}
