using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 主菜单（`MainMenu` 站点，`Main` 场景，Normal 层）：两段式 ——
    /// **顶带**（资源条 78 + 名字条 89 = 167，贴屏幕顶沿）与**底部导航带**（172.8，贴屏幕底沿），
    /// 中间只留背景美术。
    ///
    /// <para>
    /// <b>导航带只有两个入口</b>：卡组编辑（原版 `icon_menu_cards` 帧 446）与战斗
    /// （原版 `icon_menu_battle` 帧 226，抬起态）。「战斗」入口只打开
    /// **对战入口覆盖层**（见 <see cref="BuildBattleEntry"/>），开打是玩家在入口里的第二次点击。
    /// </para>
    /// <para>
    /// <b>本面板只负责"按钮存在 + 发事件"</b>：动作的**实现**分属各模块
    /// （卡组编辑 / 房间列表 / 人机对战与对局），
    /// 所以这里一律 `Emit(Events.Xxx.…Request)`，由各模块（或 `AppFlow`）订阅后执行。
    /// ⛔ 面板不 `using CR.Module` —— 这是契约 §1 的硬线，也是"按钮与链路解耦"的收益：
    /// 模块接入时**不需要**动本文件。
    /// </para>
    /// <para>
    /// <b>版式出处</b>：`策划/基线图/12_主菜单_750x1334.png`（750×1334 的原版整屏），
    /// 比例 ×1.44 折到本工程的 1080×1920 画布（见 <see cref="CrUiStyle.DesignW"/>）。
    /// 帧号出处 = 原版 `ui.sc` 的**显式子件引用**（可复跑：
    /// `python tools/probes/sc-subtree.py --clip Menu_topLayer --depth 4`），
    /// 每条都写在该常量 / 该构件旁。
    /// </para>
    /// </summary>
    public sealed class MainMenuPanel : UIPanel
    {
        // ═══════════════════ 顶部两条固定带（原版 y0..167@1080） ═══════════════════
        //
        // 出处：基线 `策划/基线图/12_主菜单_750x1334.png` 顶部两条带（资源条 y0..54.2@750、
        // 名字条 y54.2..116@750）×1.44 ⇒ 资源条 **78** + 名字条 **89** = **167**，
        // 与 <see cref="CrUiStyle.PanelTopOffset"/>（同一张图上面板顶边 y=116@750 的读数）同值。
        // 两条带各自贴屏幕左右沿全宽。

        /// <summary>资源条高 = **78**（原版 y0..54.2@750 ×1.44）。</summary>
        private const float ResBarH = 78f;

        /// <summary>名字条高 = **89**（原版 y54.2..116@750 ×1.44）。</summary>
        private const float NameBarH = 89f;

        /// <summary>名字条顶边 = 资源条底边 = **78**（两条带上下相接，合计 = <see cref="CrUiStyle.PanelTopOffset"/>）。</summary>
        private const float NameBarY = ResBarH;

        /// <summary>两条带的内容左 / 右边界留白 = **32**（= <see cref="CrUiStyle.PopupBorder"/> + <see cref="CrUiStyle.PopupPad"/>，与面板亮面体内缩同一口径）。</summary>
        private const float BarPadX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;

        /// <summary>资源槽高 = **58**（本项目自定：资源条 78 − 上下各 10；原版槽盘边界压在深色背景上量不稳）。</summary>
        private const float BarSlotH = 58f;

        /// <summary>
        /// 资源槽端帽宽 = **9.5**。出处：帧 <see cref="ResPaths.MenuTopSlotCap"/> 原生 **11×67**，
        /// 原版按 1:1 放置（`sx` 1.2168 ≈ 1）⇒ 本工程按槽高 58 保原比例 = 58 × 11/67 = **9.5**（⛔ 不横拉变形）。
        /// </summary>
        private const float BarSlotCapW = 9.5f;

        /// <summary>
        /// 槽内资源图标宽 = **36**。出处：基线 `12_主菜单_750x1334` 行 1 的金币图标 y25..50@750
        /// （高 25 ⇒ 36@1080；帧 <see cref="ResPaths.MenuTopCoinIcon"/> 70×76 按该宽等比高 39 &lt; 槽高 58）。
        /// </summary>
        private const float BarIconW = 36f;

        /// <summary>资源槽「+」（buy）按钮宽 = **63.4**（基线 `12_主菜单_750x1334` 绿色钮外接 x250..293 = 44@750 ×1.44）。</summary>
        private const float BuyW = 63.4f;

        /// <summary>资源槽「+」按钮高 = **57.6**（基线 y15..54 = 40@750 ×1.44；≈ 槽高 <see cref="BarSlotH"/> 58）。</summary>
        private const float BuyH = 57.6f;

        /// <summary>「+」按钮离槽左沿 = **7.2**（基线 250 − 槽左沿 245 = 5@750 ×1.44）。</summary>
        private const float BuyPadX = 7.2f;

        // ── 顶带左端的等级盘 + 经验条（原版 `left_top` → `xp` 整组） ──
        //
        // 出处：基线 `12_主菜单_750x1334` 逐点量取，全部 ×1.44 折到 1080：
        //   等级盘外接 x21..90 / y6..66；经验条外框 x88..228 / y17..58；条内填充读数 (10,57,72)。
        //   `xp` 组在 `.sc` 里的设计期相对坐标与基线**不成同一比例**（同组内盘宽/条宽在 `.sc` 是 1.16、
        //   基线是 0.51）⇒ 绝对几何一律以基线为准，`.sc` 只用来定帧与结构。

        /// <summary>等级盘 左沿 = **30.2**（基线 x21@750 ×1.44）。</summary>
        private const float LevelDiscX = 30.2f;

        /// <summary>等级盘 宽 = **100.8**（基线 x21..90 = 70@750 ×1.44）。</summary>
        private const float LevelDiscW = 100.8f;

        /// <summary>等级盘 高 = **86.4**（基线 y6..66 = 60@750 ×1.44；比资源条 <see cref="ResBarH"/> 高 ⇒ 原版该盘也压出带外）。</summary>
        private const float LevelDiscH = 86.4f;

        /// <summary>经验条 左沿 = **126.7**（基线 x88@750 ×1.44；左端与原版一样压在等级盘的锯齿上）。</summary>
        private const float XpBarX = 126.7f;

        /// <summary>经验条 宽 = **201.6**（基线 x88..228 = 140@750 ×1.44）。</summary>
        private const float XpBarW = 201.6f;

        /// <summary>
        /// 等级盘标定 = 帧 <see cref="ResPaths.MenuLevelDisc"/> 的均值 **(39,147,177)** → 基线盘面读数 **(13,46,55)**。
        /// 原版顶带整体压暗；同一压暗系数也能从经验条帧均值 (215,66,221) 与基线 (10,57,72) 复算。
        /// </summary>
        private static readonly Color LevelDiscTint = new Color(13f / 39f, 46f / 147f, 55f / 177f, 1f);

        /// <summary>
        /// 经验条内填色标定 = 帧 <see cref="ResPaths.MenuXpBarFill"/> 的**众数色 (240,136,244)** → 基线条内读数 **(10,57,72)**。
        /// <para>
        /// 分母用**众数**而不是均值：该帧是一条纵向渐变（顶/底 (216,16,224)/(188,28,196)、中部 (240,136,244)），
        /// 用均值当分母会让 G 通道被拉亮成绿色。用众数 ⇒ 中部渲染为 (10,57,72)，两端自然压暗成深蓝，
        /// 即原版那条「中间亮、上下暗」的条面。
        /// </para>
        /// </summary>
        private static readonly Color XpFillTint = new Color(10f / 240f, 57f / 136f, 72f / 244f, 1f);

        /// <summary>
        /// 名字板端帽宽 = **13.2**。出处：帧 <see cref="ResPaths.MenuNamePlateCap"/> 原生 **24×162**
        /// （含竖向渐变），本工程带高只有 89 ⇒ 按 89 × 24/162 保原比例 = **13.2**（⛔ 不横拉 / 不竖压变形）。
        /// </summary>
        private const float NamePlateCapW = 13.2f;

        /// <summary>
        /// 资源槽 / 名字板的槽底色 = <c>(10,12,14)</c>。出处：基线 `12_主菜单_750x1334` 的槽盘内部逐点读数
        /// （金币槽 `(330,30)` = (11,13,15)、`(360,20)` = (9,11,12)、宝石槽 `(600,30)` = (11,13,15)）。
        /// </summary>
        private static readonly Color BarSlotReading = new Color32(10, 12, 14, 255);

        // ═══════════════════ 底部导航带（原版整条缺失项） ═══════════════════
        //
        // 出处：`策划/验收表.md` **D13**（原版带高 120@750 = **172.8**@1080、5 个页签）。
        // 原版 5 格的页签边界见 `12_主菜单_750x1334` 的 4 处整带高分隔槽（中心 125.5 / 251.5 / 497.5 / 623.5）
        // ×1.44 ⇒ 180.7 / 362.2 / 716.4 / 897.8，中间两格被原版的「对战」页签合并成一格宽。
        //
        // 本工程只有**两个入口**（卡组编辑 / 战斗）⇒ 原版的 5 格边界不再适用，
        // 改按原版"整带均分"的同一口径重排成 **2 等分**（180.7/362.2 那套是 5 格形态的值）。

        /// <summary>导航带高 = **172.8**（= D13 的 120@750 ×1.44）。</summary>
        private const float NavBandH = 172.8f;

        /// <summary>两个入口的左右边界（@1080）：1080 ÷ 2 等分。</summary>
        private static readonly float[] NavTabEdges = { 0f, 540f, 1080f };

        /// <summary>「战斗」入口的下标 = **1** —— 原版底栏里抬起的那一格也是对战。</summary>
        private const int NavBattleTab = 1;

        /// <summary>提示行高 = **32**（本项目自定：一行 <see cref="CrUiStyle.FontSmall"/> 的提示文本）。</summary>
        private const float StatusH = 32f;

        /// <summary>未选中页签的图元宽 = **130**（基线测：页签图标高约 90@750 ⇒ 130@1080）。</summary>
        private const float NavIconW = 130f;

        /// <summary>选中页签的图元宽 = **166**（基线测：对战那双剑宽约 115@750 ⇒ 166@1080）。</summary>
        private const float NavIconWSelected = 166f;

        /// <summary>
        /// 分隔槽宽 = **10.1**。出处：帧 <see cref="ResPaths.NavDivider"/> 原生 **7×1**，原版只做**纵向**拉伸
        /// （`sy` 1.3604 = 整带高）⇒ 横向按原版 1:1 = 7@750 ×1.44 = 10.1@1080。
        /// </summary>
        private const float NavSepW = 10.1f;

        /// <summary>
        /// 导航带底色 = <c>(35,43,48)</c>。出处：`策划/对照表.md` §U#4 的审计读数
        /// （原版该区均色 (35,43,48) vs 我们的 (6,28,6)）。
        /// </summary>
        private static readonly Color NavBandReading = new Color32(35, 43, 48, 255);

        /// <summary>
        /// 未选中页签格色 = <c>(15,19,25)</c>。出处：基线 `12_主菜单_750x1334` 导航带内逐点读数
        /// （x=100/250/520/600 在 y=1250/1290/1310 处实测 (11,14,20)~(19,22,30)）。
        /// </summary>
        private static readonly Color NavCellReading = new Color32(15, 19, 25, 255);

        /// <summary>选中页签盘色 = <c>(31,47,62)</c>。出处：基线 `12_主菜单_750x1334` 对战签内 y=1250/1290 逐点读数。</summary>
        private static readonly Color NavSelReading = new Color32(31, 47, 62, 255);

        /// <summary>
        /// 分隔槽色 = <c>(24,24,24)</c>。出处：帧 `ui_out` **438** 落盘 PNG 的不透明像素均值 (24,24,24)，
        /// 原版直接用它当槽（基线读数 (18,21,27) 与帧不同族、且纯乘无法从 24 提到 27 ⇒ 按帧原色直出）。
        /// </summary>
        private static readonly Color NavSepReading = new Color32(24, 24, 24, 255);

        /// <inheritdoc cref="BarSlotTint"/>
        private static readonly Color NavBandTint = new Color(35f / 96f, 43f / 102f, 48f / 119f, 1f);

        /// <summary>分母 = 帧 `ui_out` **544** 的均值 (52,66,83)（`menu_bottom_tab_bg` → `<shape 1635>`）。</summary>
        private static readonly Color NavCellTint = new Color(15f / 52f, 19f / 66f, 25f / 83f, 1f);

        /// <summary>分母 = 帧 `ui_out` **565** 的均值 (88,130,165)（`menu_bottom_tab_selected` → `<shape 1657>`）。</summary>
        private static readonly Color NavSelTint = new Color(31f / 88f, 47f / 130f, 62f / 165f, 1f);

        /// <summary>
        /// 选中页签**两侧的页选箭头**（原版 `UI_pageSelection_arrow_anim` → 帧 <see cref="ResPaths.NavSelectArrow"/>）
        /// 宽 = **56.2**（基线 x262..300 = 39@750 ×1.44）。
        /// </summary>
        private const float NavArrowW = 56.2f;

        /// <summary>页选箭头 高 = **67.7**（基线 y1252..1298 = 47@750 ×1.44）。</summary>
        private const float NavArrowH = 67.7f;

        /// <summary>页选箭头 离本格左右沿 = **15.1**（基线 262 − 选中格左沿 251.5 = 10.5@750 ×1.44）。</summary>
        private const float NavArrowInsetX = 15.1f;

        /// <summary>页选箭头 离带顶 = **54.7**（基线 1252 − 带顶 1214 = 38@750 ×1.44）。</summary>
        private const float NavArrowTop = 54.7f;

        /// <summary>
        /// 页选箭头标定 = 帧 <see cref="ResPaths.NavSelectArrow"/> 的均值 **(109,145,162)** → 基线箭头面读数 **(74,91,97)**。
        /// </summary>
        private static readonly Color NavArrowTint = new Color(74f / 109f, 91f / 145f, 97f / 162f, 1f);

        /// <summary>
        /// 两个入口的标签，顺序与 <see cref="NavIconPaths"/> 一一对应（0 = 卡组编辑 / 1 = 战斗）。
        /// ⚠️ 文字本身没有 2.1.5 一手出处（原版文字在字体/语言资源里，不在本工程持有的 `.sc` 图元里）。
        /// </summary>
        private static readonly string[] NavLabels = { "卡组编辑", "战斗" };

        /// <summary>
        /// 「本工程没有这一项数据」的显示符 —— 资源槽数值 / 等级盘数字 / 经验条文字都用它：
        /// ⛔ 不编数字，⛔ 也不留空（留空在画面上就是一个空槽，与"界面坏了"分不开）。
        /// </summary>
        private const string NoValueGlyph = "\u2014";

        private bool _built;
        private Text _nicknameLabel;

        /// <summary>提示行（贴导航带上沿）：对战失败原因 / 各入口的即时提示。</summary>
        private Text _status;

        /// <summary>两个资源槽的数值文本（顺序 = 金币 / 宝石，见 <see cref="BuildTopBars"/>）。</summary>
        private readonly Text[] _resValues = new Text[2];

        /// <summary>「对战入口」覆盖层（<see cref="BuildBattleEntry"/> 建，默认隐藏）。</summary>
        private GameObject _battleEntry;

        /// <summary>「对战入口」覆盖层的模态遮罩（挡掉下层页签的点击）。</summary>
        private GameObject _battleEntryMask;

        /// <summary>
        /// "没拿到服务端昵称"这条非预期分支**只报一次**。
        /// <para>
        /// ⛔ 刻意是 <c>static</c>（整个会话只报一次，而不是"每个面板实例一次"）：本面板每次
        /// `Open` / `Close` 都是新实例（`UIManager` 析构重建），实例级标志在"开→关→再开"下会重复刷屏，
        /// 而这条日志想说的事实是"这个会话没拿到服务端昵称"，与会话同粒度。
        /// </para>
        /// </summary>
        private static bool _nicknameWarned;

        /// <summary>"资源条 / 名字条奖杯没有出处"这条**只报一次**（与会话同粒度，理由同 <see cref="_nicknameWarned"/>）。</summary>
        private static bool _resGapWarned;

        private Action<string> _onBattleStartFailed;
        private Action<string> _onNicknameKnown;

        /// <summary>主菜单在 Normal 层（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }
            Subscribe();

            // 昵称来源有两条，按权威性排序（见 `Core/PlayerSession.cs` 的类注释）：
            //  ① param —— 由 AppFlow 传入（`Game.UI.Open<MainMenuPanel>(nickname)`）；
            //  ② `PlayerSession.Nickname` —— **服务端权威会话态**（AppFlow 从 GetProfile / SetNickname
            //     回包写入）。⛔ 之所以必须要有 ②：param 是**一次性快照**，只有 `AppFlow.EnterMainMenu`
            //     会传；别的打开路径（`UIManager.Open<T>` 对已存在面板重调 `OnOpen(param)`，`UI.cs:127`；
            //     `AppFlow.GoTo` 对同站点早退，`AppFlow.cs:365`）拿到的就是 null ⇒ 硬编码的 "玩家"
            //     会被**静默**显示，与服务端档案里的名字不一致且**一句日志都没有**。
            //     面板⛔不许 `using CR.Module`（契约 §1）⇒ 只能读同层 `CR.PlayerSession`，不能直接问 Flow。
            var nickname = param as string;
            if (string.IsNullOrEmpty(nickname)) nickname = PlayerSession.Nickname;
            ApplyNickname(nickname);
            RefreshValues();
            SetBattleEntryVisible(false);   // 入口是"当次一次动作"：面板被再次 OnOpen 时收起，不残留
            SetStatus(string.Empty, CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
        }

        // ───────────────────────── 视觉树 ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            CrUiStyle.Screen("Bg", root);
            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // ── 两段式：顶带（贴屏幕顶沿）→ 底部导航带（贴屏幕底沿），中间只留背景美术 ──
            BuildTopBars(root);      // 资源条 78 + 名字条 89 = 167
            BuildStatusLine(root);   // 提示行（贴导航带上沿）
            BuildNavBand(root);      // 导航带 172.8、2 个入口（卡组编辑 / 战斗）
            BuildBattleEntry(root);  // 「战斗」入口的落点 = 对战入口覆盖层（默认隐藏）

            // 非预期分支（必须留痕）：提示行没建出来 ⇒ 失败原因 / 提示文本无处可显。
            if (_status == null)
            {
                Game.Logger?.Error("MainMenuPanel", "提示行没建出来，对战失败原因与提示文本无法显示");
            }
        }

        /// <summary>
        /// 提示行：贴导航带上沿的**整屏宽**一行文本（对战失败原因 / 各入口的即时提示）。
        /// 白字黑描边 —— 它压在主菜单背景美术上。
        /// </summary>
        private void BuildStatusLine(Transform root)
        {
            _status = CrUiStyle.Outlined("Status", root, string.Empty, CrUiStyle.FontSmall,
                Color.white, Color.black,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, NavBandH + GapM),
                new Vector2(CrUiStyle.DesignW - 2f * BarPadX, StatusH), TextAnchor.MiddleCenter);
            if (_status != null) _status.raycastTarget = false;
        }

        // ══════════════════ 顶部两条固定带 / 底部导航带 ══════════════════

        /// <summary>
        /// 顶部两条固定带（贴屏幕顶沿、全宽）：资源条 <see cref="ResBarH"/> + 名字条 <see cref="NameBarH"/>。
        /// <para>
        /// <b>资源条</b>：两个原版资源槽（原版 `Menu_topLayer`(ui.sc clip 4539) 的
        /// `right_top` → `coins` / `gems` 两组的矩形），槽身 = <see cref="ResPaths.MenuTopSlotBar"/> 横拉、
        /// 两端 = <see cref="ResPaths.MenuTopSlotCap"/> 镜像对、图标 = <see cref="ResPaths.MenuTopCoinIcon"/> /
        /// <see cref="ResPaths.MenuTopGemIcon"/>。
        /// ⚠️ 数值域**留空** —— 本工程没有货币 / 奖杯系统（`策划/差异登记.tsv` **D12**），
        /// ⛔ 不编数值（只落版式与图元，缺口在运行时 Warn 一次）。
        /// 左端另有一组**等级盘 + 经验条**（`left_top` → `xp`），见 <see cref="BuildLevelXp"/>。
        /// </para>
        /// <para>
        /// <b>名字条</b>：原版 `profile_strip` → `profile_button` 的名字板
        /// （横条 <see cref="ResPaths.MenuNamePlateBar"/> + 两端 <see cref="ResPaths.MenuNamePlateCap"/> 镜像对）
        /// + 昵称（服务端权威值）+ 奖杯图元 + 设置齿轮（<see cref="ResPaths.IconGear"/>，
        /// 原版 `settings_button` → `<shape 1655>`）。
        /// 板内**没有头像框**：原版那颗头像的落点是玩家资料页，本工程没有资料页。
        /// </para>
        /// </summary>
        private void BuildTopBars(Transform root)
        {
            // 原版两个资源槽的横向范围（基线 `12_主菜单_750x1334` 行 1 槽色分段实测
            // x245..498 / x512..694 ⇒ ×1.44）：
            const float coinX = 352.8f;
            const float coinW = 365.8f;
            const float gemX = 737.3f;
            const float gemW = 263.5f;
            // 行内容顶边 = **23**（基线 `12_主菜单_750x1334` 资源槽盘顶边 y16@750 ×1.44 = 23.0；
            // 盘高 40@750 ⇒ 底边 80.6 —— 原版同样是槽盘底边压住两条带的分界）。
            const float slotY = 23f;

            // 左端整组：等级盘 + 经验条（见本文件「顶带左端的等级盘 + 经验条」段）。
            BuildLevelXp(root, slotY);

            _resValues[0] = BuildResourceSlot(root, "ResCoin", coinX, slotY, coinW, ResPaths.MenuTopCoinIcon);
            _resValues[1] = BuildResourceSlot(root, "ResGem", gemX, slotY, gemW, ResPaths.MenuTopGemIcon);

            if (!_resGapWarned)
            {
                _resGapWarned = true;
                // 非预期分支（必须留痕）：本工程没有货币 / 奖杯系统 ⇒ 资源条与名字条奖杯**没有出处**。
                Game.Logger?.Warn("MainMenuPanel",
                    "资源条 / 名字条奖杯无数值出处：本工程无货币 / 奖杯系统（差异登记 D12）" +
                    "⇒ 只落版式与图元、数值显示占位符 " + NoValueGlyph);
            }

            // ── 名字条：名字板（原版 `profile_button`）+ 昵称 + 奖杯 + 设置齿轮 ──
            var plateW = CrUiStyle.DesignW - 2f * BarPadX;   // 1016
            // 名字板用的就是原版那一帧（`profile_button` 的 `<shape 1653>`）⇒ 原色直出，⛔ 不加 tint。
            var plate = CrUiStyle.Skin("NamePlate", root, ResPaths.MenuNamePlateBar, 0, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BarPadX, -NameBarY),
                new Vector2(plateW, NameBarH), BarSlotReading, false);
            var pr = plate.rectTransform;

            CrUiStyle.Icon("NamePlateCapL", pr, ResPaths.MenuNamePlateCap,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(NamePlateCapW, NameBarH));
            var capR = CrUiStyle.Icon("NamePlateCapR", pr, ResPaths.MenuNamePlateCap,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(NamePlateCapW, NameBarH));
            // 原版同一张端帽帧放两次（sx +0.7998 / −0.8008）⇒ 右端是本图的水平镜像。
            capR.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            // 板内的元素留白 = GapM（板本身已从屏幕边内缩 BarPadX）⇒ ⛔ 不再叠一层 BarPadX。
            // 昵称：服务端权威值（`PlayerSession.Nickname`），白字黑描边（原版名字条的读法）。
            // 右端给奖杯 + 设置齿轮留位，右边界 = 板右沿 −（GapM + 奖杯宽 + GapM + 齿轮宽 + GapM）。
            _nicknameLabel = CrUiStyle.Outlined("Nickname", pr, "玩家", CrUiStyle.FontBody,
                Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(GapM, 0f),
                new Vector2(plateW - 4f * GapM - BarTrophyW - GearW, NameBarH),
                TextAnchor.MiddleLeft);

            CrUiStyle.AspectImage("TrophyBadge", pr, ResPaths.IconTrophyGold, BarTrophyW,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-GapM - GearW - GapM, 0f),
                CrUiStyle.TextDim);

            // ── 设置入口 = 原版 `settings_button` 的齿轮帧（`ui_out` 563），贴名字条右端 ──
            var gear = CrUiStyle.Skin("SettingsButton", pr, ResPaths.IconGear, 0, Vector4.zero,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-GapM, 0f),
                new Vector2(GearW, GearH), CrUiStyle.BandSlate, true);
            var gearBtn = gear.gameObject.AddComponent<Button>();
            gearBtn.targetGraphic = gear;
            DressButtonColors(gearBtn);
            gearBtn.onClick.AddListener(() =>
            {
                CrUiStyle.PlayClick("SettingsButton");
                EmitRequest(Events.Flow.OpenSettingsRequest, "设置");
            });
        }

        /// <summary>
        /// 顶带左端的**等级盘 + 经验条**（原版 `Menu_topLayer`(ui.sc clip 4539) 的 `left_top` → `xp` →
        /// `xp_icon` / `xp_bar`）。
        /// <para>
        /// 结构与帧全取自 `.sc`：等级盘 = <see cref="ResPaths.MenuLevelDisc"/>（帧 543）；
        /// 经验条轨道 = <see cref="ResPaths.MenuTopSlotFill"/>（帧 208 横拉）+ <see cref="ResPaths.MenuTopSlotCap"/>
        /// （帧 209 镜像端帽对，与两个资源槽**同一件**）；条内填充 = <see cref="ResPaths.MenuXpBarFill"/>（帧 518）。
        /// </para>
        /// <para>
        /// ⚠️ 数值域**留空**：本工程没有等级 / 经验系统（`策划/差异登记.tsv` **D12** 残余①）⇒ 盘内的等级数字与
        /// 条上的经验文字都写 <see cref="NoValueGlyph"/>，⛔ 不编等级值。填充按基线 `12_主菜单_750x1334` 量到的
        /// 「整条内区都是填充读数」铺满条内区（⛔ 同样不代表任何人的经验进度）。
        /// </para>
        /// </summary>
        private void BuildLevelXp(Transform root, float rowY)
        {
            // 等级盘：中心与顶带行同高（基线盘 y6..66 的中心 = 行中心；盘比行高 ⇒ 原版也压出带外）。
            CrUiStyle.Skin("LevelDisc", root, ResPaths.MenuLevelDisc, 0, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(LevelDiscX, -(rowY + BarSlotH * 0.5f)),
                new Vector2(LevelDiscW, LevelDiscH), new Color32(13, 46, 55, 255), false, LevelDiscTint);
            CrUiStyle.Outlined("LevelValue", root, NoValueGlyph, CrUiStyle.FontBody,
                Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(LevelDiscX, -(rowY + BarSlotH * 0.5f)),
                new Vector2(LevelDiscW, LevelDiscH), TextAnchor.MiddleCenter);

            // 经验条：轨道（帧 208 横拉 + 帧 209 镜像端帽对）—— 与资源槽同一件的读法。
            var bar = CrUiStyle.Skin("XpBar", root, ResPaths.MenuTopSlotFill, 0, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(XpBarX, -rowY),
                new Vector2(XpBarW, BarSlotH), BarSlotReading, false);
            var br = bar.rectTransform;

            var innerW = XpBarW - 2f * BarSlotCapW;
            CrUiStyle.Icon("XpBarCapL", br, ResPaths.MenuTopSlotCap,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(BarSlotCapW, BarSlotH));
            var capR = CrUiStyle.Icon("XpBarCapR", br, ResPaths.MenuTopSlotCap,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(BarSlotCapW, BarSlotH));
            // 原版同一张端帽帧放两次（sx +1.167 / −1.167）⇒ 右端是本图的水平镜像。
            capR.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            // 条内填充：帧 518，铺满条内区。帧本身是品红 ⇒ 靠纯乘标定到基线读数。
            CrUiStyle.Skin("XpBarFill", br, ResPaths.MenuXpBarFill, 0, Vector4.zero,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BarSlotCapW, 0f),
                new Vector2(innerW, BarSlotH - 2f * GapS), new Color32(10, 57, 72, 255), false, XpFillTint);
            CrUiStyle.Outlined("XpBarValue", br, NoValueGlyph, CrUiStyle.FontBody,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(innerW, BarSlotH), TextAnchor.MiddleCenter);
        }

        /// <summary>名字条内设置齿轮 高 = **44**（本项目自定：名字条 89 高，齿轮上下各留 ~22）。</summary>
        private const float GearH = 44f;

        /// <summary>名字条内设置齿轮 宽 = **40.2**（帧 <see cref="ResPaths.IconGear"/> 107×117 按高 44 保原比例）。</summary>
        private const float GearW = 40.2f;

        /// <summary>名字条内奖杯图元的占位宽 = **42**（<see cref="ResPaths.IconTrophyGold"/> 按 <see cref="CrUiStyle.AspectImage"/> 的目标宽）。</summary>
        private const float BarTrophyW = 42f;

        /// <summary>
        /// 建一个资源槽：槽条（<see cref="ResPaths.MenuTopSlotBar"/> 横向拉伸）+ 内填
        /// （<see cref="ResPaths.MenuTopSlotFill"/>）+ 两端端帽（<see cref="ResPaths.MenuTopSlotCap"/> 镜像对）
        /// + 原版资源图标 + 数值域（返回它，交给 <see cref="RefreshValues"/> 写占位符）。
        /// 数值域**不写数**：本工程没有货币系统（`策划/差异登记.tsv` D12），⛔ 不编数值。
        /// <para>槽条本身是**纯黑**的原版帧（落盘均值 (0,0,0)），基线读数 (10,12,14) 是截图压缩噪声 ⇒ ⛔ 不提亮。</para>
        /// </summary>
        private Text BuildResourceSlot(Transform root, string name, float x, float y, float w, string iconPath)
        {
            var slot = CrUiStyle.Skin(name, root, ResPaths.MenuTopSlotBar, 0, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(w, BarSlotH), BarSlotReading, false);
            var sr = slot.rectTransform;

            var innerW = w - 2f * BarSlotCapW;
            CrUiStyle.Skin(name + "Fill", sr, ResPaths.MenuTopSlotFill, 0, Vector4.zero,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BarSlotCapW, 0f),
                new Vector2(innerW, BarSlotH - 2f * GapS), BarSlotReading, false);

            CrUiStyle.Icon(name + "CapL", sr, ResPaths.MenuTopSlotCap,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(BarSlotCapW, BarSlotH));
            var capR = CrUiStyle.Icon(name + "CapR", sr, ResPaths.MenuTopSlotCap,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(BarSlotCapW, BarSlotH));
            // 原版同一张端帽帧放两次（sx +1.2168 / −1.2178）⇒ 右端是本图的水平镜像。
            capR.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

            // 原版图标压在槽的**右端**（基线行 1：金币图标 x455..500 在金币组 x245..498 的右沿）。
            CrUiStyle.AspectImage(name + "Icon", sr, iconPath, BarIconW,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-BarPadX, 0f), CrUiStyle.TextDim);

            // 原版资源槽**左端**还有一颗绿色「+」（`right_top` → `coins`/`gems` → `buy_gold`/`buy_gems`）：
            // 帧 = <see cref="ResPaths.IconPlus"/>（`ui_out` 521，帧本身就是"绿底 + 深绿加号"的整颗按钮，
            // 实测 50×50 满幅不透明、主色 (72,176,72) + 深绿 (0,48,0) 笔画）。
            var buy = CrUiStyle.Skin(name + "Buy", sr, ResPaths.IconPlus, 0, Vector4.zero,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BuyPadX, 0f),
                new Vector2(BuyW, BuyH), CrUiStyle.PrimaryBg, true);
            var buyBtn = buy.gameObject.AddComponent<Button>();
            buyBtn.targetGraphic = buy;
            DressButtonColors(buyBtn);
            var buyName = name;
            buyBtn.onClick.AddListener(() => OnBuyClicked(buyName));

            var value = CrUiStyle.Outlined(name + "Value", sr, string.Empty, CrUiStyle.FontBody,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(GapM, 0f),
                new Vector2(innerW - BarIconW - 3f * GapM, BarSlotH), TextAnchor.MiddleRight);
            value.raycastTarget = false;
            return value;
        }

        /// <summary>
        /// 底部导航带（原版整条缺失项，出处见本文件「底部导航带」段的抬头）。
        /// <para>
        /// **只有两个入口**（卡组编辑 / 战斗），按原版"整带均分"的口径铺满；格底 = <see cref="ResPaths.NavTabBgStrip"/>
        /// （原版 `menu_bottom_tab_bg` 的 `<shape 1635>`）、选中格 = <see cref="ResPaths.NavTabSelectedStrip"/>
        /// （`menu_bottom_tab_selected` 的 `<shape 1657>`）、分隔槽 = <see cref="ResPaths.NavDivider"/>
        /// （`menu_bottom_divider` 的 `<shape 1518>`）、选中标签投影 = <see cref="ResPaths.NavLabelShadow"/>
        /// （`menu_bottom_label` 的 `title_shadow`）、选中格两侧 = <see cref="ResPaths.NavSelectArrow"/>
        /// （`UI_pageSelection_arrow_anim` 的 `<shape 1656>` 镜像对）。
        /// </para>
        /// <para>
        /// 点按行为：卡组编辑 → `Events.Deck.OpenRequest`；战斗 → **对战入口**
        /// （见 <see cref="BuildBattleEntry"/>），⛔ 入口本身不开打 ——
        /// 入口只决定"进哪一屏"，开打要由玩家在对战入口里再点一次。
        /// </para>
        /// </summary>
        private void BuildNavBand(Transform root)
        {
            var band = CrUiStyle.Skin("NavBand", root, CrUiStyle.PopupFrameSlate, 0, CrUiStyle.BorderButtonDark,
                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero,
                new Vector2(CrUiStyle.DesignW, NavBandH), NavBandReading, false, NavBandTint);
            var bt = band.rectTransform;
            var icons = NavIconPaths();

            for (var i = 0; i < NavTabEdges.Length - 1; i++)
            {
                var x0 = NavTabEdges[i];
                var w = NavTabEdges[i + 1] - x0;
                var selected = i == NavBattleTab;

                // 原版：选中 = 抬起的盘（frame 565）；未选中 = 格底条（frame 544）。
                var cell = CrUiStyle.Skin(NavCellName(i), bt, selected ? ResPaths.NavTabSelectedStrip : ResPaths.NavTabBgStrip,
                    0, Vector4.zero, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x0, 0f), new Vector2(w, NavBandH),
                    selected ? NavSelReading : NavCellReading, true, selected ? NavSelTint : NavCellTint);

                CrUiStyle.AspectImage("NavIcon" + i, cell.rectTransform, icons[i],
                    selected ? NavIconWSelected : NavIconW,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, selected ? 16f : 0f), CrUiStyle.PanelBg, false);

                if (selected)
                {
                    // 原版选中格**两侧各一颗页选箭头**（`UI_pageSelection_arrow_anim` → 帧 564）：
                    // 帧本身是**朝左**的那一颗，原版左格用镜像件（▶ 指向格内）、右格用原向件（◀ 指向格内）。
                    // 镜像件的锚点取整格右沿、枢轴 (0,1) ⇒ 翻转后右沿落在格左沿 + 内缩处（⛔ 用 (1,1) 枢轴
                    // 翻转会把整块推到格右沿外侧，实机读到 rect 越过 1080 屏宽）。
                    var arrowL = CrUiStyle.Skin("NavSelectArrowL", cell.rectTransform, ResPaths.NavSelectArrow,
                        0, Vector4.zero, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(NavArrowInsetX + NavArrowW, -NavArrowTop),
                        new Vector2(NavArrowW, NavArrowH), Color.black, false, NavArrowTint);
                    arrowL.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                    CrUiStyle.Skin("NavSelectArrowR", cell.rectTransform, ResPaths.NavSelectArrow, 0, Vector4.zero,
                        new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-NavArrowInsetX, -NavArrowTop),
                        new Vector2(NavArrowW, NavArrowH), Color.black, false, NavArrowTint);

                    // 原版只在「抬起」的那一格下面带文字标签（基线 `12_主菜单` 的对战签下就是 "Battle"）；
                    // 标签底下压一条原版投影条（`menu_bottom_label` → `title_shadow`）。
                    CrUiStyle.Skin("NavLabelShadow" + i, cell.rectTransform, ResPaths.NavLabelShadow, 0, Vector4.zero,
                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f),
                        new Vector2(w, 40f), Color.black, false);
                    CrUiStyle.Outlined("NavLabel" + i, cell.rectTransform, NavLabels[i], CrUiStyle.FontSmall,
                        new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f),
                        new Vector2(w, 40f), TextAnchor.MiddleCenter);
                }

                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = cell;
                DressButtonColors(btn);
                var idx = i;
                btn.onClick.AddListener(() =>
                {
                    CrUiStyle.PlayClick("NavTab" + idx);
                    OnNavTabClicked(idx);
                });
            }

            // 分隔槽压在页签之上（原版那 4 处整带高的槽，见本文件「底部导航带」段抬头）。
            for (var i = 1; i < NavTabEdges.Length - 1; i++)
            {
                CrUiStyle.Skin("NavSep" + i, bt, ResPaths.NavDivider, 0, Vector4.zero,
                    new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(NavTabEdges[i], 0f),
                    new Vector2(NavSepW, NavBandH), NavSepReading, false);
            }
        }

        /// <summary>
        /// 入口格节点名：第 <see cref="NavBattleTab"/> 格（战斗）**逐字**叫 `AiBattleButton`、
        /// 卡组编辑格**逐字**叫 `DeckButton` —— 这两个名字是本工程抓帧 / 驱动脚本
        /// （`tools/probes/CrProbe.cs` 的 `ClickAiBattle` / `ClickDeckEdit`）按名点的那两个节点，
        /// ⛔ 改名等于把那条驱动链拆掉。
        /// </summary>
        private static string NavCellName(int index)
        {
            return index == NavBattleTab ? "AiBattleButton" : "DeckButton";
        }

        /// <summary>
        /// 两个入口的图元（**都是已落地的原版图元**，⛔ 没有一格是自绘 / 纯色块）。
        /// <para>
        /// 每一帧由原版 `ui_v215.sc` 的导出名给出：`icon_menu_cards`(clip 3929 → frame 446) /
        /// `icon_menu_battle`(clip 4510 → frame 226)。「卡组编辑」在本工程就是原版底栏的**卡牌**页签
        /// （卡组编辑页的入口）⇒ 用它的那一手帧，⛔ 不为卡组编辑另找 / 另画一个图标。
        /// </para>
        /// </summary>
        private static string[] NavIconPaths()
        {
            return new[]
            {
                ResPaths.NavIconCards,       // 卡组编辑（icon_menu_cards → frame 446）
                ResPaths.IconBattle,         // 战斗（icon_menu_battle → frame 226）
            };
        }

        /// <summary>按钮四态（常态 = 白 = 原版图元原色；⛔ 不用底色当滤镜）。口径同其它面板的建按钮处。</summary>
        private static void DressButtonColors(Button btn)
        {
            if (btn == null) return;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = CrUiStyle.ButtonFade;
            btn.colors = colors;
        }

        /// <summary>
        /// 底部导航带被点（0 = 卡组编辑 / 1 = 战斗，见 <see cref="NavLabels"/>）。
        /// 「战斗」只**打开对战入口**（<see cref="OpenBattleEntry"/>），⛔ 不在这里开打 ——
        /// 入口是导航，开打必须是玩家在对战入口里的另一次显式点击。
        /// </summary>
        private void OnNavTabClicked(int index)
        {
            switch (index)
            {
                case 0:
                    EmitRequest(Events.Deck.OpenRequest, NavLabels[index]);
                    return;
                case NavBattleTab:
                    OpenBattleEntry();
                    return;
                default:
                    Game.Logger?.Warn("MainMenuPanel", $"底部导航收到未知下标 {index}，无对应页签");
                    return;
            }
        }

        // ══════════════════ 面板侧不自备建件（`Outlined` / `BlueButton` 都在 `CrUiStyle`） ══════════════════
        //
        // 本文件只调用 `CrUiStyle.*`：`Outlined` 在 `CenteredText` 旁、`BlueButton` 在 `ActionButton` 旁，
        // ⛔ 这里没有第二份实现（否则房间列表 / 房间内 / 卡组编辑三处再各抄一份，必漂移）。

        private const float GapS = 12f;
        private const float GapM = 16f;

        // ───────────────────────── 交互 ─────────────────────────

        /// <summary>
        /// 资源槽「+」（原版 `buy_gold` / `buy_gems`，进内购）被点。本工程**没有商店 / 货币系统**
        /// （`策划/差异登记.tsv` D12）⇒ 按本面板既有口径给**可读提示**（⛔ 不做点了没反应的假按钮，
        /// ⛔ 也不假装能进内购）。
        /// </summary>
        private void OnBuyClicked(string slot)
        {
            Game.Logger?.Info("MainMenuPanel",
                $"资源槽「{slot}」的「+」：原版进内购商店，本工程没有商店 / 货币系统（差异登记 D12）⇒ 只给可读提示");
            SetStatus("本工程没有商店 / 货币系统（原版这个「+」是内购入口）", CrUiStyle.Accent);
        }

        private void EmitRequest(string eventName, string what)
        {
            Game.Logger?.Info("MainMenuPanel", $"请求：{what}（事件 {eventName}）");
            Game.Event?.Emit(eventName);
        }

        // ───────────────────────── 订阅 ─────────────────────────

        private void Subscribe()
        {
            if (_onBattleStartFailed == null) _onBattleStartFailed = OnBattleStartFailed;
            Game.Event?.Off(Events.Battle.StartFailed, _onBattleStartFailed);
            Game.Event?.On(Events.Battle.StartFailed, _onBattleStartFailed);

            // 服务端权威昵称是**迟到**数据（登录拉档案 / 创角回包 / 重新拉取都可能晚于本面板 OnOpen）
            // ⇒ 订阅推送，到了就地刷新标签，⛔ 不用重开面板。
            if (_onNicknameKnown == null) _onNicknameKnown = OnNicknameKnown;
            Game.Event?.Off(Events.Flow.NicknameKnown, _onNicknameKnown);
            Game.Event?.On(Events.Flow.NicknameKnown, _onNicknameKnown);
        }

        private void Unsubscribe()
        {
            if (_onBattleStartFailed != null) Game.Event?.Off(Events.Battle.StartFailed, _onBattleStartFailed);
            if (_onNicknameKnown != null) Game.Event?.Off(Events.Flow.NicknameKnown, _onNicknameKnown);
        }

        /// <summary>
        /// 推送侧：服务端权威昵称已到达。昵称与资源缺口来自**同一条回包**（`GetProfileReply`）
        /// ⇒ 昵称到位即可就地刷资源数值域，⛔ 不必为此另拉一次档案。
        /// </summary>
        private void OnNicknameKnown(string nickname)
        {
            if (string.IsNullOrEmpty(nickname)) return;
            ApplyNickname(nickname);
            RefreshValues();
        }

        /// <summary>
        /// 把昵称写到标签上；空值走**兜底策略**并留痕。
        /// <para>
        /// 兜底策略（写清）：拿不到服务端昵称时显示 <see cref="Cfg.Game.default_nick"/>（= 本地配置里的
        /// 默认名，出处 `Core/ClientConfig.cs:42`），⛔ 不是硬编码字面量、⛔ 不是留空；同时
        /// <c>Game.Logger?.Warn</c> **只报一次**（<see cref="_nicknameWarned"/>）——下一次
        /// `Flow.NicknameKnown` 或重开面板拿到真名就会覆盖掉它。
        /// </para>
        /// </summary>
        private void ApplyNickname(string nickname)
        {
            if (_nicknameLabel == null) return;
            if (string.IsNullOrEmpty(nickname))
            {
                // 非预期分支：拿不到档案 / 档案为空（服务端该玩家还没创角 / 档案请求失败）。
                if (!_nicknameWarned)
                {
                    _nicknameWarned = true;
                    Game.Logger?.Warn("MainMenuPanel",
                        "未拿到服务端昵称（Open 的 param 为空且 PlayerSession 无值）⇒ 按兜底策略显示本地默认名 " +
                        $"'{Cfg.Game.default_nick}'；请查 AppFlow 登录链 / GetProfile 是否走完（根因见 Core/PlayerSession.cs）");
                }
                nickname = Cfg.Game.default_nick;
            }
            // ⛔ 必须过 `TextFit`：昵称是**服务端数据**（长度不由本面板控制），而引擎的文本节点
            //    默认 `Wrap + Overflow`（`UIWidgets.cs:151-152`）⇒ 超长昵称换行后会画出标签矩形、
            //    **盖住整个面板**（D4 实测 preferredW 24576 > rectW 808）。
            //    ⛔ 不改字号（那是"糊过去"，D4 明令禁止）；uGUI 也没有 `horizontalOverflow=Truncate`
            //    这个取值（`HorizontalWrapMode` 只有 Wrap/Overflow）⇒ 只能按宽度裁**字符串**。
            _nicknameLabel.text = TextFit.Clamp(_nicknameLabel, nickname);
        }

        private void OnBattleStartFailed(string reason)
        {
            // 失败原因必须显示（同样不许只打日志）：最常见的是"还没编辑过卡组"
            //（服务端 `onAiBattleStart` 在 len(deck) != 8 时回落到档案卡组，档案卡组为空 ⇒ 回 err）。
            SetStatus(string.IsNullOrEmpty(reason) ? "人机对战启动失败（原因未知）" : reason, CrUiStyle.ErrorText);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }

        // ───────────────────────── 资源数值 ─────────────────────────

        /// <summary>
        /// 把两个资源槽的数值域刷成 <see cref="NoValueGlyph"/>（本工程没有货币系统）⇒
        /// <see cref="OnOpen"/> 与 <see cref="OnNicknameKnown"/> 各调一次。
        /// </summary>
        private void RefreshValues()
        {
            for (var i = 0; i < _resValues.Length; i++)
                if (_resValues[i] != null) _resValues[i].text = NoValueGlyph;
        }

        // ───────────────────────── 对战入口 ─────────────────────────

        /// <summary>入口里按钮高 = **88**（本项目自定：<see cref="CrUiStyle.ButtonSecondaryH"/> 同档，三颗放进 388 高的亮面体）。</summary>
        private const float EntryBtnH = 88f;

        /// <summary>入口亮面体高 = **364**（上下留白 16 + 3×88 按钮 + 2×20 缝 + 28 说明行）。</summary>
        private const float EntryBodyH = 16f + 3f * EntryBtnH + 2f * 20f + 28f + 16f;

        /// <summary>入口框高 = **424**（亮面体 364 + 标题带 <see cref="CrUiStyle.PopupTitleH"/> + 描边）。</summary>
        private const float EntryH = EntryBodyH + CrUiStyle.PopupTitleH + CrUiStyle.PopupBorder;

        /// <summary>入口的模态遮罩色 = <see cref="CrUiStyle.ScreenBg"/> 的 65%（⛔ 不另造颜色）。</summary>
        private static readonly Color EntryMaskColor =
            new Color(CrUiStyle.ScreenBg.r, CrUiStyle.ScreenBg.g, CrUiStyle.ScreenBg.b, 0.65f);

        /// <summary>
        /// 建「对战入口」覆盖层（**本项目新增界面**）：原版点底栏「对战」直接进配对，本工程没有配对服，
        /// 服务端要"建房 + 选模式"才开打 ⇒ 把两个真入口收在这里，由玩家点第二次才开打。
        /// 外观语言与 `RoomListPanel` / `SettingsPanel` 同一套（`ui_out` 014 板岩外框 + 019 亮面体 +
        /// 447 蓝按钮），⛔ 没有一个自绘图元。
        /// </summary>
        private void BuildBattleEntry(Transform root)
        {
            var mask = UIFactory.CreatePanel("BattleEntryMask", root, EntryMaskColor, true);
            UIFactory.Stretch(mask.rectTransform);
            _battleEntryMask = mask.gameObject;

            var box = CrUiStyle.SettingsPopup("BattleEntryBox", root, EntryH);
            _battleEntry = box.gameObject;
            CrUiStyle.BandTitle("EntryTitle", box, "对 战", CrUiStyle.PopupW);

            var body = box.rectTransform.Find("BattleEntryBoxBody") as RectTransform;
            if (body == null)
            {
                // 非预期分支（必须留痕）：亮面体没建出来 ⇒ 入口上的按钮全落空。
                Game.Logger?.Error("MainMenuPanel", "对战入口的亮面体 BattleEntryBoxBody 没建出来，入口按钮无法摆放");
                SetBattleEntryVisible(false);
                return;
            }

            const float x = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;
            const float btnW = CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder - 2f * CrUiStyle.PopupPad;
            const float gapY = 20f;

            var y1 = -16f;
            var y2 = y1 - EntryBtnH - gapY;
            var y3 = y2 - EntryBtnH - gapY;

            CrUiStyle.BlueButton("RoomEntryButton", body, "房间列表",
                new Vector2(x, y1), new Vector2(btnW, EntryBtnH), OnRoomEntryClicked);
            CrUiStyle.BlueButton("AiEntryButton", body, "人机对战",
                new Vector2(x, y2), new Vector2(btnW, EntryBtnH), OnAiEntryClicked);
            CrUiStyle.BlueButton("EntryBackButton", body, "返 回",
                new Vector2(x, y3), new Vector2(btnW, EntryBtnH), CloseBattleEntry);

            UIFactory.CreateLabel("EntryHint", body,
                "原版点「对战」直接进配对；本工程没有配对服 —— 先建房、或直接开局打人机。",
                CrUiStyle.FontSmall, new Vector2(x, y3 - EntryBtnH - gapY),
                new Vector2(btnW, 28f), TextAnchor.MiddleLeft, CrUiStyle.TextOnLightDim);

            SetBattleEntryVisible(false);
        }

        /// <summary>「战斗」入口的落点：显式打开对战入口（⛔ 入口本身不开打）。</summary>
        private void OpenBattleEntry()
        {
            Game.Logger?.Info("MainMenuPanel",
                "打开对战入口（战斗入口只负责进这一屏；开打由入口里的按钮触发）");
            SetBattleEntryVisible(true);
        }

        /// <summary>关掉对战入口（返回 / 选了某个入口之后）。</summary>
        private void CloseBattleEntry()
        {
            SetBattleEntryVisible(false);
        }

        private void SetBattleEntryVisible(bool visible)
        {
            if (_battleEntry != null) _battleEntry.SetActive(visible);
            if (_battleEntryMask != null) _battleEntryMask.SetActive(visible);
        }

        /// <summary>入口里的「房间列表」：发 `Events.Room.OpenListRequest`。</summary>
        private void OnRoomEntryClicked()
        {
            CloseBattleEntry();
            EmitRequest(Events.Room.OpenListRequest, "房间列表");
        }

        /// <summary>入口里的「人机对战」—— 本工程**唯一**会真正开打的点击点。</summary>
        private void OnAiEntryClicked()
        {
            CloseBattleEntry();
            SetStatus("正在向服务端申请人机对战…", CrUiStyle.Accent);
            EmitRequest(Events.Battle.AiBattleRequest, "人机对战");
        }
    }
}
