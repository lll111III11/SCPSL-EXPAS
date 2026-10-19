// ============================================================================
//   EXPAS —— SSS 面板层 (partial) / EXPAS — SSS panel layer (partial)
//   玩家在 ESC → 设置 → 服务器设置 里就能调自己的 HUD: / Players can adjust their own HUD in ESC → Settings → Server Settings:
//     HUD 折叠(展开/折叠) / 屏幕分辨率档位(自动检测/720p/1080p/1440p/4K) / 整体缩放(%) / HUD collapse (expanded/collapsed) / screen resolution preset (auto-detect/720p/1080p/1440p/4K) / overall scale (%) /
//     HUD 开关 / 字号(%) / 屏幕缩放(%) / 行距(%) / 垂直位置 / 水平对齐 / HUD toggle / font size (%) / screen scale (%) / line height (%) / vertical offset / horizontal align /
//     等级块对齐 / 重置本机 HUD 设置 / Level block align / reset local HUD settings
//   (v1.6 新增「HUD 折叠 / 屏幕分辨率 / 整体缩放」三项; 两块文本区改为默认折叠, 面板不占地方) / (v1.6 adds three items: HUD collapse / screen resolution / overall scale; both text areas default to collapsed so the panel takes no space)
//   (本轮改造: 玩家称号功能已整体移除 —— 面板不再有称号分组, 插件不再读写名字下方的灰字字段; / (This revision: the player title feature has been removed entirely — the panel no longer has a title group, and the plugin no longer reads/writes the gray text field under the name;
//    名字前缀 [Lv.X] 属于等级显示, 保留。) / the [Lv.X] name prefix is part of level display and is kept.)
//   面板里还有一块纯文本「我的数据」区(SSTextArea), 实时显示等级/经验/伤害/锁定。 / The panel also has a plain-text "My Data" area (SSTextArea) showing level/EXP/damage/lock in real time.
//   全部按 Steam64 记录在玩家自己的 .dat 里, 换机器也跟随。 / Everything is recorded per Steam64 in the player's own .dat, so it follows them across machines.
//
//   注意: 原生 Hint 固定锚在屏幕底部居中, 插件拿不到"屏幕上任意坐标"的接口, / Note: the native Hint is anchored bottom-center, and plugins have no API for arbitrary screen coordinates,
//   所以位置是用 TMP 富文本实现的: / so positioning is implemented with TMP rich text:
//     <size=N%>         字号 × 屏幕缩放 合成后的最终字号 / <size=N%>         final font size = font size × screen scale
//     <line-height=N%>  行距 / <line-height=N%>  line height
//     <voffset=N>       垂直微调(正=下, 负=上) / <voffset=N>       vertical fine-tuning (positive = down, negative = up)
//     <align=left|center|right>  整块水平对齐 / <align=left|center|right>  whole-block horizontal alignment
//   当某个值等于默认(100% / 0)时, 插件**不会**输出该标签 —— 所以把字号和屏幕 / When a value equals its default (100% / 0), the plugin does **not** emit that tag — so setting font size and screen
//   缩放都调回 100% 就等于完全原生渲染(万一客户端不解析某个标签, 这样最安全)。 / scale back to 100% is equivalent to fully native rendering (safest if the client cannot parse a tag).
// ============================================================================
using CommandSystem;
using LabApi.Features.Wrappers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UserSettings.ServerSpecific;
using UnityEngine;
using L = Expas.ExpasLang;
using Logger = LabApi.Features.Console.Logger;

namespace Expas
{
    public partial class ExpasPlugin
    {
        // ==================== SSS ID 段 ==================== / SSS ID range
        private const int SssIdKillFeed = 258;      // 击杀/积分(独立文本区) / Kills/points (separate text area)
        private const int SssIdMyData = 259;        // 纯文本「我的数据」 / Plain-text "My Data"
        private const int SssIdHudEnabled = 260;
        private const int SssIdSize = 261;
        private const int SssIdScreen = 262;
        private const int SssIdLineHeight = 263;
        private const int SssIdVOffset = 264;
        private const int SssIdAlign = 265;
        private const int SssIdLevelAlign = 266;
        private const int SssIdReset = 267;
        // ---- v1.6 新增三项 ---- / Three new items in v1.6 ----
        private const int SssIdCollapse = 268;     // HUD 折叠: 展开/折叠 / HUD collapse: expanded/collapsed
        private const int SssIdResolution = 269;   // 屏幕分辨率档位: 自动检测 / 640x480 … 3840x2160(16 档) / Screen resolution preset: auto-detect / 640x480 … 3840x2160 (16 presets)
        private const int SssIdGlobalScale = 270;  // 整体缩放: 50-200% / Overall scale: 50-200%
        // ---- v1.7/v1.8 自定义 UI 布局(下拉按玩家归属动态下发, 见 SendSssMenuTo / LayoutKeysForPlayer) ---- / Custom UI layouts (dropdown sent dynamically per player ownership; see SendSssMenuTo / LayoutKeysForPlayer)
        private const int SssIdLayout = 271;       // 自定义 UI 布局(layouts\ 下的布局, 归属匹配或白名单可见) / Custom UI layout (layout under layouts\; visible when owner matches or whitelisted)

        private SSTextArea _myDataArea;
        private SSTextArea _killFeedArea;
        /// <summary>基础菜单(所有人; 「自定义 UI」下拉按玩家归属动态追加, 见 SendSssMenuTo)。 / Base menu (for everyone; the "Custom UI" dropdown is appended dynamically per player ownership, see SendSssMenuTo).</summary>
        private ServerSpecificSettingBase[] _menuBase;
        /// <summary>SSS 是否处于接管全局表状态(RegisterSss 置位 / UnregisterSss 复位)。 / Whether SSS currently owns the global settings table (set by RegisterSss / reset by UnregisterSss).</summary>
        private bool _sssRegistered;
        /// <summary>
        /// v1.8.1 一次性个性化下发标记(UserId): 已给该玩家 SendToPlayer 过个性化菜单。 / v1.8.1 one-time personalized send marker (UserId): a personalized menu has already been sent to this player via SendToPlayer.
        /// 用途: ServerOnStatusReceived(打开/关闭设置页)回调里**不能**每触发一次就重发菜单 / Purpose: in the ServerOnStatusReceived (open/close settings page) callback, we **must not** resend the menu on every trigger
        ///       —— 那会形成"打开→重发→重渲染→再上报→再重发"死循环, 面板永远在刷新打不开(官方 /       — that forms an "open → resend → re-render → report again → resend" infinite loop, the panel keeps refreshing and cannot open (the official
        ///       示例 12_自定义UI全攻略 L428 只记日志不回发)。只在玩家"第一次打开设置页"时兜底补发一次 /       sample 12_自定义UI全攻略 L428 only logs and never resends). Only fall back to sending once when the player opens the settings page for the first time
        ///       (进服时若被官方 OnInstanceModeChanged 自动下发抢先后, 玩家缺「自定义 UI」下拉 → 打开面板补发)。 /       (if the official OnInstanceModeChanged auto-send wins the race on join, the player lacks the "Custom UI" dropdown → resend when the panel opens).
        /// </summary>
        private readonly HashSet<string> _menuSentTo = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>
        /// v1.8.4: 本插件**自己创建**的 SSS 控件实例(引用集合, HashSet 默认引用相等)。 / v1.8.4: SSS control instances **created by this plugin** (reference set; HashSet uses reference equality by default).
        /// 用途: RemoveOwnFromDefined 按**实例引用**过滤自己的项 —— 而不是按固定 SettingId 段(258..271)。 / Purpose: RemoveOwnFromDefined filters our items by **instance reference** — not by the fixed SettingId range (258..271).
        /// ★ 修"误删 BGMCC 等其它插件面板项": 若其它插件(BGMCC 等)的控件恰好用了与 EXPAS 相同的 / ★ Fix "accidentally deleting panels of other plugins such as BGMCC": if controls from other plugins (BGMCC etc.) happen to use the same
        ///   SettingId(如 260/271), 旧代码按 id 段过滤会把它们的项一起删掉 → 对方的 SSS 界面被"顶掉"。 /   SettingId as EXPAS (e.g. 260/271), the old id-range filter would delete them too → the other plugin's SSS panel gets "kicked out".
        ///   按引用过滤后, 无论 id 是否撞号, 只删我们自己的实例, 其它插件项永远保留。 /   With reference filtering, regardless of id collisions, only our own instances are removed and other plugins' items are always kept.
        /// </summary>
        private readonly HashSet<ServerSpecificSettingBase> _ownSss = new HashSet<ServerSpecificSettingBase>();
        /// <summary>
        /// v1.8.2 写盘/刷新节流(UserId → 最近一次提交时间 UTC)。 / v1.8.2 save/refresh throttle (UserId → last commit time UTC).
        /// 依据: 源码 SSSliderSetting 提供 SyncDragging(G_72665 附近, 详见 21_UI系统权威参考 L46-51), / Basis: the source's SSSliderSetting provides SyncDragging (near G_72665, see 21_UI系统权威参考 L46-51),
        ///       **拖动滑条的整个过程客户端会逐帧回传值** → OnSssValue 被高频调用。 /       **while dragging the slider the client sends values every frame** → OnSssValue is called very frequently.
        /// 旧代码每次都 SavePlayer(File.WriteAllText 主线程写盘) + RefreshMyData(2 条 SendTextUpdate) / The old code did SavePlayer (File.WriteAllText on the main thread) + RefreshMyData (2 SendTextUpdate calls) every time
        /// + 重发 HUD → 拖一下滑条 = 几十次写盘/刷屏, 服务器卡顿、面板疯狂刷新("频繁更新"的实锤)。 / + resend HUD → one slider drag = dozens of disk writes/screen updates, server lag and frantic panel refresh (the proof of "frequent updates").
        /// 修复: 拖动中只更新内存; 拖动结束 / 非滑条控件, 250ms 内合并为一次提交(与 HudLoop 自动保存兜底配合)。 / Fix: during dragging only update memory; on drag end / non-slider controls, merge into one commit within 250ms (together with HudLoop auto-save fallback).
        /// </summary>
        private readonly Dictionary<string, DateTime> _sssCommitThrottle =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        // ========================================================================
        // 注册 / 注销 (只摘自己那批, 不动别的插件) / Register / unregister (remove only our own items, leave other plugins untouched)
        // ========================================================================
        private void RegisterSss()
        {
            try
            {
                // 菜单结构: 基础菜单(所有人) + 按玩家归属动态追加的「自定义 UI」下拉(见 SendSssMenuTo)。 / Menu structure: base menu (everyone) + "Custom UI" dropdown appended dynamically per player ownership (see SendSssMenuTo).
                //   为什么能做"按人不同": ServerSpecificSettingsSync.SendToPlayer(hub, collection) /   Why per-player differences are possible: ServerSpecificSettingsSync.SendToPlayer(hub, collection)
                //   是官方口子(源码 G_71984), 基础 DefinedSettings 仍走 SendToAll() / 进服自动下发。 /   is the official entry point (source G_71984); base DefinedSettings still uses SendToAll() / auto-send on join.
                _menuBase = BuildSssMenu(null);
                _ownSss.UnionWith(_menuBase);   // 登记本插件创建的实例(供按引用过滤) / Register instances created by this plugin (for reference filtering)
                ServerSpecificSettingsSync.DefinedSettings = RemoveOwnFromDefined().Concat(_menuBase).ToArray();
                ServerSpecificSettingsSync.SendToAll();
                ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSssValue;
                ServerSpecificSettingsSync.ServerOnStatusReceived += OnSssStatus;
                _sssRegistered = true;
                Logger.Info(L.T("[EXPAS] SSS 面板已注册(HUD 字号/屏幕缩放/位置/分辨率档/自定义 UI 可由玩家自调)",
                    "[EXPAS] SSS panel registered (players can adjust HUD font size/screen scale/position/resolution preset/custom UI)"));
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] SSS 注册失败: {ex.Message}", $"[EXPAS] SSS registration failed: {ex.Message}")); }
        }

        private void UnregisterSss()
        {
            try
            {
                ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSssValue;
                ServerSpecificSettingsSync.ServerOnStatusReceived -= OnSssStatus;
                // 只摘掉自己的面板项, 其它插件/游戏自带的项原样保留 —— 修旧版"恢复快照"会把后来者抹掉 / Remove only our own panel items; other plugins'/vanilla items stay as-is — fixes the old "restore snapshot" wiping later additions
                // 官方卸载套路(12 号 L354): DefinedSettings = null; SendToAll(); —— 自己独占(没别人项)时给 null, / Official uninstall pattern (doc 12 L354): DefinedSettings = null; SendToAll(); — use null when we are the sole owner (no other items),
                // 空数组会让客户端设置页异常; 有别家项则保留它们。 / an empty array would break the client settings page; if other items exist, keep them.
                var keep = RemoveOwnFromDefined();
                ServerSpecificSettingsSync.DefinedSettings = keep.Length > 0 ? keep : null;
                ServerSpecificSettingsSync.SendToAll();
                _sssRegistered = false;
                _ownSss.Clear();   // v1.8.4: 本插件实例已全部移出全局表, 清空登记 / v1.8.4: all this plugin's instances have been removed from the global table; clear the registry
            }
            catch { }
        }

        /// <summary>
        /// 修复"顶掉其它插件面板": 从当前全局 DefinedSettings 里**只过滤掉 EXPAS 自己创建的实例** / Fix "kicking out other plugins' panels": from the current global DefinedSettings, **filter out only instances created by EXPAS itself**
        /// (按 _ownSss 引用集合匹配, 与 SettingId 无关) —— 其余(其它插件 + 游戏自带)原样保留, / (matched by the _ownSss reference set, independent of SettingId) — everything else (other plugins + vanilla) is kept as-is,
        /// 与插件注册先后无关, **且不误删 id 撞号(260/271 等)的其它插件项**。 / regardless of plugin registration order, **and other plugins' items with colliding ids (260/271 etc.) are not mistakenly deleted**.
        /// </summary>
        private ServerSpecificSettingBase[] RemoveOwnFromDefined()
        {
            ServerSpecificSettingBase[] cur = ServerSpecificSettingsSync.DefinedSettings;
            if (cur == null || cur.Length == 0) return new ServerSpecificSettingBase[0];
            return cur.Where(s => s != null && !_ownSss.Contains(s)).ToArray();
        }

        /// <summary>
        /// v1.8: 把"该玩家该看到的那套菜单"单独发给他 —— 基础菜单所有人都有, / v1.8: Send "the menu this player should see" to them individually — everyone gets the base menu,
        /// 「自定义 UI」下拉只发给"有可用布局"的玩家(布局文件 owner Steam64 归属匹配, 或白名单)。 / the "Custom UI" dropdown is only sent to players with available layouts (layout file owner Steam64 matches, or whitelisted).
        /// 依据: ServerSpecificSettingsSync.SendToPlayer(ReferenceHub, ServerSpecificSettingBase[], int?) / Basis: ServerSpecificSettingsSync.SendToPlayer(ReferenceHub, ServerSpecificSettingBase[], int?)
        ///       (game-src L71984-71990), 官方分页示例就是这么按人换菜单的(文档 12 号 L140-145)。 /       (game-src L71984-71990); the official paging example switches menus per player exactly like this (doc 12 L140-145).
        /// ★ v1.8.3 修复"顶掉 BGMCC 等其它插件面板": 发送给玩家的集合**必须包含全局 DefinedSettings 里 / ★ v1.8.3 fix "kicking out panels of other plugins such as BGMCC": the set sent to the player **must include all items in the global DefinedSettings
        ///   不属于 EXPAS 的所有项**(RemoveOwnFromDefined) —— 客户端收到 SendToPlayer(集合) 会把它当作 /   that do not belong to EXPAS** (RemoveOwnFromDefined) — when the client receives SendToPlayer(set), it treats it as
        ///   该玩家**当前的全部设置项**渲染; 若只发自己插件那批, 其它插件(BGMCC 等)的面板项就会从该玩家 /   **all of that player's current settings** to render; if only our own plugin's items are sent, other plugins' (BGMCC etc.) panel items will
        ///   面板里消失。这里发**完整集合 = 其它插件项 + EXPAS 项**, 谁也不顶。 /   disappear from that player's panel. Here we send the **full set = other plugins' items + EXPAS items**, so nothing gets kicked out.
        /// 说明: 进服瞬间可能和官方"进服自动下发"(L72024 OnInstanceModeChanged → SendToPlayer(hub)) / Note: on join this may race with the official "auto-send on join" (L72024 OnInstanceModeChanged → SendToPlayer(hub))
        ///       抢先后 → 这里立刻发一次; 若被官方覆盖, 由 OnSssStatus 在玩家**第一次打开设置页**时 /       → send once immediately here; if overwritten by the official one, OnSssStatus resends once when the player **opens the settings page for the first time**
        ///       兜底补发一次(_menuSentTo 判重, 绝不反复发)。 /       as a fallback (_menuSentTo dedupes, never repeatedly sends).
        /// </summary>
        private void SendSssMenuTo(Player p)
        {
            try
            {
                if (p == null || p.IsDestroyed || _menuBase == null) return;
                ReferenceHub hub = p.ReferenceHub;
                if (hub == null || hub.connectionToClient == null) return;
                PlayerData d = GetData(p.UserId, p.Nickname);
                ServerSpecificSettingBase[] mine = BuildSssMenuFor(d);
                if (mine == null) mine = _menuBase;
                ServerSpecificSettingBase[] others = RemoveOwnFromDefined();   // 全局里其它插件的项 / Other plugins' items in the global table
                ServerSpecificSettingsSync.SendToPlayer(hub, others.Concat(mine).ToArray());
                if (p.UserId != null) _menuSentTo.Add(p.UserId);   // 已个性化下发过 → 打开面板不再补发 / Personalized menu already sent → no resend when the panel opens
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] SSS 菜单下发失败: {ex.Message}", $"[EXPAS] Failed to send SSS menu: {ex.Message}")); }
        }

        /// <summary>
        /// 按玩家构建整份菜单: 基础菜单 + (若有可用布局)追加「自定义 UI」下拉。 / Build the full menu for a player: base menu + (if layouts are available) the appended "Custom UI" dropdown.
        /// 下拉选项 = 布局**显示名**(LayoutPickerEntries 统一生成, 与 OnSssValue 的 index→key 映射用同一份), / Dropdown options = layout **display names** (generated consistently by LayoutPickerEntries, the same set used for the index→key mapping in OnSssValue),
        /// 初始选中他当前存储的布局; 没有可用布局 → 返回 null(整项不显示, 只发基础菜单)。 / initially selecting the layout they currently store; no available layout → return null (the item is hidden and only the base menu is sent).
        /// </summary>
        private ServerSpecificSettingBase[] BuildSssMenuFor(PlayerData d)
        {
            string[] keys, names;
            if (!LayoutPickerEntries(d, out keys, out names)) return null;   // 无可选布局 → 不带下拉 / No selectable layouts → no dropdown
            int initIdx = 0;
            string cur = GetLayoutKey(d);
            for (int i = 0; i < keys.Length; i++)
                if (string.Equals(keys[i], cur, StringComparison.OrdinalIgnoreCase)) { initIdx = i; break; }
            var withPicker = _menuBase.Concat(new ServerSpecificSettingBase[]
            {
                new SSDropdownSetting(SssIdLayout, L.T("自定义 UI", "Custom UI"), names, initIdx,
                    hint: L.T("选择 HUD 布局(来自 configs\\<端口>\\EXPAS\\layouts\\*.txt / *.js); 默认 = 默认布局",
                        "Choose a HUD layout (from configs\\<port>\\EXPAS\\layouts\\*.txt / *.js); Default = default layout"))
            }).ToArray();
            return withPicker;
        }

