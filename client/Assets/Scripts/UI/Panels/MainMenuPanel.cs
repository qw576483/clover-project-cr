using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 主菜单（`MainMenu` 站点，`Main` 场景，Normal 层）：两段式 ——
    /// **顶带**（资源条 83.5 + 名字条 83.5 = 167，贴屏幕顶沿）与**底部导航带**（172.8，贴屏幕底沿），
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
        // 出处：基线 `策划/基线图/12_主菜单_750x1334.png` 顶部两条带的分界（x=300 / x=400 竖剖：
        // 资源槽内区止于 y57、名字带自 y58 起）⇒ 分界 y58@750 ×1.44 = **83.5**；顶带总高 = 原版
        // y0..116@750 ×1.44 = **167**，与 <see cref="CrUiStyle.PanelTopOffset"/>（同一张图上面板顶边
        // y=116@750 的读数）同值 ⇒ 两条带各 83.5。
        // 两条带各自贴屏幕左右沿全宽。

        /// <summary>资源条高 = **83.5**（原版两条带分界 y58@750 ×1.44）。</summary>
        private const float ResBarH = 83.5f;

        /// <summary>名字条高 = **83.5**（原版 y58..116@750 ×1.44）。</summary>
        private const float NameBarH = 83.5f;

        /// <summary>名字条顶边 = 资源条底边 = **83.5**（两条带上下相接，合计 = <see cref="CrUiStyle.PanelTopOffset"/>）。</summary>
        private const float NameBarY = ResBarH;

        /// <summary>两条带的内容左 / 右边界留白 = **32**（= <see cref="CrUiStyle.PopupBorder"/> + <see cref="CrUiStyle.PopupPad"/>，与面板亮面体内缩同一口径）。</summary>
        private const float BarPadX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;

        // ═══════════════════ 底部导航带（原版整条缺失项） ═══════════════════
        //
        // 出处：`策划/对照表.md` §D.3（原版带高 120@750 = **172.8**@1080、5 个页签）。
        // 原版 5 格的页签边界见 `12_主菜单_750x1334` 的 4 处整带高分隔槽（中心 125.5 / 251.5 / 497.5 / 623.5）
        // ×1.44 ⇒ 180.7 / 362.2 / 716.4 / 897.8，中间两格被原版的「对战」页签合并成一格宽。
        //
        // 本工程只有**两个入口**（卡组编辑 / 战斗）⇒ 原版的 5 格边界不再适用，
        // 改按原版"整带均分"的同一口径重排成 **2 等分**（180.7/362.2 那套是 5 格形态的值）。

        /// <summary>导航带高 = **172.8**（= 120@750 ×1.44）。</summary>
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
        /// 导航带底色 = <c>(35,43,48)</c>。出处：`策划/参考图/12_主菜单_750x1334.png`
        /// 第 1240 行的均色 (34,45,49)（逐行统计 x=20..730）。
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

        /// <summary>「对战入口」覆盖层（<see cref="BuildBattleEntry"/> 建，默认隐藏）。</summary>
        private GameObject _battleEntry;

        /// <summary>「对战入口」覆盖层的模态遮罩（挡掉下层页签的点击）。</summary>
        private GameObject _battleEntryMask;

        /// <summary>「玩家资料」弹窗（<see cref="BuildProfile"/> 建，默认隐藏）。</summary>
        private GameObject _profile;

        /// <summary>「玩家资料」弹窗的模态遮罩。</summary>
        private GameObject _profileMask;

        /// <summary>
        /// "没拿到服务端昵称"这条非预期分支**只报一次**。
        /// <para>
        /// ⛔ 刻意是 <c>static</c>（整个会话只报一次，而不是"每个面板实例一次"）：本面板每次
        /// `Open` / `Close` 都是新实例（`UIManager` 析构重建），实例级标志在"开→关→再开"下会重复刷屏，
        /// 而这条日志想说的事实是"这个会话没拿到服务端昵称"，与会话同粒度。
        /// </para>
        /// </summary>
        private static bool _nicknameWarned;

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
            SetBattleEntryVisible(false);   // 入口是"当次一次动作"：面板被再次 OnOpen 时收起，不残留
            SetProfileVisible(false);       // 资料弹窗同口径：重开主菜单时不残留
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
            // 玩家资料弹窗**首次点名字条时**才建（见 OpenProfile）：那时 GetProfile 的 8 项战绩已落库。

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
        /// ⚠️ 数值域**留空** —— 本工程没有货币 / 奖杯系统（`策划/对照表.md` §D.3），
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
            // 资源条（等级盘 / 经验条 / 金币 / 宝石）**整行不建**：本工程没有货币 / 等级 / 经验系统，
            // 四处数值都没有出处 ⇒ 建出来就是一条空黑胶囊（与“界面坏了”分不开）。
            // 口径 = 没数据就整行隐藏（与资料页空字段同一口径，见 `策划/对照表.md` §D.3）。
            Game.Logger?.Info("MainMenuPanel",
                "资源条整行不建：本工程无货币 / 等级 / 经验系统（对照表 §D.3）⇒ 不留空槽");

            // ── 名字条：原版木质名字板 + 原版亮色描边 + 昵称 + 设置齿轮 ──
            var plateW = CrUiStyle.DesignW - 2f * BarPadX;   // 1016
            // 板身 = 原版木质标题条帧（`ui_out` 70，1067×79；见 <see cref="ResPaths.TitleBarWood"/>）横拉，
            // 帧自带上下暗边（木质描边），⛔ 不再叠第二层描边件（802 的亮面会把木纹整片盖白）。
            var plate = CrUiStyle.Skin("NamePlate", root, ResPaths.TitleBarWood, 0, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BarPadX, -NameBarY),
                new Vector2(plateW, NameBarH), new Color32(83, 66, 48, 255), false);
            var pr = plate.rectTransform;

            // 名字条 = 原版 `profile_button`：点开玩家资料弹窗（同原版 profile 按钮的入口）。
            var plateBtn = plate.gameObject.AddComponent<Button>();
            plateBtn.targetGraphic = plate;
            DressButtonColors(plateBtn);
            CrUiFx.ButtonFx(plate);
            plateBtn.onClick.AddListener(() =>
            {
                CrUiStyle.PlayClick("NamePlate");
                OpenProfile();
            });

            // 板内的元素留白 = GapM（板本身已从屏幕边内缩 BarPadX）⇒ ⛔ 不再叠一层 BarPadX。
            // 昵称：服务端权威值（`PlayerSession.Nickname`），白字黑描边（原版名字条的读法）。
            // 右端只给设置齿轮留位；⛔ 不放奖杯徽章 —— 本工程没有奖杯数据，空徽章会被读成“坏了”。
            _nicknameLabel = CrUiStyle.Outlined("Nickname", pr, Cfg.Game.default_nick, CrUiStyle.FontBody,
                Color.white, Color.black,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(GapM, 0f),
                new Vector2(plateW - 3f * GapM - GearW, NameBarH),
                TextAnchor.MiddleLeft);

            // ── 设置入口 = 原版 `settings_button` 的齿轮帧（`ui_out` 563），贴名字条右端 ──
            var gear = CrUiStyle.Skin("SettingsButton", pr, ResPaths.IconGear, 0, Vector4.zero,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-GapM, 0f),
                new Vector2(GearW, GearH), CrUiStyle.BandSlate, true);
            var gearBtn = gear.gameObject.AddComponent<Button>();
            gearBtn.targetGraphic = gear;
            DressButtonColors(gearBtn);
            CrUiFx.ButtonFx(gear);
            gearBtn.onClick.AddListener(() =>
            {
                CrUiStyle.PlayClick("SettingsButton");
                EmitRequest(Events.Flow.OpenSettingsRequest, "设置");
            });
        }

        /// <summary>名字条内设置齿轮 高 = **44**（本项目自定：名字条 89 高，齿轮上下各留 ~22）。</summary>
        private const float GearH = 44f;

        /// <summary>名字条内设置齿轮 宽 = **40.2**（帧 <see cref="ResPaths.IconGear"/> 107×117 按高 44 保原比例）。</summary>
        private const float GearW = 40.2f;

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
                    // 选中态：2px 亮边 + 底部高亮条（纯色件；选中格几何不变）。
                    CrUiFx.SelectedTabEdge(cell.rectTransform, w, NavBandH, CrUiFx.NumberGold);

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
                CrUiFx.ButtonFx(cell);
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
            CrUiFx.ApplyButtonColors(btn);
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
            // err 可能是英文原文 ⇒ 先映射成玩家文案，原文只在日志里留痕。
            SetStatus(ServerText.ToPlayer(reason, "MainMenuPanel"), CrUiStyle.ErrorText);
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            // 提示行是**单行**（高 32）：对战失败原因是服务端 err 原文，长度不受本面板控制
            // ⇒ 必须过 `TextFit`，否则换行后会压到导航带上（D4 同族）。
            _status.text = TextFit.Clamp(_status, text);
            _status.color = color;
        }

        // ───────────────────────── 资源数值 ─────────────────────────

        // ───────────────────────── 对战入口 ─────────────────────────

        /// <summary>入口里按钮高 = **88**（本项目自定：<see cref="CrUiStyle.ButtonSecondaryH"/> 同档，三颗放进 388 高的亮面体）。</summary>
        private const float EntryBtnH = 88f;

        /// <summary>入口亮面体高 = **388**（上留白 16 + 3×88 按钮 + 2×20 缝 + 28 说明行 + 下留白 40）。</summary>
        private const float EntryBodyH = 16f + 3f * EntryBtnH + 2f * 20f + 28f + 40f;

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
                "和好友同房对战，或直接与 AI 开局。",
                CrUiStyle.FontSmall, new Vector2(x, y3 - EntryBtnH - gapY),
                new Vector2(btnW, 28f), TextAnchor.MiddleLeft, CrUiStyle.TextOnLightDim);

            SetBattleEntryVisible(false);
        }

        // ══════════════════ 玩家资料弹窗（原版 `popup_profile_new` 的版式） ══════════════════
        //
        // 版式出处：`策划/参考图/12_主菜单_750x1334.png`（原版整屏 750×1334）×1.44 折到 1080 画布：
        //   弹窗 x=17..727 ⇒ 宽 **1024**；标题带 y=119..188 ⇒ 高 **99**；缎带 x=136..608 ⇒ 宽 **680**。
        // 素材：外框 / 亮面 = `ui_out` 014 / 019（与设置、房间同一套）；区块标题 = 原版缎带
        //   372 / 373 / 374（<see cref="CrUiStyle.Ribbon"/>）；关闭 = 477 + 164；奖杯 = 562。
        // 数据 = <see cref="PlayerSession"/>（服务端 `GetProfileReply` 的 8 项）。
        // ⛔ 本工程没有部落 / 竞技场 / 奖杯机制 ⇒ 部落名、竞技场图、奖杯行**不建节点**（不留空框）；
        //   未拿到战绩时统计格也不建，只留一行说明。

        /// <summary>资料弹窗宽 = **1024**（基线 x17..727 = 711@750 ×1.44）。</summary>
        private const float ProfileW = 1024f;

        /// <summary>资料弹窗标题带高 = **99**（基线 y119..188 = 69@750 ×1.44）。</summary>
        private const float ProfileTitleH = 99f;

        /// <summary>资料弹窗高 = **1003**（名字行 + 两条缎带 + 4 行统计格 + 卡组编辑缎带）。</summary>
        private const float ProfileH = 1003f;

        /// <summary>资料弹窗内左右留白 = **32**（与顶带 <see cref="BarPadX"/> 同一口径）。</summary>
        private const float ProfilePadX = 32f;

        /// <summary>统计行节距 = **132**（基线 y653→745→834 = 92@750 ×1.44）。</summary>
        private const float StatRowPitch = 132f;

        /// <summary>统计标签行高 = **34**。</summary>
        private const float StatLabelH = 34f;

        /// <summary>统计数值盘高 = **66**（基线 y653..698 = 46@750 ×1.44）。</summary>
        private const float StatPlateH = 66f;

        /// <summary>两列统计格之间的缝 = **24**。</summary>
        private const float StatColGap = 24f;

        /// <summary>统计格盘色 = 基线 (51,51,51) ÷ 019 亮面 (216,229,235)（纯乘标定，与其它 tint 同口径）。</summary>
        private static readonly Color StatPlateTint = new Color(51f / 216f, 51f / 229f, 51f / 236f, 1f);

        /// <summary>建资料弹窗时的 <see cref="PlayerSession.HasStats"/>；打开时若已变化就重建一次。</summary>
        private bool _profileBuiltWithStats;

        /// <summary>资料弹窗是否已建过（首次点名字条时才建，数据此时已从 GetProfile 落库）。</summary>
        private bool _profileBuilt;

        private void BuildProfile(Transform root)
        {
            var mask = UIFactory.CreatePanel("ProfileMask", root, EntryMaskColor, true);
            UIFactory.Stretch(mask.rectTransform);
            _profileMask = mask.gameObject;

            var box = CrUiStyle.SettingsPopup("ProfileBox", root, ProfileH, ProfileW, ProfileTitleH);
            _profile = box.gameObject;
            CrUiStyle.BandTitle("ProfileTitle", box, "玩家资料", ProfileW, ProfileTitleH);

            // 关闭 = 原版红钮底 477 + 白叉 164（与设置面板右上角同一对帧）。
            var close = CrUiStyle.Skin("ProfileClose", box.rectTransform, CrUiStyle.ButtonRed, 0,
                CrUiStyle.BorderStateButton, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -17f), new Vector2(64f, 64f), CrUiStyle.ErrorText, true);
            CrUiStyle.Icon("ProfileCloseCross", close.rectTransform, ResPaths.IconClose,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(38f, 38f));
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = close;
            DressButtonColors(closeBtn);
            CrUiFx.ButtonFx(close);
            closeBtn.onClick.AddListener(() => { CrUiStyle.PlayClick("ProfileClose"); CloseProfile(); });

            var body = box.rectTransform.Find("ProfileBoxBody") as RectTransform;
            if (body == null)
            {
                // 非预期分支（必须留痕）：亮面体没建出来 ⇒ 资料内容无处摆放。
                Game.Logger?.Error("MainMenuPanel", "玩家资料弹窗的亮面体 ProfileBoxBody 没建出来");
                SetProfileVisible(false);
                return;
            }

            var hasStats = PlayerSession.HasStats;
            _profileBuiltWithStats = hasStats;

            // ── 名字行（原版 player_name：白字黑描边）──
            var name = string.IsNullOrEmpty(PlayerSession.Nickname) ? Cfg.Game.default_nick : PlayerSession.Nickname;
            var nameLabel = CrUiStyle.Outlined("ProfileName", body, name, CrUiStyle.FontTitle,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ProfilePadX, -8f),
                new Vector2(ProfileW - 2f * ProfilePadX, 72f), TextAnchor.MiddleLeft);
            nameLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            nameLabel.text = TextFit.Clamp(nameLabel, name);

            // ── 对战数据缎带 + 3 行 × 2 列 ──
            CrUiStyle.Ribbon("ProfileRibbonStats", body, "对战数据",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), CrUiStyle.RibbonW);

            const float row1 = -206f;
            if (hasStats)
            {
                BuildStatRow(body, row1, "胜场", PlayerSession.Wins.ToString(),
                    "常用卡牌", PlayerSession.HasFavouriteCard ? PlayerSession.FavouriteCardName : NoValueGlyph);
                BuildStatRow(body, row1 - StatRowPitch, "三冠胜场", PlayerSession.ThreeCrownWins.ToString(),
                    "已收集卡牌", PlayerSession.CardsFound.ToString());
                BuildStatRow(body, row1 - 2f * StatRowPitch, "最高奖杯", PlayerSession.HighestTrophies.ToString(),
                    "累计捐赠", PlayerSession.CardsDonated.ToString());
            }
            else
            {
                CrUiStyle.Outlined("ProfileNoStats", body, "暂无战绩数据", CrUiStyle.FontBody,
                    CrUiStyle.TextOnLightDim, CrUiStyle.OutlineDark,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(ProfilePadX, row1),
                    new Vector2(ProfileW - 2f * ProfilePadX, 66f), TextAnchor.MiddleCenter);
            }

            // ── 锦标赛数据缎带 + 1 行 × 2 列 ──
            var ribbon2Y = row1 - 2f * StatRowPitch - StatPlateH - (StatLabelH + 6f) * 0.5f - 40f;
            CrUiStyle.Ribbon("ProfileRibbonTournament", body, "锦标赛数据",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, ribbon2Y), CrUiStyle.RibbonW);
            if (hasStats)
            {
                BuildStatRow(body, ribbon2Y - 76f, "参赛场次", PlayerSession.Matches.ToString(),
                    "赢得卡牌", PlayerSession.CardsWon.ToString());
            }

            // ── 卡组编辑缎带（原版 Battle Deck 那一格，本工程 = 打开卡组编辑）──
            var deckY = ribbon2Y - (hasStats ? 76f + StatPlateH : 0f) - 40f;
            var deck = CrUiStyle.Ribbon("ProfileRibbonDeck", body, "卡组编辑",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, deckY),
                CrUiStyle.RibbonW, true);
            var deckBtn = deck.gameObject.AddComponent<Button>();
            deckBtn.targetGraphic = deck;
            DressButtonColors(deckBtn);
            deckBtn.onClick.AddListener(() =>
            {
                CrUiStyle.PlayClick("ProfileDeck");
                CloseProfile();
                EmitRequest(Events.Deck.OpenRequest, "卡组编辑");
            });

            SetProfileVisible(false);
            Game.Logger?.Info("MainMenuPanel",
                $"玩家资料弹窗已建：hasStats={hasStats}，缎带 3 条（对战数据 / 锦标赛数据 / 卡组编辑）");
        }

        /// <summary>一行两格统计（左、右各一组“标签 + 数值盘”）。</summary>
        private static void BuildStatRow(Transform parent, float topY, string leftLabel, string leftValue,
            string rightLabel, string rightValue)
        {
            var colW = (ProfileW - 2f * ProfilePadX - StatColGap) * 0.5f;
            BuildStatCell(parent, ProfilePadX, topY, colW, leftLabel, leftValue);
            BuildStatCell(parent, ProfilePadX + colW + StatColGap, topY, colW, rightLabel, rightValue);
        }

        /// <summary>一格统计：蓝标签（原版同色）+ 深色数值盘（019 亮面纯乘标定到基线 (51,51,51)）+ 白字。</summary>
        private static void BuildStatCell(Transform parent, float x, float topY, float w,
            string label, string value)
        {
            CrUiStyle.Outlined(label, parent, label, CrUiStyle.FontSmall, CrUiStyle.RibbonReading,
                CrUiStyle.OutlineDark, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, topY), new Vector2(w, StatLabelH), TextAnchor.MiddleLeft);

            var plate = CrUiStyle.Skin("StatPlate", parent, CrUiStyle.PopupSkinLight, 10, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, topY - StatLabelH - 6f),
                new Vector2(w, StatPlateH), CrUiStyle.BandSlate, false, StatPlateTint);
            CrUiStyle.Outlined("Value", plate.rectTransform, value, CrUiStyle.FontBody, CrUiStyle.TextColor,
                CrUiStyle.OutlineDark, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f),
                new Vector2(w - 32f, StatPlateH), TextAnchor.MiddleLeft);
        }

        /// <summary>点名字条打开资料弹窗；首次打开时才建（数据已由 GetProfile 落库）。</summary>
        private void OpenProfile()
        {
            if (!_profileBuilt)
            {
                BuildProfile(transform);
                _profileBuilt = true;
            }
            else if (PlayerSession.HasStats != _profileBuiltWithStats)
            {
                // 建窗之后才拿到 / 清空战绩 ⇒ 重建一次（值域与统计格的有无都要跟着变）。
                if (_profile != null) Destroy(_profile);
                if (_profileMask != null) Destroy(_profileMask);
                _profile = null;
                _profileMask = null;
                BuildProfile(transform);
            }
            SetProfileVisible(true);
            Game.Logger?.Info("MainMenuPanel", "打开玩家资料弹窗（名字条入口）");
        }

        private void CloseProfile()
        {
            SetProfileVisible(false);
        }

        private void SetProfileVisible(bool visible)
        {
            if (visible) CrUiFx.ShowOverlay(_profile); else CrUiFx.HideOverlay(_profile);
            if (_profileMask != null) _profileMask.SetActive(visible);
        }

        /// <summary>
        /// 是否有覆盖层（对战入口 / 玩家资料）正在屏幕上。
        /// 供返回键路由判断"这一层该关掉谁"，⛔ 不改变面板自己的开关逻辑。
        /// </summary>
        public bool HasOverlayOpen
        {
            get
            {
                return (_battleEntry != null && _battleEntry.activeSelf)
                    || (_profile != null && _profile.activeSelf);
            }
        }

        /// <summary>
        /// 关掉当前最上面的覆盖层：资料弹窗优先（它后开、盖在对战入口之上），否则关对战入口。
        /// 返回 false = 没有覆盖层可关（调用方继续处理下一层）。
        /// </summary>
        public bool TryCloseTopOverlay()
        {
            if (_profile != null && _profile.activeSelf)
            {
                CloseProfile();
                return true;
            }
            if (_battleEntry != null && _battleEntry.activeSelf)
            {
                CloseBattleEntry();
                return true;
            }
            return false;
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
            if (visible) CrUiFx.ShowOverlay(_battleEntry); else CrUiFx.HideOverlay(_battleEntry);
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
