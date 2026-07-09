using System.Net;
using System.Text;
using System.Text.Json;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services.Providers;

/// <summary>
/// QQ 音乐（非官方公开接口，两个请求都必须带 Referer: https://y.qq.com/）：
/// 搜索 GET c.y.qq.com/soso/fcgi-bin/client_search_cp
/// 歌词 GET c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg（lyric/trans 为 Base64 + HTML 实体）
/// </summary>
public sealed class QQMusicLyricsProvider : ILyricsProvider
{
    private const string Referer = "https://y.qq.com/";

    public string Id => "qq";

    public string DisplayName => "QQ音乐";

    public async Task<IReadOnlyList<ProviderSong>> SearchAsync(string keyword, CancellationToken ct)
    {
        var list = new List<ProviderSong>();
        var kw = Uri.EscapeDataString(keyword.Trim());

        var url = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp" +
                  $"?w={kw}&p=1&n=10&cr=1&format=json&inCharset=utf8&outCharset=utf-8";
        using (var doc = await HttpHelper.GetJsonAsync(url, Referer, ct).ConfigureAwait(false))
        {
            if (doc != null)
            {
                var songs = doc.RootElement.Get("data").Get("song").Get("list")
                         ?? doc.RootElement.Get("data").Get("songlist");
                ParseSongs(songs, list);
            }
        }

        return list;
    }

    public async Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct)
    {
        var url = "https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg" +
                  $"?songmid={songId}&g_tk=5381&format=json&inCharset=utf8&outCharset=utf-8";
        using var doc = await HttpHelper.GetJsonAsync(url, Referer, ct).ConfigureAwait(false);
        if (doc == null)
            return null;

        var root = doc.RootElement;
        if (root.Get("retcode").GetLong() != 0)
            return null;

        var lyric = DecodeLyric(root.Get("lyric").GetStr());
        if (lyric.Length < 10)
            return null;

        var trans = DecodeLyric(root.Get("trans").GetStr());
        return new RawLyric(lyric, trans.Length < 10 ? null : trans);
    }

    private void ParseSongs(JsonElement? songs, List<ProviderSong> list)
    {
        if (songs is null)
            return;
        foreach (var s in songs.Value.EnumerateArray())
        {
            var mid = s.Get("mid").GetStr();
            if (mid.Length == 0)
                mid = s.Get("songmid").GetStr();
            if (mid.Length == 0)
                continue;

            var name = s.Get("name").GetStr();
            if (name.Length == 0)
                name = s.Get("songname").GetStr();

            var artists = s.Get("singer").Items()
                .Select(a => a.Get("name").GetStr())
                .Where(n => n.Length > 0);

            var album = s.Get("album").Get("name").GetStr();
            if (album.Length == 0)
                album = s.Get("albumname").GetStr();

            var durSec = s.Get("interval").GetLong();
            if (durSec <= 0)
                durSec = s.Get("duration").GetLong();

            list.Add(new ProviderSong(Id, mid, name, string.Join("/", artists), album, durSec * 1000));
        }
    }

    private static string DecodeLyric(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return "";
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return WebUtility.HtmlDecode(text);
        }
        catch
        {
            return "";
        }
    }
}
