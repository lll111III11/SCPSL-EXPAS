// ============================================================================
// EXPAS —— 经验系统插件 v1.0.0 (LabAPI / SCP:SL) / EXPAS — Experience system plugin v1.0.0 (LabAPI / SCP:SL)
// ============================================================================
// 【HUD】(原生 Hint, 底部居中, 随玩家分辨率/UI缩放自适应) / [HUD] (native Hint, bottom-centered, auto-adapts to player resolution/UI scale)
//   第1行 蓝色: 欢迎 {玩家名} 进入到 {服务器名} /   Line 1 blue: Welcome {player name} to {server name}
//   第2行     : 当前已经游玩了 {天数,每天12点为新一天} 天 /   Line 2     : Days played so far: {days, a new day starts at 12:00}
//               当前时间段是 {时区/国家} {当地时间} 您准确的游玩时间是 {累计游玩时长} /               Current time period is {timezone/country} {local time}; your exact playtime is {cumulative playtime}
//   第3行     : 当前回合已造成 {回合伤害} 当前使用的角色已造成 {角色伤害} 总伤害为 {累计总伤害} /   Line 3     : Damage dealt this round {round damage}; damage dealt with current role {role damage}; total damage {cumulative total damage}
//   第4行     : EXPAS v1.0.0  {数据状态图标} /   Line 4     : EXPAS v1.0.0  {data status icon}
//   右下角块  : 第1行蓝色 您当前有 {等级} 级 / 第2行 距离下一次升级还差 {剩余经验} 经验 /   Bottom-right block: line 1 blue "Your current level: {level}" / line 2 "EXP needed for next level: {remaining EXP}"
//               (用 TMP 富文本 <align=right> 贴右边, 可配置关掉) /               (right-aligned with TMP rich text <align=right>, can be disabled in config)
//
// 【数据状态图标】(字符直接写在名字前缀与 HUD 上; 锁定模型: 0=none 1=punishment 2=selfban 3=inactive) / [Data status icons] (characters are written directly into the name prefix and HUD; lock model: 0=none 1=punishment 2=selfban 3=inactive)
//   ★ v1.6 修 unicode 缺字: 游戏 TMP 字体不含 emoji 字形、也没有缺字回退 → /   ★ v1.6 fixes missing unicode glyphs: the game TMP font contains no emoji glyphs and has no glyph fallback →
//     默认改用中文标记 IconPreset=0: [锁] punishment / [禁] selfban / [离] inactive; /     Chinese markers are used by default, IconPreset=0: [锁] punishment / [禁] selfban / [离] inactive;
//     IconPreset=1 才回到 emoji 🚫/🔒/🕒(与旧版完全一致, 可回退)。 /     only IconPreset=1 falls back to emoji 🚫/🔒/🕒 (fully identical to the old version, reversible).
//     三个 IconLock* 配置可覆盖文本("预设只决定默认文本")。 /     The three IconLock* configs can override the text ("the preset only decides the default text").
//   [锁] punishment = 惩罚锁定   —— 任何伤害/经验都不记录, 等级与经验都不涨 /   [锁] punishment = punishment lock   — no damage/EXP is recorded, neither level nor EXP grows
//   [禁] selfban    = 自助封禁   —— 伤害统计不增长、等级不涨, 但经验会叠加, /   [禁] selfban    = self-ban          — damage stats and level do not grow, but EXP accumulates;
//                                解除封禁后把期间叠加的经验一次性入账 /                                when the ban is lifted, the EXP accumulated during the ban is credited at once
//   [离] inactive   = 长期未上线 —— 超过配置天数(默认30天)未登录, 由 OfflineDays 自动置位; /   [离] inactive   = long-time offline — set automatically by OfflineDays when not logged in for more than the configured days (default 30);
//                                玩家回来自动清回 none /                                automatically cleared back to none when the player returns
//   ★ 玩家不能自助锁定, 一律由管理端(RA 面板 / 服务器控制台)用 EXPAS 指令下发。 /   ★ Players cannot lock themselves; locks are always issued by admins (RA panel / server console) via EXPAS commands.
//
// 【命令】主命令 EXPAS(别名 expsr / expr), 只注册在 / [Commands] main command EXPAS (aliases expsr / expr), registered only on
//   RemoteAdminCommandHandler(RA 面板) + GameConsoleCommandHandler(服务器控制台); /   RemoteAdminCommandHandler (RA panel) + GameConsoleCommandHandler (server console);
//   玩家在游戏内 **不能** 执行任何 EXPAS 命令(已删除 ClientCommandHandler 注册)。 /   players **cannot** execute any EXPAS command in-game (ClientCommandHandler registration has been removed).
//
// 【数据库】按 Steam64 (Player.UserId) 记录, 一个玩家一个文件: / [Database] recorded by Steam64 (Player.UserId), one file per player:
//   %APPDATA%\SCP Secret Laboratory\LabAPI\configs\<服务器端口>\EXPAS\players\<steam64>.dat /   %APPDATA%\SCP Secret Laboratory\LabAPI\configs\<server port>\EXPAS\players\<steam64>.dat
//   (旧位置 configs\EXPAS\players\ 启动时自动复制过来, 旧文件保留不删除) /   (the old location configs\EXPAS\players\ is copied over at startup; old files are kept and not deleted)
//   另维护 index.txt(全部用户 + 最后在线) 供长期未上线扫描 /   index.txt (all users + last online) is also maintained for long-offline scanning
//
// 【升级方式】只统计"玩家可扮演角色(真人)"被打时所记录的伤害; 受害者死亡时结算, / [Level-up method] only damage recorded when a "player-playable role (real player)" is hit is counted; settled when the victim dies,
//   把伤害按 DamageToExp 换算成经验发给造成伤害的玩家(按各自伤害占比分配); /   damage is converted to EXP by DamageToExp and given to the players who dealt it (split by each one's damage share);
//   经验累计到 ExpNeeded(level) 即升级。可配置为"造成伤害立刻结算"。 /   when accumulated EXP reaches ExpNeeded(level) the player levels up. Can be configured to "settle immediately on damage".
// ============================================================================
using CommandSystem;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using MEC;
using PlayerRoles;
using PlayerStatsSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using ICommand = CommandSystem.ICommand;
using L = Expas.ExpasLang;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin : Plugin<ExpasPlugin.PluginConfig>
    {
        public static ExpasPlugin Instance { get; private set; }
        public const string PluginVersion = "2.0.0-beta";

        public override string Name => "EXPAS";
        public override string Description => L.T("经验扩展插件系统 (Beta): 经验/称号/音频/状态 (Experience, eXtended Plugins, Audio, and Status System)", "Experience extension plugin system (Beta): EXP/Title/Audio/Status (Experience, eXtended Plugins, Audio, and Status System)");
        public override string Author => "匿名作者";
        public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;

        // 锁定状态(规格口径): 0=none 1=punishment 2=selfban 3=inactive / Lock state (spec definition): 0=none 1=punishment 2=selfban 3=inactive
        // 兼容说明: 旧数据文件里 lock=1(全数据锁定) 与 lock=2(自愿锁定) 在新表里语义不变, / Compatibility note: in old data files lock=1 (full data lock) and lock=2 (voluntary lock) keep the same meaning in the new table,
        // 只有"独立的 longOffline 标记"会在加载时迁移成 lock=3(inactive)。 / only the "standalone longOffline flag" is migrated to lock=3 (inactive) when loading.
        public const int LockNone = 0;          // ✅ 正常 / OK
        public const int LockPunishment = 1;    // 🚫 惩罚: 任何数据都不记录(等级/经验/伤害都不涨) / Punishment: no data is recorded (level/EXP/damage all stop growing)
        public const int LockSelfBan = 2;       // 🔒 自助封禁: 伤害不涨、等级不涨, 但经验叠加, 解除时一次性入账 / Self-ban: damage and level do not grow, but EXP accumulates and is credited at once when lifted
        public const int LockInactive = 3;      // 🕒 长期未上线: 由 OfflineDays 自动置位, 玩家回来自动清回 none / Long-time offline: set automatically by OfflineDays, cleared back to none when the player returns

        public class PluginConfig
        {
            [System.ComponentModel.Description("服务器名称(HUD 第一行)")]
            public string ServerName { get; set; } = "服务器";

            [System.ComponentModel.Description("时区: 可写 GMT+8 / +8 / 国家名(中国/日本/美国东部/英国...) 默认 GMT+8")]
            public string TimeZone { get; set; } = "GMT+8";

            [System.ComponentModel.Description("界面语言: auto=按地区自动检测(时区+公网IP) / zh=中文 / en=English (默认 auto)")]
            public string Language { get; set; } = "auto";

            [System.ComponentModel.Description("伤害换算经验倍率(1点伤害 = 多少经验)")]
            public float DamageToExp { get; set; } = 1f;

            [System.ComponentModel.Description("1级升2级所需经验")]
            public int BaseExp { get; set; } = 100;

            [System.ComponentModel.Description("每级所需经验增长倍率")]
            public float ExpGrowth { get; set; } = 1.35f;

            [System.ComponentModel.Description("最高等级")]
            public int MaxLevel { get; set; } = 100;

            [System.ComponentModel.Description("只统计真人玩家(玩家可扮演角色)受到的伤害")]
            public bool OnlyRealPlayers { get; set; } = true;

            [System.ComponentModel.Description("是否也统计对 SCP 的伤害(SCP 也是玩家可扮演角色, 建议开)")]
            public bool CountScp { get; set; } = true;

            [System.ComponentModel.Description("是否也统计对人机(dummy)的伤害")]
            public bool CountDummies { get; set; } = false;

            [System.ComponentModel.Description("受害者死亡时才结算经验(关=造成伤害立刻结算)")]
            public bool SettleOnDeath { get; set; } = true;

            [System.ComponentModel.Description("HUD 开关")]
            public bool HudEnabled { get; set; } = true;

            [System.ComponentModel.Description("HUD 刷新间隔(秒)")]
            public float HudInterval { get; set; } = 1f;

            [System.ComponentModel.Description("等级块贴右下角(<align=right>)")]
            public bool AlignLevelRight { get; set; } = true;

            [System.ComponentModel.Description("名字前缀开关(名字前加 [Lv.X])")]
            public bool NamePrefix { get; set; } = true;

            [System.ComponentModel.Description("名字前缀格式, {0}=等级")]
            public string NameFormat { get; set; } = "[Lv.{0}]";

            [System.ComponentModel.Description("名字前缀里也带锁定图标")]
            public bool NameIcons { get; set; } = true;

            [System.ComponentModel.Description("多少天未上线算长期离线(自动打 🕒 标记)")]
            public int OfflineDays { get; set; } = 30;

            [System.ComponentModel.Description("自动保存间隔(秒)")]
            public float AutoSave { get; set; } = 60f;

            [System.ComponentModel.Description("升级公告时长(秒, 0=不公告)")]
            public int LevelUpNotice { get; set; } = 5;

            [System.ComponentModel.Description("HUD 清空间隔(秒, 略大于刷新间隔即可)")]
            public float HudClear { get; set; } = 1.6f;

            [System.ComponentModel.Description("HUD 默认字号(%), 玩家可在 SSS 面板里自己改")]
            public int HudSizePercentDefault { get; set; } = 85;

            [System.ComponentModel.Description("HUD 默认屏幕缩放(%), 与字号相乘")]
            public int HudScreenPercentDefault { get; set; } = 100;

            [System.ComponentModel.Description("HUD 默认行距(%), 100=不动, 小于100更紧凑")]
            public int HudLineHeightDefault { get; set; } = 100;

            [System.ComponentModel.Description("HUD 默认水平对齐(0左 1居中 2右)")]
            public int HudAlignDefault { get; set; } = 1;

            [System.ComponentModel.Description("HUD 默认等级块对齐(0左 1居中 2右 3跟随整块)")]
            public int HudLevelAlignDefault { get; set; } = 2;

            [System.ComponentModel.Description("是否输出 TMP 标签(size/line-height/voffset) —— 若客户端把它们当普通文字显示出来, 设为 false")]
            public bool HudUseTags { get; set; } = true;

            [System.ComponentModel.Description("字号标签写法(0=百分比, 1=相对偏移)")]
            public int HudSizeStyle { get; set; } = 0;

            // ==================== v1.2 文本模板(改配置文件→重启生效) ==================== / ==================== v1.2 text templates (edit config file → restart to apply) ====================
            [System.ComponentModel.Description("使用配置文件里的文本模板(关掉则用插件内置的老版文案)")]
            public bool UseTextTemplates { get; set; } = true;

            [System.ComponentModel.Description("HUD 第1行模板")]
            public string HudLine1 { get; set; } = "<color=#4FA8FF>欢迎 {name} 进入到 {server}</color>";

            [System.ComponentModel.Description("HUD 第2行模板")]
            public string HudLine2 { get; set; } = "当前已经游玩了 {days} 天  当前时间段是 {tz} {clock}  您准确的游玩时间是 {playtime}";

            [System.ComponentModel.Description("HUD 第3行模板")]
            public string HudLine3 { get; set; } = "当前回合已造成 {roundDmg}  当前使用的角色已造成 {roleDmg}  总伤害为 {totalDmg}";

            [System.ComponentModel.Description("HUD 第4行模板")]
            public string HudLine4 { get; set; } = "EXPAS v{version}{icons}";

            [System.ComponentModel.Description("等级块第1行模板")]
            public string HudLevel1 { get; set; } = "<color=#4FA8FF>您当前有 {level} 级</color>";

            [System.ComponentModel.Description("等级块第2行模板")]
            public string HudLevel2 { get; set; } = "距离下一次升级还差 {expNeed} 经验";

            [System.ComponentModel.Description("自定义文本行(依次显示在 HUD 上, 内容随便写; 例如 ADSFAF / WSFAGEW / SGAFWS)")]
            public List<string> CustomLines { get; set; } = new List<string>();

            [System.ComponentModel.Description("自定义行位置(0=等级块之前, 1=等级块之后)")]
            public int CustomLinesPos { get; set; } = 0;

            [System.ComponentModel.Description("占位符:{name} {server} {days} {hours} {tz} {clock} {playtime} {roundDmg} {roleDmg} {totalDmg} {totalExp} {level} {exp} {expNeed} {kills} {deaths} {version} {icons} {mvpName} {mvpDamage} {mvpLevel} {topList} | v1.3 积分/击杀:{points} {roundPoints} {killName} {killDamage} {killShare} {killPoints} | v1.4 新增:{roundTime} 本回合时长 {aliveCount} 存活玩家数 {tps} 服务器TPS {scpCount} 存活SCP数 {roundId} 本回合序号(服务器启动起算) | v1.5 新增:{killRole} 被击杀的角色(RoleTypeId 名, 例 NtfSergeant) {killRoleKind} 该角色类型(人形/SCP/人机/其它) | v1.6 新增:{aspect} 屏幕宽高比标签(16:9 / 16:10 / 21:9 / 4:3 / 其它(x.xx), 取不到时显示 -)")]
            public string PlaceholderHelp { get; set; } = "上面这些占位符可以写在任意一行模板或自定义行里";

            // ==================== v1.2 时间显示 ==================== / ==================== v1.2 time display ====================
            [System.ComponentModel.Description("游玩时间显示(0=自动 天/小时/分/秒, 1=只按小时(两位小数))")]
            public int PlaytimeMode { get; set; } = 1;

            [System.ComponentModel.Description("天数显示(0=天数(每天12点为新一天), 1=从首次进入起算的总小时(一位小数))")]
            public int DaysMode { get; set; } = 0;

            // ==================== v1.2 本回合 API ==================== / ==================== v1.2 round API ====================
            [System.ComponentModel.Description("是否开放本回合 API(其它插件可查询局内最高分/MVP/排行榜, 见 ExpasApi)")]
            public bool ApiEnabled { get; set; } = false;

            [System.ComponentModel.Description("排行榜取前几名")]
            public int TopCount { get; set; } = 5;

            [System.ComponentModel.Description("回合结束的 MVP 公告模板(空=不公告; 占位符 {mvpName} {mvpDamage} {mvpLevel} {topList})")]
            public string MvpAnnounce { get; set; } = "";

            [System.ComponentModel.Description("回合结算语音播报(Announcer, 原文写作 Cassie 已过时)文本(默认空 = 不播报, 保持安静; 占位符 {mvpName} {mvpDamage} {mvpLevel} {topList}; 注意语音库只识别英文单词与数字, 中文名字读不出来)")]
            public string MvpCassie { get; set; } = "";

            [System.ComponentModel.Description("播报字幕文本(Announcer.Message 的 customSubtitles; 空=用播报文本本身当字幕; 这里可以写中文)")]
            public string MvpCassieSubtitle { get; set; } = "";

            // ==================== v1.3 积分系统 ==================== / ==================== v1.3 points system ====================
            [System.ComponentModel.Description("满额积分对应的伤害量(默认100点伤害 = 100%)")]
            public float PointsFullDamage { get; set; } = 100f;

            [System.ComponentModel.Description("满额积分值(默认100点伤害占100积分)")]
            public float PointsFullValue { get; set; } = 100f;

            [System.ComponentModel.Description("积分是否按伤害占比分配(开=按对该受害者的伤害占比, 关=按实际伤害直接换算)")]
            public bool PointsByShare { get; set; } = false;

            // ==================== v1.5 击杀确认模板(改造2) ==================== / ==================== v1.5 kill confirmation template (rework 2) ====================
            //   三段: ① 击杀的是什么角色  ② 左占比 / 右伤害  ③ 本次加多少积分(累计) /   Three parts: ① what role was killed  ② share on the left / damage on the right  ③ points gained this time (cumulative)
            //   ★ TMP 注意: <align> 在 TMP 里是"按行"生效的标签(整行最终只取一个对齐) —— /   ★ TMP note: <align> is a "per-line" tag in TMP (the whole line ultimately keeps only one alignment) —
            //     多数客户端上写在同一行的两个 <align> 段会退化成"整行统一对齐"(仍可读, 只是左右不分离)。 /     on most clients two <align> sections on the same line degrade to "one alignment for the whole line" (still readable, just not split left/right).
            //     若你的客户端确实如此, 把 kill_line2 改成用空格补齐的等价写法即可(不用改代码): /     If your client behaves this way, change kill_line2 to the equivalent space-padded form (no code change required):
            //       kill_line2: 占比 {killShare}%                    伤害 {killDamage} /       kill_line2: share {killShare}%                    damage {killDamage}
            //     (导出段落只在"对齐"上退化, 文字内容与数值不受影响。) /     (the exported section only degrades in "alignment"; text content and values are unaffected.)
            [System.ComponentModel.Description("击杀确认第1行模板: ① 被击杀的角色({killRole}=角色名, 例 NtfSergeant; {killRoleKind}=人形/SCP/人机/其它; dummy 也照记)")]
            public string KillLine1 { get; set; } = "<color=#FFD700>已击杀 {killRole}</color>";

            [System.ComponentModel.Description("击杀确认第2行模板: ② 左边占比 / 右边伤害(同一行两个 <align> 段; 若客户端不支持, 见上一行注释里的空格补齐写法)")]
            public string KillLine2 { get; set; } = "<align=left>占比 {killShare}%</align><align=right>伤害 {killDamage}</align>";

            [System.ComponentModel.Description("击杀确认第3行模板: ③ 本次获得多少积分(括号里是累计积分)")]
            public string KillLine3 { get; set; } = "获得积分 +{killPoints}   (累计 {points})";

            // ==================== v1.3 UI 隔离 ==================== / ==================== v1.3 UI isolation ====================
            [System.ComponentModel.Description("HUD 输出方式: 1=屏幕 Hint(默认, 每个玩家屏幕底部实时刷新) / 2=同 1 / 0=关闭屏幕 HUD(只更新面板文本区)")]
            public int HudSurface { get; set; } = 1;

            [System.ComponentModel.Description("未存活时也显示 HUD(大厅/开局前/观察者/死亡后) —— 关掉则只在活着时显示")]
            public bool HudInLobby { get; set; } = true;

            [System.ComponentModel.Description("SSS 面板里额外注册一个独立的「击杀/积分」文本区(与状态区隔离开)")]
            public bool KillFeedPanel { get; set; } = true;

            // ==================== v1.5 击杀确认(改造2: 并入 HUD, 不再单独发 Hint) ==================== / ==================== v1.5 kill confirmation (rework 2: merged into HUD, no separate Hint anymore) ====================
            //   为什么并入 HUD: HUD 每 HudInterval 秒重建一次, 而"单独发一条 4 秒 Hint"会被下一次 /   Why merge into HUD: the HUD is rebuilt every HudInterval seconds, while "a standalone 4-second Hint" would be
            //   HUD 刷新立刻覆盖 → 玩家根本看不到击杀信息。改成把击杀块追加在 HUD 文本末尾, /   immediately overwritten by the next HUD refresh → players would never see the kill info. Appending the kill block
            //   持续 KillNoticeSeconds 秒后自动消失, 就与 HUD 刷新不再打架。 /   to the end of the HUD text and keeping it for KillNoticeSeconds seconds makes it stop fighting with HUD refreshes.
            [System.ComponentModel.Description("击杀确认: 把击杀块(kill_line1/2/3)并入 HUD 末尾并持续显示的秒数(0=不并入)")]
            public float KillNoticeSeconds { get; set; } = 6f;

            [System.ComponentModel.Description("击杀确认: 是否额外再单独发一条 4 秒 Hint(默认 false —— 独立 Hint 会被下一次 HUD 刷新覆盖, 所以默认不用)")]
            public bool KillNoticeAsHint { get; set; } = false;

            // ==================== v1.6 图标预设(修 unicode 缺字) ==================== / ==================== v1.6 icon presets (fix missing unicode glyphs) ====================
            //   背景(已查证): 游戏 TMP 字体**不含 emoji 字形, 也没有缺字回退** /   Background (verified): the game TMP font **contains no emoji glyphs and has no glyph fallback**
            //   (在 game-src 里 grep "missing glyph" / "fallbackFontAsset" / "SpriteAsset" 全 0 命中), /   (grep for "missing glyph" / "fallbackFontAsset" / "SpriteAsset" in game-src returns 0 hits),
            //   所以 🚫🔒🕒 必然渲染成缺字方框。默认改成字体安全的中文标记, 需要 emoji 的人把本项设为 1。 /   so 🚫🔒🕒 necessarily render as missing-glyph boxes. The default is now font-safe Chinese markers; set this to 1 if you need emoji.
            [System.ComponentModel.Description("锁定图标预设: 0=中文标记 [锁]/[禁]/[离](默认, 字体安全) / 1=emoji 🚫/🔒/🕒(与旧版完全一致)")]
            public int IconPreset { get; set; } = 0;

            [System.ComponentModel.Description("惩罚锁定图标文本(默认 [锁]; IconPreset=1 时不填则用 🚫; 这里填了非默认值则以这里为准)")]
            public string IconLockPunishment { get; set; } = "[锁]";

            [System.ComponentModel.Description("自助封禁图标文本(默认 [禁]; IconPreset=1 时不填则用 🔒; 这里填了非默认值则以这里为准)")]
            public string IconLockSelfBan { get; set; } = "[禁]";

            [System.ComponentModel.Description("长期未上线图标文本(默认 [离]; IconPreset=1 时不填则用 🕒; 这里填了非默认值则以这里为准)")]
            public string IconLockInactive { get; set; } = "[离]";

            // ==================== v1.6 HUD 折叠 / 分辨率档位 / 整体缩放(服务器默认值) ==================== / ==================== v1.6 HUD collapse / resolution preset / global scaling (server defaults) ====================
            //   ★ 下面这三项是"服务器默认值"; 玩家在 SSS 面板里改过的值会按 Steam64 覆盖它们 /   ★ The three items below are "server defaults"; values changed by players in the SSS panel override them per Steam64
            //     (玩家 .dat 里的 resolutionPreset / globalScalePercent / hudCollapsed, -1 = 跟随服务器)。 /     (resolutionPreset / globalScalePercent / hudCollapsed in the player's .dat, -1 = follow the server).
            //   ★ 服务端读不到玩家的**像素分辨率**, 只能读宽高比/视野(见 AspectRatioSync: G_14903/G_14915/G_14956), /   ★ The server cannot read the player's **pixel resolution**, only aspect ratio/FOV (see AspectRatioSync: G_14903/G_14915/G_14956),
            //     所以"分辨率档位"是"玩家自选 + 按宽高比自动", 不是真的检测像素。 /     so the "resolution preset" is "player choice + automatic by aspect ratio", not real pixel detection.
            [System.ComponentModel.Description("屏幕分辨率档位默认值: 0=自动检测(按宽高比) 1=640x480 2=800x600 3=1024x768 4=1280x720 5=1280x800 6=1366x768 7=1440x900 8=1600x900 9=1680x1050 10=1920x1080 11=1920x1200 12=2560x1080 13=2560x1440 14=3440x1440 15=3840x2160(4K)")]
            public int ResolutionPreset { get; set; } = 0;

            [System.ComponentModel.Description("整体缩放默认值(%): 在「字号 × 屏幕缩放」之外再乘一次(50..200, 默认 100)")]
            public int GlobalScalePercent { get; set; } = 100;

            [System.ComponentModel.Description("HUD 折叠默认值: 0=展开(默认) 1=折叠(屏幕 HUD 只显示 hud_collapsed_line 一行; 击杀块仍然显示)")]
            public int HudCollapsed { get; set; } = 0;

            [System.ComponentModel.Description("HUD 折叠时显示的**单行**模板(展开时不用它; 占位符与其它模板完全一样)")]
            public string HudCollapsedLine { get; set; } = "<color=#4FA8FF>[EXPAS]</color> Lv.{level}  积分 {points}  伤害 {totalDmg}{icons}";

            // ==================== v1.7/v1.8 自定义 UI 布局 + 白名单(权限隔离) ==================== / ==================== v1.7/v1.8 custom UI layouts + whitelist (permission isolation) ====================
            //   布局文件: configs\<服务器端口>\EXPAS\layouts\<key>.txt 或 <key>.js (key = 文件名去扩展名) /   Layout file: configs\<server port>\EXPAS\layouts\<key>.txt or <key>.js (key = file name without extension)
            //   ★ v1.8 可见性以"布局文件的 owner Steam64 归属匹配"为准: 文件首行写 /   ★ v1.8 visibility is based on "the layout file's owner Steam64 ownership match": write on the first line of the file
            //     "# owner = <Steam64>" / "// owner = <Steam64>" / "const owner = \"…\"" 或文件名带 17 位数字前缀, /     "# owner = <Steam64>" / "// owner = <Steam64>" / "const owner = \"…\"" or prefix the file name with a 17-digit number,
            //     该玩家进服时检测 ID, 归属匹配 SSS 面板里才会出现「自定义 UI」下拉(默认 + 本人布局); /     the player's ID is checked on join; only when ownership matches does the "Custom UI" dropdown appear in the SSS panel (default + own layouts);
            //     其它玩家整项不显示(见 EXPAS说明.txt)。 /     for other players the whole item is hidden (see EXPAS说明.txt).
            //   写法: 支持 "76561198000000000@steam" 与纯数字 "76561198000000000"(自动补 @steam), 归一化后匹配。 /   Syntax: supports "76561198000000000@steam" and plain digits "76561198000000000" (auto-appends @steam), matched after normalization.
            //   "*" = 放开给所有人(方便测试)。 /   "*" = open to everyone (for testing convenience).
            [System.ComponentModel.Description("自定义 UI 白名单(Steam64, 支持 7656119…@steam 或纯数字; 兼容覆盖: 名单里的玩家能看到全部布局; 平时不用加, 布局文件里的 owner 已决定归属; 填 * = 放开给所有人)")]
            public List<string> CustomUiWhitelist { get; set; } = new List<string>();

            // ==================== v2.0 NP 称号系统(三层) ==================== / ==================== v2.0 NP title system (three tiers) ====================
            [System.ComponentModel.Description("称号系统总开关(基于 LabAPI Player.CustomInfo 名字下方灰字)")]
            public bool TitleEnabled { get; set; } = true;

            [System.ComponentModel.Description("称号默认策略(玩家可被命令覆盖): 0=不干预 1=无称号才设(层1) 2=强制替换(层2) 3=彻底清除(层3)")]
            public int TitleDefaultPlan { get; set; } = 1;

            // ==================== v2.0 NP 音频系统 ==================== / ==================== v2.0 NP audio system ====================
            [System.ComponentModel.Description("音频播放方案: 0=关闭 1=隐藏音箱(默认, 远处高空非空间化 SpeakerToy 全服可闻, 不生成假人/不进玩家列表)")]
            public int AudioPlaybackPlan { get; set; } = 1;

            [System.ComponentModel.Description("音频音量(0..1)")]
            public float AudioVolume { get; set; } = 1f;

            [System.ComponentModel.Description("是否循环播放")]
            public bool AudioLoop { get; set; } = false;

            [System.ComponentModel.Description("可选: 自定义 ffmpeg.exe 完整路径(留空 = 自动: EXPAS\\bin\\ → EXPAS\\ → 插件目录 → BGMCC 临时目录 → PATH → 从 DLL 内嵌资源释放)")]
            public string FfmpegPath { get; set; } = string.Empty;

            [System.ComponentModel.Description("音频解码超时(秒): 非 WAV 格式走 ffmpeg 后台解码, 超过这个时间就放弃(默认 120)")]
            public int AudioDecodeTimeoutSec { get; set; } = 120;

            [System.ComponentModel.Description("音频调用声明(每行: 名称|打开后的效果|通过哪个API播放会怎样)。放进 configs\\<端口>\\EXPAS\\audio\\ 的 .wav 即使用 EXPAS audio play <名字> 播放")]
            public List<string> AudioCalls { get; set; } = new List<string>
            {
                "示例call|例: 开门提示音|SpeakerToy/SpawnDummy: 全服广播该提示音; 需把 xxx.wav 放入 audio 目录",
                "API 调用说明|第三方插件可用 ExpasApi.PlayAudioFile(file)/PlayMvpMusic(userId)/StopAudio()|例: ExpasApi.PlayAudioFile(\"ambient.wav\") 全服播放 audio\\ambient.wav; PlayMvpMusic(steam64) 播放该玩家命中的第一名音乐盒"
            };

            // ==================== v2.0/v2.2 NP 第一名音乐盒(JS 文件驱动) ==================== / ==================== v2.0/v2.2 NP first-place music box (driven by JS files) ====================
            //   机制: mvp_music\ 目录下放 <Steam64>.js(声明 owner + audio), 音频也放该目录; /   Mechanism: put <Steam64>.js (declares owner + audio) in the mvp_music\ directory, and put the audio there too;
            //         回合结束时用"伤害第一名"的 Steam64 检索这些 JS, 命中即按 JS 里填的路径播放。 /         at round end the Steam64 of the "top damage" player is used to look up these JS files, and if matched the audio is played from the path in the JS.
            //   (与 UI 布局的 layouts\*.js 是两套独立的东西, 只是都用同一套"只读引号串"的解析规则) /   (This is a separate system from the layouts\*.js of UI layouts; both just use the same "quoted strings only" parsing rule)
            [System.ComponentModel.Description("第一名音乐盒总开关: 回合结束时检测本回合伤害第一的玩家, 用其 Steam64 检索 mvp_music\\ 下的 JS 文件; 命中就按 JS 里填的路径播放音频")]
            public bool MvpMusicEnabled { get; set; } = true;

            [System.ComponentModel.Description("【可选/兼容】音乐盒映射表(每行: Steam64|音频文件名或路径)。一般不用 —— 请改用 mvp_music\\<Steam64>.js 声明方式; 这里留空即可")]
            public List<string> MvpMusicPlayers { get; set; } = new List<string>();

            // ==================== v2.1 玩家ID 解析方式(命令里 <玩家ID> 支持哪些写法, 可开关) ==================== / ==================== v2.1 player ID resolution (which forms <player ID> supports in commands, toggleable) ====================
            //   命令里不必非要写 Steam64: 也可以写"局内序列ID"(RA 玩家列表编号)或"玩家名", /   Commands do not require Steam64: you can also write the "in-game sequence ID" (RA player list number) or the "player name",
            //   系统会自行解析成 Steam64 再改。下面四项决定允许哪些写法(默认全开)。 /   and the system resolves it to Steam64 before applying the change. The four items below decide which forms are allowed (all on by default).
            [System.ComponentModel.Description("允许用「局内序列ID」(RA 玩家列表编号, 如 3)查找玩家 —— 系统自动解析成 Steam64 再改(需在线; 默认开)")]
            public bool LookupBySeqId { get; set; } = true;

            [System.ComponentModel.Description("允许用「玩家名」查找玩家(需在线; 默认开)")]
            public bool LookupByName { get; set; } = true;

            [System.ComponentModel.Description("允许用「Steam64 / 完整 UserId」查找玩家(离线也能改; 默认开)")]
            public bool LookupBySteam { get; set; } = true;

            [System.ComponentModel.Description("玩家名是否支持模糊(包含)匹配: true=可只写名字的一部分; false=必须完全一致。模糊命中多人时报错要求写精确(默认开)")]
            public bool NameLookupContains { get; set; } = true;
        }

        // ==================== 运行时状态 ==================== / ==================== runtime state ====================
        private readonly Dictionary<string, PlayerData> _cache = new Dictionary<string, PlayerData>();
        private readonly Dictionary<string, DateTime> _sessionStart = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, float> _roundDamage = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _roleDamage = new Dictionary<string, float>();
        private readonly Dictionary<string, RoleTypeId> _lastRole = new Dictionary<string, RoleTypeId>();
        private readonly Dictionary<string, bool> _hudOff = new Dictionary<string, bool>();
        /// <summary>受害者 netId → 攻击者 userId → 累计伤害(死亡时结算) / Victim netId → attacker userId → accumulated damage (settled on death)</summary>
        private readonly Dictionary<uint, Dictionary<string, float>> _pending
            = new Dictionary<uint, Dictionary<string, float>>();
        private CoroutineHandle _loop;
        private float _saveTimer = 0f;
        private int _panelTick = 0;
        /// <summary>本回合 MVP 是否已结算 —— 去重标记: RoundEnded 结算过之后 RoundRestarted 不再重复结算。 / Whether this round's MVP has already been settled — dedupe flag: after settling in RoundEnded, RoundRestarted will not settle again</summary>
        private bool _mvpSettled = false;

        /// <summary>v1.5 击杀确认: userId → 已渲染好的击杀块文本(kill_line1/2/3)。 / v1.5 kill confirmation: userId → rendered kill block text (kill_line1/2/3)</summary>
        private readonly Dictionary<string, string> _killNoticeText = new Dictionary<string, string>();
        /// <summary>v1.5 击杀确认: userId → 击杀块过期时刻(UTC); 过期后自动从 HUD 上消失。 / v1.5 kill confirmation: userId → kill block expiry time (UTC); removed from the HUD automatically after expiry</summary>
        private readonly Dictionary<string, DateTime> _killNoticeUntil = new Dictionary<string, DateTime>();

        /// <summary>
        /// 经验冻结开关(v1.5, EXPAS xpfreeze / freezexp): / EXP freeze switch (v1.5, EXPAS xpfreeze / freezexp):
        /// 打开后 ApplyExp 直接返回 → 本回合不再有任何经验增长; 回合重开(RoundRestarted)自动恢复。 / when enabled ApplyExp returns immediately → no EXP growth at all this round; automatically restored on round restart (RoundRestarted)
        /// </summary>
        private bool _xpFrozen = false;

        /// <summary>当前是否处于"本回合经验冻结"状态(供指令/面板/其它插件查询)。 / Whether "EXP is frozen for this round" is currently active (for commands/panel/other plugins to query)</summary>
        public bool XpFrozen => _xpFrozen;

        /// <summary>
        /// v1.5 配置防呆 —— 修"配置溢出 → 整份配置读取失败"的根因之一。 / v1.5 config guard — fixes one of the root causes of "config overflow → whole config fails to load"
        /// 对全部数值配置做 Clamp 校正(夹回安全区间), **只有确有改动时**才 SaveConfig() 写回, / Clamps all numeric config values back into safe ranges, and calls SaveConfig() to write back **only when something actually changed**,
        /// 并 Logger.Warn 打印被修正的项。 / while Logger.Warn prints the corrected items
        /// 注: YamlDotNet 在反序列化阶段就抛 OverflowException(例如 max_level 写成 21 位数) 时, / Note: if YamlDotNet throws OverflowException during deserialization (e.g. max_level written as a 21-digit number),
        ///     整份配置会回落默认值 —— 那种情况靠改正配置文件本身; 这里兜底"能解析但越界"的值 /     the whole config falls back to defaults — fix the config file itself in that case; this guards "parseable but out-of-range" values
        ///     (例如 max_level: 99999999999)。 /     (e.g. max_level: 99999999999)
        /// </summary>
        private void ClampConfig()
        {
            var fixedItems = new List<string>();

            Config.MaxLevel = ClampTo(fixedItems, "MaxLevel", Config.MaxLevel, 1, 100000);
            Config.BaseExp = ClampTo(fixedItems, "BaseExp", Config.BaseExp, 1, 1000000);
            Config.DamageToExp = ClampTo(fixedItems, "DamageToExp", Config.DamageToExp, 0f, 1000f);
            Config.ExpGrowth = ClampTo(fixedItems, "ExpGrowth", Config.ExpGrowth, 1.01f, 100f);
            Config.PointsFullDamage = ClampTo(fixedItems, "PointsFullDamage", Config.PointsFullDamage, 1f, 1000000f);
            Config.PointsFullValue = ClampTo(fixedItems, "PointsFullValue", Config.PointsFullValue, 1f, 1000000f);
            Config.OfflineDays = ClampTo(fixedItems, "OfflineDays", Config.OfflineDays, 1, 3650);
            Config.HudSizePercentDefault = ClampTo(fixedItems, "HudSizePercentDefault", Config.HudSizePercentDefault, 40, 160);
            Config.HudScreenPercentDefault = ClampTo(fixedItems, "HudScreenPercentDefault", Config.HudScreenPercentDefault, 50, 150);
            Config.HudLineHeightDefault = ClampTo(fixedItems, "HudLineHeightDefault", Config.HudLineHeightDefault, 60, 140);
            Config.HudAlignDefault = ClampTo(fixedItems, "HudAlignDefault", Config.HudAlignDefault, 0, 2);
            Config.HudLevelAlignDefault = ClampTo(fixedItems, "HudLevelAlignDefault", Config.HudLevelAlignDefault, 0, 3);
            Config.TopCount = ClampTo(fixedItems, "TopCount", Config.TopCount, 1, 20);
            Config.HudSurface = ClampTo(fixedItems, "HudSurface", Config.HudSurface, 0, 2);
            Config.KillNoticeSeconds = ClampTo(fixedItems, "KillNoticeSeconds", Config.KillNoticeSeconds, 0f, 120f);
            Config.AudioDecodeTimeoutSec = ClampTo(fixedItems, "AudioDecodeTimeoutSec", Config.AudioDecodeTimeoutSec, 5, 1800);
            Config.PlaytimeMode = ClampTo(fixedItems, "PlaytimeMode", Config.PlaytimeMode, 0, 1);
            Config.DaysMode = ClampTo(fixedItems, "DaysMode", Config.DaysMode, 0, 1);
            // v1.6 新增项防呆 / v1.6 new item guard
            Config.IconPreset = ClampTo(fixedItems, "IconPreset", Config.IconPreset, 0, 1);
            Config.ResolutionPreset = ClampTo(fixedItems, "ResolutionPreset", Config.ResolutionPreset, 0, 15);
            Config.GlobalScalePercent = ClampTo(fixedItems, "GlobalScalePercent", Config.GlobalScalePercent, 50, 200);
            Config.HudCollapsed = ClampTo(fixedItems, "HudCollapsed", Config.HudCollapsed, 0, 1);
            // v1.7 白名单: null 视为空表(配置文件里 custom_ui_whitelist: 后什么都不写时可能出现 null) / v1.7 whitelist: null is treated as an empty list (null may appear when custom_ui_whitelist: is left empty in the config file)
            if (Config.CustomUiWhitelist == null) Config.CustomUiWhitelist = new List<string>();
            // v2.0 NP: 音频声明 / 第一名音乐盒映射表 的 null 防护 + 方案取值夹紧 / v2.0 NP: null protection for audio declarations / first-place music box mapping + plan value clamping
            if (Config.AudioCalls == null) Config.AudioCalls = new List<string>();
            if (Config.MvpMusicPlayers == null) Config.MvpMusicPlayers = new List<string>();
            Config.AudioPlaybackPlan = ClampTo(fixedItems, "AudioPlaybackPlan", Config.AudioPlaybackPlan, AudioPlanOff, AudioPlanSpeaker);
            Config.AudioVolume = ClampTo(fixedItems, "AudioVolume", Config.AudioVolume, 0f, 1f);
            Config.TitleDefaultPlan = ClampTo(fixedItems, "TitleDefaultPlan", Config.TitleDefaultPlan, TitleOff, TitleClear);

            if (fixedItems.Count > 0)
            {
                SaveConfig(); LocalizeConfigFile();
                Logger.Warn(L.T($"[EXPAS] 配置防呆: 已修正 {fixedItems.Count} 项越界数值并写回 → {string.Join(", ", fixedItems)}",
                    $"[EXPAS] Config guard: fixed {fixedItems.Count} out-of-range value(s) and wrote them back → {string.Join(", ", fixedItems)}"));
            }
        }

        private static int ClampTo(List<string> log, string name, int v, int min, int max)
        {
            if (v < min) { log.Add($"{name}={v}→{min}"); return min; }
            if (v > max) { log.Add($"{name}={v}→{max}"); return max; }
            return v;
        }

        private static float ClampTo(List<string> log, string name, float v, float min, float max)
        {
            if (float.IsNaN(v) || v < min) { log.Add($"{name}={v}→{min}"); return min; }
            if (v > max) { log.Add($"{name}={v}→{max}"); return max; }
            return v;
        }

        /// <summary>按当前语言回写 config.yml 的注释(LabAPI 生成的是 [Description] 原文)。 / Writes back config.yml comments in the current language (LabAPI generates the raw [Description] text)</summary>
        private void LocalizeConfigFile()
        {
            try
            {
                DirectoryInfo dir = ConfigDirInfo;
                if (dir != null) ExpasLang.LocalizeConfigFile(Path.Combine(dir.FullName, "config.yml"));
            }
            catch { }
        }

        public override void Enable()
        {
            Instance = this;
            // 语言/地区检测先跑: 后面的默认文案、日志、面板标题都按结果取词 / Language/region detection runs first: later default texts, logs, and panel titles pick words based on the result
            ExpasLang.Initialize(Config.Language, Config.TimeZone);
            if (ExpasLang.ApplyTextDefaults(Config)) SaveConfig();
            LocalizeConfigFile();
            ClampConfig();               // v1.5 配置防呆: 越界数值夹回安全区间, 只在确有改动时写回 / v1.5 config guard: clamp out-of-range values back into safe ranges, write back only when something actually changed
            MigrateLegacyPlayersDir();   // v1.4: 旧库 configs\EXPAS\players → configs\<端口>\EXPAS\players(复制, 不删旧文件) / v1.4: old library configs\EXPAS\players → configs\<port>\EXPAS\players (copy, old files are not deleted)
            LoadLayouts();               // v1.7: 扫描 layouts\*.txt(缺失/失败 → 回退内置默认布局, 必须永远可用) / v1.7: scan layouts\*.txt (missing/failure → fall back to built-in default layout, which must always be available)
            LoadOrCreateIndex();
            MarkLongOfflinePass();
            try { Directory.CreateDirectory(AudioDir); } catch { }   // v2.0: 音频目录(audio\) / v2.0: audio directory (audio\)
            try { Directory.CreateDirectory(MvpMusicDir); } catch { } // v2.0: 第一名音乐盒专属目录(mvp_music\) / v2.0: first-place music box dedicated directory (mvp_music\)
            // v2.5: 启动即释放/加载**自建原生 DLL 解码器**(OGG/MP3/AAC/M4A/MP4/FLAC…), / v2.5: release/load the **self-built native DLL decoder** at startup (OGG/MP3/AAC/M4A/MP4/FLAC…),
            //        ffmpeg.exe 仅作为后备(外部 ffmpeg / 原生加载失败时) /       ffmpeg.exe is only a fallback (external ffmpeg / when native loading fails)
            try
            {
                EnsureNative();
                EnsureFfmpeg();
                Logger.Info(L.T($"[EXPAS] 音频解码器: {DecoderStatusText()}  (支持 {string.Join("/", AudioExtensions.Select(x => x.TrimStart('.')))})",
                    $"[EXPAS] Audio decoder: {DecoderStatusText()}  (supports {string.Join("/", AudioExtensions.Select(x => x.TrimStart('.')))})"));
            }
            catch { }

            PlayerEvents.Joined += OnJoined;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Hurting += OnHurting;
            PlayerEvents.Death += OnDeath;
            PlayerEvents.ChangedRole += OnChangedRole;   // 换角色改为事件驱动(v1.4, 轮询保留作兜底) / role change switched to event-driven (v1.4, polling kept as fallback)
            ServerEvents.RoundEnded += OnRoundEnded;     // v1.4: 回合结束即结算 MVP(不再等 RoundRestarted, 免迟一整回合) / v1.4: settle MVP as soon as the round ends (no longer wait for RoundRestarted, avoiding a full-round delay)
            ServerEvents.RoundRestarted += OnRoundRestarted;

            RegisterSss();                       // SSS 面板: 玩家自调 HUD 字号/缩放/位置 / SSS panel: players adjust HUD font size/scale/position themselves

            _loop = Timing.RunCoroutine(HudLoop());
            Logger.Info(L.T($"[EXPAS] 经验系统已启用 v{PluginVersion} (服务器名: {Config.ServerName})",
                $"[EXPAS] EXP system enabled v{PluginVersion} (server name: {Config.ServerName})"));
            Logger.Info(L.T(
                $"[EXPAS] 语言/地区: 中文 (检测依据: {ExpasLang.DetectedBy})",
                $"[EXPAS] Language/region: English (detected by: {ExpasLang.DetectedBy})"));
        }

        public override void Disable()
        {
            PlayerEvents.Joined -= OnJoined;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Hurting -= OnHurting;
            PlayerEvents.Death -= OnDeath;
            PlayerEvents.ChangedRole -= OnChangedRole;
            ServerEvents.RoundEnded -= OnRoundEnded;
            ServerEvents.RoundRestarted -= OnRoundRestarted;

            Timing.KillCoroutines(_loop);
            UnregisterSss();
            StopAudio();                 // v2.0: 停掉隐藏音箱播放 / v2.0: stop hidden speaker playback
            try { lock (_pendingLock) _pendingPlays.Clear(); } catch { }   // v2.4: 丢掉未播出的解码结果 / v2.4: drop decoded results that were never played
            FlushAll(L.T("插件卸载", "plugin unload"));
            Instance = null;
        }

        /// <summary>
        /// 回合结束 → 立刻结算本回合 MVP(公告 + 触发 ExpasApi.RoundMvpSettled 供音乐/其它扩展)。 / Round ended → settle this round's MVP immediately (announcement + fire ExpasApi.RoundMvpSettled for music/other extensions)
        /// 依据: 源码 ServerEvents.RoundEnded(L_19581, LabEventHandler&lt;RoundEndedEventArgs&gt;)、 / Basis: source ServerEvents.RoundEnded(L_19581, LabEventHandler&lt;RoundEndedEventArgs&gt;),
        ///       RoundEndedEventArgs(L_22716-22725: LeadingTeam / ShowSummary, 命名空间 /       RoundEndedEventArgs(L_22716-22725: LeadingTeam / ShowSummary, namespace
        ///       LabApi.Events.Arguments.ServerEvents)、ServerEvents.OnRoundEnded(L_19722-19724);
        ///       Round.Ended 相关语义见 RoundSummary.singleton.IsRoundEnded(L_7643)。 /       Round.Ended related semantics see RoundSummary.singleton.IsRoundEnded(L_7643)
        /// 语义: 回合结束**立刻**结算 —— 原来挂在 RoundRestarted 上会迟一整回合(重开时本回合统计已被清空)。 / Semantics: settle **immediately** when the round ends — previously hooked on RoundRestarted caused a full-round delay (this round's stats were already cleared on restart)
        /// </summary>
        private void OnRoundEnded(LabApi.Events.Arguments.ServerEvents.RoundEndedEventArgs ev)
        {
            SettleRoundMvp();
            _mvpSettled = true;      // 去重: 这一回合已结算, RoundRestarted 不再重复结算 / dedupe: this round has been settled, RoundRestarted will not settle again
        }

        /// <summary>
        /// 回合重开 → 清空本回合统计 + 落盘。 / Round restart → clear this round's stats + flush to disk
        /// MVP 结算已前移到 RoundEnded(见上); 这里只保留兜底: 若 RoundEnded 没触发 / MVP settlement has moved to RoundEnded (see above); only a fallback is kept here: if RoundEnded did not fire
        /// (例如控制台直接重开地图)才补结算一次, 靠 _mvpSettled 去重, 确保不会两处都结算。 / (e.g. the map was restarted directly from the console) settle once more, deduped by _mvpSettled, ensuring it never settles in both places
        /// </summary>
        private void OnRoundRestarted()
        {
            if (!_mvpSettled) SettleRoundMvp();   // 兜底结算(正常流程已在 RoundEnded 结算过) / fallback settlement (normal flow already settled in RoundEnded)
            _mvpSettled = false;
            if (_xpFrozen)                        // v1.5: 经验冻结只作用于"当前回合", 下一回合自动恢复 / v1.5: EXP freeze only affects the "current round" and is restored automatically next round
            {
                _xpFrozen = false;
                Logger.Info(L.T("[EXPAS] 新回合开始, 经验冻结已自动解除(EXPAS xpfreeze 只冻结一个回合)", "[EXPAS] New round started, EXP freeze has been lifted automatically (EXPAS xpfreeze only freezes one round)"));
            }
            _pending.Clear();
            _roundDamage.Clear();
            _roundExp.Clear();
            _roundKills.Clear();
            _roundPoints.Clear();
            FlushAll(L.T("回合结束", "round end"));
        }

        // ========================================================================
        // 加入 / 离开 / Join / Leave
        // ========================================================================
        private void OnJoined(PlayerJoinedEventArgs ev)
        {
            Player p = ev.Player;
            if (p == null || p.IsDummy || !p.IsPlayer) return;
            try
            {
                _sessionStart[p.UserId] = DateTime.UtcNow;
                _roundDamage[p.UserId] = 0f;
                _roleDamage[p.UserId] = 0f;
                _lastRole[p.UserId] = p.Role;

                PlayerData d = GetData(p.UserId, p.Nickname);
                d.Name = p.Nickname;
                d.LastSeen = DateTime.UtcNow;
                MarkLongOffline(d);
                UpdateName(p, d);
                ApplyTitle(p, d);            // v2.0: 进服应用称号(三层策略) / v2.0: apply title on join (three-tier policy)
                SavePlayer(d);
                // 面板里同步这个玩家的 HUD 设置与数据 / sync this player's HUD settings and data in the panel
                _hudOff[p.UserId] = d.HudEnabledSet == 0;
                RefreshMyData(p, d);
                SendSssMenuTo(p);      // v1.7: 白名单玩家收到"带自定义 UI 下拉"的菜单, 其他人收到基础菜单 / v1.7: whitelisted players receive the menu "with the custom UI dropdown", others receive the basic menu

                string icons = GetIconString(d);
                if (Config.HudEnabled && !IsHudOff(p.UserId))
                    p.SendHint(L.T($"<color=#4FA8FF>欢迎 {p.Nickname} 进入到 {Config.ServerName}</color> ",
                                   $"<color=#4FA8FF>Welcome {p.Nickname} to {Config.ServerName}</color> ") +
                               $"<color=#AAAAAA>[EXPAS v{PluginVersion}]</color>{icons}", 5f);
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 加入处理失败: {ex.Message}", $"[EXPAS] Join handling failed: {ex.Message}")); }
        }

        private void OnLeft(PlayerLeftEventArgs ev)
        {
            try
            {
                string uid = null;
                try { if (ev.Player != null && !ev.Player.IsDestroyed) uid = ev.Player.UserId; } catch { }
                if (string.IsNullOrEmpty(uid)) return;

                if (_sessionStart.TryGetValue(uid, out DateTime start))
                {
                    PlayerData d = GetData(uid, null);
                    double delta = (DateTime.UtcNow - start).TotalSeconds;
                    d.PlaySeconds += delta;
                    AddTodayPlayTime(d, delta);          // v1.5: 今日游玩时间(跨天自动归零累加) / v1.5: today's playtime (auto-accumulates from zero across days)
                    d.LastSeen = DateTime.UtcNow;
                    _sessionStart.Remove(uid);
                    SavePlayer(d);
                }
                _pendingRemoveAttacker(uid);
                _menuSentTo.Remove(uid);      // v1.8.1: 玩家离开 → 清掉"已下发个性化菜单"标记(防集合无限增长) / v1.8.1: player left → clear the "personalized menu already sent" flag (prevents the set from growing forever)
                _sssCommitThrottle.Remove(uid);  // v1.8.2: 清掉 SSS 写盘/刷新节流记录 / v1.8.2: clear the SSS disk-write/refresh throttle record
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 离开处理失败: {ex.Message}", $"[EXPAS] Leave handling failed: {ex.Message}")); }
        }

        private void _pendingRemoveAttacker(string uid)
        {
            foreach (var kv in _pending.Values) kv.Remove(uid);
        }

        // ========================================================================
        // 伤害记录 / 结算 / Damage recording / settlement
        // ========================================================================
        private void OnHurting(PlayerHurtingEventArgs ev)
        {
            try
            {
                if (!ev.IsAllowed) return;                     // 别的插件已经取消这次伤害 → 不记账(Hurting 是可取消事件) / another plugin has already cancelled this damage → do not record it (Hurting is a cancellable event)
                Player atk = ev.Attacker, vic = ev.Player;
                if (atk == null || vic == null || atk.IsDestroyed || vic.IsDestroyed) return;
                if (atk == vic) return;
                if (atk.IsDummy || !atk.IsPlayer) return;      // 只统计真人玩家造成的伤害 / only count damage dealt by real players
                if (!IsCountableVictim(vic)) return;

                float dmg = 0f;
                if (ev.DamageHandler is StandardDamageHandler sdh) dmg = sdh.Damage;
                if (dmg <= 0f) return;

                PlayerData d = GetData(atk.UserId, atk.Nickname);
                if (d.LockState == LockPunishment) return;      // 🚫 惩罚锁定: 任何数据都不记录 / Punishment lock: no data is recorded

                float exp = dmg * Mathf.Max(0f, Config.DamageToExp);
                _roundDamage[atk.UserId] = GetRoundDmg(atk.UserId) + dmg;
                _roleDamage[atk.UserId] = GetRoleDmg(atk.UserId) + dmg;

                if (d.LockState == LockSelfBan)
                {
                    // 🔒 自助封禁: 伤害统计不增长, 经验只往待结算池里叠 / Self-ban: damage stats do not grow, EXP only accumulates in the pending pool
                    d.PendingDamage += dmg;
                    d.PendingExp += exp;
                    return;
                }

                if (Config.SettleOnDeath)
                {
                    if (!_pending.TryGetValue(vic.NetworkId, out var map))
                    {
                        map = new Dictionary<string, float>();
                        _pending[vic.NetworkId] = map;
                    }
                    map[atk.UserId] = (map.TryGetValue(atk.UserId, out float old) ? old : 0f) + dmg;
                }
                else
                {
                    d.TotalDamage += dmg;
                    ApplyExp(d, exp);
                }
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 伤害记录失败: {ex.Message}", $"[EXPAS] Damage recording failed: {ex.Message}")); }
        }

        private void OnDeath(PlayerDeathEventArgs ev)
        {
            Player vic = ev.Player;
            if (vic == null) return;
            try
            {
                // v1.5: 把"死亡前的角色"一起传进结算 —— PlayerDeathEventArgs.OldRole(L_25037) 是 / v1.5: pass the "role before death" into settlement — PlayerDeathEventArgs.OldRole(L_25037) is
                //       死亡瞬间的角色快照, 比事后读 vic.Role(可能已变 Spectator)可靠。 /       the role snapshot at the moment of death, more reliable than reading vic.Role afterwards (which may have become Spectator)
                SettlePending(vic, ev.OldRole);        // 受害者死亡 → 把伤害换算经验给攻击者 / victim died → convert damage into EXP for the attackers

                if (vic.IsPlayer && !vic.IsDummy)
                {
                    PlayerData vd = GetData(vic.UserId, vic.Nickname);
                    vd.Deaths++;
                    _roleDamage[vic.UserId] = 0f;      // 自己角色伤害重置 / reset own role damage
                    SavePlayer(vd);
                    UpdateName(vic, vd);
                }
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 死亡结算失败: {ex.Message}", $"[EXPAS] Death settlement failed: {ex.Message}")); }
        }

        /// <summary>
        /// v1.4: 换角色改为事件驱动 —— 重置"当前角色已造成伤害"。 / v1.4: role change is now event-driven — reset "damage dealt by the current role"
        /// 依据: 文档 01_事件系统写法大全 L64/L166(角色: ChangingRole/ChangedRole(换角色)), / Basis: document 01_事件系统写法大全 L64/L166 (role: ChangingRole/ChangedRole)
        ///       源码 PlayerEvents.ChangedRole(L_17538, LabEventHandler&lt;PlayerChangedRoleEventArgs&gt;)、 /       source PlayerEvents.ChangedRole(L_17538, LabEventHandler&lt;PlayerChangedRoleEventArgs&gt;),
        ///       PlayerChangedRoleEventArgs(L_24700: Player/OldRole/NewRole/ChangeReason/SpawnFlags)。
        /// HudLoop 里的 _lastRole 轮询**保留**作兜底(事件未触发时仍能归零)。 / the _lastRole polling in HudLoop is **kept** as a fallback (still zeroes out if the event does not fire)
        /// </summary>
        private void OnChangedRole(PlayerChangedRoleEventArgs ev)
        {
            try
            {
                Player p = ev.Player;
                if (p == null || p.IsDestroyed || p.IsDummy || !p.IsPlayer) return;

                _roleDamage[p.UserId] = 0f;      // 当前角色伤害归零 / current role damage reset to zero
                _lastRole[p.UserId] = p.Role;    // 同步轮询快照, 避免下一周期又被判成"换角色"重复归零 / sync the polling snapshot to avoid being treated as a "role change" and zeroed again next cycle
                SavePlayer(GetData(p.UserId, p.Nickname));
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 换角色处理失败: {ex.Message}", $"[EXPAS] Role change handling failed: {ex.Message}")); }
        }

        /// <summary>
        /// 受害者死亡 → 结算: 伤害入账 / 经验 / 击杀数 / 积分 / 击杀确认提示。 / Victim died → settlement: damage crediting / EXP / kill count / points / kill confirmation notice
        /// v1.5(改造2): 增加 victimRole 参数(= PlayerDeathEventArgs.OldRole, LabApi L_25037), / v1.5 (rework 2): added the victimRole parameter (= PlayerDeathEventArgs.OldRole, LabApi L_25037),
        ///   用于记录"被击杀的是什么角色"(LastKillRole / LastKillRoleKind), 并据此生成击杀块。 /   used to record "what role was killed" (LastKillRole / LastKillRoleKind) and generate the kill block from it
        /// </summary>
        private void SettlePending(Player victim, RoleTypeId victimRole)
        {
            if (!_pending.TryGetValue(victim.NetworkId, out var map)) return;
            _pending.Remove(victim.NetworkId);
            if (map.Count == 0) return;

            float total = 0f;
            foreach (var kv0 in map) total += Mathf.Max(0f, kv0.Value);
            string victimName = victim.Nickname;
            string victimRoleName = victimRole.ToString();               // 例 NtfSergeant(dummy 也照记) / e.g. NtfSergeant (dummies are recorded too)
            string victimRoleKind = KillRoleKind(victim, victimRole);    // 人形 / SCP / 人机 / 其它 / human / SCP / dummy / other

            foreach (var kv in map)
            {
                if (kv.Value <= 0f) continue;
                PlayerData d = GetDataById(kv.Key);
                if (d == null) continue;

                float share = total > 0f ? kv.Value / total : 1f;                       // 对该目标的伤害占比 / damage share against this target
                float worth = Config.PointsByShare ? share * Config.PointsFullValue
                                                   : kv.Value / Mathf.Max(1f, Config.PointsFullDamage) * Config.PointsFullValue;
                float exp = kv.Value * Mathf.Max(0f, Config.DamageToExp);

                if (d.LockState == LockPunishment) continue;    // 🚫 惩罚锁定: 结算也不入账 / Punishment lock: settlement does not credit either
                if (d.LockState == LockSelfBan)
                {
                    // 🔒 自助封禁: 经验/伤害进待入账池(解除时一次性入账) / Self-ban: EXP/damage go into the pending pool (credited at once when lifted)
                    d.PendingDamage += kv.Value;
                    d.PendingExp += exp;
                    continue;
                }

                d.TotalDamage += kv.Value;
                d.Kills++;
                d.Points += worth;                       // 积分入账 / credit points
                d.LastKillName = victimName;
                d.LastKillRole = victimRoleName;         // v1.5: 击杀确认 ① 被击杀的角色类型 / v1.5: kill confirmation ① the killed role type
                d.LastKillRoleKind = victimRoleKind;     // 人形 / SCP / 人机 / human / SCP / dummy
                d.LastKillDamage = kv.Value;
                d.LastKillShare = share * 100f;
                d.LastKillPoints = worth;
                RecordRoundKill(d.UserId);
                RecordRoundPoints(d.UserId, worth);
                ApplyExp(d, exp);
                SavePlayer(d);

                Player atk = FindOnline(d.UserId);
                // 屏幕提示只在"允许使用 Hint 通道"时发(HudSurface 0=仅面板, 不干扰其它插件) / the on-screen notice is only sent when the "Hint channel" is allowed (HudSurface 0 = panel only, do not disturb other plugins)
                if (atk != null && Config.HudSurface != 0)
                {
                    string block = BuildKillFeedText(atk, d);   // kill_line1/2/3 渲染结果(此刻定格) / kill_line1/2/3 render result (frozen at this moment)
                    // ① v1.5 默认做法: 把击杀块并入 HUD 末尾 —— HUD 每 HudInterval 秒重建, / ① v1.5 default behavior: append the kill block to the end of the HUD — the HUD is rebuilt every HudInterval seconds,
                    //    单独发一条 Hint 会被下一次刷新立刻覆盖, 所以必须并入 HUD 才看得见。 /    a standalone Hint would be immediately overwritten by the next refresh, so it must be merged into the HUD to be visible
                    if (Config.KillNoticeSeconds > 0f)
                    {
                        _killNoticeText[d.UserId] = block;
                        _killNoticeUntil[d.UserId] = DateTime.UtcNow.AddSeconds(Config.KillNoticeSeconds);
                    }
                    // ② 可选(默认关): 保留旧的"另发一条独立 Hint"行为, 供需要的人打开 / ② optional (off by default): keep the old "send a separate standalone Hint" behavior for those who need it
                    if (Config.KillNoticeAsHint)
                        atk.SendHint(block, 4f);
                }
            }
        }

        /// <summary>
        /// 把"被击杀的角色"归类成 HUD 上显示的类型: 人机 / SCP / 人形 / 其它。 / Classify the "killed role" into the type shown on the HUD: dummy / SCP / human / other
        /// 依据(全部来自源码): / Basis (all from the source):
        ///   · 人机: LabApi.Features.Wrappers.Player.IsDummy —— 本插件已有用法(IsCountableVictim/OnJoined); /   · dummy: LabApi.Features.Wrappers.Player.IsDummy — already used in this plugin (IsCountableVictim/OnJoined);
        ///   · SCP : 游戏本体 PlayerRoles.PlayerRolesUtils.GetTeam(this RoleTypeId)(G_L90895-90901, /   · SCP  : game PlayerRoles.PlayerRolesUtils.GetTeam(this RoleTypeId)(G_L90895-90901,
        ///           取角色模板 Team) 与 Team 枚举(Team.SCPs, G_L90425-90434); /           gets the role template Team) and the Team enum (Team.SCPs, G_L90425-90434);
        ///           LabApi 侧同口径实现见 PlayerRoleExtensions.IsScp(this RoleTypeId)(L_16880-16883: /           the same implementation on the LabApi side is PlayerRoleExtensions.IsScp(this RoleTypeId)(L_16880-16883:
        ///           roleType.GetTeam() == Team.SCPs);
        ///   · 人形: 游戏本体 PlayerRolesUtils.IsHuman(this RoleTypeId)(G_L90933-90941: /   · human: game PlayerRolesUtils.IsHuman(this RoleTypeId)(G_L90933-90941:
        ///           team != Team.Dead && team != Team.SCPs && !role.IsFlamingo());
        ///   · 其余(旁观者/死亡/教程等非"可操作角色")→ 其它。 /   · everything else (spectator/dead/tutorial and other non-"playable roles") → other
        /// 注: 先判 dummy 再判阵营 —— dummy 可以披人形/SCP 的皮, 单独标出来更好认。 / Note: check dummy before team — a dummy can wear a human/SCP skin, so tagging it separately makes it easier to recognize
        /// </summary>
        private static string KillRoleKind(Player victim, RoleTypeId role)
        {
            try
            {
                if (victim != null && !victim.IsDestroyed && victim.IsDummy) return L.T("人机", "Dummy");
                if (role.GetTeam() == Team.SCPs) return "SCP";
                if (role.IsHuman()) return L.T("人形", "Human");
                return L.T("其它", "Other");
            }
            catch { return "-"; }
        }

        private bool IsCountableVictim(Player vic)
        {
            if (vic.IsDummy)
                return !Config.OnlyRealPlayers && Config.CountDummies;
            if (Config.OnlyRealPlayers && !vic.IsPlayer) return false;
            if (vic.IsSCP && !Config.CountScp) return false;
            return vic.IsAlive;
        }

        /// <param name="ignoreFreeze">true = 无视"本回合经验冻结"强制入账(仅用于解除 selfban 时补入待结算经验)。 / true = ignore "this round's EXP freeze" and force crediting (only used to credit pending EXP when unbanning selfban)</param>
        private void ApplyExp(PlayerData d, float exp, bool ignoreFreeze = false)
        {
            if (d == null || exp <= 0f) return;
            // v1.5 经验冻结(EXPAS xpfreeze / freezexp): 当前回合不再产生任何经验增长, 回合重开自动恢复 / v1.5 EXP freeze (EXPAS xpfreeze / freezexp): no EXP growth this round, automatically restored on round restart
            if (_xpFrozen && !ignoreFreeze) return;
            d.TotalExp += exp;
            d.Exp += exp;
            RecordRoundExp(d.UserId, exp);     // 本回合经验(供排行榜/API) / this round's EXP (for leaderboard/API)

            int ups = 0;
            while (d.Level < Config.MaxLevel && d.Exp >= ExpNeeded(d.Level))
            {
                d.Exp -= ExpNeeded(d.Level);
                d.Level++;
                ups++;
            }

            Player owner = FindOnline(d.UserId);
            if (ups > 0 && owner != null)
            {
                // v1.7: 升级提示由「全员广播」改为「个人 Hint」—— / v1.7: level-up notice changed from "broadcast to all" to "personal Hint" —
                //   Server.SendBroadcast 会在服务器控制台/日志里留下脏行 /   Server.SendBroadcast leaves dirty lines in the server console/log
                //   (`[BROADCAST FROM SERVER] [color=#FFD700]…`), 个人 Hint 只在玩家屏幕上显示, 不写日志。 /   (`[BROADCAST FROM SERVER] [color=#FFD700]…`), while a personal Hint only appears on the player's screen and is not logged
                //   (MVP 公告仍旧走广播, 那是全服可见的回合结算信息。) /   (MVP announcements still use broadcast, as that is round-settlement info visible to the whole server)
                if (Config.LevelUpNotice > 0 && !IsHudOff(owner.UserId))
                    owner.SendHint(
                        L.T($"<color=#FFD700>【EXPAS】升级! 您现在是 {d.Level} 级</color>\n",
                            $"<color=#FFD700>[EXPAS] Level up! You are now level {d.Level}</color>\n") +
                        L.T($"<color=#55FF55>距离下一级还差 {Mathf.Max(0f, ExpNeeded(d.Level) - d.Exp):F0} 经验</color>",
                            $"<color=#55FF55>{Mathf.Max(0f, ExpNeeded(d.Level) - d.Exp):F0} EXP to next level</color>"),
                        Mathf.Clamp(Config.LevelUpNotice, 1, 30));
                UpdateName(owner, d);
                Logger.Info(L.T($"[EXPAS] {d.Name}({d.UserId}) 升到 {d.Level} 级", $"[EXPAS] {d.Name}({d.UserId}) reached level {d.Level}"));
            }
            SavePlayer(d);
        }

        /// <summary>
        /// 按 UserId 找在线玩家。v1.4: 改为遍历 Player.ReadyList(L_14003 = IsDummy || (IsPlayer &amp;&amp; IsReady)), / Find an online player by UserId. v1.4: changed to iterate Player.ReadyList(L_14003 = IsDummy || (IsPlayer &amp;&amp; IsReady)),
        /// 再滤掉人机 —— 即"真人在线", 未认证连接(IsReady=false)不再被误判成在线。 / then filter out dummies — i.e. "real players online", unauthenticated connections (IsReady=false) are no longer misjudged as online
        /// </summary>
        private static Player FindOnline(string userId)
        {
            foreach (Player p in Player.ReadyList.Where(p => p.IsPlayer && !p.IsDummy))
            {
                if (p == null || p.IsDestroyed) continue;
                if (p.UserId == userId) return p;
            }
            return null;
        }

        // ========================================================================
        // 锁定 / 解锁 / Lock / Unlock
        // ========================================================================
        /// <summary>
        /// 设置锁定状态(规格口径 0=none 1=punishment 2=selfban 3=inactive)。 / Set the lock state (spec definition 0=none 1=punishment 2=selfban 3=inactive)
        /// ★ 玩家不能自助锁定 —— 本方法只由管理端指令(EXPAS set ... lock / EXPAS lock / unlock / full)调用。 / ★ Players cannot lock themselves — this method is only called by admin commands (EXPAS set ... lock / EXPAS lock / unlock / full)
        /// · selfban 解除时: 把封禁期间叠加的伤害与经验**一次性入账**(保留 v1.4 既有逻辑)。 / · when selfban is lifted: credit the damage and EXP accumulated during the ban **at once** (keeping the existing v1.4 logic)
        /// </summary>
        public void SetLock(PlayerData d, int newState, string reason)
        {
            if (d == null) return;
            newState = Mathf.Clamp(newState, LockNone, LockInactive);
            int oldState = d.LockState;

            // 🔒 解除: 把封禁期间叠加的全部数据一次性入账(强制入账, 不受"经验冻结"影响) / Unlock: credit all data accumulated during the ban at once (forced crediting, unaffected by "EXP freeze")
            if (oldState == LockSelfBan && newState != LockSelfBan)
            {
                float pendingExp = d.PendingExp;
                float pendingDmg = d.PendingDamage;
                d.PendingExp = 0f;
                d.PendingDamage = 0f;
                if (pendingDmg > 0f) d.TotalDamage += pendingDmg;
                if (pendingExp > 0f) ApplyExp(d, pendingExp, true);
            }
            d.LockState = newState;
            d.LongOffline = newState == LockInactive;   // 旧字段与新表双向同步(兼容旧读法) / two-way sync between the old field and the new table (compatible with old readers)
            SavePlayer(d);

            Player owner = FindOnline(d.UserId);
            if (owner != null)
            {
                UpdateName(owner, d);
                string txt = newState switch
                {
                    // v1.6: 图标统一走 LockIcon(修 unicode 缺字), 不再硬编码 emoji / v1.6: icons uniformly go through LockIcon (fix missing unicode glyphs), no longer hardcoded emoji
                    LockPunishment => L.T($"{LockIcon(LockPunishment)} 已进入惩罚锁定: 伤害与经验都不再记录",
                        $"{LockIcon(LockPunishment)} Punishment lock enabled: damage and EXP are no longer recorded"),
                    LockSelfBan => L.T($"{LockIcon(LockSelfBan)} 已进入自助封禁: 伤害不增长, 经验仍在叠加, 解除后一次性入账",
                        $"{LockIcon(LockSelfBan)} Self-ban enabled: damage does not grow, EXP still accumulates and is credited at once when unbanned"),
                    LockInactive => L.T($"{LockIcon(LockInactive)} 已标记为长期未上线(inactive)",
                        $"{LockIcon(LockInactive)} Marked as inactive"),
                    _ => L.T("✅ 数据锁定已解除", "✅ Data lock removed")
                };
                owner.SendHint($"<color=#FFD700>【EXPAS】{txt}</color>", 5f);
            }
            Logger.Info(L.T($"[EXPAS] {d.Name}({d.UserId}) 锁定状态 {LockName(oldState)} → {LockName(newState)} ({reason})",
                $"[EXPAS] {d.Name}({d.UserId}) lock state {LockName(oldState)} → {LockName(newState)} ({reason})"));
        }

        /// <summary>锁定状态的可读名(规格口径)。 / Readable name of the lock state (spec definition)</summary>
        public static string LockName(int s)
        {
            switch (s)
            {
                case LockPunishment: return L.T("punishment(惩罚)", "punishment");
                case LockSelfBan: return L.T("selfban(自助封禁)", "selfban");
                case LockInactive: return L.T("inactive(长期未上线)", "inactive");
                default: return L.T("none(正常)", "none");
            }
        }

        /// <summary>
        /// 锁定状态对应图标 —— **v1.6 统一出口(修 unicode 缺字)**。 / Icon for the lock state — **v1.6 unified exit (fix missing unicode glyphs)**
        /// 依据: 游戏 TMP 字体不含 emoji 字形、也没有缺字回退(在 game-src 里 grep / Basis: the game TMP font contains no emoji glyphs and has no glyph fallback (grep
        ///       "missing glyph" / "fallbackFontAsset" / "SpriteAsset" 全 0 命中), /       "missing glyph" / "fallbackFontAsset" / "SpriteAsset" in game-src returns 0 hits),
        ///       所以 🚫🔒🕒 必然渲染成缺字方框 → 默认改成字体安全的中文标记。 /       so 🚫🔒🕒 necessarily render as missing-glyph boxes → the default was changed to font-safe Chinese markers
        /// 规则: IconPreset=0(默认) → [锁]/[禁]/[离]; IconPreset=1 → 🚫/🔒/🕒(与旧版完全一致, 可回退)。 / Rule: IconPreset=0 (default) → [锁]/[禁]/[离]; IconPreset=1 → 🚫/🔒/🕒 (fully identical to the old version, reversible)
        ///       三个 IconLock* 配置可覆盖 —— 填了"非默认文本"就以配置为准("预设只决定默认文本")。 /       the three IconLock* configs can override — a "non-default text" in them takes precedence ("the preset only decides the default text")
        /// </summary>
        public static string LockIcon(int s)
        {
            switch (s)
            {
                case LockPunishment: return ResolveIcon(0, "[锁]", "🚫");
                case LockSelfBan: return ResolveIcon(1, "[禁]", "🔒");
                case LockInactive: return ResolveIcon(2, "[离]", "🕒");
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 解析实际要显示的图标文本(0=惩罚 1=自助封禁 2=长期未上线)。 / Parse the icon text actually displayed (0=punishment 1=self-ban 2=long-time offline)
        /// · 配置里填的既不是"中文默认"也不是"emoji 默认" → 视为用户自定义, 以配置为准; / · if the config value is neither the "Chinese default" nor the "emoji default" → treat it as user-custom and use the config;
        /// · 否则按 IconPreset 取默认文本(0=中文 / 1=emoji)。 / · otherwise pick the default text by IconPreset (0=Chinese / 1=emoji)
        /// </summary>
        private static string ResolveIcon(int which, string cnDefault, string emojiDefault)
        {
            try
            {
                PluginConfig c = Instance != null ? Instance.Config : null;
                string cfg = null;
                bool emoji = false;
                if (c != null)
                {
                    emoji = c.IconPreset == 1;
                    if (which == 0) cfg = c.IconLockPunishment;
                    else if (which == 1) cfg = c.IconLockSelfBan;
                    else cfg = c.IconLockInactive;
                }
                if (!string.IsNullOrEmpty(cfg) && cfg != cnDefault && cfg != emojiDefault) return cfg;
                return emoji ? emojiDefault : cnDefault;
            }
            catch { return cnDefault; }
        }

        // ========================================================================
        // 名字前缀 / Name prefix
        // ========================================================================
        public void UpdateName(Player p, PlayerData d)
        {
            if (p == null || p.IsDestroyed || d == null) return;
            try
            {
                if (!Config.NamePrefix) return;
                string prefix = string.Format(Config.NameFormat ?? "[Lv.{0}]", d.Level);
                if (Config.NameIcons) prefix += GetIconString(d);
                string desired = prefix + p.Nickname;
                if (p.DisplayName != desired) p.DisplayName = desired;
            }
            catch (Exception ex) { Logger.Info(L.T($"[EXPAS] 名前缀失败: {ex.Message}", $"[EXPAS] Name prefix failed: {ex.Message}")); }
        }

        public string GetIconString(PlayerData d)
        {
            var sb = new StringBuilder();
            string ic = LockIcon(d.LockState);           // [锁] punishment / [禁] selfban / [离] inactive (或 emoji / or emoji)
            if (!string.IsNullOrEmpty(ic)) sb.Append(ic);
            // 旧数据只有 longOffline 时也标 [离](与 LockInactive 去重) / also mark [离] when old data only has longOffline (deduped with LockInactive)
            if (d.LongOffline && d.LockState != LockInactive) sb.Append(LockIcon(LockInactive));
            return sb.Length > 0 ? " " + sb.ToString() : string.Empty;
        }

        // ========================================================================
        // HUD 循环 / HUD loop
        // ========================================================================
        private IEnumerator<float> HudLoop()
        {
            while (true)
            {
                float interval = Mathf.Max(0.2f, Config.HudInterval);
                try
                {
                    if (Config.HudEnabled)
                    {
                        // v1.4: 改遍历 Player.ReadyList(L_14003 = IsDummy || (IsPlayer && IsReady)), / v1.4: iterate Player.ReadyList(L_14003 = IsDummy || (IsPlayer && IsReady)),
                        // 再滤掉人机 → 只给"真人玩家"刷 HUD, 未认证连接(IsReady=false)不参与 / then filter out dummies → only refresh the HUD for "real players", unauthenticated connections (IsReady=false) are excluded
                        foreach (Player p in Player.ReadyList.Where(p => p.IsPlayer && !p.IsDummy))
                        {
                            if (p == null || p.IsDestroyed) continue;
                            if (IsHudOff(p.UserId)) continue;

                            PlayerData d = GetData(p.UserId, p.Nickname);

                            // 角色切换 → 当前角色伤害重置 / role switched → reset current role damage
                            RoleTypeId cur = p.Role;
                            if (_lastRole.TryGetValue(p.UserId, out RoleTypeId last) && last != cur)
                            {
                                _roleDamage[p.UserId] = 0f;
                                SavePlayer(d);
                            }
                            _lastRole[p.UserId] = cur;

                            // 面板里的「我的数据」不用每秒刷, 每 3 个周期刷一次 / the "My data" in the panel does not need per-second refresh; refresh every 3 cycles
                            _panelTick++;
                            if (_panelTick % 3 == 0) RefreshMyData(p, d);

                            // 【修复 2026-09-29】原来这里是 `if (!p.IsAlive) continue;` / [Fix 2026-09-29] this used to be `if (!p.IsAlive) continue;`
                            //   → 大厅/未开局/观察者/刚复活前，HUD 一条都不发，屏幕上一个字都没有 /   → in the lobby / before round start / spectator / just before respawn, not a single HUD line was sent, so nothing appeared on screen
                            //     (用户反馈"看不到文字"就是这个, 截图正是大厅画面)。 /     (this is exactly the user-reported "cannot see text"; the screenshot was the lobby screen)
                            //   改为受 HudInLobby 控制: 默认 true = 任何状态都显示 HUD; 设 false 则只在存活时显示。 /   changed to be controlled by HudInLobby: default true = show HUD in any state; false = only show while alive
                            if (!Config.HudInLobby && !p.IsAlive) continue;
                            // UI 隔离: HudSurface 0=只走 SSS 面板(关闭屏幕 HUD, 不占用其它插件的 Hint 通道) / UI isolation: HudSurface 0 = SSS panel only (screen HUD off, does not occupy other plugins' Hint channel)
                            if (Config.HudSurface != 0)
                                p.SendHint(BuildHud(p, d), Mathf.Max(Config.HudClear, interval + 0.3f));
                        }
                    }

                    // v2.4: 主线程取走后台解完的音频并播出(非 WAV 格式由 ffmpeg 后台解码) / v2.4: main thread takes decoded audio from the background and plays it (non-WAV formats are decoded in the background by ffmpeg)
                    PumpPendingAudio();

                    _saveTimer += interval;
                    if (_saveTimer >= Mathf.Max(10f, Config.AutoSave))
                    {
                        _saveTimer = 0f;
                        FlushAll(L.T("自动保存", "auto save"));
                        MarkLongOfflinePass();
                    }
                }
                catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] HUD 循环异常: {ex.Message}", $"[EXPAS] HUD loop error: {ex.Message}")); }
                yield return Timing.WaitForSeconds(interval);
            }
        }

        private bool IsHudOff(string userId) => _hudOff.TryGetValue(userId, out bool off) && off;

        public void ToggleHud(string userId)
        {
            bool off = !IsHudOff(userId);
            _hudOff[userId] = off;
            // 同步进玩家数据(与 SSS 面板同一个开关, 换机/重进保持) / sync into player data (same switch as the SSS panel; persists across machine changes/rejoins)
            try
            {
                PlayerData d = GetData(userId, null);
                d.HudEnabledSet = off ? 0 : 1;
                SavePlayer(d);
            }
            catch { }
        }

        private float GetRoundDmg(string uid) => _roundDamage.TryGetValue(uid, out float v) ? v : 0f;
        private float GetRoleDmg(string uid) => _roleDamage.TryGetValue(uid, out float v) ? v : 0f;

        private string BuildHud(Player p, PlayerData d)
        {
            // v1.8.5 布局与配置模板的优先级: / v1.8.5 priority between layouts and config templates:
            //   · 玩家**实际选中的、来自文件的自定义布局**(HudLayout != default 且布局文件存在) → 布局渲染 /   · the player's **actually selected custom layout from a file** (HudLayout != default and the layout file exists) → render the layout
            //     (layout 的 row/level/kill/fold/custom 行; file 里的 owner 归属/白名单已在此前校验过) /     (layout row/level/kill/fold/custom lines; the owner ownership/whitelist in the file has already been validated earlier)
            //   · 其它一切情况(玩家没选 / 选的是 default / 布局文件被删) → **走 config.yml 文本模板**, /   · all other cases (player selected nothing / selected default / layout file deleted) → **use config.yml text templates**,
            //     保证 hud_line1..4 / hud_level1/2 的修改一定生效(旧版 default 布局把模板顶掉是 bug)。 /     guaranteeing that changes to hud_line1..4 / hud_level1/2 always take effect (the old default layout overriding templates was a bug)
            HudLayoutDef layout = GetLayoutFor(d);
            bool customLayout = layout != null && layout.FromFile
                && !string.Equals(layout.Key, "default", StringComparison.OrdinalIgnoreCase);

            if (IsHudCollapsed(d))
            {
                if (customLayout && layout.Fold.Count > 0) return BuildFoldedFromLayout(p, d, layout);
                return BuildHudCollapsed(p, d);
            }

            // 自定义布局 → 布局文件渲染 / custom layout → render from layout file
            if (customLayout) return BuildHudFromLayout(p, d, layout);

            // v1.2: 文案全部来自配置文件模板, 服务器重启后重新读取生效 / v1.2: all text comes from config file templates, re-read and applied after server restart
            if (Config.UseTextTemplates) return BuildHudTemplated(p, d);

            var sb = new StringBuilder();
            string blue = "<color=#4FA8FF>";
            string grey = "<color=#BFBFBF>";
            string end = "</color>";

            // 第1行(蓝) / line 1 (blue)
            sb.AppendLine(L.T($"{blue}欢迎 {p.Nickname} 进入到 {Config.ServerName}{end}", $"{blue}Welcome {p.Nickname} to {Config.ServerName}{end}"));

            // 第2行: 游玩天数 / 时间段 / 准确游玩时间 / line 2: days played / time period / exact playtime
            int days = DaysSince(d.FirstSeen, DateTime.UtcNow);
            double playSec = d.PlaySeconds;
            if (_sessionStart.TryGetValue(d.UserId, out DateTime start))
                playSec += (DateTime.UtcNow - start).TotalSeconds;
            sb.AppendLine(L.T($"当前已经游玩了 {days} 天  当前时间段是 {GetTimeZoneLabel()} {GetLocalTimeText()}",
                              $"Played {days} days  Local time {GetTimeZoneLabel()} {GetLocalTimeText()}") +
                          L.T($"  您准确的游玩时间是 {FormatDuration(playSec)}",
                              $"  Total playtime {FormatDuration(playSec)}"));

            // 第3行: 回合伤害 / 当前角色伤害 / 总伤害 / line 3: round damage / current role damage / total damage
            string lockNote = d.LockState == LockSelfBan && d.PendingDamage > 0f
                ? L.T($" {grey}(封禁中 +{d.PendingDamage:F0}){end}", $" {grey}(banned +{d.PendingDamage:F0}){end}") : string.Empty;
            sb.AppendLine(L.T($"当前回合已造成 {GetRoundDmg(d.UserId):F0}  当前使用的角色已造成 {GetRoleDmg(d.UserId):F0}",
                              $"Round damage {GetRoundDmg(d.UserId):F0}  Role damage {GetRoleDmg(d.UserId):F0}") +
                          L.T($"  总伤害为 {d.TotalDamage:F0}{lockNote}",
                              $"  Total damage {d.TotalDamage:F0}{lockNote}"));

            // 第4行: 版本 + 图标 / line 4: version + icons
            sb.AppendLine($"{grey}EXPAS v{PluginVersion}{end}{GetIconString(d)}");

            // 等级块(默认右下角; SSS 面板里可改对齐) / level block (bottom-right by default; alignment can be changed in the SSS panel)
            float need = ExpNeeded(d.Level);
            sb.Append($"<align={AlignTag(GetLevelAlign(d))}>");
            sb.AppendLine(L.T($"{blue}您当前有 {d.Level} 级{end}", $"{blue}Level {d.Level}{end}"));
            sb.Append(L.T($"距离下一次升级还差 {Mathf.Max(0f, need - d.Exp):F0} 经验", $"{Mathf.Max(0f, need - d.Exp):F0} EXP to next level"));
            sb.Append("</align>");

            // v1.5 击杀确认: 未过期的击杀块追加到 HUD 末尾(与模板版 BuildHudTemplated 同一处理) / v1.5 kill confirmation: append non-expired kill blocks to the end of the HUD (same handling as the template version BuildHudTemplated)
            string killNotice = KillNoticeBlock(p, d);
            if (!string.IsNullOrEmpty(killNotice))
            {
                sb.AppendLine();
                sb.AppendLine(killNotice);
            }

            // 外层: 水平对齐 / 字号(字号×屏幕缩放) / 行距 / 垂直位置 / outer: horizontal alignment / font size (font size × screen scale) / line height / vertical offset
            // 有效值等于默认时**不输出**对应标签, 这样随时能完全回落成原生渲染 / when the effective value equals the default, **do not output** the corresponding tag, so it can always fall back to native rendering
            StringBuilder head = new StringBuilder();
            StringBuilder tail = new StringBuilder();

            head.Append($"<align={AlignTag(GetHAlign(d))}>");
            tail.Insert(0, "</align>");

            if (Config.HudUseTags)
            {
                // v1.6: 最终字号 = 字号% × 屏幕缩放% × 分辨率档位倍率 × 整体缩放% ÷ 10000 (夹 20..240) / v1.6: final font size = size% × screen scale% × resolution preset multiplier × global scale% ÷ 10000 (clamped 20..240)
                int sizePercent = FinalSizePercent(p, d);
                if (sizePercent != 100)
                {
                    head.Append(Config.HudSizeStyle == 1
                        ? $"<size={(sizePercent - 100 >= 0 ? "+" : string.Empty)}{sizePercent - 100}>"
                        : $"<size={sizePercent}%>");
                    tail.Insert(0, "</size>");
                }

                int lh = GetLineHeightPercent(d);
                if (lh != 100)
                {
                    head.Append($"<line-height={lh}%>");
                    tail.Insert(0, "</line-height>");
                }

                int vo = GetVOffset(d);
                if (vo != 0)
                {
                    head.Append($"<voffset={vo}>");
                    tail.Insert(0, "</voffset>");
                }
            }

            return head.ToString() + sb.ToString() + tail.ToString();
        }

        private void FlushAll_Removed() { }
    }

    // ============================================================================
    // 命令 / Commands
    // ============================================================================
    // ============================================================================
    // 命令 —— 只注册在 RA 面板 + 服务器控制台 / Commands — registered only on the RA panel + server console
    //   ★ v1.5: 已删除 [CommandHandler(typeof(ClientCommandHandler))] —— /   ★ v1.5: [CommandHandler(typeof(ClientCommandHandler))] has been removed —
    //     玩家在游戏内**不能**执行任何 EXPAS 命令; 原玩家自助分支(hud/size/lock/unlock/info)整段删除。 /     players **cannot** execute any EXPAS command in-game; the whole player self-service branch (hud/size/lock/unlock/info) was deleted
    //   主命令 EXPAS(别名 expsr / expr), 语法照 BA-管理指令 8.2 参考实现: /   main command EXPAS (aliases expsr / expr), syntax follows the BA-管理指令 8.2 reference implementation:
    //       EXPAS <set|add> <玩家ID> <字段> <值> /       EXPAS <set|add> <player ID> <field> <value>
    //   8.3 等级控制子指令: addlevel / restartlevel / xpfreeze(freezexp) /   8.3 level control subcommands: addlevel / restartlevel / xpfreeze (freezexp)
    // ============================================================================
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(GameConsoleCommandHandler))]
    public class ExpCommand : ICommand, IUsageProvider
    {
        public string Command => "EXPAS";
        public string[] Aliases => new[] { "expsr", "expr" };
        public string Description => L.T("EXPAS 经验扩展插件系统: 经验/称号/音频/状态 (Experience, eXtended Plugins, Audio, and Status System)", "EXPAS experience extension plugin system: EXP/Title/Audio/Status (Experience, eXtended Plugins, Audio, and Status System)");
        public string[] Usage => new[]
        {
            L.T("<子命令> [参数]                    输入 EXPAS 看总览; EXPAS <关键字> 匹配(如 EXPAS se)",
                "<subcommand> [args]                Type EXPAS for an overview; EXPAS <keyword> to match (e.g. EXPAS se)"),
            L.T("set|add <玩家ID> <字段> <值>       字段: level exp points lock total_damage total_kills played_days today_play_time total_play_time",
                "set|add <playerID> <field> <value>   Fields: level exp points lock total_damage total_kills played_days today_play_time total_play_time"),
            L.T("info|lock|unlock|full|reset <玩家ID>   查询 / 锁定(0-3) / 解锁 / 惩罚 / 清空数据",
                "info|lock|unlock|full|reset <playerID>   Query / lock (0-3) / unlock / punish / clear data"),
            L.T("title <玩家ID> <0|1|2|3> [文本]    称号: 0不干预 1无称号才设 2强制替换 3彻底清除",
                "title <playerID> <0|1|2|3> [text]    Title: 0 do nothing 1 set only if none 2 force replace 3 clear completely"),
            L.T("audio play <文件.wav>|list|stop|call   音频播放 | mvpmusic list|play <玩家ID>   第一名音乐盒",
                "audio play <file.wav>|list|stop|call   Audio playback | mvpmusic list|play <playerID>   MVP music"),
            L.T("layout|whitelist|arsa|config|time|line|custom|mvp|top|api|save|help   其它(详 EXPAS <关键字>)",
                "layout|whitelist|arsa|config|time|line|custom|mvp|top|api|save|help   Others (see EXPAS <keyword>)"),
            L.T("<玩家ID> 三种写法: ①序列ID(如 3) ②玩家名(可只写一部分) ③Steam64/UserId; ①②需在线, ③离线也能改",
                "<playerID> three forms: ① sequence ID (e.g. 3) ② player name (partial allowed) ③ Steam64/UserId; ① and ② require online, ③ works offline"),
            L.T("① ② ③ 的开关在 config.yml: lookup_by_seq_id / lookup_by_name / lookup_by_steam / name_lookup_contains",
                "Toggles for ① ② ③ are in config.yml: lookup_by_seq_id / lookup_by_name / lookup_by_steam / name_lookup_contains")
        };

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            ExpasPlugin pl = ExpasPlugin.Instance;
            if (pl == null) { response = L.T("EXPAS 未启用", "EXPAS is not enabled"); return false; }

            if (arguments.Count < 1)
            {
                // 简略总览(而不是一屏长文本), 让管理员一眼看懂怎么用 / brief overview (instead of a full screen of long text), so admins can understand usage at a glance
                response = pl.ExpasHelpText(string.Empty);
                return false;
            }

            // 命令层不再区分"玩家/管理端": 玩家已经进不到这条命令, 一律按管理端处理 / command layer no longer distinguishes "player/admin": players can no longer reach this command, so treat it as admin everywhere
            return pl.HandleAdminSub(arguments.At(0).ToLowerInvariant(), arguments, sender, out response);
        }
    }
}
