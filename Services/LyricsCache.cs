using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>歌词磁盘缓存：cache/lyrics/{sha1(trackKey)}.json。任意 IO 失败均静默降级。</summary>
public sealed class LyricsCache
{
    public async Task<CachedLyric?> GetAsync(string trackKey)
    {
        var file = PathFor(trackKey);
        try
        {
            if (!File.Exists(file))
                return null;
            await using var fs = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.CachedLyric).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("cache read", ex);
            return null;
        }
    }

    public async Task SetAsync(string trackKey, CachedLyric value)
    {
        var file = PathFor(trackKey);
        try
        {
            Directory.CreateDirectory(AppPaths.LyricsCacheDir);
            var tmp = file + ".tmp";
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, value, AppJsonContext.Default.CachedLyric).ConfigureAwait(false);
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("cache write", ex);
        }
    }

    public void Clear()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LyricsCacheDir))
                return;
            foreach (var f in Directory.EnumerateFiles(AppPaths.LyricsCacheDir))
                File.Delete(f);
        }
        catch (Exception ex)
        {
            Log.Error("cache clear", ex);
        }
    }

    private static string PathFor(string trackKey)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(trackKey)));
        return Path.Combine(AppPaths.LyricsCacheDir, hash + ".json");
    }
}
