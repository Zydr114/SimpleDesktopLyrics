# EasyDesktopLyrics 架构文档

> 一款极简的 Windows 桌面歌词：通过 SMTC 与任意播放器同步，从公开歌词接口自动匹配歌词，
> 以一行可穿透、置顶的文本悬浮于桌面。

- 技术栈：C# / .NET 10 (LTS) / Avalonia 12
- 目标平台：Windows 10 1809 (build 17763) 及以上（SMTC 读取 API 的最低要求）
- 文档版本：v1.0（2026-07）
- 配套文档：[IMPLEMENTATION.md](./IMPLEMENTATION.md)（关键实现说明）

---

## 1. 需求梳理

### 1.1 核心场景

用户使用任意支持 SMTC 的播放器（Spotify、网易云音乐、QQ 音乐、Apple Music、
foobar2000 新版、浏览器网页播放器等）听歌，本程序：

1. 监听系统 SMTC 会话，获得当前曲目的 标题 / 艺术家 /（专辑）/ 时长 / 播放进度；
2. 用曲目元数据到网易云音乐、QQ 音乐的公开接口搜索并匹配 LRC 歌词；
3. 在桌面上以**一行文本**实时滚动显示当前歌词行，置顶于所有窗口、鼠标穿透、不抢焦点。

### 1.2 功能性需求（FR）

| 编号 | 需求 | 说明 |
|------|------|------|
| FR-1 | SMTC 同步 | 监听当前会话与曲目切换、播放/暂停、进度（seek）变化 |
| FR-2 | 进度插值 | SMTC 时间线更新是离散的，需本地时钟插值出连续播放位置 |
| FR-3 | 歌词搜索 | 按"艺术家 + 标题（+ 时长）"从网易云 / QQ 音乐搜索，自动打分匹配 |
| FR-4 | LRC 解析 | 标准 LRC（行级时间戳），支持 offset 标签、一行多时间戳 |
| FR-5 | 翻译显示 | **可选开关**（已确认）：开启后在原文下方显示翻译行（网易 tlyric / QQ trans） |
| FR-6 | 悬浮渲染 | 单行歌词（开翻译时两行），置顶、透明背景、鼠标穿透、不出现在任务栏和 Alt+Tab |
| FR-7 | 位置调整 | "解锁"模式下可拖动到任意位置，"锁定"后恢复穿透；位置持久化 |
| FR-8 | 手动校正 | **需要（已确认）**：自动匹配失败/错误时，在设置窗口手动搜索并指定歌词，绑定关系持久化 |
| FR-9 | 本地缓存 | 命中过的歌词落盘缓存，重复播放不再请求网络；支持"未找到"负缓存 |
| FR-10 | 托盘常驻 | 无主窗口，托盘图标提供：锁定/解锁、显示/隐藏、设置、退出 |
| FR-11 | 时间偏移 | 全局偏移（ms）+ 单曲偏移（存于校正记录），修正歌词快慢 |

### 1.3 设置项清单（封闭集合，体现"极简"）

设置项以下述清单为**上限**，不再增加：

**外观**
- 字体（系统字体枚举）、字号、字重
- 主颜色、阴影开关（保证任意背景下可读，代替描边）
- 不透明度
- 最大宽度（超宽自动缩小字号，下限后省略号截断）
- 位置（拖动设定，无坐标输入框）

**行为**
- 显示翻译（开关，默认关）
- 暂停时隐藏（开关，默认关）
- 无歌词时显示曲目标题（开关，默认开）
- 开机自启（HKCU Run 注册表项）

**SMTC**
- 会话选择：自动（跟随系统 CurrentSession）/ 锁定某播放器（从活动会话列表选择 AUMID）

**歌词源**
- 源启用与优先级（网易云 / QQ 音乐，可拖动排序）
- 全局时间偏移（ms）
- 手动搜索校正（当前曲目）
- 清空缓存

### 1.4 非功能性需求（NFR）

