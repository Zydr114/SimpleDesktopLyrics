using System.Text.RegularExpressions;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// LRC 解析：行级时间戳、一行多标签、[offset:] 标签；翻译按相同时间戳（±20ms）合并。
/// </summary>
public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    /// <summary>解析主歌词 + 可选翻译；无任何带时间戳的行时返回 null（视为无可用歌词）。</summary>
    public static LyricDocument? Parse(string? mainLrc, string? transLrc)
    {
        var main = ParseTimed(mainLrc);
        if (main.Count == 0)
            return null;

        var lines = main.Select(m => new LyricLine(m.TimeMs, m.Text, null)).ToList();

        if (!string.IsNullOrWhiteSpace(transLrc))
        {
            var trans = ParseTimed(transLrc);
            if (trans.Count > 0)
            {
                // 时间戳取整到 10ms 作桶，容差 ±20ms
                var map = new Dictionary<long, string>();
                foreach (var t in trans)
                    if (t.Text.Length > 0)
                        map[t.TimeMs / 10] = t.Text;

                for (var i = 0; i < lines.Count; i++)
                {
                    var bucket = lines[i].TimeMs / 10;
                    string? tr = null;
                    for (long d = 0; d <= 2 && tr is null; d++)
                    {
                        if (map.TryGetValue(bucket + d, out var v) || map.TryGetValue(bucket - d, out v))
                            tr = v;
                    }
                    if (tr != null)
                        lines[i] = lines[i] with { Translation = tr };
                }
            }
        }

        return new LyricDocument(lines);
    }

    /// <summary>二分定位：返回最后一个 TimeMs &lt;= posMs 的行索引；前奏返回 -1。</summary>
    public static int FindIndex(IReadOnlyList<LyricLine> lines, long posMs)
    {
        int lo = 0, hi = lines.Count - 1, ans = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].TimeMs <= posMs)
            {
                ans = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return ans;
    }

    private static List<(long TimeMs, string Text)> ParseTimed(string? text)
    {
        var result = new List<(long TimeMs, string Text)>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        long offset = 0;
        var om = OffsetTag().Match(text);
        if (om.Success && long.TryParse(om.Groups[1].Value, out var o))
            offset = o;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var matches = TimeTag().Matches(line);
            if (matches.Count == 0)
                continue;

            // 时间标签必须是行首连续前缀
            var end = 0;
            var stamps = new List<long>();
            foreach (Match m in matches)
            {
                if (m.Index != end)
                    break;
                var min = long.Parse(m.Groups[1].Value);
                var sec = long.Parse(m.Groups[2].Value);
                long frac = 0;
                if (m.Groups[3].Success)
                {
                    var f = m.Groups[3].Value;
                    frac = f.Length switch
                    {
                        1 => int.Parse(f) * 100,
                        2 => int.Parse(f) * 10,
                        _ => int.Parse(f[..3]),
                    };
                }
                stamps.Add(min * 60_000 + sec * 1000 + frac);
                end = m.Index + m.Length;
            }
            if (stamps.Count == 0)
                continue;

            var content = line[end..].Trim();
            foreach (var s in stamps)
            {
                // LRC 约定：offset 正值 = 歌词整体提前
                var effective = Math.Max(0, s - offset);
                result.Add((effective, content));
            }
        }

        return result.OrderBy(x => x.TimeMs).ToList();
    }
}
