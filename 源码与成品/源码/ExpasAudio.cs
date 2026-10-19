// ============================================================================
//   EXPAS —— 音频模块(NP 层) / EXPAS — audio module (NP layer)
//   语音播放: **只用隐藏音箱**(v2.3 起不再生成任何假人)。 / Voice playback: **hidden speaker only** (since v2.3 no dummies are spawned).
//     在远处高空 (0, 990, 0) 建一个非空间化 SpeakerToy → 全服可闻; / Creates a non-spatialized SpeakerToy high up at (0, 990, 0) → audible server-wide;
//     不生成 Dummy/bot, 玩家列表零新增(避免列表里多出一条 "Dummy")。 / no Dummy/bot is created and nothing is added to the player list (avoids an extra "Dummy" entry).
//   音频调用声明: 配置 audio_calls 里写清"有哪些音频调用 / 打开后什么效果 / 通过哪个 API 播放会怎样"。 / Audio call declarations: audio_calls in the config states "which audio calls exist / what effect they have when opened / what happens when played via which API".
//   解码: 内嵌自写 WAV(PCM16 单/双声道) → f32 mono 48kHz(SpeakerToy 口径), 不依赖 ffmpeg/exe。 / Decoding: built-in custom WAV (PCM16 mono/stereo) → f32 mono 48kHz (SpeakerToy format), with no dependency on ffmpeg/exe.
//   v2.2 第一名音乐盒改为 **JS 文件驱动**: mvp_music\ 下放 <Steam64>.js(声明 owner + audio), / v2.2 MVP music is now **JS-file driven**: place <Steam64>.js under mvp_music\ (declaring owner + audio),
//        音频也放该目录; 回合结束用伤害第一名的 Steam64 检索 JS → 按所填路径播放。 / put the audio in that directory too; at round end use the top-damage player's Steam64 to look up the JS → play the path written in it.
// ============================================================================
using LabApi.Features.Wrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using L = Expas.ExpasLang;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        public const int AudioPlanOff = 0;
        public const int AudioPlanSpeaker = 1;  // 隐藏音箱(唯一方案: 不生成假人) / Hidden speaker (the only option: creates no dummy)

        private SpeakerToy _audioSpeaker;
        private string _audioCurrent = string.Empty;

        /// <summary>当前正在播放的音频文件名(空 = 未播放)。 / File name of the currently playing audio (empty = not playing).</summary>
        public string AudioCurrent => _audioCurrent;

        /// <summary>音频目录(与 config.yml 同级, configs\&lt;端口&gt;\EXPAS\audio\)。 / Audio directory (alongside config.yml, configs\&lt;port&gt;\EXPAS\audio\).</summary>
        public string AudioDir => Path.Combine(RootDir, "audio");

        /// <summary>第一名音乐盒专属目录: configs\&lt;端口&gt;\EXPAS\mvp_music\(与 audio\ 独立)。 / MVP music directory: configs\&lt;port&gt;\EXPAS\mvp_music\ (separate from audio\).</summary>
        public string MvpMusicDir => Path.Combine(RootDir, "mvp_music");

        // ==================== v2.5 多格式解码(内嵌自建原生 DLL, 启动释放) ==================== / v2.5 multi-format decoding (embedded self-built native DLL, extracted at startup)
        //   · WAV → 用内置自写解码器(快, 不开进程) / · WAV → use the built-in custom decoder (fast, no process)
        //   · 其它格式(OGG/MP3/AAC/M4A/MP4/FLAC/WMA/OPUS…) → 自建原生 DLL 解码器 / · Other formats (OGG/MP3/AAC/M4A/MP4/FLAC/WMA/OPUS…) → self-built native DLL decoder
        //     (EXPAS.AudioCodec.dll + avformat/avcodec/avutil/swresample, 全部从 / (EXPAS.AudioCodec.dll + avformat/avcodec/avutil/swresample, all built from
        //      ffmpeg n9.0.1 源码用 mingw-w64 编译而来, 见 ExpasNative.cs) / ffmpeg n9.0.1 source with mingw-w64; see ExpasNative.cs)
        //   · 原生解码器内嵌在 EXPAS.NP.dll 里, 首次用到时释放到 EXPAS\bin\native\ / · The native decoder is embedded in EXPAS.NP.dll and extracted to EXPAS\bin\native\ on first use
        //   · ffmpeg.exe 仅作**可选后备**(外部自带), 不再内嵌任何 exe / · ffmpeg.exe is only an **optional fallback** (provided externally); no exe is embedded anymore
        //   · 解码在后台线程执行, 解完由主线程(HudLoop)取走播放 —— 避免卡住服务器主线程 / · Decoding runs on a background thread; the main thread (HudLoop) picks it up for playback — avoids blocking the server main thread
        private const string FfmpegResName = "EXPAS.ffmpeg.exe";
        private string _ffmpegPath;
        private readonly object _pendingLock = new object();
        private readonly Queue<KeyValuePair<string, float[]>> _pendingPlays = new Queue<KeyValuePair<string, float[]>>();
        private int _decoding;   // 1 = 有后台解码进行中(同一时间只解一个) / 1 = a background decode is in progress (only one at a time)

        /// <summary>释放目录: EXPAS\bin\(放释放出来的 ffmpeg.exe)。 / Extraction directory: EXPAS\bin\ (for the extracted ffmpeg.exe).</summary>
        public string BinDir => Path.Combine(RootDir, "bin");

        /// <summary>支持的音频扩展名。 / Supported audio extensions.</summary>
        public static readonly string[] AudioExtensions =
        {
            ".wav", ".ogg", ".mp3", ".m4a", ".aac", ".mp4", ".flac",
            ".wma", ".opus", ".aiff", ".aif", ".wv", ".ape", ".mka", ".webm"
        };

        /// <summary>列出可用音频文件(支持的全部扩展名)。 / List available audio files (all supported extensions).</summary>
        public List<string> ListAudioFiles()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(AudioDir)) return list;
                foreach (string f in Directory.GetFiles(AudioDir))
                    if (Array.IndexOf(AudioExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                        list.Add(Path.GetFileName(f));
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 定位/释放 ffmpeg。查找顺序: / Locate/extract ffmpeg. Search order:
        ///   ① 配置 FfmpegPath ② EXPAS\bin\ffmpeg.exe ③ EXPAS\ffmpeg.exe / 1) config FfmpegPath 2) EXPAS\bin\ffmpeg.exe 3) EXPAS\ffmpeg.exe
        ///   ④ 插件目录 ffmpeg.exe ⑤ BGMCC 释放的 %TEMP%\bgmcc_ffmpeg\ffmpeg.exe ⑥ PATH / 4) plugin directory ffmpeg.exe 5) %TEMP%\bgmcc_ffmpeg\ffmpeg.exe extracted by BGMCC 6) PATH
        ///   都没有 → 从 DLL 内嵌资源释放到 EXPAS\bin\。 / if none exist → extract from the DLL's embedded resources into EXPAS\bin\.
        /// </summary>
        public string EnsureFfmpeg()
        {
            try
            {
                if (!string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath)) return _ffmpegPath;

                if (!string.IsNullOrWhiteSpace(Config.FfmpegPath) && File.Exists(Config.FfmpegPath))
                    return _ffmpegPath = Config.FfmpegPath;

                string inBin = Path.Combine(BinDir, "ffmpeg.exe");
                if (File.Exists(inBin)) return _ffmpegPath = inBin;

                string inCfg = Path.Combine(RootDir, "ffmpeg.exe");
                if (File.Exists(inCfg)) return _ffmpegPath = inCfg;

                try
                {
                    string beside = Path.Combine(Path.GetDirectoryName(typeof(ExpasPlugin).Assembly.Location) ?? string.Empty, "ffmpeg.exe");
                    if (File.Exists(beside)) return _ffmpegPath = beside;
                }
                catch { }

                try
                {
                    string bgmcc = Path.Combine(Path.GetTempPath(), "bgmcc_ffmpeg", "ffmpeg.exe");
                    if (File.Exists(bgmcc)) return _ffmpegPath = bgmcc;
                }
                catch { }

                // PATH
                try
                {
                    string pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    foreach (string dir in pathVar.Split(';'))
                    {
                        if (string.IsNullOrWhiteSpace(dir)) continue;
                        string cand = Path.Combine(dir.Trim(), "ffmpeg.exe");
                        if (File.Exists(cand)) return _ffmpegPath = cand;
                    }
                }
                catch { }

                // 从内嵌资源释放 / Extract from embedded resources
                using (Stream s = typeof(ExpasPlugin).Assembly.GetManifestResourceStream(FfmpegResName))
                {
                    if (s == null) return null;
                    Directory.CreateDirectory(BinDir);
                    using (FileStream f = File.Create(inBin)) s.CopyTo(f);
                    Logger.Info(L.T($"[EXPAS] 已释放内嵌 ffmpeg → {inBin} ({new FileInfo(inBin).Length / 1024 / 1024} MB)", $"[EXPAS] Extracted embedded ffmpeg → {inBin} ({new FileInfo(inBin).Length / 1024 / 1024} MB)"));
                    return _ffmpegPath = inBin;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(L.T($"[EXPAS] 释放/定位 ffmpeg 失败: {ex.Message}", $"[EXPAS] Failed to extract/locate ffmpeg: {ex.Message}"));
                return null;
            }
        }

        /// <summary>ffmpeg 是否可用(决定能否解码 WAV 以外的格式)。 / Whether ffmpeg is available (determines whether non-WAV formats can be decoded).</summary>
        public bool FfmpegAvailable => EnsureFfmpeg() != null;

        /// <summary>
        /// 任意格式解码(后台线程调用): / Decode any format (called from a background thread):
        ///   ① 首选 **自建原生 DLL 解码器**(EXPAS.AudioCodec.dll + ffmpeg 共享库, 无进程开销) / 1) Prefer the **self-built native DLL decoder** (EXPAS.AudioCodec.dll + ffmpeg shared libraries, no process overhead)
        ///   ② 失败才回落到 ffmpeg.exe(兼容外部 ffmpeg) / 2) fall back to ffmpeg.exe only on failure (compatible with an external ffmpeg)
        /// </summary>
        private float[] DecodeAnyFormat(string path)
        {
            if (EnsureNative())
            {
                float[] s; string err;
                if (TryNativeDecode(path, out s, out err)) return s;
                Logger.Info(L.T($"[EXPAS] 原生解码未成功({err}), 尝试 ffmpeg.exe 后备: {Path.GetFileName(path)}", $"[EXPAS] Native decoding failed ({err}), trying ffmpeg.exe fallback: {Path.GetFileName(path)}"));
            }
            return DecodeWithFfmpeg(path);
        }

        /// <summary>整体解码能力描述(命令/日志用)。 / Overall decoding capability description (for commands/logs).</summary>
        public string DecoderStatusText()
        {
            if (NativeCodecAvailable) return L.T("原生 DLL 解码器(自建, " + (NativeCodecVersion ?? "?") + ")",
                                                 "Native DLL Decoder (built-in, " + (NativeCodecVersion ?? "?") + ")");
            if (FfmpegAvailable) return L.T("ffmpeg.exe(后备)", "ffmpeg.exe (fallback)");
            return L.T("仅 WAV(无 ffmpeg)", "WAV only (no ffmpeg)");
        }

        /// <summary>调 ffmpeg 把任意格式解成 f32 单声道 48kHz(写临时文件再读, 避免管道死锁)。失败返回 null。 / Use ffmpeg to decode any format into f32 mono 48kHz (write a temp file then read it, avoiding pipe deadlocks). Returns null on failure.</summary>
        private float[] DecodeWithFfmpeg(string path)
        {
            string ff = EnsureFfmpeg();
            if (ff == null) return null;
            string tmp = Path.Combine(Path.GetTempPath(), "expas_" + Guid.NewGuid().ToString("N") + ".pcm");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ff,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false
                };
                // 注: 不加 -hide_banner(ffmpeg 2.1 以下不认这个参数) / Note: do not add -hide_banner (ffmpeg below 2.1 does not recognize this option)
                psi.Arguments = $"-loglevel error -y -i \"{path}\" -vn -f f32le -ac 1 -ar 48000 \"{tmp}\"";

                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(Math.Max(5, Config.AudioDecodeTimeoutSec) * 1000))
                    {
                        try { p.Kill(); } catch { }
                        Logger.Warn(L.T($"[EXPAS] ffmpeg 解码超时: {Path.GetFileName(path)}", $"[EXPAS] ffmpeg decoding timed out: {Path.GetFileName(path)}"));
                        return null;
                    }
                    if (p.ExitCode != 0)
                    {
                        Logger.Warn(L.T($"[EXPAS] ffmpeg 解码失败({p.ExitCode}): {Path.GetFileName(path)} {err.Trim()}", $"[EXPAS] ffmpeg decoding failed ({p.ExitCode}): {Path.GetFileName(path)} {err.Trim()}"));
                        return null;
                    }
                }
                if (!File.Exists(tmp)) return null;
                byte[] raw = File.ReadAllBytes(tmp);
                if (raw.Length < 4) return null;
                var s = new float[raw.Length / 4];
                Buffer.BlockCopy(raw, 0, s, 0, s.Length * 4);
                return s;
            }
            catch (Exception ex)
            {
                Logger.Warn(L.T($"[EXPAS] ffmpeg 调用异常: {ex.Message}", $"[EXPAS] ffmpeg call exception: {ex.Message}"));
                return null;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>播放音频(按配置方案)。file = audio\ 下的文件名。 / Play audio (per the configured plan). file = file name under audio\.</summary>
        public bool PlayAudio(string file, Player requester = null)
        {
            if (string.IsNullOrWhiteSpace(file)) return false;
            return PlayAudioPath(Path.Combine(AudioDir, file));
        }

        /// <summary>
        /// 播放音频(隐藏音箱)。 / Play audio (hidden speaker).
        /// · WAV → 内置解码, 立即播放; / · WAV → built-in decoding, play immediately;
        /// · OGG/MP3/AAC/M4A/MP4/FLAC/WMA… → 后台线程调 ffmpeg 解码, 解完由主线程(HudLoop 的 PumpPendingAudio)播放。 / · OGG/MP3/AAC/M4A/MP4/FLAC/WMA… → decoded by ffmpeg on a background thread; the main thread (HudLoop's PumpPendingAudio) plays it afterwards.
        /// 返回值: true = 已受理(可能仍在后台解码)。 / Return: true = accepted (may still be decoding in the background).
        /// </summary>
        public bool PlayAudioPath(string path)
        {
            try
            {
                if (Config.AudioPlaybackPlan == AudioPlanOff) return false;
                if (!File.Exists(path))
                {
                    Logger.Warn(L.T($"[EXPAS] 音频不存在: {path}", $"[EXPAS] Audio not found: {path}"));
                    return false;
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(AudioExtensions, ext) < 0)
                {
                    Logger.Warn(L.T($"[EXPAS] 不支持的音频格式: {ext}(支持: {string.Join(" ", AudioExtensions)})", $"[EXPAS] Unsupported audio format: {ext} (supported: {string.Join(" ", AudioExtensions)})"));
                    return false;
                }

                // WAV: 内置解码(快) → 直接播 / WAV: built-in decoding (fast) → play directly
                if (ext == ".wav")
                {
                    float[] wav = DecodeWavToSpeaker(File.ReadAllBytes(path));
                    if (wav != null && wav.Length > 0)
                    {
                        StopAudio();
                        return PlayViaSpeakerAt(Path.GetFileName(path), wav, new Vector3(0, 990, 0));
                    }
                    Logger.Info(L.T($"[EXPAS] WAV 内置解码不适用(非 PCM16), 转用 ffmpeg: {Path.GetFileName(path)}", $"[EXPAS] Built-in WAV decoding not applicable (not PCM16), using ffmpeg: {Path.GetFileName(path)}"));
                }

                // 其它格式: 后台解码, 避免卡主线程 / Other formats: decode in the background to avoid blocking the main thread
                if (Interlocked.CompareExchange(ref _decoding, 1, 0) != 0)
                {
                    Logger.Warn(L.T($"[EXPAS] 已有解码任务进行中, 忽略本次: {Path.GetFileName(path)}", $"[EXPAS] A decoding task is already running, ignoring this request: {Path.GetFileName(path)}"));
                    return false;
                }
                string fileName = Path.GetFileName(path);
                Task.Run(() =>
                {
                    try
                    {
                        float[] s = DecodeAnyFormat(path);
                        if (s == null || s.Length == 0)
                        {
                            Logger.Warn(L.T($"[EXPAS] 解码失败: {fileName}", $"[EXPAS] Decoding failed: {fileName}"));
                            return;
                        }
                        lock (_pendingLock) _pendingPlays.Enqueue(new KeyValuePair<string, float[]>(fileName, s));
                    }
                    catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 后台解码异常: {ex.Message}", $"[EXPAS] Background decoding exception: {ex.Message}")); }
                    finally { Interlocked.Exchange(ref _decoding, 0); }
                });
                Logger.Info(L.T($"[EXPAS] 已提交后台解码: {fileName}", $"[EXPAS] Background decoding queued: {fileName}"));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(L.T($"[EXPAS] 播放失败: {ex.Message}", $"[EXPAS] Playback failed: {ex.Message}"));
                return false;
            }
        }

        /// <summary>主线程调用(HudLoop 每周期): 把后台解完的音频真正播出去。 / Called on the main thread (each HudLoop cycle): actually play audio decoded in the background.</summary>
        public void PumpPendingAudio()
        {
            try
            {
                KeyValuePair<string, float[]> item;
                bool has;
                lock (_pendingLock)
                {
                    has = _pendingPlays.Count > 0;
                    item = has ? _pendingPlays.Dequeue() : default(KeyValuePair<string, float[]>);
                }
                if (!has) return;
                StopAudio();
                PlayViaSpeakerAt(item.Key, item.Value, new Vector3(0, 990, 0));
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 播出待播音频失败: {ex.Message}", $"[EXPAS] Failed to play pending audio: {ex.Message}")); }
        }

        /// <summary>
        /// 一条"第一名音乐盒"条目 —— 来自 mvp_music\ 下的 **.js 文件**(与 UI 布局的 layouts\*.js 完全分开)。 / An "MVP music" entry — comes from **.js files** under mvp_music\ (completely separate from layouts\*.js used for UI layout).
        /// </summary>
        public sealed class MvpMusicEntry
        {
            public string Owner = string.Empty;    // 归一化 Steam64(UserId 口径, 如 7656…@steam) / Normalized Steam64 (UserId form, e.g. 7656…@steam)
            public string File = string.Empty;     // JS 里填的音频(文件名或绝对路径) / Audio specified in the JS (file name or absolute path)
            public string Path = string.Empty;     // 解析后的实际路径 / Resolved actual path
            public string Source = string.Empty;   // 来源 js 文件 / Source js file
            public bool Exists => Path.Length > 0 && System.IO.File.Exists(Path);
        }

        private readonly Dictionary<string, MvpMusicEntry> _mvpMusicIndex =
            new Dictionary<string, MvpMusicEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前音乐盒索引(Steam64 → 条目)副本, 供面板/命令显示。 / Copy of the current music-box index (Steam64 → entry), for panel/command display.</summary>
        public IReadOnlyDictionary<string, MvpMusicEntry> MvpMusicIndex => _mvpMusicIndex;

        /// <summary>
        /// 扫描 mvp_music\ 下的 *.js(兼容 *.txt) 建索引: Steam64 → 音频路径。 / Scan *.js (also *.txt) under mvp_music\ to build the index: Steam64 → audio path.
        /// 文件写法(与布局 JS 同一套"只读引号串"的解析规则, 但**放在 mvp_music\ 目录**, 与 layouts\ 分开): / File format (same "quoted-string only" parsing rules as layout JS, but **located in mvp_music\**, separate from layouts\):
        ///     // owner = 76561198000000000
        ///     const owner = "76561198000000000";
        ///     const audio = "示例开场曲.wav";     // 也认 music / file / path / wav / also accepts music / file / path / wav
        /// 音频默认相对本目录, 也可写绝对路径。每次调用重扫 → 改完即时生效(回合结束频率很低)。 / Audio defaults to this directory; absolute paths are also allowed. Rescanned on every call → changes take effect immediately (round end is infrequent).
        /// </summary>
        public Dictionary<string, MvpMusicEntry> LoadMvpMusicIndex()
        {
            _mvpMusicIndex.Clear();
            try
            {
                Directory.CreateDirectory(MvpMusicDir);
                IEnumerable<string> files = Directory.GetFiles(MvpMusicDir, "*.js")
                    .Concat(Directory.GetFiles(MvpMusicDir, "*.txt"));
                foreach (string f in files)
                {
                    try
                    {
                        // 音乐盒 JS/TXT 结构极简(owner + audio), 这里**直接解析原始行**, / The music-box JS/TXT structure is minimal (owner + audio); here we **parse raw lines directly**,
                        // 不复用布局那套 JS 摊平 —— 避免 keys 白名单(name/owner)把 audio 丢掉。 / instead of reusing the layout JS flattening — this avoids the keys whitelist (name/owner) dropping audio.
                        string[] lines = File.ReadAllLines(f, new UTF8Encoding(false));

                        string owner = Find17DigitRun(Path.GetFileName(f));   // 文件名 17 位前缀优先 / 17-digit prefix in the file name takes priority
                        string audio = null;
                        foreach (string raw in lines)
                        {
                            string t = (raw ?? string.Empty).Trim();
                            if (t.Length == 0) continue;

                            // owner: 兼容 "// owner = …" / "# owner = …" / const owner = "…" / owner: accepts "// owner = …" / "# owner = …" / const owner = "…"
                            string o = TryExtractOwner(t);
                            if (o != null) { if (owner == null) owner = o; continue; }

                            // audio: 键可为 audio / music / file / path / wav(允许 const/let/var 前缀与引号) / audio: the key may be audio / music / file / path / wav (const/let/var prefix and quotes allowed)
                            int eq = t.IndexOf('=');
                            if (eq <= 0) continue;
                            string key = t.Substring(0, eq).Trim().TrimStart('#', '/').Trim();
                            key = StripVarPrefix(key).ToLowerInvariant();
                            if (key != "audio" && key != "music" && key != "file" && key != "path" && key != "wav") continue;

                            string val = t.Substring(eq + 1).Trim().TrimEnd(';', ',').Trim();
                            audio = StripInlineComment(val).Trim().Trim('"', '\'').Trim();
                        }

                        if (owner == null || string.IsNullOrEmpty(audio))
                        {
                            Logger.Warn(L.T($"[EXPAS] 音乐盒 JS 不可用(缺 owner 或 缺 audio=): {f}", $"[EXPAS] MVP Music JS unusable (missing owner or audio=): {f}"));
                            continue;
                        }

                        var e = new MvpMusicEntry
                        {
                            Owner = CanonUserId(owner),
                            File = audio,
                            Source = f
                        };
                        e.Path = ResolveAudioPath(audio);
                        _mvpMusicIndex[e.Owner] = e;      // 同一 Steam64 后写的覆盖 / later writes for the same Steam64 override earlier ones
                        Logger.Info(L.T(
                            $"[EXPAS] 音乐盒条目: {e.Owner} → {audio}" +
                            (e.Exists ? string.Empty : "  [音频缺失]") + $" ← {Path.GetFileName(f)}",
                            $"[EXPAS] MVP Music entry: {e.Owner} → {audio}" +
                            (e.Exists ? string.Empty : "  [Audio missing]") + $" ← {Path.GetFileName(f)}"));
                    }
                    catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 解析音乐盒 JS 失败 {f}: {ex.Message}", $"[EXPAS] Failed to parse MVP Music JS {f}: {ex.Message}")); }
                }
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 扫描音乐盒目录失败: {ex.Message}", $"[EXPAS] Failed to scan MVP Music directory: {ex.Message}")); }
            return _mvpMusicIndex;
        }

        /// <summary>去掉键名前缀 const/let/var(音乐盒 JS 解析用: node[key] → 归一化键名)。 / Strip the const/let/var key prefix (for music-box JS parsing: node[key] → normalized key name).</summary>
        private static string StripVarPrefix(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            string k = key.Trim();
            foreach (string p in new[] { "const", "let", "var" })
            {
                if (k.Length > p.Length && k.StartsWith(p, StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(k[p.Length]))
                    return k.Substring(p.Length).Trim();
            }
            return k;
        }

        /// <summary>音频路径解析: 绝对路径原样用; 否则先找 mvp_music\, 再找 audio\。 / Audio path resolution: absolute paths are used as-is; otherwise look in mvp_music\ first, then audio\.</summary>
        private string ResolveAudioPath(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return string.Empty;
            if (Path.IsPathRooted(file) && File.Exists(file)) return file;
            string p = Path.Combine(MvpMusicDir, file);
            if (File.Exists(p)) return p;
            string q = Path.Combine(AudioDir, file);
            if (File.Exists(q)) return q;
            return Path.IsPathRooted(file) ? file : p;   // 都不存在 → 返回候选路径(日志里能看出期望位置) / none exist → return the candidate path (so logs show the expected location)
        }

        /// <summary>
        /// 第一名音乐盒: 回合结束由伤害第一名的 Steam64 触发。 / MVP music: triggered at round end by the top-damage player's Steam64.
        /// ① 主: 检索 mvp_music\ 下的 JS 文件, 匹配 Steam64 → 按其中填写的路径播放音频 / 1) Primary: search JS files under mvp_music\ for the matching Steam64 → play the audio at the path written there
        /// ② 兼容: config.yml 的 mvp_music_players(可选, 一般不用) / 2) Compatibility: mvp_music_players in config.yml (optional, generally unnecessary)
        /// </summary>
        public bool PlayMvpMusicFor(string userId)
        {
            if (!Config.MvpMusicEnabled) return false;
            string canon = CanonUserId(userId);
            if (canon == null) return false;

            // ① mvp_music\ 下的 JS(每次重扫, 改完即生效) / 1) JS under mvp_music\ (rescanned each time; changes take effect immediately)
            LoadMvpMusicIndex();
            if (_mvpMusicIndex.TryGetValue(canon, out MvpMusicEntry e))
            {
                if (!e.Exists)
                {
                    Logger.Warn(L.T($"[EXPAS] 第一名音乐盒音频缺失: {e.File} (来自 {Path.GetFileName(e.Source)}; 期望 {e.Path})", $"[EXPAS] MVP Music audio missing: {e.File} (from {Path.GetFileName(e.Source)}; expected {e.Path})"));
                    return false;
                }
                Logger.Info(L.T($"[EXPAS] 第一名音乐盒触发: {canon} → {Path.GetFileName(e.Path)} (来自 {Path.GetFileName(e.Source)})", $"[EXPAS] MVP Music triggered: {canon} → {Path.GetFileName(e.Path)} (from {Path.GetFileName(e.Source)})"));
                return PlayAudioPath(e.Path);
            }

            // ② 兼容配置表 / 2) Compatibility config table
            if (Config.MvpMusicPlayers != null)
            {
                foreach (string entry in Config.MvpMusicPlayers)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    int bar = entry.IndexOf('|');
                    string idPart = (bar > 0 ? entry.Substring(0, bar) : entry).Trim();
                    string filePart = (bar > 0 ? entry.Substring(bar + 1) : string.Empty).Trim();
                    if (filePart.Length == 0) continue;
                    if (!string.Equals(CanonUserId(idPart), canon, StringComparison.OrdinalIgnoreCase)) continue;

                    string p = ResolveAudioPath(filePart);
                    if (!File.Exists(p)) { Logger.Warn(L.T($"[EXPAS] 第一名音乐盒音频缺失(config): {filePart}", $"[EXPAS] MVP Music audio missing (config): {filePart}")); return false; }
                    Logger.Info(L.T($"[EXPAS] 第一名音乐盒触发(config): {canon} → {Path.GetFileName(p)}", $"[EXPAS] MVP Music triggered (config): {canon} → {Path.GetFileName(p)}"));
                    return PlayAudioPath(p);
                }
            }
            return false;
        }

        /// <summary>在指定位置建非空间化 SpeakerToy 播放(隐藏音箱: 只发声音, 不生成任何 bot)。 / Create a non-spatialized SpeakerToy at the specified position for playback (hidden speaker: sound only, creates no bot).</summary>
        private bool PlayViaSpeakerAt(string file, float[] samples, Vector3 pos)
        {
            var sp = SpeakerToy.Create(pos);
            sp.IsSpatial = false;
            sp.Volume = Config.AudioVolume;
            sp.MinDistance = 0f;
            sp.MaxDistance = float.MaxValue;
            sp.Spawn();
            sp.Play(samples, queue: false, loop: Config.AudioLoop);
            _audioSpeaker = sp;
            _audioCurrent = file;
            Logger.Info(L.T($"[EXPAS] ▶ 音频 {file} ({(double)samples.Length / 48000.0:F1}s) 隐藏音箱播放", $"[EXPAS] ▶ Audio {file} ({(double)samples.Length / 48000.0:F1}s) played via Hidden Speaker"));
            return true;
        }

        /// <summary>停止当前播放(销毁音箱; 不涉及任何假人)。 / Stop current playback (destroy the speaker; no dummies involved).</summary>
        public void StopAudio()
        {
            try { if (_audioSpeaker != null) { _audioSpeaker.Stop(); _audioSpeaker.Destroy(); } } catch { }
            _audioSpeaker = null;
            if (!string.IsNullOrEmpty(_audioCurrent)) { _audioCurrent = string.Empty; }
        }

        // ---------------- 内嵌自写 WAV 解码 → f32 mono 48k(SpeakerToy 口径) ---------------- / Built-in custom WAV decoding → f32 mono 48k (SpeakerToy format)
        private static float[] DecodeWavToSpeaker(byte[] data)
        {
            if (data == null || data.Length < 44) return null;
            if (!(data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')) return null;
            try
            {
                int pos = 12; int fmtAt = -1, dataAt = -1;
                while (pos + 8 <= data.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    uint size = BitConverter.ToUInt32(data, pos + 4);
                    if (id == "fmt ") fmtAt = pos;
                    else if (id == "data") dataAt = pos;
                    long next = pos + 8L + size; if (next > data.Length) break;
                    if ((size & 1) != 0) next++;
                    pos = (int)next;
                }
                if (fmtAt < 0 || dataAt < 0) return null;

                int fp = fmtAt + 8;
                short fmt = BitConverter.ToInt16(data, fp);
                short ch = BitConverter.ToInt16(data, fp + 2);
                int sr = BitConverter.ToInt32(data, fp + 4);
                short bps = BitConverter.ToInt16(data, fp + 14);
                if (fmt != 1 || ch <= 0 || sr <= 0 || bps != 16) return null;   // 仅 PCM16 / PCM16 only

                int dp = dataAt + 8;
                long end = Math.Min(data.Length, dp + BitConverter.ToUInt32(data, dataAt + 4));
                int block = 2 * ch;
                long frames = Math.Max(0, (end - dp) / block);
                var inter = new float[frames * ch];
                for (long f = 0; f < frames; f++)
                {
                    long src = dp + f * block;
                    for (int c = 0; c < ch; c++)
                        inter[f * ch + c] = BitConverter.ToInt16(data, (int)(src + c * 2)) / 32768f;
                }

                // 混单声道 + 重采样到 48k / Mix to mono + resample to 48k
                var mono = ch == 1 ? inter : MixMono(inter, ch);
                return sr == 48000 ? mono : ResampleLinear(mono, sr, 48000);
            }
            catch { return null; }
        }

        private static float[] MixMono(float[] inter, int channels)
        {
            int n = inter.Length / channels;
            var m = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += inter[i * channels + c];
                m[i] = sum / channels;
            }
            return m;
        }

        private static float[] ResampleLinear(float[] mono, int srcRate, int dstRate)
        {
            int n = Math.Max(1, (int)((long)mono.Length * dstRate / srcRate));
            var o = new float[n];
            double step = (double)srcRate / dstRate;
            for (int i = 0; i < n; i++)
            {
                double pos = i * step;
                int i0 = (int)pos, i1 = Math.Min(i0 + 1, mono.Length - 1);
                o[i] = (float)(mono[i0] + (mono[i1] - mono[i0]) * (pos - i0));
            }
            return o;
        }

        /// <summary>音频调用声明汇总(配置 audio_calls 里写清效果与 API 说明)。 / Audio call declaration summary (audio_calls in the config describes the effects and API notes).</summary>
        public string AudioCallsSummary()
        {
            if (Config.AudioCalls == null || Config.AudioCalls.Count == 0) return L.T("(未配置 audio_calls)", "(audio_calls not configured)");
            return string.Join("\n", Config.AudioCalls);
        }
    }
}