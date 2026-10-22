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
                // 与 MacFinderActions 保持一致：先通过系统服务创建 Terminal 标签页，避免命令进入当前忙碌会话。
                var openedTerminalTab = TryOpenMacTerminalTab(normalizedWorkingDirectory);
                var appleScript = openedTerminalTab
                    ? $"""
                      tell application id "com.apple.Terminal"
                          activate
                          delay 0.3
                          do script "{EscapeAppleScriptString(terminalCommand)}" in front window
                      end tell
                      """
                    : $"""
                      tell application id "com.apple.Terminal"
                          activate
                          do script "{EscapeAppleScriptString(terminalCommand)}"
                      end tell
                      """;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add(appleScript);

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

    private static bool TryOpenMacTerminalTab(string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/osascript",
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("JavaScript");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(OpenMacTerminalTabScript);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(workingDirectory);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && string.Equals(output.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private const string OpenMacTerminalTabScript = """
        ObjC.import("AppKit");
        ObjC.import("Foundation");

        function run(argv) {
            const directory = argv[0];
            const pasteboard = $.NSPasteboard.pasteboardWithName("com.leen.APISwitch.terminal-tab");
            pasteboard.clearContents;

            const item = $.NSPasteboardItem.alloc.init;
            const directoryUrl = $.NSURL.fileURLWithPathIsDirectory(directory, true);
            item.setStringForType(directoryUrl.absoluteString, $.NSPasteboardTypeFileURL);
            item.setStringForType(directory, $.NSPasteboardTypeString);

            const objects = $.NSArray.arrayWithObject(item);
            if (!pasteboard.writeObjects(objects)) {
                return "false";
            }

            return $.NSPerformService("New Terminal Tab at Folder", pasteboard) ? "true" : "false";
        }
        """;

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'") + "'";
    }
}
