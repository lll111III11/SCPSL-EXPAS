# 第三方组件声明（THIRD-PARTY NOTICES）

本仓库的**原创部分**（C# 插件代码、原生解码包装层 `expas_codec.c`、构建脚本、文档）
由 example_dev 独立开发，采用仓库根目录 [`LICENSE`](LICENSE) 中的
**CC0 1.0 Universal（公共领域贡献）**。

但编译产物与源码目录中包含以下**第三方开源组件**，它们的版权与授权**不属于本项目**。
下面逐项说明，以满足各自的许可要求。

---

## 1. FFmpeg

| 项目 | 说明 |
|---|---|
| 组件 | `avcodec-63.dll`、`avformat-63.dll`、`avutil-61.dll`、`swresample-7.dll` |
| 位置 | `源码与成品/源码/native/`（同时被内嵌进 `成品/EXPAS.NP.dll`，运行时释放到 `EXPAS\bin\native\`） |
| 版权 | Copyright (c) FFmpeg developers |
| 授权 | **GNU Lesser General Public License v2.1 或更高版本**（LGPL-2.1-or-later） |
| 上游 | <https://ffmpeg.org/> |
| 本项目使用版本 | **n9.0.1** |
| 授权文本 | <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html> ／ 上游源码树中的 `COPYING.LGPLv2.1` |

### 编译方式（LGPL 合规要点）

本项目以 **共享库**（`--enable-shared --disable-static`）方式使用 FFmpeg，
**未**启用任何 GPL 或 nonfree 模块：

```
CONFIG_GPL      = 0
CONFIG_NONFREE  = 0
CONFIG_VERSION3 = 0
```

因此适用的是 **LGPL-2.1-or-later**，而非 GPL。
完整的音频专用裁剪 `configure` 参数见
[`源码与成品/源码/native/src/cfg.sh`](源码与成品/源码/native/src/cfg.sh)
与 [`源码与成品/源码/native/src/rebuild.sh`](源码与成品/源码/native/src/rebuild.sh)。

### 关于"禁止修改"条款的除外

FFmpeg 以独立 DLL 形式随附（并非静态链接进我们的托管代码），
用户可以自行用自己编译的同名 DLL 替换它们。
**`LICENSE` 第 2 条的限制不适用于这些 FFmpeg 组件**——
行使 FFmpeg 自身 LGPL 权利（包括为调试目的反向工程、替换、重新编译）不受本仓库许可约束。

### 获取 FFmpeg 源码

FFmpeg 源码可从上游获取：<https://ffmpeg.org/download.html>
（本项目使用的精确版本为 **n9.0.1**）。

---

## 2. mingw-w64 运行库

| 项目 | 说明 |
|---|---|
| 组件 | `libwinpthread-1.dll` |
| 位置 | `源码与成品/源码/native/`（同样被内嵌进 `EXPAS.NP.dll`） |
| 版权 | Copyright (c) 2011 mingw-w64 project |
| 授权 | mingw-w64 runtime 许可（Zope Public License 风格 / 宽松许可） |
| 上游 | <https://www.mingw-w64.org/> |

该库是 FFmpeg 共享库的运行时依赖（`avutil` 依赖它），因此一并随附。

---

## 3. 仅构建期依赖（未随附、未内嵌）

以下组件只在**编译或运行环境**中需要，本仓库**不分发**它们的任何二进制：

| 组件 | 用途 | 授权 |
|---|---|---|
| .NET SDK / Roslyn (`csc.dll`) | 编译 C# 插件 | MIT |
| MSYS2 + mingw-w64 gcc / nasm / make | 编译 FFmpeg 与原生包装层 | GPL / 宽松许可（仅作为工具使用） |
| LabAPI | 插件宿主框架（运行时由服务器提供） | 见其上游 |
| SCP:SL 专用服务器程序集 | 编译期引用（`Assembly-CSharp`、`UnityEngine.*` 等） | 归 Northwood Studios 所有，仅作参考引用 |

> 说明：编译 `EXPAS.NP.dll` 需要引用游戏本体的托管程序集（`SCPSL_Data/Managed`），
> 这些文件**不在本仓库中**，需自行从你安装的 SCP:SL 专用服务器获取。

---

## 4. 未使用的第三方代码声明

本项目的 C# 代码与原生包装层**未**包含、复制或改编任何其他 SCP:SL 插件作者的代码。
代码注释中对"官方文档 / 官方示例 / 设计规格文档"的引用仅为**按文档实现**，
不涉及任何第三方插件源码。

---

*本文件最后更新：2026-09-29*
