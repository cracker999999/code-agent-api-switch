using System.Threading;
using System.Linq;
using APISwitch.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace APISwitch.Avalonia;

public partial class App : Application
{
    private const int NotifyIconTextMaxLength = 63;
    private const string SingleInstanceMutexName = "APISwitch.Avalonia.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIcon? _trayIcon;

    public bool IsExitRequested { get; private set; }
    public bool HasStatusIcon => _trayIcon is not null;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            _ownsSingleInstanceMutex = createdNew;
            if (!createdNew)
            {
                desktop.Shutdown();
                return;
            }

            var databaseService = new DatabaseService();
            databaseService.Initialize();
            var configWriterService = new ConfigWriterService();

            var mainWindow = new MainWindow(databaseService, configWriterService);
            desktop.MainWindow = mainWindow;

            desktop.Exit += (_, _) =>
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }

                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex?.ReleaseMutex();
                    _ownsSingleInstanceMutex = false;
                }

                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
            };

            InitializeTrayIcon(mainWindow, desktop);
            RefreshTrayTooltip(databaseService);
            InitializeDockMenu(mainWindow, desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void RequestExit()
    {
        IsExitRequested = true;
    }

    public void RefreshTrayTooltip(DatabaseService databaseService)
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ToolTipText = BuildTrayTooltipText(databaseService);
    }

    private void InitializeTrayIcon(MainWindow mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var menu = new NativeMenu();

            var showMainWindowItem = new NativeMenuItem("显示主窗口");
            showMainWindowItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(mainWindow.ShowAndActivate);
            };
            menu.Add(showMainWindowItem);

            var sessionWindowItem = new NativeMenuItem("会话管理");
            sessionWindowItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(mainWindow.OpenSessionManagerWindow);
            };
            menu.Add(sessionWindowItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RequestExit();
                    desktop.Shutdown();
                });
            };
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                Icon = LoadTrayIcon(),
                ToolTipText = "APISwitch",
                Menu = menu,
                IsVisible = true
            };

            _trayIcon.Clicked += (_, _) =>
            {
                Dispatcher.UIThread.Post(mainWindow.ShowAndActivate);
            };
        }
        catch
        {
            _trayIcon = null;
        }
    }

    private static string BuildTrayTooltipText(DatabaseService databaseService)
    {
        var codexProvider = GetActiveProviderDisplayName(databaseService, 0);
        var claudeProvider = GetActiveProviderDisplayName(databaseService, 1);
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

    private static string GetActiveProviderDisplayName(DatabaseService databaseService, int toolType)
    {
        try
        {
            var activeName = databaseService
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

    private void InitializeDockMenu(MainWindow mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var dockMenu = new NativeMenu();

            var showMainWindowItem = new NativeMenuItem("显示主窗口");
            showMainWindowItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(mainWindow.ShowAndActivate);
            };
            dockMenu.Add(showMainWindowItem);

            var sessionWindowItem = new NativeMenuItem("会话管理");
            sessionWindowItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(mainWindow.OpenSessionManagerWindow);
            };
            dockMenu.Add(sessionWindowItem);

            dockMenu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("退出");
            exitItem.Click += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RequestExit();
                    desktop.Shutdown();
                });
            };
            dockMenu.Add(exitItem);

            NativeDock.SetMenu(this, dockMenu);
        }
        catch
        {
            // ignore dock menu failures to avoid startup crash
        }
    }

    private static WindowIcon LoadTrayIcon()
    {
        var candidates = new[]
        {
            "avares://APISwitch/Assets/app-preview.png",
            "avares://APISwitch/Assets/app.ico",
            "avares://APISwitch.Avalonia/Assets/app-preview.png",
            "avares://APISwitch.Avalonia/Assets/app.ico"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                return new WindowIcon(AssetLoader.Open(new Uri(candidate)));
            }
            catch
            {
                continue;
            }
        }

        throw new InvalidOperationException("无法加载托盘图标资源。");
    }
}
