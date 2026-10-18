// ============================================================================
//   EXPAS —— 帮助与"字段匹配"(简略显示)
//   背景: SCP:SL 客户端只同步**命令名**给补全列表(QueryProcessor.CommandData:
//         Command/Usage/Description/AliasOf/Hidden —— 见文档 06 号 L269-270),
//         自定义插件的**参数**无法在客户端做补全。所以这里在**服务端**做"字段匹配":
//           EXPAS                  → 简略用法总览
//           EXPAS <关键字>          → 列出匹配的子命令 / 字段(前缀与包含匹配)
//           EXPAS help [关键字]     → 同上
//   目的: 管理员在 RA 面板敲 EXPAS 之后, 立刻知道"有什么子命令、什么字段、怎么用"。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using L = Expas.ExpasLang;

namespace Expas
{
    public partial class ExpasPlugin
    {
        /// <summary>一条帮助项: Keys=可匹配的别名(空格分隔), Usage=用法, Desc=一句话说明。</summary>
        private sealed class HelpItem
        {
            public string Keys;
            public string Usage;
            public string UsageEn;
            public string Desc;
            public string DescEn;
        }

        /// <summary>子命令/字段帮助表(顺序即显示顺序)。</summary>
        private static readonly HelpItem[] HelpItems =
        {
            new HelpItem { Keys = "set add",       Usage = "set|add <玩家ID> <字段> <值>", UsageEn = "set|add <PlayerID> <Field> <Value>", Desc = "改数值字段(见「字段」)", DescEn = "Modify numeric Fields (see \"Fields\")" },
            new HelpItem { Keys = "info",          Usage = "info <玩家ID>",                UsageEn = "info <PlayerID>", Desc = "查玩家经验/等级/伤害/锁定", DescEn = "Show Player EXP / Level / Damage / Lock" },
            new HelpItem { Keys = "addlevel",      Usage = "addlevel <玩家ID> <数量>",      UsageEn = "addlevel <PlayerID> <Amount>", Desc = "增加等级", DescEn = "Add Levels" },
            new HelpItem { Keys = "restartlevel",  Usage = "restartlevel <玩家ID>",         UsageEn = "restartlevel <PlayerID>", Desc = "重置等级到 1 级", DescEn = "Reset Level to 1" },
            new HelpItem { Keys = "lock unlock full", Usage = "lock|unlock|full <玩家ID> [0-3]", UsageEn = "lock|unlock|full <PlayerID> [0-3]", Desc = "锁定/解锁(0正常 1惩罚 2自助封禁 3长期未上线)", DescEn = "Lock/Unlock (0 normal, 1 punishment, 2 self-ban, 3 inactive)" },
            new HelpItem { Keys = "reset",         Usage = "reset <玩家ID>",               UsageEn = "reset <PlayerID>", Desc = "清空该玩家全部数据", DescEn = "Clear all data of this Player" },
            new HelpItem { Keys = "xpfreeze",      Usage = "xpfreeze [on|off]",            UsageEn = "xpfreeze [on|off]", Desc = "冻结本回合经验增长", DescEn = "Freeze EXP gain this Round" },
            new HelpItem { Keys = "title",         Usage = "title <玩家ID> <0|1|2|3> [文本]", UsageEn = "title <PlayerID> <0|1|2|3> [text]", Desc = "称号: 0不干预 1无称号才设 2强制替换 3彻底清除", DescEn = "Title: 0 off, 1 only if empty, 2 force replace, 3 clear" },
            new HelpItem { Keys = "audio",         Usage = "audio play <文件.wav>|list|stop|call", UsageEn = "audio play <file.wav>|list|stop|call", Desc = "播放音频(隐藏音箱, 不生成假人)", DescEn = "Play Audio (hidden speaker, no dummy)" },
            new HelpItem { Keys = "mvpmusic",      Usage = "mvpmusic list|play <玩家ID>",   UsageEn = "mvpmusic list|play <PlayerID>", Desc = "第一名音乐盒: 回合伤害第一播其专属音频", DescEn = "MVP Music: play the Round's top-damage Player's own Audio" },
            new HelpItem { Keys = "layout",        Usage = "layout list|reload|info <玩家ID>|<玩家ID> <布局>", UsageEn = "layout list|reload|info <PlayerID>|<PlayerID> <Layout>", Desc = "自定义 UI 布局(layouts\\*.txt|*.js)", DescEn = "Custom UI Layout (layouts\\*.txt|*.js)" },
            new HelpItem { Keys = "whitelist",     Usage = "whitelist add|remove|list [Steam64]", UsageEn = "whitelist add|remove|list [Steam64]", Desc = "自定义 UI 白名单(兼容覆盖)", DescEn = "Custom UI Whitelist (compatible override)" },
            new HelpItem { Keys = "arsa",          Usage = "arsa [full]",                  UsageEn = "arsa [full]", Desc = "重新识别其它插件(重读配置文件+文件, 不含自身)", DescEn = "Re-detect other Plugins (reload Configs + files, excluding self)" },
            new HelpItem { Keys = "config setcfg", Usage = "config | setcfg <键> <值>",     UsageEn = "config | setcfg <key> <value>", Desc = "查看/修改插件配置", DescEn = "View / modify Plugin Config" },
            new HelpItem { Keys = "time",          Usage = "time <时区>",                  UsageEn = "time <timezone>", Desc = "改 HUD 时区(如 GMT+8 / 中国)", DescEn = "Change HUD time zone (e.g. GMT+8 / China)" },
            new HelpItem { Keys = "line",          Usage = "line <1-4|level1|level2> <文本>", UsageEn = "line <1-4|level1|level2> <text>", Desc = "改 HUD 某行文案", DescEn = "Change a HUD line text" },
            new HelpItem { Keys = "custom",        Usage = "custom add|clear|list [文本]",  UsageEn = "custom add|clear|list [text]", Desc = "HUD 自定义行", DescEn = "Custom HUD lines" },
            new HelpItem { Keys = "mvp",           Usage = "mvp <文本>",                   UsageEn = "mvp <text>", Desc = "回合 MVP 公告模板(空=关)", DescEn = "Round MVP announcement template (empty = off)" },
            new HelpItem { Keys = "top",           Usage = "top",                          UsageEn = "top", Desc = "本回合排行榜", DescEn = "Round leaderboard" },
            new HelpItem { Keys = "api",           Usage = "api <0|1>",                    UsageEn = "api <0|1>", Desc = "开关本回合 API", DescEn = "Toggle this Round's API" },
            new HelpItem { Keys = "save",          Usage = "save",                         UsageEn = "save", Desc = "立即保存数据与配置", DescEn = "Save data and Config now" },
            new HelpItem { Keys = "help",          Usage = "help [关键字]",                UsageEn = "help [keyword]", Desc = "帮助 / 字段匹配", DescEn = "Help / Field matching" },
        };

