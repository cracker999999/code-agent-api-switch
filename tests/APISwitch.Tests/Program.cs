using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using APISwitch.Models;
using APISwitch.Services;
using APISwitch.Services.Parsers;
#if AVALONIA
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SessionWindow = APISwitch.UI.Views.SessionWindow;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SessionWindow = WPF.Views.SessionWindow;
#endif

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string PixelImage = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAMSURBVBhXY0ADDAwAABIAAaMn47oAAAAASUVORK5CYII=";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
#if AVALONIA
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
            Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
#else
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["SearchTextBoxStyle"] = new Style(typeof(TextBox));
            application.MainWindow = new Window();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
#endif
            if (args.Contains("--measure-session-list"))
            {
                MeasureSessionList();
                return 0;
            }

#if !AVALONIA
            if (args.Contains("--measure-session-details"))
            {
                ReportMemory("before");
                for (var iteration = 0; iteration < 3; iteration++)
                {
                    var references = OpenAndCloseSessionWindow(measureMemory: true);
                    var timeout = Stopwatch.StartNew();
                    do
                    {
                        PumpDispatcher();
                        if (references.All(reference => !reference.IsAlive))
                        {
                            break;
                        }

                        Thread.Sleep(20);
                    } while (timeout.Elapsed < TimeSpan.FromSeconds(10));
                    ReportMemory($"closed-details-{iteration + 1}");
                    // 只依赖窗口自身的关闭回收，防止测试 GC 掩盖实际释放时机的问题。
                    Assert(references.All(reference => !reference.IsAlive), "详情关闭后窗口或消息未被回收。");
                }

                return 0;
            }
