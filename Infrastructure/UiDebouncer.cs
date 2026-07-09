using Avalonia.Threading;

namespace EasyDesktopLyrics.Infrastructure;

/// <summary>UI 线程去抖器（基于 DispatcherTimer，必须在 UI 线程使用）。</summary>
internal sealed class UiDebouncer
{
    private DispatcherTimer? _timer;

    public void Schedule(TimeSpan delay, Action action)
    {
        Cancel();
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_timer, timer))
                _timer = null;
            action();
        };
        _timer = timer;
        timer.Start();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _timer = null;
    }
}
