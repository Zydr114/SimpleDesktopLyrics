using System.Net;
using System.Text.Json;
using EasyDesktopLyrics.Infrastructure;

namespace EasyDesktopLyrics.Services;

/// <summary>共享 HttpClient 与宽松 JSON 解析（兼容 JSONP 包裹）。</summary>
internal static class HttpHelper
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        return client;
    }

    public static async Task<JsonDocument?> GetJsonAsync(string url, string? referer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer != null)
            req.Headers.Referrer = new Uri(referer);
        using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Info($"http {(int)resp.StatusCode} GET {url}");
            return null;
        }
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseLoose(text);
    }

    public static async Task<JsonDocument?> PostFormJsonAsync(
        string url, string? referer, IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (referer != null)
            req.Headers.Referrer = new Uri(referer);
        using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Info($"http {(int)resp.StatusCode} POST {url}");
            return null;
        }
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseLoose(text);
    }

    private static JsonDocument? ParseLoose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            // JSONP：callback({...})
            var i = text.IndexOf('(');
            var j = text.LastIndexOf(')');
            if (i >= 0 && j > i)
                text = text[(i + 1)..j].Trim();
        }
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (Exception ex)
        {
            Log.Error("json parse", ex);
            return null;
        }
    }
}

/// <summary>JsonElement 防御式读取扩展：字段缺失/类型不符一律返回默认值，不抛异常。</summary>
internal static class JsonExt
{
    public static JsonElement? Get(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? v
            : null;

    public static JsonElement? Get(this JsonElement? e, string name) =>
        e.HasValue ? e.Value.Get(name) : null;

    public static IEnumerable<JsonElement> Items(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray() : [];

    public static string GetStr(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? "" : "";

    public static long GetLong(this JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } v when v.TryGetInt64(out var n) => n,
        { ValueKind: JsonValueKind.String } v when long.TryParse(v.GetString(), out var n) => n,
        _ => 0,
    };

    public static bool GetBool(this JsonElement? e) => e is { ValueKind: JsonValueKind.True };
}
