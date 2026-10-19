// ============================================================================
//  EXPAS —— 语言与地区自动检测 / EXPAS — language and region auto-detection
//    · 中国区(TZ / 公网 IP) → 中文；其它地区 → English / · China region (TZ / public IP) → Chinese; other regions → English
//    · config.yml 的 language: auto|zh|en 可手动覆盖 / · language: auto|zh|en in config.yml can override manually
//    · 所有对外文案走 T("中文", "English")，配置注释在保存后按语言回写 / · All user-facing text goes through T("Chinese", "English"); config comments are rewritten in the current language after saving
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    internal static class ExpasLang
    {
        /// <summary>当前是否为中文(默认 true，检测失败时保持中文，与中国区默认一致)。 / Whether the current language is Chinese (default true; stays Chinese if detection fails, consistent with the China-region default).</summary>
        public static bool Chinese { get; private set; } = true;

        /// <summary>当前语言代码: zh / en。 / Current language code: zh / en.</summary>
        public static string Code { get { return Chinese ? "zh" : "en"; } }

        /// <summary>语言检测结果说明(写进日志)。 / Language detection result description (written to the log).</summary>
        public static string DetectedBy { get; private set; } = "default";

        /// <summary>取词: 中文环境返回 zh，其它返回 en。 / Pick text: returns zh in a Chinese environment, otherwise en.</summary>
        public static string T(string zh, string en)
        {
            return Chinese ? zh : en;
        }

        /// <summary>启动时初始化: 先按语言配置判定，auto 时按 时区 + 公网 IP 判定。 / Initialize at startup: first decide from the language config; for auto, decide by time zone + public IP.</summary>
        public static void Initialize(string mode, string configuredTimeZone)
        {
            string m = (mode ?? "auto").Trim().ToLowerInvariant();

            if (m == "zh" || m == "cn" || m == "chinese" || m == "中文")
            {
                Chinese = true;
                DetectedBy = "config:zh";
                return;
            }

            if (m == "en" || m == "english" || m == "英文")
            {
                Chinese = false;
                DetectedBy = "config:en";
                return;
            }

            // auto(以及无法识别的值): 时区优先给出离线结果，有网络再用公网 IP 复核 / auto (and unrecognized values): give an offline result from the time zone first, then verify with the public IP when the network is available
            bool tzChina = DetectByTimeZone(configuredTimeZone);
            string country = TryGetCountryCode();
            if (country != null)
            {
                Chinese = IsChineseRegion(country);
                DetectedBy = "ip:" + country + (tzChina == Chinese ? "" : " (tz overridden)");
            }
            else
            {
                Chinese = tzChina;
                DetectedBy = "tz:" + SafeLocalZoneId();
            }
        }

        private static string SafeLocalZoneId()
        {
            try { return TimeZoneInfo.Local.Id; }
            catch { return "unknown"; }
        }

        private static bool DetectByTimeZone(string configured)
        {
            try
            {
                if (IsChinaZoneId(TimeZoneInfo.Local.Id)) return true;
            }
            catch { }

            string c = (configured ?? string.Empty).ToLowerInvariant();
            return c.Contains("中国") || c.Contains("china") || c.Contains("北京") || c.Contains("beijing")
                || c.Contains("上海") || c.Contains("shanghai") || c.Contains("asia/shanghai");
        }

        private static bool IsChinaZoneId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string s = id.ToLowerInvariant();
            return s == "china standard time"
                || s == "asia/shanghai" || s == "asia/urumqi" || s == "asia/chongqing"
                || s == "asia/harbin" || s == "asia/kashgar"
                || s == "prc" || s == "roc";
        }

        /// <summary>中国区(含港澳台，均为中文玩家区)。 / Chinese region (including HK/Macau/Taiwan; all are Chinese-speaking player regions).</summary>
        private static bool IsChineseRegion(string countryCode)
        {
            string c = (countryCode ?? string.Empty).ToUpperInvariant();
            return c == "CN" || c == "HK" || c == "MO" || c == "TW";
        }

        /// <summary>
        /// 公网 IP 归属地查询: 超时即放弃(离线服务器完全走时区判定)。 / Public IP location lookup: give up on timeout (offline servers rely entirely on time-zone detection).
        /// 只用纯文本/极简 JSON 接口，不引入任何依赖。 / Uses only plain-text/minimal JSON endpoints, introducing no dependencies.
        /// </summary>
        private static string TryGetCountryCode()
        {
            string[] urls =
            {
                "https://ipapi.co/country/",
                "http://ip-api.com/json/?fields=countryCode"
            };
            foreach (string url in urls)
            {
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 1200;
                    req.ReadWriteTimeout = 1200;
                    req.UserAgent = "EXPAS/" + ExpasPlugin.PluginVersion;
                    using (WebResponse resp = req.GetResponse())
                    using (Stream stream = resp.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd().Trim();
                        if (body.Length == 2) return body.ToUpperInvariant();
                        Match match = Regex.Match(body, "\"countryCode\"\\s*:\\s*\"([A-Za-z]{2})\"");
                        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
                    }
                }
                catch
                {
                    // 无网络/被墙/超时 → 尝试下一个，最终回退时区判定 / No network/blocked/timeout → try the next one, ultimately falling back to time-zone detection
                }
            }
            return null;
        }

        // ==================== 配置文本 ==================== / Config text

        /// <summary>
        /// 把内置中文默认文案换成对应语言(只替换"仍是内置默认值"的项，不会覆盖管理员自定义内容)。 / Replace built-in Chinese default text with the corresponding language (only items still at built-in defaults; admin custom content is never overwritten).
        /// 返回是否有改动。 / Returns whether anything changed.
        /// </summary>
        public static bool ApplyTextDefaults(ExpasPlugin.PluginConfig c)
        {
            if (c == null) return false;
            bool changed = false;

            c.ServerName = SwapValue(c.ServerName, "服务器", "Server", ref changed);
            c.PlaceholderHelp = SwapValue(c.PlaceholderHelp,
                "上面这些占位符可以写在任意一行模板或自定义行里",
                "These placeholders can be used in any HUD template or custom line", ref changed);

            c.HudLine1 = SwapValue(c.HudLine1,
                "<color=#4FA8FF>欢迎 {name} 进入到 {server}</color>",
                "<color=#4FA8FF>Welcome {name} to {server}</color>", ref changed);
            c.HudLine2 = SwapValue(c.HudLine2,
                "当前已经游玩了 {days} 天  当前时间段是 {tz} {clock}  您准确的游玩时间是 {playtime}",
                "Played {days} days  Local time {tz} {clock}  Total playtime {playtime}", ref changed);
            c.HudLine3 = SwapValue(c.HudLine3,
                "当前回合已造成 {roundDmg}  当前使用的角色已造成 {roleDmg}  总伤害为 {totalDmg}",
                "Round damage {roundDmg}  Role damage {roleDmg}  Total damage {totalDmg}", ref changed);
            c.HudLevel1 = SwapValue(c.HudLevel1,
                "<color=#4FA8FF>您当前有 {level} 级</color>",
                "<color=#4FA8FF>Level {level}</color>", ref changed);
            c.HudLevel2 = SwapValue(c.HudLevel2,
                "距离下一次升级还差 {expNeed} 经验",
                "{expNeed} EXP to next level", ref changed);
            c.HudCollapsedLine = SwapValue(c.HudCollapsedLine,
                "<color=#4FA8FF>[EXPAS]</color> Lv.{level}  积分 {points}  伤害 {totalDmg}{icons}",
                "<color=#4FA8FF>[EXPAS]</color> Lv.{level}  Points {points}  DMG {totalDmg}{icons}", ref changed);

            c.KillLine1 = SwapValue(c.KillLine1,
                "<color=#FFD700>已击杀 {killRole}</color>",
                "<color=#FFD700>Killed {killRole}</color>", ref changed);
            c.KillLine2 = SwapValue(c.KillLine2,
                "<align=left>占比 {killShare}%</align><align=right>伤害 {killDamage}</align>",
                "<align=left>Share {killShare}%</align><align=right>Damage {killDamage}</align>", ref changed);
            c.KillLine3 = SwapValue(c.KillLine3,
                "获得积分 +{killPoints}   (累计 {points})",
                "Points +{killPoints}   (total {points})", ref changed);

            c.IconLockPunishment = SwapValue(c.IconLockPunishment, "[锁]", "[P]", ref changed);
            c.IconLockSelfBan = SwapValue(c.IconLockSelfBan, "[禁]", "[B]", ref changed);
            c.IconLockInactive = SwapValue(c.IconLockInactive, "[离]", "[I]", ref changed);

            // 音频调用声明的两条内置示例(管理员在 config.yml 里看到的默认值) / Two built-in examples of audio call declarations (the default values admins see in config.yml)
            if (c.AudioCalls != null)
            {
                for (int i = 0; i < c.AudioCalls.Count; i++)
                {
                    string item = c.AudioCalls[i];
                    item = SwapValue(item,
                        "示例call|例: 开门提示音|SpeakerToy/SpawnDummy: 全服广播该提示音; 需把 xxx.wav 放入 audio 目录",
                        "examplecall|e.g. door chime|SpeakerToy/SpawnDummy: broadcast server-wide; put xxx.wav into the audio directory",
                        ref changed);
                    item = SwapValue(item,
                        "API 调用说明|第三方插件可用 ExpasApi.PlayAudioFile(file)/PlayMvpMusic(userId)/StopAudio()|例: ExpasApi.PlayAudioFile(\"ambient.wav\") 全服播放 audio\\ambient.wav; PlayMvpMusic(steam64) 播放该玩家命中的第一名音乐盒",
                        "API usage|third-party plugins can call ExpasApi.PlayAudioFile(file)/PlayMvpMusic(userId)/StopAudio()|e.g. ExpasApi.PlayAudioFile(\"ambient.wav\") plays audio\\ambient.wav server-wide; PlayMvpMusic(steam64) plays that Player's MVP Music",
                        ref changed);
                    c.AudioCalls[i] = item;
                }
            }

            return changed;
        }

        private static string SwapValue(string value, string zh, string en, ref bool changed)
        {
            string from = Chinese ? en : zh;
            string to = Chinese ? zh : en;
            if (string.Equals(value, from, StringComparison.Ordinal))
            {
                changed = true;
                return to;
            }
            return value;
        }

        /// <summary>
        /// 把 config.yml 里的注释按当前语言回写(中文 ↔ English)。 / Rewrite comments in config.yml in the current language (Chinese ↔ English).
        /// LabAPI 生成注释时用的是 [Description] 原文(中文)，这里做一次就地翻译。 / LabAPI generates comments from the original [Description] text (Chinese); this translates them in place.
        /// </summary>
        public static void LocalizeConfigFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                bool hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                string text = new UTF8Encoding(false).GetString(raw, hasBom ? 3 : 0, raw.Length - (hasBom ? 3 : 0));
                string[] lines = text.Split(new char[] { '\n' });
                bool changed = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    Match m = Regex.Match(line, "^(\\s*)#\\s?(.*?)\\s*$");
                    if (!m.Success) continue;

                    string body = m.Groups[2].Value;
                    string target = Chinese ? ToChinese(body) : ToEnglish(body);
                    if (target == null || target == body) continue;

                    lines[i] = m.Groups[1].Value + "# " + target;
                    changed = true;
                }

                if (changed)
                    File.WriteAllText(path, string.Join("\n", lines), new UTF8Encoding(hasBom));
            }
            catch
            {
                // 配置注释只是可读性增强，失败不影响插件运行 / Config comments only improve readability; failure does not affect plugin operation
            }
        }

        private static string ToEnglish(string body)
        {
            string en;
            return ExpasConfigText.Comments.TryGetValue(body, out en) ? en : null;
        }

        private static string ToChinese(string body)
        {
            foreach (KeyValuePair<string, string> pair in ExpasConfigText.Comments)
            {
                if (string.Equals(pair.Value, body, StringComparison.Ordinal)) return pair.Key;
            }
            return null;
        }
    }
}