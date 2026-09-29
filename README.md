# EXPAS · 经验扩展插件系统

> **Experience, eXtended Plugins, Audio, and Status System**
> SCP:SL（SCP Secret Laboratory）LabAPI 插件

[![状态](https://img.shields.io/badge/status-Beta-orange)](#-版本状态beta)
[![平台](https://img.shields.io/badge/platform-SCP%3ASL%20LabAPI-blue)](#-环境要求)
[![授权](https://img.shields.io/badge/license-专有%2F保留所有权利-red)](LICENSE)

---

## 版本状态：Beta

当前版本 **v2.0.0-beta**。

Beta 的含义是：**功能已经可以稳定使用，但仍在持续修问题**——绝大部分场景没问题，但可能存在尚未覆盖的边界情况。欢迎在使用中反馈，升级时建议关注版本变更。

---

## 简介

EXPAS 是一套面向 SCP:SL LabAPI 服务端的综合插件，把**经验等级、称号、音频播放、状态面板**四块能力做进同一个插件里，并通过 SSS 面板（`ESC → 设置 → 服务器设置 → EXPAS`）把界面的自定义权交给每个玩家自己。

所有 EXPAS 指令**只**注册在 **RA 远程管理面板** 与 **服务器控制台**；玩家在游戏内不能执行任何 EXPAS 指令。

---

## 主要功能

### 经验与等级
- 伤害换算经验，可配置倍率、成长曲线、等级上限
- 等级前缀显示、升级提示、游玩时长/天数统计
- 死亡结算 / 立即结算可选；可选只统计真人、是否计入 SCP 与人机伤害

### 玩家数据
- 按 Steam64 持久化：等级、经验、累计经验、总伤害、总击杀、死亡、积分、游玩时长、游玩天数、今日游玩
- 四种锁定模型（正常 / 惩罚 / 自助封禁 / 长期未上线），自助封禁解锁时一次性补入账

### 称号系统（三层）
- 基于 LabAPI 的 `Player.CustomInfo`（名字下方灰字）
- 层 1：玩家本来没有称号时才设
- 层 2：强制替换
- 层 3：彻底清除

### HUD 与自定义 UI
- HUD 内容全部由占位符模板驱动（39 个占位符）
- 玩家可在 SSS 面板里单独调：字号、屏幕缩放、整体缩放、行距、垂直位置、水平对齐、等级块对齐、折叠模式、屏幕分辨率档位
- **每个玩家的界面完全隔离**，按 Steam64 记忆，互不干涉
- 支持 `layouts\` 目录下的 `*.txt` / `*.js` 布局文件自定义整块 HUD
- 布局文件按 **Steam64 归属识别**（文件名 17 位前缀 或 首行 `owner=`），只有归属匹配的玩家才能看到并使用自己的布局

### 音频与第一名音乐盒
- **不使用任何假人**：播放走"隐藏音箱"（远处高空的非空间化 SpeakerToy），全服可闻，玩家列表零新增
- 支持格式：`wav / ogg / mp3 / m4a / aac / mp4 / flac / wma / opus / aiff / aif / wv / ape / mka / webm`
- **自包含解码**：内嵌自建原生 DLL 解码器（FFmpeg 共享库 + 自写 C 包装层），启动时自动释放到 `EXPAS\bin\native\`，**不需要任何外部 exe，也不需要另行安装 FFmpeg**
- 第一名音乐盒：回合结束时检测本回合伤害第一名，用其 Steam64 检索 `mvp_music\` 下的 JS 文件，命中即播放对应音频
- 对第三方插件开放 `ExpasApi`（`PlayAudioFile` / `PlayMvpMusic` / `StopAudio`）

### 面板与维护
- SSS 面板与其它插件的面板项**互不干扰**（按实例引用过滤自己的项，发送时带上全局完整集合）
- `EXPAS arsa`：重新识别"除自身以外"的全部插件（重扫插件目录 + 重读配置文件），支持 `full` 完整重跑

---

## 环境要求

| 项目 | 要求 |
|---|---|
| 游戏 | SCP:SL 专用服务器（Dedicated Server） |
| 框架 | LabAPI |
| 操作系统 | Windows |
| 其它依赖 | 无（解码器自包含，不需要 FFmpeg / Node.js 等） |

---

## 安装

1. 下载 [`源码与成品/成品/EXPAS.NP.dll`](源码与成品/成品/EXPAS.NP.dll)
2. 放进服务器的 LabAPI 插件目录：

   ```
   %APPDATA%\SCP Secret Laboratory\LabAPI\plugins\global\EXPAS.NP.dll
   ```

3. 重启服务器

首次启动会自动创建：

```
%APPDATA%\SCP Secret Laboratory\LabAPI\configs\<端口>\EXPAS\
├─ config.yml            主配置
├─ layouts\              HUD 布局（自动生成 default.txt / example.txt）
├─ mvp_music\            第一名音乐盒（JS 声明 + 音频）
├─ audio\                通用音频
├─ players\              玩家数据
└─ bin\native\           运行时释放的原生解码 DLL
```

启动日志里应能看到：

```
[EXPAS] 原生解码器就绪: EXPAS.AudioCodec/1.0 (ffmpeg n9.0.1)  释放 6 个 DLL → ...\EXPAS\bin\native
[EXPAS] 音频解码器: 原生 DLL 解码器(自建, ...)
[EXPAS] SSS 面板已注册(HUD 字号/屏幕缩放/位置/分辨率档/自定义 UI 可由玩家自调)
```

---

## 快速开始

在 RA 面板或服务器控制台输入（主命令 `EXPAS`，别名 `expsr` / `expr`）：

```
EXPAS                                 简略总览 / 帮助
EXPAS se                              按关键字匹配子命令
EXPAS info 3                          查玩家数据（序列ID / 玩家名 / Steam64 三种写法）
EXPAS set 示例 level 225              改数值字段
EXPAS add 示例 points 300
EXPAS title 示例 2 <color=#FFCC00>示例</color>    称号三层
EXPAS audio play 示例开场曲.wav         播放音频
EXPAS mvpmusic list                   第一名音乐盒条目与文件检查
EXPAS layout reload                   改完布局文件后重扫
EXPAS whitelist add 76561198000000000 自定义 UI 白名单
EXPAS arsa                            重新识别其它插件
```

> `<玩家ID>` 支持三种写法：**局内序列 ID**（如 `3`）、**玩家名**（可只写一部分）、**Steam64 / 完整 UserId**。
> 前两种需在线，Steam64 离线也能改。三种写法都可在 `config.yml` 里单独开关。

**完整指令说明见 [`解析/01_指令使用方法.txt`](解析/01_指令使用方法.txt)**

---

## 文档

| 文档 | 内容 |
|---|---|
| [`解析/01_指令使用方法.txt`](解析/01_指令使用方法.txt) | 全部指令逐条详解、字段、占位符、配置键、示例速查 |
| [`解析/02_自定义UI编写方法.txt`](解析/02_自定义UI编写方法.txt) | 布局文件 `.txt` / `.js` 写法、行类型、归属识别、白名单、玩家面板可调项 |
| [`解析/03_音乐盒JS编写方法.txt`](解析/03_音乐盒JS编写方法.txt) | 第一名音乐盒 JS 写法、路径解析、音频要求、排查 |
| [`投稿与部署/玩家投稿填写说明.txt`](投稿与部署/玩家投稿填写说明.txt) | 面向玩家投稿的填写说明（含一键部署脚本） |

---

## 从源码构建

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

**打包插件本体**（需要 .NET SDK 提供 Roslyn `csc.dll`，以及本机装好的 SCP:SL 专用服务器用于取引用程序集）：

```powershell
powershell -ExecutionPolicy Bypass -File 源码与成品\源码\build.ps1
```

> `build.ps1` **必须以 UTF-8 带 BOM 保存**。PowerShell 5.1 会把无 BOM 的脚本按系统 ANSI 解码，文件里的中文会变成乱码。

**重建原生解码器**：见 [`源码与成品/源码/native/src/BUILD.txt`](源码与成品/源码/native/src/BUILD.txt)。
原生 DLL 需要 MSYS2 + mingw-w64 工具链，FFmpeg 源码请从 <https://ffmpeg.org/> 获取 **n9.0.1** 版本（`rebuild.sh` 中的 `configure` 参数即为本项目使用的音频专用裁剪配置）。

---

## 许可

本项目采用**专有许可**：**保留所有权利**（All Rights Reserved）。

- ✅ 可以：下载、阅读源码、在自己的服务器上使用、为自己服务器修改与重新编译、原样转发
- ❌ 不可以：修改后以任何名义二次发布、移除署名、据为己有、商业售卖

完整条款见 [`LICENSE`](LICENSE)。第三方组件（FFmpeg 等）不受本项目许可约束，见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。

**Copyright (c) 2026 example_dev. All rights reserved.**
