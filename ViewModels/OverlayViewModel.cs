using Avalonia.Media;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// 悬浮窗只读视图模型：组合 设置 + 协调器状态 → 文本/样式/可见性。
/// 所有属性为计算属性，来源变化时整体广播。
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly string[] AllProps =
    [
        nameof(MainText), nameof(TransText), nameof(ShowTransLine),
        nameof(FontFamilyValue), nameof(MainFontSize), nameof(EffectiveTransFontSize), nameof(WeightValue),
        nameof(Fill), nameof(TextOpacity), nameof(MaxTextWidth), nameof(TextEffect),
        nameof(EditBackground), nameof(IsUnlocked), nameof(WindowVisible),
        nameof(StrokeEnabled), nameof(StrokeBrush), nameof(StrokeThickness), nameof(LineSpacing),
        nameof(TextAlignment),
    ];

    private static readonly IBrush UnlockedBackground = new SolidColorBrush(Color.FromArgb(0x55, 0x10, 0x10, 0x10));

    private readonly SettingsService _settings;
    private readonly LyricsOrchestrator _orchestrator;
    private readonly DropShadowEffect _shadow = new()
    {
        OffsetX = 0,
        OffsetY = 2,
        BlurRadius = 8,
        Color = Color.FromArgb(0xC0, 0x00, 0x00, 0x00),
    };

    private string _fillHexCache = "";
    private IBrush _fillCache = Brushes.White;
    private string _strokeHexCache = "";
    private IBrush _strokeBrushCache = Brushes.Black;

    public OverlayViewModel(SettingsService settings, LyricsOrchestrator orchestrator)
    {
        _settings = settings;
        _orchestrator = orchestrator;
        _settings.Changed += () => RaiseMany(AllProps);
        _orchestrator.StateChanged += () => RaiseMany(AllProps);
    }

    private AppSettings S => _settings.Current;

    public string MainText
    {
        get
        {
            var text = _orchestrator.Phase == LyricsPhase.Ready ? _orchestrator.CurrentMain : FallbackText();
            if (IsUnlocked && string.IsNullOrEmpty(text))
                return "拖动调整歌词位置 · 双击锁定";
            return text;
        }
    }

    public string TransText => _orchestrator.Phase == LyricsPhase.Ready ? _orchestrator.CurrentTrans : "";

    public bool ShowTransLine => S.ShowTranslation && TransText.Length > 0;

    public FontFamily FontFamilyValue
    {
        get
        {
            try
            {
                return new FontFamily(S.FontFamily);
            }
            catch
            {
                return FontFamily.Default;
            }
        }
    }

    public double MainFontSize => Math.Clamp(S.FontSize, 10, 200);

    public double EffectiveTransFontSize
    {
        get
        {
            var ts = S.TransFontSize > 0 ? S.TransFontSize : Math.Round(MainFontSize * 0.6);
            return Math.Max(10, ts);
        }
    }

    public double LineSpacing => Math.Clamp(S.LineSpacing, 0, 50);

    public TextAlignment TextAlignment
    {
        get
        {
            try { return (TextAlignment)Enum.Parse(typeof(TextAlignment), S.Alignment); }
            catch { return Avalonia.Media.TextAlignment.Center; }
        }
    }

    public FontWeight WeightValue => (FontWeight)Math.Clamp(S.FontWeight, 100, 950);

    public IBrush Fill
    {
        get
        {
            if (_fillHexCache != S.ColorHex)
            {
                _fillHexCache = S.ColorHex;
                _fillCache = Color.TryParse(S.ColorHex, out var c) ? new SolidColorBrush(c) : Brushes.White;
            }
            return _fillCache;
        }
    }

    public double TextOpacity => Math.Clamp(S.Opacity, 0.05, 1.0);

    public double MaxTextWidth => Math.Clamp(S.MaxWidth, 200, 4000);

    public object? TextEffect => S.ShadowEnabled ? _shadow : null;

    public bool StrokeEnabled => S.StrokeEnabled;

    public IBrush StrokeBrush
    {
        get
        {
            if (_strokeHexCache != S.StrokeColorHex)
            {
                _strokeHexCache = S.StrokeColorHex;
                _strokeBrushCache = Color.TryParse(S.StrokeColorHex, out var c) ? new SolidColorBrush(c) : Brushes.Black;
            }
            return _strokeBrushCache;
        }
    }

    public double StrokeThickness => Math.Clamp(S.StrokeThickness, 1, 8);

    public IBrush EditBackground => IsUnlocked ? UnlockedBackground : Brushes.Transparent;

    public bool IsUnlocked => !S.PositionLocked;

    public bool WindowVisible
    {
        get
        {
            if (IsUnlocked)
                return true; // 解锁时必须可见，才能拖动
            if (!S.LyricsVisible)
                return false;
            if (_orchestrator.Phase == LyricsPhase.NoSession)
                return false;
            if (S.HideWhenPaused && !_orchestrator.IsPlaying)
                return false;
            if (MainText.Length == 0 && !ShowTransLine)
                return false;
            return true;
        }
    }

    private string FallbackText()
    {
        var t = _orchestrator.Track;
        if (t == null || !S.ShowTitleWhenNoLyric)
            return "";
        return string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
    }
}
