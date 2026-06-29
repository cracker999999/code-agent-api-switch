using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace APISwitch.Services;

public enum OpenDirectoryStatus
{
    Ok,
    NotFound,
    Failed,
}

public readonly record struct OpenDirectoryResult(OpenDirectoryStatus Status, string? ErrorMessage);

public enum OpenTerminalStatus
{
    Ok,
    Failed,
}

public readonly record struct OpenTerminalResult(OpenTerminalStatus Status, string? ErrorMessage);

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

    public static OpenTerminalResult OpenTerminalCommand(string command, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new OpenTerminalResult(OpenTerminalStatus.Failed, "命令不能为空");
        }

        var normalizedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : workingDirectory.Trim();

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows 这里必须传真实目录，不能传 %V 这类仅限资源管理器上下文菜单的占位符。
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c wt -w 0 new-tab -d \"{normalizedWorkingDirectory}\" pwsh -NoExit -Command {command}",
                    UseShellExecute = true,
                });
                return new OpenTerminalResult(OpenTerminalStatus.Ok, null);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var terminalCommand = $"cd {ShellQuote(normalizedWorkingDirectory)}; clear; exec {command}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add($"tell application id \"com.apple.Terminal\" to do script \"{EscapeAppleScriptString(terminalCommand)}\"");

                Process.Start(startInfo);
                return new OpenTerminalResult(OpenTerminalStatus.Ok, null);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "sh",
                Arguments = "-lc \"" + command.Replace("\"", "\\\"") + "\"",
                WorkingDirectory = normalizedWorkingDirectory,
                UseShellExecute = false,
            });
            return new OpenTerminalResult(OpenTerminalStatus.Ok, null);
        }
        catch (Exception ex)
        {
            return new OpenTerminalResult(OpenTerminalStatus.Failed, ex.Message);
        }
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }
}
