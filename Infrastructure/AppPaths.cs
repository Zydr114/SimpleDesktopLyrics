namespace EasyDesktopLyrics.Infrastructure;

/// <summary>应用数据目录布局（%AppData%\EasyDesktopLyrics）。</summary>
internal static class AppPaths
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyDesktopLyrics");

    public static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string OverridesFile = Path.Combine(Root, "overrides.json");
    public static readonly string LyricsCacheDir = Path.Combine(Root, "cache", "lyrics");
    public static readonly string LogFile = Path.Combine(Root, "log.txt");

    static AppPaths()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LyricsCacheDir);
        }
        catch
        {
            // 目录创建失败时由各写入点自行兜底
        }
    }
}
