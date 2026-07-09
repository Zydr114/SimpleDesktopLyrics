# EasyDesktopLyrics 关键实现说明

配套文档：[ARCHITECTURE.md](./ARCHITECTURE.md)。本文按模块给出可直接落地的技术细节与代码骨架，
所有代码为 C# / .NET 10 / Avalonia 12。

---

## 1. 工程与发布配置

### 1.1 csproj 要点

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <!-- Windows TFM：自动引入 WinRT 投影（Windows.Media.Control），无需额外包 -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <!-- SMTC 读取 API 需要 Win10 1809+ -->
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets/app.ico</ApplicationIcon>
    <InvariantGlobalization>true</InvariantGlobalization>  <!-- 减小体积，本项目无本地化排序需求 -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.0" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.0" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.0" />
    <PackageReference Include="Avalonia.Diagnostics" Version="12.1.0" Condition="'$(Configuration)'=='Debug'" />
  </ItemGroup>
</Project>
```

### 1.2 app.manifest 要点

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
<!-- 外加 compatibility 段声明 Win10/11 supportedOS GUID -->
```

### 1.3 发布命令

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=true -p:TrimMode=partial
```

- 预期体积：不裁剪 ~70–100MB；`TrimMode=partial` 后 ~35–60MB（WinRT 投影 dll 裁剪收益最大）。
- 裁剪安全性由两点保证：接口 JSON 用 `JsonDocument` 手解（无反射建型）；
  settings/cache 序列化用 `JsonSerializerContext` 源生成。
- 若裁剪后冒烟测试（托盘、两个窗口、SMTC、两个源各搜一次）失败，直接去掉 `PublishTrimmed`。

### 1.4 单实例与自启动

```csharp
// Program.cs
using var mutex = new Mutex(true, @"Local\EasyDesktopLyrics.SingleInstance", out bool createdNew);
if (!createdNew) return; // 静默退出即可，无需激活已有实例（无主窗口可激活）
BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
```

自启动写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值为
`"{Environment.ProcessPath}" --autostart`（Registry API 在 Windows TFM 下直接可用）。
`ShutdownMode.OnExplicitShutdown` 必须设置：本程序无主窗口，退出仅由托盘菜单触发。

---

## 2. SMTC 接入（SmtcService）

### 2.1 API 与事件

命名空间 `Windows.Media.Control`，核心类型：

```csharp
var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
manager.CurrentSessionChanged += ...;   // 系统认为的"当前"会话变化
manager.SessionsChanged += ...;         // 会话列表增删（供"锁定播放器"下拉框用）

GlobalSystemMediaTransportControlsSession session = manager.GetCurrentSession(); // 可能为 null
session.SourceAppUserModelId            // AUMID，用于识别/过滤播放器
session.MediaPropertiesChanged += ...;  // 曲目元数据变化
session.PlaybackInfoChanged += ...;     // 播放/暂停/速率
session.TimelinePropertiesChanged += ...; // 进度上报（离散！）

var props = await session.TryGetMediaPropertiesAsync(); // Title/Artist/AlbumTitle
var tl = session.GetTimelineProperties();  // Position/StartTime/EndTime/LastUpdatedTime
var pb = session.GetPlaybackInfo();        // PlaybackStatus/PlaybackRate
```

### 2.2 会话选择策略

```
设置 = 自动:      跟随 manager.GetCurrentSession()
设置 = 锁定 X:    在 manager.GetSessions() 中找 SourceAppUserModelId == X；
                  没找到 → 视为无会话（不回退到其他会话，避免浏览器干扰）
```

会话切换时必须：解绑旧 session 的 3 个事件 → 绑定新 session → 立即主动拉一次
元数据 + 时间线 + 播放状态（事件只保证"之后的变化"）。

### 2.3 必须注意的坑

1. **线程**：所有 SMTC 事件在 MTA 回调线程触发，服务内统一
   `Dispatcher.UIThread.Post(...)` 后再对外发布 C# 事件，下游全部免加锁。
2. **重复事件**：多数播放器在一次切歌中连发 2–4 次 `MediaPropertiesChanged`
   （封面异步加载导致）。用 300ms 去抖 + `(Title, Artist)` 元组比对，相同则忽略。
3. **瞬时失败**：切歌瞬间 `TryGetMediaPropertiesAsync` 可能抛 `COMException` 或返回空
   Title。捕获后延迟 250ms 重试一次，仍失败则等下一次事件。
4. **空会话**：`GetCurrentSession()` 随时可能返回 null（播放器退出），进入 NoSession 状态并隐藏窗口。

### 2.4 服务骨架

```csharp
public sealed class SmtcService : IAsyncDisposable
{
    public event Action<TrackInfo?>? TrackChanged;        // null = 无会话
    public event Action<PlaybackSnapshot>? PlaybackChanged; // 状态+速率+时间线快照
    public IReadOnlyList<(string Aumid, string Display)> ActiveSessions { get; } // 供设置页