        /// <summary>
        /// 为该玩家生成「自定义 UI」下拉的选项(显示名)与 index→layout key 映射(守卫顺序一致)。 / Generate the "Custom UI" dropdown options (display names) and index→layout key mapping for this player (guard order is consistent).
        /// 下拉显示布局的 DisplayName(如 "测试布局(示例玩家)")而不是裸 key(一串数字); / The dropdown shows the layout's DisplayName (e.g. "测试布局(示例玩家)") instead of the bare key (a string of digits);
        /// 顺序与 LayoutKeysForPlayer 完全一致: default 排第一, 其后按 key 排序。 / the order is exactly the same as LayoutKeysForPlayer: default first, then sorted by key.
        /// 无可用布局 → 返回 false(调用方不显示下拉)。 / No available layout → return false (the caller hides the dropdown).
        /// </summary>
        private bool LayoutPickerEntries(PlayerData d, out string[] keys, out string[] names)
        {
            keys = null; names = null;
            string[] all = LayoutKeysForPlayer(d);
            if (all == null || all.Length <= 1) return false;
            keys = all;
            names = new string[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                HudLayoutDef def;
                if (string.Equals(all[i], "default", StringComparison.OrdinalIgnoreCase)) names[i] = L.T("默认", "Default");
                else if (_layouts.TryGetValue(all[i], out def) && def != null && !string.IsNullOrEmpty(def.DisplayName))
                    names[i] = def.DisplayName;
                else names[i] = all[i];
            }
            return true;
        }

        /// <summary>重建基础菜单并重新下发(布局列表变化 / 白名单变化后调用)。 / Rebuild the base menu and resend it (called after layout list or whitelist changes).</summary>
        private void RebuildSssMenus(bool resend)
        {
            try
            {
                if (!_sssRegistered) return;   // 未接管全局表时不要碰 DefinedSettings / Do not touch DefinedSettings when the global table is not owned
                _menuBase = BuildSssMenu(null);
                _ownSss.UnionWith(_menuBase);   // 登记本插件创建的实例(供按引用过滤) / Register instances created by this plugin (for reference filtering)
                ServerSpecificSettingsSync.DefinedSettings = RemoveOwnFromDefined().Concat(_menuBase).ToArray();
                if (resend)
                {
                    // v1.8.2: 不再 SendToAll —— 那会全服整份重渲染, 且把已在线的个性化下拉 / v1.8.2: no longer SendToAll — that re-renders the whole menu for the entire server and overwrites online players' personalized dropdowns
                    // 覆盖回基础菜单(闪烁/掉下拉)。只按玩家重发个性化菜单即可(官方分页例 12 号 L139-146 也是按人发); / back to the base menu (flicker/lost dropdown). Just resend personalized menus per player (official paging example 12 L139-146 also sends per player);
                    // 新进服玩家由官方进服自动下发新的 DefinedSettings + OnJoined 个性化兜底。 / players who newly join get the new DefinedSettings via the official auto-send on join + OnJoined personalized fallback.
                    ResendSssMenusToAll();
                }
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] SSS 菜单重建失败: {ex.Message}", $"[EXPAS] Failed to rebuild SSS menu: {ex.Message}")); }
        }

        /// <summary>v1.7: 给每个在线真人玩家单独下发"他那套"菜单(白名单 → 含自定义 UI 下拉)。 / v1.7: Send each online real player their own menu (whitelisted → includes the Custom UI dropdown).</summary>
        private void ResendSssMenusToAll()
        {
            try
            {
                foreach (Player p in Player.ReadyList.Where(x => x != null && !x.IsDestroyed && x.IsPlayer && !x.IsDummy))
                    SendSssMenuTo(p);
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] SSS 菜单重发失败: {ex.Message}", $"[EXPAS] Failed to resend SSS menu: {ex.Message}")); }
        }

        /// <param name="keys">非 null 且长度>1 时, 追加「自定义 UI」下拉(选项 = keys, 初始选中 index 0=default)。 / When non-null and length > 1, append the "Custom UI" dropdown (options = keys, initial selection index 0 = default).</param>
        private ServerSpecificSettingBase[] BuildSssMenu(string[] keys)
        {
            // v1.6: 两块文本区都改成「默认折叠」(CollapsedByDefault) —— 面板本身不占地方, 想看点开即可 / v1.6: both text areas now default to CollapsedByDefault — the panel itself takes no space; expand to view
            //   SSTextArea.FoldoutMode 共 5 种(源码 G_73110): NotCollapsable / CollapseOnEntry / /   SSTextArea.FoldoutMode has 5 values (source G_73110): NotCollapsable / CollapseOnEntry /
            //   ExtendOnEntry / CollapsedByDefault / ExtendedByDefault
            // v1.7: 文本区实例只在第一次创建(重建菜单时复用, SettingId 保持不变) / v1.7: text area instances are created only once (reused on menu rebuild, SettingId stays the same)
            if (_myDataArea == null)
                _myDataArea = new SSTextArea(SssIdMyData, L.T("正在载入...", "Loading..."),
                    SSTextArea.FoldoutMode.CollapsedByDefault, L.T("EXPAS 我的数据 / HUD 设置", "EXPAS My Data / HUD Settings"));
            // 击杀/积分单独注册一个文本区 —— 每个 UI 独立注册, 互不干扰 / Register kills/points as a separate text area — each UI registers independently without interfering
            if (_killFeedArea == null && Config.KillFeedPanel)
                _killFeedArea = new SSTextArea(SssIdKillFeed, L.T("击杀/积分: 暂无记录", "Kills/Points: No records yet"),
                    SSTextArea.FoldoutMode.CollapsedByDefault, L.T("EXPAS 击杀与积分(独立文本区)", "EXPAS Kills & Points (separate text area)"));

            var menu = new ServerSpecificSettingBase[]
            {
                _myDataArea,
                new SSGroupHeader(L.T("HUD 显示", "HUD Display"), false, L.T("只改你自己的界面, 按 Steam64 记住", "Only changes your own UI, remembered by Steam64")),
                new SSDropdownSetting(SssIdHudEnabled, L.T("HUD 开关", "HUD Toggle"),
                    new[] { L.T("开", "On"), L.T("关", "Off") }, 0, hint: L.T("关掉后你自己的 EXPAS HUD 不再显示", "When off, your EXPAS HUD is no longer shown")),
                new SSSliderSetting(SssIdSize, L.T("字号", "Font Size"), 40f, 160f, 85f, integer: true,
                    valueToStringFormat: "0", finalDisplayFormat: "{0}%", hint: L.T("文字大小, 100%=游戏默认", "Text size, 100% = game default")),
                new SSSliderSetting(SssIdScreen, L.T("屏幕缩放", "Screen Scale"), 50f, 150f, 100f, integer: true,
                    valueToStringFormat: "0", finalDisplayFormat: "{0}%",
                    hint: L.T("整块 HUD 的缩放(与字号相乘), 大屏想让文字更小时往小调", "Scales the whole HUD (multiplied with font size); lower it on big screens for smaller text")),
                // v1.6 新增: 整体缩放(在「字号×屏幕缩放」之外再乘一次) / Added in v1.6: overall scale (multiplied once more on top of font size × screen scale)
                new SSSliderSetting(SssIdGlobalScale, L.T("整体缩放", "Overall Scale"), 50f, 200f, 100f, integer: true,
                    valueToStringFormat: "0", finalDisplayFormat: "{0}%",
                    hint: L.T("在「字号×屏幕缩放」之外再整体缩放一次(默认100%), 想一键放大/缩小整个 HUD 就用它", "Applies an extra overall scale on top of font size × screen scale (default 100%); use it to enlarge/shrink the whole HUD at once")),
                new SSSliderSetting(SssIdLineHeight, L.T("行距", "Line Height"), 60f, 140f, 100f, integer: true,
                    valueToStringFormat: "0", finalDisplayFormat: "{0}%", hint: L.T("行与行的间隔, 小于100更紧凑", "Spacing between lines; below 100 is more compact")),
                new SSSliderSetting(SssIdVOffset, L.T("垂直位置", "Vertical Offset"), -200f, 200f, 0f, integer: true,
                    valueToStringFormat: "0", finalDisplayFormat: "{0}", hint: L.T("负=整体上移, 正=整体下移(像素级微调)", "Negative = move up, positive = move down (pixel fine-tuning)")),
                new SSDropdownSetting(SssIdAlign, L.T("水平对齐", "Horizontal Align"),
                    new[] { L.T("左", "Left"), L.T("居中", "Center"), L.T("右", "Right") }, 1, hint: L.T("整块 HUD 的对齐方式", "Alignment of the whole HUD")),
                new SSDropdownSetting(SssIdLevelAlign, L.T("等级块对齐", "Level Block Align"),
                    new[] { L.T("左", "Left"), L.T("居中", "Center"), L.T("右", "Right"), L.T("跟随上面", "Follow above") }, 2, hint: L.T("右下角那块等级/经验的对齐", "Alignment of the level/EXP block at the bottom right")),
                // v1.6 新增: HUD 折叠 + 屏幕分辨率档位(v1.7 档位扩到 16 档) / Added in v1.6: HUD collapse + screen resolution preset (v1.7 expanded to 16 presets)
                new SSDropdownSetting(SssIdCollapse, L.T("HUD 折叠", "HUD Collapse"),
                    new[] { L.T("展开", "Expanded"), L.T("折叠", "Collapsed") }, 0,
                    hint: L.T("折叠时屏幕 HUD 只显示一行(hud_collapsed_line 模板); 击杀提示仍然显示", "When collapsed, the screen HUD shows only one line (hud_collapsed_line template); kill notices still show")),
                new SSDropdownSetting(SssIdResolution, L.T("屏幕分辨率", "Screen Resolution"),
                    ResolutionLabels, 0,
                    hint: L.T("服务端读不到像素分辨率, 只能读宽高比; 「自动检测」按宽高比给倍率, 也可按自己的屏幕手动选档", "The server cannot read pixel resolution, only aspect ratio; Auto Detect scales by aspect ratio, or pick a preset matching your screen")),
                new SSButton(SssIdReset, L.T("重置本机 HUD 设置", "Reset Local HUD Settings"), L.T("重置", "Reset"), 1f)
            };

            // v1.8: 「自定义 UI」下拉 —— 由调用方(BuildSssMenuFor)按玩家归属动态传入 keys; 不传就没有这一项 / v1.8: "Custom UI" dropdown — the caller (BuildSssMenuFor) passes keys dynamically per player ownership; if omitted, this item is absent
            if (keys != null && keys.Length > 1)
            {
                menu = menu.Concat(new ServerSpecificSettingBase[]
                {
                    new SSDropdownSetting(SssIdLayout, L.T("自定义 UI", "Custom UI"),
                        keys, 0,
                        hint: L.T("选择 HUD 布局(来自 configs\\<端口>\\EXPAS\\layouts\\*.txt / *.js); 默认 = 默认布局", "Choose a HUD layout (from configs\\<port>\\EXPAS\\layouts\\*.txt / *.js); Default = default layout"))
                }).ToArray();
            }

            // 击杀/积分文本区独立插在最前面(单独注册) / The kills/points text area is inserted independently at the very front (registered separately)
            return (_killFeedArea != null)
                ? new[] { (ServerSpecificSettingBase)_killFeedArea }.Concat(menu).ToArray()
                : menu;
        }

        /// <summary>
        /// ServerOnStatusReceived: 玩家"打开/关闭设置页"时触发(见 12_自定义UI全攻略 L38)。 / ServerOnStatusReceived: triggered when a player opens/closes the settings page (see 12_自定义UI全攻略 L38).
        /// ★ 修复 v1.8.1(玩家打不开面板): 官方示例(12 号 L428-432)在回调里**只记日志、不回发菜单**。 / ★ v1.8.1 fix (players cannot open the panel): the official example (12 L428-432) **only logs and never resends the menu** in this callback.
        ///   旧代码在这里无条件 SendSssMenuTo(p) → 打开面板 → 状态上报 → 重发整份菜单 → 客户端重渲染 → /   The old code unconditionally called SendSssMenuTo(p) here → open panel → status report → resend the whole menu → client re-render →
        ///   再次上报 → 无限循环, 面板一直在刷新, 玩家根本点不到 —— 这是本次"UI 频繁更新打不开"的根因。 /   report again → infinite loop, the panel keeps refreshing and the player cannot click anything — this is the root cause of "UI updates frequently and cannot open".
        ///   现改为: 仅当该玩家**还没**收到过个性化菜单(_menuSentTo 无此 UserId)时才补发一次, 其余情况纯粹记录日志。 /   Now: only when the player has **not yet** received a personalized menu (_menuSentTo lacks this UserId) do we resend once; otherwise just log.
        ///   这样既兜底了"进服时被官方自动下发覆盖 → 缺「自定义 UI」下拉"的竞态, 又绝对不会循环。 /   This handles the race where the official auto-send on join overwrites it → the "Custom UI" dropdown is missing, while never looping.
        /// </summary>
        private void OnSssStatus(ReferenceHub hub, SSSUserStatusReport status)
        {
            try
            {
                Player p = Player.Get(hub);
                if (p == null || p.IsDestroyed || p.IsDummy || !p.IsPlayer) return;
                if (!string.IsNullOrEmpty(p.UserId) && _menuSentTo.Add(p.UserId))
                    SendSssMenuTo(p);          // 第一次打开面板且此前未个性化下发过 → 兜底补发一次 / First panel open and no personalized send yet → fallback resend once
            }
            catch { }
        }

        private void OnSssValue(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            try
            {
                Player p = Player.Get(hub);
                if (p == null || p.IsDestroyed || p.IsDummy || !p.IsPlayer) return;
                PlayerData d = GetData(p.UserId, p.Nickname);
                bool changed = true;
                // v1.8.2: 拖动滑条时客户端逐帧回传 → 拖动中只更新内存(switch 里已改 d.xxx), / v1.8.2: while dragging the slider the client sends values every frame → during dragging only update memory (d.xxx is already changed in the switch),
                // 不写盘/不刷面板/不重发 HUD, 等拖动结束(下一回调 SyncDragging==false)再统一提交。 / no disk write / panel refresh / HUD resend; commit once when dragging ends (next callback with SyncDragging == false).
                bool dragging = setting is SSSliderSetting sld && sld.SyncDragging;

                switch (setting.SettingId)
                {
                    case SssIdHudEnabled:
                        if (setting is SSDropdownSetting he)
                        {
                            d.HudEnabledSet = he.SyncSelectionIndexValidated == 0 ? 1 : 0;
                            _hudOff[p.UserId] = d.HudEnabledSet == 0;
                        }
                        break;
                    case SssIdSize:
                        if (setting is SSSliderSetting sz) d.SizePercent = Mathf.Clamp(Mathf.RoundToInt(sz.SyncFloatValue), 40, 160);
                        break;
                    case SssIdScreen:
                        if (setting is SSSliderSetting sc) d.ScreenPercent = Mathf.Clamp(Mathf.RoundToInt(sc.SyncFloatValue), 50, 150);
                        break;
                    case SssIdLineHeight:
                        if (setting is SSSliderSetting lh) d.LineHeightPercent = Mathf.Clamp(Mathf.RoundToInt(lh.SyncFloatValue), 60, 140);
                        break;
                    case SssIdVOffset:
                        if (setting is SSSliderSetting vo) d.VOffset = Mathf.Clamp(Mathf.RoundToInt(vo.SyncFloatValue), -200, 200);
                        break;
                    case SssIdAlign:
                        if (setting is SSDropdownSetting al) d.HAlign = Mathf.Clamp(al.SyncSelectionIndexValidated, 0, 2);
                        break;
                    case SssIdLevelAlign:
                        if (setting is SSDropdownSetting la) d.LevelAlign = Mathf.Clamp(la.SyncSelectionIndexValidated, 0, 3);
                        break;
                    // ---- v1.6 新增三项 ---- / Three new items in v1.6 ----
                    case SssIdCollapse:
                        // 0=展开 1=折叠 / 0 = expanded, 1 = collapsed
                        if (setting is SSDropdownSetting hc) d.HudCollapsed = hc.SyncSelectionIndexValidated == 1 ? 1 : 0;
                        break;
                    case SssIdResolution:
                        // v1.7: 0=自动检测 1..15=具体分辨率档(见 ResolutionLabels / ResolutionSizes) / v1.7: 0 = auto-detect, 1..15 = specific resolution preset (see ResolutionLabels / ResolutionSizes)
                        if (setting is SSDropdownSetting rs) d.ResolutionPreset = Mathf.Clamp(rs.SyncSelectionIndexValidated, 0, 15);
                        break;
                    case SssIdGlobalScale:
                        if (setting is SSSliderSetting gs) d.GlobalScalePercent = Mathf.Clamp(Mathf.RoundToInt(gs.SyncFloatValue), 50, 200);
                        break;
                    // ---- v1.8 自定义 UI 布局(下拉只在归属匹配/白名单玩家的菜单里) ---- / v1.8 Custom UI layouts (the dropdown only appears in menus of owner-matched/whitelisted players)
                    case SssIdLayout:
                        if (setting is SSDropdownSetting ly)
                        {
                            // 用与下发完全一致的 LayoutPickerEntries 做 index→key 映射(顺序稳定); / Use the same LayoutPickerEntries as the send path for the index→key mapping (stable order);
                            // 二次校验: 越界/伪造/无权一律回落 default / Re-validate: out-of-range/forged/unauthorized always falls back to default
                            string[] pKeys, pNames;
                            if (!LayoutPickerEntries(d, out pKeys, out pNames)) { d.HudLayout = "default"; Logger.Info(L.T($"[EXPAS] SSS 布局选择: {p.Nickname}({p.UserId}) 无可选布局 → default", $"[EXPAS] SSS layout selection: {p.Nickname}({p.UserId}) no available layouts → default")); break; }
                            int pIdx = Mathf.Clamp(ly.SyncSelectionIndexValidated, 0, pKeys.Length - 1);
                            string pWant = pKeys[pIdx];
                            d.HudLayout = CanUseLayout(d, pWant) ? pWant : "default";
                            Logger.Info(L.T($"[EXPAS] SSS 布局选择: {p.Nickname}({p.UserId}) index={ly.SyncSelectionIndexValidated} → {d.HudLayout} (下拉选项: {string.Join("/", pNames)})", $"[EXPAS] SSS layout selection: {p.Nickname}({p.UserId}) index={ly.SyncSelectionIndexValidated} → {d.HudLayout} (options: {string.Join("/", pNames)})"));
                        }
                        break;
                    case SssIdReset:
                        if (setting is SSButton)
                        {
                            d.SizePercent = -1;
                            d.ScreenPercent = -1;
                            d.LineHeightPercent = -1;
                            d.VOffset = 0;
                            d.HAlign = -1;
                            d.LevelAlign = -1;
                            d.HudEnabledSet = 1;
                            // v1.6: 折叠 / 分辨率档位 / 整体缩放 一并回到"跟随服务器配置" / v1.6: collapse / resolution preset / overall scale also return to "follow server config"
                            d.HudCollapsed = -1;
                            d.ResolutionPreset = -1;
                            d.GlobalScalePercent = -1;
                            d.HudLayout = "default";      // v1.7: 布局也回到默认布局 / v1.7: the layout also returns to the default layout
                            _hudOff[p.UserId] = false;
                            // UI 隔离(与下面"立刻刷一次 HUD"同一判据): HudSurface 0=关闭屏幕 HUD, / UI isolation (same criterion as "refresh HUD immediately" below): HudSurface 0 = screen HUD disabled,
                            // 这时不占用全局唯一的 Hint 通道, 免得顶掉别的插件 / in that case do not occupy the globally unique Hint channel, to avoid kicking out other plugins
                            if (Config.HudSurface != 0)
                                p.SendHint(L.T("<color=#55FF55>【EXPAS】HUD 设置已重置</color>", "<color=#55FF55>[EXPAS] HUD settings reset</color>"), 3f);
                        }
                        break;
                    default:
                        changed = false;
                        break;
                }

                if (changed)
                {
                    if (dragging) return;      // 拖动中: 内存已更新, 等拖动结束(下一回调)再落盘 / Dragging: memory is updated; save to disk when dragging ends (next callback)

                    // v1.8.3: **落盘不节流** —— 布局下拉/按钮/非拖动回调都属于低频操作, 必须每次都 / v1.8.3: **saving is not throttled** — layout dropdowns/buttons/non-drag callbacks are low-frequency operations and must always
                    // SavePlayer(否则打开面板时的 Acquisition 批量回传里, 后到的控件值会被 250ms 节流 / SavePlayer (otherwise, in the batched Acquisition values when the panel opens, later control values get swallowed by the 250ms throttle
                    // 吞掉 → 改了内存却不落盘 → 重启后丢设置, 这正是"选了自定义布局重启后不变"的根因之一)。 / → memory changed but not saved → settings lost after restart; this is one root cause of "custom layout selection does not persist after restart").
                    // 只对"网络刷新"(面板文本+常驻 HUD)节流, 防拖动/批量回传刷屏。 / Only throttle "network refresh" (panel text + persistent HUD) to prevent screen flooding during dragging/batched reports.
                    SavePlayer(d);
                    DateTime now = DateTime.UtcNow;
                    if (_sssCommitThrottle.TryGetValue(p.UserId, out DateTime last)
                        && (now - last).TotalMilliseconds < 250)
                        return;
                    _sssCommitThrottle[p.UserId] = now;

                    RefreshMyData(p, d);
                    // 立刻按新设置刷一次, 不用等下一个刷新周期 / Refresh immediately with the new settings instead of waiting for the next refresh cycle
                    // UI 隔离: HudSurface 0=只走 SSS 面板, 这里必须和 HudLoop 用同一个判断, / UI isolation: HudSurface 0 = SSS panel only; this must use the same check as HudLoop,
                    // 否则改一次设置就会把常驻 HUD 塞进全局唯一的 Hint 通道, 顶掉别的插件 / otherwise one settings change shoves the persistent HUD into the globally unique Hint channel and kicks out other plugins
                    if (Config.HudSurface != 0 && !IsHudOff(p.UserId) && p.IsAlive)
                        p.SendHint(BuildHud(p, d), Mathf.Max(Config.HudClear, Config.HudInterval + 0.3f));
                }
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] SSS 回调异常: {ex.Message}", $"[EXPAS] SSS callback error: {ex.Message}")); }
        }