| 编号 | 需求 | 量化目标 |
|------|------|---------|
| NFR-1 | 轻量 | 第三方运行时依赖 = **仅 Avalonia**；不引入 MVVM 框架、JSON 库（用 BCL）、HTTP 库（用 HttpClient）、图像库 |
| NFR-2 | 资源占用 | 空闲 CPU ≈ 0%，播放中 < 1%（100ms 定时器 + 行级重绘）；内存 < 150MB |
| NFR-3 | 分发 | **自包含单文件 exe（已确认）**，免安装 .NET；启用裁剪后目标 35–60MB |
| NFR-4 | 无侵入 | 不 hook 任何进程、不读写其他程序数据、仅出站 HTTPS 请求歌词 |
| NFR-5 | 网络礼貌 | 每次切歌 ≤ 3 次请求，8s 超时，缓存优先，绝不轮询接口 |

### 1.5 明确不做（Out of Scope）

以下内容 v1 明确排除，避免范围膨胀：

- 逐字卡拉 OK 效果（QRC/YRC/klyric 逐字时间轴）
- 多行滚动歌词面板、专辑封面、频谱
- 控制播放（暂停/切歌）——本程序只读 SMTC
- 本地音频文件解析 / 本地 LRC 文件扫描
- 歌词编辑器、上传分享
- 全屏游戏内 overlay（独占全屏本就无法覆盖，不做特殊处理）
- 多语言 UI（仅中文）
- 自动更新

---

## 2. 技术选型

| 项 | 选择 | 理由 / 否决项 |
|----|------|--------------|
| 运行时 | .NET 10 (LTS) | 当前 LTS（支持至 2028-11）；.NET 8 于 2026-11 EOL |
| TFM | `net10.0-windows10.0.19041.0`，`SupportedOSPlatformVersion=10.0.17763.0` | Windows TFM 自动引入 CsWinRT 投影（`Windows.Media.Control`），**无需任何额外 NuGet 包**；SMTC 读取 API 最低 17763 (1809) |
| UI | Avalonia 12.1 | 需求指定；Fluent 主题；保守可退回 11.3.x LTS 线 |
| MVVM | 手写 `ObservableObject`（约 30 行） | 项目仅 2 个窗口 + 2 个 ViewModel，引入 ReactiveUI/Prism/CommunityToolkit 违背 NFR-1 |
| JSON | `System.Text.Json`：设置/缓存用源生成（trim 友好），接口响应用 `JsonDocument` 手解 | 否决 Newtonsoft.Json；接口 DTO 不建类，避免裁剪反射问题 |
| HTTP | 单例 `HttpClient` + 自动解压 | 否决 RestSharp/Flurl |
| 托盘 | Avalonia 内置 `TrayIcon` | 否决 Hardcodet/WinForms NotifyIcon |
| 穿透/置顶 | 手写 Win32 P/Invoke（约 60 行） | Avalonia 官方确认不内置点击穿透，需 `WS_EX_TRANSPARENT` |
| 配置存储 | `%AppData%\EasyDesktopLyrics\` 下 JSON 文件 | 否决 SQLite（杀鸡用牛刀） |

最终依赖清单（完整）：

```
Avalonia
Avalonia.Desktop
Avalonia.Themes.Fluent
Avalonia.Diagnostics   (仅 Debug)
```

---

## 3. 总体架构

### 3.1 模块图

```
┌─────────────────────────────── UI 层 ───────────────────────────────┐
│  LyricsOverlayWindow      SettingsWindow          TrayIcon(App)     │
│  （穿透悬浮窗，只读绑定）  （设置 + 手动校正）      （菜单/生命周期）    │
│          ▲                        ▲ ▼                               │
│          │ OverlayViewModel       │ SettingsViewModel               │
└──────────┼────────────────────────┼─────────────────────────────────┘
           │ 当前行文本/可见性        │ 读写设置、触发手动搜索
┌──────────┴────────────────────────┴─────────────────────────────────┐
│                        协调层  LyricsOrchestrator                    │
│   曲目变化 → 查缓存/查 override → 搜索匹配 → 解析 → 发布 LyricDocument │
│   时钟 tick → 二分定位当前行 → 仅在行号变化时通知 UI                    │
└──────┬──────────────┬──────────────┬──────────────┬─────────────────┘
       │              │              │              │
