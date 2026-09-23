using System;
using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 卡组编辑（`MainMenu` 站点的 Popup 子面板，架构契约 §4）：60 张卡池里选 8 张 + 保存 / 取消。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔不许 `using CR.Module` —— 它只
    /// `Emit(Events.Deck.*)` 并把管理器回来的数据画出来。保存请求走的是
    /// `Events.Deck.Changed`（`Events.cs` 里 Deck 段只有 4 条事件，没有独立的"保存请求"事件），
    /// 由 `Module/Deck/DeckManager` 按方向区分同一条事件（它那边有重入保护），详见
    /// <see cref="OnSaveClicked"/> 与 <see cref="OnDeckChanged"/>。
    /// </para>
    ///
    /// <para>
    /// <b>竖版重排（G3）+ AP1 换帧</b>：画布 = <see cref="CrUiStyle.DesignW"/>×<see cref="CrUiStyle.DesignH"/> = 1080×1920、`match = 0`。
    /// 面板底 = <see cref="CrUiStyle.SettingsPopup"/>（居中弹窗：`ui_out` 014 板岩外框 + 019 亮面体，
    /// 与主菜单 / 设置同语言；宽 = <see cref="CrUiStyle.ContentW"/> = 1000 ≤ 竖版口径上限 1000），
    /// 标题 = <see cref="CrUiStyle.BandTitle"/>（板岩带上的白字黑描边），
    /// 按钮 = <see cref="CrUiStyle.BlueButton"/>（`ui_out` 165 蓝底白字），
    /// 格子底 = <see cref="ResPaths.SlotCard"/>（原版白色卡片底九宫格，**经联络图复核后判定不改**，
    /// 见 <see cref="CreateCell"/> 的注释）。
    /// 全部几何数字量自原版基线图 `策划/参考图/07_卡组编辑_1242x2208.jpg`（折算 ×(1080/1242) = 0.8696，
    /// 与 1242×2208 → 1080×1920 同宽高比），逐条出处见 `.ai-tmp/test/G3-量取.md`；
    /// AP1 换帧引起的宽度差（卡 205 / 槽 95）登记在 `.ai-tmp/test/AP1-量取.md` B 段。
    /// </para>
    ///
    /// <para>
    /// <b>卡面（G3 改：⛔ 不再按类型上色）</b>：卡面 = 原版素材帧 `ResPaths.SpellArtFrame(i)`
    /// （`ui_spells_out`，403×377 画布、图的透明包围盒在 (98,0)-(295,251)）；
    /// 卡 key → 帧号 的对应表已上收到 <see cref="CrUiStyle.TryGetCardArtFrame"/>（唯一真源，
    /// 依据 = `策划/原版UI素材名称索引.md` §3.5 的原版 export 名 → `frame_NNN`）；
    /// 表里没有的卡**不猜帧号**，只画原版卡片底 + 名字/圣水（登记在 `G3-自审.md`）。
    /// </para>
    /// </summary>
    public sealed class DeckEditPanel : UIPanel
    {
        private const string Tag = "DeckEditPanel";

        /// <summary>
        /// 打开参数（由 `Module/Deck/DeckManager` 构造）。⛔ 面板不许引 `CR.Module`，
        /// 所以这类"服务端口径的常量"只能经参数递进来，不能在面板里再写一份。
        /// </summary>
        public sealed class PanelArgs
        {
            /// <summary>一副卡组的张数（服务端口径 8，由 `DeckManager` 递入）。</summary>
            public int MaxSelected;
        }

        /// <summary>
        /// 打开参数缺失时的兜底张数。为什么会有这条路：有人绕过 `DeckManager` 直接 `Game.UI.Open&lt;DeckEditPanel&gt;()`
        /// 时参数是 null；面板不许引 `CR.Module` ⇒ 读不到 `DeckManager.DeckSize`，只能兜底 8（= 服务端口径）。
        /// 这条路径会打 Warn（不静默），正常游戏流程永远不走它。
        /// </summary>
        private const int FallbackMaxSelected = 8;

        // ═══════════════ 竖版排版常量（出处见 G3-量取.md；原版 = 07_卡组编辑_1242x2208.jpg，折算 ×0.8696） ═══════════════
        //
        // <b>AP1 换帧：面板底从「贴顶居中的米色纸框」换成「居中弹窗（014 板岩外框 + 019 亮面体）」</b>
        // （与主菜单 / 设置同一套语言）⇒ 元素坐标系改为**亮面体左上角**为原点，宽度按亮面体重新定：
        //   · 弹窗宽 = `ContentW` = 1000（**仍守住竖版"内容框 ≤ 1000"口径**；比主菜单的 935 宽，
        //     因为本面板要放**原版 4 列卡阵**——基线 07 的卡阵几乎顶到屏边，属原版的**全宽**布局）；
        //   · 亮面体宽 = 1000 − 2×2 = 996；文字元素内缩 30（`PopupPad`）⇒ 可用 936。
        // ⚠️ 由此卡阵 / 槽位比 G3 的取值各收 ~2%：卡宽 206 → **205**（原版量取值 209.6 ⇒ −2.2%，G3 时是 −1.7%），
        //    槽宽 96 → **95**、槽缝 33 → **32**；差值逐条登记在 `.ai-tmp/test/AP1-量取.md` B 段 + 允许差异。

        /// <summary>弹窗外框宽 = 1000（<see cref="CrUiStyle.ContentW"/>；见上面的口径说明）。</summary>
        private const float BoxW = CrUiStyle.ContentW;

        /// <summary>亮面体宽 = 996（= BoxW − 2×描边 2）。</summary>
        private const float BodyW = BoxW - 2f * CrUiStyle.PopupBorder;

        /// <summary>亮面体内缩留白后的可用宽 = 936（= BodyW − 2×<see cref="CrUiStyle.PopupPad"/>）。</summary>
        private const float InnerW = BodyW - 2f * CrUiStyle.PopupPad;

        /// <summary>元素左边界 = 32（= 描边 2 + 留白 30）。</summary>
        private const float InsetX = CrUiStyle.PopupBorder + CrUiStyle.PopupPad;

        /// <summary>标题行高 = 92（与主菜单同口径：标题压弹窗顶部板岩带）。</summary>
        private const float TitleRowH = CrUiStyle.TitleBarH;

        private const float GapS = 12f;
        private const float GapM = 16f;
        private const float GapL = 32f;

        /// <summary>卡池列数。原版 07 的卡格实测 **4 列**（列左边 x = 344 / 651 / 959 @1242）。</summary>
        private const int Columns = 4;

        /// <summary>每页行数。原版 07 可见 2 行（行顶 y = 437 / 934 @1242）⇒ 每页 8 张。</summary>
        private const int Rows = 2;

        private const int PageSize = Columns * Rows;                      // 8

        /// <summary>单格宽。原版卡格 241@1242 ⇒ 209.6；AP1 换帧后要塞进亮面体 996 ⇒ 取 205（原版 −2.2%，登记）。</summary>
        private const float CardW = 205f;

        /// <summary>单格高。原版卡格 393@1242 ⇒ 341.8，同比例收窄取 334（原版 −2.2%，登记）。</summary>
        private const float CardH = 334f;

        /// <summary>列间距。原版 307.5 − 241 = 66.5@1242 ⇒ 57.8 ⇒ 58。</summary>
        private const float ColGap = 58f;

        /// <summary>行间距。原版 497 − 393 = 104@1242 ⇒ 90.4 ⇒ 90。</summary>
        private const float RowGap = 90f;

        private const float CardStepX = CardW + ColGap;                   // 263
        private const float CardStepY = CardH + RowGap;                   // 424
        private const float GridW = Columns * CardW + (Columns - 1) * ColGap;   // 994 ≤ 亮面体 996
        private const float GridH = Rows * CardH + (Rows - 1) * RowGap;         // 758

        /// <summary>
        /// 卡阵左边界 = 1。
        /// <para>⚠️ 卡阵按**亮面体全宽**居中（不是按内缩 936 居中）：基线 07 的卡阵是**全宽铺满**
        /// 的（4 列几乎顶到屏边）⇒ 卡阵不该再套 30px 内缩（套了就装不下 994 宽）。</para>
        /// </summary>
        private const float GridX = (BodyW - GridW) * 0.5f;               // 1

        // ── 卡面几何（量自素材自身，⛔ 不是"拍"的） ──
        //
        // ★ CR-T2：帧自身的**透明包围盒 / 逐帧 x 偏移例外 / 卡 key → 帧号表** 三项已全部上收到
        // `CrUiStyle`（`CardArtBbox*` / `CardArtCropOffsetX` / `TryGetCardArtFrame`）—— 原先本面板与
        // `HudPanel` 各存一份，AO1 把本面板那份由 34 扩到 60 条时没同步 `HudPanel` ⇒ 手牌大批卡无卡面。
        // ⛔ 本面板只保留**自己这两处**的量取值（卡池格的内边距/距格顶，量自 `07_卡组编辑` 基线图）。
        private const float ArtW = 188f;                                  // 卡面宽 = 格宽 − 2×9（原版卡面占卡宽 ≈91%）
        private const float ArtH = 239f;                                  // = 188 × (251/197)（素材自身宽高比）
        /// <summary>
        /// 卡面距格顶 / 距格左右的内边距（AQ1 实测，读数见 `.ai-tmp/test/AQ1-量取.md` C 段）。
        /// <para>
        /// <b>量的哪条边</b>：原版 `07_卡组编辑_1242x2208.jpg` 第一行卡片的**上边与左右两边** ——
        /// 卡格外沿（黑描边/白亮框）与卡面彩色内容之间那条留白，逐像素读 RGB 找边界。
        /// 读数：左右各 **6~8px@1242**（卡格宽 256~270 ⇒ 3.05%）、格顶 **4px@1242**（卡格宽 263 ⇒ 1.52%）。
        /// </para>
        /// <para>
        /// ⛔ 旧值 = 左右 9 / 顶 6（卡池格 205 宽 ⇒ 4.39% / 2.93%）**大于**原版 ⇒ 卡面比原版小一圈、
        /// 格内四周多出一圈留白（D-AP1-9「卡面偏上留白」）。按比例换算：左右 = 205×3.05% ≈ **6**、
        /// 顶 = 205×1.52% ≈ **3**。
        /// </para>
        /// </summary>
        private const float ArtInsetX = 6f;

        private const float ArtTop = 3f;                                  // 卡面距格顶（AQ1 实测 4px@1242 ⇒ 3）
        private const float NameBandH = 64f;                              // 名字带高（原版 Level 带 74@1242 ⇒ 64）
        private const float BadgeW = 40f;                                 // 圣水水滴宽（原版格角圣水数是紫圆 + 数字）
        private const float BadgeX = 4f;
        private const float BadgeH = 40f;

        /// <summary>已选槽位宽：8 格 + 7 缝排进亮面体 996 ⇒ 95（原版卡格宽 209.6 的同一收窄比例）。</summary>
        private const float SlotW = 95f;
        private const float SlotH = 155f;                                 // = 95 × (393/241)（原版卡格宽高比）
        private const float SlotGap = 32f;
        private const int DefaultSlotCount = 8;

        private const float BtnW = 824f;                                  // A 宽条按钮 572@750 ×1.44 = 823.7
        private const float BtnH = 66f;                                   // A 同一条 h=46@750 ×1.44 = 66.2
        private const float PageBtnH = 48f;                               // 行内小按钮档（A "Clan" 33@750 ⇒ 47.5）

        /// <summary>悬停 / 按下的混色系数（本项目自定；原版只有常态底图）。</summary>
        private const float HoverBlend = 0.15f;

        // ★ CR-T2：本面板原先自存的「卡 `key` → `ui_spells_out` 帧号」表（60 条，依据 = 原版
        // `原版资源/sc/ui_spells_v215.sc` 的 95 条 export 名 → `frame_NNN`，登记在
        // `策划/原版UI素材名称索引.md` §3.5）已**整体上收到 `CrUiStyle.CardArtFrameTable`**（唯一真源）。
        // 原因：`HudPanel` 也曾自存一份 35 条的副本，本面板扩到 60 条时那份没同步 ⇒ 对局手牌里 24 张卡
        // 查不到帧号、只画「卡槽底 + 卡名」。两张表并存 = 必然漂移，所以只留一处。
        // 唯一缺口 `goblin-hut`（原版 95 条 export 里没有 `goblin_hut` 命名）用哨兵
        // `CrUiStyle.CardArtFrameMissing` 表达，判定走 `CrUiStyle.IsCardArtKnownGap`。

        // ═══════════════ 运行时状态 ═══════════════

        private bool _built;
        private int _maxSelected;                       // 服务端卡组张数（来自 PanelArgs，见 ResolveMaxSelected）
        private int _page;                              // 当前卡池页（0 基）

        private CardInfo[] _pool;                       // 60 张卡池（可达 null：还没拉到）
        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();
        private readonly List<int> _selected = new List<int>();  // 当前选中（按点选顺序）

        private RectTransform _content;                 // 内容框（所有元素的父节点）
        private RectTransform _gridRoot;                // 卡池格子的容器
        private readonly List<Cell> _cells = new List<Cell>();       // 60 个卡池格（整池一次建好，翻页只切 active）
        private readonly List<Cell> _slotCells = new List<Cell>();

        private Text _selectedLabel;
        private Text _pageLabel;
        private Text _status;

        private bool _emittingSave;                     // 正在发保存请求（用来识别本类自己那条 Changed 的回声）
        private bool _busy;                             // 保存在途（界面侧；权威在途闸是 DeckManager._saving）
        private Button _saveButton;                     // 「保 存」按钮（在途时置 interactable=false）
        private int _lastUnknownType = int.MinValue;    // 未知类型只告警一次（记录上一次报过的值）
        private bool _poolTooBigWarned;                 // 「卡池比预建格子多」只告警一次

        private Action<CardInfo[]> _onPoolLoaded;
        private Action<int[]> _onDeckChanged;
        private Action<string> _onSaveFailed;

        /// <summary>一个格子（卡池格 / 已选槽位共用）的句柄：底图 + 卡面 + 名字 + 圣水数字。</summary>
        private sealed class Cell
        {
            public Image Chassis;      // 原版白色卡片底（九宫格）
            public Image Art;          // 卡面（原版素材帧；没登记帧号的卡为 null）
            public Text Name;
            public Text Elixir;

            /// <summary>
            /// 本格当前应有的色调。为什么要存下来：卡面的 Sprite 是**异步**到达的，到货时会重设 color
            /// ⇒ 必须由回调重新贴回当前状态色，否则刚选中的格子在素材到货后又变回原色。
            /// </summary>
            public Color Tint = Color.white;
        }

        /// <summary>卡组编辑是 `MainMenu` 的子面板（架构契约 §4 ⇒ Popup 层）。</summary>
        public override UILayer Layer => UILayer.Popup;

        public override void OnOpen(object param)
        {
            // ⚠️ 顺序：先解析张数（Build 要用它决定槽位数），再建树，最后订阅。
            ResolveMaxSelected(param);

            if (!_built)
            {
                Build();
                _built = true;
            }

            // CR-F2：面板重开时把「保存在途」标记归零。
            //    为什么要归零而不是原样保留：`DeckManager` 才是*权威*的在途闸（`DeckManager._saving`），
            //    面板这个 `_busy` 只负责界面；若上一次保存的 `SaveFailed` 是在面板关闭期间到达的
            //    （面板已 `Unsubscribe`，没人清标记），原样保留会让按钮**永久禁用**、玩家再也存不了卡组。
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "面板重开：上一次保存在途标记未清（失败回调在面板关闭期间到达？），已重置");
                SetSaveBusy(false);
            }
            SyncSaveButton();

            Subscribe();
            RefreshSlots();
            RefreshCells();
            RefreshPageLabel();
            SetStatus("点卡池里的卡选进卡组；点已选槽位可移除。最多 8 张、同一张只能带一次。",
                CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
        }

        private void ResolveMaxSelected(object param)
        {
            var args = param as PanelArgs;
            if (args != null && args.MaxSelected > 0)
            {
                _maxSelected = args.MaxSelected;
                return;
            }

            _maxSelected = FallbackMaxSelected;
            // 非预期分支：绕过 DeckManager 打开了面板。留痕（不静默），但界面照样能用。
            Game.Logger?.Warn(Tag,
                $"打开参数缺失 / 非法（MaxSelected={(args == null ? "args=null" : args.MaxSelected.ToString())}），" +
                $"按兜底 {FallbackMaxSelected} 张工作；正常路径应由 DeckManager 经 PanelArgs 递入");
        }

        // ───────────────────────── 视觉树（D1：OnOpen 里用 UIFactory 自建） ─────────────────────────

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            // 面板底 = **居中弹窗**（014 板岩外框 + 019 亮面体）、宽 1000（竖版口径 ≤1000）、高由内容倒推。
            // 坐标原点 = **亮面体左上角**（`CrUiStyle.SettingsPopup` 的 body 锚点在左上），y 为负 = 向下。
            float ySel = -(TitleRowH + GapM);                                            // −108
            float ySlots = ySel - CrUiStyle.LabelH - GapS;                               // −164
            float yPool = ySlots - SlotH - GapM;                                         // −335
            float yGrid = yPool - 36f - GapS;                                            // −383
            float yStatus = yGrid - GridH - GapM;                                        // −1157
            // 状态行**垫一条板岩盘**再放字（与主菜单 StatusBar 同构）：状态色（TextDim 浅灰 / Accent 金 /
            // ErrorText 浅红）是为暗底设计的，直接压亮面体 (229,236,242) 上对比度极低（AP1 首版实机实测）。
            // ⚠️ 左上角锚点 + anchoredPosition ⇒ y 越接近 0 越靠上：盘的顶边写在字的顶边**之上**（`+ GapS`）。
            float statusPlateY = yStatus + GapS;                                         // −1145
            float statusPlateH = 44f + 2f * GapS;                                        // 68
            float yPrev = statusPlateY - statusPlateH - GapS;                            // −1225
            float yNext = yPrev - PageBtnH - GapS;                                       // −1309
            float ySave = yNext - PageBtnH - GapM;                                       // −1373
            float yCancel = ySave - BtnH - GapS;                                         // −1451
            float boxH = -yCancel + BtnH + GapL;                                         // 1549（亮面体高）

            var box = CrUiStyle.SettingsPopup("DeckBox", root, boxH + CrUiStyle.PopupTitleH, BoxW);
            var c = box.rectTransform.Find("DeckBoxBody") as RectTransform;
            if (c == null)
            {
                // 非预期分支（必须留痕）：亮面体没建出来 ⇒ 后面的元素全落空。
                Game.Logger?.Error(Tag, "弹窗亮面体 DeckBoxBody 没建出来，卡组编辑内容无法摆放");
                return;
            }
            _content = c;

            // 标题：板岩带上的白字黑描边（A 原版 UI 的标题语言；⛔ 不用旧的金色标题条 069）。
            CrUiStyle.BandTitle("Title", box, "卡 组 编 辑", BodyW);

            // 亮面体上的字一律用**暗色**（`TextOnLight` / `TextOnLightDim`）：亮底 + 亮字 = 看不见
            // （AM2 在 24_设置 上给的解法；AP1 首版实机截图实测过）。
            _selectedLabel = UIFactory.CreateLabel("SelectedLabel", c, "已选", CrUiStyle.FontBody,
                new Vector2(InsetX, ySel), new Vector2(InnerW, CrUiStyle.LabelH), TextAnchor.MiddleLeft,
                CrUiStyle.TextOnLight);

            BuildSlots(ySlots);

            _pageLabel = UIFactory.CreateLabel("PoolLabel", c, "卡池", CrUiStyle.FontSmall,
                new Vector2(InsetX, yPool), new Vector2(InnerW, 36f), TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);

            BuildGrid(yGrid);

            CrUiStyle.Skin("StatusPlate", c, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(InsetX, statusPlateY),
                new Vector2(InnerW, statusPlateH), CrUiStyle.BandSlate, false);
            _status = UIFactory.CreateLabel("Status", c, string.Empty, CrUiStyle.FontSmall,
                new Vector2(InsetX + 16f, yStatus), new Vector2(InnerW - 32f, 44f), TextAnchor.UpperLeft,
                CrUiStyle.TextDim);

            // ⛔ 竖版：标签/控件/按钮**不并排** —— 分页与保存各占整行。
            // 按钮 = 原版**蓝底白字**（`ui_out` 165，A 12_主菜单的 `Clan` / 蓝 ribbon 语言）
            // ⇒ ⛔ 不用旧的金色主按钮（`ui_out` 300，那是商店 / 宝箱的语言）。
            CrUiStyle.BlueButton("PrevButton", c, "上一页", new Vector2(InsetX, yPrev),
                new Vector2(InnerW, PageBtnH), OnPrevPage);
            CrUiStyle.BlueButton("NextButton", c, "下一页", new Vector2(InsetX, yNext),
                new Vector2(InnerW, PageBtnH), OnNextPage);
            // CR-F2：保存按钮句柄留下来 —— 在途时必须**真的禁用**（见 `SetSaveBusy`）。
            _saveButton = CrUiStyle.BlueButton("SaveButton", c, "保 存", new Vector2((BodyW - BtnW) * 0.5f, ySave),
                new Vector2(BtnW, BtnH), OnSaveClicked).GetComponent<Button>();
            CrUiStyle.BlueButton("CancelButton", c, "取 消", new Vector2((BodyW - BtnW) * 0.5f, yCancel),
                new Vector2(BtnW, BtnH), OnCancelClicked);
        }

        /// <summary>已选槽位：一行 8 格（原版 07 的卡格宽高比），每格可点（点 = 从卡组里移除这张）。</summary>
        private void BuildSlots(float ySlots)
        {
            var count = SlotCount;
            var totalW = count * SlotW + (count - 1) * SlotGap;   // 8×95 + 7×32 = 984 ≤ 亮面体 996
            var x0 = (BodyW - totalW) * 0.5f;                     // 6

            for (var i = 0; i < count; i++)
            {
                var index = i; // 闭包捕获：每个槽位记自己的下标
                var pos = new Vector2(x0 + i * (SlotW + SlotGap), ySlots);
                _slotCells.Add(CreateCell($"Slot{index}", _content, pos, new Vector2(SlotW, SlotH),
                    () => OnSlotClicked(index), true));
            }
        }

        /// <summary>
        /// 卡池格子：整池一次建好（60 个格子），翻页只切 `SetActive` —— 不反复销毁重建节点。
        /// 行容器用 <see cref="UIFactory.AnchoredTopLeft"/> 定位（左上角口径，与引擎控件一致）。
        /// </summary>
        private void BuildGrid(float yGrid)
        {
            _gridRoot = UIFactory.CreateNode("PoolGrid", _content);
            UIFactory.AnchoredTopLeft(_gridRoot, new Vector2(GridX, yGrid), new Vector2(GridW, GridH));

            for (var i = 0; i < 60; i++)
            {
                var slotInPage = i % PageSize;
                var col = slotInPage % Columns;
                var row = slotInPage / Columns;
                var pos = new Vector2(col * CardStepX, -row * CardStepY);
                var index = i;
                _cells.Add(CreateCell($"Card{index}", _gridRoot, pos, new Vector2(CardW, CardH),
                    () => OnPoolCellClicked(index), false));
            }
        }

        /// <summary>槽位数 = 卡组张数（夹到 <see cref="DefaultSlotCount"/>：槽位是在 OnOpen 里按张数建好的，张数变了不重建节点）。</summary>
        private int SlotCount => Mathf.Clamp(_maxSelected > 0 ? _maxSelected : DefaultSlotCount, 1, DefaultSlotCount);

        /// <summary>
        /// 建一个可点的格子：原版白色卡片底（<see cref="ResPaths.SlotCard"/> 九宫格）+ 卡面 + 名字 + 圣水数字。
        /// 底图不是纯色块，也不是把原图直接拉伸（九宫格切边 = 20，见 G3-量取.md C 段）。
        /// <para>
        /// <b>AP1 判定「卡组槽底 / 卡池格底」= <see cref="ResPaths.SlotCard"/>（`ui_out` 43，107×159）保持不变</b>：
        /// 它**就是 A 的原版白色卡底**（联络图 `.ai-tmp/test/AP1-frames-sheet.png` 第 14 格 =
        /// 白色圆角卡片剪影，与基线 `07_卡组编辑` 每张卡背面的卡形一致）⇒ 不是错帧、没有可换的替代帧。
        /// ⚠️ 与基线的**已知差异**：原版卡格带**按稀有度**的彩色卡框（紫/橙/灰，基线 07 可见），
        /// 本项目已落地的图元里**没有**成套的稀有度卡框 ⇒ 统一用白色卡底，登记在 `AP1-允许差异.md`。
        /// </para>
        /// </summary>
        private Cell CreateCell(string name, Transform parent, Vector2 pos, Vector2 size, Action onClick, bool isSlot)
        {
            var cell = new Cell();

            // 卡片底：原版白色卡片底九宫格（107×159 的圆角卡形 ⇒ 切边 20 保住圆角）
            cell.Chassis = CrUiStyle.NineSlice(name, parent, ResPaths.SlotCard, BorderCard,
                new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, CrUiStyle.PanelBg, true);

            var btn = cell.Chassis.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell.Chassis;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            // 四态一律以**白**为基准（常态 = 白 = 原版图元原色；hover/pressed 只在亮度上差一点）。
            // ⛔ 不用底色染整格 —— 底色会给原版卡片底加滤镜。
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = CrUiStyle.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = CrUiStyle.ButtonFade;
            btn.colors = colors;

            // 卡面（素材到达后按帧号换成裁剪过的 Sprite；未登记帧号的卡这一格保持 null = 不画）
            cell.Art = UIFactory.CreatePanel(name + "Art", cell.Chassis.rectTransform, CrUiStyle.PanelBg, false);
            // ⚠️ 修正（V4 取证实测 2026-09-21）：原实现**恒用** ArtW/ArtH = 188×239（按卡池格宽 206 定的）
            // ⇒ 已选槽位（SlotW=96）里的卡面比槽位还宽：`Slot0Art` 实测 px=(-5.5,337)-(182.5,576)
            //    **出画布 5.5px**（审计 ASSERT offCanvasNodes=1 FAIL）。按格宽派生即两处都对：
            //    卡池格 206−18=188（与原值一致）、槽位 96−18=78。
            // ⚠️ AQ1：内边距由 **9 改为 `ArtInsetX`（= 6，实测值）** —— 旧值 9 是自定值（4.39%），比原版 3.05% 大。
            var artW = size.x - 2f * ArtInsetX;
            var artH = artW * (CrUiStyle.CardArtBboxH / CrUiStyle.CardArtBboxW);
            UIFactory.Place(cell.Art.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -ArtTop), new Vector2(artW, artH));
            cell.Art.gameObject.SetActive(false);

            // 圣水水滴（原版图标）+ 数字
            if (!isSlot)
            {
                CrUiStyle.AspectImage(name + "ElixirDrop", cell.Chassis.rectTransform, ResPaths.IconElixirDrop,
                    BadgeW, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BadgeX, -(ArtTop + 2f)),
                    CrUiStyle.FieldBg, false);
            }

            // 卡格底是**原版白色卡底**（43）⇒ 格上的字必须用**暗色**，否则白字压白卡 = 看不见
            // （AP1 首版实机实测；AM2 在 24_设置 亮面上给过同一结论）。
            cell.Elixir = UIFactory.CreateLabel(name + "Elixir", cell.Chassis.rectTransform, string.Empty,
                CrUiStyle.FontSmall, new Vector2(BadgeX + (isSlot ? 0f : BadgeW), -(ArtTop + 2f)),
                new Vector2(BadgeH + 20f, BadgeH), TextAnchor.MiddleLeft, CrUiStyle.TextOnLight);

            // 名字带（原版卡的 "Level 11" 带位置；本项目没有卡等级 ⇒ 显示中文名）
            cell.Name = UIFactory.CreateLabel(name + "Name", cell.Chassis.rectTransform, string.Empty,
                CrUiStyle.FontSmall, new Vector2(0f, -(size.y - NameBandH)),
                new Vector2(size.x, NameBandH), TextAnchor.MiddleCenter, CrUiStyle.TextOnLight);

            if (cell.Chassis.GetComponent<Button>() == null)
            {
                // 非预期分支：AddComponent 没挂上 ⇒ 格子点不动且无线索。
                Game.Logger?.Error(Tag, $"格子 {name} 上没有 Button 组件，点选会失效");
            }
            return cell;
        }

        /// <summary>原版白色卡片底（<see cref="ResPaths.SlotCard"/>，107×159）的九宫格切边：四边各 20（实测，见 G3-量取.md）。</summary>
        private static readonly Vector4 BorderCard = new Vector4(20f, 20f, 20f, 20f);

        // ───────────────────────── 交互 ─────────────────────────

        private void OnPoolCellClicked(int index)
        {
            if (_pool == null || index < 0 || index >= _pool.Length)
            {
                Game.Logger?.Warn(Tag, $"卡池格子下标越界（index={index}，卡池数={_pool?.Length ?? 0}），已忽略");
                return;
            }

            var card = _pool[index];
            if (card == null) { Game.Logger?.Warn(Tag, $"卡池第 {index} 项为空，已忽略"); return; }

            Toggle(card.id);
        }

        private void OnSlotClicked(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _selected.Count) return; // 空槽位：无事发生
            Toggle(_selected[slotIndex]);
        }

        private void Toggle(int cardId)
        {
            var at = _selected.IndexOf(cardId);
            if (at >= 0)
            {
                _selected.RemoveAt(at);
                RefreshSlots();
                RefreshCells();
                SetStatus($"已移除「{CardName(cardId)}」（当前 {_selected.Count}/{_maxSelected} 张）", CrUiStyle.TextDim);
                return;
            }

            if (_selected.Count >= _maxSelected)
            {
                SetStatus($"最多只能带 {_maxSelected} 张 —— 先点上面的已选槽位移除一张再加", CrUiStyle.ErrorText);
                return;
            }

            _selected.Add(cardId);
            RefreshSlots();
            RefreshCells();
            var card = FindCard(cardId);
            var kind = card != null ? TypeName(card.type) : "未知";
            SetStatus($"已选入「{CardName(cardId)}」（{kind}，圣水{(card != null ? card.elixir : 0)}，当前 {_selected.Count}/{_maxSelected} 张）",
                CrUiStyle.TextDim);
        }

        private void OnPrevPage()
        {
            if (_page <= 0) { SetStatus("已经是第一页", CrUiStyle.TextDim); return; }
            _page--;
            RefreshCells();
            RefreshPageLabel();
        }

        private void OnNextPage()
        {
            if (_page >= PageCount - 1) { SetStatus("已经是最后一页", CrUiStyle.TextDim); return; }
            _page++;
            RefreshCells();
            RefreshPageLabel();
        }

        /// <summary>
        /// 保存：**客户端先校验**（张数 / 不重复 / 都在卡池里），不合法就**一个字节都不发**，
        /// 原因显示在面板上（⛔ 不许只打日志）。校验通过后 `Emit(Events.Deck.Changed, ids)` ——
        /// 这就是本片与 `DeckManager` 约定的"保存请求"通道（见类注释）。
        /// 服务端仍会再校验一次，它才是权威（客户端拦一道只是为了不让明知非法的请求白跑一趟）。
        /// </summary>
        private void OnSaveClicked()
        {
            if (_busy)
            {
                // 期望分支（连点）：留痕即可计数（ASCII 标记 ⇒ 数值类判据）。
                Game.Logger?.Info(Tag, "[Deck] SAVE-DUP 上一次保存在途，忽略本次点击（未提交，未发 SaveDeck）");
                SetStatus("上一次保存还在进行中，请稍候", CrUiStyle.Accent);
                return;
            }

            var reason = ValidateSelection();
            if (reason != null)
            {
                Game.Logger?.Warn(Tag, "本地校验未通过，未提交保存：" + reason);
                SetStatus(reason, CrUiStyle.ErrorText);
                return;
            }

            // ⚠️ 这条日志是**防重入的运行时判据**（数值类 L3）：连点 N 次后它必须只出现 1 次
            //    —— 判的是"提交了几次请求"（过程），不是"最后存没存上"（结果）。
            SetSaveBusy(true);
            var ids = _selected.ToArray();
            Game.Logger?.Info(Tag, $"[Deck] SAVE-SEND ids={ids.Length}（事件 {Events.Deck.Changed}）");
            SetStatus("已提交保存请求；服务端确认后会显示最新卡组，失败会显示原因", CrUiStyle.Accent);

            // `_emittingSave` 标记：`Emit` 是**同步分发**，本类自己的订阅会立刻收到这条事件，
            // 用它区分"我自己发的请求的回声"与"管理器通知的权威卡组"（见 OnDeckChanged）。
            _emittingSave = true;
            try
            {
                Game.Event?.Emit(Events.Deck.Changed, ids);
            }
            finally
            {
                _emittingSave = false;
            }
        }

        private void OnCancelClicked()
        {
            // 关闭面板（Popup 层；设置面板同一做法，见 SettingsPanel.cs 的关闭按钮）。
            // 本地选择不落盘：没保存过就没有任何副作用。
            Game.Logger?.Info(Tag, "取消卡组编辑（未保存的改动丢弃）");
            Game.UI.Close<DeckEditPanel>();
        }

        // ───────────────────────── 校验与刷新 ─────────────────────────

        /// <summary>返回 null = 合法；否则返回**给玩家看的原因**。口径与服务端 `logic/deck.go` 的 checkDeck 一致：8 张 / 不重复 / 存在。</summary>
        private string ValidateSelection()
        {
            if (_maxSelected <= 0) return "卡组张数未知（打开参数缺失），无法保存";
            if (_pool == null) return "卡池还没加载好（等一下再试，或检查服务端是否在跑）";
            if (_selected.Count != _maxSelected)
                return $"卡组必须正好 {_maxSelected} 张（当前 {_selected.Count} 张）";

            var seen = new HashSet<int>();
            for (var i = 0; i < _selected.Count; i++)
            {
                var id = _selected[i];
                if (!seen.Add(id)) return $"「{CardName(id)}」重复了（同一张卡只能带一张）";
                if (!_cards.ContainsKey(id)) return $"卡 id={id} 不在卡池里（数据异常，请关掉面板重开）";
            }
            return null;
        }

        private void OnPoolLoaded(CardInfo[] cards)
        {
            _pool = cards ?? Array.Empty<CardInfo>();
            _cards.Clear();
            for (var i = 0; i < _pool.Length; i++)
            {
                var card = _pool[i];
                if (card == null) continue;
                if (_cards.ContainsKey(card.id))
                {
                    // 非预期分支：卡池里出现重复 id（配表重复 / 服务端拼装出错）。留痕。
                    Game.Logger?.Warn(Tag, $"卡池里有重复的卡 id={card.id}（后到的覆盖先到的）");
                }
                _cards[card.id] = card;
            }

            Game.Logger?.Info(Tag, $"卡池已到达 {_pool.Length} 张（登记 id {_cards.Count} 个）");
            if (_pool.Length > _cells.Count && !_poolTooBigWarned)
            {
                // 非预期分支：卡池比本面板预建的格子多（协议口径是 60 张，见 registry.md 的 card_cs = 60 行）。
                // 留痕：多出来的卡**不会显示**，否则表现为"某张卡在卡池里找不到"。
                _poolTooBigWarned = true;
                Game.Logger?.Warn(Tag,
                    $"卡池 {_pool.Length} 张 > 面板预建的 {_cells.Count} 个格子，多出的卡不显示（卡池规模变了？）");
            }
            RefreshCells();
            RefreshSlots();   // 卡池到达前选中的槽位显示的是占位文案，这里补上真名
            // ⚠️ 修正（V4 取证实测 2026-09-21）：这里漏了页签行 —— `OnOpen` 里 `RefreshPageLabel` 跑在
            // 卡池到达**之前**（那时 `_pool` 还是 null）⇒ 卡池到货后页签一直停在「卡池：共 0 张 · 第 1/1 页」，
            // 而屏幕上明明画着 60 张卡（V4 截图实测到的可见错数）。补一行即可与真实卡池一致。
            RefreshPageLabel();
        }

        /// <summary>
        /// 卡组（服务端确认过的）到达。★ 双通道识别见 <see cref="OnSaveClicked"/>：
        /// <c>_emittingSave</c> 为真 ⇒ 这是本类刚发出的"保存请求"被自己的订阅收到了，只刷新显示；
        /// 否则 ⇒ 管理器通知的权威卡组（打开面板时的初始态 / 保存成功后的确认），据此更新状态行。
        /// </summary>
        private void OnDeckChanged(int[] ids)
        {
            var list = ids ?? Array.Empty<int>();
            _selected.Clear();
            for (var i = 0; i < list.Length; i++)
            {
                var id = list[i];
                if (_cards.Count > 0 && !_cards.ContainsKey(id))
                {
                    Game.Logger?.Warn(Tag, $"服务端卡组里的卡 id={id} 不在卡池里（配表/卡池不一致），仍按 id 显示");
                }
                _selected.Add(id);
            }

            RefreshSlots();
            RefreshCells();

            if (_emittingSave) return;

            SetSaveBusy(false);
            if (list.Length == _maxSelected)
            {
                SetStatus($"卡组已保存（服务端确认 {list.Length} 张）", CrUiStyle.Accent);
            }
            else
            {
                SetStatus($"当前卡组 {list.Length} 张（还没存满 {_maxSelected} 张，选满后点「保存」）", CrUiStyle.TextDim);
            }
        }

        private void OnSaveFailed(string reason)
        {
            SetSaveBusy(false);
            SetStatus(string.IsNullOrEmpty(reason) ? "保存失败（服务端未给出原因）" : reason, CrUiStyle.ErrorText);
        }

        /// <summary>
        /// 设置「保存在途」并**真的禁用保存按钮**（CR-F2 修复）。
        ///
        /// <para>
        /// <b>根因</b>：原实现只把 `_busy` 置位、只改状态行文案，按钮本身仍 `interactable = true`
        /// ⇒ 玩家看到一颗"能点"的按钮、点下去只换来一句"上一次保存还在进行中"，而且在途期间
        /// 反复点击会反复走 `OnSaveClicked`（今天靠 `_busy` 早退挡住，但界面上完全看不出来）。
        /// 现在两件事同时做：`_busy` 挡逻辑重入 + 按钮禁用给视觉反馈。
        /// ⛔ 两者都要留：`interactable = false` 只拦**真实指针点击**，脚本 / 快捷键直接 `onClick.Invoke()`
        /// 仍会进来 ⇒ `_busy` 那道判断不能被按钮禁用取代。
        /// </para>
        /// </summary>
        private void SetSaveBusy(bool busy)
        {
            _busy = busy;
            SyncSaveButton();
        }

        /// <summary>把按钮的可用态同步到 `_busy`（不改标记本身；`OnOpen` 重开面板时用）。</summary>
        private void SyncSaveButton()
        {
            if (_saveButton != null) _saveButton.interactable = !_busy;
        }

        // ───────────────────────── 订阅（OnOpen 挂 / OnClose 摘，成对） ─────────────────────────

        private void Subscribe()
        {
            if (_onPoolLoaded == null)
            {
                _onPoolLoaded = OnPoolLoaded;
                _onDeckChanged = OnDeckChanged;
                _onSaveFailed = OnSaveFailed;
            }

            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger?.Error(Tag, "Game.Event 为空（引擎未 Launch？），卡组面板收不到任何数据");
                return;
            }

            // 幂等：UIManager 对已打开的面板会再次调用 OnOpen（UI.cs:107-117），重复 On 会让一次失败走两遍处理。
            bus.Off(Events.Deck.PoolLoaded, _onPoolLoaded);
            bus.Off(Events.Deck.Changed, _onDeckChanged);
            bus.Off(Events.Deck.SaveFailed, _onSaveFailed);
            bus.On(Events.Deck.PoolLoaded, _onPoolLoaded);
            bus.On(Events.Deck.Changed, _onDeckChanged);
            bus.On(Events.Deck.SaveFailed, _onSaveFailed);
        }

        private void Unsubscribe()
        {
            if (_onPoolLoaded == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Deck.PoolLoaded, _onPoolLoaded);
            bus?.Off(Events.Deck.Changed, _onDeckChanged);
            bus?.Off(Events.Deck.SaveFailed, _onSaveFailed);
        }

        // ───────────────────────── 显示刷新 ─────────────────────────

        private void RefreshSlots()
        {
            for (var i = 0; i < _slotCells.Count; i++)
            {
                var occupied = i < _selected.Count;
                var cell = _slotCells[i];
                if (occupied)
                {
                    var card = FindCard(_selected[i]);
                    SetCell(cell, card, _selected[i], false);
                    SetInteractable(cell, true);
                }
                else
                {
                    ClearCell(cell, "空");
                    SetInteractable(cell, false);
                }
            }

            if (_selectedLabel != null)
            {
                _selectedLabel.text = $"已选 {_selected.Count} / {_maxSelected}（点槽位移除）";
            }
        }

        private void RefreshCells()
        {
            var count = _pool?.Length ?? 0;
            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null || cell.Chassis == null) continue;

                if (i >= count || i / PageSize != _page)
                {
                    cell.Chassis.gameObject.SetActive(false);
                    continue;
                }

                var card = _pool[i];
                cell.Chassis.gameObject.SetActive(card != null);
                if (card == null) continue;

                var selected = _selected.Contains(card.id);
                SetCell(cell, card, card.id, selected);
                SetInteractable(cell, true);
            }
        }

        /// <summary>把卡的数据写进一个格子（名字 / 圣水 / 卡面）。已选 = 卡面染暖金色 + 名字用强调色。</summary>
        private void SetCell(Cell cell, CardInfo card, int cardId, bool selected)
        {
            if (cell == null) return;

            // 选中态：只染**卡面**（常态 = 白 = 原图原色；这是状态表达，不是给素材加滤镜）。
            cell.Tint = selected ? new Color(1f, 0.93f, 0.72f, 1f) : Color.white;

            if (card == null)
            {
                ClearCell(cell, $"卡 {cardId}");
                return;
            }

            if (cell.Name != null)
            {
                cell.Name.text = (selected ? "[已选] " : string.Empty) + card.name_cn;
                cell.Name.color = selected ? CrUiStyle.Accent : CrUiStyle.TextColor;
            }
            if (cell.Elixir != null)
            {
                cell.Elixir.text = card.elixir.ToString();
                // 压在白卡底上 ⇒ 未选 = 亮面上的暗字；已选 = 金色强调（原版卡面的圣水数字是彩色的）
                cell.Elixir.color = selected ? CrUiStyle.Accent : CrUiStyle.TextOnLight;
            }
            LoadArt(cell, card.key);
        }

        private void ClearCell(Cell cell, string label)
        {
            if (cell == null) return;
            cell.Tint = Color.white;
            if (cell.Name != null)
            {
                cell.Name.text = label;
                cell.Name.color = CrUiStyle.TextOnLightDim;   // 压白卡底 ⇒ 亮面上的**次要**暗字
            }
            if (cell.Elixir != null)
            {
                cell.Elixir.text = string.Empty;
                cell.Elixir.color = CrUiStyle.TextOnLightDim;
            }
            if (cell.Art != null)
            {
                cell.Art.color = Color.white;
                cell.Art.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 卡面：按 <see cref="CrUiStyle.TryGetCardArtFrame"/> 查到帧号才画（⛔ 查不到就不画 ——
        /// 不按类型涂色、也不猜帧号）；帧本身的透明边由 <see cref="CrUiStyle.CropCardArt"/> 裁掉，
        /// 否则一半是空白。
        /// </summary>
        private void LoadArt(Cell cell, string cardKey)
        {
            if (cell == null || cell.Art == null) return;

            int artIndex;
            if (!CrUiStyle.TryGetCardArtFrame(cardKey, out artIndex))
            {
                // 非预期分支（**必须留痕**）：该格只剩白色卡片底。两种原因必须能从日志一眼分开：
                // ① 已知缺口（本表登记了哨兵 -1，原版 ui_spells 无对应 export 名，当前只有 `goblin-hut`）；
                // ② 新卡没跟上帧号表（卡池与 `CrUiStyle.CardArtFrameTable` 脱节）。
                if (!string.IsNullOrEmpty(cardKey))
                {
                    WarnOnce("卡「" + cardKey + "」在帧号表（CrUiStyle.CardArtFrameTable）里"
                        + (CrUiStyle.IsCardArtKnownGap(cardKey)
                            ? "是**已知缺口**（原版 ui_spells 无对应 export 命名）"
                            : "**没有登记帧号**（卡池与表脱节）")
                        + " ⇒ 该格只有白色卡片底，没有卡面");
                }
                cell.Art.gameObject.SetActive(false);
                return;
            }

            cell.Art.gameObject.SetActive(true);
            var path = ResPaths.SpellArtFrame(artIndex);
            LoadArtSprite(artIndex, path, sprite =>
            {
                if (cell.Art == null) return;
                cell.Art.sprite = sprite;
                cell.Art.type = Image.Type.Simple;
                cell.Art.preserveAspect = false;   // 裁剪后的 rect 已是素材自身比例，再按比例缩会再留边
                cell.Art.color = cell.Tint;        // ★ 素材异步到货 ⇒ 贴回当前状态色（见 Cell.Tint 的注释）
            });
        }

        /// <summary>(帧号 → 裁剪后的 Sprite)。`Sprite.Create` 造的 Sprite 不归 Resources 管，必须复用。</summary>
        private static readonly Dictionary<int, Sprite> ArtCache = new Dictionary<int, Sprite>();

        /// <summary>已经 Warn 过的卡面路径（缺素材只报一次，避免每次开面板刷屏）。</summary>
        private static readonly HashSet<string> WarnedMissing = new HashSet<string>();

        private static void LoadArtSprite(int artIndex, string resPath, Action<Sprite> onLoaded)
        {
            Sprite cached;
            if (ArtCache.TryGetValue(artIndex, out cached) && cached != null)
            {
                onLoaded(cached);
                return;
            }

            if (Game.Res == null)
            {
                // 非预期分支：CloverRes.Init 缺失 ⇒ 卡面加载不到。留痕（只报一次）。
                WarnOnce("Game.Res 为空（漏了 CloverRes.Init？），卡面加载不了，格子只剩原版卡片底");
                return;
            }

            Game.Res.LoadAsset<Sprite>(resPath, sprite =>
            {
                if (sprite == null)
                {
                    // 素材没进工程 / 没导入为 Sprite（Texture Type 不是 Sprite 时取到 null）。
                    WarnOnce("卡面素材加载不到（该格只画卡片底）：" + resPath);
                    return;
                }

                // 裁剪口径 = `CrUiStyle.CropCardArt`（含 `frame_022`/knight 的 x 偏移例外），
                // 与 `HudPanel` **同一处** —— ⛔ 原先各写一份，改一处必然漂移。
                var made = CrUiStyle.CropCardArt(sprite, artIndex);
                if (made == null)
                {
                    // 非预期分支：纹理裁不出合法矩形（该格只画卡片底）。留痕（只报一次）。
                    WarnOnce("卡面帧裁不出合法矩形（该格只画卡片底）：" + resPath);
                    return;
                }
                ArtCache[artIndex] = made;
                onLoaded(made);
            });
        }

        private static void WarnOnce(string message)
        {
            if (!WarnedMissing.Add(message)) return;
            Game.Logger?.Warn(Tag, message + "（本条只报一次；已登记进 策划/验收表.md §3「允许的差异」）");
        }

        private void RefreshPageLabel()
        {
            if (_pageLabel == null) return;
            var count = _pool?.Length ?? 0;
            _pageLabel.text = $"卡池：共 {count} 张 · 第 {_page + 1}/{PageCount} 页（每页 {PageSize} 张）";
        }

        private int PageCount => Mathf.Max(1, Mathf.CeilToInt((_pool?.Length ?? 0) / (float)PageSize));

        /// <summary>类型名（`CardInfo.type`：0=部队 1=法术 2=建筑，见 `Def/ProtoDef.cs`）。</summary>
        private string TypeName(int type)
        {
            switch (type)
            {
                case 0: return "部队";
                case 1: return "法术";
                case 2: return "建筑";
                default:
                    // 非预期分支：配表里出现了协议注释外的类型取值（或服务端改了枚举）。留痕（同一值只报一次）。
                    if (_lastUnknownType != type)
                    {
                        _lastUnknownType = type;
                        Game.Logger?.Warn(Tag, $"未知的卡牌类型 type={type}（协议只定义 0=部队 1=法术 2=建筑），按「未知」显示");
                    }
                    return "未知";
            }
        }

        /// <summary>设置格子可点性（空槽位不可点；已选的格可点 = 移除）。色态由 Button 的四态负责，不改底色。</summary>
        private static void SetInteractable(Cell cell, bool interactable)
        {
            if (cell == null || cell.Chassis == null) return;
            var btn = cell.Chassis.GetComponent<Button>();
            if (btn != null) btn.interactable = interactable;
        }

        private CardInfo FindCard(int id)
        {
            return _cards.TryGetValue(id, out var card) ? card : null;
        }

        /// <summary>取卡片中文名（卡池没到 / 不在卡池里时给出可读的占位，绝不给玩家看 null）。</summary>
        private string CardName(int id)
        {
            var card = FindCard(id);
            if (card != null && !string.IsNullOrEmpty(card.name_cn)) return card.name_cn;
            return $"卡 id={id}";
        }

        private void SetStatus(string text, Color color)
        {
            if (_status == null) return;
            _status.text = text ?? string.Empty;
            _status.color = color;
        }
    }
}
