# EXPAS · 经验扩展插件系统 (EXPAS · Experience Expansion Plugin System)

> **Experience, eXtended Plugins, Audio, and Status System**
> SCP:SL（SCP Secret Laboratory）LabAPI 插件 (A LabAPI plugin for SCP:SL — SCP Secret Laboratory)

[![状态](https://img.shields.io/badge/status-Beta-orange)](#版本状态beta-version-status-beta)
[![平台](https://img.shields.io/badge/platform-SCP%3ASL%20LabAPI-blue)](#环境要求-requirements)
[![授权](https://img.shields.io/badge/license-CC0--1.0-lightgrey)](LICENSE)

---

## 版本状态：Beta (Version Status: Beta)

当前版本 **v2.0.0-beta**。
(Current version: **v2.0.0-beta**.)

Beta 的含义是：**功能已经可以稳定使用，但仍在持续修问题**——绝大部分场景没问题，但可能存在尚未覆盖的边界情况。欢迎在使用中反馈，升级时建议关注版本变更。
(Beta means **the features are stable enough to use, but fixes are still ongoing** — most scenarios are fine, while some edge cases may not be covered yet. Feedback is welcome; please watch the changelog when upgrading.)

---

## 简介 (Introduction)

EXPAS 是一套面向 SCP:SL LabAPI 服务端的综合插件，把**经验等级、称号、音频播放、状态面板**四块能力做进同一个插件里，并通过 SSS 面板（`ESC → 设置 → 服务器设置 → EXPAS`）把界面的自定义权交给每个玩家自己。
(EXPAS is an all-in-one plugin for SCP:SL LabAPI servers that combines **XP & levels, titles, audio playback and a status panel** in a single plugin. Through the SSS panel (`ESC → Settings → Server Settings → EXPAS`), every player can customize their own UI.)

所有 EXPAS 指令**只**注册在 **RA 远程管理面板** 与 **服务器控制台**；玩家在游戏内不能执行任何 EXPAS 指令。
(All EXPAS commands are registered **only** in the **RA remote admin panel** and the **server console**; players cannot run any EXPAS command in-game.)

---

## 主要功能 (Main Features)

### 经验与等级 (XP & Levels)
- 伤害换算经验，可配置倍率、成长曲线、等级上限
  (Damage is converted into XP; the multiplier, growth curve and level cap are configurable)
- 等级前缀显示、升级提示、游玩时长/天数统计
  (Level prefix display, level-up notice, play-time / play-day statistics)
- 死亡结算 / 立即结算可选；可选只统计真人、是否计入 SCP 与人机伤害
  (Settle on death or settle immediately — your choice; optionally count only real players, and choose whether SCP and bot damage count)

### 玩家数据 (Player Data)
- 按 Steam64 持久化：等级、经验、累计经验、总伤害、总击杀、死亡、积分、游玩时长、游玩天数、今日游玩
  (Persisted per Steam64: level, XP, total XP, total damage, total kills, deaths, points, play time, play days, today's play time)
- 四种锁定模型（正常 / 惩罚 / 自助封禁 / 长期未上线），自助封禁解锁时一次性补入账
  (Four lock models (normal / penalty / self-ban / long inactivity); when a self-ban is lifted, the accumulated values are credited in one go)

### 称号系统（三层） (Title System (Three Layers))
- 基于 LabAPI 的 `Player.CustomInfo`（名字下方灰字）
  (Based on LabAPI's `Player.CustomInfo` — the grey text under a player's name)
- 层 1：玩家本来没有称号时才设
  (Layer 1: only sets a title when the player does not already have one)
- 层 2：强制替换
  (Layer 2: force-replaces the title)
- 层 3：彻底清除
  (Layer 3: removes the title completely)

### HUD 与自定义 UI (HUD & Custom UI)
- HUD 内容全部由占位符模板驱动（39 个占位符）
  (All HUD content is driven by placeholder templates — 39 placeholders in total)
- 玩家可在 SSS 面板里单独调：字号、屏幕缩放、整体缩放、行距、垂直位置、水平对齐、等级块对齐、折叠模式、屏幕分辨率档位
  (Each player can adjust in the SSS panel: font size, screen scale, overall scale, line spacing, vertical position, horizontal alignment, level-block alignment, collapse mode, screen-resolution preset)
- **每个玩家的界面完全隔离**，按 Steam64 记忆，互不干涉
  (**Every player's UI is fully isolated**) — remembered by Steam64, so players never affect each other
- 支持 `layouts\` 目录下的 `*.txt` / `*.js` 布局文件自定义整块 HUD
  (Layout files `*.txt` / `*.js` under the `layouts\` directory can customize the whole HUD block)
- 布局文件按 **Steam64 归属识别**（文件名 17 位前缀 或 首行 `owner=`），只有归属匹配的玩家才能看到并使用自己的布局
  (Layout files are identified by **Steam64 ownership** (a 17-digit filename prefix or an `owner=` first line); only the owning player can see and use that layout)

### 音频与第一名音乐盒 (Audio & First-Place Music Box)
- **不使用任何假人**：播放走"隐藏音箱"（远处高空的非空间化 SpeakerToy），全服可闻，玩家列表零新增
  (**No fake players are used**): playback goes through a "hidden speaker" (a non-spatial `SpeakerToy` placed high in the distance), audible server-wide, adding nothing to the player list
- 支持格式（Supported formats）：`wav / ogg / mp3 / m4a / aac / mp4 / flac / wma / opus / aiff / aif / wv / ape / mka / webm`
- **自包含解码**：内嵌自建原生 DLL 解码器（FFmpeg 共享库 + 自写 C 包装层），启动时自动释放到 `EXPAS\bin\native\`，**不需要任何外部 exe，也不需要另行安装 FFmpeg**
  (**Self-contained decoding**): a self-built native DLL decoder is embedded (FFmpeg shared libraries + a custom C wrapper layer) and extracted at startup to `EXPAS\bin\native\` — **no external exe and no separate FFmpeg installation are required**
- 第一名音乐盒：回合结束时检测本回合伤害第一名，用其 Steam64 检索 `mvp_music\` 下的 JS 文件，命中即播放对应音频
  (First-place music box: at round end the top-damage player of the round is detected; their Steam64 is used to look up a JS file under `mvp_music\`, and the matching audio is played when a hit is found)
- 对第三方插件开放 `ExpasApi`（`PlayAudioFile` / `PlayMvpMusic` / `StopAudio`）
  (`ExpasApi` is exposed to third-party plugins (`PlayAudioFile` / `PlayMvpMusic` / `StopAudio`))

### 面板与维护 (Panel & Maintenance)
- SSS 面板与其它插件的面板项**互不干扰**（按实例引用过滤自己的项，发送时带上全局完整集合）
  (The SSS panel and other plugins' panel entries **do not interfere**: entries are filtered by instance reference, and the full global set is sent on submit)
- `EXPAS arsa`：重新识别"除自身以外"的全部插件（重扫插件目录 + 重读配置文件），支持 `full` 完整重跑
  (`EXPAS arsa`: re-identifies all plugins **other than itself** (rescan the plugin directory + re-read config files); `full` performs a complete re-run)

---

## 环境要求 (Requirements)

| 项目 (Item) | 要求 (Requirement) |
|---|---|
| 游戏 (Game) | SCP:SL 专用服务器 (SCP:SL Dedicated Server) |
| 框架 (Framework) | LabAPI |
| 操作系统 (OS) | Windows |
| 其它依赖 (Other dependencies) | 无（解码器自包含，不需要 FFmpeg / Node.js 等）(None — the decoder is self-contained; no FFmpeg / Node.js etc. required) |

---

## 安装 (Installation)

1. 下载 (Download) [`源码与成品/成品/EXPAS.NP.dll`](源码与成品/成品/EXPAS.NP.dll)
2. 放进服务器的 LabAPI 插件目录 (Copy it into the server's LabAPI plugin directory):

   ```
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\global\EXPAS.NP.dll
   ```

3. 重启服务器 (Restart the server)

首次启动会自动创建 (On first startup, the following are created automatically):

```
%APPDATA%\SCP Secret Laboratory\LabAPI\configs\<端口>\EXPAS\
├─ config.yml            主配置
├─ layouts\              HUD 布局（自动生成 default.txt / example.txt）
├─ mvp_music\            第一名音乐盒（JS 声明 + 音频）
├─ audio\                通用音频
├─ players\              玩家数据
└─ bin\native\           运行时释放的原生解码 DLL
```

| 条目 (Entry) | 说明 (Description) |
|---|---|
| `config.yml` | 主配置 (Main configuration) |
| `layouts\` | HUD 布局（自动生成 `default.txt` / `example.txt`）(HUD layouts; `default.txt` / `example.txt` are generated automatically) |
| `mvp_music\` | 第一名音乐盒（JS 声明 + 音频）(First-place music box: JS declarations + audio) |
| `audio\` | 通用音频 (General-purpose audio) |
| `players\` | 玩家数据 (Player data) |
| `bin\native\` | 运行时释放的原生解码 DLL (Native decoder DLLs extracted at runtime) |

启动日志里应能看到 (The startup log should show):

```
[EXPAS] 原生解码器就绪: EXPAS.AudioCodec/1.0 (ffmpeg n9.0.1)  释放 6 个 DLL → ...\EXPAS\bin\native
[EXPAS] 音频解码器: 原生 DLL 解码器(自建, ...)
[EXPAS] SSS 面板已注册(HUD 字号/屏幕缩放/位置/分辨率档/自定义 UI 可由玩家自调)
```

---

## 快速开始 (Quick Start)

在 RA 面板或服务器控制台输入（主命令 `EXPAS`，别名 `expsr` / `expr`）：
(Enter the following in the RA panel or the server console (main command `EXPAS`, aliases `expsr` / `expr`):)

```
EXPAS                                 简略总览 / 帮助
EXPAS se                              按关键字匹配子命令
EXPAS info 3                          查玩家数据（序列ID / 玩家名 / Steam64 三种写法）
EXPAS set 示例玩家 level 225              改数值字段
EXPAS add 示例玩家 points 300
EXPAS title 示例玩家 2 <color=#FFCC00>示例玩家</color>    称号三层
EXPAS audio play 示例开场曲.wav         播放音频
EXPAS mvpmusic list                   第一名音乐盒条目与文件检查
EXPAS layout reload                   改完布局文件后重扫
EXPAS whitelist add 76561198000000000 自定义 UI 白名单
EXPAS arsa                            重新识别其它插件
```

| 指令 (Command) | 用途 (Purpose) |
|---|---|
| `EXPAS` | 简略总览 / 帮助 (Brief overview / help) |
| `EXPAS se` | 按关键字匹配子命令 (Match a subcommand by keyword) |
| `EXPAS info 3` | 查玩家数据（序列ID / 玩家名 / Steam64 三种写法）(Look up player data — sequence ID / player name / Steam64) |
| `EXPAS set 示例玩家 level 225` | 改数值字段 (Set a numeric field) |
| `EXPAS add 示例玩家 points 300` | 改数值字段（累加）(Add to a numeric field) |
| `EXPAS title 示例玩家 2 <color=#FFCC00>示例玩家</color>` | 称号三层 (Apply one of the three title layers) |
| `EXPAS audio play 示例开场曲.wav` | 播放音频 (Play an audio file) |
| `EXPAS mvpmusic list` | 第一名音乐盒条目与文件检查 (List first-place music-box entries and check their files) |
| `EXPAS layout reload` | 改完布局文件后重扫 (Rescan after editing layout files) |
| `EXPAS whitelist add 76561198000000000` | 自定义 UI 白名单 (Add a player to the custom-UI whitelist) |
| `EXPAS arsa` | 重新识别其它插件 (Re-identify other plugins) |

> `<玩家ID>` 支持三种写法：**局内序列 ID**（如 `3`）、**玩家名**（可只写一部分）、**Steam64 / 完整 UserId**。
> (`<PlayerID>` supports three forms: the **in-round sequence ID** (e.g. `3`), the **player name** (a partial name is enough), and the **Steam64 / full UserId**.)
> 前两种需在线，Steam64 离线也能改。三种写法都可在 `config.yml` 里单独开关。
> (The first two require the player to be online; Steam64 can be edited even while offline. Each of the three forms can be toggled separately in `config.yml`.)

**完整指令说明见 (Full command reference) [`解析/01_指令使用方法.txt`](解析/01_指令使用方法.txt)**

---

## 文档 (Documentation)

| 文档 (Document) | 内容 (Content) |
|---|---|
| [`解析/01_指令使用方法.txt`](解析/01_指令使用方法.txt) | 全部指令逐条详解、字段、占位符、配置键、示例速查 (Every command explained one by one: fields, placeholders, config keys, quick examples) |
| [`解析/02_自定义UI编写方法.txt`](解析/02_自定义UI编写方法.txt) | 布局文件 `.txt` / `.js` 写法、行类型、归属识别、白名单、玩家面板可调项 (How to write `.txt` / `.js` layout files: line types, ownership detection, whitelist, player-adjustable options) |
| [`解析/03_音乐盒JS编写方法.txt`](解析/03_音乐盒JS编写方法.txt) | 第一名音乐盒 JS 写法、路径解析、音频要求、排查 (First-place music-box JS syntax, path resolution, audio requirements, troubleshooting) |
| [`投稿与部署/玩家投稿填写说明.txt`](投稿与部署/玩家投稿填写说明.txt) | 面向玩家投稿的填写说明（含一键部署脚本）(Filling instructions for player submissions, including a one-click deploy script) |

---

## 从源码构建 (Building from Source)

```
源码与成品\源码\
├─ *.cs              插件 C# 源码（9 个文件）
├─ build.ps1         打包脚本 → 输出到 ..\成品\EXPAS.NP.dll
└─ native\
   ├─ *.dll          6 个原生解码 DLL（构建输入，会被内嵌进 EXPAS.NP.dll）
   └─ src\           原生解码器的源码与重建脚本
      ├─ expas_codec.c   自写 C 包装层源码
      ├─ rebuild.sh      一键重建（编译 FFmpeg 共享库 → 编译包装层）
      ├─ BUILD.txt       构建三步 + 注意事项
      └─ cfg.sh / build.sh / wrap.sh   原始构建脚本
```

| 条目 (Entry) | 说明 (Description) |
|---|---|
| `*.cs` | 插件 C# 源码（9 个文件）(Plugin C# source — 9 files) |
| `build.ps1` | 打包脚本 → 输出到 `..\成品\EXPAS.NP.dll` (Build script → outputs to `..\成品\EXPAS.NP.dll`) |
| `native\*.dll` | 6 个原生解码 DLL（构建输入，会被内嵌进 `EXPAS.NP.dll`）(6 native decoder DLLs — build inputs, embedded into `EXPAS.NP.dll`) |
| `native\src\` | 原生解码器的源码与重建脚本 (Source and rebuild scripts of the native decoder) |
| `expas_codec.c` | 自写 C 包装层源码 (Source of the custom C wrapper layer) |
| `rebuild.sh` | 一键重建（编译 FFmpeg 共享库 → 编译包装层）(One-shot rebuild: build the FFmpeg shared libraries → build the wrapper layer) |
| `BUILD.txt` | 构建三步 + 注意事项 (Three build steps + caveats) |
| `cfg.sh / build.sh / wrap.sh` | 原始构建脚本 (Original build scripts) |

**打包插件本体**（需要 .NET SDK 提供 Roslyn `csc.dll`，以及本机装好的 SCP:SL 专用服务器用于取引用程序集）：
(**Building the plugin itself** (requires the Roslyn `csc.dll` provided by the .NET SDK, plus a locally installed SCP:SL dedicated server as the source of reference assemblies):)

```powershell
powershell -ExecutionPolicy Bypass -File 源码与成品\源码\build.ps1
```

> `build.ps1` **必须以 UTF-8 带 BOM 保存**。PowerShell 5.1 会把无 BOM 的脚本按系统 ANSI 解码，文件里的中文会变成乱码。
> (`build.ps1` **must be saved as UTF-8 with BOM**. PowerShell 5.1 decodes scripts without a BOM using the system ANSI code page, which garbles the Chinese text in the file.)

**重建原生解码器 (Rebuilding the native decoder)**：见 (see) [`源码与成品/源码/native/src/BUILD.txt`](源码与成品/源码/native/src/BUILD.txt)。
原生 DLL 需要 MSYS2 + mingw-w64 工具链，FFmpeg 源码请从 <https://ffmpeg.org/> 获取 **n9.0.1** 版本（`rebuild.sh` 中的 `configure` 参数即为本项目使用的音频专用裁剪配置）。
(The native DLLs require the MSYS2 + mingw-w64 toolchain. Fetch the FFmpeg source at version **n9.0.1** from <https://ffmpeg.org/> (the `configure` arguments in `rebuild.sh` are the trimmed audio-only configuration used by this project).)

---

## 许可 (License)

本项目采用 **CC0 1.0 Universal（公共领域贡献）** —— 在法律允许的最大范围内放弃全部著作权及相关权利。
(This project is released under **CC0 1.0 Universal (Public Domain Dedication)** — all copyright and related rights are waived to the maximum extent permitted by law.)

- ✅ 可以：任意使用、修改、再发布、商业使用，**无需署名**
  (✅ Allowed: use, modify, redistribute and use commercially in any way, **with no attribution required**)
- ✅ 可以：直接并入闭源或开源项目
  (✅ Allowed: merge directly into closed-source or open-source projects)
- ⚠️ **第三方组件（FFmpeg 等）不受本项目的 CC0 约束**，仍遵循各自的许可，见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)
  (⚠️ **Third-party components (FFmpeg etc.) are not covered by this project's CC0** and remain under their own licenses; see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md))
- ⚠️ 本作品按"现状"提供，不附带任何明示或默示担保
  (⚠️ This work is provided "as is", without any express or implied warranty)

完整条款见 [`LICENSE`](LICENSE)（CC0 1.0 官方法律文本）。
(Full terms are in [`LICENSE`](LICENSE) — the official CC0 1.0 legal text.)