┌──────┴─────┐ ┌──────┴──────┐ ┌─────┴──────┐ ┌─────┴───────┐
│ SmtcService│ │PlaybackClock│ │LyricsCache │ │ Providers    │
│ 会话/事件/  │ │ 进度插值    │ │ 磁盘缓存    │ │ ILyricsProvider
│ 元数据封装  │ │            │ │ +override  │ │ ├ Netease    │
└──────┬─────┘ └─────────────┘ └────────────┘ │ └ QQMusic    │
       │                                      └──────┬───────┘
┌──────┴─────────┐                            ┌──────┴───────┐
│ Windows.Media. │                            │  HttpClient  │
│ Control (WinRT)│                            │  (music.163 / │
└────────────────┘                            │   c.y.qq.com)│
                                              └──────────────┘
基础设施：SettingsService（JSON 持久化） / Win32Interop（穿透、置顶、自启动）/ LrcParser
```

### 3.2 核心数据流（切歌）

```
播放器切歌
  → SMTC MediaPropertiesChanged（去抖 300ms，可能连发多次）
  → SmtcService 发布 TrackChanged(TrackInfo{title, artist, album, duration})
  → LyricsOrchestrator（取消上一曲未完成的搜索任务）
       1. 查 overrides.json（手动校正记录）→ 命中则直取指定歌词
       2. 查磁盘缓存（含"未找到"负缓存）→ 命中则直接解析
       3. 按优先级遍历 Provider：搜索 → 打分 → 过阈值则取歌词 → 写缓存
       4. 全部失败 → 写负缓存 → 显示标题或隐藏（按设置）
  → 解析为 LyricDocument（行数组 + 可选翻译，按时间排序）
  → OverlayViewModel 更新
```

### 3.3 渲染循环（播放中）

```
DispatcherTimer(100ms，仅播放且有歌词时运行)
  → PlaybackClock.Estimate()   // basePos + (now - baseAt) × rate
  → 二分查找当前行索引（含全局偏移 + 单曲偏移）
  → 索引变化时才更新 OverlayViewModel.CurrentLine（最小化重绘）

SMTC TimelinePropertiesChanged（seek/定期上报）→ PlaybackClock 重新校准
SMTC PlaybackInfoChanged（暂停/恢复）→ 启停定时器、按设置隐藏
```

### 3.4 线程模型

| 线程 | 职责 |
|------|------|
| UI 线程 | 渲染、定时器 tick、ViewModel 更新 |
| WinRT 回调线程（MTA） | SMTC 事件 → 一律 `Dispatcher.UIThread.Post` 转发，**不得直接碰 UI** |
| 线程池 | HTTP 请求、磁盘缓存读写（async/await），每曲一个 `CancellationTokenSource`，切歌即取消 |

### 3.5 应用状态机

```
                 ┌────────────┐  找到会话   ┌────────────┐
  启动 ────────▶ │  NoSession │ ─────────▶ │  Resolving │──搜索失败──▶ NoLyric
                 │ (窗口隐藏)  │            │ (显示标题…) │             (标题/隐藏)
                 └────────────┘ ◀───────── └─────┬──────┘
                        ▲        会话消失         │ 匹配成功
                        │                        ▼
                  暂停(可选隐藏) ◀──────────  Playing(逐行滚动)
