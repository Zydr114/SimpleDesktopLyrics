# SimpleDesktopLyrics

极简 Windows 桌面歌词——通过 [SMTC](https://learn.microsoft.com/windows/uwp/audio-video-camera/system-media-transport-controls) 与任意播放器同步，悬浮于桌面所有窗口之上，鼠标穿透、不打扰正常使用。

![Platform](https://img.shields.io/badge/platform-Windows%2010%201909+-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8A2BE2)

## 功能

- **SMTC 同步** — 自动检测 Spotify、网易云音乐、QQ 音乐、Apple Music、foobar2000 等任何支持 Windows SMTC 的播放器
- **自动歌词匹配** — 从网易云音乐 / QQ 音乐公开接口搜索并匹配 LRC 歌词，离线缓存
- **桌面悬浮** — 一行歌词悬浮于所有窗口之上，鼠标穿透，不抢焦点，不出现在 Alt+Tab
- **描边 & 阴影** — 可选文字描边（8 方向偏移）和阴影，保证任意背景可读
- **翻译显示** — 支持网易云翻译行（tlyric），可单独调整字号与间距
- **PS 风格色板** — 内置 100 色全光谱色板，一键选取歌词颜色
- **位置预设** — 九宫格快捷定位 + 百分比滑块，覆盖屏幕任意位置
- **手动校正** — 自动匹配失败时，可从设置面板手动搜索并指定歌词
- **极轻量** — 唯一第三方依赖为 Avalonia，单文件 exe 约 14MB，空闲 CPU ≈ 0%

## 截图

```

┌───────────────────────────────────────────────────┐
│                                                   │
│                  故事的小黄花                        │    ← 悬浮歌词（置顶、穿透）
│                  从出生那年就飘着                     │
│                                                   │
│          [托盘图标] ─ 锁定 · 显示 · 设置 · 退出     │
└───────────────────────────────────────────────────┘
```

## 使用

1. 下载 [最新 Release](https://github.com/Zydr114/SimpleDesktopLyrics/releases) 中的 `EasyDesktopLyrics.exe`，放到任意目录双击运行
2. 打开任意音乐播放器（Spotify、网易云、QQ 音乐等）开始播放
3. 歌词自动出现在桌面底部
4. 右下角托盘图标可：锁定/解锁拖动、显示/隐藏、打开设置
5. 解锁状态下可直接拖动歌词窗口到任意位置，双击锁定

## 设置项速览

| 分类 | 选项 |
|------|------|
| 外观 | 字体/字号/字重/颜色/色板、阴影开关、描边开关（颜色+宽度）、不透明度、最大宽度 |
| 布局 | 翻译字号、行间距、对齐方式（居中/左/右）、九宫格位置预设 + 水平/垂直百分比滑块 |
| 行为 | 显示翻译、暂停时隐藏、无歌词时显示标题、开机自启 |
| SMTC | 监听播放器选择（自动/锁定指定 App）、歌词源开关与优先级、全局偏移 |
| 校正 | 手动搜索歌词、应用指定歌词/清除校正、单曲偏移 |

## 构建

```powershell
# 需要 .NET 10 SDK
git clone https://github.com/Zydr114/SimpleDesktopLyrics.git
cd SimpleDesktopLyrics

# Debug 运行
dotnet run

# 自包含单文件发布
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=true -p:TrimMode=partial

# 产物位于 bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\
```

```powershell
# 调试命令
.\EasyDesktopLyrics.exe --probe "歌名" "歌手"   # 测试歌词搜索
.\EasyDesktopLyrics.exe --probe-smtc             # 测试 SMTC 会话
```

## 技术栈

| 组件 | 选型 |
|------|------|
| 运行时 | .NET 10 (net10.0-windows10.0.19041.0) |
| UI 框架 | Avalonia 12.1 + FluentTheme |
| SMTC | Windows.Media.Control (WinRT 投影) |
| HTTP | System.Net.Http.HttpClient（单例） |
| JSON | System.Text.Json（源生成，裁剪安全） |
| 存储 | %AppData%\EasyDesktopLyrics 下 JSON 文件 |

## 许可

MIT