    // 内部：manager 事件 → RebindSession()；session 事件 → 去抖 → Dispatcher.Post → 对外事件
}

public readonly record struct PlaybackSnapshot(
    bool IsPlaying, double Rate,
    TimeSpan Position, TimeSpan Duration, DateTimeOffset PositionAt);
```

---

## 3. 播放进度插值（PlaybackClock）

SMTC 的 `Position` 只在 `LastUpdatedTime` 那一刻准确，播放器上报间隔从毫秒级到
仅在 seek 时上报不等，因此本地必须插值：

```csharp
public sealed class PlaybackClock
{
    TimeSpan _basePos; DateTimeOffset _baseAt; double _rate = 1; bool _playing;
    TimeSpan _duration;

    public void Sync(PlaybackSnapshot s)
    {
        _basePos = s.Position; _rate = s.Rate == 0 ? 1 : s.Rate;
        _playing = s.IsPlaying; _duration = s.Duration;
        // LastUpdatedTime 可能是过期值甚至 0，做合法性校验：
        var age = DateTimeOffset.UtcNow - s.PositionAt;
        _baseAt = (age > TimeSpan.Zero && age < TimeSpan.FromSeconds(30))
                  ? s.PositionAt : DateTimeOffset.UtcNow;
    }

    public TimeSpan Estimate()
    {
        var pos = _playing
            ? _basePos + (DateTimeOffset.UtcNow - _baseAt) * _rate
            : _basePos;
        if (_duration > TimeSpan.Zero && pos > _duration) pos = _duration;
        return pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }

    public bool HasTimeline => _duration > TimeSpan.Zero; // false → 降级：只显示标题
}
```

校准时机：`TimelinePropertiesChanged`（覆盖 seek）、`PlaybackInfoChanged`（暂停/恢复/变速）、
切歌。**seek 的表现就是一次带新 Position 的 Sync，无需特判。**

---

## 4. 桌面悬浮窗（LyricsOverlayWindow）

### 4.1 XAML 关键属性

```xml
<Window xmlns="https://github.com/avaloniaui" ...
        SystemDecorations="None"
        TransparencyLevelHint="Transparent"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ShowActivated="False"
        CanResize="False"
        SizeToContent="WidthAndHeight">
  <!-- Viewbox DownOnly：超过 MaxWidth 自动等比缩小，不足不放大 -->
  <Viewbox StretchDirection="DownOnly" MaxWidth="{Binding MaxWidth}">
    <StackPanel>
      <TextBlock Text="{Binding CurrentText}"
                 FontFamily="{Binding Font}" FontSize="{Binding FontSize}"
                 Foreground="{Binding Brush}" Opacity="{Binding Opacity}"
                 Effect="drop-shadow(0 2 6 #C0000000)"/>   <!-- 阴影保证任意背景可读 -->
      <TextBlock Text="{Binding TranslationText}" IsVisible="{Binding ShowTranslation}"
                 HorizontalAlignment="Center" FontSize="{Binding TransFontSize}" .../>
    </StackPanel>
  </Viewbox>
