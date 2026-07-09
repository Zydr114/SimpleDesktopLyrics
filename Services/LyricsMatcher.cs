using System.Text;
using System.Text.RegularExpressions;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 曲目归一化与候选打分：
/// score = 0.45×标题 + 0.35×歌手 + 0.20×时长（缺项权重并入标题）。
/// </summary>
public static partial class LyricsMatcher
{
    public const double AcceptThreshold = 0.60;
    public const double EarlyAcceptThreshold = 0.85;

    [GeneratedRegex(@"[\(\[（【「《<].*?[\)\]）】」》>]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\s*(?:/|、|,|，|&|;|；|\+|×|\bfeat\.?\b|\bft\.?\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    public static string BuildKeyword(TrackInfo t) =>
        string.IsNullOrWhiteSpace(t.Artist) ? t.Title.Trim() : $"{t.Title.Trim()} {t.Artist.Trim()}";

    public static string TrackKey(TrackInfo t) => Normalize(t.Artist) + "|" + Normalize(t.Title);

    public static double Score(TrackInfo t, ProviderSong s)
    {
        var titleSim = TitleSimilarity(t.Title, s.Title);
        double? artistSim = string.IsNullOrWhiteSpace(t.Artist) || string.IsNullOrWhiteSpace(s.Artist)
            ? null
            : ArtistSimilarity(t.Artist, s.Artist);
        double? durSim = t.DurationMs > 1000 && s.DurationMs > 1000
            ? 1.0 - Math.Min(Math.Abs(t.DurationMs - s.DurationMs) / 10_000.0, 1.0)
            : null;

        double wt = 0.45, wa = 0.35, wd = 0.20;
        if (artistSim is null) { wt += wa; wa = 0; }
        if (durSim is null) { wt += wd; wd = 0; }
        return wt * titleSim + wa * (artistSim ?? 0) + wd * (durSim ?? 0);
    }

    /// <summary>小写、全角转半角、空白折叠。</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var c = ch;
            if (c == '\u3000')
                c = ' ';
            else if (c is >= '\uFF01' and <= '\uFF5E')
                c = (char)(c - 0xFEE0);
            sb.Append(char.ToLowerInvariant(c));
        }
        return MultiSpace().Replace(sb.ToString(), " ").Trim();
    }

    private static string CoreTitle(string normalized)
    {
        var core = MultiSpace().Replace(Brackets().Replace(normalized, " "), " ").Trim();
        return core.Length == 0 ? normalized : core;
    }

    private static double TitleSimilarity(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0)
            return 0;
        var direct = StringSimilarity(na, nb);
        var core = 0.95 * StringSimilarity(CoreTitle(na), CoreTitle(nb));
        return Math.Max(direct, core);
    }

    private static double StringSimilarity(string a, string b)
    {
        if (a == b)
            return 1;
        if (a.Length == 0 || b.Length == 0)
            return 0;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            return Math.Max(0.8, Dice(a, b));
        return Dice(a, b);
    }

    /// <summary>字符 bigram Dice 系数（忽略空格）。</summary>
    private static double Dice(string a, string b)
    {
        var x = a.Replace(" ", "");
        var y = b.Replace(" ", "");
        if (x.Length < 2 || y.Length < 2)
        {
            if (x == y) return 1;
            if (x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x))) return 0.7;
            return 0;
        }
        var setA = new HashSet<int>();
        var setB = new HashSet<int>();
        for (var i = 0; i < x.Length - 1; i++) setA.Add((x[i] << 16) | x[i + 1]);
        for (var i = 0; i < y.Length - 1; i++) setB.Add((y[i] << 16) | y[i + 1]);
        var common = setA.Count(setB.Contains);
        return 2.0 * common / (setA.Count + setB.Count);
    }

    private static double ArtistSimilarity(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0)
            return 0;
        var inter = ta.Count(tb.Contains);
        var jaccard = (double)inter / (ta.Count + tb.Count - inter);
        // 一方是另一方子集（多歌手合唱只报了主唱等场景）给保底分
        var subset = inter > 0 && inter == Math.Min(ta.Count, tb.Count);
        return subset ? Math.Max(jaccard, 0.85) : jaccard;
    }

    private static HashSet<string> Tokens(string s) =>
        ArtistSplit().Split(Normalize(s))
            .Select(x => x.Trim().Replace(" ", ""))
            .Where(x => x.Length > 0)
            .ToHashSet();
}
