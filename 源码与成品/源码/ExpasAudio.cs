// ============================================================================
//   EXPAS —— 音频模块(NP 层)
//   语音播放: **只用隐藏音箱**(v2.3 起不再生成任何假人)。
//     在远处高空 (0, 990, 0) 建一个非空间化 SpeakerToy → 全服可闻;
//     不生成 Dummy/bot, 玩家列表零新增(避免列表里多出一条 "Dummy")。
//   音频调用声明: 配置 audio_calls 里写清"有哪些音频调用 / 打开后什么效果 / 通过哪个 API 播放会怎样"。
//   解码: 内嵌自写 WAV(PCM16 单/双声道) → f32 mono 48kHz(SpeakerToy 口径), 不依赖 ffmpeg/exe。
//   v2.2 第一名音乐盒改为 **JS 文件驱动**: mvp_music\ 下放 <Steam64>.js(声明 owner + audio),
//        音频也放该目录; 回合结束用伤害第一名的 Steam64 检索 JS → 按所填路径播放。
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
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        public const int AudioPlanOff = 0;
        public const int AudioPlanSpeaker = 1;  // 隐藏音箱(唯一方案: 不生成假人)

        private SpeakerToy _audioSpeaker;
        private string _audioCurrent = string.Empty;

        /// <summary>当前正在播放的音频文件名(空 = 未播放)。</summary>
        public string AudioCurrent => _audioCurrent;

        /// <summary>音频目录(与 config.yml 同级, configs\&lt;端口&gt;\EXPAS\audio\)。</summary>
        public string AudioDir => Path.Combine(RootDir, "audio");

        /// <summary>第一名音乐盒专属目录: configs\&lt;端口&gt;\EXPAS\mvp_music\(与 audio\ 独立)。</summary>
        public string MvpMusicDir => Path.Combine(RootDir, "mvp_music");

        // ==================== v2.5 多格式解码(内嵌自建原生 DLL, 启动释放) ====================
        //   · WAV → 用内置自写解码器(快, 不开进程)
        //   · 其它格式(OGG/MP3/AAC/M4A/MP4/FLAC/WMA/OPUS…) → 自建原生 DLL 解码器
        //     (EXPAS.AudioCodec.dll + avformat/avcodec/avutil/swresample, 全部从
        //      ffmpeg n9.0.1 源码用 mingw-w64 编译而来, 见 ExpasNative.cs)
        //   · 原生解码器内嵌在 EXPAS.NP.dll 里, 首次用到时释放到 EXPAS\bin\native\
        //   · ffmpeg.exe 仅作**可选后备**(外部自带), 不再内嵌任何 exe
        //   · 解码在后台线程执行, 解完由主线程(HudLoop)取走播放 —— 避免卡住服务器主线程
        private const string FfmpegResName = "EXPAS.ffmpeg.exe";
        private string _ffmpegPath;
        private readonly object _pendingLock = new object();
        private readonly Queue<KeyValuePair<string, float[]>> _pendingPlays = new Queue<KeyValuePair<string, float[]>>();
        private int _decoding;   // 1 = 有后台解码进行中(同一时间只解一个)

        /// <summary>释放目录: EXPAS\bin\(放释放出来的 ffmpeg.exe)。</summary>
        public string BinDir => Path.Combine(RootDir, "bin");

        /// <summary>支持的音频扩展名。</summary>
        public static readonly string[] AudioExtensions =
        {
            ".wav", ".ogg", ".mp3", ".m4a", ".aac", ".mp4", ".flac",
            ".wma", ".opus", ".aiff", ".aif", ".wv", ".ape", ".mka", ".webm"
        };

        /// <summary>列出可用音频文件(支持的全部扩展名)。</summary>
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
        /// 定位/释放 ffmpeg。查找顺序:
        ///   ① 配置 FfmpegPath ② EXPAS\bin\ffmpeg.exe ③ EXPAS\ffmpeg.exe
        ///   ④ 插件目录 ffmpeg.exe ⑤ BGMCC 释放的 %TEMP%\bgmcc_ffmpeg\ffmpeg.exe ⑥ PATH
        ///   都没有 → 从 DLL 内嵌资源释放到 EXPAS\bin\。
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

                // 从内嵌资源释放
                using (Stream s = typeof(ExpasPlugin).Assembly.GetManifestResourceStream(FfmpegResName))
                {
                    if (s == null) return null;
                    Directory.CreateDirectory(BinDir);
                    using (FileStream f = File.Create(inBin)) s.CopyTo(f);
                    Logger.Info($"[EXPAS] 已释放内嵌 ffmpeg → {inBin} ({new FileInfo(inBin).Length / 1024 / 1024} MB)");
                    return _ffmpegPath = inBin;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EXPAS] 释放/定位 ffmpeg 失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>ffmpeg 是否可用(决定能否解码 WAV 以外的格式)。</summary>
        public bool FfmpegAvailable => EnsureFfmpeg() != null;

        /// <summary>
        /// 任意格式解码(后台线程调用):
        ///   ① 首选 **自建原生 DLL 解码器**(EXPAS.AudioCodec.dll + ffmpeg 共享库, 无进程开销)
        ///   ② 失败才回落到 ffmpeg.exe(兼容外部 ffmpeg)
        /// </summary>
        private float[] DecodeAnyFormat(string path)
        {
            if (EnsureNative())
            {
                float[] s; string err;
                if (TryNativeDecode(path, out s, out err)) return s;
                Logger.Info($"[EXPAS] 原生解码未成功({err}), 尝试 ffmpeg.exe 后备: {Path.GetFileName(path)}");
            }
            return DecodeWithFfmpeg(path);
        }

        /// <summary>整体解码能力描述(命令/日志用)。</summary>
        public string DecoderStatusText()
        {
            if (NativeCodecAvailable) return "原生 DLL 解码器(自建, " + (NativeCodecVersion ?? "?") + ")";
            if (FfmpegAvailable) return "ffmpeg.exe(后备)";
            return "仅 WAV(无 ffmpeg)";
        }

        /// <summary>调 ffmpeg 把任意格式解成 f32 单声道 48kHz(写临时文件再读, 避免管道死锁)。失败返回 null。</summary>
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
                // 注: 不加 -hide_banner(ffmpeg 2.1 以下不认这个参数)
                psi.Arguments = $"-loglevel error -y -i \"{path}\" -vn -f f32le -ac 1 -ar 48000 \"{tmp}\"";

                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string err = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(Math.Max(5, Config.AudioDecodeTimeoutSec) * 1000))
                    {
                        try { p.Kill(); } catch { }
                        Logger.Warn($"[EXPAS] ffmpeg 解码超时: {Path.GetFileName(path)}");
                        return null;
                    }
                    if (p.ExitCode != 0)
                    {
                        Logger.Warn($"[EXPAS] ffmpeg 解码失败({p.ExitCode}): {Path.GetFileName(path)} {err.Trim()}");
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
                Logger.Warn($"[EXPAS] ffmpeg 调用异常: {ex.Message}");
                return null;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>播放音频(按配置方案)。file = audio\ 下的文件名。</summary>
        public bool PlayAudio(string file, Player requester = null)
        {
            if (string.IsNullOrWhiteSpace(file)) return false;
            return PlayAudioPath(Path.Combine(AudioDir, file));
        }

        /// <summary>
        /// 播放音频(隐藏音箱)。
        /// · WAV → 内置解码, 立即播放;
        /// · OGG/MP3/AAC/M4A/MP4/FLAC/WMA… → 后台线程调 ffmpeg 解码, 解完由主线程(HudLoop 的 PumpPendingAudio)播放。
        /// 返回值: true = 已受理(可能仍在后台解码)。
        /// </summary>
        public bool PlayAudioPath(string path)
        {
            try
            {
                if (Config.AudioPlaybackPlan == AudioPlanOff) return false;
                if (!File.Exists(path))
                {
                    Logger.Warn($"[EXPAS] 音频不存在: {path}");
                    return false;
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(AudioExtensions, ext) < 0)
                {
                    Logger.Warn($"[EXPAS] 不支持的音频格式: {ext}(支持: {string.Join(" ", AudioExtensions)})");
                    return false;
                }

                // WAV: 内置解码(快) → 直接播
                if (ext == ".wav")
                {
                    float[] wav = DecodeWavToSpeaker(File.ReadAllBytes(path));
                    if (wav != null && wav.Length > 0)
                    {
                        StopAudio();
                        return PlayViaSpeakerAt(Path.GetFileName(path), wav, new Vector3(0, 990, 0));
                    }
                    Logger.Info($"[EXPAS] WAV 内置解码不适用(非 PCM16), 转用 ffmpeg: {Path.GetFileName(path)}");
                }

                // 其它格式: 后台解码, 避免卡主线程
                if (Interlocked.CompareExchange(ref _decoding, 1, 0) != 0)
                {
                    Logger.Warn($"[EXPAS] 已有解码任务进行中, 忽略本次: {Path.GetFileName(path)}");
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
                            Logger.Warn($"[EXPAS] 解码失败: {fileName}");
                            return;
                        }
                        lock (_pendingLock) _pendingPlays.Enqueue(new KeyValuePair<string, float[]>(fileName, s));
                    }
                    catch (Exception ex) { Logger.Warn($"[EXPAS] 后台解码异常: {ex.Message}"); }
                    finally { Interlocked.Exchange(ref _decoding, 0); }
                });
                Logger.Info($"[EXPAS] 已提交后台解码: {fileName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EXPAS] 播放失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>主线程调用(HudLoop 每周期): 把后台解完的音频真正播出去。</summary>
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
            catch (Exception ex) { Logger.Warn($"[EXPAS] 播出待播音频失败: {ex.Message}"); }
        }

        /// <summary>
        /// 一条"第一名音乐盒"条目 —— 来自 mvp_music\ 下的 **.js 文件**(与 UI 布局的 layouts\*.js 完全分开)。
        /// </summary>
        public sealed class MvpMusicEntry
        {
            public string Owner = string.Empty;    // 归一化 Steam64(UserId 口径, 如 7656…@steam)
            public string File = string.Empty;     // JS 里填的音频(文件名或绝对路径)
            public string Path = string.Empty;     // 解析后的实际路径
            public string Source = string.Empty;   // 来源 js 文件
            public bool Exists => Path.Length > 0 && System.IO.File.Exists(Path);
        }

        private readonly Dictionary<string, MvpMusicEntry> _mvpMusicIndex =
            new Dictionary<string, MvpMusicEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前音乐盒索引(Steam64 → 条目)副本, 供面板/命令显示。</summary>
        public IReadOnlyDictionary<string, MvpMusicEntry> MvpMusicIndex => _mvpMusicIndex;

        /// <summary>
        /// 扫描 mvp_music\ 下的 *.js(兼容 *.txt) 建索引: Steam64 → 音频路径。
        /// 文件写法(与布局 JS 同一套"只读引号串"的解析规则, 但**放在 mvp_music\ 目录**, 与 layouts\ 分开):
        ///     // owner = 76561198000000000
        ///     const owner = "76561198000000000";
        ///     const audio = "示例开场曲.wav";     // 也认 music / file / path / wav
        /// 音频默认相对本目录, 也可写绝对路径。每次调用重扫 → 改完即时生效(回合结束频率很低)。
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
                        // 音乐盒 JS/TXT 结构极简(owner + audio), 这里**直接解析原始行**,
                        // 不复用布局那套 JS 摊平 —— 避免 keys 白名单(name/owner)把 audio 丢掉。
                        string[] lines = File.ReadAllLines(f, new UTF8Encoding(false));

                        string owner = Find17DigitRun(Path.GetFileName(f));   // 文件名 17 位前缀优先
                        string audio = null;
                        foreach (string raw in lines)
                        {
                            string t = (raw ?? string.Empty).Trim();
                            if (t.Length == 0) continue;

                            // owner: 兼容 "// owner = …" / "# owner = …" / const owner = "…"
                            string o = TryExtractOwner(t);
                            if (o != null) { if (owner == null) owner = o; continue; }

                            // audio: 键可为 audio / music / file / path / wav(允许 const/let/var 前缀与引号)
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
                            Logger.Warn($"[EXPAS] 音乐盒 JS 不可用(缺 owner 或 缺 audio=): {f}");
                            continue;
                        }

                        var e = new MvpMusicEntry
                        {
                            Owner = CanonUserId(owner),
                            File = audio,
                            Source = f
                        };
                        e.Path = ResolveAudioPath(audio);
                        _mvpMusicIndex[e.Owner] = e;      // 同一 Steam64 后写的覆盖
                        Logger.Info($"[EXPAS] 音乐盒条目: {e.Owner} → {audio}" +
                                    (e.Exists ? string.Empty : "  [音频缺失]") + $" ← {Path.GetFileName(f)}");
                    }
                    catch (Exception ex) { Logger.Warn($"[EXPAS] 解析音乐盒 JS 失败 {f}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 扫描音乐盒目录失败: {ex.Message}"); }
            return _mvpMusicIndex;
        }

        /// <summary>去掉键名前缀 const/let/var(音乐盒 JS 解析用: node[key] → 归一化键名)。</summary>
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

        /// <summary>音频路径解析: 绝对路径原样用; 否则先找 mvp_music\, 再找 audio\。</summary>
        private string ResolveAudioPath(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return string.Empty;
            if (Path.IsPathRooted(file) && File.Exists(file)) return file;
            string p = Path.Combine(MvpMusicDir, file);
            if (File.Exists(p)) return p;
            string q = Path.Combine(AudioDir, file);
            if (File.Exists(q)) return q;
            return Path.IsPathRooted(file) ? file : p;   // 都不存在 → 返回候选路径(日志里能看出期望位置)
        }

        /// <summary>
        /// 第一名音乐盒: 回合结束由伤害第一名的 Steam64 触发。
        /// ① 主: 检索 mvp_music\ 下的 JS 文件, 匹配 Steam64 → 按其中填写的路径播放音频
        /// ② 兼容: config.yml 的 mvp_music_players(可选, 一般不用)
        /// </summary>
        public bool PlayMvpMusicFor(string userId)
        {
            if (!Config.MvpMusicEnabled) return false;
            string canon = CanonUserId(userId);
            if (canon == null) return false;

            // ① mvp_music\ 下的 JS(每次重扫, 改完即生效)
            LoadMvpMusicIndex();
            if (_mvpMusicIndex.TryGetValue(canon, out MvpMusicEntry e))
            {
                if (!e.Exists)
                {
                    Logger.Warn($"[EXPAS] 第一名音乐盒音频缺失: {e.File} (来自 {Path.GetFileName(e.Source)}; 期望 {e.Path})");
                    return false;
                }
                Logger.Info($"[EXPAS] 第一名音乐盒触发: {canon} → {Path.GetFileName(e.Path)} (来自 {Path.GetFileName(e.Source)})");
                return PlayAudioPath(e.Path);
            }

            // ② 兼容配置表
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
                    if (!File.Exists(p)) { Logger.Warn($"[EXPAS] 第一名音乐盒音频缺失(config): {filePart}"); return false; }
                    Logger.Info($"[EXPAS] 第一名音乐盒触发(config): {canon} → {Path.GetFileName(p)}");
                    return PlayAudioPath(p);
                }
            }
            return false;
        }

        /// <summary>在指定位置建非空间化 SpeakerToy 播放(隐藏音箱: 只发声音, 不生成任何 bot)。</summary>
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
            Logger.Info($"[EXPAS] ▶ 音频 {file} ({(double)samples.Length / 48000.0:F1}s) 隐藏音箱播放");
            return true;
        }

        /// <summary>停止当前播放(销毁音箱; 不涉及任何假人)。</summary>
        public void StopAudio()
        {
            try { if (_audioSpeaker != null) { _audioSpeaker.Stop(); _audioSpeaker.Destroy(); } } catch { }
            _audioSpeaker = null;
            if (!string.IsNullOrEmpty(_audioCurrent)) { _audioCurrent = string.Empty; }
        }

        // ---------------- 内嵌自写 WAV 解码 → f32 mono 48k(SpeakerToy 口径) ----------------
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
                if (fmt != 1 || ch <= 0 || sr <= 0 || bps != 16) return null;   // 仅 PCM16

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

                // 混单声道 + 重采样到 48k
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

        /// <summary>音频调用声明汇总(配置 audio_calls 里写清效果与 API 说明)。</summary>
        public string AudioCallsSummary()
        {
            if (Config.AudioCalls == null || Config.AudioCalls.Count == 0) return "(未配置 audio_calls)";
            return string.Join("\n", Config.AudioCalls);
        }
    }
}