</Window>
```

说明：
- `Background="Transparent"`（而非 null）保证解锁模式下整个窗口可拖动命中。
- 阴影用 Avalonia 内置 `DropShadowEffect`；真正的文字描边需自绘 TextLayout，v1 不做。
- 字号缩到下限仍超宽的情况极少，接受 Viewbox 缩小即可，不再做省略号二级处理
 （若要 Ellipsis 则去掉 Viewbox 换 `TextTrimming="CharacterEllipsis"`，二选一，默认 Viewbox）。

### 4.2 鼠标穿透与窗口样式（核心 Win32）

Avalonia 官方明确不内置点击穿透，需在窗口 `Opened` 后设置扩展样式：

```csharp
internal static class Win32
{
    const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TRANSPARENT = 0x00000020; // 鼠标穿透（配合 LAYERED）
    internal const int WS_EX_LAYERED    = 0x00080000;
    internal const int WS_EX_NOACTIVATE = 0x08000000; // 永不获得焦点
    internal const int WS_EX_TOOLWINDOW = 0x00000080; // 不出现在 Alt+Tab / 任务栏

    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLongW(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after,
        int x, int y, int cx, int cy, uint flags);
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        var ex = GetWindowLongW(hwnd, GWL_EXSTYLE)
                 | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        ex = enable ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLongW(hwnd, GWL_EXSTYLE, ex);
    }

    public static void AssertTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
```

```csharp
// LyricsOverlayWindow.axaml.cs
protected override void OnOpened(EventArgs e)
{
    base.OnOpened(e);
    _hwnd = TryGetPlatformHandle()!.Handle;
    Win32.SetClickThrough(_hwnd, enable: true);   // 默认锁定=穿透
}
```

置顶保持：Avalonia `Topmost=True` 打底；在切歌事件与一个 10s 稀疏定时器上调用
`AssertTopmost`（幂等且零成本），对抗其他后来置顶的窗口。不做更激进的抢顶循环。

### 4.3 锁定 / 解锁（编辑位置）

| 状态 | 扩展样式 | 视觉 | 交互 |
|------|---------|------|------|
| 锁定（默认） | `WS_EX_TRANSPARENT` on | 纯文本 | 完全穿透，无任何交互 |
| 解锁 | `WS_EX_TRANSPARENT` off | 显示半透明圆角底板提示可拖 | 按住拖动（`BeginMoveDrag`），双击或托盘菜单锁定 |

```csharp
void OnPointerPressed(object? s, PointerPressedEventArgs e)
{
    if (!_locked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        BeginMoveDrag(e);
}
```

### 4.4 位置锚点与多屏/DPI

- 窗口 `SizeToContent`，宽度随歌词变化 → 直接存左上角会"左对齐抖动"。
  持久化**中心锚点**（DIP，虚拟桌面坐标）：`anchor = Position + PixelSize/2/scale`；
  每次尺寸变化后 `Position = anchor - newSize/2`（在 `SizeChanged` 里回写）。
- 启动时将锚点 clamp 进当前 `Screens.All` 的并集边界，防止显示器拔掉后窗口丢失。
- PerMonitorV2 由 manifest 声明，Avalonia 自动处理缩放，代码中统一用 DIP。

---

## 5. 歌词源（Providers）

### 5.1 接口抽象

```csharp
public interface ILyricsProvider
{
    string Id { get; }                       // "netease" / "qq"
    Task<IReadOnlyList<LyricSearchResult>> SearchAsync(TrackInfo t, CancellationToken ct);
    Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct);
}
public sealed record RawLyric(string Lrc, string? TranslationLrc);
```

共享单例 `HttpClient`：`AutomaticDecompression = All`，超时 8s，
UA 设为常规浏览器 UA。**所有接口细节封闭在各 Provider 文件内**，接口失效只改一个文件。

### 5.2 网易云音乐

| 用途 | 请求 |
|------|------|
| 搜索 | `GET https://music.163.com/api/search/get/web?s={kw}&type=1&offset=0&limit=10`（Referer: `https://music.163.com`） |
| 搜索备用 | `POST https://music.163.com/api/cloudsearch/pc`，表单 `s={kw}&type=1&limit=10`（响应字段名不同：`ar`/`al`/`dt`） |
| 歌词 | `GET https://music.163.com/api/song/lyric?os=pc&id={id}&lv=-1&tv=-1` |

响应要点：
- 搜索：`result.songs[] → { id, name, artists[].name, duration(毫秒), album.name }`
- 歌词：`lrc.lyric`（原文 LRC）、`tlyric.lyric`（翻译 LRC，可能为空）、
  `nolyric == true` 表示纯音乐（视为"确认无歌词"，正缓存，显示标题）、
  `uncollected == true` 表示未收录。

### 5.3 QQ 音乐

