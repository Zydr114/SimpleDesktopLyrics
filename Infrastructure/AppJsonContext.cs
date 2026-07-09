using System.Text.Json.Serialization;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Infrastructure;

/// <summary>System.Text.Json 源生成上下文（裁剪安全）。</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CachedLyric))]
[JsonSerializable(typeof(Dictionary<string, LyricOverride>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
