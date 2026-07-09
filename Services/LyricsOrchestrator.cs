using Avalonia.Threading;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services.Providers;

namespace EasyDesktopLyrics.Services;

public enum LyricsPhase
{
    NoSession,
    Resolving,
    Ready,
    NoLyric,
}

/// <summary>
/// 协调层：曲目变化 → override/缓存/搜索匹配 → LyricDocument；
/// 100ms 定时器 → 二分定位当前行，仅行号变化时通知 UI。
/// 除 ResolveCoreAsync 内部外全部运行在 UI 线程。
/// </summary>
public sealed class LyricsOrchestrator
{
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(3);

    private readonly SettingsService _settings;
    private readonly LyricsCache _cache;
    private readonly OverridesStore _overrides;
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _cts;
    private LyricDocument? _doc;
    private int _lineIndex = -1;
    private int _songOffsetMs;

    public LyricsOrchestrator(
        SmtcService smtc,
        SettingsService settings,
        LyricsCache cache,
        OverridesStore overrides,
        IReadOnlyList<ILyricsProvider> providers)
    {
        _settings = settings;
        _cache = cache;
        _overrides = overrides;
        _providers = providers;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Tick(forceRaise: false);

        smtc.TrackChanged += OnTrackChanged;
        smtc.PlaybackChanged += OnPlaybackChanged;
    }

    public LyricsPhase Phase { get; private set; } = LyricsPhase.NoSession;

    public TrackInfo? Track { get; private set; }

    public bool IsPlaying => _clock.IsPlaying;

    public string CurrentMain { get; private set; } = "";

    public string CurrentTrans { get; private set; } = "";

    /// <summary>任何展示相关状态变化（相位/当前行/播放状态）。UI 线程回调。</summary>
    public event Action? StateChanged;

    /// <summary>手动校正后强制重新解析当前曲目。</summary>
    public void RefreshCurrent(bool force = true)
    {
        var t = Track;
        if (t == null)
            return;
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Phase = LyricsPhase.Resolving;
        _doc = null;
        _lineIndex = -1;
        CurrentMain = "";
        CurrentTrans = "";
        UpdateTimer();
        Raise();
        _ = ResolveAndApplyAsync(t, force, cts.Token);
    }

    /// <summary>单曲偏移变更后即时生效（不重新拉取歌词）。</summary>
    public void NotifySongOffsetChanged()
    {
        var t = Track;
        if (t == null)
            return;
        _songOffsetMs = _overrides.Get(LyricsMatcher.TrackKey(t))?.OffsetMs ?? 0;
        Tick(forceRaise: true);
    }

    private void OnTrackChanged(TrackInfo? t)
    {
        _cts?.Cancel();
        _cts = null;
        Track = t;
        _doc = null;
        _lineIndex = -1;
        _songOffsetMs = 0;
        CurrentMain = "";
        CurrentTrans = "";
        _clock.Reset();

        if (t == null)
        {
            Phase = LyricsPhase.NoSession;
            UpdateTimer();
            Raise();
            return;
        }

        Phase = LyricsPhase.Resolving;
        UpdateTimer();
        Raise();

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = ResolveAndApplyAsync(t, force: false, cts.Token);
    }

    private void OnPlaybackChanged(PlaybackSnapshot snapshot)
    {
        _clock.Sync(snapshot);
        UpdateTimer();
        Tick(forceRaise: false);
        Raise(); // IsPlaying 可能变化
    }