| 用途 | 请求 |
|------|------|
| 搜索 | `GET https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w={kw}&p=1&n=10&new_json=1&cr=1&format=json&inCharset=utf8&outCharset=utf-8` |
| 歌词 | `GET https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={mid}&g_tk=5381&format=json&inCharset=utf8&outCharset=utf-8` |

**两个请求都必须带 `Referer: https://y.qq.com/`，否则被拒。**

响应要点：
- 搜索：`data.song.list[] → { mid, name, singer[].name, interval(秒), album.name }`
- 歌词：`{ retcode, lyric, trans }`，`lyric`/`trans` 为 **Base64**（UTF-8），解码后是 LRC；
  文本内含 HTML 实体（`&apos;` 等），需 `WebUtility.HtmlDecode`；
  `retcode != 0` 或解码后长度极短视为无歌词。

### 5.4 容错

- 单 Provider 任意异常（超时/非 200/JSON 结构变化）→ 记日志、返回空 → 走下一个 Provider。
- `JsonDocument` 手解一律用 `TryGetProperty`，接口字段缺失不抛异常。

---

## 6. 匹配算法（LyricsOrchestrator）

### 6.1 归一化

对标题与艺术家统一执行：小写 → 全角转半角 → 去首尾空白/合并连续空白 →
剥离尾部括注（`(Live)`、`（伴奏）`、`[Remix]` 等仅在**打分时**作为次要文本，不用于主比较）→
艺术家按 `/ 、 & , feat. ft.` 拆分为 token 集合。

### 6.2 打分

```
score = 0.45 × titleSim + 0.35 × artistSim + 0.20 × durationSim

titleSim   : 完全相等 1.0；一方包含另一方 0.8；否则字符 bigram Dice 系数
artistSim  : 艺术家 token 集合的 Jaccard（任一方为空则此项权重并入 title）
durationSim: 1 - min(|Δt| / 10s, 1)；SMTC 无时长时权重并入 title
```

取全部候选（两源各 ≤10 条）最高分，`score ≥ 0.60` 才接受；按设置里的源优先级
先搜第一源，**第一源有 ≥0.85 的高分直接采用**（省一次请求），否则再搜第二源比较。

### 6.3 缓存与校正

- trackKey = `norm(artist) + "|" + norm(title)`，文件名取其 SHA-1。
- 正缓存：`{source, songId, lrc, tlrc, fetchedAt}`，永久有效（清空缓存按钮兜底）。
- 负缓存：`{notFound: true, fetchedAt}`，TTL 3 天（防止单曲循环反复打接口，也留出接口恢复余地）。
- 手动校正（overrides.json）：`trackKey → {provider, songId, offsetMs}`，
  优先级最高；设置页流程 = 预填 "artist title" → 编辑关键词搜索（列表含 源/歌名/歌手/时长）
  → 选中即拉取歌词、写 override、立即生效。

### 6.4 并发纪律

每次 TrackChanged 创建新的 `CancellationTokenSource` 并取消上一个；
所有 IO 均传 token。快速切歌时保证只有最后一曲的结果会发布到 UI。

---

## 7. LRC 解析（LrcParser）

### 7.1 规则

```
时间标签: [mm:ss] [mm:ss.x] [mm:ss.xx] [mm:ss.xxx]，一行可有多个标签（重复段）
元数据:   [ti:] [ar:] [al:] [by:] 忽略；[offset:±ms] 参与时间计算（正值=提前）
正则:     \[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]
毫秒归一: 小数部分 1 位×100、2 位×10、3 位×1
```

流程：逐行取全部前缀标签 → 余下文本为歌词；每个标签生成一条 `LyricLine`；
按时间排序；空文本行保留（作为"间奏静默"标记，显示为空）；
全文件无有效时间标签 → 视为纯文本歌词，降级为不滚动（等同无时间线）。

### 7.2 翻译合并

网易 `tlyric` / QQ `trans` 是与原文**时间戳相同**的独立 LRC：
解析后按时间戳精确匹配（容差 ±20ms）挂到 `LyricLine.Translation`；匹配不上的翻译行丢弃。

### 7.3 当前行定位

