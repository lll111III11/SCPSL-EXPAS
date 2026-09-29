// ============================================================================
//   EXPAS —— 原生解码互操作(NP 层, v2.5)
//   目标: 用**自建的原生 DLL 解码器**(而非 exe)解码 OGG/MP3/AAC/M4A/MP4/FLAC/WMA…
//
//   构成:
//     · EXPAS.AudioCodec.dll —— 我们自己写的 C 包装层(源码 expas_codec.c),
//       内部调用 ffmpeg 共享库, 对外只暴露 3 个极简 C 函数
//     · avformat-*.dll / avcodec-*.dll / avutil-*.dll / swresample-*.dll
//       —— 从 ffmpeg n9.0.1 源码用 mingw-w64 现场编译出来的共享库
//
//   这些 DLL 都作为**内嵌资源**打进 EXPAS.NP.dll, 首次用到时释放到 EXPAS\bin\native\,
//   再用 LoadLibrary 显式加载(不依赖 .NET 的 DllImport 探测路径, 也不污染插件目录)。
// ============================================================================
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        // ---------- 内嵌资源前缀(资源名形如 native.avcodec-63.dll.bin) ----------
        //   ★ 为什么叫 ".dll.bin" 而不是 ".dll":
        //     LabAPI 的 AssemblyUtils.ResolveEmbeddedResources() 会对**每个**插件无条件执行,
        //     凡资源名以 ".dll" / ".dll.compressed" 结尾的都会被当成"内嵌托管程序集"
        //     丢给 AssemblyName.GetAssemblyName(), 原生二进制会炸出
        //     BadImageFormatException(日志里那串 ERROR)。
        //     所以这里统一加 ".bin" 后缀, LabAPI 直接跳过, 释放时再由我们自己剥掉。
        private const string NativeResPrefix = "native.";
        private const string NativeResSuffix = ".bin";
        private const string WrapperDllName = "EXPAS.AudioCodec.dll";

        private bool _nativeTried;
        private bool _nativeOk;
        private string _nativeDir;
        private IntPtr _nativeModule = IntPtr.Zero;
        private ExpasDecodeFn _nativeDecode;
        private ExpasFreeFn _nativeFree;

        // ---------- kernel32 ----------
        // 用 LoadLibraryExW + LOAD_WITH_ALTERED_SEARCH_PATH(0x8):
        // 让 Windows 在**该 DLL 自己所在目录**里找它的依赖(avcodec-63.dll 等),
        // 否则依赖解析会从 EXE 目录/PATH 找 → 报 err=126。
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        // ---------- 我们包装层的 3 个导出函数 ----------
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ExpasDecodeFn(IntPtr pathUtf8, int targetRate, int targetChannels,
                                           out IntPtr data, out int samples, byte[] err, int errLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ExpasFreeFn(IntPtr p);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ExpasVersionFn();

        /// <summary>原生解码目录: EXPAS\bin\native\。</summary>
        public string NativeDir => Path.Combine(BinDir, "native");

        /// <summary>原生解码器是否可用(不可用时回落 ffmpeg.exe / 仅 WAV)。</summary>
        public bool NativeCodecAvailable { get { EnsureNative(); return _nativeOk; } }

        /// <summary>原生解码器版本串(不可用返回 null)。</summary>
        public string NativeCodecVersion
        {
            get
            {
                EnsureNative();
                if (!_nativeOk || _nativeModule == IntPtr.Zero) return null;
                try
                {
                    IntPtr p = GetProcAddress(_nativeModule, "expas_codec_version");
                    if (p == IntPtr.Zero) return null;
                    IntPtr s = Marshal.GetDelegateForFunctionPointer<ExpasVersionFn>(p)();
                    return s == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(s);
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// 释放内嵌原生 DLL 并加载包装层(幂等)。成功返回 true。
        /// 释放目标: EXPAS\bin\native\
        /// </summary>
        public bool EnsureNative()
        {
            if (_nativeOk) return true;
            if (_nativeTried) return false;
            _nativeTried = true;
            try
            {
                string dir = NativeDir;
                Directory.CreateDirectory(dir);

                var asm = typeof(ExpasPlugin).Assembly;
                int released = 0;
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith(NativeResPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    string file = res.Substring(NativeResPrefix.Length);
                    if (file.EndsWith(NativeResSuffix, StringComparison.OrdinalIgnoreCase))
                        file = file.Substring(0, file.Length - NativeResSuffix.Length);
                    if (file.Length == 0) continue;
                    string dst = Path.Combine(dir, file);
                    if (File.Exists(dst)) continue;
                    using (Stream s = asm.GetManifestResourceStream(res))
                    {
                        if (s == null) continue;
                        using (FileStream f = File.Create(dst)) s.CopyTo(f);
                    }
                    released++;
                }

                string wrapper = Path.Combine(dir, WrapperDllName);
                if (!File.Exists(wrapper))
                {
                    Logger.Warn($"[EXPAS] 未内嵌原生解码器({WrapperDllName}), 跳过");
                    return false;
                }

                _nativeModule = LoadLibraryExW(wrapper, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (_nativeModule == IntPtr.Zero)
                {
                    Logger.Warn($"[EXPAS] 加载原生解码器失败(err={Marshal.GetLastWin32Error()}): {wrapper}");
                    return false;
                }

                IntPtr pDecode = GetProcAddress(_nativeModule, "expas_decode");
                IntPtr pFree = GetProcAddress(_nativeModule, "expas_free");
                if (pDecode == IntPtr.Zero || pFree == IntPtr.Zero)
                {
                    Logger.Warn("[EXPAS] 原生解码器缺少导出函数(expas_decode/expas_free)");
                    return false;
                }
                _nativeDecode = Marshal.GetDelegateForFunctionPointer<ExpasDecodeFn>(pDecode);
                _nativeFree = Marshal.GetDelegateForFunctionPointer<ExpasFreeFn>(pFree);
                _nativeDir = dir;
                _nativeOk = true;
                Logger.Info($"[EXPAS] 原生解码器就绪: {NativeCodecVersion ?? "?"}  释放 {released} 个 DLL → {dir}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EXPAS] 原生解码器初始化失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 用原生解码器把任意格式解成 f32 单声道 48kHz。失败返回 false 并给出 error。
        /// 路径以 UTF-8 传给 C 层(中文路径也安全)。
        /// </summary>
        public bool TryNativeDecode(string path, out float[] samples, out string error)
        {
            samples = null; error = null;
            if (!EnsureNative() || _nativeDecode == null)
            {
                error = "原生解码器不可用";
                return false;
            }

            IntPtr pPath = IntPtr.Zero, pData = IntPtr.Zero;
            try
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(path);
                pPath = Marshal.AllocHGlobal(utf8.Length + 1);
                Marshal.Copy(utf8, 0, pPath, utf8.Length);
                Marshal.WriteByte(pPath, utf8.Length, 0);

                byte[] ebuf = new byte[512];
                int rc = _nativeDecode(pPath, 48000, 1, out pData, out int n, ebuf, ebuf.Length);
                if (rc != 0 || pData == IntPtr.Zero || n <= 0)
                {
                    int z = Array.IndexOf(ebuf, (byte)0);
                    error = Encoding.UTF8.GetString(ebuf, 0, z < 0 ? ebuf.Length : z);
                    if (string.IsNullOrEmpty(error)) error = "decode rc=" + rc;
                    if (pData != IntPtr.Zero) { try { _nativeFree(pData); } catch { } }
                    return false;
                }

                samples = new float[n];
                Marshal.Copy(pData, samples, 0, n);
                try { _nativeFree(pData); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (pPath != IntPtr.Zero) Marshal.FreeHGlobal(pPath);
            }
        }
    }
}
