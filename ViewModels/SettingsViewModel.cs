using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;
using EasyDesktopLyrics.Services.Providers;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>SMTC 会话下拉项。</summary>
public sealed record SessionOption(string? Aumid, string Display)
{
    public override string ToString() => Display;
}

/// <summary>手动搜索结果条目。</summary>
public sealed record SearchResultItem(string ProviderId, string ProviderName, string SongId, string Title, string Artist, long DurationMs, double Score)
{
    public override string ToString()
    {
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, DurationMs));
        var scorePart = Score > 0 ? $"　匹配度 {Score * 100:F0}%" : "";
        return $"[{ProviderName}] {Title} — {Artist}　({(int)dur.TotalMinutes}:{dur.Seconds:D2}){scorePart}";
    }
}

/// <summary>位置预设九宫格条目。</summary>
public sealed record PresetOption(PositionPreset Preset, string Label, int Col, int Row);

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private static readonly string[] SettingProps =
    [
        nameof(SelectedFont), nameof(FontSize), nameof(WeightIndex), nameof(ColorHex),
        nameof(ColorValue), nameof(StrokeColorValue),
        nameof(ShadowEnabled), nameof(StrokeEnabled), nameof(StrokeColorHex), nameof(StrokeThicknessVal),
        nameof(TransFontSizeVal), nameof(LineSpacingVal), nameof(TextOpacity), nameof(MaxWidth),
        nameof(ShowTranslation), nameof(HideWhenPaused), nameof(ShowTitleWhenNoLyric), nameof(AutoStartEnabled),
        nameof(NeteaseEnabled), nameof(QQMusicEnabled), nameof(NeteaseFirst), nameof(GlobalOffsetMs),
        nameof(SelectedSession), nameof(SelectedPresetIndex), nameof(PositionXPct), nameof(PositionYPct),
        nameof(AlignmentIndex), nameof(SelectedFontFamily),
    ];

    private static readonly int[] WeightValues = [400, 500, 600, 700];

    private readonly SettingsService _settings;
    private readonly SmtcService _smtc;
    private readonly LyricsOrchestrator _orchestrator;
    private readonly OverridesStore _overrides;
    private readonly LyricsCache _cache;
    private readonly IReadOnlyList<ILyricsProvider> _providers;

    private TrackInfo? _lastTrack;
    private string _searchKeyword = "";
    private bool _keywordDirty;
    private SearchResultItem? _selectedResult;
    private bool _isBusy;
    private string _statusText = "";
    private decimal? _songOffsetMs;

    /// <summary>由 App 层注入，用于快捷定位。</summary>
    public Action<PositionPreset>? SnapToPreset { get; set; }

    /// <summary>由 App 层注入，用于直接定位到像素坐标。</summary>
    public Action<double, double>? SetAnchor { get; set; }

    /// <summary>由 App 层设置，主屏工作区（物理像素）。</summary>
    public PixelRect ScreenArea { get; set; }

    public SettingsViewModel(
        SettingsService settings,
        SmtcService smtc,
        LyricsOrchestrator orchestrator,
        OverridesStore overrides,
        LyricsCache cache,
        IReadOnlyList<ILyricsProvider> providers)
    {
        _settings = settings;
        _smtc = smtc;
        _orchestrator = orchestrator;
        _overrides = overrides;
        _cache = cache;
        _providers = providers;

        FontOptions = LoadFontOptions();

        SearchCommand = new RelayCommand(() => _ = RunSearchAsync(), () => !_isBusy);
        ApplyResultCommand = new RelayCommand(ApplySelectedResult, () => _selectedResult != null && _orchestrator.Track != null);
        ClearOverrideCommand = new RelayCommand(ClearOverride, () => _orchestrator.Track != null);
        ApplySongOffsetCommand = new RelayCommand(ApplySongOffset, () => _orchestrator.Track != null);
        ClearCacheCommand = new RelayCommand(ClearCache);
        RefreshSessionsCommand = new RelayCommand(RefreshSessions);
        PickColorCommand = new RelayCommand(p =>
        {
            if (p is string hex)
                ColorHex = hex;
        });

        PickStrokeColorCommand = new RelayCommand(p =>
        {
            if (p is string hex)
                StrokeColorHex = hex;
        });

        SnapPresetCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse<PositionPreset>(s, out var preset))
                SnapToPreset?.Invoke(preset);
        });

        _settings.Changed += OnSettingsChanged;
        _orchestrator.StateChanged += OnOrchestratorChanged;
        _smtc.SessionsChanged += RefreshSessions;

        RefreshSessions();
        SyncFromTrack(force: true);
    }

    // ---------- 公用命令与数据 ----------
    public RelayCommand SnapPresetCommand { get; }

    public IReadOnlyList<PresetOption> PositionPresets { get; } =
    [
        new(PositionPreset.TopLeft,     "↖", 0, 0), new(PositionPreset.TopCenter,    "↑", 1, 0), new(PositionPreset.TopRight,    "↗", 2, 0),
        new(PositionPreset.MiddleLeft,  "←", 0, 1), new(PositionPreset.Center,       "⊙", 1, 1), new(PositionPreset.MiddleRight, "→", 2, 1),
        new(PositionPreset.BottomLeft,  "↙", 0, 2), new(PositionPreset.BottomCenter, "↓", 1, 2), new(PositionPreset.BottomRight, "↘", 2, 2),
    ];

    public IReadOnlyList<string> PresetColors { get; } =
    [
        "#FFFFFF", "#F0F0F0", "#FFD700", "#FFB347", "#FF6B6B", "#FF4500",
        "#00FF88", "#00CED1", "#00BFFF", "#7ED6DF", "#1E90FF", "#6A5ACD",
        "#FF69B4", "#FF1493", "#BA55D3", "#9370DB", "#C0C0C0", "#FFA07A",
        "#98FB98", "#87CEEB",
    ];

    public RelayCommand PickColorCommand { get; }

    public RelayCommand PickStrokeColorCommand { get; }

    // ---------- 外观 ----------
    public IReadOnlyList<string> FontOptions { get; }

    public string SelectedFont
    {
        get => _settings.Current.FontFamily;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _settings.Current.FontFamily)
                return;
            _settings.Update(s => s.FontFamily = value);
        }
    }

    public double FontSize
    {
        get => _settings.Current.FontSize;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.FontSize) < 0.5)
                return;
            _settings.Update(s => s.FontSize = v);
        }
    }

    public IReadOnlyList<string> WeightOptions { get; } = ["常规 (400)", "中等 (500)", "半粗 (600)", "粗体 (700)"];

    public int WeightIndex
    {
        get
        {
            var i = Array.IndexOf(WeightValues, _settings.Current.FontWeight);
            return i >= 0 ? i : 2;
        }
        set
        {
            if (value < 0 || value >= WeightValues.Length || WeightValues[value] == _settings.Current.FontWeight)
                return;
            _settings.Update(s => s.FontWeight = WeightValues[value]);
        }
    }

    public string ColorHex
    {
        get => _settings.Current.ColorHex;
        set
        {
            if (value == _settings.Current.ColorHex || !Color.TryParse(value, out _))
                return;
            _settings.Update(s => s.ColorHex = value);
            Raise(nameof(ColorValue));
        }
    }

    public Color ColorValue
    {
        get => Color.TryParse(_settings.Current.ColorHex, out var c) ? c : Colors.White;
        set => _settings.Update(s => s.ColorHex = value.ToString().ToUpperInvariant());
    }

    public bool ShadowEnabled
    {
        get => _settings.Current.ShadowEnabled;
        set
        {
            if (value == _settings.Current.ShadowEnabled) return;
            _settings.Update(s => s.ShadowEnabled = value);
        }
    }

    public bool StrokeEnabled
    {
        get => _settings.Current.StrokeEnabled;
        set
        {
            if (value == _settings.Current.StrokeEnabled) return;
            _settings.Update(s => s.StrokeEnabled = value);
        }
    }

    public string StrokeColorHex
    {
        get => _settings.Current.StrokeColorHex;
        set
        {
            if (value == _settings.Current.StrokeColorHex || !Color.TryParse(value, out _)) return;
            _settings.Update(s => s.StrokeColorHex = value);
            Raise(nameof(StrokeColorValue));
        }
    }

    public Color StrokeColorValue
    {
        get => Color.TryParse(_settings.Current.StrokeColorHex, out var c) ? c : Colors.Black;
        set => _settings.Update(s => s.StrokeColorHex = value.ToString().ToUpperInvariant());
    }

    public double StrokeThicknessVal
    {
        get => _settings.Current.StrokeThickness;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.StrokeThickness) < 0.5) return;
            _settings.Update(s => s.StrokeThickness = v);
        }
    }

    public double TransFontSizeVal
    {
        get => _settings.Current.TransFontSize;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.TransFontSize) < 1) return;
            _settings.Update(s => s.TransFontSize = v);
        }
    }

    public double LineSpacingVal
    {
        get => _settings.Current.LineSpacing;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.LineSpacing) < 0.5) return;
            _settings.Update(s => s.LineSpacing = v);
        }
    }

    public double TextOpacity
    {
        get => _settings.Current.Opacity;
        set
        {
            if (Math.Abs(value - _settings.Current.Opacity) < 0.01) return;
            _settings.Update(s => s.Opacity = Math.Round(value, 2));
        }
    }

    public double MaxWidth
    {
        get => _settings.Current.MaxWidth;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.MaxWidth) < 1) return;
            _settings.Update(s => s.MaxWidth = v);
        }
    }

    // ---------- 字体预览 ----------
    public string FontPreviewText => "预览文字 AaBbCc 晴";

    public FontFamily SelectedFontFamily
    {
        get
        {
            try { return new FontFamily(SelectedFont); }
            catch { return FontFamily.Default; }
        }
    }

    // ---------- 位置 ----------
    private bool _updatingPosition;

    public IReadOnlyList<string> PresetOptionLabels { get; } =
        ["左上", "中上", "右上",
         "左中", "居中", "右中",
         "左下", "中下", "右下",
         "自定义"];

    private static readonly PositionPreset[] PresetValues =
        [PositionPreset.TopLeft, PositionPreset.TopCenter, PositionPreset.TopRight,
         PositionPreset.MiddleLeft, PositionPreset.Center, PositionPreset.MiddleRight,
         PositionPreset.BottomLeft, PositionPreset.BottomCenter, PositionPreset.BottomRight];

    public int SelectedPresetIndex
    {
        get
        {
            var ax = _settings.Current.AnchorX;
            var ay = _settings.Current.AnchorY;
            var a = ScreenArea;
            if (!ax.HasValue || !ay.HasValue || a.Width <= 0) return 4; // default = center

            for (int i = 0; i < 9; i++)
            {
                var (ex, ey) = PresetToAnchor(PresetValues[i]);
                if (Math.Abs(ex - ax.Value) < 3 && Math.Abs(ey - ay.Value) < 3)
                    return i;
            }
            return 9; // 自定义
        }
        set
        {
            if (_updatingPosition || value < 0 || value > 9) return;
            _updatingPosition = true;
            if (value < 9)
            {
                var preset = PresetValues[value];
                var (ax, ay) = PresetToAnchor(preset);
                _settings.Update(s => { s.AnchorX = ax; s.AnchorY = ay; });
                SnapToPreset?.Invoke(preset);
            }
            // value==9 (自定义): 不做任何操作, 由滑块驱动
            Raise(nameof(PositionXPct));
            Raise(nameof(PositionYPct));
            _updatingPosition = false;
        }
    }

    public double PositionXPct
    {
        get
        {
            var a = ScreenArea;
            if (a.Width <= 0 || !_settings.Current.AnchorX.HasValue) return 50;
            return Math.Clamp((_settings.Current.AnchorX.Value - a.X) / a.Width * 100, 5, 95);
        }
        set
        {
            if (_updatingPosition) return;
            var a = ScreenArea;
            if (a.Width <= 0) return;
            _updatingPosition = true;
            var ax = a.X + a.Width * value / 100.0;
            var ay = _settings.Current.AnchorY ?? a.Y + a.Height * 0.85;
            _settings.Update(s => { s.AnchorX = ax; s.AnchorY = ay; });
            SetAnchor?.Invoke(ax, ay);
            Raise(nameof(SelectedPresetIndex));
            _updatingPosition = false;
        }
    }

    public double PositionYPct
    {
        get
        {
            var a = ScreenArea;
            if (a.Height <= 0 || !_settings.Current.AnchorY.HasValue) return 85;
            return Math.Clamp((_settings.Current.AnchorY.Value - a.Y) / a.Height * 100, 5, 95);
        }
        set
        {
            if (_updatingPosition) return;
            var a = ScreenArea;
            if (a.Height <= 0) return;
            _updatingPosition = true;
            var ax = _settings.Current.AnchorX ?? a.X + a.Width * 0.5;
            var ay = a.Y + a.Height * value / 100.0;
            _settings.Update(s => { s.AnchorX = ax; s.AnchorY = ay; });
            SetAnchor?.Invoke(ax, ay);
            Raise(nameof(SelectedPresetIndex));
            _updatingPosition = false;
        }
    }

    private (double x, double y) PresetToAnchor(PositionPreset p)
    {
        var a = ScreenArea;
        if (a.Width <= 0) a = new PixelRect(0, 0, 1920, 1080);
        var margin = 80;
        int cx = a.X + a.Width / 2;
        int left = a.X + margin;
        int right = a.X + a.Width - margin;
        int cy = a.Y + a.Height / 2;
        int top = a.Y + margin;
        int bottom = a.Y + a.Height - margin;
        int x = p switch
        {
            PositionPreset.TopLeft or PositionPreset.MiddleLeft or PositionPreset.BottomLeft => left,
            PositionPreset.TopCenter or PositionPreset.Center or PositionPreset.BottomCenter => cx,
            _ => right,
        };
        int y = p switch
        {
            PositionPreset.TopLeft or PositionPreset.TopCenter or PositionPreset.TopRight => top,
            PositionPreset.MiddleLeft or PositionPreset.Center or PositionPreset.MiddleRight => cy,
            _ => bottom,
        };
        return (x, y);
    }

    // ---------- 行为 ----------
    public bool ShowTranslation
    {
        get => _settings.Current.ShowTranslation;
        set
        {
            if (value == _settings.Current.ShowTranslation) return;
            _settings.Update(s => s.ShowTranslation = value);
        }
    }

    public IReadOnlyList<string> AlignmentOptions { get; } = ["居中", "左对齐", "右对齐"];

    private static readonly string[] AlignmentValues = ["Center", "Left", "Right"];

    public int AlignmentIndex
    {
        get => Math.Clamp(Array.IndexOf(AlignmentValues, _settings.Current.Alignment), 0, 2);
        set
        {
            if (value < 0 || value > 2 || AlignmentValues[value] == _settings.Current.Alignment) return;
            _settings.Update(s => s.Alignment = AlignmentValues[value]);
        }
    }

    public bool HideWhenPaused
    {
        get => _settings.Current.HideWhenPaused;
        set
        {
            if (value == _settings.Current.HideWhenPaused) return;
            _settings.Update(s => s.HideWhenPaused = value);
        }
    }

    public bool ShowTitleWhenNoLyric
    {
        get => _settings.Current.ShowTitleWhenNoLyric;
        set
        {
            if (value == _settings.Current.ShowTitleWhenNoLyric) return;
            _settings.Update(s => s.ShowTitleWhenNoLyric = value);
        }
    }

    public bool AutoStartEnabled
    {
        get => _settings.Current.AutoStart;
        set
        {
            if (value == _settings.Current.AutoStart) return;
            _settings.Update(s => s.AutoStart = value);
        }
    }

    // ---------- SMTC 与歌词源 ----------
    public ObservableCollection<SessionOption> SessionOptions { get; } = [];

    public SessionOption? SelectedSession
    {
        get
        {
            var aumid = _settings.Current.LockedSessionAumid;
            return SessionOptions.FirstOrDefault(o => o.Aumid == (string.IsNullOrEmpty(aumid) ? null : aumid))
                   ?? SessionOptions.FirstOrDefault();
        }
        set
        {
            if (value is null || value.Aumid == _settings.Current.LockedSessionAumid)
                return;
            _settings.Update(s => s.LockedSessionAumid = value.Aumid);
        }
    }

    public bool NeteaseEnabled
    {
        get => _settings.Current.NeteaseEnabled;
        set
        {
            if (value == _settings.Current.NeteaseEnabled) return;
            _settings.Update(s => s.NeteaseEnabled = value);
        }
    }

    public bool QQMusicEnabled
    {
        get => _settings.Current.QQMusicEnabled;
        set
        {
            if (value == _settings.Current.QQMusicEnabled) return;
            _settings.Update(s => s.QQMusicEnabled = value);
        }
    }

    public bool NeteaseFirst
    {
        get => _settings.Current.NeteaseFirst;
        set
        {
            if (value == _settings.Current.NeteaseFirst) return;
            _settings.Update(s => s.NeteaseFirst = value);
        }
    }

    public decimal? GlobalOffsetMs
    {
        get => _settings.Current.GlobalOffsetMs;
        set
        {
            var v = (int)(value ?? 0);
            if (v == _settings.Current.GlobalOffsetMs) return;
            _settings.Update(s => s.GlobalOffsetMs = v);
        }
    }

    // ---------- 手动校正 ----------
    public string CurrentTrackText
    {
        get
        {
            var t = _orchestrator.Track;
            if (t == null)
                return "（当前无播放会话）";
            var phase = _orchestrator.Phase switch
            {
                LyricsPhase.Ready => "已匹配歌词",
                LyricsPhase.NoLyric => "未找到歌词",
                LyricsPhase.Resolving => "正在匹配…",
                _ => "",
            };
            var head = string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
            return $"{head}　[{phase}]";
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (Set(ref _searchKeyword, value))
                _keywordDirty = true;
        }
    }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    public SearchResultItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (Set(ref _selectedResult, value))
                ApplyResultCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public decimal? SongOffsetMs
    {
        get => _songOffsetMs;
        set => Set(ref _songOffsetMs, value);
    }

    public RelayCommand SearchCommand { get; }

    public RelayCommand ApplyResultCommand { get; }

    public RelayCommand ClearOverrideCommand { get; }

    public RelayCommand ApplySongOffsetCommand { get; }

    public RelayCommand ClearCacheCommand { get; }

    public RelayCommand RefreshSessionsCommand { get; }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _orchestrator.StateChanged -= OnOrchestratorChanged;
        _smtc.SessionsChanged -= RefreshSessions;
    }

    private static IReadOnlyList<string> LoadFontOptions()
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("enum fonts", ex);
            return ["Microsoft YaHei UI", "Segoe UI"];
        }
    }

    private void OnSettingsChanged() => RaiseMany(SettingProps);

    private void OnOrchestratorChanged()
    {
        Raise(nameof(CurrentTrackText));
        SyncFromTrack(force: false);
    }

    private void SyncFromTrack(bool force)
    {
        var t = _orchestrator.Track;
        if (!force && Equals(t, _lastTrack))
            return;
        _lastTrack = t;

        ApplyResultCommand.RaiseCanExecuteChanged();
        ClearOverrideCommand.RaiseCanExecuteChanged();
        ApplySongOffsetCommand.RaiseCanExecuteChanged();

        if (t != null)
        {
            if (!_keywordDirty || string.IsNullOrWhiteSpace(_searchKeyword))
            {
                _searchKeyword = LyricsMatcher.BuildKeyword(t);
                _keywordDirty = false;
                Raise(nameof(SearchKeyword));
            }
            SongOffsetMs = _overrides.Get(LyricsMatcher.TrackKey(t))?.OffsetMs ?? 0;
        }
    }

    private void RefreshSessions()
    {
        var current = _settings.Current.LockedSessionAumid;
        SessionOptions.Clear();
        SessionOptions.Add(new SessionOption(null, "自动（跟随系统当前会话）"));
        foreach (var (aumid, display) in _smtc.GetSessions())
            SessionOptions.Add(new SessionOption(aumid, display));

        if (!string.IsNullOrEmpty(current) && SessionOptions.All(o => o.Aumid != current))
            SessionOptions.Add(new SessionOption(current, $"{current}（未运行）"));

        Raise(nameof(SelectedSession));
    }

    private async Task RunSearchAsync()
    {
        var keyword = _searchKeyword.Trim();
        if (keyword.Length == 0)
        {
            StatusText = "请输入搜索关键词。";
            return;
        }

        IsBusy = true;
        StatusText = "搜索中…";
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        try
        {
            var track = _orchestrator.Track;
            var items = new List<SearchResultItem>();
            foreach (var provider in _providers)
            {
                try
                {
                    var found = await provider.SearchAsync(keyword, CancellationToken.None);
                    items.AddRange(found.Select(s => new SearchResultItem(
                        provider.Id, provider.DisplayName, s.SongId, s.Title, s.Artist, s.DurationMs,
                        track != null ? LyricsMatcher.Score(track, s) : 0)));
                }
                catch (Exception ex)
                {
                    Log.Error($"manual search ({provider.Id})", ex);
                }
            }

            foreach (var item in items.OrderByDescending(i => i.Score))
                SearchResults.Add(item);

            Raise(nameof(HasSearchResults));
            StatusText = SearchResults.Count == 0 ? "未搜索到结果。" : $"共 {SearchResults.Count} 条结果，选中后点击“应用所选歌词”。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySelectedResult()
    {
        var r = _selectedResult;
        var t = _orchestrator.Track;
        if (r == null || t == null)
            return;

        var key = LyricsMatcher.TrackKey(t);
        var existing = _overrides.Get(key);
        _overrides.Set(key, new LyricOverride
        {
            Provider = r.ProviderId,
            SongId = r.SongId,
            OffsetMs = existing?.OffsetMs ?? 0,
        });
        _orchestrator.RefreshCurrent(force: true);
        StatusText = $"已应用 [{r.ProviderName}] {r.Title}，本曲将始终使用该歌词。";
    }

    private void ClearOverride()
    {
        var t = _orchestrator.Track;
        if (t == null)
            return;
        _overrides.Remove(LyricsMatcher.TrackKey(t));
        SongOffsetMs = 0;
        _orchestrator.RefreshCurrent(force: true);
        StatusText = "已清除本曲校正，恢复自动匹配。";
    }

    private void ApplySongOffset()
    {
        var t = _orchestrator.Track;
        if (t == null)
            return;

        var key = LyricsMatcher.TrackKey(t);
        var offset = (int)(_songOffsetMs ?? 0);
        var existing = _overrides.Get(key);

        if (offset == 0 && existing?.SongId is null or { Length: 0 })
        {
            _overrides.Remove(key);
        }
        else
        {
            _overrides.Set(key, new LyricOverride
            {
                Provider = existing?.Provider,
                SongId = existing?.SongId,
                OffsetMs = offset,
            });
        }
        _orchestrator.NotifySongOffsetChanged();
        StatusText = $"本曲偏移已设为 {offset} ms（正值 = 歌词提前）。";
    }

    private void ClearCache()
    {
        _cache.Clear();
        StatusText = "歌词缓存已清空。";
    }
}
