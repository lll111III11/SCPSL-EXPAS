// ============================================================================
//   EXPAS —— 称号系统(NP 层, 三层策略) / EXPAS — title system (NP layer, three-tier policy)
//   基于 LabAPI 的 CustomInfo(玩家名字下方灰字, L_14140)： / Based on LabAPI's CustomInfo (gray text under the player's name, L_14140):
//     · 层1(ifempty) 只给"没有称号"的玩家设称号 —— 不覆盖任何已有内容; / · Tier 1 (ifempty) set a title only for players who have none — never overwrite existing content;
//     · 层2(force)   强制替换 —— 无条件覆盖(包括其它插件/服务端已设的); / · Tier 2 (force) force replace — overwrite unconditionally (including what other plugins/the server have set);
//     · 层3(clear)   彻底清除 —— 把该玩家的 CustomInfo 清空, 撤销替换相关 API 调用。 / · Tier 3 (clear) clear completely — empty the player's CustomInfo and undo replacement-related API calls.
//   持久化: 每玩家存 TitlePlan(1|2|3) + TitleText, 进服/换数据时自动应用。 / Persistence: store TitlePlan(1|2|3) + TitleText per player; applied automatically on join/data change.
//   校验: 写入前走 LabApi Player.ValidateCustomInfo(L_14698); 校验失败剥除 < > [ ] / Validation: run LabApi Player.ValidateCustomInfo(L_14698) before writing; on failure, strip < > [ ]
//         与颜色标签后重试, 仍失败则拒绝并记录(避免客户端被拒登)。 / and color tags, then retry; if it still fails, reject and log (to avoid the client being refused login).
// ============================================================================
using LabApi.Features.Wrappers;
using System;
using System.Text;
using L = Expas.ExpasLang;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        public const int TitleOff = 0;        // 不干预(默认) / Do nothing (default)
        public const int TitleIfEmpty = 1;    // 层1: 无称号才设 / Tier 1: set only if none
        public const int TitleForce = 2;      // 层2: 强制替换 / Tier 2: force replace
        public const int TitleClear = 3;      // 层3: 彻底清除 / Tier 3: clear completely

        /// <summary>称号模板的可读名(日志/回执用)。 / Human-readable name of the title plan (for logs/receipts).</summary>
        public static string TitlePlanName(int plan)
        {
            switch (plan)
            {
                case TitleIfEmpty: return L.T("①无称号才设", "1) Set only if empty");
                case TitleForce: return L.T("②强制替换", "2) Force replace");
                case TitleClear: return L.T("③彻底清除", "3) Clear completely");
                default: return L.T("不干预", "Off");
            }
        }

        /// <summary>
        /// 应用该玩家的称号(三层策略)。在 OnJoined / 数据变更 / 命令后调用。 / Apply the player's title (three-tier policy). Called on OnJoined / data change / after commands.
        /// 依据: LabApi Player.CustomInfo(L_14140, 底层 Network_customPlayerInfoString)、 / Basis: LabApi Player.CustomInfo (L_14140, underlying Network_customPlayerInfoString),
        ///       Player.ValidateCustomInfo(L_14698)、NicknameSync.ValidateCustomInfo(G_13910-13992, / Player.ValidateCustomInfo (L_14698), NicknameSync.ValidateCustomInfo (G_13910-13992,
        ///       正则 PlayerCustomInfoRegex G_27581 = ^((?!\[\])[\p{L}\p{P}\p{Sc}\p{N} ^=+|~`<>\n]){0,400}$, 即不允许 [] 、≤400 字符)。 / regex PlayerCustomInfoRegex G_27581 = ^((?!\[\])[\p{L}\p{P}\p{Sc}\p{N} ^=+|~`<>\n]){0,400}$, i.e. [] is not allowed and length ≤400 characters).
        /// </summary>
        public void ApplyTitle(Player p, PlayerData d)
        {
            if (p == null || p.IsDestroyed || d == null) return;
            if (!Config.TitleEnabled) return;
            try
            {
                switch (d.TitlePlan)
                {
                    case TitleIfEmpty:
                        // 层1: 已有称号(灰字非空) → 不覆盖 / Tier 1: already has a title (gray text non-empty) → do not overwrite
                        if (!string.IsNullOrEmpty(p.CustomInfo)) return;
                        SetTitleChecked(p, d.TitleText);
                        break;
                    case TitleForce:
                        // 层2: 强制替换(含覆盖别人设的) / Tier 2: force replace (including overwriting what others set)
                        SetTitleChecked(p, d.TitleText);
                        break;
                    case TitleClear:
                        // 层3: 彻底清除 —— 把 CustomInfo 清空(撤销对替换类 API 的调用) / Tier 3: clear completely — empty CustomInfo (undo calls to replacement APIs)
                        if (!string.IsNullOrEmpty(p.CustomInfo)) p.CustomInfo = string.Empty;
                        break;
                }
            }
            catch (Exception ex) { Logger.Warn("[EXPAS] " + L.T($"称号应用失败: {ex.Message}", $"Failed to apply Title: {ex.Message}")); }
        }

        /// <summary>写称号(带 ValidateCustomInfo 预校验; 失败剥 < > [ ] 后重试一次)。 / Write the title (with ValidateCustomInfo pre-validation; on failure strip < > [ ] and retry once).</summary>
        private void SetTitleChecked(Player p, string text)
        {
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0) { if (!string.IsNullOrEmpty(p.CustomInfo)) p.CustomInfo = string.Empty; return; }
            try
            {
                if (t.Length > 400) t = t.Substring(0, 400);
                if (Player.ValidateCustomInfo(t, out string reason))
                {
                    p.CustomInfo = t;
                    return;
                }
                // 剥离非法字符(< > [ ])后重试 / Strip illegal characters (< > [ ]) and retry
                var sb = new StringBuilder();
                foreach (char c in t)
                    if (c != '<' && c != '>' && c != '[' && c != ']') sb.Append(c);
                string clean = sb.ToString().Trim();
                if (clean.Length > 0 && Player.ValidateCustomInfo(clean, out _))
                { p.CustomInfo = clean; return; }
                Logger.Warn("[EXPAS] " + L.T($"称号被拒({reason}): '{t}'", $"Title rejected ({reason}): '{t}'"));
            }
            catch (Exception ex) { Logger.Warn("[EXPAS] " + L.T($"称号写入失败: {ex.Message}", $"Failed to write Title: {ex.Message}")); }
        }
    }
}