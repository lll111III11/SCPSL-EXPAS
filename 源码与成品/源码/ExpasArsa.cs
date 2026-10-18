// ============================================================================
//   EXPAS —— arsa 子系统: 重新识别"除自身以外"的全部插件(NP 层)
//   EXPAS arsa
//     ① 重新读取文件:   重扫插件目录(PluginPaths: global / $port), 把**新出现**的 .dll
//                        载入 PluginLoader 并启用(已在册的 DLL 不重复加载, 避免重复实例)。
//     ② 重新读取配置文件: 对每个"非本插件"的插件重跑 TryLoadProperties()(properties.yml)
//                        与 LoadConfigs()(config.yml) —— 即官方 ConfigsCommand 的做法。
//   依据(LabAPI 官方源码, 路径 LabAPI-master/LabApi/Loader/)：
//     · PluginLoader.cs(L27-57: Plugins/EnabledPlugins/Config;
//        L153-235 LoadAllPlugins/LoadPlugins; L241-299 EnablePlugins/EnablePlugin)
//     · Plugin{TConfig}.cs(L24-36: LoadConfigs() 重读 config.yml 到 Config)
//     · ConfigurationLoader.cs(L235: TryLoadProperties(this Plugin) 重读 properties.yml)
//     · Features/Commands/Reload/ConfigsCommand.cs(L44-56: 逐插件 LoadConfigs() 的官方范式)
//   ★ 限制(.NET 本身): 程序集一经载入**无法卸载**(无 AssemblyLoadContext 卸载时),
//     所以"磁盘上已替换同名 DLL"不会被重新读取; arsa 负责"重读配置 + 识别新增插件"。
// ============================================================================
using LabApi.Features.Wrappers;
using LabApi.Loader;
using LabApi.Loader.Features.Paths;
using LabApi.Loader.Features.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using L = Expas.ExpasLang;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        /// <summary>
        /// EXPAS arsa: 重新识别除自身以外的全部插件(重读配置文件 + 重读文件)。
        /// 返回给命令行的回执文本。
        /// </summary>
        /// <param name="alsoRestart">true = 额外对其它插件执行 Disable→Enable(完整重跑, 慎用)。</param>
        public string ArsaReloadOthers(bool alsoRestart = false)
        {
            var sb = new StringBuilder("[EXPAS arsa] " + L.T("重新识别其它插件", "Re-detecting other Plugins"));

            try
            {
                Dictionary<Plugin, Assembly> plugins = PluginLoader.Plugins;
                if (plugins == null || plugins.Count == 0)
                    return "[EXPAS arsa] " + L.T("当前没有已加载的插件。", "No Plugins are currently loaded.");

                ExpasPlugin self = Instance;

                // ---------------- ① 重新读取文件: 重扫插件目录, 载入新增 DLL ----------------
                int loadedNew = RescanAndLoadNewPlugins(out List<string> newNames);
                if (loadedNew > 0)
                    sb.Append(L.T($"\n① 重读文件: 新载入并启用 {loadedNew} 个插件 → {string.Join(", ", newNames)}",
                                  $"\n① Reloaded files: loaded and enabled {loadedNew} new Plugins → {string.Join(", ", newNames)}"));
                else
                    sb.Append(L.T("\n① 重读文件: 插件目录无新增 DLL(已载入的不重复加载)",
                                  "\n① Reloaded files: no new DLLs in the Plugin directory (already loaded ones are not reloaded)"));

                // ---------------- ② 重新读取配置文件: properties.yml + config.yml ----------------
                // 快照 Keys 后再操作(载入新插件会改动 Plugins 字典)
                List<Plugin> others = plugins.Keys
                    .Where(p => p != null && !ReferenceEquals(p, self)
                                && !string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int okProps = 0, okCfg = 0;
                var failed = new List<string>();
                var restarted = new List<string>();

                foreach (Plugin p in others)
                {
                    try
                    {
                        // properties.yml(是否启用/不支持加载开关等)
                        try { p.TryLoadProperties(); okProps++; }
                        catch (Exception ex) { failed.Add($"{p.Name}(properties): {ex.Message}"); }

                        // config.yml(重读到 p.Config)
                        p.LoadConfigs();
                        okCfg++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{p.Name}(config): {ex.Message}");
                    }
                }

                // 可选: 完整重跑(Disable → Enable)
                if (alsoRestart)
                {
                    foreach (Plugin p in others)
                    {
                        try
                        {
                            if (PluginLoader.EnabledPlugins.Contains(p))
                            {
                                p.Disable();
                                PluginLoader.EnabledPlugins.Remove(p);
                            }
                            Logger.Info("[EXPAS arsa] " + L.T($"重新启用插件: {p.Name}", $"Re-enabling Plugin: {p.Name}"));
                            PluginLoader.EnablePlugin(p);
                            restarted.Add(p.Name);
                        }
                        catch (Exception ex)
                        {
                            failed.Add($"{p.Name}(restart): {ex.Message}");
                        }
                    }
                }

                sb.Append(L.T($"\n② 重读配置: {okCfg}/{others.Count} 个插件 config.yml 已重读, {okProps} 个 properties.yml 已重读",
                              $"\n② Reloaded Configs: config.yml reloaded for {okCfg}/{others.Count} Plugins, properties.yml reloaded for {okProps}"));
                if (alsoRestart)
                    sb.Append(L.T($"\n③ 完整重跑(Disable→Enable): {restarted.Count} 个" +
                                  (restarted.Count > 0 ? $" → {string.Join(", ", restarted)}" : string.Empty),
                                  $"\n③ Full restart (Disable→Enable): {restarted.Count}" +
                                  (restarted.Count > 0 ? $" → {string.Join(", ", restarted)}" : string.Empty)));
                if (failed.Count > 0)
                    sb.Append(L.T($"\n⚠ 失败 {failed.Count} 项: {string.Join(" | ", failed)}",
                                  $"\n⚠ Failed {failed.Count} item(s): {string.Join(" | ", failed)}"));

                sb.Append(L.T($"\n完成(本插件 EXPAS 自身已跳过)。", $"\nDone (EXPAS itself was skipped)."));

                Logger.Info(L.T($"[EXPAS arsa] 重新识别其它插件完成: 新载入 {loadedNew} 个, 重读配置 {okCfg}/{others.Count}, ",
                                $"[EXPAS arsa] Re-detection complete: {loadedNew} newly loaded, {okCfg}/{others.Count} Configs reloaded, ") +
                            L.T($"失败 {failed.Count}{(alsoRestart ? $", 重跑 {restarted.Count}" : string.Empty)}",
                                $"failed {failed.Count}{(alsoRestart ? $", restarted {restarted.Count}" : string.Empty)}"));
            }
            catch (Exception ex)
            {
                sb.Append(L.T($"\n❌ 执行失败: {ex.Message}", $"\n❌ Failed: {ex.Message}"));
                Logger.Warn("[EXPAS arsa] " + L.T($"失败: {ex}", $"failed: {ex}"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// 重扫 PluginPaths(global / $port, 支持 $port 替换) 下的 *.dll，
        /// 把尚未登记(按 FilePath 去重)的 DLL 载入并启用。返回新载入的插件数。
        /// </summary>
        private int RescanAndLoadNewPlugins(out List<string> newNames)
        {
            newNames = new List<string>();
            try
            {
                Dictionary<Plugin, Assembly> plugins = PluginLoader.Plugins;
                if (plugins == null) return 0;

                // 已在册的插件文件路径(去重: 同名 DLL 不再重复载入, 否则会产生重复插件实例)
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Plugin p in plugins.Keys)
                {
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.FilePath)) known.Add(p.FilePath);
                }

                // 解析 PluginPaths(LabApi 配置: 默认 {"global","$port"})
                List<string> patterns = null;
                try { patterns = PluginLoader.Config?.PluginPaths; } catch { }
                if (patterns == null || patterns.Count == 0)
                    patterns = new List<string> { "global", "$port" };

                var files = new List<FileInfo>();
                foreach (string pat in patterns)
                {
                    string resolved = (pat ?? string.Empty).Replace("$port", Server.Port.ToString());
                    string full = Path.Combine(PathManager.Plugins.FullName, resolved);
                    if (!Directory.Exists(full)) continue;
                    foreach (FileInfo f in new DirectoryInfo(full).GetFiles("*.dll", SearchOption.AllDirectories))
                        if (!known.Contains(f.FullName)) files.Add(f);
                }

                if (files.Count == 0) return 0;

                int before = plugins.Count;
                // 官方载入入口(含 PDB 支持 / 内嵌资源解析 / InstantiatePlugins)
                PluginLoader.LoadPlugins(files);

                // 找出这批新文件对应的插件实例, 交给官方启用流程(含 properties/版本校验)
                var fresh = plugins.Keys
                    .Where(p => p != null && files.Any(f =>
                        string.Equals(f.FullName, p.FilePath, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (fresh.Count > 0)
                {
                    PluginLoader.EnablePlugins(fresh.OrderBy(p => p.Priority));
                    foreach (Plugin p in fresh) newNames.Add(p.Name);
                }

                return plugins.Count - before;
            }
            catch (Exception ex)
            {
                Logger.Warn("[EXPAS arsa] " + L.T($"重扫插件目录失败: {ex.Message}", $"Plugin directory rescan failed: {ex.Message}"));
                return 0;
            }
        }
    }
}