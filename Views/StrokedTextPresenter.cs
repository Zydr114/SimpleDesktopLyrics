using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 带描边的文本展示控件。内部用 Grid 叠放多个 TextBlock。
/// </summary>
public sealed class StrokedTextPresenter : Decorator
{
    private static readonly (double dx, double dy)[] StrokeOffsets =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-0.7, -0.7), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7),
    };

    private readonly Grid _grid = new();
    private readonly TextBlock _main = new();
    private readonly List<TextBlock> _strokeLayers = [];

    public StrokedTextPresenter()
    {
        _grid.Children.Add(_main);
        Child = _grid;
    }

    static StrokedTextPresenter()
    {
        TextProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => { x.SyncMain(); x.SyncStroke(); });
        FillProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => x._main.Foreground = x.Fill);
        StrokeProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => x.SyncStroke());
        SEnabledProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => x.RebuildStroke());
        SThicknessProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => x.RebuildStroke());
        FontFamilyProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => { x._main.FontFamily = x.FontFamily; x.SyncStroke(); });
        FontSizeProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => { x._main.FontSize = x.FontSize; x.SyncStroke(); });
        FontWeightProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => { x._main.FontWeight = x.FontWeight; x.SyncStroke(); });
        EffectProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => x._main.Effect = x.Effect);
        OpacityProperty.Changed.AddClassHandler<StrokedTextPresenter>((x, _) => { x._main.Opacity = x.Opacity; x.SyncStroke(); });
    }

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StrokedTextPresenter, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<StrokedTextPresenter, IBrush?>(nameof(Fill), Brushes.White);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<StrokedTextPresenter, IBrush?>(nameof(Stroke), Brushes.Black);

    public static readonly StyledProperty<bool> SEnabledProperty =
        AvaloniaProperty.Register<StrokedTextPresenter, bool>("StrokeEnabled");

    public static readonly StyledProperty<double> SThicknessProperty =
        AvaloniaProperty.Register<StrokedTextPresenter, double>("StrokeThickness", 2.0);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextBlock.FontFamilyProperty.AddOwner<StrokedTextPresenter>();

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<StrokedTextPresenter>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<StrokedTextPresenter>();

    public static readonly new StyledProperty<IEffect?> EffectProperty =
        Visual.EffectProperty.AddOwner<StrokedTextPresenter>();

    public static readonly new StyledProperty<double> OpacityProperty =
        Visual.OpacityProperty.AddOwner<StrokedTextPresenter>();

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public bool StrokeEnabled { get => GetValue(SEnabledProperty); set => SetValue(SEnabledProperty, value); }
    public double StrokeThickness { get => GetValue(SThicknessProperty); set => SetValue(SThicknessProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public new IEffect? Effect { get => GetValue(EffectProperty); set => SetValue(EffectProperty, value); }
    public new double Opacity { get => GetValue(OpacityProperty); set => SetValue(OpacityProperty, value); }

    private void SyncMain() => _main.Text = Text ?? "";
    private void SyncStroke()
    {
        foreach (var tb in _strokeLayers)
        {
            tb.Text = Text ?? "";
            tb.Foreground = Stroke;
            tb.FontFamily = FontFamily;
            tb.FontSize = FontSize;
            tb.FontWeight = FontWeight;
            tb.Opacity = Opacity;
        }
    }

    private void RebuildStroke()
    {
        foreach (var tb in _strokeLayers) _grid.Children.Remove(tb);
        _strokeLayers.Clear();
        if (!StrokeEnabled || StrokeThickness <= 0) return;
        var t = StrokeThickness;
        foreach (var (dx, dy) in StrokeOffsets)
        {
            var tb = new TextBlock { RenderTransform = new TranslateTransform(dx * t, dy * t) };
            _strokeLayers.Add(tb);
            _grid.Children.Insert(0, tb);
        }
        SyncStroke();
    }
}
