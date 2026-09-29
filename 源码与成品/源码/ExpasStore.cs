// ============================================================================
// EXPAS —— 数据层 (partial)
//   一个玩家一个文件: configs\<服务器端口>\EXPAS\players\<steam64>.dat  (key=value, 便于解析)
//     (v1.4 起规范到 LabAPI 官方目录; 旧位置 configs\EXPAS\players\ 会在启动时自动复制过来, 不删旧文件)
//   另写 index.txt 汇总(用户/等级/最后在线), 供长期未上线扫描与人工查看
//   所有数字/时间都用 InvariantCulture, 避免不同区域设置把小数点解析错
// ============================================================================
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Paths;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        /// <summary>单个玩家的持久化数据(按 Steam64 记录)。</summary>
        public class PlayerData
        {
            public string UserId = string.Empty;
            public string Name = string.Empty;
            public int Level = 1;
            public float Exp = 0f;              // 当前等级内经验
            public float TotalExp = 0f;         // 累计总经验
            public float TotalDamage = 0f;      // 累计总伤害(已入账)
            public float PendingExp = 0f;       // 🔒 锁定期间叠加的经验(解锁时入账)
            public float PendingDamage = 0f;    // 🔒 锁定期间的伤害
            public int Kills = 0;               // 总击杀(= 指令字段 total_kills)
            public int Deaths = 0;
            public double PlaySeconds = 0d;     // 累计游玩时间(秒)(= 指令字段 total_play_time)
            // ---- v1.5 规格字段(都可由 EXPAS <set|add> <玩家> <字段> <值> 修改并持久化) ----
            public int PlayedDays = 0;          // 游玩天数(played_days); <=0 = 未设置, 按 FirstSeen 推算
            public double TodayPlaySeconds = 0d;// 今日游玩时间(秒)(today_play_time)
            public string TodayDate = string.Empty; // 今日游玩时间所属日期(yyyy-MM-dd, 按配置时区); 跨天自动归零
            /// <summary>仅运行期标记: 本次是从旧数据文件迁移来的(lock/longOffline 口径变了), 首次访问时回写一次。</summary>
            public bool LockMigrated = false;
            public DateTime FirstSeen = DateTime.UtcNow;
            public DateTime LastSeen = DateTime.UtcNow;
            public int LockState = LockNone;    // 0=none 1=punishment🚫 2=selfban🔒 3=inactive🕒
            public bool LongOffline = false;    // 🕒(旧字段; 与 LockState==LockInactive 双向同步, 仅为兼容旧读法)
            public int HudSize = int.MinValue;  // (旧字段, 仅兼容读取)
            // ---- HUD 个人设置(-1 = 用服务器配置; 全部按 Steam64 记住) ----
            public int SizePercent = -1;         // 字号 %
            public int ScreenPercent = -1;       // 屏幕缩放 %
            public int LineHeightPercent = -1;   // 行距 %
            public int VOffset = 0;              // 垂直微调(像素, 负=上移)
            public int HAlign = -1;              // 整块水平对齐 0左 1中 2右
            public int LevelAlign = -1;          // 等级块对齐 0左 1中 2右 3跟随
            public int HudEnabledSet = 1;        // 1=HUD开 0=HUD关
            // ---- v1.3 积分 / 最近一次击杀 ----
            public float Points = 0f;             // 累计积分
            public string LastKillName = "-";
            public float LastKillDamage = 0f;      // 造成伤害
            public float LastKillShare = 0f;       // 对该目标的伤害占比 %
            public float LastKillPoints = 0f;      // 本次获得积分
            // ---- v1.5 击杀确认(改造2): 记录"被击杀的是什么角色" ----
            public string LastKillRole = "-";      // 受害者角色(RoleTypeId 名, 例 NtfSergeant; 受害者是 dummy 也照记)
            public string LastKillRoleKind = "-";  // 该角色类型: 人形 / SCP / 人机 / 其它(旧 .dat 缺该键 = "-")
            // ---- v1.6 分辨率档位 / 整体缩放 / HUD 折叠(-1 = 跟随服务器 config.yml 里的同名项) ----
            public int ResolutionPreset = -1;      // 0=自动检测(按宽高比) 1..15=具体分辨率档(见 docs 倍率表)
            public int GlobalScalePercent = -1;    // 整体缩放 %(50..200), 在「字号×屏幕缩放」之外再乘一次
            public int HudCollapsed = -1;          // 0=展开 1=折叠(折叠时屏幕 HUD 只显示 hud_collapsed_line 一行)
            // ---- v1.7/v1.8 自定义 UI 布局(按 Steam64 记住; 布局文件 = configs\<端口>\EXPAS\layouts\<key>.txt|.js) ----
            //   v1.8 可见性以"布局文件的 owner Steam64 归属匹配"为准(白名单 CustomUiWhitelist 仅作兼容覆盖):
            //   玩家进服时检测 ID, 归属匹配才在 SSS 面板里出现「自定义 UI」下拉; 否则一律按 "default" 渲染。
            public string HudLayout = "default";   // 布局 key(文件名去扩展名); 默认 "default"
            // ---- v2.0 称号系统(三层, 基于 CustomInfo) ----
            public int TitlePlan = 0;                 // 0=不干预 1=无称号才设(层1) 2=强制替换(层2) 3=彻底清除(层3)
            public string TitleText = string.Empty;   // 称号文本
            // (本轮改造: 称号字段 Title 已随称号功能整体删除 —— 旧 .dat 里的 title= 行会被 LoadFromDisk 直接忽略)
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// 旧文件名键(SafeId 形式, 即文件名主干, 例 76561198000000000steam) → 真 UserId(例 76561198000000000@steam)。
        /// v1.4: 老库的键口径是文件名, 新口径统一用文件里的 userId= 行; 这张别名表让"按文件名查找"依旧可用,
        /// 同时保证 _cache 里同一个人只有一条记录(index.txt 不再出现重复行)。
        /// </summary>
        private readonly Dictionary<string, string> _keyAlias = new Dictionary<string, string>();

        /// <summary>
        /// v1.4 官方配置根目录: configs\&lt;服务器端口&gt;\EXPAS
        ///   依据: 文档 21_UI系统权威参考 L124 与 22_完整性校验报告 L187
        ///         (真实路径 = configs\&lt;Server.Port&gt;\&lt;插件名&gt;\, isGlobal=true 才落 configs\global\);
        ///         源码 ConfigurationLoader.GetConfigDirectory(L_710-712):
        ///         PathManager.Configs.CreateSubdirectory(isGlobal ? "global" : Server.Port.ToString())
        ///         .CreateSubdirectory(plugin.Name); PathManager.Configs(L_1457); Server.Port(L_15739)。
        /// 优先用 LabAPI 官方路径入口 GetConfigDirectory(this) —— 它按端口建子目录,
        /// 返回的正好也是本插件 config.yml 所在目录, 不再自己拼 %APPDATA%。
        /// </summary>
        private DirectoryInfo ConfigDirInfo
        {
            get
            {
                // ① 官方入口(扩展方法在 LabApi.Loader.ConfigurationLoader, 见 L610; 全名调用避免额外 using)
                try { return LabApi.Loader.ConfigurationLoader.GetConfigDirectory(this); } catch { }
                try                                                                   // ② 兜底: 官方常量自己拼
                {
                    return PathManager.Configs.CreateSubdirectory(Server.Port.ToString()).CreateSubdirectory(Name);
                }
                catch { }
                return null;
            }
        }

        private string RootDir
        {
            get
            {
                DirectoryInfo dir = ConfigDirInfo;
                // ③ 最后兜底: 万一 LabAPI 路径尚未就绪, 退回旧位置, 至少不丢数据
                string root = dir != null
                    ? dir.FullName
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SCP Secret Laboratory", "LabAPI", "configs", Name);
                try { Directory.CreateDirectory(Path.Combine(root, "players")); } catch { }
                return root;
            }
        }

        private string PlayerDir => Path.Combine(RootDir, "players");

        private static string SafeId(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return "unknown";
            var sb = new StringBuilder();
            foreach (char c in userId)
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "unknown";
        }

        private string FileOf(string userId) => Path.Combine(PlayerDir, SafeId(userId) + ".dat");

        /// <summary>
        /// v1.4 旧库自动迁移: configs\EXPAS\players\ → configs\&lt;端口&gt;\EXPAS\players\。
        /// 只在"新目录还没有任何 .dat"时复制; **旧文件一律保留不删除**, 留给用户自己确认后清理。
        /// </summary>
        private void MigrateLegacyPlayersDir()
        {
            try
            {
                string legacyPlayers = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SCP Secret Laboratory", "LabAPI", "configs", "EXPAS", "players");
                if (!Directory.Exists(legacyPlayers)) return;

                string[] olds = Directory.GetFiles(legacyPlayers, "*.dat");
                if (olds.Length == 0) return;

                string newPlayers = PlayerDir;
                string[] news = Directory.Exists(newPlayers) ? Directory.GetFiles(newPlayers, "*.dat") : new string[0];

                if (news.Length > 0)
                {
                    Logger.Info($"[EXPAS] 检测到旧数据库目录({olds.Length} 个 .dat), 但新目录已有 {news.Length} 条记录 → " +
                                $"跳过迁移(两组文件都保留): {legacyPlayers}");
                    return;
                }

                int copied = 0;
                foreach (string f in olds)
                {
                    string dst = Path.Combine(newPlayers, Path.GetFileName(f));
                    if (File.Exists(dst)) continue;
                    File.Copy(f, dst, false);
                    copied++;
                }
                Logger.Info($"[EXPAS] 旧数据库已迁移(复制) {copied}/{olds.Length} 个玩家文件: {legacyPlayers} → {newPlayers} " +
                            $"(旧文件保留, 未删除)");
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 旧数据库迁移失败: {ex.Message}"); }
        }

        // ========================================================================
        // 等级公式
        // ========================================================================
        /// <summary>从 level 升到 level+1 所需经验。</summary>
        public static float ExpNeeded(int level)
        {
            ExpasPlugin pl = Instance;
            int baseExp = pl != null ? pl.Config.BaseExp : 100;
            float growth = pl != null ? pl.Config.ExpGrowth : 1.35f;
            int lv = Mathf.Max(1, level);
            return Mathf.Max(1f, baseExp * Mathf.Pow(Mathf.Max(1.01f, growth), lv - 1));
        }

        // ========================================================================
        // 读取 / 写入
        // ========================================================================
        /// <summary>
        /// 取玩家数据。v1.4 修正(修 index.txt 重复行): 缓存 **统一以真 UserId 为键**。
        /// 以前入参若是"文件名主干"(SafeId 去 @ 后的结果)也会被当成 key 塞进 _cache,
        /// 于是同一个人存在两条记录 → index.txt 每人重复一行。现在文件名只走 _keyAlias 兼容查找。
        /// </summary>
        public PlayerData GetData(string userId, string name)
        {
            if (string.IsNullOrEmpty(userId))
            {
                // 没有 UserId 时不要往 _cache 里塞脏键(否则索引会出现空键的重复行); 也不落盘(UserId 空时 SavePlayer 直接返回)
                return new PlayerData
                {
                    UserId = string.Empty,
                    Name = name ?? string.Empty,
                    Level = 1,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };
            }

            PlayerData d = FindCached(userId);
            if (d == null)
            {
                // 按 UserId 找文件(FileOf 用 SafeId, 与老库文件名一致); 入参是旧文件名键时同样能按文件名找到
                d = LoadFromDisk(userId);
                if (d == null)
                {
                    d = new PlayerData
                    {
                        UserId = userId,
                        Name = name ?? userId,
                        Level = 1,
                        FirstSeen = DateTime.UtcNow,
                        LastSeen = DateTime.UtcNow
                    };
                }
                CachePut(d);
                if (d.LockMigrated)   // v1.5: 旧数据 lock/longOffline → 新锁定模型, 首次访问时回写一次
                {
                    d.LockMigrated = false;
                    SavePlayer(d);
                }
            }
            if (!string.IsNullOrEmpty(name)) d.Name = name;
            return d;
        }

        /// <summary>查缓存: ① 真 UserId 直查 ② 旧文件名键 → 真 UserId 别名兜底(老库兼容)。</summary>
        private PlayerData FindCached(string key)
        {
            if (_cache.TryGetValue(key, out PlayerData d)) return d;
            if (_keyAlias.TryGetValue(key, out string realId) && _cache.TryGetValue(realId, out PlayerData d2)) return d2;
            return null;
        }

        /// <summary>
        /// 把一条数据挂进缓存: 键一律用真 UserId(读文件里的 userId= 行得到的), 不合法才退回入参键;
        /// 并把"文件名主干 → 真 UserId"登记进 _keyAlias; 若历史上用旧文件名键建过缓存, 顺手挪走(保证一人一条)。
        /// </summary>
        private void CachePut(PlayerData d)
        {
            if (d == null || string.IsNullOrEmpty(d.UserId)) return;
            _cache[d.UserId] = d;

            string fileKey = SafeId(d.UserId);          // 老库键口径 = 文件名主干
            if (string.Equals(fileKey, d.UserId, StringComparison.Ordinal)) return;

            _keyAlias[fileKey] = d.UserId;
            // 历史脏键清理: 旧键上若挂着"另一个实例"(其自身 UserId 与键不同), 移除它, 避免索引重复
            if (_cache.TryGetValue(fileKey, out PlayerData stale) && !ReferenceEquals(stale, d)
                && !string.Equals(stale.UserId, fileKey, StringComparison.Ordinal))
                _cache.Remove(fileKey);
        }

        public PlayerData GetDataById(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            return GetData(userId, null);
        }

        private PlayerData LoadFromDisk(string userId)
        {
            try
            {
                string f = FileOf(userId);
                if (!File.Exists(f)) return null;
                var d = new PlayerData { UserId = userId };
                foreach (string raw in File.ReadAllLines(f))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();

                    switch (k)
                    {
                        // Player.UserId 形如 76561198xxx@steam, 而文件名是 SafeId(去掉 @ 等字符)后的结果,
                        // 所以必须把文件里存的 userId 读回来, 否则 d.UserId 不是真正的 UserId,
                        // FindOnline(d.UserId) 永远匹配不到在线玩家
                        case "userId": d.UserId = v; break;
                        case "name": d.Name = v; break;
                        case "level": int.TryParse(v, NumberStyles.Integer, Inv, out d.Level); break;
                        case "exp": float.TryParse(v, NumberStyles.Float, Inv, out d.Exp); break;
                        case "totalExp": float.TryParse(v, NumberStyles.Float, Inv, out d.TotalExp); break;
                        case "totalDamage": float.TryParse(v, NumberStyles.Float, Inv, out d.TotalDamage); break;
                        case "pendingExp": float.TryParse(v, NumberStyles.Float, Inv, out d.PendingExp); break;
                        case "pendingDamage": float.TryParse(v, NumberStyles.Float, Inv, out d.PendingDamage); break;
                        case "kills": int.TryParse(v, NumberStyles.Integer, Inv, out d.Kills); break;
                        case "deaths": int.TryParse(v, NumberStyles.Integer, Inv, out d.Deaths); break;
                        case "playSeconds": double.TryParse(v, NumberStyles.Float, Inv, out d.PlaySeconds); break;
                        case "playedDays": int.TryParse(v, NumberStyles.Integer, Inv, out d.PlayedDays); break;
                        case "todayPlaySeconds": double.TryParse(v, NumberStyles.Float, Inv, out d.TodayPlaySeconds); break;
                        case "todayDate": d.TodayDate = v; break;
                        case "lock": int.TryParse(v, NumberStyles.Integer, Inv, out d.LockState); break;
                        case "longOffline": d.LongOffline = v == "1"; break;
                        case "hudSize":
                            if (v == "default") d.HudSize = int.MinValue;
                            else if (int.TryParse(v, NumberStyles.Integer, Inv, out int hs)) d.HudSize = hs;
                            break;
                        case "sizePercent": int.TryParse(v, NumberStyles.Integer, Inv, out d.SizePercent); break;
                        case "screenPercent": int.TryParse(v, NumberStyles.Integer, Inv, out d.ScreenPercent); break;
                        case "lineHeightPercent": int.TryParse(v, NumberStyles.Integer, Inv, out d.LineHeightPercent); break;
                        case "voffset": int.TryParse(v, NumberStyles.Integer, Inv, out d.VOffset); break;
                        case "halign": int.TryParse(v, NumberStyles.Integer, Inv, out d.HAlign); break;
                        case "levelAlign": int.TryParse(v, NumberStyles.Integer, Inv, out d.LevelAlign); break;
                        case "hudEnabled": int.TryParse(v, NumberStyles.Integer, Inv, out d.HudEnabledSet); break;
                        case "points": float.TryParse(v, NumberStyles.Float, Inv, out d.Points); break;
                        case "lastKillName": d.LastKillName = v; break;
                        case "lastKillDamage": float.TryParse(v, NumberStyles.Float, Inv, out d.LastKillDamage); break;
                        case "lastKillShare": float.TryParse(v, NumberStyles.Float, Inv, out d.LastKillShare); break;
                        case "lastKillPoints": float.TryParse(v, NumberStyles.Float, Inv, out d.LastKillPoints); break;
                        // v1.5 击杀确认: 旧 .dat 没有这两行 → 保持字段初值 "-"
                        case "lastKillRole": d.LastKillRole = v; break;
                        case "lastKillRoleKind": d.LastKillRoleKind = v; break;
                        // v1.6 分辨率档位 / 整体缩放 / HUD 折叠(旧 .dat 没有 → 保持 -1 = 跟随服务器配置)
                        case "resolutionPreset": int.TryParse(v, NumberStyles.Integer, Inv, out d.ResolutionPreset); break;
                        case "globalScalePercent": int.TryParse(v, NumberStyles.Integer, Inv, out d.GlobalScalePercent); break;
                        case "hudCollapsed": int.TryParse(v, NumberStyles.Integer, Inv, out d.HudCollapsed); break;
                        // v1.7 自定义 UI 布局(旧 .dat 没有该行 → 保持初值 "default")
                        case "hudLayout": if (!string.IsNullOrEmpty(v)) d.HudLayout = v; break;
                        // v2.0 称号系统(旧 .dat 没有 → 保持 0/空)
                        case "titlePlan": int.TryParse(v, NumberStyles.Integer, Inv, out d.TitlePlan); break;
                        case "titleText": if (!string.IsNullOrEmpty(v)) d.TitleText = v; break;
                        case "firstSeen":
                            if (DateTime.TryParse(v, Inv, DateTimeStyles.RoundtripKind, out DateTime fs)) d.FirstSeen = fs;
                            break;
                        case "lastSeen":
                            if (DateTime.TryParse(v, Inv, DateTimeStyles.RoundtripKind, out DateTime ls)) d.LastSeen = ls;
                            break;
                    }
                }
                // v1.5 兼容旧数据文件的锁定口径(加载时一次性迁移, 首次访问时回写):
                //   旧表: lock=0 正常 / 1 全数据锁定 / 2 自愿锁定 + 独立 longOffline 标记
                //   新表: 0=none / 1=punishment / 2=selfban / 3=inactive
                //   映射: 1→1(同为"什么都不记录"), 2→2(同为"伤害不涨/经验叠加"), longOffline=1 且 lock=0→3。
                if (d.LockState == LockNone && d.LongOffline)
                {
                    d.LockState = LockInactive;     // 旧: 只打了 🕒 标记 → 新: inactive 状态
                    d.LockMigrated = true;
                }
                if (d.LockState < LockNone || d.LockState > LockInactive)
                {
                    d.LockState = d.LongOffline ? LockInactive : LockNone;   // 越界值夹回
                    d.LockMigrated = true;
                }
                if (d.LockState == LockInactive) d.LongOffline = true;       // 旧字段与新表同步

                if (d.Level < 1) d.Level = 1;
                if (d.FirstSeen == default(DateTime)) d.FirstSeen = DateTime.UtcNow;
                return d;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EXPAS] 读取 {userId} 数据失败: {ex.Message}");
                return null;
            }
        }

        public void SavePlayer(PlayerData d)
        {
            if (d == null || string.IsNullOrEmpty(d.UserId)) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# EXPAS player data v" + PluginVersion);
                sb.AppendLine("userId=" + d.UserId);
                sb.AppendLine("name=" + (d.Name ?? string.Empty));
                sb.AppendLine("level=" + d.Level.ToString(Inv));
                sb.AppendLine("exp=" + d.Exp.ToString("R", Inv));
                sb.AppendLine("totalExp=" + d.TotalExp.ToString("R", Inv));
                sb.AppendLine("totalDamage=" + d.TotalDamage.ToString("R", Inv));
                sb.AppendLine("pendingExp=" + d.PendingExp.ToString("R", Inv));
                sb.AppendLine("pendingDamage=" + d.PendingDamage.ToString("R", Inv));
                sb.AppendLine("kills=" + d.Kills.ToString(Inv));
                sb.AppendLine("deaths=" + d.Deaths.ToString(Inv));
                sb.AppendLine("playSeconds=" + d.PlaySeconds.ToString("R", Inv));
                sb.AppendLine("playedDays=" + d.PlayedDays.ToString(Inv));
                sb.AppendLine("todayPlaySeconds=" + d.TodayPlaySeconds.ToString("R", Inv));
                sb.AppendLine("todayDate=" + (d.TodayDate ?? string.Empty));
                sb.AppendLine("lock=" + d.LockState.ToString(Inv));
                sb.AppendLine("longOffline=" + (d.LongOffline ? "1" : "0"));
                sb.AppendLine("hudSize=" + (d.HudSize == int.MinValue ? "default" : d.HudSize.ToString(Inv)));
                sb.AppendLine("sizePercent=" + d.SizePercent.ToString(Inv));
                sb.AppendLine("screenPercent=" + d.ScreenPercent.ToString(Inv));
                sb.AppendLine("lineHeightPercent=" + d.LineHeightPercent.ToString(Inv));
                sb.AppendLine("voffset=" + d.VOffset.ToString(Inv));
                sb.AppendLine("halign=" + d.HAlign.ToString(Inv));
                sb.AppendLine("levelAlign=" + d.LevelAlign.ToString(Inv));
                sb.AppendLine("hudEnabled=" + d.HudEnabledSet.ToString(Inv));
                sb.AppendLine("points=" + d.Points.ToString("R", Inv));
                sb.AppendLine("lastKillName=" + (d.LastKillName ?? "-"));
                sb.AppendLine("lastKillDamage=" + d.LastKillDamage.ToString("R", Inv));
                sb.AppendLine("lastKillShare=" + d.LastKillShare.ToString("R", Inv));
                sb.AppendLine("lastKillPoints=" + d.LastKillPoints.ToString("R", Inv));
                sb.AppendLine("lastKillRole=" + (d.LastKillRole ?? "-"));
                sb.AppendLine("lastKillRoleKind=" + (d.LastKillRoleKind ?? "-"));
                // v1.6 分辨率档位 / 整体缩放 / HUD 折叠(-1 = 跟随服务器 config.yml)
                sb.AppendLine("resolutionPreset=" + d.ResolutionPreset.ToString(Inv));
                sb.AppendLine("globalScalePercent=" + d.GlobalScalePercent.ToString(Inv));
                sb.AppendLine("hudCollapsed=" + d.HudCollapsed.ToString(Inv));
                // v1.7 自定义 UI 布局 key(默认 default; 只有白名单玩家可在 SSS 面板里切换)
                sb.AppendLine("hudLayout=" + (string.IsNullOrEmpty(d.HudLayout) ? "default" : d.HudLayout));
                // v2.0 称号系统(三层)
                sb.AppendLine("titlePlan=" + d.TitlePlan.ToString(Inv));
                sb.AppendLine("titleText=" + (d.TitleText ?? string.Empty));
                sb.AppendLine("firstSeen=" + d.FirstSeen.ToUniversalTime().ToString("o", Inv));
                sb.AppendLine("lastSeen=" + d.LastSeen.ToUniversalTime().ToString("o", Inv));
                File.WriteAllText(FileOf(d.UserId), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 保存 {d.UserId} 失败: {ex.Message}"); }
        }

        /// <summary>把在线玩家的游玩时长结算进内存数据后落盘; 只写在线玩家, 不动离线记录的 LastSeen(否则 🕒 判定会失效)。</summary>
        public void FlushAll(string reason)
        {
            try
            {
                var online = new HashSet<string>();
                // v1.4: 在线判定改走 Player.ReadyList(L_14003 = IsDummy || (IsPlayer && IsReady)) 再滤掉人机
                // —— 未认证连接(IsReady=false)不算在线, 避免它们的会话被错误计时/落盘
                foreach (Player p in Player.ReadyList.Where(p => p.IsPlayer && !p.IsDummy))
                {
                    if (p == null || p.IsDestroyed) continue;
                    online.Add(p.UserId);
                }

                // 掉线残留的会话(Left 事件里 Player 已销毁时)直接丢弃, 不再计时
                foreach (string uid in _sessionStart.Keys.ToList())
                {
                    if (!online.Contains(uid)) { _sessionStart.Remove(uid); continue; }

                    PlayerData d = GetData(uid, null);
                    double delta = (DateTime.UtcNow - _sessionStart[uid]).TotalSeconds;
                    d.PlaySeconds += delta;
                    AddTodayPlayTime(d, delta);          // v1.5: 今日游玩时间(跨天自动归零累加)
                    d.LastSeen = DateTime.UtcNow;
                    _sessionStart[uid] = DateTime.UtcNow;
                    SavePlayer(d);
                }
                WriteIndex();
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 保存失败({reason}): {ex.Message}"); }
        }

        private void WriteIndex()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# EXPAS 玩家索引 (Steam64 | 名字 | 等级 | 总伤害 | 累计经验 | 状态 | 最后在线UTC)");
                // v1.4: 缓存已统一以真 UserId 为键; 这里再兜底按 UserId 去重, 保证一个玩家只写一行(修重复行)
                foreach (PlayerData d in _cache.Values
                             .GroupBy(x => x.UserId, StringComparer.Ordinal)
                             .Select(g => g.First())
                             .OrderByDescending(x => x.Level).ThenBy(x => x.Name))
                {
                    string st = LockIcon(d.LockState);                       // [锁] / [禁] / [离] (或 emoji, 见 IconPreset)
                    if (string.IsNullOrEmpty(st)) st = "-";
                    if (d.LongOffline && d.LockState != LockInactive) st += LockIcon(LockInactive);
                    sb.AppendLine($"{d.UserId} | {d.Name} | {d.Level} | {d.TotalDamage:F0} | {d.TotalExp:F0} | {st} | " +
                                  d.LastSeen.ToUniversalTime().ToString("o", Inv));
                }
                File.WriteAllText(Path.Combine(RootDir, "index.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Logger.Info($"[EXPAS] 写索引失败: {ex.Message}"); }
        }

        // ========================================================================
        // 管理操作
        // ========================================================================
        public void AddExp(PlayerData d, float amount)
        {
            if (d == null) return;
            if (amount < 0f)
            {
                d.Exp = Mathf.Max(0f, d.Exp + amount);
                SavePlayer(d);
                return;
            }
            ApplyExp(d, amount);
        }

        public void ResetData(PlayerData d)
        {
            if (d == null) return;
            d.Level = 1;
            d.Exp = 0f;
            d.TotalExp = 0f;
            d.TotalDamage = 0f;
            d.PendingExp = 0f;
            d.PendingDamage = 0f;
            d.Kills = 0;
            d.Deaths = 0;
            d.PlaySeconds = 0d;
            d.Points = 0f;                    // v1.5: 一并清空积分
            d.PlayedDays = 0;                 // v1.5 规格字段
            d.TodayPlaySeconds = 0d;
            d.TodayDate = string.Empty;
            d.LockState = LockNone;
            d.LongOffline = false;
            d.LastKillRole = "-";             // v1.5 击杀确认字段一并清空
            d.LastKillRoleKind = "-";
            // v1.6: 显示档位一并回到"跟随服务器配置"
            d.ResolutionPreset = -1;
            d.GlobalScalePercent = -1;
            d.HudCollapsed = -1;
            d.HudLayout = "default";          // v1.7: 自定义 UI 布局回到默认布局
            d.FirstSeen = DateTime.UtcNow;
            d.LastSeen = DateTime.UtcNow;
            SavePlayer(d);
            Logger.Info($"[EXPAS] 已重置 {d.Name}({d.UserId})");
        }

        // ========================================================================
        // 🕒 长期未上线标记
        // ========================================================================
        private void MarkLongOffline(PlayerData d)
        {
            if (d == null) return;
            // v1.5: 玩家回来了 → 自动把 inactive 清回 none(🕒 摘掉); punishment/selfban 不受影响
            if (d.LockState == LockInactive)
            {
                d.LockState = LockNone;
                Logger.Info($"[EXPAS] {d.Name}({d.UserId}) 已重新上线, inactive(🕒) 自动清回 none");
            }
            d.LongOffline = false;   // 人回来了就摘掉
        }

        /// <summary>扫描所有玩家文件, 超过配置天数未上线就自动打上 🕒 并写盘。</summary>
        public void MarkLongOfflinePass()
        {
            try
            {
                int days = Mathf.Max(1, Config.OfflineDays);
                DateTime now = DateTime.UtcNow;
                DateTime limit = now.AddDays(-days);
                int marked = 0;

                foreach (string f in Directory.GetFiles(PlayerDir, "*.dat"))
                {
                    string uid = Path.GetFileNameWithoutExtension(f);   // 旧键口径(文件名主干)
                    PlayerData d = GetDataById(uid);                    // v1.4: 经 _keyAlias 映射到真 UserId, 不再新建重复记录
                    if (d == null) continue;

                    bool online = FindOnline(d.UserId) != null;
                    bool shouldMark = !online && d.LastSeen.ToUniversalTime() < limit;
                    // v1.5: 自动置位 inactive(🕒); 只在玩家当前是 none 时改动, 不覆盖 punishment/selfban
                    if (shouldMark && d.LockState == LockNone)
                    {
                        d.LockState = LockInactive;
                        d.LongOffline = true;
                        SavePlayer(d);
                        marked++;
                    }
                }
                if (marked > 0) Logger.Info($"[EXPAS] 长期未上线({days}天)自动标记 🕒: {marked} 人");
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 离线扫描失败: {ex.Message}"); }
        }

        /// <summary>
        /// 启动时扫描数据库目录, 把磁盘数据读进缓存并刷新索引; 同时打印一次规模。
        /// v1.4 修正(修 index.txt 重复行): 以前这里"用文件名当 key 读一遍就丢掉", 缓存要等玩家上线
        /// 才慢慢建起来, 而且 MarkLongOfflinePass 用文件名调 GetDataById → 同一个人被建成两个 key
        /// (旧库文件名 = SafeId 去 @ 后的结果), index.txt 就每人重复两行。
        /// 现在统一走 CachePut: **真 UserId(读文件里的 userId= 行)为键**, 旧文件名登记进 _keyAlias
        /// —— 仍能按文件名找到文件/记录(兼容), 但同一个人只占一条。
        /// </summary>
        private void LoadOrCreateIndex()
        {
            try
            {
                int n = 0, merged = 0;
                foreach (string f in Directory.GetFiles(PlayerDir, "*.dat"))
                {
                    string fileKey = Path.GetFileNameWithoutExtension(f);   // 旧键口径 = 文件名主干
                    if (FindCached(fileKey) != null) { merged++; continue; }  // 同一 UserId 的另一个文件名 → 合并, 不重复读
                    PlayerData d = LoadFromDisk(fileKey);                    // 按文件名找文件(兼容旧文件名)
                    if (d == null) continue;
                    _keyAlias[fileKey] = d.UserId;   // 旧文件名 → 真 UserId(按文件名也能找到这条记录)
                    CachePut(d);                     // 统一挂到真 UserId 键
                    n++;
                }
                WriteIndex();   // 立刻按真 UserId 重写一次索引 —— 老库里同一人两行的问题启动就修掉
                Logger.Info($"[EXPAS] 数据库: {PlayerDir} (已有 {n} 条玩家记录" +
                            (merged > 0 ? $", 合并了 {merged} 个同 UserId 的重复文件名" : string.Empty) + ")");
            }
            catch (Exception ex) { Logger.Warn($"[EXPAS] 初始化数据库失败: {ex.Message}"); }
        }

        // ========================================================================
        // 时间 / 文案
        // ========================================================================
        private static readonly Dictionary<string, int> TimeZoneMap = new Dictionary<string, int>
        {
            { "中国", 8 }, { "北京", 8 }, { "香港", 8 }, { "台湾", 8 }, { "新加坡", 8 },
            { "日本", 9 }, { "韩国", 9 },
            { "泰国", 7 }, { "越南", 7 }, { "印尼", 7 },
            { "印度", 5 }, { "印度半", 5 },
            { "俄罗斯", 3 }, { "俄国", 3 }, { "土耳其", 3 },
            { "英国", 0 }, { "法国", 1 }, { "德国", 1 }, { "意大利", 1 }, { "西班牙", 1 },
            { "巴西", -3 }, { "阿根廷", -3 },
            { "美国东部", -5 }, { "纽约", -5 }, { "美国中部", -6 }, { "美国山地", -7 },
            { "美国西部", -8 }, { "洛杉矶", -8 },
            { "澳洲", 10 }, { "悉尼", 10 }, { "新西兰", 12 }
        };

        /// <summary>解析配置里的时区: 支持 "GMT+8" / "+8" / "8" / 国家名; 默认 +8。</summary>
        public int GetOffsetHours()
        {
            string s = (Config.TimeZone ?? string.Empty).Trim();
            if (s.Length == 0) return 8;

            string digits = s.Replace("GMT", "").Replace("gmt", "").Replace("UTC", "").Replace("utc", "").Trim();
            if (digits.Length > 0 && (digits[0] == '+' || digits[0] == '-' || char.IsDigit(digits[0])))
            {
                if (float.TryParse(digits, NumberStyles.Float, Inv, out float f))
                    return Mathf.Clamp(Mathf.RoundToInt(f), -12, 14);
            }
            foreach (var kv in TimeZoneMap)
                if (s.Contains(kv.Key)) return kv.Value;
            return 8;
        }

        public string GetTimeZoneLabel()
        {
            string s = (Config.TimeZone ?? string.Empty).Trim();
            int off = GetOffsetHours();
            string sign = off >= 0 ? "+" : "-";
            if (s.Length == 0 || s.StartsWith("+") || s.StartsWith("-") || char.IsDigit(s[0]))
                return $"GMT{sign}{Mathf.Abs(off)}";
            if (s.Contains("GMT") || s.Contains("UTC")) return s;
            return $"{s} GMT{sign}{Mathf.Abs(off)}";
        }

        public string GetLocalTimeText()
        {
            DateTime t = DateTime.UtcNow.AddHours(GetOffsetHours());
            return t.ToString("HH:mm:ss", Inv);
        }

        /// <summary>以每天 12:00 为新一天分界, 返回自 from 起算的"第几天"(从1开始)。</summary>
        public static int DaysSince(DateTime fromUtc, DateTime nowUtc)
        {
            DateTime a = fromUtc.ToUniversalTime().Date.AddHours(12);
            if (fromUtc.ToUniversalTime() < a) a = a.AddDays(-1);
            DateTime b = nowUtc.ToUniversalTime().Date.AddHours(12);
            if (nowUtc.ToUniversalTime() < b) b = b.AddDays(-1);
            int days = (int)Math.Round((b - a).TotalDays) + 1;
            return Mathf.Max(1, days);
        }

        public static string FormatDuration(double seconds)
        {
            if (seconds < 0d) seconds = 0d;
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalDays >= 1d)
                return $"{(int)ts.TotalDays}天{ts.Hours}小时{ts.Minutes}分";
            if (ts.TotalHours >= 1d)
                return $"{ts.Hours}小时{ts.Minutes}分{ts.Seconds}秒";
            return $"{ts.Minutes}分{ts.Seconds}秒";
        }

        // ========================================================================
        // v1.5 今日游玩时间(today_play_time) —— 跨天自动归零累加
        // ========================================================================
        /// <summary>"今日"的日期键(yyyy-MM-dd), 按配置的时区计算 —— 与 HUD 显示的当地时间一致。</summary>
        public string TodayKey()
            => DateTime.UtcNow.AddHours(GetOffsetHours()).ToString("yyyy-MM-dd", Inv);

        /// <summary>
        /// 累加"今日游玩时间"(秒); TodayDate 与当前日期键不一致(跨天)时先归零再累加。
        /// 由 FlushAll / OnLeft 在结算会话时长时调用。
        /// </summary>
        public void AddTodayPlayTime(PlayerData d, double deltaSeconds)
        {
            if (d == null || deltaSeconds <= 0d) return;
            string today = TodayKey();
            if (!string.Equals(d.TodayDate, today, StringComparison.Ordinal))
            {
                d.TodayDate = today;
                d.TodayPlaySeconds = 0d;      // 跨天 → 今日游玩时间归零重新累计
            }
            d.TodayPlaySeconds += deltaSeconds;
        }

        /// <summary>读"今日游玩时间"(秒); 已跨天则按 0 返回(纯读取, 不写盘), 用于 HUD/面板显示。</summary>
        public double GetTodayPlaySeconds(PlayerData d)
        {
            if (d == null) return 0d;
            return string.Equals(d.TodayDate, TodayKey(), StringComparison.Ordinal) ? d.TodayPlaySeconds : 0d;
        }
    }
}