        /// <summary>set|add 可用字段。</summary>
        private static readonly string[] HelpFields =
        {
            "level", "exp", "points", "lock", "total_damage", "total_kills",
            "played_days", "today_play_time", "total_play_time"
        };

        /// <summary>玩家ID 写法提示(三种解析方式均可在配置里开关)。属性形式: 每次取词，避免静态初始化早于语言检测。</summary>
        private static string HelpPlayerNote
        {
            get
            {
                return L.T(
                    "<玩家ID> 三种写法: ①序列ID(RA玩家列表编号, 如 3) ②玩家名(可只写一部分) ③Steam64/UserId;" +
                    "①②需在线, ③离线也能改。各写法可在 config.yml 里开关(lookup_by_seq_id / lookup_by_name / lookup_by_steam / name_lookup_contains)",
                    "<PlayerID> has 3 forms: 1) sequence ID (RA player list number, e.g. 3) 2) player name (partial allowed) 3) Steam64/UserId; " +
                    "1 and 2 require the Player to be online, 3 works offline. Each form can be toggled in config.yml (lookup_by_seq_id / lookup_by_name / lookup_by_steam / name_lookup_contains)");
            }
        }

        /// <summary>
        /// 生成简略帮助: filter 为空 → 总览; 非空 → 只列匹配的子命令/字段。
        /// </summary>
        public string ExpasHelpText(string filter)
        {
            string f = (filter ?? string.Empty).Trim().ToLowerInvariant();

            // ---- 无关键字: 简略总览(务必短, 便于 RA 面板阅读) ----
            if (f.Length == 0)
            {
                var sb = new StringBuilder();
                sb.Append(L.T("EXPAS 经验扩展插件系统 v", "EXPAS Experience Expansion Plugin v")).Append(PluginVersion).Append('\n');
                sb.Append(L.T("用法: EXPAS <子命令> [参数]    输入 EXPAS <关键字> 可匹配(如 EXPAS se)\n",
                              "Usage: EXPAS <Subcommand> [args]    Type EXPAS <keyword> to match (e.g. EXPAS se)\n"));
                sb.Append(L.T("玩家ID: 序列ID(如 3) / 玩家名(可只写一部分) / Steam64 —— 写法可在配置里开关\n",
                              "PlayerID: sequence ID (e.g. 3) / player name (partial allowed) / Steam64 — each form can be toggled in Config\n"));
                sb.Append(L.T("常用: set|add  info  lock|unlock  title  audio  mvpmusic\n",
                              "Common: set|add  info  lock|unlock  title  audio  mvpmusic\n"));
                sb.Append(L.T("其它: layout  whitelist  arsa  config  time  line  custom  mvp  top  api  save  help\n",
                              "Other: layout  whitelist  arsa  config  time  line  custom  mvp  top  api  save  help\n"));
                sb.Append(L.T("字段(set|add): ", "Fields (set|add): ")).Append(string.Join(" ", HelpFields));
                sb.Append('\n').Append(L.T("示例: EXPAS set 3 level 225   |   EXPAS add 示例玩家 points 300   |   EXPAS lock 76561198… 1   |   EXPAS arsa",
                                           "Example: EXPAS set 3 level 225   |   EXPAS add ExamplePlayer points 300   |   EXPAS lock 76561198… 1   |   EXPAS arsa"));
                return sb.ToString();
            }

            // ---- 有关键字: 匹配子命令 ----
            var hits = HelpItems.Where(i => MatchesKey(i.Keys, f)).ToList();

            // ---- 同时匹配 set|add 的字段名 ----
            var fieldHits = HelpFields.Where(x => x.Contains(f)).ToList();

            if (hits.Count == 0 && fieldHits.Count == 0)
            {
                return L.T($"没有匹配「{filter}」的子命令或字段。\n", $"No Subcommand or Field matches \"{filter}\".\n") +
                       L.T("输入 EXPAS 看总览; 常用: set|add info lock unlock title audio mvpmusic layout arsa help",
                           "Type EXPAS for an overview; Common: set|add info lock unlock title audio mvpmusic layout arsa help");
            }

            var sb2 = new StringBuilder(L.T($"匹配「{filter}」:", $"Matches for \"{filter}\":"));
            foreach (HelpItem i in hits)
                sb2.Append($"\n• {L.T(i.Usage, i.UsageEn)}    —— {L.T(i.Desc, i.DescEn)}");
            if (fieldHits.Count > 0)
            {
                sb2.Append(L.T("\n字段: ", "\nFields: ")).Append(string.Join(" / ", fieldHits));
                if (fieldHits.Contains("lock"))
                    sb2.Append(L.T("   (lock 取值: 0=none 1=punishment 2=selfban 3=inactive)", "   (lock values: 0=none 1=punishment 2=selfban 3=inactive)"));
            }
            if (hits.Any(i => i.Keys.Split(' ').Contains("info") || i.Keys.Contains("set")))
                sb2.Append('\n').Append(HelpPlayerNote);
            return sb2.ToString();
        }

        /// <summary>关键字是否命中该条目: 任一别名以 f 开头, 或包含 f。</summary>
        private static bool MatchesKey(string keys, string f)
        {
            foreach (string k in keys.Split(' '))
            {
                if (k.Length == 0) continue;
                if (k.StartsWith(f, StringComparison.OrdinalIgnoreCase)) return true;
            }
            foreach (string k in keys.Split(' '))
                if (k.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}