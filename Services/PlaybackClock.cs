using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 播放进度插值时钟。SMTC 的 Position 仅在 PositionAt（LastUpdatedTime）时刻准确，
/// 播放器上报间隔不定，本地用挂钟插值出连续位置。
/// </summary>
public sealed class PlaybackClock
{
    private TimeSpan _basePos;
    private DateTimeOffset _baseAt = DateTimeOffset.UtcNow;
    private double _rate = 1.0;
    private TimeSpan _duration;

    public bool IsPlaying { get; private set; }

    /// <summary>是否存在可用时间线（无时间线 → 降级为仅显示标题）。</summary>
    public bool HasTimeline => _duration > TimeSpan.Zero;

    public void Sync(PlaybackSnapshot s)
    {
        _basePos = s.Position;
        _rate = s.Rate <= 0 ? 1.0 : s.Rate;
        IsPlaying = s.IsPlaying;
        _duration = s.Duration;

        // LastUpdatedTime 可能过期或为 0，做合法性校验后再采用
        var age = DateTimeOffset.UtcNow - s.PositionAt;
        _baseAt = age > TimeSpan.Zero && age < TimeSpan.FromSeconds(30)
            ? s.PositionAt
            : DateTimeOffset.UtcNow;
    }

    public void Reset()
    {
        _basePos = TimeSpan.Zero;
        _baseAt = DateTimeOffset.UtcNow;
        _duration = TimeSpan.Zero;
    }

    public TimeSpan Estimate()
    {
        var pos = IsPlaying
            ? _basePos + (DateTimeOffset.UtcNow - _baseAt) * _rate
            : _basePos;

        if (_duration > TimeSpan.Zero && pos > _duration)
            pos = _duration;
        return pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }
}