```csharp
// lines 已按 TimeMs 升序；pos 已含 全局偏移 + 单曲偏移
public static int FindIndex(IReadOnlyList<LyricLine> lines, long posMs)
{
    int lo = 0, hi = lines.Count - 1, ans = -1;
    while (lo <= hi)
    {
        int mid = (lo + hi) / 2;
        if (lines[mid].TimeMs <= posMs) { ans = mid; lo = mid + 1; }
        else hi = mid - 1;
    }
    return ans; // -1 = 尚未到第一行（前奏），显示空
}
```

渲染定时器 100ms tick 中调用，**仅当索引变化时**更新 ViewModel（每行一次 UI 失效，
CPU 占用可忽略）。暂停/隐藏/无歌词时停表。

---

## 8. 配置与持久化（SettingsService）

```csharp
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, LyricOverride>))]
[JsonSerializable(typeof(CachedLyric))]
internal partial class AppJsonContext : JsonSerializerContext { } // 源生成，trim 安全
```

- 读取：启动时一次，损坏/缺失则用默认值并备份坏文件。
- 写入：任何设置变化 → 500ms 防抖 → 原子写（写临时文件 + `File.Move(overwrite)`）。
- 设置对象为单例，UI 直接绑定，服务按需读取；字段即 ARCHITECTURE §1.3 清单，不多不少。

---

## 9. 托盘与生命周期（App.axaml.cs）

```
TrayIcon 菜单：
  锁定歌词位置   （勾选项，默认勾选）
  显示歌词       （勾选项）
  ─────────
  设置…          （打开/激活 SettingsWindow，含手动校正页）
  ─────────
  退出
```

- 服务组装在 `OnFrameworkInitializationCompleted` 中手动 new（依赖极少，不用 DI 容器）：
  `SettingsService → SmtcService → PlaybackClock → Providers → LyricsCache → Orchestrator → 窗口/VM`。
- 退出顺序：停定时器 → `SmtcService.DisposeAsync()`（解绑 WinRT 事件，否则进程可能不退出）
  → 冲刷防抖中的设置写入 → `Shutdown()`。

---

## 10. 性能与体验细则

| 点 | 做法 |
|----|------|
| 定时器 | 单个 `DispatcherTimer` 100ms，仅在 Playing 且有带时间轴歌词时运行 |
| UI 失效 | 只在"当前行索引变化"时改绑定属性；颜色/字体等设置变化即时生效（绑定） |
| 网络 | 缓存优先；切歌 ≤3 请求；负缓存防单曲循环；无任何轮询 |
| 事件去抖 | MediaPropertiesChanged 300ms；TimelineChanged 不去抖（seek 要即时） |
| 无时间线降级 | `HasTimeline == false` 或歌词无时间戳 → 恒显示标题（按设置）或首行 |
| 长间奏 | 当前行与下一行间隔 > 15s 且已过 8s → 淡出为空（可选简单实现：显示空行即可） |

---

## 11. 测试要点

1. **穿透/置顶**：锁定时点击歌词区域应命中下层窗口；视频全屏（非独占）之上仍可见；
   任务管理器置顶窗口共存不死循环抢顶。
2. **SMTC 矩阵**：Spotify、网易云 PC、QQ 音乐 PC、Apple Music、Edge 网页 B 站/YouTube ——
   分别验证：切歌、暂停/恢复、拖进度条、退出播放器、同时开两个播放器 + 锁定会话设置。
3. **时间线质量**：挑一个只在 seek 时上报进度的播放器验证插值精度（±0.5s 内可接受）。
4. **匹配**：中文歌、英文歌、日文歌、"歌名 (Live)"、多艺术家 "A/B"、纯音乐（网易 nolyric）、
   完全搜不到（触发负缓存与手动校正流程）。
5. **DPI/多屏**：150% 主屏 + 100% 副屏间拖动；拔掉副屏后重启位置回收。
6. **裁剪发布**：发布产物完整冒烟（§1.3 清单）。

---

## 12. 已知边界（记录备查）

- 独占全屏游戏无法覆盖（DWM 之外），属预期行为。
- 歌词接口为非官方公开接口，可能随时收紧（限流/加密/验证码）；架构上已隔离为 Provider，
  必要时可新增源或接入本地 LRC（v2 方向，当前不做）。
- 浏览器会话的元数据是"页面标题级"质量，锁定播放器设置是正解；不做启发式过滤。
- Avalonia 12 发布于 2026-04，若遇到回归问题，所有用法均兼容 11.3.x，可直接降级。
