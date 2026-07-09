using System.Text.Json;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>手动校正记录：trackKey → LyricOverride，改动即时落盘（低频操作）。</summary>
public sealed class OverridesStore
{
    private readonly Dictionary<string, LyricOverride> _map;
    private readonly object _gate = new();

    public OverridesStore() => _map = Load();

    public LyricOverride? Get(string trackKey)
    {
        lock (_gate)
            return _map.TryGetValue(trackKey, out var v) ? v : null;
    }

    public void Set(string trackKey, LyricOverride value)
    {
        lock (_gate)
        {
            _map[trackKey] = value;
            Save();
        }
    }

    public void Remove(string trackKey)
    {
        lock (_gate)
        {
            if (_map.Remove(trackKey))
                Save();
        }
    }

    private static Dictionary<string, LyricOverride> Load()
    {
        try
        {
            if (File.Exists(AppPaths.OverridesFile))
            {
                var text = File.ReadAllText(AppPaths.OverridesFile);
                var map = JsonSerializer.Deserialize(text, AppJsonContext.Default.DictionaryStringLyricOverride);
                if (map != null)
                    return map;
            }
        }
        catch (Exception ex)
        {
            Log.Error("overrides load", ex);
        }
        return new Dictionary<string, LyricOverride>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var json = JsonSerializer.Serialize(_map, AppJsonContext.Default.DictionaryStringLyricOverride);
            var tmp = AppPaths.OverridesFile + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.OverridesFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("overrides save", ex);
        }
    }
}
