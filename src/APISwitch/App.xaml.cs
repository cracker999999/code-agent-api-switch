using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows;
using APISwitch.Services;
using Forms = System.Windows.Forms;

namespace APISwitch;

public partial class App : System.Windows.Application
{
    private const int NotifyIconTextMaxLength = 63;

    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private DatabaseService? _databaseService;

    internal bool IsExitRequested { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _databaseService = new DatabaseService();
            _databaseService.Initialize();

            var configWriterService = new ConfigWriterService();
            _mainWindow = new MainWindow(_databaseService, configWriterService);
            MainWindow = _mainWindow;

            InitializeTrayIcon();
            _mainWindow.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = BuildTrayTooltipText(),
            Icon = LoadTrayIcon(),
            Visible = true
        };

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add("会话管理", null, (_, _) => ShowSessionManagerWindow());
        contextMenu.Items.Add("退出", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon LoadTrayIcon()
    {
        var processPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return SystemIcons.Application;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        IsExitRequested = true;
        _mainWindow?.Close();
        Shutdown();
    }

    private void ShowSessionManagerWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        //ShowMainWindow();
        _mainWindow.OpenSessionManagerWindow();
    }

    internal void RefreshTrayTooltip()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = BuildTrayTooltipText();
    }

    private string BuildTrayTooltipText()
    {
        var codexProvider = GetActiveProviderDisplayName(0);
        var claudeProvider = GetActiveProviderDisplayName(1);
        var tooltip = BuildTrayTooltipTextCore(codexProvider, claudeProvider);
        if (tooltip.Length <= NotifyIconTextMaxLength)
        {
            return tooltip;
        }

        return tooltip[..(NotifyIconTextMaxLength - 3)] + "...";
    }

    private static string BuildTrayTooltipTextCore(string codexProvider, string claudeProvider)
    {
        return $"APISwitch{Environment.NewLine}Codex:{codexProvider}{Environment.NewLine}Claude Code:{claudeProvider}";
    }

    private string GetActiveProviderDisplayName(int toolType)
    {
        if (_databaseService is null)
        {
            return "未启用";
        }

        try
        {
            var activeName = _databaseService
                .GetProviders(toolType)
                .FirstOrDefault(provider => provider.IsActive)?
                .Name;

            if (string.IsNullOrWhiteSpace(activeName))
            {
                return "未启用";
            }

            return activeName.Trim();
        }
        catch (Exception)
        {
            return "未知";
        }
    }
}
