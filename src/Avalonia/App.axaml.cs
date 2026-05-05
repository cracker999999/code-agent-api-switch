using System.Threading;
using APISwitch.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace APISwitch.Avalonia;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "APISwitch.Avalonia.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIcon? _trayIcon;

    public bool IsExitRequested { get; private set; }

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

            if (OperatingSystem.IsWindows())
            {
                InitializeTrayIcon(mainWindow, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void RequestExit()
    {
        IsExitRequested = true;
    }

    private void InitializeTrayIcon(MainWindow mainWindow, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var showMainWindowItem = new NativeMenuItem("显示主窗口");
        showMainWindowItem.Click += (_, _) => mainWindow.ShowAndActivate();
        menu.Add(showMainWindowItem);

        var sessionWindowItem = new NativeMenuItem("会话管理");
        sessionWindowItem.Click += (_, _) => mainWindow.OpenSessionManagerWindow();
        menu.Add(sessionWindowItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            RequestExit();
            desktop.Shutdown();
        };
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Avalonia/Assets/app.ico"))),
            ToolTipText = "APISwitch",
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => mainWindow.ShowAndActivate();
    }
}
