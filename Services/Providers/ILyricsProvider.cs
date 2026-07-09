using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services.Providers;

/// <summary>歌词源抽象：接口细节全部封闭在实现内，单源失效不影响其他源。</summary>
public interface ILyricsProvider
{
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<ProviderSong>> SearchAsync(string keyword, CancellationToken ct);

    /// <summary>返回 null 表示该曲目无可用歌词（纯音乐/未收录/请求失败）。</summary>
    Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct);
}
