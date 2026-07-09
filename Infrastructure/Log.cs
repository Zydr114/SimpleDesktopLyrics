namespace EasyDesktopLyrics.Infrastructure;

/// <summary>极简文件日志：仅少量事件级信息，启动时超过 1MB 即清空。</summary>
internal static class Log
{
    private static readonly object Gate = new();

    static Log()
    {
        try
        {
            var fi = new FileInfo(AppPaths.LogFile);
            if (fi.Exists && fi.Length > 1024 * 1024)
                fi.Delete();
        }
        catch
        {
            // ignore
        }
    }

    public static void Info(string message) => Write("INF", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败绝不影响主流程
        }
    }
}