        // ========================================================================
        // 面板里的「我的数据」纯文本区 / The plain-text "My Data" area in the panel
        // ========================================================================
        private void RefreshMyData(Player p, PlayerData d)
        {
            if (_myDataArea == null || p == null || d == null) return;
            try
            {
                double playSec = d.PlaySeconds;
                if (_sessionStart.TryGetValue(d.UserId, out DateTime start))
                    playSec += (DateTime.UtcNow - start).TotalSeconds;

                string head = BuildPanelHudText(p, d);
                string text = (string.IsNullOrEmpty(head) ? string.Empty : head + "\n---\n") +
                    L.T($"等级: {d.Level}    经验: {d.Exp:F0}/{ExpNeeded(d.Level):F0}\n",
                        $"Level: {d.Level}    EXP: {d.Exp:F0}/{ExpNeeded(d.Level):F0}\n") +
                    L.T($"距离下一级还差: {Mathf.Max(0f, ExpNeeded(d.Level) - d.Exp):F0}\n",
                        $"EXP to next level: {Mathf.Max(0f, ExpNeeded(d.Level) - d.Exp):F0}\n") +
                    L.T($"累计经验: {d.TotalExp:F0}    总伤害: {d.TotalDamage:F0}\n",
                        $"Total EXP: {d.TotalExp:F0}    Total Damage: {d.TotalDamage:F0}\n") +
                    L.T($"击杀: {d.Kills}    死亡: {d.Deaths}\n",
                        $"Kills: {d.Kills}    Deaths: {d.Deaths}\n") +
                    L.T($"游玩: {FormatDuration(playSec)}    {GetTimeZoneLabel()} {GetLocalTimeText()}\n",
                        $"Played: {FormatDuration(playSec)}    {GetTimeZoneLabel()} {GetLocalTimeText()}\n") +
                    L.T($"状态: {GetStatusText(d)}", $"Status: {GetStatusText(d)}") +
                    (d.PendingExp > 0f ? L.T($"    待入账经验: {d.PendingExp:F0}", $"    Pending EXP: {d.PendingExp:F0}") : string.Empty) +
                    // v1.6: 显示"检测到的宽高比 + 当前分辨率档位 + 整体缩放 + 最终字号 + 排版" / v1.6: show "detected aspect ratio + current resolution preset + overall scale + final font size + layout"
                    //   ★ 服务端读不到像素分辨率, 只能读宽高比/视野 → 这里显示的是宽高比标签, 不是像素 /   ★ The server cannot read pixel resolution, only aspect ratio/FOV → this shows an aspect-ratio label, not pixels
                    L.T($"\n屏幕: 宽高比 {GetAspectLabel(p)}    分辨率档位 {ResolutionPresetName(GetResolutionPreset(d))}    整体缩放 {GetGlobalScalePercent(d)}%\n",
                        $"\nScreen: Aspect {GetAspectLabel(p)}    Resolution {ResolutionPresetName(GetResolutionPreset(d))}    Scale {GetGlobalScalePercent(d)}%\n") +
                    L.T($"积分: {d.Points:F0}", $"Points: {d.Points:F0}") +
                    L.T($"\nHUD: {(IsHudOff(d.UserId) ? "关闭" : "开启")}  排版 {(IsHudCollapsed(d) ? "折叠(单行)" : "展开")}  最终字号 {FinalSizePercent(p, d)}%  ",
                        $"\nHUD: {(IsHudOff(d.UserId) ? "Off" : "On")}  Layout {(IsHudCollapsed(d) ? "Collapsed (single line)" : "Expanded")}  Final Font Size {FinalSizePercent(p, d)}%  ") +
                    L.T($"字号 {GetSizePercent(d)}%  屏幕 {GetScreenPercent(d)}%  行距 {GetLineHeightPercent(d)}%  ",
                        $"Font Size {GetSizePercent(d)}%  Screen {GetScreenPercent(d)}%  Line Height {GetLineHeightPercent(d)}%  ") +
                    L.T($"垂直 {GetVOffset(d)}  对齐 {AlignName(GetHAlign(d))}/{AlignName(GetLevelAlign(d))}",
                        $"Offset {GetVOffset(d)}  Align {AlignName(GetHAlign(d))}/{AlignName(GetLevelAlign(d))}");

                // SSTextArea.SendTextUpdate 不带 receiveFilter 时底层走 SendToAuthenticated() = 发全服, / Without receiveFilter, SSTextArea.SendTextUpdate uses SendToAuthenticated() = send to the whole server,
                // 多人同服时所有人都会看到"最后一个被刷新的玩家"的数据 → 必须只发给本人 / with multiple players on one server everyone would see the data of "the last refreshed player" → must send only to the player themself
                Func<ReferenceHub, bool> onlyMe = h => h == p.ReferenceHub;
                _myDataArea.SendTextUpdate(text, receiveFilter: onlyMe);
                if (_killFeedArea != null) _killFeedArea.SendTextUpdate(BuildKillFeedText(p, d), receiveFilter: onlyMe);
            }
            catch { }
        }

        public static string AlignName(int a)
        {
            switch (a)
            {
                case 0: return L.T("左", "Left");
                case 2: return L.T("右", "Right");
                case 3: return L.T("跟随", "Follow");
                default: return L.T("居中", "Center");
            }
        }

        private string GetStatusText(PlayerData d)
        {
            // v1.6: 图标统一走 LockIcon(修 unicode 缺字, 默认 [锁]/[禁]/[离]) / v1.6: icons consistently use LockIcon (fixes missing unicode glyphs; defaults [lock]/[ban]/[leave])
            switch (d.LockState)
            {
                case LockPunishment: return L.T($"{LockIcon(LockPunishment)} punishment 惩罚锁定(伤害/经验都不记录)", $"{LockIcon(LockPunishment)} punishment Punishment lock (damage/EXP not recorded)");
                case LockSelfBan: return L.T($"{LockIcon(LockSelfBan)} selfban 自助封禁(伤害不涨, 经验在叠加)", $"{LockIcon(LockSelfBan)} selfban Self-ban (damage does not increase, EXP accumulates)");
                case LockInactive: return L.T($"{LockIcon(LockInactive)} inactive 长期未上线(回来自动清回 none)", $"{LockIcon(LockInactive)} inactive Inactive (auto-clears to none on return)");
            }
            if (d.LongOffline) return L.T($"{LockIcon(LockInactive)} 长期未上线", $"{LockIcon(LockInactive)} Inactive");
            return L.T("正常", "Normal");
        }

        // ========================================================================
        // 玩家设置 → 有效值(未设置则落到配置默认) / Player settings → effective value (falls back to config defaults when unset)
        // ========================================================================
        public int GetSizePercent(PlayerData d) => d.SizePercent > 0 ? d.SizePercent : Mathf.Clamp(Config.HudSizePercentDefault, 40, 160);
        public int GetScreenPercent(PlayerData d) => d.ScreenPercent > 0 ? d.ScreenPercent : Mathf.Clamp(Config.HudScreenPercentDefault, 50, 150);
        public int GetLineHeightPercent(PlayerData d) => d.LineHeightPercent > 0 ? d.LineHeightPercent : Mathf.Clamp(Config.HudLineHeightDefault, 60, 140);
        public int GetVOffset(PlayerData d) => d.VOffset;
        public int GetHAlign(PlayerData d) => d.HAlign >= 0 ? Mathf.Clamp(d.HAlign, 0, 2) : Mathf.Clamp(Config.HudAlignDefault, 0, 2);
        public int GetLevelAlign(PlayerData d)
        {
            int la = d.LevelAlign >= 0 ? Mathf.Clamp(d.LevelAlign, 0, 3) : Mathf.Clamp(Config.HudLevelAlignDefault, 0, 3);
            return la == 3 ? GetHAlign(d) : la;
        }

        // ========================================================================
        // v1.6 屏幕宽高比 / 分辨率档位 / 整体缩放 / HUD 折叠 / v1.6 Screen aspect ratio / resolution preset / overall scale / HUD collapse
        //   ★ 服务端**读不到像素分辨率**, 只能读宽高比与视野 —— 依据(全部来自 game-src): /   ★ The server **cannot read pixel resolution**, only aspect ratio and FOV — basis (all from game-src):
        //     游戏本体 ReferenceHub.aspectRatioSync(G_7103, [字段] public AspectRatioSync aspectRatioSync) /     Game's ReferenceHub.aspectRatioSync (G_7103, [field] public AspectRatioSync aspectRatioSync)
        //       类型 AspectRatioSync : NetworkBehaviour(G_14903); /       type AspectRatioSync : NetworkBehaviour (G_14903);
        //       实例字段/属性 XScreenEdge(G_14917, SyncVar, 由客户端 Cmd 上报); /       instance field/property XScreenEdge (G_14917, SyncVar, reported by client Cmd);
        //       静态属性 YScreenEdge(G_14915); /       static property YScreenEdge (G_14915);
        //       Start() 里 YScreenEdge = 相机 FOV / 2(G_14956, _defaultCameraFieldOfView / 2f)。 /       in Start(), YScreenEdge = camera FOV / 2 (G_14956, _defaultCameraFieldOfView / 2f).
        //     LabAPI 侧**没有封装**(在 LabApi.decompiled.cs 里 grep aspectRatioSync 0 命中) → /     LabAPI side has **no wrapper** (grep aspectRatioSync in LabApi.decompiled.cs gives 0 hits) →
        //     只能直接读游戏组件字段。 /     we can only read the game component fields directly.
        //   结论: "分辨率档位"是"玩家自选 + 按宽高比自动", 不是真的检测像素分辨率。 /   Conclusion: "resolution preset" is "player-chosen + automatic by aspect ratio", not real pixel-resolution detection.
        // ========================================================================

        /// <summary>
        /// 取屏幕宽高比: ratio = XScreenEdge / YScreenEdge(若 YScreenEdge&lt;=0 或组件为 null → 返回 false)。 / Get the screen aspect ratio: ratio = XScreenEdge / YScreenEdge (returns false if YScreenEdge &lt;= 0 or the component is null).
        /// label 按"真实宽高比"归类(16:9 / 16:10 / 21:9 / 4:3 / 其它(x.xx))。 / label is categorized by the "real aspect ratio" (16:9 / 16:10 / 21:9 / 4:3 / other(x.xx)).
        /// ★ 全程 try/catch 兜底 —— aspectRatioSync 组件在服务器上可能为 null。 / ★ Full try/catch fallback — the aspectRatioSync component may be null on the server.
        /// </summary>
        public bool TryGetAspect(Player p, out float ratio, out string label)
        {
            ratio = 0f; label = "-";
            try
            {
                if (p == null || p.IsDestroyed) return false;
                ReferenceHub hub = p.ReferenceHub;               // LabApi Player.ReferenceHub(L_14021)
                if (hub == null) return false;

                AspectRatioSync sync = hub.aspectRatioSync;      // G_7103
                if (sync == null) return false;

                float y = AspectRatioSync.YScreenEdge;           // G_14915 (static; = 相机FOV/2, G_14956) / G_14915 (static; = camera FOV/2, G_14956)
                if (!(y > 0f)) return false;
                float x = sync.XScreenEdge;                      // G_14917 (SyncVar, 客户端上报) / G_14917 (SyncVar, reported by the client)
                ratio = x / y;

                // 标签取"真实宽高比": 优先 sync.AspectRatio(客户端上报的 Screen.width/Screen.height); / Label uses the "real aspect ratio": prefer sync.AspectRatio (Screen.width/Screen.height reported by the client);
                // 退化时用 tan(XScreenEdge)/tan(YScreenEdge) 反解(由 G_15007-G_15009 的公式推得)。 / when unavailable, derive it from tan(XScreenEdge)/tan(YScreenEdge) (derived from the formulas at G_15007-G_15009).
                float aspect = 0f;
                try { aspect = sync.AspectRatio; } catch { }
                if (!(aspect > 1.001f) && x > 0f)
                {
                    float ty = Mathf.Tan(y * Mathf.Deg2Rad);
                    if (Mathf.Abs(ty) > 1e-6f) aspect = Mathf.Tan(x * Mathf.Deg2Rad) / ty;
                }
                if (float.IsNaN(aspect) || float.IsInfinity(aspect) || !(aspect > 1.001f)) return false;

                label = AspectLabel(aspect);
                return true;
            }
            catch { ratio = 0f; label = "-"; return false; }
        }

