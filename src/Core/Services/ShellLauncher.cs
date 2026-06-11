using System.Diagnostics;
using System.IO;

namespace APISwitch.Services;

public enum OpenDirectoryStatus
{
    Ok,
    NotFound,
    Failed,
}

public readonly record struct OpenDirectoryResult(OpenDirectoryStatus Status, string? ErrorMessage);

public static class ShellLauncher
{
    // 用系统 shell 打开目录：Windows 走 explorer，macOS 走 open，Linux 走 xdg-open。
    public static OpenDirectoryResult OpenDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new OpenDirectoryResult(OpenDirectoryStatus.NotFound, null);
        }

        var trimmed = path.Trim();
        if (!Directory.Exists(trimmed))
        {
            return new OpenDirectoryResult(OpenDirectoryStatus.NotFound, null);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = trimmed,
                UseShellExecute = true,
            });
            return new OpenDirectoryResult(OpenDirectoryStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return new OpenDirectoryResult(OpenDirectoryStatus.Failed, ex.Message);
        }
    }
}
