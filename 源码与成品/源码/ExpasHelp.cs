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

namespace Expas
{
    public partial class ExpasPlugin
    {
        /// <summary>一条帮助项: Keys=可匹配的别名(空格分隔), Usage=用法, Desc=一句话说明。</summary>
        private sealed class HelpItem
        {
            public string Keys;
            public string Usage;
            public string Desc;
        }

        /// <summary>子命令/字段帮助表(顺序即显示顺序)。</summary>
        private static readonly HelpItem[] HelpItems =
        {
            new HelpItem { Keys = "set add",       Usage = "set|add <玩家ID> <字段> <值>", Desc = "改数值字段(见「字段」)" },
            new HelpItem { Keys = "info",          Usage = "info <玩家ID>",                Desc = "查玩家经验/等级/伤害/锁定" },
            new HelpItem { Keys = "addlevel",      Usage = "addlevel <玩家ID> <数量>",      Desc = "增加等级" },
            new HelpItem { Keys = "restartlevel",  Usage = "restartlevel <玩家ID>",         Desc = "重置等级到 1 级" },
            new HelpItem { Keys = "lock unlock full", Usage = "lock|unlock|full <玩家ID> [0-3]", Desc = "锁定/解锁(0正常 1惩罚 2自助封禁 3长期未上线)" },
            new HelpItem { Keys = "reset",         Usage = "reset <玩家ID>",               Desc = "清空该玩家全部数据" },
            new HelpItem { Keys = "xpfreeze",      Usage = "xpfreeze [on|off]",            Desc = "冻结本回合经验增长" },
            new HelpItem { Keys = "title",         Usage = "title <玩家ID> <0|1|2|3> [文本]", Desc = "称号: 0不干预 1无称号才设 2强制替换 3彻底清除" },
            new HelpItem { Keys = "audio",         Usage = "audio play <文件.wav>|list|stop|call", Desc = "播放音频(隐藏音箱, 不生成假人)" },
            new HelpItem { Keys = "mvpmusic",      Usage = "mvpmusic list|play <玩家ID>",   Desc = "第一名音乐盒: 回合伤害第一播其专属音频" },
            new HelpItem { Keys = "layout",        Usage = "layout list|reload|info <玩家ID>|<玩家ID> <布局>", Desc = "自定义 UI 布局(layouts\\*.txt|*.js)" },
            new HelpItem { Keys = "whitelist",     Usage = "whitelist add|remove|list [Steam64]", Desc = "自定义 UI 白名单(兼容覆盖)" },
            new HelpItem { Keys = "arsa",          Usage = "arsa [full]",                  Desc = "重新识别其它插件(重读配置文件+文件, 不含自身)" },
            new HelpItem { Keys = "config setcfg", Usage = "config | setcfg <键> <值>",     Desc = "查看/修改插件配置" },
            new HelpItem { Keys = "time",          Usage = "time <时区>",                  Desc = "改 HUD 时区(如 GMT+8 / 中国)" },
            new HelpItem { Keys = "line",          Usage = "line <1-4|level1|level2> <文本>", Desc = "改 HUD 某行文案" },
            new HelpItem { Keys = "custom",        Usage = "custom add|clear|list [文本]",  Desc = "HUD 自定义行" },
            new HelpItem { Keys = "mvp",           Usage = "mvp <文本>",                   Desc = "回合 MVP 公告模板(空=关)" },
            new HelpItem { Keys = "top",           Usage = "top",                          Desc = "本回合排行榜" },
            new HelpItem { Keys = "api",           Usage = "api <0|1>",                    Desc = "开关本回合 API" },
            new HelpItem { Keys = "save",          Usage = "save",                         Desc = "立即保存数据与配置" },
            new HelpItem { Keys = "help",          Usage = "help [关键字]",                Desc = "帮助 / 字段匹配" },
        };

        /// <summary>set|add 可用字段。</summary>
        private static readonly string[] HelpFields =
        {
            "level", "exp", "points", "lock", "total_damage", "total_kills",
            "played_days", "today_play_time", "total_play_time"
        };

        /// <summary>玩家ID 写法提示(三种解析方式均可在配置里开关)。</summary>
        private const string HelpPlayerNote =
            "<玩家ID> 三种写法: ①序列ID(RA玩家列表编号, 如 3) ②玩家名(可只写一部分) ③Steam64/UserId;" +
            "①②需在线, ③离线也能改。各写法可在 config.yml 里开关(lookup_by_seq_id / lookup_by_name / lookup_by_steam / name_lookup_contains)";

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
                sb.Append("EXPAS 经验扩展插件系统 v").Append(PluginVersion).Append('\n');
                sb.Append("用法: EXPAS <子命令> [参数]    输入 EXPAS <关键字> 可匹配(如 EXPAS se)\n");
                sb.Append("玩家ID: 序列ID(如 3) / 玩家名(可只写一部分) / Steam64 —— 写法可在配置里开关\n");
                sb.Append("常用: set|add  info  lock|unlock  title  audio  mvpmusic\n");
                sb.Append("其它: layout  whitelist  arsa  config  time  line  custom  mvp  top  api  save  help\n");
                sb.Append("字段(set|add): ").Append(string.Join(" ", HelpFields));
                sb.Append('\n').Append("示例: EXPAS set 3 level 225   |   EXPAS add 示例 points 300   |   EXPAS lock 76561198… 1   |   EXPAS arsa");
                return sb.ToString();
            }

            // ---- 有关键字: 匹配子命令 ----
            var hits = HelpItems.Where(i => MatchesKey(i.Keys, f)).ToList();

            // ---- 同时匹配 set|add 的字段名 ----
            var fieldHits = HelpFields.Where(x => x.Contains(f)).ToList();

            if (hits.Count == 0 && fieldHits.Count == 0)
            {
                return $"没有匹配「{filter}」的子命令或字段。\n" +
                       "输入 EXPAS 看总览; 常用: set|add info lock unlock title audio mvpmusic layout arsa help";
            }

            var sb2 = new StringBuilder($"匹配「{filter}」:");
            foreach (HelpItem i in hits)
                sb2.Append($"\n• {i.Usage}    —— {i.Desc}");
            if (fieldHits.Count > 0)
            {
                sb2.Append("\n字段: ").Append(string.Join(" / ", fieldHits));
                if (fieldHits.Contains("lock"))
                    sb2.Append("   (lock 取值: 0=none 1=punishment 2=selfban 3=inactive)");
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