```

---

## 4. 项目结构

单项目结构（无需拆分类库）：

```
EasyDesktopLyrics/
├─ EasyDesktopLyrics.csproj
├─ app.manifest                  # PerMonitorV2 DPI、Win10 兼容声明
├─ Program.cs                    # 入口、单实例 Mutex
├─ App.axaml / App.axaml.cs      # TrayIcon、生命周期、服务组装（手动 new，不用 DI 容器）
├─ Assets/app.ico
├─ Models/
│  ├─ AppSettings.cs             # 设置 POCO（对应 1.3 清单）
│  ├─ TrackInfo.cs               # title/artist/album/duration/AUMID
│  ├─ LyricDocument.cs           # LyricLine[]{TimeMs, Text, Translation?}
│  └─ LyricSearchResult.cs       # provider/songId/title/artist/durationMs/score
├─ Services/
│  ├─ SmtcService.cs
│  ├─ PlaybackClock.cs
│  ├─ LyricsOrchestrator.cs
│  ├─ LrcParser.cs
│  ├─ LyricsCache.cs             # cache/ + overrides.json
│  ├─ SettingsService.cs
│  └─ Providers/
│     ├─ ILyricsProvider.cs
│     ├─ NeteaseLyricsProvider.cs
│     └─ QQMusicLyricsProvider.cs
├─ ViewModels/
│  ├─ ObservableObject.cs        # 手写 INotifyPropertyChanged 基类
│  ├─ OverlayViewModel.cs
│  └─ SettingsViewModel.cs
├─ Views/
│  ├─ LyricsOverlayWindow.axaml(.cs)
│  └─ SettingsWindow.axaml(.cs)
└─ Interop/
   └─ Win32.cs                   # 穿透/置顶/自启动 P/Invoke
```

数据目录：

```
%AppData%\EasyDesktopLyrics\
├─ settings.json                 # AppSettings（源生成序列化，防抖保存）
├─ overrides.json                # trackKey → {provider, songId, offsetMs}
└─ cache\lyrics\{sha1(trackKey)}.json
                                 # {source, songId, lrc, tlrc, notFound, fetchedAt}
```

---

## 5. 风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| 网易/QQ 接口为非官方，可能变更、限流、加验证 | 搜不到歌词 | Provider 接口抽象，单源失效不影响另一源；缓存优先；请求量极低；接口细节集中在各 Provider 单文件内，便于热修 |
| 播放器时间线质量参差（如部分播放器只在 seek 时上报，或恒为 0） | 歌词不同步/无法滚动 | 插值时钟 + `LastUpdatedTime` 合法性校验；完全无时间线时降级为仅显示标题 |
| 浏览器 SMTC 会话干扰（刷视频时抢占 CurrentSession） | 显示无关"歌词" | 设置中"锁定某播放器"（按 AUMID 过滤） |
| 元数据脏（标题带 "(Live)"、"feat."、频道名后缀） | 匹配失败 | 归一化 + 打分制（见实现文档 §6），保底手动校正 |
| 置顶被其他置顶窗口覆盖 | 歌词被挡 | 低频重申 `HWND_TOPMOST`（事件驱动 + 稀疏定时） |
| 裁剪（PublishTrimmed）破坏 Avalonia/WinRT | 运行时崩溃 | `TrimMode=partial` + 冒烟测试清单；出问题即关裁剪换体积 |
| 版权 | — | 仅实时显示、仅个人使用场景、不提供导出/下载功能；README 声明 |

---

## 6. 里程碑

| 阶段 | 内容 | 验收 |
|------|------|------|
| M1 悬浮窗骨架 | 透明置顶穿透窗口、锁定/解锁拖动、托盘、设置持久化 | 假数据歌词显示正常，穿透/置顶/DPI 正确 |
| M2 SMTC | SmtcService + PlaybackClock，显示"标题 - 艺术家"与实时进度 | 对 3+ 播放器切歌/暂停/seek 均正确 |
| M3 歌词 | 两个 Provider + 匹配 + LRC 解析 + 缓存 → 端到端逐行滚动 | 常见中英文歌命中率主观可用 |
| M4 校正与打磨 | 手动搜索校正、翻译开关、偏移、无歌词降级、自启动 | 1.2/1.3 清单全项通过 |
| M5 发布 | 单文件裁剪发布、体积与资源占用达标、兼容性测试矩阵 | NFR 全项通过 |

兼容性测试矩阵（M2/M5 执行）：Spotify、网易云音乐 PC、QQ 音乐 PC、Apple Music (Store)、
foobar2000（新版内置 SMTC，旧版需组件）、Edge/Chrome 网页播放器 × Win10 22H2 / Win11 × 100%/150% DPI × 双显示器。
