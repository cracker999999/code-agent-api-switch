using System.Windows.Threading;

namespace WPF.Services;

/// <summary>
/// 搜索框输入防抖。中文输入法拼写期间 TextChanged 会随每个候选字符高频触发，
/// 每次都重建列表会明显卡顿，这里合并为输入停顿后再执行一次过滤。
/// </summary>
internal sealed class SearchDebouncer
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _timer;
    private readonly Action _action;

    public SearchDebouncer(Action action)
    {
        _action = action;

        // 用低优先级避免与输入处理抢时间片。
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = DefaultInterval
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action();
        };
    }

    /// <summary>重新计时，输入停顿满一个间隔后才执行。</summary>
    public void Restart()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>取消待执行的过滤，调用方已自行刷新数据时使用。</summary>
    public void Cancel()
    {
        _timer.Stop();
    }
}
