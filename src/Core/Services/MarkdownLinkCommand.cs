using System.Diagnostics;
using System.Windows.Input;

namespace APISwitch.Services;

public sealed class MarkdownLinkCommand(Action<Exception> onError) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    // 会话内容不可信，只允许用户主动打开网页，禁止通过链接启动本地文件或自定义协议。
    public bool CanExecute(object? parameter) =>
        parameter is string value && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo((string)parameter!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