        /// <summary>宽高比 → 标签(16:9 / 16:10 / 21:9 / 4:3 / 其它(x.xx))。 / Aspect ratio → label (16:9 / 16:10 / 21:9 / 4:3 / other(x.xx)).</summary>
        private static string AspectLabel(float a)
        {
            if (Mathf.Abs(a - 16f / 9f) <= 0.09f) return "16:9";
            if (Mathf.Abs(a - 16f / 10f) <= 0.06f) return "16:10";
            if (a >= 2.05f) return "21:9";                                    // 超宽(21:9≈2.33)及以上 / Ultrawide (21:9 ≈ 2.33) and above
            if (Mathf.Abs(a - 4f / 3f) <= 0.06f) return "4:3";
            return L.T("其它(" + a.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ")",
                       "Other (" + a.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>屏幕宽高比标签(取不到时 "-"), 供 {aspect} 占位符与面板显示。 / Screen aspect-ratio label ("-" if unavailable), used by the {aspect} placeholder and panel display.</summary>
        public string GetAspectLabel(Player p)
            => TryGetAspect(p, out _, out string label) ? label : "-";

        /// <summary>「自动检测」档位的基准倍率: 16:9=1.0, 21:9/超宽=0.9, 4:3=1.1, 其它=1.0。 / Base multiplier for the "Auto Detect" preset: 16:9 = 1.0, 21:9/ultrawide = 0.9, 4:3 = 1.1, other = 1.0.</summary>
        private float AutoAspectMultiplier(Player p)
        {
            string label = GetAspectLabel(p);
            if (label == "21:9") return 0.9f;
            if (label == "4:3") return 1.1f;
            return 1.0f;
        }

        // ========================================================================
        // v1.7 屏幕分辨率档位表(16 档) + 倍率 / v1.7 Screen resolution preset table (16 presets) + multipliers
        //   ★ 服务端**读不到像素分辨率**(只能读宽高比/视野), 所以这是"玩家自选 + 按宽高比自动"。 /   ★ The server **cannot read pixel resolution** (only aspect ratio/FOV), so this is "player-chosen + automatic by aspect ratio".
        //   档位: 0=自动检测, 1..15 = 具体分辨率(下拉里的顺序与下表一一对应)。 /   Presets: 0 = auto-detect, 1..15 = specific resolutions (dropdown order matches the table below one-to-one).
        //   倍率: 以 1080p(1920x1080) = 1.00 为基准, 按面积开方**反比** —— /   Multiplier: based on 1080p (1920x1080) = 1.00, inversely proportional to the square root of the area —
        //         m = sqrt(1920*1080 / (w*h)), 夹到 [0.6, 1.6](屏幕越大 → 倍率越小 → 同样的 % 字号显示更小)。 /         m = sqrt(1920*1080 / (w*h)), clamped to [0.6, 1.6] (larger screen → smaller multiplier → the same % font size appears smaller).
        //   实测倍率表(计算值, 保留两位): /   Measured multiplier table (computed values, two decimals):
        //     640x480  2.60→1.60(夹)  800x600  2.08→1.60(夹)  1024x768 1.62→1.60(夹) / clamped
        //     1280x720 1.50          1280x800 1.42          1366x768 1.41
        //     1440x900 1.26          1600x900 1.20          1680x1050 1.08
        //     1920x1080 1.00         1920x1200 0.95         2560x1080 0.87
        //     2560x1440 0.75         3440x1440 0.65         3840x2160 0.50→0.60(夹) / clamped
        // ========================================================================
        public static string[] ResolutionLabels => new[]
        {
            L.T("自动检测", "Auto Detect"), "640x480", "800x600", "1024x768", "1280x720", "1280x800", "1366x768",
            "1440x900", "1600x900", "1680x1050", "1920x1080", "1920x1200", "2560x1080",
            "2560x1440", "3440x1440", "3840x2160(4K)"
        };

        /// <summary>档位 → {宽,高}(下标 0=自动检测, 不用; 1..15 与 ResolutionLabels 对齐)。 / Preset → {width,height} (index 0 = auto-detect, unused; 1..15 aligned with ResolutionLabels).</summary>
        private static readonly int[][] ResolutionSizes = new[]
        {
            new[] { 0, 0 },              // 0 = 自动检测(按宽高比) / 0 = auto-detect (by aspect ratio)
            new[] { 640, 480 },
            new[] { 800, 600 },
            new[] { 1024, 768 },
            new[] { 1280, 720 },
            new[] { 1280, 800 },
            new[] { 1366, 768 },
            new[] { 1440, 900 },
            new[] { 1600, 900 },
            new[] { 1680, 1050 },
            new[] { 1920, 1080 },        // 基准 = 1.00 / Baseline = 1.00
            new[] { 1920, 1200 },
            new[] { 2560, 1080 },
            new[] { 2560, 1440 },
            new[] { 3440, 1440 },
            new[] { 3840, 2160 }
        };

        /// <summary>当前生效的分辨率档位(0=自动检测 1..15=具体档; 未设置则取服务器配置)。 / Currently effective resolution preset (0 = auto-detect, 1..15 = specific; falls back to server config when unset).</summary>
        public int GetResolutionPreset(PlayerData d)
            => d.ResolutionPreset >= 0 ? Mathf.Clamp(d.ResolutionPreset, 0, 15) : Mathf.Clamp(Config.ResolutionPreset, 0, 15);

        /// <summary>分辨率档位名(面板/日志显示用)。 / Resolution preset name (for panel/log display).</summary>
        public static string ResolutionPresetName(int preset)
        {
            if (preset < 0 || preset >= ResolutionLabels.Length) return L.T("自动检测", "Auto Detect");
            return ResolutionLabels[preset];
        }

        /// <summary>
        /// 分辨率档位倍率: 0=自动检测(按宽高比), 1..15=按 sqrt(1920*1080/(宽*高)) 反比, 夹 [0.6,1.6]。 / Resolution preset multiplier: 0 = auto-detect (by aspect ratio), 1..15 = inverse sqrt(1920*1080/(w*h)), clamped to [0.6,1.6].
        /// </summary>
        public float GetResolutionMultiplier(Player p, PlayerData d)
        {
            int preset = GetResolutionPreset(d);
            if (preset <= 0 || preset >= ResolutionSizes.Length) return AutoAspectMultiplier(p);
            int[] wh = ResolutionSizes[preset];
            if (wh == null || wh[0] <= 0 || wh[1] <= 0) return 1f;
            float area = (float)wh[0] * (float)wh[1];
            float m = Mathf.Sqrt(1920f * 1080f / area);
            return Mathf.Clamp(m, 0.6f, 1.6f);
        }

        /// <summary>整体缩放 %(未设置则取服务器配置, 默认 100)。 / Overall scale % (falls back to server config when unset, default 100).</summary>
        public int GetGlobalScalePercent(PlayerData d)
            => d.GlobalScalePercent > 0 ? Mathf.Clamp(d.GlobalScalePercent, 50, 200) : Mathf.Clamp(Config.GlobalScalePercent, 50, 200);

        /// <summary>是否处于 HUD 折叠(单行)模式(未设置则取服务器配置, 默认 0=展开)。 / Whether HUD collapsed (single-line) mode is active (falls back to server config when unset, default 0 = expanded).</summary>
        public bool IsHudCollapsed(PlayerData d)
            => (d.HudCollapsed >= 0 ? d.HudCollapsed : Config.HudCollapsed) == 1;

        /// <summary>
        /// v1.6 最终字号百分比 = 字号% × 屏幕缩放% × 分辨率档位倍率 × 整体缩放% ÷ 10000, 夹在 20..240。 / v1.6 final font size percentage = font size% × screen scale% × resolution multiplier × overall scale% ÷ 10000, clamped to 20..240.
        /// 默认值下(85 × 100 × 1.0 × 100 ÷ 10000 = 85)与改造前"字号 × 屏幕缩放 ÷ 100"的结果**完全一致**。 / With defaults (85 × 100 × 1.0 × 100 ÷ 10000 = 85) the result is **exactly the same** as the pre-change "font size × screen scale ÷ 100".
        /// </summary>
        public int FinalSizePercent(Player p, PlayerData d)
        {
            float v = GetSizePercent(d) * GetScreenPercent(d) * GetResolutionMultiplier(p, d) * GetGlobalScalePercent(d) / 10000f;
            return Mathf.Clamp(Mathf.RoundToInt(v), 20, 240);
        }

        private static string AlignTag(int a)
        {
            switch (a)
            {
                case 0: return "left";
                case 2: return "right";
                default: return "center";
            }
        }

        // ========================================================================
        // v1.7 HUD 布局系统 —— 布局独立成文件: configs\<服务器端口>\EXPAS\layouts\<key>.txt / v1.7 HUD layout system — layouts are separate files: configs\<server port>\EXPAS\layouts\<key>.txt
        // ========================================================================
        //  [为什么] 屏幕 HUD 的一排排文本原来写死在插件里/配置模板里, 想换排版只能改代码。 /  [Why] The screen HUD's rows of text used to be hard-coded in the plugin/config templates, so changing the layout meant editing code.
        //           现在"排版 + 文本"都放到 layouts\ 下的独立文件, 文件名去扩展名 = 布局 key。 /           Now both "layout + text" live in separate files under layouts\; the file name without extension = layout key.
        //
        //  [文件语法] (UTF-8; 以 # 开头的整行 = 注释; 空行忽略) /  [File syntax] (UTF-8; a whole line starting with # = comment; blank lines ignored)
        //     name = 布局显示名          (可选) /     name = layout display name          (optional)
        //     row|<left|center|right>|<垂直偏移px>|<文本> /     row|<left|center|right>|<vertical offset px>|<text>
        //     level|<left|center|right>|<垂直偏移px>|<文本> /     level|<left|center|right>|<vertical offset px>|<text>
        //   · row   = 主块的一排(默认贴在屏幕**最下方居中**) /   · row   = a row of the main block (defaults to **bottom-center** of the screen)
        //   · level = 等级块的一排(默认在**右下方, 且比主块再往上一点**) /   · level = a row of the level block (defaults to **bottom right, slightly higher than the main block**)
        //   · 文本支持全部 Tok() 占位符({name} {level} {expNeed} {playtime} …) /   · text supports all Tok() placeholders ({name} {level} {expNeed} {playtime} …)
        //   · 偏移语义: **数值越大越靠上**。同一块内按偏移从大到小自上而下排; /   · offset semantics: **larger value = higher up**. Within a block, rows are ordered top-to-bottom by descending offset;
        //              等级块整块再按"块内最大偏移"额外上抬, 所以 level 用正数就能压在主块上方。 /              the whole level block is additionally raised by its "maximum offset", so positive level values place it above the main block.
        //   · 行内注释: " #"(前面有空白)之后的内容会被丢掉; 行首的 # 是正文(例 "#1 欢迎 …")。 /   · inline comments: content after " #" (preceded by whitespace) is discarded; a leading # is body text (e.g. "#1 Welcome …").
        //
        //  [安全] layouts\ 缺失 / 文件读坏 / 一行可用都没有 → 回退内置默认布局(永远是 default, 必定可用)。 /  [Safety] layouts\ missing / file corrupted / no usable rows → fall back to the built-in default layout (always "default", guaranteed available).
        //         主块输出在最下面一行, 并统一留 LayoutSafeMarginPx 的安全余量, 避免贴边被裁切。 /         The main block is output at the very bottom, with a LayoutSafeMarginPx safety margin to avoid clipping at the edge.
        // ========================================================================

        /// <summary>主块最下面一行额外上移的像素(安全余量): 避免贴住屏幕底边被裁切。 / Extra pixels to raise the bottom row of the main block (safety margin): avoids clipping at the bottom screen edge.</summary>
        private const int LayoutSafeMarginPx = 12;

        /// <summary>布局文件里的一排。 / A row in a layout file.</summary>
        public class HudLayoutRow
        {
            public string Kind = "row";        // row | level
            public int Align = 1;              // 0=左 1=居中 2=右 / 0=left 1=center 2=right
            public int Offset = 0;             // 像素; 数值越大越靠上 / pixels; larger value = higher up
            public int Line = 0;               // 源文件行号(报错/排查用) / source file line number (for errors/troubleshooting)
            public string Text = string.Empty; // 原始模板文本(渲染时走 Tok()) / raw template text (rendered via Tok())
        }

        /// <summary>一份布局(= 一个 layouts\*.txt / *.js, 或内置默认布局)。 / One layout (= a layouts\*.txt / *.js file, or the built-in default layout).</summary>
        public class HudLayoutDef
        {
            public string Key = "default";
            public string DisplayName = L.T("默认布局", "Default Layout");
            public bool FromFile = false;      // true = 来自磁盘文件; false = 内置默认 / true = from a disk file; false = built-in default
            public string SourcePath = string.Empty;
            /// <summary>归属 Steam64(CanonUserId 归一化; 空=公共文件, 只有白名单玩家可见)。 / Owner Steam64 (normalized by CanonUserId; empty = public file, visible only to whitelisted players).</summary>
            public string Owner = string.Empty;
            public readonly List<HudLayoutRow> Main = new List<HudLayoutRow>();    // row  主块(默认屏幕最下方居中) / row  main block (default bottom-center)
            public readonly List<HudLayoutRow> Level = new List<HudLayoutRow>();   // level 等级块(默认右下方, 比主块更靠上) / level level block (default bottom right, higher than the main block)
            public readonly List<HudLayoutRow> Kill = new List<HudLayoutRow>();    // kill 击杀确认块(只在击杀显示期内显示) / kill kill-confirmation block (shown only during the kill notice period)
            public readonly List<HudLayoutRow> Fold = new List<HudLayoutRow>();    // fold 折叠模式单行(HUD 折叠时用) / fold single line in collapsed mode (used when the HUD is collapsed)
            public readonly List<HudLayoutRow> Custom = new List<HudLayoutRow>();  // custom 配置自定义行插入点(CustomLines) / custom insertion point for config custom lines (CustomLines)
        }

        /// <summary>内置默认布局的原文(= 首次运行写出的 layouts\default.txt 内容)。 / Source text of the built-in default layout (= content of layouts\default.txt written on first run).</summary>
        public static string[] DefaultLayoutLines => new[]
        {
            L.T("# EXPAS HUD 布局文件(必须 UTF-8)", "# EXPAS HUD layout file (must be UTF-8)"),
            L.T("name = 默认布局", "name = Default Layout"),
            L.T("# 主块: 默认贴在屏幕最下方居中, 每排自上而下叠加", "# Main block: anchored bottom-center by default; rows stack top to bottom"),
            L.T("# 格式: row|<left|center|right>|<垂直偏移px>|<文本(支持全部占位符)>", "# Format: row|<left|center|right>|<vertical offset px>|<text (all placeholders supported)>"),
            L.T("row|center|-0|#1 欢迎 {name} 进入到 {server}", "row|center|-0|#1 Welcome {name} to {server}"),
            L.T("row|center|-32|#2 当前已经游玩了 {days} 天  当前时间段是 {tz} {clock}  您准确的游玩时间是 {playtime}", "row|center|-32|#2 Played {days} days  Local time {tz} {clock}  Total playtime {playtime}"),
            L.T("row|center|-64|#3 当前回合已造成 {roundDmg}  当前使用的角色已造成 {roleDmg}  总伤害为 {totalDmg}", "row|center|-64|#3 Round damage {roundDmg}  Role damage {roleDmg}  Total damage {totalDmg}"),
            "row|center|-96|#4 EXPAS v{version}{icons}",
            L.T("# 等级块: 默认右下方, 且比主块再往上一点", "# Level block: bottom right by default, a bit higher than the main block"),
            L.T("level|right|40|您当前有 {level} 级", "level|right|40|Level {level}"),
            L.T("level|right|16|距离下一次升级还差 {expNeed} 经验", "level|right|16|{expNeed} EXP to next level")
        };

        /// <summary>示例自定义布局的原文(首次运行写出的 layouts\example.txt 内容)。 / Source text of the example custom layout (content of layouts\example.txt written on first run).</summary>
        public static string[] ExampleLayoutLines => new[]
        {
            L.T("# EXPAS HUD 布局文件(必须 UTF-8)", "# EXPAS HUD layout file (must be UTF-8)"),
            L.T("name = 示例布局(左上角竖排 + 右侧等级块)", "name = Example Layout (left-top vertical + right level block)"),
            L.T("# 主块: 这里演示左对齐 + 高偏移(数值越大越靠上)", "# Main block: demonstrates left alignment + high offset (larger = higher)"),
            L.T("row|left|120|欢迎 {name} 进入到 {server}  (第 {days} 天)", "row|left|120|Welcome {name} to {server}  (day {days})"),
            L.T("row|left|88|当前时间段 {tz} {clock}   准确游玩时间 {playtime}", "row|left|88|Local time {tz} {clock}   Total playtime {playtime}"),
            L.T("row|left|56|本回合伤害 {roundDmg}   角色伤害 {roleDmg}   总伤害 {totalDmg}", "row|left|56|Round damage {roundDmg}   Role damage {roleDmg}   Total damage {totalDmg}"),
            "row|left|24|EXPAS v{version}{icons}",
            L.T("# 等级块: 右侧, 比主块再往上", "# Level block: right side, higher than the main block"),
            L.T("level|right|200|Lv.{level}   经验 {exp}/{expNeed}", "level|right|200|Lv.{level}   EXP {exp}/{expNeed}"),
            L.T("level|right|168|积分 {points}   击杀 {kills}   死亡 {deaths}", "level|right|168|Points {points}   Kills {kills}   Deaths {deaths}")
        };

        private readonly Dictionary<string, HudLayoutDef> _layouts =
            new Dictionary<string, HudLayoutDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>layouts\ 目录(与 config.yml 同级)。 / layouts\ directory (next to config.yml).</summary>
        private string LayoutDir => Path.Combine(RootDir, "layouts");

        /// <summary>
        /// 扫描 layouts\ 目录建布局表。步骤: / Scan the layouts\ directory to build the layout table. Steps:
        ///   ① 先把"内置默认布局"兜进 default 键(必定可用); /   ① First put the "built-in default layout" into the default key (always available);
        ///   ② 建目录 + 缺文件时补写 default.txt / example.txt(UTF-8 无 BOM); /   ② Create the directory + write default.txt / example.txt when missing (UTF-8 without BOM);
        ///   ③ 逐个 .txt / .js 解析入库(同名文件覆盖内置 default); /   ③ Parse each .txt / .js into the table (a file with the same name overrides the built-in default);
        ///   ④ 解析失败/没有可用行的文件跳过并 Warn, 不影响别的布局。 /   ④ Skip and Warn on files that fail to parse or have no usable rows, without affecting other layouts.
        ///   ★ v1.8: 布局文件可带归属 owner Steam64(文件名 17 位数字前缀 或 首行 # owner= / // owner= / const owner=), /   ★ v1.8: layout files can carry an owner Steam64 (17-digit prefix in the file name or first line # owner= / // owner= / const owner=),
        ///     归属匹配的玩家才能在自己的 SSS 面板里看到这份布局(白名单为兼容覆盖)。 /     only players whose ownership matches can see this layout in their own SSS panel (whitelist overrides for compatibility).
        /// </summary>
        private void LoadLayouts()
        {
            _layouts.Clear();
            try
            {
                HudLayoutDef builtin = ParseLayoutLines("default", DefaultLayoutLines, string.Empty);
                builtin.DisplayName = L.T("默认布局", "Default Layout");
                builtin.FromFile = false;
                _layouts[builtin.Key] = builtin;

                string dir = LayoutDir;
                try { Directory.CreateDirectory(dir); } catch { }
                EnsureLayoutSampleFile(Path.Combine(dir, "default.txt"), DefaultLayoutLines);
                EnsureLayoutSampleFile(Path.Combine(dir, "example.txt"), ExampleLayoutLines);

                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.txt").Concat(Directory.GetFiles(dir, "*.js")))
                    {
                        string key = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrEmpty(key)) continue;
                        HudLayoutDef def = ParseLayoutFile(key, f);
                        if (def == null || (def.Main.Count == 0 && def.Level.Count == 0
                            && def.Kill.Count == 0 && def.Fold.Count == 0 && def.Custom.Count == 0))
                        {
                            Logger.Warn(L.T($"[EXPAS] 布局文件不可用(读取失败 / 没有合法 row| / level| 行), 已忽略: {f}", $"[EXPAS] Layout file unusable (read failed / no valid row| or level| lines), skipped: {f}"));
                            continue;
                        }
                        _layouts[key] = def;
                        Logger.Info(L.T($"[EXPAS] 布局已载入: {key} ({def.DisplayName}) 主块 {def.Main.Count} / 等级块 {def.Level.Count} / 击杀 {def.Kill.Count} / 折叠 {def.Fold.Count} / 自定义 {def.Custom.Count} 排", $"[EXPAS] Layout loaded: {key} ({def.DisplayName}) main {def.Main.Count} / level {def.Level.Count} / kill {def.Kill.Count} / fold {def.Fold.Count} / custom {def.Custom.Count} rows") +
                                    (def.Owner.Length > 0 ? L.T($"  归属 {def.Owner}", $"  owner {def.Owner}") : string.Empty) + $" ← {f}");
                    }
                }
                Logger.Info(L.T($"[EXPAS] 布局共 {_layouts.Count} 套: {string.Join(", ", _layouts.Keys)} (目录 {dir})", $"[EXPAS] {_layouts.Count} layouts in total: {string.Join(", ", _layouts.Keys)} (directory {dir})"));
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 布局扫描失败: {ex.Message}", $"[EXPAS] Layout scan failed: {ex.Message}")); }
        }

        /// <summary>文件不存在才写(UTF-8 **无 BOM**, 避免中文被写坏)。 / Write only if the file does not exist (UTF-8 **without BOM**, to avoid corrupting Chinese text).</summary>
        private static void EnsureLayoutSampleFile(string path, string[] lines)
        {
            try
            {
                if (File.Exists(path)) return;
                string text = string.Join("\n", lines) + "\n";
                File.WriteAllText(path, text, new UTF8Encoding(false));   // 无 BOM / No BOM
                Logger.Info(L.T($"[EXPAS] 已生成布局文件: {path}", $"[EXPAS] Generated layout file: {path}"));
            }
            catch (Exception ex) { Logger.Warn(L.T($"[EXPAS] 写布局文件失败 {path}: {ex.Message}", $"[EXPAS] Failed to write layout file {path}: {ex.Message}")); }
        }

        private HudLayoutDef ParseLayoutFile(string key, string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path, new UTF8Encoding(false));   // 无 BOM 读取 / Read without BOM
                // v1.8: .js 布局文件先"摊平"成纯文本行(游戏不能执行 JS, 这里按固定规则提取文本定义) / v1.8: .js layout files are first "flattened" into plain text lines (the game cannot execute JS; text definitions are extracted by fixed rules)
                if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    lines = JsToPlainLines(lines);
                HudLayoutDef def = ParseLayoutLines(key, lines, path);
                def.FromFile = true;
                // 归属双保险: 文件内声明了 owner 就用它; 否则看文件名开头的 17 位数字前缀(Steam64) / Ownership double-check: use the owner declared in the file; otherwise look at the 17-digit prefix at the start of the file name (Steam64)
                if (def.Owner.Length == 0)
                {
                    string fn = Find17DigitRun(Path.GetFileName(path));
                    if (fn != null) def.Owner = CanonUserId(fn);
                }
                return def;
            }
            catch (Exception ex)
            {
                Logger.Warn(L.T($"[EXPAS] 解析布局 {path} 失败: {ex.Message}", $"[EXPAS] Failed to parse layout {path}: {ex.Message}"));
                return null;
            }
        }

