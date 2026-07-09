using System.Text.Json;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 设置加载/防抖保存。除 Flush 外必须在 UI 线程使用。
/// </summary>
public sealed class SettingsService
{
    private readonly UiDebouncer _saveDebouncer = new();

    public SettingsService() => Current = Load();

    public AppSettings Current { get; }

    public event Action? Changed;

    /// <summary>修改设置并触发 Changed + 防抖落盘。</summary>
    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Changed?.Invoke();
        _saveDebouncer.Schedule(TimeSpan.FromMilliseconds(500), SaveNow);
    }

    /// <summary>退出前冲刷未保存的修改。</summary>
    public void Flush()
    {
        _saveDebouncer.Cancel();
        SaveNow();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var text = File.ReadAllText(AppPaths.SettingsFile);
                var s = JsonSerializer.Deserialize(text, AppJsonContext.Default.AppSettings);
                if (s != null)
                    return s;
            }
        }
        catch (Exception ex)
        {
            Log.Error("settings load failed, using defaults", ex);
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".bad", overwrite: true); } catch { }
        }
        return new AppSettings();
    }

    private void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings);
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("settings save", ex);
        }
    }
}