#endif

            CanceledReadsStopAndReleaseFile();
            ChunkedReadsPreserveLines();
            ParsersAndScansHonorCancellation();
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var references = OpenAndCloseSessionWindow();
                var timeout = Stopwatch.StartNew();
                do
                {
                    PumpDispatcher();
                    // 仅测试中强制回收，用弱引用区分真正泄漏和运行时暂留的堆空间。
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    if (references.All(reference => !reference.IsAlive))
                    {
                        break;
                    }

                    Thread.Sleep(20);
                } while (timeout.Elapsed < TimeSpan.FromSeconds(10));

                Assert(references.All(reference => !reference.IsAlive),
                    $"第 {iteration + 1} 次关闭后仍有对象被引用（窗口、消息控件、计划、旧集合、正文、终结后正文、占位集合、新集合）：{string.Join(", ", references.Select(reference => reference.IsAlive))}。");
            }

            Console.WriteLine("PASS: 取消读取、释放文件、关闭清理及连续三次窗口/消息回收。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CanceledReadsStopAndReleaseFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apiswitch-memory-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(path, "first\nsecond\n", new UTF8Encoding(false));
            using var cancellation = new CancellationTokenSource();
            using (var lines = SessionFileUtils.EnumerateLinesShared(path, cancellation.Token).GetEnumerator())
            {
                Assert(lines.MoveNext() && lines.Current == "first", "逐行读取应保留消息顺序。");
                cancellation.Cancel();
                ExpectCanceled(() => lines.MoveNext());
            }

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void MeasureSessionList()
    {
        ReportMemory("before");
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var reference = OpenAndCloseSessionList();
            PumpDispatcher();
            ReportMemory($"closed-{iteration + 1}");
            // 此路径不调用测试 GC，验证实际关闭流程已完成回收。
            Assert(!reference.IsAlive, "列表关闭后窗口仍被引用。");
        }

        var collections = GC.CollectionCount(GC.MaxGeneration);
        var pendingReference = CloseWhileListLoads();
        var timeout = Stopwatch.StartNew();
        while (GC.CollectionCount(GC.MaxGeneration) == collections)
        {
            Assert(timeout.Elapsed < TimeSpan.FromSeconds(10), "关闭后后台读取没有退出并触发回收。");
            PumpDispatcher();
            Thread.Sleep(10);
        }

        Assert(!pendingReference.IsAlive, "加载中关闭的窗口未被回收。");
        ReportMemory("closed-during-load");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CloseWhileListLoads()
    {
        var window = new SessionWindow("codex") { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        window.Close();
        return new WeakReference(window);
    }

    private static void ChunkedReadsPreserveLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apiswitch-lines-{Guid.NewGuid():N}.jsonl");
        try
        {
            foreach (var content in new[]
            {
                "", "\r\n", "first\rsecond\n\nthird\r\nlast",
                new string('中', 4095) + "\r\n尾行\n",
                string.Join('\n', Enumerable.Range(0, 50).Select(index => new string('文', index % 5 == 0 ? 90_000 : 32)))
            })
            {
                File.WriteAllText(path, content, new UTF8Encoding(true));
                var expected = new List<string>();
                using var reader = new StringReader(content);
                while (reader.ReadLine() is { } line)
                {
                    expected.Add(line);
                }

                Assert(SessionFileUtils.EnumerateLinesShared(path).SequenceEqual(expected), "分块读取改变了换行或 Unicode 内容。");
                var (head, tail) = SessionFileUtils.ReadHeadAndTailLines(path, 10, 30);
                Assert(head.SequenceEqual(expected.Take(10)) && tail.SequenceEqual(expected.TakeLast(30)), "大记录的头尾读取不完整。");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndCloseSessionList()
    {
        var window = new SessionWindow("codex") { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        try
        {
            var timeout = Stopwatch.StartNew();
            while (GetControl<TextBlock>(window, "SessionCountTextBlock").Text!.Contains("加载"))
            {
                Assert(timeout.Elapsed < TimeSpan.FromSeconds(60), "会话列表加载超时。");
                PumpDispatcher();
                Thread.Sleep(10);
            }

            PumpDispatcher();
            ReportMemory("opened");
            return new WeakReference(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static void ReportMemory(string stage)
    {
        using var process = Process.GetCurrentProcess();
        var heap = GC.GetGCMemoryInfo();
        Console.WriteLine($"{stage}: working={process.WorkingSet64 / 1048576d:F1} MB, private={process.PrivateMemorySize64 / 1048576d:F1} MB, managed={GC.GetTotalMemory(false) / 1048576d:F1} MB, committed={heap.TotalCommittedBytes / 1048576d:F1} MB");
    }

    private static void ParsersAndScansHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(Path.GetTempPath(), $"apiswitch-missing-{Guid.NewGuid():N}");
        ISessionParser[] parsers = [new CodexSessionParser(), new ClaudeSessionParser(), new GrokSessionParser()];
        foreach (var parser in parsers)
        {
            ExpectCanceled(() => parser.LoadMessages(path, cancellation.Token));
        }

        var service = new SessionService(path, path, path);
        ExpectCanceled(() => service.ScanCodexSessions(cancellation.Token));
        ExpectCanceled(() => service.ScanClaudeSessions(cancellation.Token));
        ExpectCanceled(() => service.ScanGrokSessions(cancellation.Token));
        foreach (var provider in new[] { "codex", "claude", "grok" })
        {
            ExpectCanceled(() => service.LoadMessages(provider, path, cancellation.Token));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] OpenAndCloseSessionWindow(bool measureMemory = false)
    {
        var window = new SessionWindow("grok")
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = 0
        };
        window.Show();
        try
        {
            List<SessionMessage> messages =
            [
                new() { Role = "assistant", Content = "# Memory check\n\n**Markdown**\n\n" + new string('a', 200) },
                new() { Role = "user", Content = "image", ImageDataUrls = [PixelImage] },
                new() { Role = "tool", Content = new string('b', 50_000) },
                new() { Role = "assistant", Content = new string('c', 50_000), Plan = [new("Check memory", "in_progress")] }
            ];
            if (measureMemory)
            {
                // 大文本放在首屏外，覆盖虚拟化保留数据但未创建控件的消息。
                for (var index = 0; index < 200; index++)
                {
                    messages.Add(new SessionMessage { Role = "tool", Content = new string('d', 100_000) });
                }
            }

            typeof(SessionWindow).GetMethod("RenderMessages", PrivateInstance)!.Invoke(window, [messages]);
#if AVALONIA
            PumpDispatcher();
            var items = GetControl<ItemsControl>(window, "MessagesItemsControl");
            var firstContainer = items.ContainerFromIndex(0);
#else
            window.UpdateLayout();
            var items = GetControl<ItemsControl>(window, "MessagesListControl");
            var firstContainer = items.ItemContainerGenerator.ContainerFromIndex(0);
#endif
            var views = items.ItemsSource!.Cast<SessionMessageView>().ToArray();
            Assert(views.Length == messages.Count && firstContainer is not null, "详情应保留所有消息并生成首屏控件。");
            var references = new List<WeakReference>
            {
                new WeakReference(window),
                new WeakReference(firstContainer),
                new WeakReference(views[3]),
                new WeakReference(items.ItemsSource),
                new WeakReference(views[^1].Content),
                // 普通弱引用会提前失效，长弱引用还能检测终结阶段暂留的大文本。
                new WeakReference(views[^1].Content, trackResurrection: true)
            };
#if AVALONIA
            var images = (IDictionary)typeof(SessionWindow).GetField("_messageImages", PrivateInstance)!.GetValue(window)!;
            Assert(images.Count == 1, "图片测试数据必须成功解码。");
            var bitmap = (Avalonia.Media.Imaging.Bitmap)images[PixelImage]!;
#endif
            // 占位提示也经过集合视图；切换后应连同其数据源一起释放。
            typeof(SessionWindow).GetMethod("ShowMessagePlaceholder", PrivateInstance)!.Invoke(window, ["loading"]);
            references.Add(new WeakReference(items.ItemsSource));
            typeof(SessionWindow).GetMethod("RenderMessages", PrivateInstance)!.Invoke(window, [messages]);
            references.Add(new WeakReference(items.ItemsSource));
            PumpDispatcher();
#if AVALONIA
            AssertBitmapDisposed(bitmap);
            Assert(images.Count == 1, "切换详情后只应保留当前位图。");
            bitmap = (Avalonia.Media.Imaging.Bitmap)images[PixelImage]!;
#endif
            if (measureMemory)
            {
                ReportMemory("opened-details");
            }

            var debouncer = typeof(SessionWindow).GetField("_searchDebouncer", PrivateInstance)!.GetValue(window)!;
            debouncer.GetType().GetMethod("Restart")!.Invoke(debouncer, null);
            window.Close();
            Assert(window.Content is null && items.ItemsSource is null, "关闭应立即清空详情数据源。");
#if AVALONIA
            Assert(GetControl<ItemsControl>(window, "SessionGroupsItemsControl").ItemsSource is null, "关闭应清空会话列表。");
            Assert(images.Count == 0, "关闭应清空详情位图。");
            AssertBitmapDisposed(bitmap);
#else
            Assert(GetControl<ListBox>(window, "SessionListBox").ItemsSource is null, "关闭应清空会话列表。");
#endif
            if (measureMemory)
            {
                return references.ToArray();
            }

            // 给构造时发起的扫描和搜索防抖一次完成机会，验证关闭后不会重新填充。
            for (var i = 0; i < 15; i++)
            {
                PumpDispatcher();
                Thread.Sleep(20);
            }

            Assert(items.ItemsSource is null, "后台任务不得重新填充已关闭的详情。");
            typeof(SessionWindow).GetMethod("ShowMessagePlaceholder", PrivateInstance)!.Invoke(window, ["late result"]);
            Assert(items.ItemsSource is null, "迟到的回调不得重新创建详情占位控件。");
            return references.ToArray();
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    private static T GetControl<T>(SessionWindow window, string name) where T : class
    {
#if AVALONIA
        return (T)(object)window.FindControl<Control>(name)!;
#else
        return (T)window.FindName(name);
#endif
    }

    private static void PumpDispatcher()
    {
#if AVALONIA
        Dispatcher.UIThread.RunJobs();
#else
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
#endif
    }

#if AVALONIA
    private static void AssertBitmapDisposed(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        // Avalonia 的 Dispose 会清空原生位图引用，不依赖其 Save 对已释放对象的异常类型。
        var implementation = typeof(Avalonia.Media.Imaging.Bitmap).GetProperty(
            "PlatformImpl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var reference = implementation.GetValue(bitmap)!;
        var item = reference.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert(item.GetValue(reference) is null, "详情位图的原生内存尚未释放。");
    }
#endif

    private static void ExpectCanceled(Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("取消请求没有终止后台读取。");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