        /// <summary>
        /// 把 .js 布局文件的行"摊平"成与 .txt 相同的行格式(供 ParseLayoutLines 复用)。 / Flatten .js layout file lines into the same line format as .txt (reused by ParseLayoutLines).
        ///   · 丢弃 JS 壳: 行首 // 注释(带 owner 的保留)、纯括号/const/let/var 壳语句; /   · Drop JS shell: leading // comments (keep those with owner), pure bracket/const/let/var shell statements;
        ///   · 内容行: 抽引号串("row|…" / "level|…" / "name = …" / "owner = …"), 去引号与尾逗号/分号; /   · Content lines: extract quoted strings ("row|…" / "level|…" / "name = …" / "owner = …"), strip quotes and trailing commas/semicolons;
        ///   · 一行可拆多段(如 const rows = ["row|…", "row|…"];)。 /   · One line may split into multiple segments (e.g. const rows = ["row|…", "row|…"];).
        /// </summary>
        private static string[] JsToPlainLines(string[] lines)
        {
            var outLines = new List<string>();
            if (lines == null) return outLines.ToArray();
            foreach (string ln in lines)
            {
                string t = (ln ?? string.Empty).Trim();
                if (t.Length == 0) continue;
                // 行首 // 注释: 含 owner 的保留(归属识别), 其余丢弃 / Leading // comment: keep those containing owner (ownership detection), discard the rest
                if (t.StartsWith("//"))
                {
                    if (t.IndexOf("owner", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    t = t.Substring(2).Trim();
                }
                if (t.Length == 0) continue;

                foreach (string seg in ExtractJsSegments(t))
                {
                    string s = seg.Trim();
                    if (s.Length == 0) continue;
                    outLines.Add(s);
                }
            }
            return outLines.ToArray();
        }

        /// <summary>
        /// 从一行 JS 里抽出"有用的"段: / Extract "useful" segments from one line of JS:
        ///   · 行内含 owner 关键字 + 17 位数字 → 直接产出 "owner = <数字>"; /   · line contains the owner keyword + a 17-digit number → directly produce "owner = <digits>";
        ///   · 否则抽取引号串("row|…" / "level|…" 内容行)与无引号的 owner/name 赋值; /   · otherwise extract quoted strings ("row|…" / "level|…" content lines) and unquoted owner/name assignments;
        ///   · 其它 JS 壳(纯括号等) → 空列表。 /   · other JS shell (pure brackets etc.) → empty list.
        /// </summary>
        private static List<string> ExtractJsSegments(string t)
        {
            var segs = new List<string>();

            // ① owner 归属优先: 一行里同时出现 owner 与 17 位数字 → 直接产出归属行 / ① owner takes priority: a line containing both owner and a 17-digit number → directly produce an owner line
            string digits = Find17DigitRun(t);
            if (digits != null && t.IndexOf("owner", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                segs.Add("owner = " + digits);
                return segs;
            }

            // ② 引号串: "row|…" / "level|…" / const name = "…" / const owner = "…" / ② Quoted strings: "row|…" / "level|…" / const name = "…" / const owner = "…"
            int i = 0;
            while (i < t.Length)
            {
                if (t[i] == '"' || t[i] == '\'')
                {
                    char q = t[i];
                    int j = i + 1;
                    var sb = new StringBuilder();
                    while (j < t.Length && t[j] != q) { sb.Append(t[j]); j++; }
                    string seg = sb.ToString().Trim();
                    if (seg.Length > 0) segs.Add(seg);
                    i = j + 1;
                    continue;
                }
                i++;
            }
            if (segs.Count > 0)
            {
                // 赋值语句(const/let/var xxx = "…"): 把 xxx 当元信息键产出 "xxx = <内容>", / Assignment statement (const/let/var xxx = "…"): treat xxx as a metadata key and produce "xxx = <content>",
                //   只认 name / owner —— 避免把正文里的 {name} 占位符误当成 name 赋值 /   accept only name / owner — avoids mistaking the {name} placeholder in body text for a name assignment
                string pre = t.TrimStart();
                string varName = null;
                if (pre.StartsWith("const ")) varName = KeywordAfter("const ", pre);
                else if (pre.StartsWith("let ")) varName = KeywordAfter("let ", pre);
                else if (pre.StartsWith("var ")) varName = KeywordAfter("var ", pre);
                if (varName != null && (varName == "name" || varName == "owner"))
                    return new List<string> { varName + " = " + segs[0] };
                return segs;
            }

            // ③ 无引号: 剥 JS/注释噪音后保留 owner/name 直写 / ③ Unquoted: strip JS/comment noise, then keep direct owner/name assignments
            string s = t.Replace("\"", "").Replace("'", "").Trim();
            if (s.StartsWith("#")) s = s.Substring(1).Trim();
            if (s.StartsWith("//")) s = s.Substring(2).Trim();
            if (s.StartsWith("const ")) s = s.Substring(6).Trim();
            if (s.StartsWith("let ")) s = s.Substring(4).Trim();
            if (s.StartsWith("var ")) s = s.Substring(4).Trim();
            s = s.TrimEnd(';', ',').Trim();
            if (s.StartsWith("owner", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("name", StringComparison.OrdinalIgnoreCase))
                segs.Add(s);
            return segs;
        }

        /// <summary>取赋值语句前缀后紧跟的变量名(const rows = → "rows"), 取不到返回 null。 / Get the variable name immediately after an assignment prefix (const rows = → "rows"); null if not found.</summary>
        private static string KeywordAfter(string prefix, string s)
        {
            string rest = s.Substring(prefix.Length).TrimStart();
            int sp = 0;
            while (sp < rest.Length && (char.IsLetterOrDigit(rest[sp]) || rest[sp] == '_')) sp++;
            return sp > 0 ? rest.Substring(0, sp).ToLowerInvariant() : null;
        }

        /// <summary>在一段文本里找第一个连续 17 位数字(Steam64), 找到返回该数字串, 否则 null。 / Find the first run of 17 consecutive digits (Steam64) in a text; return that digit string, otherwise null.</summary>
        private static string Find17DigitRun(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            for (int i = 0; i < s.Length; i++)
                if (char.IsDigit(s[i]))
                {
                    int j = i;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    if (j - i == 17) return s.Substring(i, 17);
                    i = j;
                }
            return null;
        }

        /// <summary>把布局文件的行解析成 HudLayoutDef(内置默认布局也走这里, 保证两者一致)。 / Parse layout file lines into a HudLayoutDef (the built-in default layout also goes through here, ensuring consistency).</summary>
        private static HudLayoutDef ParseLayoutLines(string key, string[] lines, string sourcePath)
        {
            var def = new HudLayoutDef { Key = key, DisplayName = key, SourcePath = sourcePath ?? string.Empty };
            if (lines == null) return def;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = (lines[i] ?? string.Empty).Trim();
                if (raw.Length == 0) continue;      // 空行 / Blank line

                // v1.8 归属 owner(Steam64): 支持 "# owner=…" / "// owner=…" / "owner=…" / "const owner = \"…\"" / v1.8 owner (Steam64): supports "# owner=…" / "// owner=…" / "owner=…" / "const owner = \"…\""
                //   —— 必须放在"整行注释跳过"之前判断, 因为 "# owner" 本身是注释行 /   — must be checked before "skip whole-line comments", because "# owner" itself is a comment line
                string own = TryExtractOwner(raw);
                if (own != null)
                {
                    if (def.Owner.Length == 0) def.Owner = CanonUserId(own);
                    continue;
                }

                if (raw[0] == '#') continue;        // 整行注释 / Whole-line comment

                // 元信息行: name = xxx (不含 | 才当元信息) / Metadata line: name = xxx (only treated as metadata when it contains no |)
                if (raw.IndexOf('=') > 0 && raw.IndexOf('|') < 0)
                {
                    int eq = raw.IndexOf('=');
                    string mk = raw.Substring(0, eq).Trim().ToLowerInvariant();
                    string mv = StripInlineComment(raw.Substring(eq + 1));
                    if (mk == "name" && mv.Length > 0) def.DisplayName = mv;
                    continue;
                }

                string[] p = raw.Split('|');
                if (p.Length < 2) continue;
                string kind = p[0].Trim().ToLowerInvariant();
                // v1.8.4 扩展行类型: row(主块) / level(等级块) / kill(击杀确认块) / fold(折叠单行) / custom(配置自定义行插入点) / v1.8.4 extended row types: row (main block) / level (level block) / kill (kill-confirmation block) / fold (collapsed single line) / custom (config custom-line insertion point)
                if (kind != "row" && kind != "level" && kind != "kill" && kind != "fold" && kind != "custom") continue;

                var row = new HudLayoutRow
                {
                    Kind = kind,
                    Line = i + 1,
                    Align = ParseAlign(p.Length > 1 ? p[1] : null),
                    Offset = ParseIntOr(p.Length > 2 ? p[2] : null, 0)
                };
                string text = p.Length > 3 ? string.Join("|", p.Skip(3).ToArray()) : string.Empty;
                row.Text = StripInlineComment(text);
                // custom 允许空文本(= 在该位置插入 config 的全部 CustomLines 组); 其它行文本为空才跳过 / custom allows empty text (= insert the config's whole CustomLines group at this position); other rows are skipped only when their text is empty
                if (kind != "custom" && row.Text.Trim().Length == 0) continue;

                switch (kind)
                {
                    case "row": def.Main.Add(row); break;
                    case "level": def.Level.Add(row); break;
                    case "kill": def.Kill.Add(row); break;
                    case "fold": def.Fold.Add(row); break;
                    case "custom": def.Custom.Add(row); break;
                }
            }
            return def;
        }

        /// <summary>
        /// 从一行里提取 owner Steam64(17 位数字), 取不到返回 null。 / Extract the owner Steam64 (17 digits) from a line; return null if not found.
        /// 识别: 行内出现 "owner" 关键字 + "=" 之后的 17 位数字(自动剥掉引号 / const|let|var / # | // 前缀)。 / Recognition: the line contains the "owner" keyword + a 17-digit number after "=" (automatically strips quotes / const|let|var / # | // prefixes).
        /// 注: 行文本里若恰好出现 "owner" 字样且有 17 位数字会被误判, 但布局文本里不会有这种写法, 可接受。 / Note: a line whose text happens to contain "owner" and 17 digits would be misidentified, but layout text never looks like that, so it is acceptable.
        /// </summary>
        private static string TryExtractOwner(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string s = line.Replace("\"", "").Replace("'", "");
            s = s.Replace("const", "").Replace("let", "").Replace("var", "");
            s = s.Replace("#", "").Replace("//", "");
            int oi = s.IndexOf("owner", StringComparison.OrdinalIgnoreCase);
            if (oi < 0) return null;
            int eq = s.IndexOf('=', oi);
            if (eq < 0) return null;
            string tail = s.Substring(eq + 1);
            for (int i = 0; i < tail.Length; i++)
                if (char.IsDigit(tail[i]))
                {
                    int j = i;
                    while (j < tail.Length && char.IsDigit(tail[j])) j++;
                    if (j - i == 17) return tail.Substring(i, 17);
                    i = j;
                }
            return null;
        }

        /// <summary>
        /// 去掉" # 行内注释"(# 前面必须是空白)。注意: 文本**行首**的 # 是正文(例如 "#1 欢迎 {name} …"), / Remove " # inline comments" (# must be preceded by whitespace). Note: a # at the **start** of the text is body content (e.g. "#1 Welcome {name} …"),
        /// 所以只从第 2 个字符开始找。 / so start searching from the 2nd character only.
        /// </summary>
        private static string StripInlineComment(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            for (int i = 1; i < s.Length; i++)
                if (s[i] == '#' && char.IsWhiteSpace(s[i - 1])) return s.Substring(0, i).TrimEnd();
            return s.Trim();
        }

        private static int ParseAlign(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1;
            switch (s.Trim().ToLowerInvariant())
            {
                case "left": case "l": case "左": return 0;
                case "right": case "r": case "右": return 2;
                case "center": case "centre": case "c": case "中": case "居中": return 1;
            }
            int n;
            if (int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out n))
                return Mathf.Clamp(n, 0, 2);
            return 1;
        }

        private static int ParseIntOr(string s, int def)
        {
            int v;
            if (int.TryParse((s ?? string.Empty).Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        // ---------------- 权限隔离: 自定义 UI 白名单 ---------------- / Permission isolation: Custom UI whitelist ----------------

        /// <summary>
        /// 把 Steam64 归一化成游戏口径的 UserId: "7656119…@steam" 原样, 纯数字补 "@steam"; / Normalize a Steam64 into the game's UserId format: "7656119…@steam" stays as-is, plain digits get "@steam" appended;
        /// 其它写法原样返回(大小写不敏感比较)。空串 → null。 / other forms are returned as-is (case-insensitive comparison). Empty string → null.
        /// </summary>
        public static string CanonUserId(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (s.Length == 0) return null;
            if (s.IndexOf('@') > 0) return s;
            if (s.All(char.IsDigit)) return s + "@steam";
            return s;
        }

        /// <summary>
        /// 该 UserId 是否在 Config.CustomUiWhitelist 里(**归一化匹配**: 7656119…@steam 与纯数字等价)。 / Whether this UserId is in Config.CustomUiWhitelist (**normalized match**: 7656119…@steam is equivalent to plain digits).
        /// 名单里写 "*" = 放开给所有人。 / Writing "*" in the list = allow everyone.
        /// </summary>
        public bool IsCustomUiWhitelisted(string userId)
        {
            string u = CanonUserId(userId);
            if (u == null) return false;
            List<string> list = Config != null ? Config.CustomUiWhitelist : null;
            if (list == null) return false;
            foreach (string w in list)
            {
                if (string.IsNullOrEmpty(w)) continue;
                if (w.Trim() == "*") return true;                       // 通配: 所有人 / Wildcard: everyone
                string c = CanonUserId(w);
                if (c != null && string.Equals(c, u, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 该玩家在 SSS「自定义 UI」下拉里能看到的选项: default 恒排第一, 其后是该玩家**可用**的布局 / The options this player can see in the SSS "Custom UI" dropdown: default is always first, followed by the layouts **available** to this player
        /// —— 白名单玩家 = 全部布局(兼容旧口径); 其它玩家 = 布局文件 owner Steam64 与本人匹配的布局 / — whitelisted players = all layouts (legacy behavior); other players = layouts whose file owner Steam64 matches them
        /// (一人一份, 互不干涉)。只有 default 时返回 ["default"], 调用方据此整项不显示。 / (one per player, no interference). When only default exists, returns ["default"], and the caller hides the entire item accordingly.
        /// </summary>
        private string[] LayoutKeysForPlayer(PlayerData d)
        {
            if (d == null) return new[] { "default" };
            List<string> keys = new List<string>();
            foreach (var kv in _layouts)
                if (CanUseLayout(d, kv.Key)) keys.Add(kv.Key);
            keys.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            var list = new List<string>();
            if (keys.Remove("default")) list.Add("default");
            list.AddRange(keys);
            return list.ToArray();
        }

        /// <summary>
        /// 该玩家能否使用某个布局: default 恒可用 → 白名单(IsCustomUiWhitelisted)全部可用 / Whether this player can use a layout: default is always available → whitelisted (IsCustomUiWhitelisted) means all are available
        /// → 布局文件的 owner Steam64 与本人 UserId 归一化匹配才可用(权限隔离)。 / → a layout file's owner Steam64 must match the player's UserId after normalization to be usable (permission isolation).
        /// </summary>
        private bool CanUseLayout(PlayerData d, string key)
        {
            if (d == null || string.IsNullOrEmpty(key)) return false;
            if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase)) return true;
            if (!_layouts.ContainsKey(key)) return false;
            if (IsCustomUiWhitelisted(d.UserId)) return true;
            return OwnerMatches(_layouts[key], d.UserId);
        }

        /// <summary>布局文件 owner 与玩家 UserId 是否归属匹配(CanonUserId 归一化, 大小写不敏感)。 / Whether the layout file owner matches the player's UserId (normalized by CanonUserId, case-insensitive).</summary>
        private static bool OwnerMatches(HudLayoutDef def, string userId)
        {
            if (def == null || string.IsNullOrEmpty(def.Owner)) return false;
            string u = CanonUserId(userId);
            if (u == null) return false;
            return string.Equals(CanonUserId(def.Owner), u, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 玩家**实际生效**的布局 key: 只有该玩家"可用"的布局(归属匹配或白名单)且确实存在, 才用他自己选的; / The layout key **actually in effect** for the player: only if their chosen layout is "available" (owner match or whitelist) and actually exists;
        /// 其余一律 "default"。 (权限隔离: 从白名单移除 / 归属文件被删后, 自动回到默认) / otherwise always "default". (Permission isolation: after removal from the whitelist / deletion of the owner file, automatically return to default)
        /// </summary>
        public string GetLayoutKey(PlayerData d)
        {
            if (d != null && !string.IsNullOrEmpty(d.HudLayout) && CanUseLayout(d, d.HudLayout))
                return d.HudLayout;
            return "default";
        }

        private HudLayoutDef GetLayoutFor(PlayerData d)
        {
            HudLayoutDef def;
            if (_layouts.TryGetValue(GetLayoutKey(d), out def) && def != null) return def;
            if (_layouts.TryGetValue("default", out def)) return def;
            return null;
        }

        // ---------------- 按布局渲染 HUD ---------------- / Render the HUD from a layout ----------------

        /// <summary>
        /// 布局版 HUD(v1.8.4): 覆盖**全部 UI 部分** —— 5 种行类型: / Layout-based HUD (v1.8.4): covers **all UI parts** — 5 row types:
        ///   row|…    主块(默认屏幕最下方居中) /   row|…    main block (default bottom-center of the screen)
        ///   level|…  等级块(默认右下方, 比主块更靠上) /   level|…  level block (default bottom right, higher than the main block)
        ///   kill|…   击杀确认块(只在击杀显示期 KillNoticeSeconds 内渲染; 文本走 Tok, 支持 {killRole} 等) /   kill|…   kill-confirmation block (rendered only during the kill notice period KillNoticeSeconds; text goes through Tok, supports {killRole} etc.)
        ///   fold|…   HUD 折叠模式单行(IsHudCollapsed 时用它代替 HudCollapsedLine) /   fold|…   single line in HUD collapsed mode (replaces HudCollapsedLine when IsHudCollapsed)
        ///   custom|… 插入 config.yml 的 CustomLines 全部行(无文本段; 对齐/偏移取该行) /   custom|… inserts all CustomLines from config.yml (no text segment; alignment/offset come from this row)
        /// 竖直排布依据: Hint **固定锚在屏幕底部居中**(文档 21 号 L15), TMP 文本自上而下排 → / Vertical layout basis: Hint is **anchored bottom-center** (doc 21 L15) and TMP text flows top to bottom →
        ///   ① 等级块先输出 = 显示在主块**上方**(满足"右下方, 比主块再往上一点"); /   ① level block output first = displayed **above** the main block (satisfying "bottom right, slightly higher than the main block");
        ///   ② 主块后输出 = 贴在屏幕**最下方**; /   ② main block output later = pinned to the **very bottom** of the screen;
        ///   ③ 主块整体再上移 LayoutSafeMarginPx 像素留安全余量。 /   ③ the whole main block is raised by LayoutSafeMarginPx pixels as a safety margin.
        /// 垂直偏移放在"每个块"上(不做 &lt;voffset&gt; 嵌套), 外层只挂 字号/行距。 / Vertical offset is applied per "block" (no nested &lt;voffset&gt;); the outer layer only carries font size/line height.
        /// </summary>
        private string BuildHudFromLayout(Player p, PlayerData d, HudLayoutDef layout)
        {
            var body = new StringBuilder();
            // v1.8 分辨率适配: 把"像素级垂直定位"按分辨率档位倍率缩放 —— 同一布局在 4K/1080p 上位置观感一致 / v1.8 resolution adaptation: scale "pixel-level vertical positioning" by the resolution preset multiplier — the same layout looks consistent on 4K/1080p
            //   (字号已由 FinalSizePercent 用同一 GetResolutionMultiplier 缩放; 行内 Offset 只参与排序不参与定位) /   (font size is already scaled by FinalSizePercent with the same GetResolutionMultiplier; row Offset only affects ordering, not positioning)
            float m = Mathf.Max(0.1f, GetResolutionMultiplier(p, d));
            int vo = Config.HudUseTags ? Mathf.Clamp(Mathf.RoundToInt(GetVOffset(d) * m), -320, 320) : 0;

            // ① 等级块: 整块按"块内最大偏移"额外上抬(= 数值越大越靠上) / ① Level block: the whole block is additionally raised by its "maximum offset" (= larger value = higher up)
            if (layout.Level.Count > 0)
                AppendLayoutBlock(body, layout.Level, p, d,
                    vo - Mathf.RoundToInt(Mathf.Clamp(MaxOffsetOf(layout.Level), 0, 400) * m));

            // ② v1.8.4 击杀确认块(布局里写了 kill| 行 → 用布局内容在"击杀显示期"内渲染) / ② v1.8.4 kill-confirmation block (if the layout has kill| rows → render the layout content during the "kill notice period")
            //   显示期与旧机制一致: KillNoticeSeconds 秒(由 SettlePending 写入 _killNoticeUntil) /   The display period matches the old mechanism: KillNoticeSeconds seconds (written to _killNoticeUntil by SettlePending)
            if (layout.Kill.Count > 0 && KillNoticeAlive(d.UserId))
                AppendLayoutBlock(body, layout.Kill, p, d,
                    vo - Mathf.RoundToInt(Mathf.Clamp(MaxOffsetOf(layout.Kill), 0, 400) * m));
            else if (layout.Kill.Count == 0)
            {
                // 布局没写 kill 行 → 保持旧行为: 把"定格好的击杀块"追加到主块下方 / No kill rows in the layout → keep the old behavior: append the "frozen kill block" below the main block
                string killNotice = KillNoticeBlock(p, d);
                if (!string.IsNullOrEmpty(killNotice))
                {
                    body.AppendLine();
                    body.AppendLine(killNotice);
                }
            }

            // ③ v1.8.4 配置自定义行插入点(custom|<对齐>|<偏移>): 在布局指定位置插入 config 的 CustomLines / ③ v1.8.4 config custom-line insertion point (custom|<align>|<offset>): insert the config's CustomLines at the specified layout position
            if (layout.Custom.Count > 0 && Config.CustomLines != null && Config.CustomLines.Count > 0)
                AppendCustomBlock(body, layout.Custom, p, d,
                    vo - Mathf.RoundToInt(Mathf.Clamp(MaxOffsetOf(layout.Custom), 0, 400) * m));

            // ④ 主块: 贴在屏幕最下方(再留安全余量, 按分辨率缩放) / ④ Main block: pinned to the very bottom of the screen (with a safety margin, scaled by resolution)
            if (layout.Main.Count > 0)
                AppendLayoutBlock(body, layout.Main, p, d, vo - Mathf.RoundToInt(LayoutSafeMarginPx * m));

            return HudTags(p, d, body.ToString(), includeVOffset: false);
        }

        /// <summary>v1.8.4 布局里的 fold 行: 折叠模式下用布局指定的一行/几行(代替 HudCollapsedLine)。 / v1.8.4 fold rows in a layout: use the layout's specified line(s) in collapsed mode (instead of HudCollapsedLine).</summary>
        private string BuildFoldedFromLayout(Player p, PlayerData d, HudLayoutDef layout)
        {
            var body = new StringBuilder();
            float m = Mathf.Max(0.1f, GetResolutionMultiplier(p, d));
            int vo = Config.HudUseTags ? Mathf.Clamp(Mathf.RoundToInt(GetVOffset(d) * m), -320, 320) : 0;
            if (layout.Fold.Count > 0)
                AppendLayoutBlock(body, layout.Fold, p, d, vo - Mathf.RoundToInt(LayoutSafeMarginPx * m));
            // 击杀块照旧追加(折叠时击杀确认不能丢) / Append the kill block as before (kill confirmation must not be lost when collapsed)
            string killNotice = KillNoticeBlock(p, d);
            if (!string.IsNullOrEmpty(killNotice))
            {
                body.AppendLine();
                body.AppendLine(killNotice);
            }
            return HudTags(p, d, body.ToString(), includeVOffset: false);
        }

        /// <summary>布局自定义行插入点: 渲染 config 的 CustomLines 全部行(组内同一对齐/偏移)。 / Layout custom-line insertion point: render all CustomLines from config (same alignment/offset within the group).</summary>
        private void AppendCustomBlock(StringBuilder sb, List<HudLayoutRow> rows, Player p, PlayerData d, int voffset)
        {
            if (rows == null || rows.Count == 0 || Config.CustomLines == null || Config.CustomLines.Count == 0) return;
            HudLayoutRow anchor = rows.OrderByDescending(x => x.Offset).First();
            bool wrap = Config.HudUseTags && voffset != 0;
            if (wrap) sb.Append($"<voffset={voffset}>");
            foreach (string line in Config.CustomLines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                sb.Append($"<align={AlignTag(anchor.Align)}>");
                sb.Append(Tok(line, p, d));
                sb.AppendLine("</align>");
            }
            if (wrap) sb.Append("</voffset>");
        }

        /// <summary>该玩家是否仍处于击杀显示期(_killNoticeUntil 未过期)。 / Whether the player is still in the kill notice period (_killNoticeUntil has not expired).</summary>
        private bool KillNoticeAlive(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            if (!_killNoticeUntil.TryGetValue(userId, out DateTime until)) return false;
            if (DateTime.UtcNow >= until)
            {
                _killNoticeUntil.Remove(userId);
                _killNoticeText.Remove(userId);
                return false;
            }
            return true;
        }

        private static int MaxOffsetOf(List<HudLayoutRow> rows)
        {
            int m = int.MinValue;
            foreach (HudLayoutRow r in rows) if (r.Offset > m) m = r.Offset;
            return m == int.MinValue ? 0 : m;
        }

        /// <summary>
        /// 输出一块(主块或等级块): 每排一行, 自带 &lt;align&gt;; 排的先后 = 偏移从大到小(数值越大越靠上)。 / Output one block (main or level): one row per line with its own &lt;align&gt;; row order = descending offset (larger value = higher up).
        /// 整块用一个 &lt;voffset&gt; 包起来做竖直定位。 / The whole block is wrapped in one &lt;voffset&gt; for vertical positioning.
        /// </summary>
        private void AppendLayoutBlock(StringBuilder sb, List<HudLayoutRow> rows, Player p, PlayerData d, int voffset)
        {
            bool wrap = Config.HudUseTags && voffset != 0;
            if (wrap) sb.Append($"<voffset={voffset}>");
            foreach (HudLayoutRow r in rows.OrderByDescending(x => x.Offset))
            {
                sb.Append($"<align={AlignTag(r.Align)}>");
                sb.Append(Tok(r.Text, p, d));
                sb.AppendLine("</align>");
            }
            if (wrap) sb.Append("</voffset>");
        }

        // ========================================================================
        // 管理子指令 —— EXPAS 的管理命令也是「exp 的子指令」 / Admin subcommands — EXPAS admin commands are also "subcommands of exp"
        //   控制台/RA 形式:  exp <子命令> ...        例: exp time GMT+9 /   Console/RA form:  exp <subcommand> ...        e.g. exp time GMT+9
        //   兼容别名形式:    expadmin <子命令> ...    (等价) /   Compatible alias form:    expadmin <subcommand> ...    (equivalent)
        // ========================================================================
        public bool HandleAdminSub(string sub, ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            response = string.Empty;
            int n = arguments.Count;

            // 注: 玩家解析统一走 ResolvePlayer(支持 序列ID / 玩家名 / Steam64, 受配置开关控制), / Note: player resolution always goes through ResolvePlayer (supports sequence ID / player name / Steam64, controlled by config switches),
            //     这里不再预解析玩家(旧代码有一段无人使用的预解析, 已删)。 /     no pre-resolution of players here anymore (the old code had an unused pre-resolution block, now removed).

            string rawVal = null;
            if (n >= 3)
            {
                var parts = new List<string>();
                for (int i = 2; i < n; i++) parts.Add(arguments.At(i));
                rawVal = string.Join(" ", parts);
            }

            try
            {
                switch (sub)
                {
                    // ============ 8.2 字段式:  EXPAS <set|add> <玩家ID> <字段> <值> ============ / ============ 8.2 Field form: EXPAS <set|add> <player ID> <field> <value> ============
                    case "set":
                    case "add":
                        return HandleSetAdd(sub == "add", arguments, out response);

                    // ============ 8.3 等级控制子指令 ============ / ============ 8.3 Level control subcommands ============
                    case "addlevel":
                        {
                            if (arguments.Count < 3) { response = L.T("用法: EXPAS addlevel <玩家ID> <数量>", "Usage: EXPAS addlevel <player ID> <amount>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out Player on, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            int deltaLv = Mathf.RoundToInt(ParseFloat(arguments.At(2)));
                            d.Level = Mathf.Clamp(d.Level + deltaLv, 1, Config.MaxLevel);
                            SavePlayer(d);
                            if (on != null) UpdateName(on, d);
                            response = L.T($"{d.Name} 等级 {(deltaLv >= 0 ? "+" : "")}{deltaLv} → Lv.{d.Level}", $"{d.Name} level {(deltaLv >= 0 ? "+" : "")}{deltaLv} → Lv.{d.Level}");
                            return true;
                        }
                    case "restartlevel":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: EXPAS restartlevel <玩家ID>", "Usage: EXPAS restartlevel <player ID>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out Player on, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            d.Level = 1;
                            d.Exp = 0f;
                            d.PendingExp = 0f;
                            SavePlayer(d);
                            if (on != null) UpdateName(on, d);
                            response = L.T($"已重置 {d.Name} 的等级: Lv.1 (当前经验清零, 累计经验 {d.TotalExp:F0} 保留)", $"Reset {d.Name}'s level: Lv.1 (current EXP cleared, total EXP {d.TotalExp:F0} kept)");
                            return true;
                        }
                    case "xpfreeze":
                    case "freezexp":
                        {
                            bool? want = null;
                            if (arguments.Count >= 2)
                            {
                                string a = arguments.At(1).ToLowerInvariant();
                                if (a == "on" || a == "1" || a == "开") want = true;
                                else if (a == "off" || a == "0" || a == "关") want = false;
                            }
                            _xpFrozen = want ?? !_xpFrozen;
                            response = _xpFrozen
                                ? L.T("已冻结本回合经验增长(本回合不再获得任何经验, 下一回合自动恢复)", "EXP growth frozen for this round (no EXP will be gained this round; it resumes next round)")
                                : L.T("已解除经验冻结(经验增长恢复正常)", "EXP freeze lifted (EXP growth is back to normal)");
                            Logger.Info(L.T($"[EXPAS] 经验冻结 = {_xpFrozen}", $"[EXPAS] EXP freeze = {_xpFrozen}"));
                            return true;
                        }

                    // ---------------- 玩家数据(支持昵称/RA玩家ID/Steam64/完整UserId, 离线也能改) ---------------- / Player data (supports nickname/RA player ID/Steam64/full UserId; offline edits possible) ----------------
                    case "info":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: EXPAS info <玩家ID>", "Usage: EXPAS info <player ID>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out Player on, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            response =
                                L.T($"{d.Name}({uid}) {(on != null ? "在线" : "离线")}\n", $"{d.Name}({uid}) {(on != null ? "Online" : "Offline")}\n") +
                                L.T($"等级 Lv.{d.Level}  经验 {d.Exp:F0}/{ExpasPlugin.ExpNeeded(d.Level):F0}  累计经验 {d.TotalExp:F0}\n",
                                    $"Level Lv.{d.Level}  EXP {d.Exp:F0}/{ExpasPlugin.ExpNeeded(d.Level):F0}  Total EXP {d.TotalExp:F0}\n") +
                                L.T($"总伤害 {d.TotalDamage:F0}  总击杀 {d.Kills}  死亡 {d.Deaths}  积分 {d.Points:F0}\n",
                                    $"Total Damage {d.TotalDamage:F0}  Total Kills {d.Kills}  Deaths {d.Deaths}  Points {d.Points:F0}\n") +
                                L.T($"游玩天数 {d.PlayedDays}  今日游玩 {GetTodayPlaySeconds(d):F0} 秒  总游玩 {PlaytimeText(d.PlaySeconds)}\n",
                                    $"Played Days {d.PlayedDays}  Today {GetTodayPlaySeconds(d):F0} s  Total playtime {PlaytimeText(d.PlaySeconds)}\n") +
                                L.T($"锁定 {LockName(d.LockState)}{LockIcon(d.LockState)}  待入账经验 {d.PendingExp:F0}\n",
                                    $"Lock {LockName(d.LockState)}{LockIcon(d.LockState)}  Pending EXP {d.PendingExp:F0}\n") +
                                L.T($"经验冻结(本回合) {(_xpFrozen ? "是" : "否")}", $"EXP freeze (this round) {(_xpFrozen ? "Yes" : "No")}");
                            return true;
                        }
                    case "reset":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: EXPAS reset <玩家ID>", "Usage: EXPAS reset <player ID>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out Player on, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            ResetData(d);
                            if (on != null) UpdateName(on, d);
                            response = L.T($"已重置 {d.Name} 的全部数据", $"Reset all data for {d.Name}");
                            return true;
                        }
                    case "lock":
                        {
                            if (arguments.Count < 2)
                            { response = L.T("用法: EXPAS lock <玩家ID> <none|punishment|selfban|inactive|0-3>", "Usage: EXPAS lock <player ID> <none|punishment|selfban|inactive|0-3>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out _, out _, out string rerr))
                            { response = rerr; return false; }
                            int mode = LockSelfBan;   // 不带取值 → 默认 selfban(与旧 lock 的"自愿锁定"语义一致) / No value given → default selfban (matches the old lock's "voluntary lock" semantics)
                            if (arguments.Count >= 3 && !ParseLockMode(arguments.At(2), out mode))
                            { response = L.T("lock 取值: 0/none, 1/punishment, 2/selfban, 3/inactive", "lock values: 0/none, 1/punishment, 2/selfban, 3/inactive"); return false; }
                            PlayerData d = GetDataById(uid);
                            SetLock(d, mode, L.T("管理指令", "Admin command"));
                            response = L.T($"{d.Name} 锁定状态 → {LockName(mode)} {LockIcon(mode)}", $"{d.Name} lock state → {LockName(mode)} {LockIcon(mode)}");
                            return true;
                        }
                    case "unlock":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: EXPAS unlock <玩家ID>", "Usage: EXPAS unlock <player ID>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out _, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            SetLock(d, LockNone, L.T("管理指令", "Admin command"));
                            response = L.T($"{d.Name} 已解锁 ✅ (selfban 期间的数据已一次性入账)", $"{d.Name} unlocked ✅ (self-ban period data has been credited at once)");
                            return true;
                        }
                    case "full":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: EXPAS full <玩家ID>", "Usage: EXPAS full <player ID>"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out _, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            SetLock(d, LockPunishment, L.T("管理指令", "Admin command"));
                            response = L.T($"{d.Name} 已进入惩罚锁定 🚫 (旧 full 语义 = punishment)", $"{d.Name} is now punishment-locked 🚫 (legacy full = punishment)");
                            return true;
                        }

                    // ---------------- v2.0 称号系统(三层) ---------------- / v2.0 title system (three layers) ----------------
                    case "title":
                        {
                            // EXPAS title <玩家ID> <plan> [文本] / EXPAS title <player ID> <plan> [text]
                            // plan: 0/off 不干预  1/ifempty 无称号才设(层1)  2/force 强制替换(层2)  3/clear 彻底清除(层3) / plan: 0/off do nothing  1/ifempty set only if empty (layer 1)  2/force force replace (layer 2)  3/clear clear completely (layer 3)
                            if (arguments.Count < 3)
                            { response = L.T("用法: EXPAS title <玩家ID> <0|1|2|3> [文本]\n0=不干预 1=无称号才设(层1) 2=强制替换(层2) 3=彻底清除(层3)", "Usage: EXPAS title <player ID> <0|1|2|3> [text]\n0=do nothing 1=set only if empty (layer 1) 2=force replace (layer 2) 3=clear completely (layer 3)"); return false; }
                            if (!ResolvePlayer(arguments.At(1), out string uid, out Player on, out _, out string rerr))
                            { response = rerr; return false; }
                            PlayerData d = GetDataById(uid);
                            int plan = TitleIfEmpty;
                            if (arguments.At(2).ToLowerInvariant() == "clear") plan = TitleClear;
                            else if (arguments.At(2) == "3" || arguments.At(2).ToLowerInvariant() == "off") plan = TitleClear;
                            else if (arguments.At(2) == "2" || arguments.At(2).ToLowerInvariant() == "force") plan = TitleForce;
                            else if (arguments.At(2) == "1" || arguments.At(2).ToLowerInvariant() == "ifempty") plan = TitleIfEmpty;
                            else if (arguments.At(2) == "0" || arguments.At(2).ToLowerInvariant() == "none") { plan = TitleOff; }

                            if (plan != TitleClear)
                            {
                                var parts = new List<string>();
                                for (int i = 3; i < arguments.Count; i++) parts.Add(arguments.At(i));
                                d.TitleText = string.Join(" ", parts).Trim();
                            }
                            d.TitlePlan = plan;
                            SavePlayer(d);
                            if (on != null) ApplyTitle(on, d);
                            response = L.T($"{d.Name}({uid}) 称号策略 → {TitlePlanName(plan)}", $"{d.Name}({uid}) title plan → {TitlePlanName(plan)}") +
                                       (plan != TitleClear && d.TitleText.Length > 0 ? L.T($"  文本: {d.TitleText}", $"  Text: {d.TitleText}") : string.Empty) +
                                       (plan == TitleClear ? L.T($"  (已清空 {d.Name} 的 CustomInfo)", $"  (Cleared {d.Name}'s CustomInfo)") : string.Empty);
                            return true;
                        }

                    // ---------------- v2.0 音频播放 ---------------- / v2.0 audio playback ----------------
                    case "audio":
                        {
                            // EXPAS audio <play 文件|list|stop|call> / EXPAS audio <play file|list|stop|call>
                            string a1 = arguments.Count >= 2 ? arguments.At(1).ToLowerInvariant() : "";
                            if (a1 == "list")
                            {
                                var files = ListAudioFiles();
                                response = (files.Count == 0
                                    ? L.T("audio 目录没有音频: ", "No audio in the audio directory: ") + AudioDir + L.T("\n支持格式: ", "\nSupported formats: ") + string.Join(" ", AudioExtensions)
                                    : L.T($"可用音频({files.Count}, 支持 {string.Join("/", AudioExtensions.Select(x => x.TrimStart('.')))}): ", $"Available audio ({files.Count}, supports {string.Join("/", AudioExtensions.Select(x => x.TrimStart('.')))}): ") + string.Join(", ", files))
                                    + ((NativeCodecAvailable || FfmpegAvailable) ? string.Empty : L.T("   ⚠ 无解码器 → 目前只有 WAV 能播", "   ⚠ No decoder → only WAV can play for now"))
                                    + L.T("   解码器: ", "   Decoder: ") + DecoderStatusText();
                                return true;
                            }
                            if (a1 == "stop")
                            {
                                StopAudio();
                                response = L.T("已停止音频播放", "Audio playback stopped");
                                return true;
                            }
                            if (a1 == "call")
                            {
                                response = AudioCallsSummary();
                                return true;
                            }
                            if (a1 == "play")
                            {
                                if (arguments.Count < 3)
                                { response = L.T("用法: EXPAS audio play <文件名>(支持 ", "Usage: EXPAS audio play <file name> (supports ") + string.Join("/", AudioExtensions.Select(x => x.TrimStart('.'))) + L.T("; 文件放 ", "; put files in ") + AudioDir + ")"; return false; }
                                var parts2 = new List<string>();
                                for (int i = 2; i < arguments.Count; i++) parts2.Add(arguments.At(i));
                                string file = string.Join(" ", parts2).Trim();
                                // 没写扩展名就按支持的格式找一遍 / If no extension is given, try all supported formats
                                string full = Path.Combine(AudioDir, file);
                                if (!File.Exists(full) && Path.GetExtension(file).Length == 0)
                                {
                                    foreach (string e in AudioExtensions)
                                    {
                                        string cand = Path.Combine(AudioDir, file + e);
                                        if (File.Exists(cand)) { full = cand; break; }
                                    }
                                }
                                bool ok = File.Exists(full) && PlayAudioPath(full);
                                response = ok ? L.T("▶ 已受理播放: ", "▶ Playback accepted: ") + Path.GetFileName(full) : L.T("播放失败(文件不存在或不支持): ", "Playback failed (file missing or unsupported): ") + file;
                                return true;
                            }
                            response = L.T("用法: EXPAS audio play <文件名> | list | stop | call\n", "Usage: EXPAS audio play <file name> | list | stop | call\n") +
                                       L.T("支持格式: ", "Supported formats: ") + string.Join(" ", AudioExtensions) +
                                       L.T("\n当前方案: ", "\nCurrent plan: ") + (Config.AudioPlaybackPlan == AudioPlanSpeaker ? L.T("隐藏音箱(不生成假人)", "Hidden Speaker (no dummy spawned)") : L.T("关闭", "Off")) +
                                       L.T("\n解码器: ", "\nDecoder: ") + DecoderStatusText();
                            return false;
                        }

                    // ---------------- v2.0 第一名音乐盒 ---------------- / v2.0 MVP music box ----------------
                    case "mvpmusic":
                    case "mvp_music":
                        {
                            string a1 = arguments.Count >= 2 ? arguments.At(1).ToLowerInvariant() : "";
                            if (a1 == "list" || a1 == "")
                            {
                                var idx = LoadMvpMusicIndex();
                                var sb = new StringBuilder(
                                    L.T($"第一名音乐盒(开关 {(Config.MvpMusicEnabled ? "开" : "关")})  目录: {MvpMusicDir}", $"MVP Music ({(Config.MvpMusicEnabled ? "On" : "Off")})  Directory: {MvpMusicDir}"));
                                sb.Append(L.T("\n来源: 该目录下的 *.js 文件(每个 JS 声明 owner + audio)", "\nSource: *.js files in this directory (each JS declares owner + audio)"));
                                if (idx.Count == 0)
                                {
                                    sb.Append(L.T("\n(目录里没有可用的音乐盒 JS —— 放一个 <Steam64>.js 进去即可)", "\n(No usable MVP music JS in the directory — just drop a <Steam64>.js in)"));
                                }
                                else
                                {
                                    foreach (var kv in idx.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                                    {
                                        var e = kv.Value;
                                        sb.Append(L.T($"\n- {e.Owner} → {e.File}  [{(e.Exists ? "文件存在" : "文件缺失")}]  ← {Path.GetFileName(e.Source)}", $"\n- {e.Owner} → {e.File}  [{(e.Exists ? "File exists" : "File missing")}]  ← {Path.GetFileName(e.Source)}"));
                                    }
                                }
                                // 顺带列出目录里存在、但没有任何 JS 引用的音频 / Also list audio files present in the directory but not referenced by any JS
                                try
                                {
                                    var used = new HashSet<string>(idx.Values.Select(v => Path.GetFullPath(v.Path)), StringComparer.OrdinalIgnoreCase);
                                    var orphan = Directory.GetFiles(MvpMusicDir, "*.wav")
                                        .Where(f => !used.Contains(Path.GetFullPath(f)))
                                        .Select(Path.GetFileName).ToList();
                                    if (orphan.Count > 0)
                                        sb.Append(L.T("\n未被 JS 引用的音频: ", "\nAudio not referenced by any JS: ") + string.Join(", ", orphan));
                                }
                                catch { }
                                response = sb.ToString();
                                return true;
                            }
                            if (a1 == "play")
                            {
                                if (arguments.Count < 3)
                                { response = L.T("用法: EXPAS mvpmusic play <玩家ID>(按其 Steam64 检索 mvp_music\\*.js 播放)", "Usage: EXPAS mvpmusic play <player ID> (looks up mvp_music\\*.js by their Steam64 and plays it)"); return false; }
                                if (!ResolvePlayer(arguments.At(2), out string uid, out _, out _, out string rerr))
                                { response = rerr; return false; }
                                bool ok = PlayMvpMusicFor(uid);
                                response = ok ? L.T($"▶ 已播放 {CanonUserId(uid)} 的第一名音乐盒", $"▶ Playing MVP Music for {CanonUserId(uid)}") : L.T($"{CanonUserId(uid)} 未命中任何音乐盒 JS 或音频缺失", $"No MVP music JS matched {CanonUserId(uid)}, or the audio file is missing");
                                return true;
                            }
                            response = L.T("用法: EXPAS mvpmusic list | EXPAS mvpmusic play <玩家ID>\n", "Usage: EXPAS mvpmusic list | EXPAS mvpmusic play <player ID>\n") +
                                       L.T("回合结束自动触发: 伤害第一名命中 mvp_music_players 即播放其专属音频。", "Auto-triggered at round end: the top-damage player listed in mvp_music_players gets their own audio played.");
                            return false;
                        }

                    // ---------------- 配置 / 参数 ---------------- / Config / parameters ----------------
                    case "config":
                        response =
                            $"ServerName={Config.ServerName} | TimeZone={Config.TimeZone}({GetTimeZoneLabel()}) | " +
                            $"DamageToExp={Config.DamageToExp} | BaseExp={Config.BaseExp} | Growth={Config.ExpGrowth} | MaxLevel={Config.MaxLevel}\n" +
                            $"OfflineDays={Config.OfflineDays} | HudInterval={Config.HudInterval} | PlaytimeMode={Config.PlaytimeMode} | " +
                            $"DaysMode={Config.DaysMode} | ApiEnabled={Config.ApiEnabled} | TopCount={Config.TopCount}\n" +
                            L.T($"仅算真人={Config.OnlyRealPlayers} 含SCP={Config.CountScp} 含人机={Config.CountDummies} 死亡时结算={Config.SettleOnDeath} | ", $"Real players only={Config.OnlyRealPlayers} Include SCP={Config.CountScp} Include dummies={Config.CountDummies} Settle on death={Config.SettleOnDeath} | ") +
                            L.T($"CustomLines={Config.CustomLines?.Count ?? 0}行 | MvpAnnounce={(string.IsNullOrEmpty(Config.MvpAnnounce) ? "(空)" : Config.MvpAnnounce)}", $"CustomLines={Config.CustomLines?.Count ?? 0} lines | MvpAnnounce={(string.IsNullOrEmpty(Config.MvpAnnounce) ? "(empty)" : Config.MvpAnnounce)}");
                        return true;

                    case "setcfg":
                        {
                            if (rawVal == null || arguments.Count < 3) { response = L.T("用法: exp setcfg <键> <值>", "Usage: exp setcfg <key> <value>"); return false; }
                            string key = arguments.At(1).ToLowerInvariant();
                            return SetConfigValue(key, rawVal, out response);
                        }
                    case "time":
                        {
                            if (rawVal == null) { response = L.T("用法: exp time <GMT+9|日本|+8|8>", "Usage: exp time <GMT+9|Japan|+8|8>"); return false; }
                            Config.TimeZone = rawVal.Trim();
                            SaveConfig(); LocalizeConfigFile();
                            response = L.T($"时区已改为 {Config.TimeZone} → 当前显示 {GetTimeZoneLabel()} {GetLocalTimeText()} (已写入配置文件)", $"Time zone changed to {Config.TimeZone} → now showing {GetTimeZoneLabel()} {GetLocalTimeText()} (written to config file)");
                            return true;
                        }
                    case "line":
                        {
                            if (arguments.Count < 3) { response = L.T("用法: exp line <1-4|level1|level2> <文本>", "Usage: exp line <1-4|level1|level2> <text>"); return false; }
                            string which = arguments.At(1).ToLowerInvariant();
                            return SetLineText(which, rawVal, out response);
                        }
                    case "custom":
                        {
                            if (arguments.Count < 2) { response = L.T("用法: exp custom <add 文本|clear|list>", "Usage: exp custom <add text|clear|list>"); return false; }
                            string act = arguments.At(1).ToLowerInvariant();
                            if (Config.CustomLines == null) Config.CustomLines = new List<string>();
                            if (act == "add")
                            {
                                if (string.IsNullOrEmpty(rawVal)) { response = L.T("用法: exp custom add <文本>", "Usage: exp custom add <text>"); return false; }
                                Config.CustomLines.Add(rawVal);
                                SaveConfig(); LocalizeConfigFile();
                                response = L.T($"已添加自定义行({Config.CustomLines.Count} 行), 已写入配置文件", $"Added custom line ({Config.CustomLines.Count} lines), written to config file");
                                return true;
                            }
                            if (act == "clear")
                            {
                                Config.CustomLines.Clear();
                                SaveConfig(); LocalizeConfigFile();
                                response = L.T("已清空自定义行", "Custom lines cleared");
                                return true;
                            }
                            response = Config.CustomLines.Count == 0
                                ? L.T("当前没有自定义行", "No custom lines currently")
                                : L.T("自定义行: ", "Custom lines: ") + string.Join(" / ", Config.CustomLines);
                            return true;
                        }
                    case "mvp":
                        {
                            Config.MvpAnnounce = rawVal ?? string.Empty;
                            SaveConfig(); LocalizeConfigFile();
                            response = string.IsNullOrEmpty(Config.MvpAnnounce)
                                ? L.T("已关闭 MVP 公告", "MVP announcement disabled")
                                : L.T($"MVP 公告已设为: {Config.MvpAnnounce}", $"MVP announcement set to: {Config.MvpAnnounce}");
                            return true;
                        }
                    case "top":
                        {
                            List<RoundEntry> top = BuildRoundTop(Config.TopCount);
                            if (top.Count == 0) { response = L.T("本回合还没有数据", "No data for this round yet"); return true; }
                            var sb = new StringBuilder(L.T("本回合排行榜: ", "Round leaderboard: "));
                            for (int i = 0; i < top.Count; i++)
                                sb.Append(L.T($"\n{i + 1}. {top[i].Name}  伤害{top[i].Damage:F0}  经验{top[i].Exp:F0}  击杀{top[i].Kills}  Lv.{top[i].Level}", $"\n{i + 1}. {top[i].Name}  Damage {top[i].Damage:F0}  EXP {top[i].Exp:F0}  Kills {top[i].Kills}  Lv.{top[i].Level}"));
                            response = sb.ToString();
                            return true;
                        }
                    case "api":
                        {
                            Config.ApiEnabled = rawVal == null || ParseFloat(rawVal) != 0f;
                            SaveConfig(); LocalizeConfigFile();
                            response = L.T($"本回合 API 已{(Config.ApiEnabled ? "开放" : "关闭")} (ExpasApi.Enabled={ExpasApi.Enabled})", $"API is {(Config.ApiEnabled ? "open" : "closed")} this round (ExpasApi.Enabled={ExpasApi.Enabled})");
                            return true;
                        }
                    case "save":
                        FlushAll(L.T("管理指令", "Admin command"));
                        SaveConfig(); LocalizeConfigFile();
                        response = L.T("已保存玩家数据与配置文件", "Player data and config saved");
                        return true;

                    // ---------------- v2.0 arsa: 重新识别"除自身以外"的全部插件 ---------------- / v2.0 arsa: re-detect all plugins "other than itself" ----------------
                    //   EXPAS arsa       重新读取文件(重扫插件目录, 载入新增 DLL) + 重读配置文件(properties.yml/config.yml) /   EXPAS arsa       re-read files (rescan the plugin directory, load new DLLs) + re-read config files (properties.yml/config.yml)
                    //   EXPAS arsa full  在上面基础上再对其它插件执行 Disable→Enable(完整重跑, 慎用) /   EXPAS arsa full  additionally Disable→Enable other plugins on top of the above (full restart; use with caution)
                    case "arsa":
                        {
                            bool full = arguments.Count >= 2 &&
                                        (arguments.At(1).ToLowerInvariant() == "full" || arguments.At(1) == "1");
                            response = ArsaReloadOthers(full);
                            return true;
                        }

                    // ---------------- v1.7 布局 / 白名单 ---------------- / v1.7 layouts / whitelist ----------------
                    case "layout":
                        return HandleLayoutSub(arguments, out response);
                    case "whitelist":
                    case "wl":
                        return HandleWhitelistSub(arguments, out response);

                    // ---------------- 帮助 / 字段匹配 ---------------- / Help / field matching ----------------
                    case "help":
                    case "?":
                        {
                            string kw = arguments.Count >= 2 ? arguments.At(1) : string.Empty;
                            response = ExpasHelpText(kw);
                            return true;
                        }

                    default:
                        // 未知子命令 → 当作关键字做"字段匹配"(如 EXPAS se → set/setcfg/save) / Unknown subcommand → treat it as a keyword for "field matching" (e.g. EXPAS se → set/setcfg/save)
                        // 而不是丢一屏"未知子命令"长文本; 完全没匹配时才给总览提示。 / instead of dumping a screenful of "unknown subcommand" text; only show the overview when nothing matches.
                        response = ExpasHelpText(sub);
                        return false;
                }
            }
            catch (Exception ex)
            {
                response = L.T("执行失败: ", "Execution failed: ") + ex.Message;
                return false;
            }
        }

        // ========================================================================
        // v1.7 布局 / 白名单 子指令 / v1.7 layout / whitelist subcommands
        //   EXPAS layout list                     列出全部布局 /   EXPAS layout list                     list all layouts
        //   EXPAS layout reload                   重新扫描 layouts\ 目录(并重发 SSS 菜单) /   EXPAS layout reload                   rescan the layouts\ directory (and resend the SSS menu)
        //   EXPAS layout info <玩家ID>            看某玩家当前"存储的/实际生效的"布局 /   EXPAS layout info <player ID>            show a player's currently "stored/effective" layout
        //   EXPAS layout <玩家ID> <布局名>         给某玩家指定布局(写进他的 .dat, 按 Steam64 记住) /   EXPAS layout <player ID> <layout name>         assign a layout to a player (written to their .dat, remembered by Steam64)
        //   EXPAS whitelist add|remove|list [Steam64]   自定义 UI 白名单(写回 config.yml) /   EXPAS whitelist add|remove|list [Steam64]   Custom UI whitelist (written back to config.yml)
        // ========================================================================

        /// <summary>白名单的可读文本(回执用)。 / Human-readable whitelist text (for responses).</summary>
        private string WhitelistText()
        {
            List<string> l = Config != null ? Config.CustomUiWhitelist : null;
            if (l == null || l.Count == 0) return L.T("(空 —— 所有人只看到 default 布局)", "(empty — everyone only sees the default layout)");
            return string.Join(", ", l);
        }

        private bool HandleLayoutSub(ArraySegment<string> args, out string response)
        {
            response = string.Empty;
            int n = args.Count;
            if (n < 2) { response = L.T("用法: EXPAS layout list | reload | info <玩家ID> | <玩家ID> <布局名>", "Usage: EXPAS layout list | reload | info <player ID> | <player ID> <layout name>"); return false; }
            string a1 = args.At(1).ToLowerInvariant();

            if (a1 == "list")
            {
                var sb = new StringBuilder(L.T($"已载入 {_layouts.Count} 套布局(目录 {LayoutDir}):", $"{_layouts.Count} layouts loaded (directory {LayoutDir}):"));
                foreach (var kv in _layouts.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                    sb.Append(L.T($"\n- {kv.Key}  [{kv.Value.DisplayName}]  主块 {kv.Value.Main.Count} 排 / 等级块 {kv.Value.Level.Count} 排", $"\n- {kv.Key}  [{kv.Value.DisplayName}]  main {kv.Value.Main.Count} rows / level {kv.Value.Level.Count} rows") +
                              (kv.Value.FromFile ? L.T("  (来自文件)", "  (from file)") : L.T("  (内置默认)", "  (built-in default)")));
                sb.Append(L.T("\n白名单(CustomUiWhitelist): ", "\nWhitelist (CustomUiWhitelist): ") + WhitelistText());
                response = sb.ToString();
                return true;
            }
            if (a1 == "reload")
            {
                LoadLayouts();
                RebuildSssMenus(true);
                response = L.T($"已重新扫描布局: {string.Join(", ", _layouts.Keys)}", $"Layouts rescanned: {string.Join(", ", _layouts.Keys)}");
                return true;
            }
            if (a1 == "info")
            {
                if (n < 3) { response = L.T("用法: EXPAS layout info <玩家ID>", "Usage: EXPAS layout info <player ID>"); return false; }
                if (!ResolvePlayer(args.At(2), out string iUid, out Player iOn, out _, out string irerr))
                { response = irerr; return false; }
                PlayerData di = GetDataById(iUid);
                string[] avail = LayoutKeysForPlayer(di);
                response =
                    L.T($"{di.Name}({iUid}) {(iOn != null ? "在线" : "离线")}\n", $"{di.Name}({iUid}) {(iOn != null ? "Online" : "Offline")}\n") +
                    L.T($"自定义 UI 选项: {(avail.Length > 1 ? string.Join(" / ", avail) : "(无 —— 面板里整项不显示, 固定用默认布局)")}\n", $"Custom UI options: {(avail.Length > 1 ? string.Join(" / ", avail) : "(none — the item is hidden in the panel; the default layout is always used)")}\n") +
                    L.T($"存储的布局: {(string.IsNullOrEmpty(di.HudLayout) ? "default" : di.HudLayout)}\n", $"Stored layout: {(string.IsNullOrEmpty(di.HudLayout) ? "default" : di.HudLayout)}\n") +
                    L.T($"实际生效: {GetLayoutKey(di)}", $"Effective: {GetLayoutKey(di)}");
                return true;
            }

            // EXPAS layout <玩家ID> <布局名> / EXPAS layout <player ID> <layout name>
            if (n < 3) { response = L.T("用法: EXPAS layout <玩家ID> <布局名>(布局名用 EXPAS layout list 查)", "Usage: EXPAS layout <player ID> <layout name> (use EXPAS layout list to find layout names)"); return false; }
            if (!ResolvePlayer(args.At(1), out string uid, out Player online, out _, out string lerr))
            { response = lerr; return false; }
            string key = args.At(2).Trim();
            HudLayoutDef def;
            if (!_layouts.TryGetValue(key, out def) || def == null)
            { response = L.T($"没有这个布局: {key}\n可用: {string.Join(", ", _layouts.Keys)}", $"No such layout: {key}\nAvailable: {string.Join(", ", _layouts.Keys)}"); return false; }

            PlayerData d = GetDataById(uid);
            if (d == null) { response = L.T("无法创建/读取该玩家数据", "Cannot create/read this player's data"); return false; }
            d.HudLayout = def.Key;
            SavePlayer(d);

            // v1.8: 生效条件 = 布局归属匹配或白名单; 否则存了也不生效(GetLayoutKey 回落 default) / v1.8: effective condition = layout ownership match or whitelist; otherwise it is stored but not applied (GetLayoutKey falls back to default)
            bool usable = CanUseLayout(d, def.Key);
            if (online != null)
            {
                UpdateName(online, d);
                if (usable)
                {
                    SendSssMenuTo(online);   // 让他面板里的下拉立刻可用 / Make the dropdown in their panel usable immediately
                    if (Config.HudSurface != 0 && !IsHudOff(online.UserId))
                        online.SendHint(BuildHud(online, d), Mathf.Max(Config.HudClear, Config.HudInterval + 0.3f));
                }
            }
            response = L.T($"{d.Name}({uid}) 布局 → {def.Key}  [{def.DisplayName}]", $"{d.Name}({uid}) layout → {def.Key}  [{def.DisplayName}]") +
                       (usable ? string.Empty
                            : L.T($"\n注意: 该布局的 owner Steam64 不匹配 {uid}, 也非白名单 —— 自定义布局**不会生效**, ", $"\nNote: this layout's owner Steam64 does not match {uid} and the player is not whitelisted — the custom layout will NOT take effect, ") +
                              L.T($"会按默认布局渲染。请确认布局文件首行(# owner= / // owner= / const owner=)或文件名前缀是他本人的 Steam64。", $"the default layout will be used instead. Make sure the first line (# owner= / // owner= / const owner=) or the file name prefix is the player's own Steam64."));
            return true;
        }

        private bool HandleWhitelistSub(ArraySegment<string> args, out string response)
        {
            response = string.Empty;
            int n = args.Count;
            if (Config.CustomUiWhitelist == null) Config.CustomUiWhitelist = new List<string>();
            if (n < 2) { response = L.T("用法: EXPAS whitelist add|remove|list [Steam64]\n(Steam64 支持 7656119…@steam 或纯数字)", "Usage: EXPAS whitelist add|remove|list [Steam64]\n(Steam64 accepts 7656119…@steam or plain digits)"); return false; }
            string a1 = args.At(1).ToLowerInvariant();

            if (a1 == "list")
            {
                response = L.T($"CustomUiWhitelist({Config.CustomUiWhitelist.Count} 项): {WhitelistText()}", $"CustomUiWhitelist ({Config.CustomUiWhitelist.Count} entries): {WhitelistText()}");
                return true;
            }
            if (a1 != "add" && a1 != "remove")
            { response = L.T("用法: EXPAS whitelist add|remove|list [Steam64]", "Usage: EXPAS whitelist add|remove|list [Steam64]"); return false; }
            if (n < 3) { response = L.T($"用法: EXPAS whitelist {a1} <Steam64>", $"Usage: EXPAS whitelist {a1} <Steam64>"); return false; }

            string canon = CanonUserId(args.At(2));
            if (canon == null) { response = L.T("Steam64 不能为空", "Steam64 cannot be empty"); return false; }

            if (a1 == "add")
            {
                foreach (string w in Config.CustomUiWhitelist)
                    if (string.Equals(CanonUserId(w), canon, StringComparison.OrdinalIgnoreCase))
                    { response = L.T($"已在白名单里: {w}", $"Already whitelisted: {w}"); return true; }
                Config.CustomUiWhitelist.Add(canon);
                SaveConfig(); LocalizeConfigFile();                      // 写回 config.yml (UTF-8) / Write back to config.yml (UTF-8)
                RebuildSssMenus(true);             // 立刻让他看到「自定义 UI」下拉 / Let them see the "Custom UI" dropdown immediately
                Player on = FindOnline(canon);
                response = L.T($"已加入白名单: {canon} (已写回 config.yml)", $"Added to whitelist: {canon} (written back to config.yml)") +
                           (on != null ? L.T($" —— 已给 {on.Nickname} 下发含「自定义 UI」的 SSS 菜单", $" — sent the SSS menu with \"Custom UI\" to {on.Nickname}") : string.Empty);
                return true;
            }

            int removed = 0;
            for (int i = Config.CustomUiWhitelist.Count - 1; i >= 0; i--)
                if (string.Equals(CanonUserId(Config.CustomUiWhitelist[i]), canon, StringComparison.OrdinalIgnoreCase))
                { Config.CustomUiWhitelist.RemoveAt(i); removed++; }
            if (removed == 0) { response = L.T($"白名单里没有: {canon}", $"Not in whitelist: {canon}"); return false; }

            SaveConfig(); LocalizeConfigFile();
            RebuildSssMenus(true);
            Player on2 = FindOnline(canon);
            if (on2 != null)
            {
                // 被移出白名单 → 实际生效布局立刻回到 default, 立刻按默认布局重刷一次 / Removed from whitelist → the effective layout immediately returns to default; refresh once with the default layout right away
                PlayerData d2 = GetDataById(canon);
                if (d2 != null && Config.HudSurface != 0 && !IsHudOff(canon))
                    on2.SendHint(BuildHud(on2, d2), Mathf.Max(Config.HudClear, Config.HudInterval + 0.3f));
            }
            response = L.T($"已从白名单移除: {canon} (已写回 config.yml; 该玩家立即回到默认布局)", $"Removed from whitelist: {canon} (written back to config.yml; the player immediately returns to the default layout)");
            return true;
        }

        // ========================================================================
        // v1.5 命令辅助 —— 字段式 set/add、玩家标识解析(离线可用) / v1.5 command helpers — field-style set/add, player identifier resolution (works offline)
        // ========================================================================

        /// <summary>
        /// 8.2 字段式: EXPAS &lt;set|add&gt; &lt;玩家ID&gt; &lt;字段&gt; &lt;值&gt;。 / 8.2 Field form: EXPAS &lt;set|add&gt; &lt;player ID&gt; &lt;field&gt; &lt;value&gt;.
        /// 兼容旧式两参写法: EXPAS add &lt;玩家&gt; &lt;经验&gt; / EXPAS set &lt;玩家&gt; &lt;等级&gt;(第 3 个 token 是纯数字时按旧语义)。 / Compatible with the legacy two-argument form: EXPAS add &lt;player&gt; &lt;EXP&gt; / EXPAS set &lt;player&gt; &lt;level&gt; (when the 3rd token is a plain number, legacy semantics apply).
        /// </summary>
        private bool HandleSetAdd(bool add, ArraySegment<string> arguments, out string response)
        {
            response = string.Empty;
            if (arguments.Count < 3)
            {
                response = L.T("用法: EXPAS ", "Usage: EXPAS ") + (add ? "add" : "set") + L.T(" <玩家ID> <字段> <值>\n", " <player ID> <field> <value>\n") +
                           L.T("字段: level / points / lock / total_damage / total_kills / played_days / today_play_time(秒) / total_play_time(秒)\n", "Fields: level / points / lock / total_damage / total_kills / played_days / today_play_time(seconds) / total_play_time(seconds)\n") +
                           "lock: 0=none 1=punishment 2=selfban 3=inactive\n" +
                           L.T("示例: EXPAS set 玩家 level 225   |   EXPAS add 玩家 points 300   |   EXPAS set 玩家 lock 1", "Example: EXPAS set <player> level 225   |   EXPAS add <player> points 300   |   EXPAS set <player> lock 1");
                return false;
            }

            string ident = arguments.At(1);
            string field;
            string val;
            if (arguments.Count == 3 && TryParseNumber(arguments.At(2)))
            {
                // 兼容旧式: set <玩家> <等级> / add <玩家> <经验> / Legacy compatibility: set <player> <level> / add <player> <EXP>
                field = add ? "exp" : "level";
                val = arguments.At(2);
            }
            else
            {
                field = arguments.At(2).Trim().ToLowerInvariant();
                var parts = new List<string>();
                for (int i = 3; i < arguments.Count; i++) parts.Add(arguments.At(i));
                val = string.Join(" ", parts).Trim();
            }
            if (string.IsNullOrEmpty(val)) { response = L.T("缺少 <值>", "Missing <value>"); return false; }

            if (!ResolvePlayer(ident, out string userId, out Player online, out _, out string serr))
            { response = serr; return false; }

            PlayerData d = GetDataById(userId);
            if (d == null) { response = L.T("无法创建/读取该玩家数据", "Cannot create/read this player's data"); return false; }

            if (!ApplyField(d, field, val, add, out response)) return false;

            if (online != null) UpdateName(online, d);
            response = L.T($"{d.Name}({userId}) {field} {(add ? "增加" : "设为")} {val} → 现在 {DescribeField(d, field)}", $"{d.Name}({userId}) {field} {(add ? "added" : "set to")} {val} → now {DescribeField(d, field)}");
            return true;
        }

        /// <summary>把字段值应用到玩家数据(并持久化); lock 走 SetLock(新锁定模型)。 / Apply a field value to player data (and persist it); lock goes through SetLock (the new lock model).</summary>
        private bool ApplyField(PlayerData d, string field, string val, bool add, out string response)
        {
            response = string.Empty;
            switch (field)
            {
                case "level":
                    {
                        int lv = Mathf.RoundToInt(ParseFloat(val));
                        d.Level = Mathf.Clamp(add ? d.Level + lv : lv, 1, Config.MaxLevel);
                        break;
                    }
                case "points":
                    {
                        float p = ParseFloat(val);
                        d.Points = Mathf.Max(0f, add ? d.Points + p : p);
                        break;
                    }
                case "total_damage":
                    {
                        float t = ParseFloat(val);
                        d.TotalDamage = Mathf.Max(0f, add ? d.TotalDamage + t : t);
                        break;
                    }
                case "total_kills":
                    {
                        int k = Mathf.RoundToInt(ParseFloat(val));
                        d.Kills = Mathf.Max(0, add ? d.Kills + k : k);
                        break;
                    }
                case "played_days":
                    {
                        int pd = Mathf.RoundToInt(ParseFloat(val));
                        d.PlayedDays = Mathf.Max(1, add ? d.PlayedDays + pd : pd);
                        break;
                    }
                case "today_play_time":
                    {
                        double sec = ParseFloat(val);
                        d.TodayPlaySeconds = Math.Max(0d, add ? d.TodayPlaySeconds + sec : sec);
                        d.TodayDate = TodayKey();     // 标记为今天, 免得显示时被跨天归零 / Mark as today, so it is not reset to zero when the day changes during display
                        break;
                    }
                case "total_play_time":
                    {
                        double sec = ParseFloat(val);
                        d.PlaySeconds = Math.Max(0d, add ? d.PlaySeconds + sec : sec);
                        break;
                    }
                case "exp":                            // 兼容旧式 add exp / set exp / Legacy compatibility: add exp / set exp
                    {
                        float e = ParseFloat(val);
                        if (add) AddExp(d, e);
                        else d.Exp = Mathf.Max(0f, e);
                        break;
                    }
                case "lock":
                    {
                        if (!ParseLockMode(val, out int mode))
                        { response = L.T("lock 取值: 0/none, 1/punishment, 2/selfban, 3/inactive", "lock values: 0/none, 1/punishment, 2/selfban, 3/inactive"); return false; }
                        SetLock(d, mode, L.T("管理指令", "Admin command"));    // SetLock 内部落盘并给提示 / SetLock saves to disk and shows a notice internally
                        return true;
                    }
                default:
                    response = L.T($"未知字段: {field}\n可用字段: level / points / lock / total_damage / total_kills / played_days / ", $"Unknown field: {field}\nAvailable fields: level / points / lock / total_damage / total_kills / played_days / ") +
                               L.T("today_play_time(秒) / total_play_time(秒)   (另支持 exp)", "today_play_time(seconds) / total_play_time(seconds)   (exp is also supported)");
                    return false;
            }
            SavePlayer(d);
            return true;
        }

        /// <summary>字段当前值的可读描述(用于回执)。 / Readable description of the field's current value (for responses).</summary>
        private string DescribeField(PlayerData d, string field)
        {
            switch (field)
            {
                case "level": return $"Lv.{d.Level}";
                case "points": return $"{d.Points:F0}";
                case "total_damage": return $"{d.TotalDamage:F0}";
                case "total_kills": return $"{d.Kills}";
                case "played_days": return L.T($"{d.PlayedDays} 天", $"{d.PlayedDays} days");
                case "today_play_time": return L.T($"{GetTodayPlaySeconds(d):F0} 秒", $"{GetTodayPlaySeconds(d):F0} s");
                case "total_play_time": return PlaytimeText(d.PlaySeconds);
                case "exp": return $"{d.Exp:F0}";
                case "lock": return $"{LockName(d.LockState)}{LockIcon(d.LockState)}";
                default: return "-";
            }
        }

        /// <summary>解析 lock 取值: 支持 0-3 与 none/punishment/selfban/inactive(及中文名)。 / Parse lock values: supports 0-3 and none/punishment/selfban/inactive (plus Chinese names).</summary>
        private static bool ParseLockMode(string v, out int mode)
        {
            mode = LockNone;
            if (string.IsNullOrEmpty(v)) return false;
            switch (v.Trim().ToLowerInvariant())
            {
                case "0": case "none": case "无": case "解锁": mode = LockNone; return true;
                case "1": case "punishment": case "惩罚": mode = LockPunishment; return true;
                case "2": case "selfban": case "自助封禁": case "自愿": mode = LockSelfBan; return true;
                case "3": case "inactive": case "长期未上线": mode = LockInactive; return true;
            }
            return false;
        }

        /// <summary>
        /// 解析玩家标识 → UserId(系统自行把"序列ID/玩家名"换算成 Steam64 再改)。 / Resolve a player identifier → UserId (the system converts "sequence ID/player name" to Steam64 itself before changing).
        /// 支持三种写法, 各自可在配置文件里开关(LookupBySeqId / LookupByName / LookupBySteam): / Three formats are supported, each toggled in the config file (LookupBySeqId / LookupByName / LookupBySteam):
        ///   ① 局内序列ID: RA 玩家列表编号(纯数字且 ≤6 位), 如 3  —— 需在线 /   ① In-game sequence ID: RA player-list number (plain digits, ≤6), e.g. 3  — requires online
        ///   ② 玩家名:     在线昵称; NameLookupContains=true 时支持只写一部分 —— 需在线 /   ② Player name:     online nickname; when NameLookupContains=true, partial names are supported — requires online
        ///   ③ Steam64 / 完整 UserId(如 76561198000000000 或 …@steam) —— 离线也能改 /   ③ Steam64 / full UserId (e.g. 76561198000000000 or …@steam) — can be changed offline
        /// error 里带明确原因(未命中 / 名字命中多人 / 三类解析都被关掉)。 / error contains a clear reason (no match / name matched multiple players / all three resolution methods disabled).
        /// </summary>
        private bool ResolvePlayer(string ident, out string userId, out Player online, out string displayName, out string error)
        {
            userId = null; online = null; displayName = null; error = null;
            if (string.IsNullOrWhiteSpace(ident)) { error = L.T("玩家标识为空", "Player identifier is empty"); return false; }
            ident = ident.Trim();

            bool allDigits = true;
            foreach (char c in ident) if (!char.IsDigit(c)) { allDigits = false; break; }

            // ① 局内序列ID(纯数字且 ≤6 位, 避免与 17 位 Steam64 混淆) / ① In-game sequence ID (plain digits, ≤6, to avoid confusion with 17-digit Steam64)
            if (Config.LookupBySeqId && allDigits && ident.Length <= 6 && int.TryParse(ident, out int seqId))
            {
                try
                {
                    if (Player.TryGet(seqId, out Player sp) && sp != null && !sp.IsDestroyed)
                    { online = sp; userId = sp.UserId; displayName = sp.Nickname; return true; }
                }
                catch { }
            }

            // ② 玩家名(17 位以上的纯数字一律当 Steam64, 不当名字) / ② Player name (plain numbers of 15+ digits are always treated as Steam64, not names)
            if (Config.LookupByName && !(allDigits && ident.Length >= 15))
            {
                List<Player> matches = FindOnlineByName(ident, Config.NameLookupContains);
                if (matches.Count == 1)
                {
                    Player p = matches[0];
                    online = p; userId = p.UserId; displayName = p.Nickname;
                    return true;
                }
                if (matches.Count > 1)
                {
                    // 命中多人 → 必须报错, 否则可能改错人 / Multiple matches → must error, otherwise the wrong player might be modified
                    error = L.T($"名字「{ident}」匹配到 {matches.Count} 个在线玩家: ", $"Name \"{ident}\" matched {matches.Count} online players: ") +
                            string.Join(", ", matches.Take(8).Select(x => $"{x.Nickname}({L.T("序列", "seq ")}{x.PlayerId})")) +
                            L.T(" —— 请写完整名字, 或直接用序列ID / Steam64", " — please write the full name, or use the sequence ID / Steam64 directly");
                    return false;
                }
            }

            // ③ Steam64 / 完整 UserId(在线或离线) / ③ Steam64 / full UserId (online or offline)
            if (Config.LookupBySteam)
            {
                string uid = NormalizeUserId(ident);
                if (!string.IsNullOrEmpty(uid))
                {
                    Player on = FindOnline(uid);
                    if (on != null) { online = on; displayName = on.Nickname; }
                    userId = uid;
                    PlayerData d = GetDataById(uid);              // 离线也能建/取 / Can also create/get while offline
                    if (string.IsNullOrEmpty(displayName) && d != null) displayName = d.Name;
                    return true;
                }
            }

            // 都没命中 → 说明当前允许哪些写法 / Nothing matched → explain which formats are currently allowed
            var allowed = new List<string>();
            if (Config.LookupBySeqId) allowed.Add(L.T("局内序列ID", "In-game sequence ID"));
            if (Config.LookupByName) allowed.Add(L.T("玩家名", "Player name"));
            if (Config.LookupBySteam) allowed.Add("Steam64/UserId");
            error = allowed.Count == 0
                ? L.T($"找不到玩家「{ident}」—— 三类解析方式(序列ID/玩家名/Steam64)在配置文件里都被关闭了", $"Player \"{ident}\" not found — all three lookup methods (sequence ID/player name/Steam64) are disabled in the config file")
                : L.T($"找不到玩家「{ident}」(当前允许的写法: {string.Join(" / ", allowed)})", $"Player \"{ident}\" not found (currently allowed formats: {string.Join(" / ", allowed)})");
            return false;
        }

        /// <summary>
        /// 按名字查在线真人玩家: 先精确匹配(命中即返回该一人); 再按 contains 决定是否做包含匹配。 / Find online real players by name: first exact match (return that single player immediately); then use contains to decide whether to do substring matching.
        /// 只考虑真人(排除人机/dummy); 结果可能为空 / 一人 / 多人。 / Only real players are considered (dummies excluded); the result may be empty / one player / multiple players.
        /// </summary>
        private static List<Player> FindOnlineByName(string query, bool contains)
        {
            var hits = new List<Player>();
            if (string.IsNullOrEmpty(query)) return hits;
            try
            {
                foreach (Player p in Player.List)
                {
                    if (p == null || p.IsDestroyed || p.IsDummy || !p.IsPlayer) continue;
                    string nick = p.Nickname ?? string.Empty;
                    if (string.Equals(nick, query, StringComparison.OrdinalIgnoreCase))
                        return new List<Player> { p };            // 精确命中 → 唯一结果 / Exact match → single result
                    if (contains && nick.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                        hits.Add(p);
                }
            }
            catch { }
            return hits;
        }

        /// <summary>
        /// 把 Steam64 纯数字补成游戏口径的 UserId(76561198xxxxxxxxx@steam); / Append the game's UserId suffix to a plain Steam64 number (76561198xxxxxxxxx@steam);
        /// 含 @ 的按完整 UserId 原样使用; 其它(既非数字也非 UserId)返回 null → 视为"找不到"。 / values containing @ are used as full UserIds as-is; others (neither digits nor UserId) return null → treated as "not found".
        /// </summary>
        private static string NormalizeUserId(string ident)
        {
            if (string.IsNullOrEmpty(ident)) return null;
            ident = ident.Trim();
            if (ident.IndexOf('@') > 0) return ident;                                    // 完整 UserId / Full UserId
            if (ident.Length >= 15 && ident.All(char.IsDigit)) return ident + "@steam";  // Steam64 纯数字 / Plain Steam64 digits
            return null;
        }

        /// <summary>严格判断一个 token 是否是数字(set/add 旧式两参兼容用)。 / Strictly check whether a token is numeric (for legacy two-argument set/add compatibility).</summary>
        private static bool TryParseNumber(string s)
            => float.TryParse(s, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out _)
               || float.TryParse(s, out _);

        private static float ParseFloat(string s)
        {
            float v;
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            if (float.TryParse(s, out v)) return v;
            return 0f;
        }

        /// <summary>按 key 修改配置(写回配置文件, 重启后仍生效)。 / Modify config by key (written back to the config file; persists after restart).</summary>
        private bool SetConfigValue(string key, string val, out string response)
        {
            switch (key)
            {
                case "server": Config.ServerName = val; break;
                case "time": case "timezone": Config.TimeZone = val; break;
                case "rate": Config.DamageToExp = ParseFloat(val); break;
                case "base": Config.BaseExp = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(val))); break;
                case "growth": Config.ExpGrowth = Mathf.Max(1.01f, ParseFloat(val)); break;
                case "maxlevel": Config.MaxLevel = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(val))); break;
                case "offlinedays": Config.OfflineDays = Mathf.Max(1, Mathf.RoundToInt(ParseFloat(val))); break;
                case "hudinterval": Config.HudInterval = Mathf.Clamp(ParseFloat(val), 0.2f, 10f); break;
                case "playtimemode": Config.PlaytimeMode = Mathf.Clamp(Mathf.RoundToInt(ParseFloat(val)), 0, 1); break;
                case "daysmode": Config.DaysMode = Mathf.Clamp(Mathf.RoundToInt(ParseFloat(val)), 0, 1); break;
                case "api": Config.ApiEnabled = ParseFloat(val) != 0f; break;
                case "topcount": Config.TopCount = Mathf.Clamp(Mathf.RoundToInt(ParseFloat(val)), 1, 20); break;
                case "custompos": Config.CustomLinesPos = Mathf.Clamp(Mathf.RoundToInt(ParseFloat(val)), 0, 1); break;
                case "onlyreal": Config.OnlyRealPlayers = ParseFloat(val) != 0f; break;
                case "countscp": Config.CountScp = ParseFloat(val) != 0f; break;
                case "countdummies": Config.CountDummies = ParseFloat(val) != 0f; break;
                case "settleondeath": Config.SettleOnDeath = ParseFloat(val) != 0f; break;
                case "texttemplates": Config.UseTextTemplates = ParseFloat(val) != 0f; break;
                case "mvpannounce": Config.MvpAnnounce = val; break;
                case "line1": Config.HudLine1 = val; break;
                case "line2": Config.HudLine2 = val; break;
                case "line3": Config.HudLine3 = val; break;
                case "line4": Config.HudLine4 = val; break;
                case "level1": Config.HudLevel1 = val; break;
                case "level2": Config.HudLevel2 = val; break;
                default:
                    response = L.T($"未知配置键: {key}\n可用: server/time/rate/base/growth/maxlevel/offlinedays/hudinterval/", $"Unknown config key: {key}\nAvailable: server/time/rate/base/growth/maxlevel/offlinedays/hudinterval/") +
                               "playtimemode/daysmode/api/topcount/custompos/onlyreal/countscp/countdummies/" +
                               "settleondeath/texttemplates/mvpannounce/line1..4/level1/level2";
                    return false;
            }
            SaveConfig(); LocalizeConfigFile();
            response = L.T($"已设置 {key} = {val} (已写入配置文件)", $"Set {key} = {val} (written to config file)");
            return true;
        }

        private bool SetLineText(string which, string text, out string response)
        {
            switch (which)
            {
                case "1": Config.HudLine1 = text; break;
                case "2": Config.HudLine2 = text; break;
                case "3": Config.HudLine3 = text; break;
                case "4": Config.HudLine4 = text; break;
                case "level1": case "l1": Config.HudLevel1 = text; break;
                case "level2": case "l2": Config.HudLevel2 = text; break;
                default:
                    response = L.T("用法: exp line <1-4|level1|level2> <文本>", "Usage: exp line <1-4|level1|level2> <text>");
                    return false;
            }
            SaveConfig(); LocalizeConfigFile();
            response = L.T($"已更新 {which} 行文本并写入配置文件", $"Updated line {which} and wrote it to the config file");
            return true;
        }
    }
}
