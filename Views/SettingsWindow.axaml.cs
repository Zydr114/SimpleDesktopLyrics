using Avalonia.Controls;
using Avalonia.Media;
using EasyDesktopLyrics.ViewModels;

namespace EasyDesktopLyrics.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    /// <summary>全光谱色板（按色相分段，每行 8 个）。</summary>
    private static readonly string[] SpectrumColors =
    [
        // Whites → Grays → Blacks
        "#FFFFFF","#F5F5F5","#E0E0E0","#C0C0C0","#A0A0A0","#808080","#606060","#404040","#202020","#000000",
        // Reds
        "#FFEBEE","#FFCDD2","#EF9A9A","#E57373","#EF5350","#F44336","#E53935","#D32F2F","#C62828","#B71C1C",
        // Oranges
        "#FFF3E0","#FFE0B2","#FFCC80","#FFB74D","#FFA726","#FF9800","#FB8C00","#F57C00","#EF6C00","#E65100",
        // Yellows → Golds
        "#FFFDE7","#FFF9C4","#FFF59D","#FFF176","#FFEE58","#FFEB3B","#FDD835","#FBC02D","#F9A825","#F57F17",
        // Light Greens → Greens
        "#E8F5E9","#C8E6C9","#A5D6A7","#81C784","#66BB6A","#4CAF50","#43A047","#388E3C","#2E7D32","#1B5E20",
        // Cyans → Teals
        "#E0F7FA","#B2EBF2","#80DEEA","#4DD0E1","#26C6DA","#00BCD4","#00ACC1","#0097A7","#00838F","#006064",
        // Light Blues → Blues
        "#E3F2FD","#BBDEFB","#90CAF9","#64B5F6","#42A5F5","#2196F3","#1E88E5","#1976D2","#1565C0","#0D47A1",
        // Deep Blues → Purples
        "#EDE7F6","#D1C4E9","#B39DDB","#9575CD","#7E57C2","#673AB7","#5E35B1","#512DA8","#4527A0","#311B92",
        // Magentas → Pinks
        "#FCE4EC","#F8BBD0","#F48FB1","#F06292","#EC407A","#E91E63","#D81B60","#C2185B","#AD1457","#880E4F",
        // Browns / Warm
        "#EFEBE9","#D7CCC8","#BCAAA4","#A1887F","#8D6E63","#795548","#6D4C41","#5D4037","#4E342E","#3E2723",
    ];

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        BuildPalette(MainColorPalette, hex => _vm.ColorHex = hex);
        BuildPalette(null, null); // stroke palette not built inline (too many)
        UpdatePreviews();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsViewModel.ColorHex) or nameof(SettingsViewModel.StrokeColorHex))
                UpdatePreviews();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }

    private void BuildPalette(WrapPanel? panel, Action<string>? apply)
    {
        if (panel == null) return;
        foreach (var hex in SpectrumColors)
        {
            var btn = new Button
            {
                Width = 22, Height = 22, Margin = new Avalonia.Thickness(1),
                Padding = new Avalonia.Thickness(0), CornerRadius = new Avalonia.CornerRadius(3),
            };
            if (Avalonia.Media.Color.TryParse(hex, out var c))
                btn.Background = new SolidColorBrush(c);
            btn.Click += (_, _) => apply?.Invoke(hex);
            panel.Children.Add(btn);
        }
    }

    private void UpdatePreviews()
    {
        if (Avalonia.Media.Color.TryParse(_vm.ColorHex, out var mc))
            MainColorPreview.Background = new SolidColorBrush(mc);
        if (Avalonia.Media.Color.TryParse(_vm.StrokeColorHex, out var sc))
            StrokeColorPreview.Background = new SolidColorBrush(sc);
    }
}
