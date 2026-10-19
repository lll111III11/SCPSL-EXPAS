// ============================================================================
// EXPAS —— 文本模板层 + 本回合 API (partial + 公开静态类) / EXPAS — text template layer + this-round API (partial + public static class)
//   1) HUD 所有文字都来自配置文件模板, 可随意改, 服务器重启后重新读取生效: / 1) All HUD text comes from config file templates; edit freely and it reloads after server restart:
//        HudLine1..HudLine4 / HudLevel1 / HudLevel2 / CustomLines(列表) / HudLine1..HudLine4 / HudLevel1 / HudLevel2 / CustomLines (list)
//      占位符: {name} {server} {days} {hours} {tz} {clock} {playtime} / Placeholders: {name} {server} {days} {hours} {tz} {clock} {playtime}
//              {roundDmg} {roleDmg} {totalDmg} {totalExp}
//              {level} {exp} {expNeed} {kills} {deaths}
//              {version} {icons} {mvpName} {mvpDamage} {mvpLevel} {topList}
//       v1.6 新增: {aspect} 屏幕宽高比标签(16:9 / 16:10 / 21:9 / 4:3 / 其它(x.xx); 取不到时 "-") / v1.6 new: {aspect} screen aspect-ratio label (16:9 / 16:10 / 21:9 / 4:3 / other (x.xx); "-" if unavailable)
//              —— 服务端**读不到像素分辨率**, 只能读宽高比/视野(见 AspectRatioSync 源码 G_14903/G_14915/G_14956) / — the server cannot read pixel resolution, only aspect ratio/FOV (see AspectRatioSync source G_14903/G_14915/G_14956)
//       v1.4 新增(回合/服务器): {roundTime} 本回合已进行时长  {aliveCount} 当前存活玩家数 / v1.4 new (round/server): {roundTime} elapsed time this round  {aliveCount} current alive player count
//              {tps} 服务器 TPS  {scpCount} 存活 SCP 数  {roundId} 本回合序号(服务器启动起算) / {tps} server TPS  {scpCount} alive SCP count  {roundId} this round's number (since server start)
//       v1.5 新增(击杀确认): {killRole} 被击杀的角色(RoleTypeId 名, 例 NtfSergeant) / v1.5 new (kill confirmation): {killRole} the killed role (RoleTypeId name, e.g. NtfSergeant)
//              {killRoleKind} 该角色类型(人形 / SCP / 人机 / 其它) / {killRoleKind} that role kind (human / SCP / dummy / other)
//              —— 另新增"击杀块并入 HUD"机制: 见文件末尾 KillNoticeBlock 与 ExpasPlugin.SettlePending / — also added the "kill block merged into HUD" mechanism: see KillNoticeBlock at the end of the file and ExpasPlugin.SettlePending
//       (只增加"可用占位符", HUD 默认模板原样不动, 玩家看到的界面不变) / (only adds "available placeholders"; the default HUD template is untouched and players see no UI change)
//   2) 本回合统计(伤害/经验/击杀) → 排行榜 / MVP / 2) This round's stats (damage/EXP/kills) → leaderboard / MVP
//   3) 公开 API(ExpasApi): 其他插件(例如 MVP 音乐扩展)可查询局内最高分等信息, / 3) Public API (ExpasApi): other plugins (e.g. MVP music extensions) can query round top score and other info,
//      并订阅 RoundMvpSettled 事件; 由配置 ApiEnabled 总开关控制是否开放。 / and subscribe to the RoundMvpSettled event; gated by the ApiEnabled config switch.
// ============================================================================
using LabApi.Features.Wrappers;
using RoundRestarting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using L = Expas.ExpasLang;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    /// <summary>本回合某玩家的统计条目(公开给其它插件用)。 / This round's stats entry for a player (exposed to other plugins).</summary>
    public class RoundEntry
    {
        public string UserId = string.Empty;
        public string Name = string.Empty;
        public float Damage = 0f;      // 本回合造成伤害 / Damage dealt this round
        public float Exp = 0f;         // 本回合获得经验 / EXP gained this round
        public int Kills = 0;          // 本回合击杀 / Kills this round
        public int Level = 1;          // 当前等级 / Current level
    }

    /// <summary>
    /// EXPAS 对外 API —— 供其它插件做 MVP/音乐等扩展。 / EXPAS public API — for other plugins to build MVP/music extensions.
    /// ApiEnabled=false 时全部返回空/0, 不暴露任何数据。 / When ApiEnabled=false everything returns empty/0; no data is exposed.
    /// </summary>
    public static class ExpasApi
    {
        /// <summary>是否已开放本回合 API(由配置 ApiEnabled 控制)。 / Whether this round's API is enabled (controlled by the ApiEnabled config).</summary>
        public static bool Enabled
        {
            get
            {
                ExpasPlugin pl = ExpasPlugin.Instance;
                return pl != null && pl.Config.ApiEnabled;
            }
        }

        /// <summary>回合结算出 MVP 时触发(可用来放音乐/公告)。 / Fired when the round MVP is settled (can be used for music/announcements).</summary>
        public static event Action<RoundEntry> RoundMvpSettled;

        /// <summary>本回合排行榜(按造成伤害降序), count<=0 表示全部。 / This round's leaderboard (sorted by damage dealt, descending); count<=0 means all.</summary>
        public static List<RoundEntry> GetRoundTop(int count = 5)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.BuildRoundTop(count)
                : new List<RoundEntry>();

        /// <summary>本回合 MVP(伤害最高者); 没有数据时返回 null。 / This round's MVP (highest damage); returns null when there is no data.</summary>
        public static RoundEntry GetRoundMvp()
        {
            List<RoundEntry> top = GetRoundTop(1);
            return top.Count > 0 ? top[0] : null;
        }

        public static float GetRoundDamage(string userId)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.GetRoundDamageOf(userId) : 0f;

        public static float GetRoundExp(string userId)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.GetRoundExpOf(userId) : 0f;

        public static float GetTotalDamage(string userId)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.GetTotalDamageOf(userId) : 0f;

        public static int GetLevel(string userId)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.GetLevelOf(userId) : 0;

        public static float GetExp(string userId)
            => ExpasPlugin.Instance != null && ExpasPlugin.Instance.Config.ApiEnabled
                ? ExpasPlugin.Instance.GetExpOf(userId) : 0f;

        /// <summary>给某个玩家加经验(同样受锁定状态与等级上限约束)。 / Add EXP to a player (also subject to lock state and the level cap).</summary>
        public static void AddExp(string userId, float amount)
        {
            ExpasPlugin pl = ExpasPlugin.Instance;
            if (pl == null || !pl.Config.ApiEnabled || string.IsNullOrEmpty(userId)) return;
            ExpasPlugin.PlayerData d = pl.GetDataById(userId);
            if (d == null || d.LockState == ExpasPlugin.LockPunishment) return;   // 🚫 惩罚锁定: 不入账 / Punishment lock: not credited
            if (d.LockState == ExpasPlugin.LockSelfBan)
            {
                // 🔒 自助封禁: 经验进待入账池(解除时一次性入账) / Self-ban: EXP goes to the pending pool (credited in one go when lifted)
                d.PendingExp += amount;
                pl.SavePlayer(d);
                return;
            }
            pl.AddExp(d, amount);
        }

        internal static void FireMvp(RoundEntry e)
        {
            try { RoundMvpSettled?.Invoke(e); } catch { }
        }

        // ========================================================================
        // v2.0 NP 音频公开 API —— 供第三方插件调用(受配置 audio_playback_plan / mvp_music_enabled 约束) / v2.0 NP audio public API — for third-party plugins (subject to audio_playback_plan / mvp_music_enabled config)
        //   用法示例: / Usage examples:
        //     ExpasApi.PlayAudioFile("ambient.wav");        // 全服播放 audio\ambient.wav / play audio\ambient.wav server-wide
        //     ExpasApi.PlayAudioFile(@"D:\x\y.wav");        // 也支持绝对路径 / absolute paths are also supported
        //     ExpasApi.PlayMvpMusic("76561198000000000@steam");  // 播放该玩家命中的第一名音乐盒 / play the MVP music matched for that player
        //     ExpasApi.StopAudio();
        // ========================================================================

        /// <summary>音频系统是否可用(插件已启用 且 audio_playback_plan != 0)。 / Whether the audio system is available (plugin enabled and audio_playback_plan != 0).</summary>
        public static bool AudioEnabled
        {
            get
            {
                ExpasPlugin pl = ExpasPlugin.Instance;
                return pl != null && pl.Config.AudioPlaybackPlan != ExpasPlugin.AudioPlanOff;
            }
        }

        /// <summary>播放 audio\ 下的音频文件(或绝对路径)。返回是否成功发起播放。 / Play an audio file under audio\ (or an absolute path). Returns whether playback was successfully started.</summary>
        public static bool PlayAudioFile(string file)
        {
            ExpasPlugin pl = ExpasPlugin.Instance;
            if (pl == null || !AudioEnabled || string.IsNullOrWhiteSpace(file)) return false;
            string path = System.IO.Path.IsPathRooted(file) ? file : System.IO.Path.Combine(pl.AudioDir, file);
            return pl.PlayAudioPath(path);
        }

        /// <summary>按 Steam64 播放"第一名音乐盒"(命中 mvp_music_players 才有音频)。 / Play "MVP music" by Steam64 (audio exists only if matched in mvp_music_players).</summary>
        public static bool PlayMvpMusic(string userId)
        {
            ExpasPlugin pl = ExpasPlugin.Instance;
            if (pl == null) return false;
            return pl.PlayMvpMusicFor(userId);
        }

        /// <summary>停止当前音频播放(销毁隐藏音箱)。 / Stop current audio playback (destroys the hidden speaker).</summary>
        public static void StopAudio()
        {
            try { ExpasPlugin.Instance?.StopAudio(); } catch { }
        }

        /// <summary>当前正在播放的音频文件名(空 = 未播放)。 / File name of the currently playing audio (empty = not playing).</summary>
        public static string CurrentAudio
            => ExpasPlugin.Instance != null ? ExpasPlugin.Instance.AudioCurrent : string.Empty;
    }

    public partial class ExpasPlugin
    {
        // ==================== 本回合统计 ==================== / This round's stats
        private readonly Dictionary<string, float> _roundExp = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _roundKills = new Dictionary<string, int>();

        internal void RecordRoundExp(string userId, float exp)
        {
            if (string.IsNullOrEmpty(userId) || exp <= 0f) return;
            _roundExp[userId] = (_roundExp.TryGetValue(userId, out float v) ? v : 0f) + exp;
        }

        internal void RecordRoundKill(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            _roundKills[userId] = (_roundKills.TryGetValue(userId, out int v) ? v : 0) + 1;
        }

        public float GetRoundDamageOf(string userId) => GetRoundDmg(userId);
        public float GetRoundExpOf(string userId) => _roundExp.TryGetValue(userId, out float v) ? v : 0f;
        public float GetTotalDamageOf(string userId) => GetDataById(userId)?.TotalDamage ?? 0f;
        public int GetLevelOf(string userId) => GetDataById(userId)?.Level ?? 0;
        public float GetExpOf(string userId) => GetDataById(userId)?.Exp ?? 0f;

        /// <summary>本回合排行榜(按伤害降序)。 / This round's leaderboard (sorted by damage, descending).</summary>
        public List<RoundEntry> BuildRoundTop(int count)
        {
            var list = new List<RoundEntry>();
            var seen = new HashSet<string>();

            foreach (var kv in _roundDamage)
            {
                PlayerData d = _cache.TryGetValue(kv.Key, out PlayerData dd) ? dd : null;
                list.Add(MakeEntry(kv.Key, d, kv.Value));
                seen.Add(kv.Key);
            }
            foreach (var kv in _roundExp)
            {
                if (seen.Contains(kv.Key)) continue;
                PlayerData d = _cache.TryGetValue(kv.Key, out PlayerData dd) ? dd : null;
                list.Add(MakeEntry(kv.Key, d, 0f));
            }

            list.Sort((a, b) => b.Damage.CompareTo(a.Damage));
            if (count > 0 && list.Count > count) list = list.GetRange(0, count);
            return list;
        }

        private RoundEntry MakeEntry(string userId, PlayerData d, float damage)
        {
            return new RoundEntry
            {
                UserId = userId,
                Name = d != null && !string.IsNullOrEmpty(d.Name) ? d.Name : userId,
                Damage = damage,
                Exp = _roundExp.TryGetValue(userId, out float e) ? e : 0f,
                Kills = _roundKills.TryGetValue(userId, out int k) ? k : 0,
                Level = d != null ? d.Level : 1
            };
        }

        /// <summary>回合结束: 结算 MVP → 公告 + 触发 API 事件(供音乐扩展)。 / Round end: settle MVP → announce + fire the API event (for music extensions).</summary>
        private void SettleRoundMvp()
        {
            try
            {
                List<RoundEntry> top = BuildRoundTop(Mathf.Max(1, Config.TopCount));
                if (top.Count == 0 || top[0].Damage <= 0f) return;

                RoundEntry mvp = top[0];
                string msg = Config.MvpAnnounce;
                if (!string.IsNullOrEmpty(msg))
                {
                    msg = msg.Replace("{mvpName}", mvp.Name)
                             .Replace("{mvpDamage}", mvp.Damage.ToString("F0"))
                             .Replace("{mvpLevel}", mvp.Level.ToString())
                             .Replace("{topList}", BuildTopListText(top));
                    Server.SendBroadcast(msg, 10);
                }
                // v2.0 NP 第一名音乐盒: 回合结束 → 检测伤害第一(mvp)的 Steam ID 是否命中配置表 → 播其专属音频 / v2.0 NP MVP music: at round end → check whether the top-damage (MVP) player's Steam ID is in the config table → play their own audio
                //   (位置: 公告之后、FireMvp 之前; 播放走隐藏音箱: 不生成假人) / (position: after the announcement, before FireMvp; playback uses the hidden speaker: creates no dummy)
                try { PlayMvpMusicFor(mvp.UserId); }
                catch (Exception ex) { Logger.Warn("[EXPAS] " + L.T($"第一名音乐盒触发失败: {ex.Message}", $"MVP Music trigger failed: {ex.Message}")); }

                ExpasApi.FireMvp(mvp);

                // ================= v1.4 结算语音播报 —— 入口是 Announcer(不是 Cassie)(默认关: MvpCassie 为空 → 完全不播, 保持安静) ================= / v1.4 settlement voice announcement — entry point is Announcer (not Cassie) (off by default: empty MvpCassie → no playback at all, stays quiet)
                //   正确入口: LabApi.Features.Wrappers.Announcer.Message(string message, string customSubtitles = "", / Correct entry point: LabApi.Features.Wrappers.Announcer.Message(string message, string customSubtitles = "",
                //     bool playBackground = true, float priority = 0f, float glitchScale = 1f)
                //     —— 类 Announcer(L_3512), 方法本体 L_3569。 / — class Announcer (L_3512), method body L_3569.
                //   ★ 不能走 Cassie: 整个 LabApi.Features.Wrappers.Cassie 类已被 / ★ Cassie cannot be used: the entire LabApi.Features.Wrappers.Cassie class is
                //     [Obsolete("Use Announcer instead of Cassie.", true)] 标记(L_3683-3684), 直接调用会 CS0619 编译失败; / marked [Obsolete("Use Announcer instead of Cassie.", true)] (L_3683-3684); a direct call fails to compile with CS0619;
                //     其每个成员也都各自标了 "Use Announcer.xxx instead."(L_3686 起)。 / each of its members is also marked "Use Announcer.xxx instead." (from L_3686).
                //     所以本文件/文档里凡是写"Cassie 广播/播报"的地方一律按 Announcer 理解 / so wherever this file/docs say "Cassie broadcast/announcement", always read it as Announcer
                //     —— 文档 02_核心API调用速查 L226-L228 写的是 Cassie.Message(...), 那份写法已过时(以源码为准)。 / — docs 02_核心API调用速查 L226-L228 write Cassie.Message(...), which is outdated (the source is authoritative).
                //   这里第二个参数固定传 string(字幕), 以避开 Announcer 里同样被 [Obsolete(..., true)] 标记的 / Here the second parameter is always the string (subtitles) overload, avoiding the Announcer overload also marked [Obsolete(..., true)]:
                //     Message(string, bool isHeld, bool isNoisy, bool isSubtitles, string customSubtitles) 重载(L_3564)。 / Message(string, bool isHeld, bool isNoisy, bool isSubtitles, string customSubtitles) (L_3564).
                //   语音库只念英文单词与数字, 中文名字读不出来 → 中文内容请写在 MvpCassieSubtitle 里当字幕。 / The voice library only speaks English words and digits, so Chinese names cannot be read → put Chinese content in MvpCassieSubtitle as subtitles.
                //   【本轮不做】音频类钩子(SpeakerToy + AudioTransmitter, 48kHz / 480帧 / 单声道 float[], / [Not in this round] audio hooks (SpeakerToy + AudioTransmitter, 48kHz / 480 frames / mono float[],
                //     见文档 16_更多机制详解)需要自备音频素材, 留待后续版本。 / see docs 16_更多机制详解) require your own audio assets and are left for a later version.
                if (!string.IsNullOrEmpty(Config.MvpCassie))
                {
                    string cassie = Config.MvpCassie
                        .Replace("{mvpName}", mvp.Name)
                        .Replace("{mvpDamage}", mvp.Damage.ToString("F0"))
                        .Replace("{mvpLevel}", mvp.Level.ToString())
                        .Replace("{topList}", BuildTopListText(top));
                    // 必须写全名: 游戏本体里也有个 Cassie 命名空间(Assembly-CSharp G_176315), 短名会被解析成命名空间 / Must use the full name: the game itself also has a Cassie namespace (Assembly-CSharp G_176315), so the short name resolves to the namespace
                    LabApi.Features.Wrappers.Announcer.Message(cassie, Config.MvpCassieSubtitle ?? string.Empty);
                    Logger.Info("[EXPAS] " + L.T($"Announcer 结算播报: {cassie}", $"Announcer settlement broadcast: {cassie}"));
                }

                Logger.Info("[EXPAS] " + L.T($"本回合 MVP: {mvp.Name} 伤害{mvp.Damage:F0} 经验{mvp.Exp:F0}",
                                              $"Round MVP: {mvp.Name} Damage {mvp.Damage:F0} EXP {mvp.Exp:F0}"));
            }
            catch (Exception ex) { Logger.Warn("[EXPAS] " + L.T($"MVP 结算失败: {ex.Message}", $"MVP settlement failed: {ex.Message}")); }
        }

        private string BuildTopListText(List<RoundEntry> top)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append($"{i + 1}.{top[i].Name} {top[i].Damage:F0}");
            }
            return sb.ToString();
        }

        // ========================================================================
        // 文本模板渲染 / Text template rendering
        // ========================================================================
        private string Tok(string template, Player p, PlayerData d)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            double playSec = d.PlaySeconds;
            if (_sessionStart.TryGetValue(d.UserId, out DateTime st))
                playSec += (DateTime.UtcNow - st).TotalSeconds;

            float need = ExpNeeded(d.Level);
            List<RoundEntry> top = Config.ApiEnabled ? BuildRoundTop(1) : new List<RoundEntry>();
            RoundEntry mvp = top.Count > 0 ? top[0] : null;

            return template
                .Replace("{name}", p != null ? p.Nickname : d.Name)
                .Replace("{server}", Config.ServerName)
                .Replace("{days}", DaysText(d))
                .Replace("{hours}", (playSec / 3600.0).ToString("F2"))
                .Replace("{tz}", GetTimeZoneLabel())
                .Replace("{clock}", GetLocalTimeText())
                .Replace("{playtime}", PlaytimeText(playSec))
                .Replace("{roundDmg}", GetRoundDmg(d.UserId).ToString("F0"))
                .Replace("{roleDmg}", GetRoleDmg(d.UserId).ToString("F0"))
                .Replace("{totalDmg}", d.TotalDamage.ToString("F0"))
                .Replace("{totalExp}", d.TotalExp.ToString("F0"))
                .Replace("{level}", d.Level.ToString())
                .Replace("{exp}", d.Exp.ToString("F0"))
                .Replace("{expNeed}", Mathf.Max(0f, need - d.Exp).ToString("F0"))
                .Replace("{kills}", d.Kills.ToString())
                .Replace("{deaths}", d.Deaths.ToString())
                .Replace("{version}", PluginVersion)
                .Replace("{icons}", GetIconString(d))
                // ===== v1.6 屏幕宽高比标签(服务端只能读宽高比/视野, 读不到像素分辨率) ===== / v1.6 screen aspect-ratio label (the server can only read aspect ratio/FOV, not pixel resolution)
                .Replace("{aspect}", GetAspectLabel(p))
                .Replace("{mvpName}", mvp != null ? mvp.Name : "-")
                .Replace("{mvpDamage}", mvp != null ? mvp.Damage.ToString("F0") : "0")
                .Replace("{mvpLevel}", mvp != null ? mvp.Level.ToString() : "-")
                .Replace("{topList}", BuildTopListText(BuildRoundTop(Config.TopCount)))
                // ===== v1.3 积分 / 最近一次击杀 ===== / v1.3 points / latest kill
                .Replace("{points}", d.Points.ToString("F0"))
                .Replace("{roundPoints}", GetRoundPointsOf(d.UserId).ToString("F0"))
                .Replace("{killName}", d.LastKillName ?? "-")
                .Replace("{killDamage}", d.LastKillDamage.ToString("F0"))
                .Replace("{killShare}", d.LastKillShare.ToString("F1"))
                .Replace("{killPoints}", d.LastKillPoints.ToString("F0"))
                // ===== v1.5 击杀确认: 被击杀的角色(RoleTypeId 名)与它的类型 ===== / v1.5 kill confirmation: the killed role (RoleTypeId name) and its kind
                .Replace("{killRole}", d.LastKillRole ?? "-")
                .Replace("{killRoleKind}", d.LastKillRoleKind ?? "-")
                // ===== v1.4 回合 / 服务器信息 ===== / v1.4 round / server info
                .Replace("{roundTime}", RoundTimeText())
                .Replace("{aliveCount}", AlivePlayerCount().ToString())
                .Replace("{tps}", ServerTpsText())
                .Replace("{scpCount}", ScpTargetsCount().ToString())
                .Replace("{roundId}", CurrentRoundNumber().ToString());
        }

        // ========================================================================
        // v1.4 回合 / 服务器占位符取值 / v1.4 round / server placeholder values
        //   依据: 文档 02_核心API调用速查 L11/L19(Player.List / Player.Count)、 / Basis: docs 02_核心API调用速查 L11/L19 (Player.List / Player.Count),
        //         L106-L111(Round.Duration / ScpTargetsAmount)、L140(Server.Tps) 与 / L106-L111 (Round.Duration / ScpTargetsAmount), L140 (Server.Tps) and
        //         文档 09_完整示例插件源码 L140/L224(Server.Tps / Round.Duration.TotalSeconds 实跑输出); / docs 09_完整示例插件源码 L140/L224 (actual runtime output of Server.Tps / Round.Duration.TotalSeconds);
        //   源码: LabApi Round.Duration(L_7727)、Round.ScpTargetsAmount(L_7709)、 / Source: LabApi Round.Duration (L_7727), Round.ScpTargetsAmount (L_7709),
        //         Server.Tps(L_15769)、Player.List(L_14002)/Player.Count(L_14015); / Server.Tps (L_15769), Player.List (L_14002)/Player.Count (L_14015);
        //         游戏本体 RoundRestart.UptimeRounds(G_85713, 命名空间 RoundRestarting)。 / game itself RoundRestart.UptimeRounds (G_85713, namespace RoundRestarting).
        // ========================================================================

        /// <summary>本回合已进行时长(TimeSpan) → 复用插件既有 FormatDuration 文案。 / Elapsed time this round (TimeSpan) → reuses the plugin's existing FormatDuration text.</summary>
        private string RoundTimeText()
        {
            try { return FormatDuration(Round.Duration.TotalSeconds); } catch { return "-"; }
        }

        /// <summary>
        /// 当前存活玩家数: 遍历"真人玩家"(Player.ReadyList, L_14003 = IsDummy || (IsPlayer &amp;&amp; IsReady), / Current alive player count: iterates "real players" (Player.ReadyList, L_14003 = IsDummy || (IsPlayer &amp;&amp; IsReady),
        /// 即已把未认证连接排除掉)再按存活过滤。 / i.e. unauthenticated connections already excluded), then filters by alive state.
        /// (Player.Count 也是 ReadyList 的条数 L_14015, 但含人机且不判存活, 不适合直接当"存活人数"用。) / (Player.Count is also the ReadyList count L_14015, but it includes dummies and does not check alive, so it is not suitable directly as the "alive count".)
        /// </summary>
        private static int AlivePlayerCount()
        {
            int n = 0;
            foreach (Player p in Player.ReadyList.Where(p => p.IsPlayer && !p.IsDummy))
            {
                if (p == null || p.IsDestroyed) continue;
                if (p.IsAlive) n++;
            }
            return n;
        }

        /// <summary>服务器 TPS(LabApi Server.Tps, L_15769: 1/Time.smoothDeltaTime)。 / Server TPS (LabApi Server.Tps, L_15769: 1/Time.smoothDeltaTime).</summary>
        private static string ServerTpsText()
        {
            try { return Server.Tps.ToString("F0"); } catch { return "-"; }
        }

        /// <summary>
        /// 存活 SCP 数(LabApi Round.ScpTargetsAmount, L_7709)。 / Alive SCP count (LabApi Round.ScpTargetsAmount, L_7709).
        /// 源码口径: ReferenceHub.AllHubs 里 SCP 阵营数量 + Round.ExtraTargets —— 是"SCP 目标数", / Source semantics: SCP faction count in ReferenceHub.AllHubs + Round.ExtraTargets — this is the "SCP target count",
        /// 含 ExtraTargets 附加目标, 与界面上"存活 SCP 玩家数"可能略有出入(见报告)。 / includes ExtraTargets extra targets and may differ slightly from the "alive SCP player count" in the UI (see report).
        /// </summary>
        private static int ScpTargetsCount()
        {
            try { return Round.ScpTargetsAmount; } catch { return 0; }
        }

        /// <summary>
        /// 本回合序号(自服务器启动起算) = RoundRestart.UptimeRounds + 1。 / This round number (counted since server start) = RoundRestart.UptimeRounds + 1.
        /// LabApi 的 Round / Server 里没有回合编号; 游戏本体 RoundRestart.UptimeRounds(G_85713) / LabApi's Round / Server has no round number; the game's RoundRestart.UptimeRounds (G_85713)
        /// 每重开一次 +1(G_85854), 故当前回合 = 已完成重开数 + 1。 / increments by 1 on each restart (G_85854), so the current round = completed restarts + 1.
        /// </summary>
        private static int CurrentRoundNumber()
        {
            try { return RoundRestart.UptimeRounds + 1; } catch { return 1; }
        }

        // ==================== v1.3 本回合积分 ==================== / v1.3 this round's points
        private readonly Dictionary<string, float> _roundPoints = new Dictionary<string, float>();

        internal void RecordRoundPoints(string userId, float points)
        {
            if (string.IsNullOrEmpty(userId) || points <= 0f) return;
            _roundPoints[userId] = (_roundPoints.TryGetValue(userId, out float v) ? v : 0f) + points;
        }

        public float GetRoundPointsOf(string userId) => _roundPoints.TryGetValue(userId, out float v) ? v : 0f;

        /// <summary>游玩时间文案: PlaytimeMode 1=只按小时(两位小数), 0=自动天/小时/分/秒。 / Playtime text: PlaytimeMode 1=hours only (two decimals), 0=automatic days/hours/minutes/seconds.</summary>
        private string PlaytimeText(double seconds)
        {
            if (Config.PlaytimeMode == 1) return L.T($"{(seconds / 3600.0):F2} 小时", $"{(seconds / 3600.0):F2} hours");
            return FormatDuration(seconds);
        }

        /// <summary>
        /// 天数文案: DaysMode 1=按小时(一位小数), 0=天数(每天12点为界)。 / Days text: DaysMode 1=hours (one decimal), 0=days (new day at 12:00).
        /// v1.5: 天数改为优先用**可写字段 PlayedDays**(可由 EXPAS set/add ... played_days 修改); / v1.5: days now prefer the **writable field PlayedDays** (modifiable via EXPAS set/add ... played_days);
        ///        未设置(<=0)时按 v1.4 老口径从 FirstSeen 推算, 保持既有界面不变。 / when unset (<=0), derive from FirstSeen as in v1.4, keeping the existing UI unchanged.
        /// </summary>
        private string DaysText(PlayerData d)
        {
            if (Config.DaysMode == 1)
            {
                double h = Math.Max(0d, (DateTime.UtcNow - d.FirstSeen).TotalHours);
                return h.ToString("F1");
            }
            return (d.PlayedDays > 0 ? d.PlayedDays : DaysSince(d.FirstSeen, DateTime.UtcNow)).ToString();
        }

        // ========================================================================
        // 模板版 HUD 组装 / Template-based HUD assembly
        // ========================================================================
        private string BuildHudTemplated(Player p, PlayerData d)
        {
            var body = new StringBuilder();
            body.AppendLine(Tok(Config.HudLine1, p, d));
            body.AppendLine(Tok(Config.HudLine2, p, d));
            body.AppendLine(Tok(Config.HudLine3, p, d));
            body.AppendLine(Tok(Config.HudLine4, p, d));

            bool customBefore = Config.CustomLines != null && Config.CustomLines.Count > 0 && Config.CustomLinesPos == 0;
            if (customBefore) AppendCustomLines(body, p, d);

            // 等级块(默认右下角) / Level block (bottom-right by default)
            body.Append($"<align={AlignTag(GetLevelAlign(d))}>");
            body.AppendLine(Tok(Config.HudLevel1, p, d));
            body.Append(Tok(Config.HudLevel2, p, d));
            body.Append("</align>");

            if (Config.CustomLines != null && Config.CustomLines.Count > 0 && Config.CustomLinesPos == 1)
            {
                body.AppendLine();
                AppendCustomLines(body, p, d);
            }

            // v1.5 击杀确认(改造2): 未过期的击杀块追加在 HUD 文本末尾。 / v1.5 kill confirmation (rework 2): append the unexpired kill block at the end of the HUD text.
            //   为什么放这里而不是"另发一条 Hint": HUD 每 HudInterval 秒重建一次, 独立的 4 秒 Hint / Why here instead of "sending a separate Hint": the HUD rebuilds every HudInterval seconds, and a standalone 4-second Hint
            //   会被紧接着的下一次 HUD 刷新覆盖 → 玩家看不到击杀信息; 并入 HUD 才不被覆盖。 / would be overwritten by the next HUD refresh → players would not see the kill info; merging into the HUD prevents that.
            string killNotice = KillNoticeBlock(p, d);
            if (!string.IsNullOrEmpty(killNotice))
            {
                body.AppendLine();
                body.AppendLine(killNotice);
            }

            // 外层: 水平对齐 / 字号(字号×屏幕缩放) / 行距 / 垂直位置 / Outer layer: horizontal alignment / font size (font size × screen scale) / line height / vertical position
            var head = new StringBuilder();
            var tail = new StringBuilder();
            head.Append($"<align={AlignTag(GetHAlign(d))}>");
            tail.Insert(0, "</align>");

            if (Config.HudUseTags)
            {
                // v1.6: 最终字号 = 字号% × 屏幕缩放% × 分辨率档位倍率 × 整体缩放% ÷ 10000 (夹 20..240) / v1.6: final font size = size% × screen scale% × resolution preset multiplier × global scale% ÷ 10000 (clamped to 20..240)
                int sizePercent = FinalSizePercent(p, d);
                if (sizePercent != 100)
                {
                    head.Append(Config.HudSizeStyle == 1
                        ? $"<size={(sizePercent - 100 >= 0 ? "+" : string.Empty)}{sizePercent - 100}>"
                        : $"<size={sizePercent}%>");
                    tail.Insert(0, "</size>");
                }
                int lh = GetLineHeightPercent(d);
                if (lh != 100) { head.Append($"<line-height={lh}%>"); tail.Insert(0, "</line-height>"); }
                int vo = GetVOffset(d);
                if (vo != 0) { head.Append($"<voffset={vo}>"); tail.Insert(0, "</voffset>"); }
            }

            return head.ToString() + body + tail.ToString();
        }

        // ========================================================================
        // v1.6 HUD 折叠 —— 屏幕 HUD 太占位置时的"只显示一行"模式 / v1.6 HUD collapse — the "show only one line" mode when the screen HUD takes too much space
        //   折叠时: 只渲染 config.yml 的 HudCollapsedLine 一行(默认 / When collapsed: render only the single HudCollapsedLine line from config.yml (default
        //     "<color=#4FA8FF>[EXPAS]</color> Lv.{level}  积分 {points}  伤害 {totalDmg}{icons}"), / "<color=#4FA8FF>[EXPAS]</color> Lv.{level}  Points {points}  DMG {totalDmg}{icons}"),
        //   **击杀块仍然显示**(击杀确认不能跟着折掉) —— 与展开模式同一套 KillNoticeBlock 逻辑。 / **the kill block is still shown** (kill confirmation must not be collapsed away) — same KillNoticeBlock logic as expanded mode.
        //   展开时 BuildHud/BuildHudTemplated 的行为**完全不变**。 / When expanded, BuildHud/BuildHudTemplated behavior is **completely unchanged**.
        // ========================================================================
        private string BuildHudCollapsed(Player p, PlayerData d)
        {
            var body = new StringBuilder();
            body.Append(Tok(Config.HudCollapsedLine, p, d));

            // 击杀块照旧追加(与展开模式同一处理) / Kill block appended as usual (same handling as expanded mode)
            string killNotice = KillNoticeBlock(p, d);
            if (!string.IsNullOrEmpty(killNotice))
            {
                body.AppendLine();
                body.Append(killNotice);
            }
            return HudTags(p, d, body.ToString());
        }

        /// <summary>
        /// 屏幕 HUD 的外层标签(水平对齐 / 字号 / 行距 / 垂直位置)。 / Outer tags for the screen HUD (horizontal alignment / font size / line height / vertical position).
        /// 与既有 BuildHud / BuildHudTemplated 里的写法完全一致, 抽出来给折叠单行复用; / Exactly the same as in the existing BuildHud / BuildHudTemplated, extracted for reuse by the collapsed single line;
        /// 某值等于默认(100% / 0)时**不输出**对应标签(可完全回落成原生渲染)。 / when a value equals the default (100% / 0), the corresponding tag is **not emitted** (can fully fall back to native rendering).
        /// v1.7: includeVOffset=false 时不写外层 &lt;voffset&gt; —— 布局模式由每个块自己带垂直偏移 / v1.7: when includeVOffset=false, do not write the outer &lt;voffset&gt; — layout mode carries vertical offsets per block
        ///       (避免 &lt;voffset&gt; 嵌套; 嵌套在 TMP 里是"内层覆盖外层", 语义容易踩坑)。 / (avoid nested &lt;voffset&gt;; in TMP, nesting means "inner overrides outer", which is easy to get wrong).
        /// </summary>
        private string HudTags(Player p, PlayerData d, string body, bool includeVOffset = true)
        {
            var head = new StringBuilder();
            var tail = new StringBuilder();
            head.Append($"<align={AlignTag(GetHAlign(d))}>");
            tail.Insert(0, "</align>");

            if (Config.HudUseTags)
            {
                int sizePercent = FinalSizePercent(p, d);
                if (sizePercent != 100)
                {
                    head.Append(Config.HudSizeStyle == 1
                        ? $"<size={(sizePercent - 100 >= 0 ? "+" : string.Empty)}{sizePercent - 100}>"
                        : $"<size={sizePercent}%>");
                    tail.Insert(0, "</size>");
                }
                int lh = GetLineHeightPercent(d);
                if (lh != 100) { head.Append($"<line-height={lh}%>"); tail.Insert(0, "</line-height>"); }
                int vo = GetVOffset(d);
                if (includeVOffset && vo != 0) { head.Append($"<voffset={vo}>"); tail.Insert(0, "</voffset>"); }
            }
            return head.ToString() + body + tail.ToString();
        }

        private void AppendCustomLines(StringBuilder sb, Player p, PlayerData d)
        {
            foreach (string line in Config.CustomLines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                sb.AppendLine(Tok(line, p, d));
            }
        }

        // ========================================================================
        // 面板文本(不带 <align>/<size> 这些屏幕标签, 直接给 SSS 文本区用) / Panel text (without screen tags like <align>/<size>, used directly by the SSS text area)
        // ========================================================================
        private string BuildPanelHudText(Player p, PlayerData d)
        {
            if (!Config.UseTextTemplates) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine(Tok(Config.HudLine1, p, d));
            sb.AppendLine(Tok(Config.HudLine2, p, d));
            sb.AppendLine(Tok(Config.HudLine3, p, d));
            sb.AppendLine(Tok(Config.HudLine4, p, d));
            if (Config.CustomLines != null)
                foreach (string l in Config.CustomLines)
                    if (!string.IsNullOrEmpty(l)) sb.AppendLine(Tok(l, p, d));
            sb.AppendLine(Tok(Config.HudLevel1, p, d));
            sb.Append(Tok(Config.HudLevel2, p, d));
            return sb.ToString();
        }

        /// <summary>击杀/积分文本区(独立注册的 UI, 与状态区隔离)。 / Kill/points text area (separately registered UI, isolated from the status area).</summary>
        private string BuildKillFeedText(Player p, PlayerData d)
        {
            return Tok(Config.KillLine1, p, d) + "\n" + Tok(Config.KillLine2, p, d) + "\n" + Tok(Config.KillLine3, p, d);
        }

        // ========================================================================
        // v1.5 击杀确认 —— 击杀块并入 HUD(改造2) / v1.5 kill confirmation — kill block merged into HUD (rework 2)
        //   流程图: 结算(SettlePending) → 渲染击杀块并记时间戳 → BuildHud/BuildHudTemplated / Flow: settle (SettlePending) → render kill block and record timestamp → BuildHud/BuildHudTemplated
        //           在 KillNoticeSeconds 秒内把击杀块追加到 HUD 末尾 → 过期自动消失。 / append the kill block to the end of the HUD within KillNoticeSeconds seconds → disappears automatically after expiry.
        //   文本在结算那一刻就渲染好并缓存, 所以 {killShare}/{killPoints}/{points} 定格在本次击杀。 / Text is rendered and cached at settlement time, so {killShare}/{killPoints}/{points} are frozen at this kill.
        // ========================================================================

        /// <summary>取该玩家仍在显示期内的击杀块; 已过期则返回空串并顺手清理缓存。 / Get the player's kill block if still within its display period; if expired, return an empty string and clean up the cache.</summary>
        private string KillNoticeBlock(Player p, PlayerData d)
        {
            if (d == null || string.IsNullOrEmpty(d.UserId)) return string.Empty;
            if (!_killNoticeUntil.TryGetValue(d.UserId, out DateTime until)) return string.Empty;
            if (DateTime.UtcNow >= until)
            {
                _killNoticeUntil.Remove(d.UserId);
                _killNoticeText.Remove(d.UserId);
                return string.Empty;
            }
            return _killNoticeText.TryGetValue(d.UserId, out string txt) ? txt : string.Empty;
        }
    }
}