    private async Task ResolveAndApplyAsync(TrackInfo t, bool force, CancellationToken ct)
    {
        LyricDocument? doc = null;
        var songOffset = 0;
        try
        {
            (doc, songOffset) = await Task.Run(() => ResolveCoreAsync(t, force, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"resolve failed: {t.Title}", ex);
        }

        if (ct.IsCancellationRequested)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(Track, t))
                return;
            _doc = doc;
            _songOffsetMs = songOffset;
            _lineIndex = -1;
            CurrentMain = "";
            CurrentTrans = "";
            Phase = doc != null ? LyricsPhase.Ready : LyricsPhase.NoLyric;
            Log.Info($"lyric {(doc != null ? $"ready ({doc.Lines.Count} lines)" : "not found")}: {t.Title}");
            UpdateTimer();
            Tick(forceRaise: true);
            Raise();
        });
    }

    private async Task<(LyricDocument? Doc, int SongOffsetMs)> ResolveCoreAsync(TrackInfo t, bool force, CancellationToken ct)
    {
        var key = LyricsMatcher.TrackKey(t);
        var ov = _overrides.Get(key);
        var songOffset = ov?.OffsetMs ?? 0;

        // 1. 磁盘缓存
        if (!force)
        {
            var cached = await _cache.GetAsync(key).ConfigureAwait(false);
            if (cached != null)
            {
                var ovMismatch = ov?.SongId is { Length: > 0 }
                                 && (cached.SongId != ov.SongId || cached.Source != ov.Provider);
                if (!ovMismatch)
                {
                    if (cached.NotFound)
                    {
                        if (ov?.SongId is null && DateTimeOffset.UtcNow - cached.FetchedAt < NegativeCacheTtl)
                            return (null, songOffset);
                    }
                    else
                    {
                        var cachedDoc = LrcParser.Parse(cached.Lrc, cached.TransLrc);
                        if (cachedDoc != null)
                            return (cachedDoc, songOffset);
                    }
                }
            }
        }

        // 2. 手动校正指定的歌词
        if (ov is { SongId.Length: > 0, Provider.Length: > 0 })
        {
            var provider = _providers.FirstOrDefault(p => p.Id == ov.Provider);
            if (provider != null)
            {
                var raw = await SafeGetLyricAsync(provider, ov.SongId, ct).ConfigureAwait(false);
                var doc = raw != null ? LrcParser.Parse(raw.Lrc, raw.TranslationLrc) : null;
                if (doc != null && raw != null)
                {
                    await _cache.SetAsync(key, CachedLyric.Positive(provider.Id, ov.SongId, raw)).ConfigureAwait(false);
                    return (doc, songOffset);
                }
            }
            // override 失效 → 继续走自动匹配
        }

        // 3. 自动搜索 + 打分
        var enabled = EnabledProviders();
        var candidates = new List<(ILyricsProvider Provider, ProviderSong Song, double Score)>();
        for (var i = 0; i < enabled.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var provider = enabled[i];
            IReadOnlyList<ProviderSong> found;
            try
            {
                found = await provider.SearchAsync(LyricsMatcher.BuildKeyword(t), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"search failed ({provider.Id})", ex);
                continue;
            }

            foreach (var song in found)
                candidates.Add((provider, song, LyricsMatcher.Score(t, song)));

            // 第一源已有高置信度命中，省掉第二源的请求
            if (i == 0 && candidates.Any(c => c.Score >= LyricsMatcher.EarlyAcceptThreshold))
                break;
        }

        foreach (var c in candidates
                     .Where(c => c.Score >= LyricsMatcher.AcceptThreshold)
                     .OrderByDescending(c => c.Score)
                     .Take(3))
        {
            ct.ThrowIfCancellationRequested();
            var raw = await SafeGetLyricAsync(c.Provider, c.Song.SongId, ct).ConfigureAwait(false);
            if (raw == null)
                continue;
            var doc = LrcParser.Parse(raw.Lrc, raw.TranslationLrc);
            if (doc == null)
                continue;

            await _cache.SetAsync(key, CachedLyric.Positive(c.Provider.Id, c.Song.SongId, raw)).ConfigureAwait(false);
            Log.Info($"matched [{c.Provider.Id}] {c.Song.Artist} - {c.Song.Title} score={c.Score:F2}");
            return (doc, songOffset);
        }

        await _cache.SetAsync(key, CachedLyric.Negative()).ConfigureAwait(false);
        return (null, songOffset);
    }

    private static async Task<RawLyric?> SafeGetLyricAsync(ILyricsProvider provider, string songId, CancellationToken ct)
    {
        try
        {
            return await provider.GetLyricAsync(songId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"get lyric failed ({provider.Id}/{songId})", ex);
            return null;
        }
    }

    private List<ILyricsProvider> EnabledProviders()
    {
        var s = _settings.Current;
        var list = new List<ILyricsProvider>(2);

        void Add(string id)
        {
            var p = _providers.FirstOrDefault(x => x.Id == id);
            if (p != null)
                list.Add(p);
        }

        var order = s.NeteaseFirst ? new[] { "netease", "qq" } : ["qq", "netease"];
        foreach (var id in order)
        {
            var isEnabled = id == "netease" ? s.NeteaseEnabled : s.QQMusicEnabled;
            if (isEnabled)
                Add(id);
        }
        return list;
    }

    private void UpdateTimer()
    {
        var shouldRun = Phase == LyricsPhase.Ready && _clock.IsPlaying;
        if (shouldRun && !_timer.IsEnabled)
            _timer.Start();
        else if (!shouldRun && _timer.IsEnabled)
            _timer.Stop();
    }

    private void Tick(bool forceRaise)
    {
        if (_doc == null)
            return;

        var pos = (long)_clock.Estimate().TotalMilliseconds + _settings.Current.GlobalOffsetMs + _songOffsetMs;
        var idx = LrcParser.FindIndex(_doc.Lines, pos);
        if (idx == _lineIndex && !forceRaise)
            return;

        _lineIndex = idx;
        CurrentMain = ResolveMainText(idx);
        CurrentTrans = idx >= 0 ? _doc.Lines[idx].Translation ?? "" : "";
        Raise();
    }

    /// <summary>空行且距下一行超过 5 秒 → 显示 "···" 作为间奏提示。</summary>
    private string ResolveMainText(int idx)
    {
        if (idx < 0) return "";
        var text = _doc!.Lines[idx].Text;
        if (text.Length > 0) return text;
        if (idx + 1 < _doc.Lines.Count && _doc.Lines[idx + 1].TimeMs - _doc.Lines[idx].TimeMs > 5000)
            return "\u00B7\u00B7\u00B7"; // midline dots
        if (idx == _doc.Lines.Count - 1)
            return "\u00B7\u00B7\u00B7";
        return text;
    }

    private void Raise() => StateChanged?.Invoke();
}
