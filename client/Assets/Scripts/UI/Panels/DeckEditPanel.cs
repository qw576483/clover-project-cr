using System;
using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 卡组编辑（`MainMenu` 站点的 Popup 子面板，架构契约 §4）：60 张卡池里选 8 张 + 保存 / 取消。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔不许 `using CR.Module` —— 它只
    /// `Emit(Events.Deck.*)` 并把管理器回来的数据画出来。三条通道：
    /// ① 读某个卡组号 = `Emit(Events.Deck.SlotRequest, slot)`（`-1` = 当前卡组号）⇒ 管理器回
    /// `Events.Deck.SlotLoaded`；② 保存 = `Emit(Events.Deck.Changed, {slot, ids})`，管理器按方向
    /// 区分同一条事件（它那边有重入保护），保存成功后再用同一条事件回执；
    /// ③ 失败 = `Events.Deck.SaveFailed(reason)`，两条都由本面板显示在底行状态行上。
    /// 点卡组号若落到「满 8 张的另一个号」，管理器会顺手把该号写成**当前卡组**（"切号即设为当前"，
    /// 进对局用的就是它）⇒ 面板随后会再收到一条 `Changed` 回执并把状态行刷成"已保存"。
    /// 详见 <see cref="OnSaveClicked"/>、<see cref="OnSlotLoaded"/> 与 <see cref="OnSaveFailed"/>。
    /// </para>
    ///
    /// <para>
    /// <b>竖版排版</b>：画布 = <see cref="CrUiStyle.DesignW"/>×<see cref="CrUiStyle.DesignH"/> = 1080×1920、`match = 0`。
    /// 面板底 = <see cref="CrUiStyle.SettingsPopup"/>（居中弹窗：`ui_out` 014 板岩外框 + 019 亮面体，
    /// 与主菜单 / 设置同语言；宽 = <see cref="CrUiStyle.ContentW"/> = 1000 ≤ 竖版口径上限 1000），
    /// 标题 = <see cref="CrUiStyle.BandTitle"/>（板岩带上的白字黑描边），
    /// 按钮 = <see cref="CrUiStyle.BlueButton"/>（`ui_out` 165 蓝底白字），
    /// 格子底 = <see cref="ResPaths.SlotCard"/>（原版白色卡片底九宫格，**经联络图复核后判定不改**，
    /// 见 <see cref="CreateCell"/> 的注释）。
    /// 全部几何数字量自原版基线图 `策划/参考图/07_卡组编辑_1242x2208.jpg`（折算 ×(1080/1242) = 0.8696，
    /// 与 1242×2208 → 1080×1920 同宽高比）。
    /// 卡宽 205 / 槽宽 95 为本面板的整屏版式取值（见下方版式常量）。
    /// </para>
    ///
    /// <para>
    /// <b>卡面（⛔ 不按类型上色）</b>：卡面 = 原版素材帧 `ResPaths.SpellArtFrame(i)`
    /// （`ui_spells_out`，403×377 画布、图的透明包围盒在 (98,0)-(295,251)）；
    /// 卡 key → 帧号 的对应表已上收到 <see cref="CrUiStyle.TryGetCardArtFrame"/>（唯一真源，
    /// 依据 = `策划/原版UI素材名称索引.md` §3.5 的原版 export 名 → `frame_NNN`）；
    /// 表里没有的卡**不猜帧号**，只画原版卡片底 + 名字/圣水。
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

        // ═══════ 原版整屏版式常量（出处 = 策划/参考图/几何量取.md **§1.4**） ═══════
        //
        // <b>版式 = 整屏，不是弹窗</b>：基线图 `07_卡组编辑_1242x2208.jpg` 上的原版是**整屏深蓝**
        // （卡池底实测 `(2,35,90)`）+ 顶部**页签带**（Collection 一颗）+ **卡组编号行**（1..5）；
        // ⛔ 不用「居中弹窗（014 板岩外框 + 019 亮面体）」那套（其原点在亮面体左上角、宽按亮面体 996 定）。
        // ⇒ 坐标系 = **整屏**：原点 = 屏幕**左上角**、y 向下走为负、宽 = <see cref="CrUiStyle.DesignW"/> = 1080。
        //
        // ⛔ 全部数字量自 `07_卡组编辑_1242x2208.jpg`（1242×2208 → 1080×1920 同宽高比 ⇒ 单一比例
        //    k = 1080/1242 = 0.8696），逐条编号 **E\*** 见 `策划/参考图/几何量取.md` **§1.4**
        //    —— 量法 = 「**整行中位数**」（对带内卡面内容鲁棒），逐带量取；
        //    原 §2 的 **C7** 把「Tab 带 / 编号行的精确边界」记为**量不到**，本节的 E\* 编号取代它。

        /// <summary>整屏深蓝底 —— 量取 **E18** <c>(2,35,90)</c>（`几何量取.md` §1.4 卡池底中位色）。</summary>
        private static readonly Color DeckBgColor = new Color32(2, 35, 90, 255);

        /// <summary>页签底填充（贴不到图元时的兜底色）= 量取 **E15** <c>(33,124,193)</c>（亮蓝）。</summary>
        private static readonly Color TabOnColor = new Color32(33, 124, 193, 255);

        /// <summary>页签带顶边 = **33.9**。出处 **E2**（y=39@1242；该处行中位色差 d=15.0）。</summary>
        private const float TabBarY = 33.9f;

        /// <summary>页签带高 = **102.6**。出处 **E4 − E2** = (157−39)×0.8696（E4 处 d=**236.0**，全图最硬边界之一）。</summary>
        private const float TabBarH = 102.6f;

        /// <summary>
        /// 页签左边 = **568.7**。出处 **E50**（原分辨率逐像素扫 y=55/100@1242，
        /// 填充色在 **x=654@1242** 从 `#072853` 跳到 `#0C3761` ⇒ 654 × 0.8696 = 568.7）。
        /// <para>
        /// ⚠️ 该边取**实测**值：⛔ 不能"由左页签右边 + 推定缝 40"反解（§2 **C7b** 已消除）。
        /// 填充 `#0C3761` 与顶区面板底部 `#0C325E` 只差 ~9/通道 ⇒ 中位色差通道会被淹没，
        /// 必须**在原分辨率上逐像素看跳变**才读得到（见 `几何量取.md` §3 复跑命令）。
        /// </para>
        /// </summary>
        private const float TabCollectionX = 568.7f;

        /// <summary>
        /// 页签宽 = **394.0**。出处 **E51**（右缘 **x=1107@1242** ⇒ 962.7@1080；
        /// 962.7 − 568.7 = 394.0）。两次独立确认：① y=55@1080 细扫在 x=964 从 `#0E3761` 掉到 `#072A57`；
        /// ② 减背景放大图上可**目视**看到右上圆角（放大 4×，圆角起弯 ≈950@1080、直边止于 ≈963）。
        /// </summary>
        /// <remarks>
        /// ⚠️ 这是**原版这颗页签自身**的实测宽（原版 "Collection" 字号比另一颗小一档 ⇒ 页签也更窄），
        /// ⛔ 不是对称假设、⛔ 不用"由两颗等宽反算"的推定值。
        /// </remarks>
        private const float TabWCollection = 394.0f;

        /// <summary>卡组编号行顶边 = **136.5**（= 量取 **E4**，即页签带的底边）。</summary>
        private const float NumRowY = 136.5f;

        /// <summary>卡组编号行高 = **152.2**。出处 **E7 − E4** = (332−157)×0.8696（E7 处 d=103.0）。</summary>
        private const float NumRowH = 152.2f;

        /// <summary>
        /// 卡格阵列第 1 行**主体**顶边 = **335.7**。出处 §1.1 **A4**（y=386@1242）+ §1.4 **E10**（同值）。
        /// ⚠️ 选中卡的**外发光**比主体再高 33（量取 **E9** = y=348@1242 ⇒ 302.6）—— 发光是卡框帧自身的
        /// 外扩，⛔ 不在几何里补偿（否则未选中的格子会整体上移 33）。
        /// </summary>
        private const float GridTopY = 335.7f;

        /// <summary>
        /// 卡格阵列**左**边界 = **71.2**。出处 **E14 = §1.1 A1**（第 1 格左边 x=82@1242 ⇒ 71.3）。
        /// <para>
        /// ⚠️ <b>卡阵**不是**整屏居中的</b>：右边 = 71.2 + 968.5 = 1039.7 ⇒ 右边距 40.3 ≠ 左边距 71.2。
        /// 这是**原版实测**（A1 与 A2 两条独立读数互证：第 4 格左边 960@1242 ⇒ 960 − 3×292.7 = 81.9 ≈ 82）
        /// ⇒ 按"写不出出处的量不进工程"，**照量取用 71.2、不居中**。
        /// （⛔ 不按弹窗亮面体宽居中 —— 那会得到 13.75，是弹窗口径的产物，不是原版位置。）
        /// </para>
        /// </summary>
        private const float GridLeftX = 71.2f;

        private const float GapS = 12f;
        private const float GapM = 16f;
        private const float GapL = 32f;

        /// <summary>卡格列数 = **4**。出处 §1.1 **A7**（原版 07 的可视卡阵 = 4 列 × 2 行）。</summary>
        private const int Columns = 4;

        /// <summary>
        /// 一屏可见的卡格行数 = 2。出处 §1.1 **A7**（4 列 × 2 行 = 8 格）+ **A6**（行步进 549@1242 ⇒ 477.4@1080）。
        /// <para>
        /// 这个数字过去叫 `Rows` 并被当成"每页行数"（配合「上一页 / 下一页」两颗按钮分页）。
        /// 用户第 7 条「配卡组竟然是点击上下页，**不是按住拖动**」⇒ 翻页职责已交给卡池的
        /// <see cref="ScrollRect"/>；本常量现在只用来定**卡池视口高度**（<see cref="PoolViewportH"/>）。
        /// </para>
        /// </summary>
        private const int VisibleRows = 2;

        /// <summary>
        /// 卡池格子总数 = **60**。出处 = 服务端配表 `server/game/table/tsv/card.tsv` 的 60 行
        /// （`策划/registry.md` 记 `card_cs = 60`）—— 与"每页几张"无关：整池一次建好、拖动滚动查看。
        /// </summary>
        private const int MaxPoolCells = 60;

        /// <summary>已选卡组的张位（原版是 8 张）；槽位数由 <see cref="_maxSelected"/> 决定，上限 <see cref="DefaultSlotCount"/>。</summary>
        private const int DefaultSlotCount = 8;

        /// <summary>
        /// 单格宽 = **205**。出处 §1.1 **A1**（原版第 1 格 236px@1242 ⇒ **205.2**@1080）；取 205，
        /// 差 0.2 在「±5px@1242」读数容差之内。
        /// </summary>
        private const float CardW = 205f;

        /// <summary>
        /// 单格高 = **256**。出处 §1.1 **A5**（原版第 1 行卡格**主体**高 294px@1242 ⇒ **255.7**@1080）。
        /// <para>
        /// ⚠️ <b>取值</b>：334 是**本项目自定值**（按「弹窗亮面体宽 996」反推）
        /// （原版 341.8 × 收窄比），比原版卡格主体高多出约 30% ⇒ 卡格被纵向拉长。
        /// 用户第 7 条「ui 也巨丑，根本不是原版」包含此项。
        /// </para>
        /// </summary>
        private const float CardH = 256f;

        /// <summary>
        /// 列间距 = **49.5**。出处 §1.1 **A3**：原版列步进 292.7px@1242 ⇒ **254.5**@1080 ⇒
        /// `ColGap = 254.5 − CardW(205)`。（旧值 58 对应步进 263，比原版宽 3.3%。）
        /// </summary>
        private const float ColGap = 49.5f;

        /// <summary>
        /// 行间距 = **46** —— = 原版两行卡片之间那条**纯背景带**。
        /// <para>
        /// 出处：`07_卡组编辑_1242x2208.jpg` 逐行统计「非卡池底色 `(2,35,90)` 的像素数」，
        /// 第 1 行卡片的**整块**（含卡框 / Level 带 / 升级条）止于 **y=865@1242**，
        /// 第 2 行卡片起于 **y=918@1242** ⇒ 缝 = 53@1242 × 0.8696 = **46.1@1080**。
        /// </para>
        /// <para>
        /// ⚠️ **不能由行步进反算**：原版一张卡占 386..865@1242（479 = 卡面 294 + Level 带 + 升级条），
        /// 而本工程的格子只画卡面（<see cref="CardH"/> = 256 = A5 的卡面高）⇒
        /// 「行步进 477.4 − CardH 256」把原版**属于卡片本体**的 Level 带 / 升级条那 185px 也当成了行距，
        /// 行距被放大到 221.4（实际 46）⇒ 两行之间空出一大片、卡阵整体被拉高 1/3。
        /// </para>
        /// </summary>
        private const float RowGap = 46f;

        /// <summary>列步进 = 254.5（= A3）。</summary>
        private const float CardStepX = CardW + ColGap;

        /// <summary>行步进 = 256 + 46 = **302**（= <see cref="CardH"/> + <see cref="RowGap"/>）。</summary>
        private const float CardStepY = CardH + RowGap;

        /// <summary>卡阵总高 = 2 行 × 256 + 1 条行距 46 = **558**。</summary>
        private const float GridH = 2f * CardH + RowGap;

        /// <summary>卡阵总宽 = 4×205 + 3×49.5 = **968.5**（= A3 的三步 254.5 + 一格 205）。</summary>
        private const float GridW = Columns * CardW + (Columns - 1) * ColGap;

        /// <summary>卡池**内容**总行数 = ⌈60 / 4⌉ = **15**（整池一次建好，靠拖动滚动查看）。</summary>
        private const int PoolRows = (MaxPoolCells + Columns - 1) / Columns;

        /// <summary>卡池内容总高 = 15×256 + 14×46 = **4484**（视口 798.3 ⇒ 可滚约 5.6 屏）。</summary>
        private const float PoolContentH = PoolRows * CardH + (PoolRows - 1) * RowGap;

        // ═══════════ 整屏分带的纵向锚点（原版整屏版式的骨架） ═══════════
        //
        // 每一条都由 `07_卡组编辑_1242x2208.jpg` 量出，逐条列在 `策划/参考图/几何量取.md` §1.4 / §1.6。
        // ⛔ 全部按**屏幕左上角**为原点、y 向下为正（写进代码时取负，见 `At`）。

        /// <summary>顶部安全区 = E13 <c>(7,38,92)</c>（整宽，高 = 页签带底边 <see cref="TabBarY"/> + <see cref="TabBarH"/>）。</summary>
        private static readonly Color TopAreaColor = new Color32(7, 38, 92, 255);

        /// <summary>页签顶部亮线 = 实测 <c>(33,194,227)</c>。出处 **E66**：
        /// 原分辨率 x=358..362 逐行读得 y28 `#34C4E7` / y29 `#1FC2E3` / y30 `#21B9E0`，
        /// 三行逐通道中位 = (33,194,227)。<para>
        /// ⚠️ ⛔ 不能取 "y32..64 的中位色"（<c>(48,148,210)</c>）：那一段已经**含渐变**（y32 之后就走下坡），
        /// 读出来是一条偏暗的蓝，不是那条高光本身。这里只取**高光那 3 行**。</para></summary>
        private static readonly Color TabOnEdgeColor = new Color32(33, 194, 227, 255);

        /// <summary>
        /// 页签底 **tint** = 实测页签色 ÷ 源帧内填色 <c>(76,176,255)</c>
        /// （与 <see cref="CrUiStyle.ButtonBlueTint"/> **同一口径**，只是分母取页签自己那件
        /// <see cref="CrUiStyle.TabCornerArt"/> = `ui_out` 447 的九宫格中心像素）。
        /// 出处 **E65**：实测页签色 <c>(33,124,193)</c> ⇒ (0.4342, 0.7045, 0.7569)。
        /// <para>
        /// <b>为什么要 tint</b>：源帧的内填色是 **(76,176,255)**（B=255，很艳），
        /// 而参考图上页签实测只有 B=193 ⇒ 不染色直接铺会**明显偏艳**。
        /// 分母是实测的（离屏复刻 `MakeRounded` 后读中心像素，见 `.ai-tmp/test/cr-deckui-mirror9.py`），
        /// ⛔ 不是随手调色。
        /// </para>
        /// </summary>
        private static readonly Color TabOnTint = new Color(0.4342f, 0.7045f, 0.7569f, 1f);

        /// <summary>编号行带的**上下亮边** = 实测 <c>(2,130,255)</c>（x=40 列 y320 / x=300 列 y160）。</summary>
        private static readonly Color NumBarEdgeColor = new Color32(2, 130, 255, 255);

        /// <summary>编号行带的带体 = 实测 <c>(1,96,234)</c>（x=40 列 y240，介于上下亮边之间）。</summary>
        private static readonly Color NumBarColor = new Color32(1, 96, 234, 255);

        /// <summary>编号按钮常态 = E16 <c>(56,107,195)</c>。</summary>
        private static readonly Color NumBtnColor = new Color32(56, 107, 195, 255);

        /// <summary>编号按钮**选中**（金色）= 实测 <c>(252,200,64)</c>（金按钮 1 的填充中位色，避开白色数字）。</summary>
        private static readonly Color NumBtnOnColor = new Color32(252, 200, 64, 255);

        /// <summary>平均圣水 pill 底 = 实测 <c>(26,66,126)</c>。</summary>
        private static readonly Color AvgPillColor = new Color32(26, 66, 126, 255);

        /// <summary>底行工具按钮底 = 实测 <c>(58,129,188)</c>。</summary>
        private static readonly Color ToolBtnColor = new Color32(58, 129, 188, 255);

        /// <summary>
        /// 页签的**顶边** = **23.5**（= 27@1242 × 0.8696）。出处 **E25**，两条独立判据：
        /// ① 「逐列首个**暗于面板−9** 的行」在 x=680..1080 上取中位 = 27@1242；
        /// ② 「逐列首个**亮于面板+40** 的行」在原版页签上取中位 = 27@1242（5%/95% 分位同为 27）。
        /// </summary>
        private const float TabTopY = 23.5f;

        /// <summary>页签块高 = 136.5 − 23.5 = **113.0**。</summary>
        private const float TabH = NumRowY - TabTopY;

        // ⚠️ 顶边**量取口径的坑**（保留，改这条边之前先读）：用"原分辨率逐点看填充色跳变"会读到
        // y=40@1242 处 `#062252`(V=82.33) → `#0D305C`(V=92.53) 的一步跳 10.2 —— 那条跳变确实存在，
        // 但它是页签「顶部暗斜面 → 签体填充」这条**内部**边，⛔ 不是页签的外上缘；
        // 外上缘在它**上面 13 行**处（面板 `#09275B`(V=92) 掉到暗斜面 `#021E4B`(V=78)）。

        /// <summary>页签顶部**亮线**高 = **3.5**（= 4px@1242）。出处 **E66**：高光只占 y27..y30 共 4 行。
        /// ⚠️ 取**实测值**（4px@1242；⛔ 不用"取 6 便于看清"）。</summary>
        private const float TabEdgeH = 3.5f;

        /// <summary>编号行带的上下亮边高 = **10**（实测 y157..180@1242 ⇒ 20@1080 是「上亮边 + 渐变」合起来，本工程取 10）。</summary>
        private const float NumBarEdgeH = 10f;

        /// <summary>编号按钮**宽** = **95.7**（实测 110@1242；E8 的金按钮实测 75..184 ⇒ 109）⇒ 110 × k。</summary>
        private const float NumBtnW = 95.7f;

        /// <summary>编号按钮**步进** = **142.2**（实测按钮 2..5 的左边 236/400/563/727@1242 ⇒ 步进 163.5 ⇒ 142.2@1080）。</summary>
        private const float NumBtnPitch = 142.2f;

        /// <summary>第 1 颗编号按钮左边 = **63.9**（实测金按钮左边 73.5@1242 ⇒ 63.9@1080，与步进反推的 236−163.5=72.5 一致）。</summary>
        private const float NumBtnX0 = 63.9f;

        /// <summary>编号按钮顶边 = **169.6**（实测金按钮 y 195..294@1242，带 157..332 ⇒ 上下各留 38 ⇒ 对称居中）。</summary>
        private const float NumBtnY = 169.6f;

        /// <summary>编号按钮高 = **87.0**（实测 100@1242）。</summary>
        private const float NumBtnH = 87.0f;

        /// <summary>
        /// 底行（平均圣水 pill + 三颗工具钮）**紧贴卡阵下方**，间距 = 原版卡阵底边到底行顶边的那条空档。
        /// <para>
        /// 出处两条实测：① 原版卡阵最后一行卡片的下缘 = **1309@1242** ⇒ **1138.3@1080**
        /// （y=918 起 + 卡片整块 391，见 <see cref="RowGap"/> 的出处段）；
        /// ② 原版底行钮板顶边 = **1351@1242** ⇒ **1174.8@1080**（§1.6.3 **E41**）。
        /// ⇒ 空档 = 1174.8 − 1138.3 = **36.5**。
        /// </para>
        /// <para>
        /// ⚠️ 为什么本工程不能用原版底行的绝对 y = 1174.8：原版卡阵**整块**高 802.4@1080
        /// （含 Level 带 / 升级条），本工程的格子只画卡面 ⇒ 卡阵只有 <see cref="GridH"/> = 558。
        /// 沿用绝对位会把底行留在离卡阵 280 远的半空、而把下方卡池压到只剩 1 行（用户报「上下比例失衡」）
        /// ⇒ 改按**实测空档**贴住卡阵，剩下的空间全部给卡池。
        /// </para>
        /// </summary>
        private const float BottomRowY = GridTopY + GridH + GridToBottomRowGap;

        /// <summary>卡阵底边 → 底行顶边的空档 = **36.5**（出处见 <see cref="BottomRowY"/>）。</summary>
        private const float GridToBottomRowGap = 1174.8f - 1138.3f;

        /// <summary>
        /// 底行钮板高 = **103.5**。出处 **E41**。
        /// <para>
        /// <b>量取口径</b>（不同算子在同一对象上会得到不同读数，本条取最后一种）：
        /// · 口径 `x800..880` 的亮掩码跨在钮 1/钮 2 的**缝**上 ⇒ 偏低（"118@1242" = 102.6@1080）；
        /// · `x1075..1200` + `V&gt;140` 且**在缩放到 1080 的图上**量 ⇒ 把钮板下方的**柔光/暗边**
        ///   一并圈进来 ⇒ 偏高（"127@1242" = 110.5@1080）；
        /// · ✅ 现用口径 **119@1242** = **103.5@1080** —— **原分辨率**、`x1080..1180`（整颗第 3 钮）、
        ///   阈值 `V&gt;165`（钮板 V≈188 / 底行带 V≈140 ⇒ 取中值偏板 2）。亮像素数的平台阶跃落在
        ///   **上缘 1351 / 下缘 1470** ⇒ 板占 [1351, 1469]，高 = **119@1242**。
        ///   独立互证：同一条钮板的 `R&gt;40` 分段给出 1354..1466（差 ≤8px，同为"板体"读数）。
        /// </para>
        /// <para>⚠️ 这条由「底行 钮板 下缘」逐带比对持续把关。</para>
        /// </summary>
        private const float BottomRowH = 103.5f;

        /// <summary>
        /// 平均圣水 pill 左边 = **20.9**。出处 **E59**。
        /// <para>
        /// 量法：pill 底 = `#1C4280` / 底行带 = `#02458B`，在 **y=1370@1242**（文字上方那一行，
        /// 避开白色数字）逐列取色 —— x=18 仍是带色 `#02458B`，**x=24 转 pill 的暗边 `#003575`**
        /// ⇒ 左缘 = 24@1242 × 0.8696 = **20.9**。
        /// </para>
        /// </summary>
        private const float AvgPillX = 20.9f;

        /// <summary>
        /// 平均圣水 pill 宽 = **231.3**。出处 **E60**。
        /// 同一行读到右缘 **x=290@1242**（x=282 转暗边 `#013475`、x=294 已回到带色 `#03468D`）
        /// ⇒ (290 − 24) × 0.8696 = **231.3**（⛔ 不用"按内容估"的 261.8 / 7.8）。
        /// </summary>
        private const float AvgPillW = 231.3f;

        // ─────────── 底行右侧：**原版是 3 颗方形工具钮** ───────────
        //
        // 原版 07 底行右侧是**三颗一样大的浅蓝方形钮**（放大镜 / 卡组视图 / 菜单）；
        // ⛔ 不能画成两颗大蓝块（保存 / 取消）。
        // 版式按原版钉死（位置 / 尺寸 / 步进全部实测），三颗钮各自接**真功能**：
        //   钮 1（放大镜 `ResPaths.IconSearch` = `ui_out` 279，原版同一件图元）= **浏览卡牌**（卡池滚回顶部）
        //   钮 2 = **保存**，钮 3 = **取消**
        // ⚠️ 原版钮 2/钮 3 的图标（卡组视图 / 菜单）本工程**没有对应图元**、对应功能也没实现 ⇒
        //    用文字占位，差异登记在 `策划/差异登记.tsv` **D153**（⛔ 不拿别的帧冒充图标）。
        //
        // 量法：钮板 = `#4B92C8`（R≈75）/ 底行带 = `#02488E`（R≈2）
        // ⇒ 用 **R>40** 在 **y=1360@1242**（钮板内、图标之上）逐列分段，得到三段各 **119@1242** 宽：
        //   (783,901) (936,1054) (1088,1206) ⇒ 步进 152.5@1242。

        /// <summary>底行第 1 颗工具钮左边 = **680.9**（实测 783@1242）。出处 **E61**。</summary>
        private const float BottomBtnX0 = 680.9f;

        /// <summary>工具钮宽 = **103.5**（实测 119@1242）。出处 **E62**。</summary>
        private const float BottomBtnW = 103.5f;

        /// <summary>工具钮步进 = **132.6**（实测 152.5@1242；第 3 颗右缘 = 1080 − 30.4 = 1049.6）。出处 **E63**。</summary>
        private const float BottomBtnPitch = 132.6f;

        /// <summary>卡池视口顶边 = **1073.7**（= 底行底边 1033.7 + 40 留白；原版该处往下是宣传插图区，⛔ 无 UI 出处）。</summary>
        private const float PoolTopY = BottomRowY + BottomRowH + 40f;

        /// <summary>卡池视口底边距画布底 = **48**（本项目自定：给状态行让位）。</summary>
        private const float PoolBottomGap = 48f;

        /// <summary>
        /// 卡池**视口**高 = **798.3**（= <see cref="CrUiStyle.DesignH"/> − <see cref="PoolTopY"/> − <see cref="PoolBottomGap"/>）。
        /// <para>
        /// ⚠️ <b>取值</b>：高度由**剩余空间**决定（上方的页签带 / 编号行 / 卡阵 / 底行各自有自己的实测几何），
        /// 不按行数反算。当前可见 ≈ 2.6 行（行步进 <see cref="CardStepY"/> = 302）；可滚的行数不变
        /// （<see cref="PoolRows"/> = 15）。
        /// </para>
        /// </summary>
        private const float PoolViewportH = CrUiStyle.DesignH - PoolTopY - PoolBottomGap;

        /// <summary>已选张数标签顶边 = **294**（卡阵 <see cref="GridTopY"/> 335.7 上方那 40 的空白带；⛔ 原版无此标签）。</summary>
        private const float SelectedLabelY = GridTopY - 42f;

        /// <summary>卡池说明行顶边 = **1284**（底行底边与卡池视口之间那条 40 的带）。</summary>
        private const float PoolLabelY = PoolTopY - 33f;

        /// <summary>状态行与左右两侧（圣水 pill 右缘 / 工具钮左缘）的留白 = **16**（本工程取值：原版该带无文字，仅保证不贴边）。</summary>
        private const float StatusGap = 16f;

        /// <summary>状态行上下各留 = **14**（整行落在底行带 <see cref="BottomRowH"/> 内）。</summary>
        private const float StatusInset = 14f;

        /// <summary>
        /// **开发期附加件的总开关**（当前只控卡阵上方那颗 `已选 N/8` 标签）。
        /// <para>
        /// 卡阵上方的 `已选 N/8` 标签是**原版没有**的东西：它对开发有用，但会破坏"1:1 复刻原版 UI"这条铁律
        /// （用户 2026-09-24「**你的UI都不是原版UI啊**」）。
        /// ⇒ 默认 **false = 不建**（= 原版版式）；需要时改这一处即可回来，⛔ 不是把代码删掉。
        /// 这条差异登记在 `策划/差异登记.tsv` **D154**。
        /// </para>
        /// </summary>
        /// <remarks>
        /// ⛔ 必须是 `static readonly` 而**不是** `const`：写成 `const` 时编译器会把
        /// `if (ShowDevChrome)` 折成恒假 ⇒ 该分支报一条 **CS0162（无法访问的代码）**。
        /// </remarks>
        private static readonly bool ShowDevChrome = false;

        // ── 卡面几何（量自素材自身，⛔ 不是"拍"的） ──
        //
        // 帧自身的**透明包围盒 / 逐帧 x 偏移例外 / 卡 key → 帧号表** 三项的唯一真源在 `CrUiStyle`
        // （`CardArtBbox*` / `CardArtCropOffsetX` / `TryGetCardArtFrame`）—— ⛔ 本面板与 `HudPanel`
        // 不能再各存一份（两份会漂移 ⇒ 一边的卡面缺失查不出来）。
        // ⛔ 本面板只保留**自己这两处**的量取值（卡池格的内边距/距格顶，量自 `07_卡组编辑` 基线图）。
        // ── 卡面几何（量自**原版基线图**，⛔ 不是"拍"的） ──
        //
        // 卡面**满铺卡格**（左右内缩 0、距顶 0、高 = 卡格高，见 `CreateCell` 的算式）。
        //   依据 = `策划/参考图/07_卡组编辑_1242x2208.jpg` 逐像素放大：原版卡格**整块**都是卡面彩图，
        //   卡格边缘那圈亮色是**压在卡面之上**的卡框，卡面并没有内缩留白。
        //   ⛔ 不用「左右各内缩 6、距顶 3」：那量的是"卡框玻璃高光边缘到彩图"的那条过渡带，
        //   把它当成"卡面内边距"是**读错了对象** ⇒ 卡面比卡格小一圈、四周露出一圈白色卡底。
        //
        // 另一半偏差在**素材**侧：`ui_spells_out/*.png` 是 403×377 整幅画布、
        //   `.meta` 的 sprite rect 是内容窗 ⇒ uGUI `Image.GetDrawingDimensions`
        //   （`com.unity.ugui` `Image.cs:850-880`）会套用它算出的 padding，把卡面画到偏移的四边形里。
        //   修法 = 与工程内其它 UI 帧同约定：**按 alpha 包围盒裁切**、`.meta` rect 改成整幅。
        //   裁切后 `.meta` 的 rect = 整幅，alpha 包围盒即内容（⛔ 不再留 padding）。

        /// <summary>卡面距格左右的内边距 = 0（卡面满铺；依据见上面的卡面几何段）。</summary>
        private const float ArtInsetX = 0f;

        /// <summary>卡面距格顶的内边距 = 0（卡面满铺；依据见上面的卡面几何段）。</summary>
        private const float ArtTop = 0f;

        /// <summary>
        /// 名字带高 64（= 原版 "Level 11" 带 74px@1242 ⇒ 64.4@1080，取 64）。
        /// <para>
        /// ⚠️ 原版那条带是**压在卡面之上**的半透明带；本项目没有这条带的图元
        /// （`ui_out` 里查不到，⛔ 不拿别的帧冒充、⛔ 不自绘），所以只画**带描边的文字**压在卡面上
        /// （<see cref="CrUiStyle.Outlined"/>：白字 + 黑描边，正是原版 UI 的绝对多数文字的读法）。
        /// 这一条差异登记在 `策划/对照表.md`。
        /// </para>
        /// </summary>
        private const float NameBandH = 64f;
        private const float BadgeW = 40f;                                 // 圣水水滴宽（原版格角圣水数是紫圆 + 数字）
        private const float BadgeX = 4f;
        private const float BadgeH = 40f;

        /// <summary>悬停 / 按下的混色系数（本项目自定；原版只有常态底图）。</summary>
        private const float HoverBlend = 0.15f;

        // ── 拖动 / 滚动（⛔ 下面这几个数是**本项目自定**，原版客户端的手势与滚动手感参数
        //    不在原版资源里；逐条登记在 `策划/差异登记.tsv` D146） ──

        /// <summary>
        /// 落位吸附的**半径系数**（K）：松手点落在某个卡格中心 **± 半格×(1+K)** 之内即算命中该格。
        /// <para>
        /// ⚠️ 版式是原版 **4×2 大卡阵**（⛔ 不是「一行 8 个小槽位」的纵向吸附带），
        /// 吸附按**最近格中心 + 半径**判定（<see cref="HitTestSlot"/>）。
        /// K 取 0.35 ⇒ 相邻格中心之间的中缝仍然判给更近的那一格、而卡格**外**的一圈也算"落在这格上"
        /// （原版手感是"拖到那一带就吸附"，严格按格判会要求像素级对准）。
        /// </para>
        /// <para>⛔ **本项目自定**：原版资源里没有手势参数（原版客户端的手势代码未取得）。</para>
        /// </summary>
        private const float SlotSnapPadK = 0.35f;

        /// <summary>卡池滚动列表的弹性系数（`ScrollRect.elasticity`）。取原版手感的近似值，本项目自定。</summary>
        private const float ScrollElasticity = 0.10f;

        /// <summary>卡池滚动列表的惯性衰减（`ScrollRect.decelerationRate`；Unity 默认 0.135，沿用）。</summary>
        private const float ScrollDeceleration = 0.135f;

        // ── 「按住卡上任意位置都能拖」的采样口径（`VerifyDragPath` 用；本项目自定） ──

        /// <summary>每个卡格的采样边长 = **5** ⇒ 5×5 = 25 个点（判据 = 25 点**全部**命中卡自己）。</summary>
        private const int ProbeSide = 5;

        /// <summary>采样区起点 = 卡面矩形内 **15%** 分位（两侧对称，见 <see cref="ProbeTo"/>）。</summary>
        private const float ProbeFrom = 0.15f;

        /// <summary>采样区终点 = 卡面矩形内 **85%** 分位。15%..85% 覆盖到卡框圆角之内、又不贴边。</summary>
        private const float ProbeTo = 0.85f;

        /// <summary>同一格最多打几条 `DRAGPATH-BLOCKED`（超出只累计、不刷屏；判据读数仍按 25 点算）。</summary>
        private const int MaxBlockedLogPerCell = 6;

        /// <summary>采样点距滚动视口边界的**排除余量**（屏幕 px；理由见 <see cref="InsideClip"/>）。</summary>
        private const float ProbeClipGuardPx = 4f;

        /// <summary>幽灵卡的不透明度（跟着指针走的那张半透明卡；`HudPanel.GhostColor` 同口径 0.55 附近，取 0.70）。</summary>
        private const float GhostAlpha = 0.70f;

        /// <summary>被拖走的那一格的卡面不透明度（半透明 = "它正在被搬走"，用户第 5 条「放卡没有卡模型 透明的那种」）。</summary>
        private const float DraggedArtAlpha = 0.35f;

        // 本面板原先自存的「卡 `key` → `ui_spells_out` 帧号」表（60 条，依据 = 原版
        // `原版资源/sc/ui_spells_v215.sc` 的 95 条 export 名 → `frame_NNN`，登记在
        // `策划/原版UI素材名称索引.md` §3.5）已**整体上收到 `CrUiStyle.CardArtFrameTable`**（唯一真源）。
        // 原因：`HudPanel` 也曾自存一份 35 条的副本，本面板扩到 60 条时那份没同步 ⇒ 对局手牌里 24 张卡
        // 查不到帧号、只画「卡槽底 + 卡名」。两张表并存 = 必然漂移，所以只留一处。
        // 唯一缺口 `goblin-hut`（原版 95 条 export 里没有 `goblin_hut` 命名）用哨兵
        // `CrUiStyle.CardArtFrameMissing` 表达，判定走 `CrUiStyle.IsCardArtKnownGap`。

        // ═══════════════ 运行时状态 ═══════════════

        private bool _built;
        private int _dragPathCheckFrame = -1;           // 拖放自检的预定帧（-1 = 没有待跑的自检，见 RequestDragPathCheck）
        private int _maxSelected;                       // 服务端卡组张数（来自 PanelArgs，见 ResolveMaxSelected）

        private CardInfo[] _pool;                       // 60 张卡池（可达 null：还没拉到）
        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();
        private readonly List<int> _selected = new List<int>();  // **当前卡组号**的编辑缓冲（按点选 / 拖放顺序）

        private int _deckIndex;                         // 正在看 / 正在编的卡组号（0..4；内容由服务端给）
        private readonly Image[] _deckNumBlocks = new Image[DeckSlotCount];  // 编号按钮的蓝底（填充 + targetGraphic 退回用）
        private readonly Image[] _deckNumGold = new Image[DeckSlotCount];    // 压在蓝底上的金层（当前号才开）
        private readonly Button[] _deckNumButtons = new Button[DeckSlotCount];

        private RectTransform _content;                 // 内容框（所有元素的父节点 = 整屏根）
        private ScrollRect _poolScroll;                 // 卡池滚动列表（拖动滚动取代翻页按钮）
        private RectTransform _poolContent;             // 卡池滚动**内容**（15 行，整池一次建好）
        private readonly List<Cell> _cells = new List<Cell>();       // 60 个卡池格（整池一次建好，只按卡池长度切 active）
        private readonly List<Cell> _slotCells = new List<Cell>();

        // 卡阵（4×2）的几何（建完就固定；拖动落位判定要用它把屏幕点换算成"第几格"，见 HitTestSlot）
        private float _slotX0;                          // 第 1 格左边 = GridLeftX = 71.2
        private float _slotYTop;                        // 第 1 行顶边 = GridTopY = 335.7
        private float _slotStepX;                       // 列步进 = CardStepX = 254.5
        private float _slotStepY;                       // 行步进 = CardStepY = 477.4

        private Text _selectedLabel;
        private Text _poolLabel;
        private Text _status;
        private Text _avgElixir;                        // 底行 pill 的「平均圣水」读数（原版 07 是 "3.8"）

        // ── D146 拖动状态 ──
        private Image _ghost;                           // 跟着指针走的半透明幽灵卡（唯一一个，反复复用）
        private int _dragCardId = -1;                   // 本次拖的是哪张卡（卡池卡 / 槽位卡的 id）
        private int _dragIndex = -1;                     // 源下标（卡池下标 或 槽位下标）
        private bool _dragFromSlot;                     // 源是不是"已选槽位"
        private bool _dragActive;                       // 正在拖（防止 OnDragMoved 早于 OnDragBegan 被调用）
        private bool _ghostArtWarned;                   // 「幽灵卡拿不到卡面」只告警一次

        private bool _emittingSave;                     // 正在发保存请求（用来识别本类自己那条 Changed 的回声）
        private bool _busy;                             // 保存在途（界面侧；权威在途闸是 DeckManager._saving）
        private Button _saveButton;                     // 「保 存」按钮（在途时置 interactable=false）
        private int _lastUnknownType = int.MinValue;    // 未知类型只告警一次（记录上一次报过的值）
        private bool _poolTooBigWarned;                 // 「卡池比预建格子多」只告警一次

        private Action<CardInfo[]> _onPoolLoaded;
        private Action<DeckRef> _onDeckChanged;
        private Action<DeckRef> _onSlotLoaded;
        private Action<string> _onSaveFailed;

        /// <summary>一个格子（卡池格 / 已选槽位共用）的句柄：底图 + 卡面 + 名字 + 圣水数字。</summary>
        private sealed class Cell
        {
            public Image Chassis;      // 原版白色卡片底（九宫格）
            public Image Art;          // 卡面（原版素材帧；没登记帧号的卡为 null）
            public Image Frame;        // 品质边框（原版卡框图元 ui_out 532，按稀有度染；空格子关掉）
            public int Rarity;         // 本格卡片的稀有度（协议 CardInfo.rarity）；边框色与异步到货的贴色都读它
            public Text Name;
            public Text Elixir;

            /// <summary>
            /// 本格当前应有的色调（**不含**"正在被拖走"那一层）。为什么要存下来：卡面的 Sprite 是
            /// **异步**到达的，到货时会重设 color ⇒ 必须由回调重新贴回当前状态色，
            /// 否则刚选中的格子在素材到货后又变回原色。
            /// </summary>
            public Color Tint = Color.white;

            /// <summary>
            /// 本格的卡是否**正在被拖走**。为 true 时卡面按 <see cref="DraggedArtAlpha"/> 半透明 ——
            /// 与 <see cref="Tint"/> 分开存，因为这两个状态会叠加（"已选的卡被拖起来换位"）。
            /// </summary>
            public bool Dragging;
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
            Subscribe();
            SyncSaveButton();

            // 每次打开都问一次「当前卡组号是哪一号、内容是什么」——权威在服务端（也可能被别的端改过）。
            // 开面板那条路上管理器会先用缓存立刻回一条，服务端回包到了再回一条（同一条处理，幂等）。
            Game.Event?.Emit(Events.Deck.SlotRequest, -1);
            // ⛔ 这里**不**预定「按住拖动」的射线自检：本方法跑在订阅之前、卡池/卡组都还没到，
            //   此刻 `_selected` 为空且 `RefreshCells` 会把 60 个卡池格全部 `SetActive(false)`
            //   ⇒ 两个 pass 全部 `continue`、`checked == ok == 0` ⇒ 只会落一条 `DRAGPATH-EMPTY`（判据空转）。
            //   预定点在**真的有可见卡格**的那一处：`OnDeckChanged`（卡阵 8 格到货）。判据本身一字未放宽。
            SetStatus("点卡池里的卡选进卡阵（最多 8 张）；按住卡片拖动可换位、拖到卡池上即移除",
                CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
            _dragPathCheckFrame = -1;                   // 面板关了就别再跑自检（节点已被销毁）
        }

        /// <summary>
        /// 拖放自检的**唯一**执行点：到期那一帧由 <c>Game.UI.Tick</c> 驱动（见 <see cref="RequestDragPathCheck"/>）。
        /// </summary>
        public override void OnUpdate(float dt)
        {
            if (_dragPathCheckFrame < 0 || Time.frameCount < _dragPathCheckFrame) return;
            _dragPathCheckFrame = -1;
            VerifyDragPath();
        }

        /// <summary>
        /// 预定一次「按住拖动」自检，**真正跑在下一帧之后**（⛔ 不在同帧直接跑）。
        ///
        /// <para>
        /// <b>为什么必须跨帧</b>：判据取 <c>EventSystem.RaycastAll</c> 的命中，而 uGUI 的
        /// <c>GraphicRaycaster</c> 会跳过 <c>Graphic.depth == -1</c> 的图元 —— 包源码
        /// <c>Library/PackageCache/com.unity.ugui@23caec89ae27/Runtime/UGUI/UI/Core/GraphicRaycaster.cs:316</c>：
        /// 「-1 means it hasn't been processed by the canvas」，而 <c>depth</c> 由画布在**本帧末尾**的画布更新里写回。
        /// 与本帧刚 <c>SetActive(true)</c>（卡池）或刚创建（面板）的图元同帧判 ⇒ 命中的必然是"上一个有合法 depth 的图元"
        /// （实测：面板刚开那帧命中主菜单按钮，卡池刚激活那帧命中 PopupMask）⇒ 每一格都判成 BLOCKED。
        /// 真人点击永远发生在之后的帧，跨帧之后量到的才是真指针路径。
        /// </para>
        /// <para>调用点：<see cref="OnDeckChanged"/>（卡阵那一屏）。</para>
        /// </summary>
        private void RequestDragPathCheck()
        {
            // +2：本帧的 `Button.onClick` / 数据回调可能仍排在画布更新之前 ⇒ 至少跨过一整次画布更新。
            _dragPathCheckFrame = Time.frameCount + 2;
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

        /// <summary>
        /// 建**整屏**视觉树（整屏原版版式）。
        ///
        /// <para>
        /// 基线图 `07_卡组编辑_1242x2208.jpg` 上的原版是**整屏深蓝**，自上而下五条带：
        /// 顶部安全区 → 页签带（Collection）→ 编号行（1..5）→ 4×2 卡格阵列 → 底行（平均圣水 + 工具钮）。
        /// </para>
        /// <para>
        /// <b>坐标口径</b>：父节点 = **面板根（整屏）**，原点 = 屏幕**左上角**、y 向下为正
        /// （写进 <c>AnchoredTopLeft</c> 时取负，见 <see cref="At"/>）。宽 = <see cref="CrUiStyle.DesignW"/> = 1080。
        /// 全部数字量自 `07` 图，逐条编号 <c>E*</c>/<c>A*</c> 见 `策划/参考图/几何量取.md` §1.1 / §1.4 / §1.6。
        /// </para>
        /// <para>
        /// ⛔ <b>两块全屏底图必须 <c>raycast = false</c></b> —— 它们盖住整屏，一旦吃掉射线，
        /// 下面的卡格、按钮就一个都点不动（这正是"按住拖动到底行不行"最容易被悄悄掐断的那一层）。
        /// </para>
        /// </summary>
        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);
            _content = root;                                    // 整屏：所有元素的父节点 = 面板根

            // ── ① 整屏底（两块，⛔ 都必须 raycast=false）──
            // 全屏底 = 卡池底深蓝 E18 (2,35,90)；顶部到页签带底 = 安全区 E13 (7,38,92)。
            UIFactory.CreateBoxRect("DeckBg", root, Vector2.zero,
                new Vector2(CrUiStyle.DesignW, CrUiStyle.DesignH), DeckBgColor, false);
            // 顶区底 = 原版斜格底纹**平铺**（源帧 `ui_out` 276；出处 = `UI_menu_background`（clip 4890）
            // → 子件 `background`(clip) → shape frame_276，显式引用可复跑，见 `ResPaths.MenuBackdropTile`）。
            // tint = 参考图 07 顶区纹理实测均值 ÷ 帧灰度均值（`CrUiStyle.BackdropTint`）；
            // 兜底色仍用 TopAreaColor（取不到图时退化成原来的纯色，⛔ 不会变白）。
            CrUiStyle.Backdrop("TopBackdrop", root, ResPaths.MenuBackdropTile, TopAreaColor,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(CrUiStyle.DesignW, TabBarY + TabBarH), CrUiStyle.BackdropTint, false);

            // ── ② 页签带（E2/E4/E5/E6 + 实测的页签上缘）──
            // 页面只有卡池这一屏 ⇒ 一颗页签，几何 / 外观全按实测（位置 TabCollectionX、宽 TabWCollection、
            // 选中态 = 亮底 + TabEdgeH 高亮顶线）。
            BuildTab("TabCollection", "Collection", TabCollectionX, TabTopY, TabH, TabWCollection,
                TabOnColor, TabOnEdgeColor, TabEdgeH, TabOnTint, OnCollectionTabClicked);

            // ── ③ 编号行（E4..E7；band 体 + 上下两条亮边）──
            UIFactory.CreateBoxRect("NumBar", root, At(0f, NumRowY),
                new Vector2(CrUiStyle.DesignW, NumRowH), NumBarColor, false);
            UIFactory.CreateBoxRect("NumBarTopEdge", root, At(0f, NumRowY),
                new Vector2(CrUiStyle.DesignW, NumBarEdgeH), NumBarEdgeColor, false);
            UIFactory.CreateBoxRect("NumBarBottomEdge", root, At(0f, NumRowY + NumRowH - NumBarEdgeH),
                new Vector2(CrUiStyle.DesignW, NumBarEdgeH), NumBarEdgeColor, false);
            BuildDeckNumberRow();

            // ── ④ 已选张数（**默认不建**）──
            // ⛔ 原版 07 卡阵上方那条 40 的空白带**是空的**，本行的 `已选 N/8` 是本工程自己加的。
            //    用户 2026-09-24「你的UI都不是原版UI啊」⇒ 第三片把它从**默认版式**里摘掉。
            //    代码保留（字段/刷新逻辑一字未改）⇒ 只要把 ShowDevChrome 改回 true 就回来了。
            if (ShowDevChrome)
            {
                _selectedLabel = CrUiStyle.Outlined("SelectedLabel", root, "已选", CrUiStyle.FontSmall,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), At(GridLeftX, SelectedLabelY),
                    new Vector2(GridW, 32f), TextAnchor.MiddleLeft);
            }

            // ── ⑤ 4×2 卡格阵列（A1/A3/A4/A5/A6/A7）──
            BuildSlots();

            // ── ⑥ 底行（平均圣水 pill + 保存 / 取消）──
            BuildBottomRow();

            // ── ⑦ 卡池说明 + 卡池（按住拖动滚动）──
            _poolLabel = CrUiStyle.Outlined("PoolLabel", root, "卡池", CrUiStyle.FontSmall,
                new Vector2(0f, 1f), new Vector2(0f, 1f), At(GridLeftX, PoolLabelY),
                new Vector2(GridW, 30f), TextAnchor.MiddleLeft);
            BuildGrid();

            // 打开即停在卡池那屏：卡池与卡阵同屏 ⇒ 一打开就能把卡从卡池拖进上面的卡阵。

            // ── ⑧ 状态行 = **底行中段**（`SetStatus` 的可见落点：保存成功 / 失败原因都显示在这里）──
            // 落点选在原版底行同一带里、平均圣水 pill 与右侧工具钮之间的空档 ⇒ 不新加装饰、不盖住别的元素
            // （pill 右缘 = AvgPillX + AvgPillW，工具钮左缘 = BottomBtnX0）。
            _status = CrUiStyle.Outlined("Status", root, string.Empty, CrUiStyle.FontSmall,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                At(AvgPillX + AvgPillW + StatusGap, BottomRowY + StatusInset),
                new Vector2(BottomBtnX0 - (AvgPillX + AvgPillW) - StatusGap * 2f, BottomRowH - StatusInset * 2f),
                TextAnchor.MiddleLeft);

            BuildGhost();
        }

        /// <summary>
        /// 把「屏幕坐标（原点 = 左上角、y 向下为**正**）」换算成 <c>AnchoredTopLeft</c> 要的
        /// <c>anchoredPosition</c>（y 取负 = 向下）。
        /// <para>为什么要有这个函数：量取表的读数一律是"距图顶边多少像素"，与 uGUI 的 y 向上为正**反号**
        /// ⇒ 散落各处的 <c>-y</c> 极易漏一个负号、且漏了看不出来（元素会跑到屏幕上方看不见）。
        /// 统一走这里 ⇒ 漏负号变成编译错误而不是"某个东西不见了"。</para>
        /// </summary>
        private static Vector2 At(float x, float yFromTop)
        {
            return new Vector2(x, -yFromTop);
        }

        /// <summary>按钮四态（常态 = 白 = 原版图元原色；⛔ 不用底色当滤镜）。口径同 <c>CrUiStyle</c> 内那几处。</summary>
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
        /// 建页面页签（本工程只有卡池这一屏 ⇒ 一颗 Collection）。
        /// <para>
        /// <b>底 = 原版图元的九宫格</b>（⛔ 不用 `CreateBoxRect` 那种**方角 + 无描边**的实心矩形：
        /// 参考图上页签是**圆角件**）。
        /// 底 = <see cref="CrUiStyle.Skin"/> + <see cref="CrUiStyle.TabCornerArt"/>（= `ui_out` **447**，
        /// 「左上圆角」件，**原版像素经四角镜像拼九宫格**）+ <see cref="TabOnTint"/>，
        /// 圆角边长 = <see cref="CrUiStyle.TabCornerSize"/>（= 24）。
        /// </para>
        /// <para>
        /// <b>圆角口径</b>：447 的弧 = 23px（`ui_out` 166 只有 11px、165 的左上角是方角）
        /// ⇒ 实机圆角 23@1080 = 26.5@1242，参考图实测 **25@1242**（E68：x140→118 收口于 y52）
        /// ⇒ 差 **+1.5px@1242**（在读数容差 ±5px@1242 内）。447 的内填色与 166 **逐像素相同**
        /// (76,172,255) ⇒ tint 分母未变、无需重标。
        /// </para>
        /// <para>
        /// <b>仍未复刻</b>：页签底**没有竖向渐变**（原版顶部 → 底部有渐变）。源内已实测**排除**
        /// `ui_out` 265（剖面有"膝"），其余图集已扫过、无剖面一致的渐变件 ⇒ 保留单色。
        /// 登记在 `策划/差异登记.tsv` D155 第 ② 条。
        /// </para>
        /// <para>
        /// <b>顶部那条线</b>（实测 E66）：亮线 <see cref="TabEdgeH"/> 高 <see cref="TabOnEdgeColor"/>。
        /// 页签只有一颗、恒为当前页 ⇒ 只用这一态。
        /// </para>
        /// </summary>
        private void BuildTab(string name, string label, float x, float y, float h, float w,
            Color fill, Color edge, float edgeH, Color tint, Action onClick)
        {
            var block = CrUiStyle.Skin(name, _content, CrUiStyle.TabCornerArt, CrUiStyle.TabCornerSize,
                Vector4.zero, new Vector2(0f, 1f), new Vector2(0f, 1f), At(x, y), new Vector2(w, h),
                fill, true, tint);
            // 顶部那条线：**盖在**圆角件之上（圆角件自身的高光在左上角，整条顶边要靠这一条补）。
            UIFactory.CreateBoxRect(name + "Edge", block.rectTransform, Vector2.zero,
                new Vector2(w, edgeH), edge, false);

            var btn = block.gameObject.AddComponent<Button>();
            btn.targetGraphic = block;
            DressButtonColors(btn);
            if (onClick != null) btn.onClick.AddListener(() => { CrUiStyle.PlayClick(name); onClick(); });

            CrUiStyle.Outlined(name + "Label", block.rectTransform, label, CrUiStyle.FontBody,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(w, 60f), TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// 编号行：**5 颗卡组号**（1..5）+ 当前卡组的**金色选中态**（几何全部实测：左边 63.9、步进 142.2）。
        /// <para>
        /// 每个号底下有**两层原版图元**：蓝底（`ui_out` 166 + <c>BlueCorner</c> + <c>ButtonBlueTint</c>）
        /// 与压在它之上的金层（`ui_out` 300 + <c>BorderButtonGold</c>）。当前号打开金层、其余关掉
        /// —— `enabled` 开关是**同步**写的，⛔ 不走 `CrUiStyle.Skin/Dress` 的异步 tint 回调
        /// （那条路线的回调会晚一帧把 `Image.color` 写回建件时的值，见 <see cref="CrUiStyle.Dress"/>）。
        /// 金层是**子节点**（数字标签是它的后一个兄弟）⇒ 数字仍画在金层之上。
        /// </para>
        /// </summary>
        private void BuildDeckNumberRow()
        {
            for (var i = 0; i < DeckNumLabels.Length; i++)
            {
                var idx = i;
                var size = new Vector2(NumBtnW, NumBtnH);
                var pos = At(NumBtnX0 + i * NumBtnPitch, NumBtnY);

                var block = CrUiStyle.Skin($"DeckNum{i}", _content, ResPaths.ButtonBlueCornerAlt,
                    CrUiStyle.BlueCorner, Vector4.zero,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size,
                    NumBtnColor, true, CrUiStyle.ButtonBlueTint);

                var gold = CrUiStyle.Skin($"DeckNum{i}Gold", block.rectTransform, ResPaths.ButtonGold, 0,
                    CrUiStyle.BorderButtonGold, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, size,
                    NumBtnOnColor, false);
                _deckNumBlocks[i] = block;
                _deckNumGold[i] = gold;

                var btn = block.gameObject.AddComponent<Button>();
                btn.targetGraphic = block;
                DressButtonColors(btn);
                btn.onClick.AddListener(() => { CrUiStyle.PlayClick($"DeckNum{idx}"); OnDeckNumberClicked(idx); });
                _deckNumButtons[i] = btn;

                CrUiStyle.Outlined($"DeckNum{i}Label", block.rectTransform, DeckNumLabels[i],
                    CrUiStyle.FontBody,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAnchor.MiddleCenter);
            }

            RestyleDeckNumbers();
        }

        /// <summary>
        /// 把编号行刷成「当前卡组号 = 金 / 其余 = 蓝」（幂等：只写 `enabled` 与按钮的 <c>targetGraphic</c>）。
        /// <para>
        /// <c>targetGraphic</c> 跟着当前那一层走：按钮的 hover / pressed 过渡是染 <c>targetGraphic</c> 的
        /// CanvasRenderer，若钉死在被金层盖住的蓝底上，点当前号就看不到任何按下反馈。
        /// </para>
        /// </summary>
        private void RestyleDeckNumbers()
        {
            for (var i = 0; i < _deckNumBlocks.Length; i++)
            {
                var current = i == _deckIndex;
                if (_deckNumGold[i] != null) _deckNumGold[i].enabled = current;
                if (_deckNumButtons[i] != null)
                {
                    _deckNumButtons[i].targetGraphic = current ? _deckNumGold[i] : _deckNumBlocks[i];
                }
            }
        }

        /// <summary>
        /// 底行：左 = **平均圣水** pill（原版位置 + 原版圣水图标 `ui_out` 99）；
        /// 右 = **原版那 3 颗方形工具钮**（几何全部实测 E61..E63）。
        /// <para>
        /// 三颗钮各自接**真功能**（⛔ 不画假按钮）：
        /// ① 放大镜（`ResPaths.IconSearch` = `ui_out` 279，**原版同一件图元**）= 浏览卡牌（卡池滚回顶部）；
        /// ② 保存（`_saveButton` 句柄保留 —— 在途时由 <see cref="SetSaveBusy"/> 真禁用）；③ 取消。
        /// </para>
        /// <para>
        /// ⚠️ <b>已知差异（D153）</b>：原版钮 ②/cancel 位的图标是「卡组视图 / 菜单」，本工程
        /// ⛔ 没有对应图元、对应功能也未实现 ⇒ 用**文字**占位（不是拿别的帧冒充图标）。
        /// 版式（位置 / 尺寸 / 步进）已按原版实测落地。
        /// </para>
        /// </summary>
        private void BuildBottomRow()
        {
            UIFactory.CreateBoxRect("AvgPill", _content, At(AvgPillX, BottomRowY),
                new Vector2(AvgPillW, BottomRowH), AvgPillColor, false);
            // 圣水水滴：原版 x40..110 / y1345..1430@1242 ⇒ 宽 ≈ 60.9@1080、顶边比 pill 高 5 ⇒ 用
            // BottomRowH − 34 = 76.5 作宽（≈实测高 73.9），x 偏移 13 落在实测左缘 34.8@1080 附近。
            CrUiStyle.AspectImage("AvgPillIcon", _content, ResPaths.IconElixirDrop, BottomRowH - 34f,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                At(AvgPillX + 13f, BottomRowY + 17f), CrUiStyle.FieldBg, false);
            _avgElixir = CrUiStyle.Outlined("AvgPillValue", _content, "--", CrUiStyle.FontTitle,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                At(AvgPillX + 96f, BottomRowY + 8f),
                new Vector2(AvgPillW - 96f - 8f, BottomRowH - 16f), TextAnchor.MiddleLeft);

            // ① 放大镜 = 浏览卡牌（原版同一件图元）
            var browse = CrUiStyle.BlueButton("BrowseButton", _content, string.Empty,
                At(BottomBtnX0, BottomRowY), new Vector2(BottomBtnW, BottomRowH), OnBrowseClicked);
            CrUiStyle.AspectImage("BrowseButtonIcon", browse.rectTransform, ResPaths.IconSearch,
                BottomBtnW * 0.66f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Color.white, false);

            // CR-F2：保存按钮句柄留下来 —— 在途时必须**真的禁用**（见 `SetSaveBusy`）。
            _saveButton = CrUiStyle.BlueButton("SaveButton", _content, "保 存",
                At(BottomBtnX0 + BottomBtnPitch, BottomRowY), new Vector2(BottomBtnW, BottomRowH),
                OnSaveClicked).GetComponent<Button>();
            CrUiStyle.BlueButton("CancelButton", _content, "取 消",
                At(BottomBtnX0 + 2f * BottomBtnPitch, BottomRowY), new Vector2(BottomBtnW, BottomRowH),
                OnCancelClicked);
        }

        /// <summary>
        /// 已选卡组的 **4×2 大卡阵**（原版版式；A1/A3/A4/A5/A6/A7）。
        /// <para>
        /// ⛔ <b>不再是一行 8 个小槽位</b>：原版 `07` 的卡组就是 4 列 × 2 行、每格 205×256（@1080），
        /// 左边距 <see cref="GridLeftX"/> = 71.2（**不居中**：右边距 40.3 ≠ 左边距 71.2，这是原版实测）。
        /// </para>
        /// <para>
        /// 每格除了「点一下 = 移除」，还挂 <see cref="CardDragHandle"/> —— 用户第 7 条
        /// 「编辑卡组不能拖动」正是说这里。卡阵**不参与滚动**（<c>AllowScroll = false</c>）⇒ 手势只有一种归属 = 拖卡。
        /// </para>
        /// </summary>
        private void BuildSlots()
        {
            var count = SlotCount;

            // 落位判定要用（见 HitTestSlot）：卡阵几何建完就不再变，所以存字段而不是每次现算。
            _slotX0 = GridLeftX;
            _slotYTop = GridTopY;
            _slotStepX = CardStepX;
            _slotStepY = CardStepY;

            for (var i = 0; i < count; i++)
            {
                var index = i;                                       // 闭包捕获：每个槽位记自己的下标
                var col = i % Columns;
                var row = i / Columns;
                var pos = At(GridLeftX + col * CardStepX, GridTopY + row * CardStepY);
                _slotCells.Add(CreateCell($"Slot{index}", _content, pos, new Vector2(CardW, CardH), index, true));
            }
        }

        /// <summary>
        /// 卡池 = **可按住拖动滚动的列表**（取代原来的「上一页 / 下一页」两颗按钮）。
        ///
        /// <para>
        /// <b>结构</b>：<c>PoolScroll</c>（<see cref="ScrollRect"/> + <see cref="RectMask2D"/>，
        /// 高 = <see cref="PoolViewportH"/>）→ <c>PoolContent</c>（高 = <see cref="PoolContentH"/> = 15 行）
        /// → 60 个格子。格子一次性建好（不虚拟化：60 个节点在竖版 UI 里不构成负担），
        /// 超出卡池长度的格子切 `SetActive`。
        /// </para>
        /// <para>
        /// <b>为什么转发手势而不是自己搬 content</b>：<see cref="CardDragHandle"/> 在纵向占优时把
        /// `OnBeginDrag` / `OnDrag` / `OnEndDrag` 转给这个 <see cref="ScrollRect"/>（它自己的
        /// `public virtual` 入口）⇒ 惯性 / 回弹 / 边界全部走既有实现，⛔ 不重造轮子。
        /// </para>
        /// <para>
        /// ⚠️ <b>本项目自定值</b>：<see cref="ScrollElasticity"/> / <see cref="ScrollDeceleration"/>
        /// 与原版客户端的真实手感参数**无出处**（原版资源里没有滚动条配置），已登记 `策划/差异登记.tsv` D146。
        /// </para>
        /// </summary>
        private void BuildGrid()
        {
            var viewport = UIFactory.CreateNode("PoolScroll", _content);
            UIFactory.AnchoredTopLeft(viewport, At(GridLeftX, PoolTopY), new Vector2(GridW, PoolViewportH));

            _poolScroll = viewport.gameObject.AddComponent<ScrollRect>();
            // 遮罩挂在**同一个**节点上（它就是自己的 viewport）：`RectMask2D` 裁的是自己的子节点，
            // 而子节点只有 `PoolContent` ⇒ 效果 = 视口裁剪，且不需要多一层空节点。
            viewport.gameObject.AddComponent<RectMask2D>();

            _poolContent = UIFactory.CreateNode("PoolContent", viewport);
            UIFactory.AnchoredTopLeft(_poolContent, Vector2.zero, new Vector2(GridW, PoolContentH));

            _poolScroll.viewport = viewport;
            _poolScroll.content = _poolContent;
            _poolScroll.horizontal = false;
            _poolScroll.vertical = true;
            _poolScroll.movementType = ScrollRect.MovementType.Elastic;
            _poolScroll.elasticity = ScrollElasticity;
            _poolScroll.inertia = true;
            _poolScroll.decelerationRate = ScrollDeceleration;
            _poolScroll.scrollSensitivity = 1f;

            for (var i = 0; i < MaxPoolCells; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var pos = new Vector2(col * CardStepX, -row * CardStepY);
                var index = i;
                _cells.Add(CreateCell($"Card{index}", _poolContent, pos, new Vector2(CardW, CardH), index, false));
            }
        }

        /// <summary>编号行的标签 = 原版 07 的**卡组号** 1..5。</summary>
        private static readonly string[] DeckNumLabels = { "1", "2", "3", "4", "5" };

        /// <summary>卡组号个数 = **5**（编号行 1..5；与服务端 `logic.deckSlots` 同值，两端同时改）。</summary>
        private const int DeckSlotCount = 5;

        /// <summary>把一份卡组灌进编辑缓冲并刷新界面（⛔ 不落盘：写服务端只走 <see cref="OnSaveClicked"/>）。</summary>
        private void LoadSelection(int[] ids)
        {
            var list = ids ?? Array.Empty<int>();
            _selected.Clear();
            for (var i = 0; i < list.Length; i++) _selected.Add(list[i]);

            RefreshSlots();
            RefreshCells();
            RefreshPoolLabel();
        }

        /// <summary>
        /// 点第 <paramref name="index"/> 颗卡组号（显示的号 = <paramref name="index"/> + 1）：读**服务端**那一号的内容。
        /// <para>
        /// 金框与内容都等 <see cref="OnSlotLoaded"/> 到了再一起换（点卡组号先发 <see cref="Events.Deck.SlotRequest"/>）
        /// ⇒ 不会出现"高亮在第 3 号、画出来还是第 1 号"。
        /// </para>
        /// </summary>
        private void OnDeckNumberClicked(int index)
        {
            if (index < 0 || index >= DeckSlotCount)
            {
                // 非预期分支：编号行只有 DeckSlotCount 颗，越界说明建件与回调的下标口径不一致。留痕。
                Game.Logger?.Warn(Tag, $"[Deck] DECK-SLOT-INVALID index={index}（编号行只有 {DeckSlotCount} 颗）");
                return;
            }

            if (index == _deckIndex)
            {
                Game.Logger?.Info(Tag, $"[Deck] DECK-SLOT-REPEAT slot={_deckIndex + 1}（已经是当前卡组号）");
                return;
            }

            Game.Logger?.Info(Tag, $"[Deck] DECK-SLOT-ASK from={_deckIndex + 1} to={index + 1}（向服务端读该号内容）");
            SetStatus($"读取卡组 {index + 1}…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Deck.SlotRequest, index);
        }

        /// <summary>
        /// 底行第 1 颗工具钮（放大镜）—— **浏览卡牌**：把卡池滚回顶部。
        /// <para>原版这颗钮是"卡牌浏览 / 搜索"的入口，⛔ 不是"保存 / 取消"的别名。</para>
        /// </summary>
        private void OnBrowseClicked()
        {
            if (_poolScroll != null) _poolScroll.verticalNormalizedPosition = 1f;
            SetStatus($"浏览卡牌：卡池共 {PoolRows} 行；按住上下拖动翻看，把卡拖到上面的卡阵即选入",
                CrUiStyle.TextDim);
        }

        /// <summary>点页签（页面只有卡池这一屏 = 当前页）：把卡池滚回顶部，作为一次可见的响应。</summary>
        private void OnCollectionTabClicked()
        {
            if (_poolScroll != null) _poolScroll.verticalNormalizedPosition = 1f;
            SetStatus($"卡池（共 {PoolRows} 行）：按住上下拖动翻看，把卡拖到上面的卡阵即选入",
                CrUiStyle.TextDim);
        }

        /// <summary>
        /// 刷新底行左边那颗 pill 的**平均圣水**读数（原版 07 是 "3.8"）。
        /// <para>⛔ 不用 <c>ToString("0.0")</c>：某些区域设置下小数点是逗号 ⇒ 用整数十分位自己拼，输出恒为 ASCII。</para>
        /// </summary>
        private void UpdateAvgElixir()
        {
            if (_avgElixir == null) return;
            if (_selected.Count == 0)
            {
                _avgElixir.text = "--";
                return;
            }

            var sum = 0;
            for (var i = 0; i < _selected.Count; i++)
            {
                var card = FindCard(_selected[i]);
                if (card != null) sum += card.elixir;
            }

            var tenths = Mathf.RoundToInt((float)sum / _selected.Count * 10f);
            _avgElixir.text = $"{tenths / 10}.{tenths % 10}";
        }

        /// <summary>槽位数 = 卡组张数（夹到 <see cref="DefaultSlotCount"/>：槽位是在 OnOpen 里按张数建好的，张数变了不重建节点）。</summary>
        private int SlotCount => Mathf.Clamp(_maxSelected > 0 ? _maxSelected : DefaultSlotCount, 1, DefaultSlotCount);

        /// <summary>
        /// 建一个可交互的格子：原版白色卡片底（<see cref="ResPaths.SlotCard"/> 九宫格）+ 卡面 + 名字 + 圣水数字，
        /// 并挂上 <see cref="CardDragHandle"/>（按住拖动 —— 卡池格纵向拖 = 滚动列表，横向拖 = 拖出卡）。
        /// 底图不是纯色块，也不是把原图直接拉伸（九宫格切边 = 20）。
        /// <para>
        /// 「卡组槽底 / 卡池格底」= <see cref="ResPaths.SlotCard"/>（`ui_out` 43，107×159）：
        /// 它**就是 A 的原版白色卡底**（白色圆角卡片剪影，与基线 `07_卡组编辑` 每张卡背面的卡形一致）
        /// ⇒ 不是错帧、没有可换的替代帧。
        /// 卡底之上再压一层**品质边框**（<see cref="ResPaths.CardFrameGlowLegendary"/> 的原版框形 +
        /// <see cref="RarityFrameColor"/> 的实测稀有度色），对应基线 07 上每格那圈彩色卡框。
        /// </para>
        /// </summary>
        /// <param name="index">本格下标（卡池格 = 卡池下标；槽位 = 槽位下标）—— 拖放回调要用。</param>
        /// <param name="isSlot">true = 已选槽位（手势只有"拖卡"，不滚动）；false = 卡池格（纵向拖 = 滚动列表）。</param>
        private Cell CreateCell(string name, Transform parent, Vector2 pos, Vector2 size, int index, bool isSlot)
        {
            var cell = new Cell();

            // 卡片底：原版白色卡片底九宫格（107×159 的圆角卡形 ⇒ 切边 20 保住圆角）
            cell.Chassis = CrUiStyle.NineSlice(name, parent, ResPaths.SlotCard, BorderCard,
                new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, CrUiStyle.PanelBg, true);

            var btn = cell.Chassis.gameObject.AddComponent<Button>();
            btn.targetGraphic = cell.Chassis;
            btn.onClick.AddListener(() => OnCellClicked(index, isSlot));
            // 四态一律以**白**为基准（常态 = 白 = 原版图元原色；hover/pressed 只在亮度上差一点）。
            // ⛔ 不用底色染整格 —— 底色会给原版卡片底加滤镜。
            // ⛔ 禁用态也必须是**白**：空槽位走 `SetInteractable(cell, false)`，而 uGUI 的禁用态
            //    会把 `disabledColor` 乘进 **CanvasRenderer** 的颜色 ⇒ 整块原版白卡底（`ui_out` 43）
            //    会被染暗成近黑（实测渲染像素 `(22,22,28)`），看起来像"空格子是暗色占位块"。
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.10f, 1.10f, 1.10f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = Color.white;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = CrUiStyle.ButtonFade;
            btn.colors = colors;

            // 同一个 GameObject 上挂手势组件（`CardDragHandle` 的类注释解释了为什么要"最深层的
            //   IDragHandler 自己分流"、以及为什么"拖完还会触发一次 onClick"必须自己压掉）。
            var drag = cell.Chassis.gameObject.AddComponent<CardDragHandle>();
            drag.Scroll = isSlot ? null : _poolScroll;
            drag.ScrollViewport = isSlot ? null : _poolContent == null ? null : (RectTransform)_poolContent.parent;
            drag.AllowScroll = !isSlot;
            drag.OnDragBegan = screen => BeginCardDrag(index, isSlot, screen);
            drag.OnDragMoved = MoveCardDrag;
            drag.OnDragEnded = screen => EndCardDrag(screen);

            // 卡面（素材到达后按帧号换成裁剪过的 Sprite；未登记帧号的卡这一格保持 null = 不画）
            // 卡面（素材到达后按帧号换成裁剪过的 Sprite；未登记帧号的卡这一格保持 null = 不画）
            cell.Art = UIFactory.CreatePanel(name + "Art", cell.Chassis.rectTransform, CrUiStyle.PanelBg, false);
            // 卡面**满铺卡格**（左右内缩 0、距顶 0、高 = 格高）。
            //   ⛔ 不能按 `artH = artW × (CardArtBboxH / CardArtBboxW)`（**素材自身的宽高比**）反推高：
            //   它与卡格比例（A1 : A5 = 205.2 : 255.7）不同源 ⇒ 卡面比卡格矮一截、底部露出一条白卡底。
            //   满铺后卡框（原版是**压在卡面之上**的图元）自然盖在卡面边缘。
            var artW = size.x - 2f * ArtInsetX;
            var artH = size.y - ArtTop;
            UIFactory.Place(cell.Art.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(ArtInsetX, -ArtTop), new Vector2(artW, artH));
            cell.Art.gameObject.SetActive(false);

            // 品质边框：图元按稀有度分两族，**唯一决定处 = `ApplyFrame`** ——
            //   传说 = 原版光晕框 `ResPaths.CardFrameGlowLegendary`（`ui_out` 532，原版权威名
            //   `card_frame_glow_legendary`）；普通 / 稀有 / 史诗 = 原版**无光晕**空心描边框
            //   `ResPaths.CardFrameOutline`（`ui_out` 592）+ 实测稀有度色。
            //   兄弟顺序在卡面**之后** ⇒ 压在卡面之上。
            //   ⛔ `raycast = false`：边框盖在卡面外圈，一旦吃射线，"按住卡上任意位置都能拖"就失效。
            //   ⛔ 传说那一族**不走 `CrUiStyle.Skin`**（尺寸要按 `RarityFrameScale` 放大、且要在回调里贴回
            //   当前稀有度色）。普通 / 稀有 / 史诗那一族改走 `CrUiStyle.Dress` 的目的只有一个：
            //   拿到**带 border 的九宫格 Sprite**（导入态 `.meta` 的 `spriteBorder` 恒为 0）。
            cell.Frame = UIFactory.CreatePanel(name + "Frame", cell.Chassis.rectTransform,
                CrUiStyle.PanelBg, false);
            UIFactory.Place(cell.Frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size);
            LoadFrameSprite(cell);

            // 圣水水滴（原版图标）+ 数字
            if (!isSlot)
            {
                CrUiStyle.AspectImage(name + "ElixirDrop", cell.Chassis.rectTransform, ResPaths.IconElixirDrop,
                    BadgeW, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BadgeX, -(ArtTop + 2f)),
                    CrUiStyle.FieldBg, false);
            }

            // 卡面已**满铺**卡格 ⇒ 格上的字不再压在白卡底上，而是压在**卡面**上
            // ⇒ 必须换成原版 UI 标准的「白字 + 黑描边」（`CrUiStyle.Outlined`）。
            // ⛔ 不能用 `TextOnLight`（暗字）：字直接压在彩色卡面上时，暗字在深色卡面上根本读不出来。
            cell.Elixir = CrUiStyle.Outlined(name + "Elixir", cell.Chassis.rectTransform, string.Empty,
                CrUiStyle.FontSmall, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(BadgeX + (isSlot ? 0f : BadgeW), -(ArtTop + 2f)),
                new Vector2(BadgeH + 20f, BadgeH), TextAnchor.MiddleLeft);

            // 名字带（原版卡的 "Level 11" 带位置；本项目没有卡等级 ⇒ 显示中文名）—— 同上，白字黑描边。
            cell.Name = CrUiStyle.Outlined(name + "Name", cell.Chassis.rectTransform, string.Empty,
                CrUiStyle.FontSmall, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -(size.y - NameBandH)),
                new Vector2(size.x, NameBandH), TextAnchor.MiddleCenter);

            if (cell.Chassis.GetComponent<Button>() == null)
            {
                // 非预期分支：AddComponent 没挂上 ⇒ 格子点不动且无线索。
                Game.Logger?.Error(Tag, $"格子 {name} 上没有 Button 组件，点选会失效");
            }
            if (cell.Chassis.GetComponent<CardDragHandle>() == null)
            {
                // 非预期分支：手势组件没挂上 ⇒ 「按住拖动」整条链断在这里，而界面上完全看不出来。
                Game.Logger?.Error(Tag, $"格子 {name} 上没有 CardDragHandle 组件，按住拖动会失效（用户第 7 条）");
            }
            return cell;
        }

        /// <summary>原版白色卡片底（<see cref="ResPaths.SlotCard"/>，107×159）的九宫格切边：四边各 20（实测）。</summary>
        private static readonly Vector4 BorderCard = new Vector4(20f, 20f, 20f, 20f);

        /// <summary>
        /// 品质边框相对卡格的放大系数 = **1.06**（**本项目自定值**，登记在 `策划/差异登记.tsv` D164）。
        /// <para>
        /// 为什么不是 1.0：原版卡框图元 `ui_out` 532 的 143×185 画布里**含外发光**（bbox 按 alpha&gt;8 量），
        /// 环本体贴在画布内圈 ⇒ 按 1.0 铺会把环压到卡面**里面**、边缘露出卡面。
        /// </para>
        /// </summary>
        private const float RarityFrameScale = 1.06f;

        /// <summary>
        /// 「传说」档的稀有度值（服务端卡池 `server/game/table/tsv/card.tsv` 的 `rarity` 列，**唯一**有专属原版框帧的一档）。
        /// </summary>
        private const int RarityLegendary = 3;

        /// <summary>
        /// 无光晕空心描边框 <see cref="ResPaths.CardFrameOutline"/>（`ui_out` 592，215×109）的九宫格切边。
        /// <para>
        /// 取值依据 = 该帧的**逐行/逐列透明剖面**实测：左上角第 0 行的最左不透明像素在 x=5、第 6 行起收到 x=0
        /// （列方向 y=6 收到 0）⇒ 圆角半径 ≈ **6px**；取 **10** 保住整条圆弧（切边必须 ≥ 半径）。
        /// </para>
        /// </summary>
        private static readonly Vector4 BorderCardFrameOutline = new Vector4(10f, 10f, 10f, 10f);

        /// <summary>
        /// 稀有度 → 边框色。四档色值全部**量自**基线图 `策划/参考图/07_卡组编辑_1242x2208.jpg`
        /// 的对应卡格（该图四档都有实例，见下面的逐条读数）。
        /// <para>
        /// 量法：在卡格**外圈**（bbox 左/右各 8px 的竖带、上/下各 8px 的横带）取像素，
        /// 先剔掉与卡池底色 `(2,35,90)` 距离 &lt; 60 的背景，再取"饱和度最高的 20%"的中位色。
        /// 逐条读数（原始输出 `.ai-tmp/test/fui-edge.py` / `fui-ring.py`）：
        /// <list type="bullet">
        /// <item>**普通** = 第 1 行第 4 格（箭雨）/ 第 2 行第 3 格（哥布林）：外圈 <b>(40,45,52)</b> 深灰
        /// （实测样本 `y=620 x=952..958` = (24,24,26)(28,27,25)(31,31,33)(21,21,23)）；</item>
        /// <item>**稀有** = 第 2 行第 2 格（火枪手）：外圈 <b>(171,74,32)</b> 橙
        /// （实测样本 `y=1120 x=367..375` = (143,63,26)(141,61,26)(136,59,29)(138,57,27)）；</item>
        /// <item>**史诗** = 第 1 行第 2 格（女巫）：顶缘外圈 <b>(151,116,210)</b> 紫
        /// （实测样本 `x=492 y=392..396` = (212,196,255)(151,116,210)(107,56,183)）；</item>
        /// <item>**传说** = 第 2 行第 1 格：外圈 <b>(228,185,29)</b> 金
        /// （实测样本 `y=1120 x=74..78` = (228,185,29)(232,198,47)(233,216,98)）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ⚠️ **只有"传说"有整幅的独立原版框帧**（`card_frame_glow_legendary` = `ui_out` 532）。
        /// 另外两档的框件是 `card_frame_glow_epic`（`ui.sc` clip 4268 → shape **515 / 516**）与
        /// `card_glow_rare`（clip 4463 → shape **550 / 551**）：盘上有图，但两件都是 **133×25 的带色
        /// 宽扁描边框**（橙 / 紫；同一形状只换色，实测 `ui_sprite_550/551` 与 `515/516` 的 alpha 包围盒
        /// 逐像素同位），而 tint 是**纯乘**，橙色件染不出紫色 ⇒ 三档改用原版中性空心框
        /// （<see cref="ResPaths.CardFrameOutline"/>）+ 这里的**实测色**区分。
        /// 缺口（原版这三档的专属框件未接入）登记在 `策划/差异登记.tsv` D164。
        /// </para>
        /// </summary>
        private static Color RarityFrameColor(int rarity)
        {
            switch (rarity)
            {
                case 1: return new Color32(171, 74, 32, 255);    // 稀有（橙）
                case 2: return new Color32(151, 116, 210, 255);  // 史诗（紫）
                case 3: return new Color32(228, 185, 29, 255);   // 传说（金）
                default: return new Color32(40, 45, 52, 255);    // 普通（深灰）
            }
        }

        // ───────────────────────── 拖动幽灵卡 ─────────────────────────

        /// <summary>
        /// 建**唯一一个**跟着指针走的半透明卡（反复复用，⛔ 不为每次拖动新建节点）。
        ///
        /// <para>
        /// <b>为什么需要它</b>：用户第 5 条「放卡没有卡模型 透明的那种」说的是对局出牌，
        /// 但卡组编辑里同一个缺陷也在：按住卡片拖动时，如果手指下什么都没有，玩家无法确认
        /// "我正在拖哪一张、它有多大"。原版拖放时手指下就是**那张卡面本身**（半透明）。
        /// </para>
        /// <para>
        /// <b>挂在哪儿 / 为什么</b>：挂在**面板根**（不是弹窗亮面体里）—— 根是整屏，
        /// 幽灵卡拖到弹窗外也能看见；而且它是根节点的**最后一个**子节点 ⇒ 画在最上层，
        /// 不会被弹窗或卡池的任何图元盖住。锚点/轴心都取中心，位置由
        /// <see cref="MoveGhost"/> 直接写世界坐标（不依赖 `anchoredPosition` 口径）。
        /// </para>
        /// </summary>
        private void BuildGhost()
        {
            var root = (RectTransform)transform;
            _ghost = UIFactory.CreatePanel("DragGhost", root, CrUiStyle.PanelBg, false);
            UIFactory.Place(_ghost.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(CardW, CardH));
            _ghost.gameObject.SetActive(false);
        }

        /// <summary>把幽灵卡换成源格那张卡面并显示；取不到卡面时**留痕**（退成半透明纯色块）与 <c>HudPanel</c> 同口径。</summary>
        private void ShowGhost(Cell cell, int cardId, Vector2 screen)
        {
            if (_ghost == null) return;

            var sprite = (cell != null && cell.Art != null && cell.Art.gameObject.activeSelf)
                ? cell.Art.sprite : null;

            _ghost.sprite = sprite;
            _ghost.type = Image.Type.Simple;
            _ghost.preserveAspect = false;
            _ghost.color = new Color(1f, 1f, 1f, GhostAlpha);

            // 幽灵卡与源格**同尺寸**（卡池拖出的就是那张大卡，槽位里拖的就是那个小卡）。
            if (cell != null && cell.Chassis != null)
            {
                _ghost.rectTransform.sizeDelta = cell.Chassis.rectTransform.sizeDelta;
            }

            _ghost.gameObject.SetActive(true);
            MoveGhost(screen);

            if (sprite == null && !_ghostArtWarned)
            {
                _ghostArtWarned = true;
                Game.Logger?.Warn(Tag,
                    $"幽灵卡拿不到卡面（card={CardName(cardId)}）⇒ 退成半透明纯色块。"
                    + "可能原因：该卡在帧号表里是**已知缺口**（`CrUiStyle.IsCardArtKnownGap`）、"
                    + "或 `ui_spells_out` 素材还没被导入为 Sprite（本条只报一次）");
            }
        }

        /// <summary>
        /// 把幽灵卡移到指针处。屏幕坐标 → **世界**坐标后直接写 `position` —— 这是
        /// `RectTransformUtility` 专门为此提供的入口，对 Overlay / Screen Space - Camera 两种画布都成立；
        /// ⛔ 不自己拿 `anchoredPosition` 去凑（锚点/轴心口径不同会整体偏一次，画布模式不同偏的量还不一样）。
        /// </summary>
        private void MoveGhost(Vector2 screen)
        {
            if (_ghost == null) return;
            var root = (RectTransform)transform;
            var cam = UiPointConvertCamera();
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(root, screen, cam, out var world))
            {
                _ghost.rectTransform.position = world;
            }
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.gameObject.SetActive(false);
        }

        /// <summary>
        /// 屏幕点 ↔ 画布矩形 / 世界点 换算要用的相机。
        ///
        /// <para>
        /// <b>⛔ 按画布模式取，不是"取一台相机就完事"</b>：常驻画布是 `ScreenSpaceOverlay`
        /// （引擎 `Runtime/Presentation/UI.cs` 里 `canvas.renderMode = RenderMode.ScreenSpaceOverlay`），
        /// 它的世界坐标**就是屏幕像素**；而 <see cref="RectTransformUtility"/> 收到**非空**相机时
        /// 会把屏幕点当成"相机视锥里的一个方向"再投到画布平面上 ⇒ 两者相差一次相机投影，
        /// 命中判定**恒为 false**（`HudPanel.UiPointConvertCamera` 的注释记了同一问题的实机读数：
        /// 手牌按下时 `HitTestHand` 返回 -1 ⇒ 拖放整条链根本不进入）。
        /// Overlay ⇒ `null`；ScreenSpaceCamera / WorldSpace 才用画布自己的 `worldCamera`。
        /// </para>
        /// </summary>
        private Camera UiPointConvertCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        // ───────────────────────── 交互 ─────────────────────────

        /// <summary>
        /// 格子的**点击**（卡池格 / 槽位共用）。第一件事是问 <see cref="CardDragHandle"/>：
        /// 「这次点击是不是一次拖动的尾巴？」
        ///
        /// <para>
        /// <b>为什么必须问</b>：uGUI 只在 <c>pointerEvent.pointerPress != pointerEvent.pointerDrag</c> 时
        /// 才清 <c>eligibleForClick</c>（`PointerInputModule.cs:388-397`）；本项目的格子把 `Button` 与
        /// `CardDragHandle` 挂在**同一个 GameObject** 上 ⇒ 两者相等 ⇒ 拖完**仍会**触发这次 onClick，
        /// 表现为"拖动换位之后又顺手把这张卡移除/加入了"。
        /// 又因为 `ReleaseMouse`（`StandaloneInputModule.cs:206-228`）的顺序是**先 click、后 endDrag**，
        /// 所以标记只能在 <see cref="CardDragHandle.OnDrag"/> 期间打上，由这里读一次即清。
        /// </para>
        /// </summary>
        private void OnCellClicked(int index, bool isSlot)
        {
            if (CardDragHandle.ConsumeClickSuppressed())
            {
                // 期望分支（每次成功拖动都会走一次）：留痕便于数"压掉了几次误点击"。
                Game.Logger?.Info(Tag, $"[Deck] DRAG-SWALLOW-CLICK index={index} from={(isSlot ? "slot" : "pool")}");
                return;
            }

            if (isSlot) OnSlotClicked(index);
            else OnPoolCellClicked(index);
        }

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

        // ───────────────────────── 按住拖动 ─────────────────────────
        //
        // 用户第 7 条原话：「编辑卡组不能拖动，配卡组竟然是点击上下页，不是按住拖动」。
        // 这里实现的就是原版那套手势：
        //   · 卡池格：**纵向**拖 = 滚动卡池（转给 ScrollRect，见 `CardDragHandle`）；**横向**拖 = 把卡拖出来。
        //   · 落点在**卡阵**（上方 4×2）里 ⇒ 选入（空格）/ 替换（已占用格）/ 换位（该卡已在卡组里）。
        //   · 卡阵里的卡拖到**卡阵之外** ⇒ 从卡组移除。
        //   · 卡阵里的卡在格与格之间拖 ⇒ 换位（`MoveSlot`）。
        // 全过程都有一条 `[Deck] DRAG-*` 的 ASCII 留痕（数值类判据：拖 1 次 = 恰好 1 条对应记录）。

        private void BeginCardDrag(int index, bool isSlot, Vector2 screen)
        {
            var cardId = -1;
            if (isSlot)
            {
                if (index < 0 || index >= _selected.Count) return;    // 空槽位：没有可拖的东西
                cardId = _selected[index];
            }
            else
            {
                if (_pool == null || index < 0 || index >= _pool.Length || _pool[index] == null) return;
                cardId = _pool[index].id;
            }

            _dragActive = true;
            _dragCardId = cardId;
            _dragIndex = index;
            _dragFromSlot = isSlot;

            var cell = CellAt(index, isSlot);
            ShowGhost(cell, cardId, screen);
            SetCellDragging(cell, true);

            Game.Logger?.Info(Tag, $"[Deck] DRAG-BEGIN from={(isSlot ? "slot" : "pool")} index={index} card={cardId}");
            SetStatus($"拖动「{CardName(cardId)}」中…松手即落位（拖到卡阵 = 选入/换位；拖出卡阵 = 移除）",
                CrUiStyle.Accent);
        }

        private void MoveCardDrag(Vector2 screen)
        {
            if (!_dragActive) return;              // OnDragMoved 理论上不会早于 OnDragBegan，但这里不赌
            MoveGhost(screen);
        }

        private void EndCardDrag(Vector2 screen)
        {
            if (!_dragActive) return;
            _dragActive = false;

            var fromSlot = _dragFromSlot;
            var fromIndex = _dragIndex;
            var cardId = _dragCardId;
            _dragIndex = -1;
            _dragCardId = -1;
            _dragFromSlot = false;

            SetCellDragging(CellAt(fromIndex, fromSlot), false);
            HideGhost();

            var target = HitTestSlot(screen);
            if (target >= 0)
            {
                if (fromSlot) MoveSlot(fromIndex, target);
                else DropPoolCardToSlot(cardId, target);
                return;
            }

            if (fromSlot)
            {
                RemoveFromSlot(fromIndex);
                return;
            }

            // 卡池的卡拖到卡阵之外 = 取消（原版同：松手在空白处不改变卡组）。
            Game.Logger?.Info(Tag, $"[Deck] DRAG-CANCEL card={cardId}（松手不在卡阵上）");
            SetStatus($"已取消（把「{CardName(cardId)}」拖到上面的卡阵里才会选入）", CrUiStyle.TextDim);
        }

        /// <summary>
        /// **运行时**自检「按住拖动」的事件路径。
        ///
        /// <para>
        /// <b>为什么必须有它</b>：把 <c>ped.pointerDrag</c> **直接写死**成卡的 GameObject、再
        /// <c>ExecuteEvents.Execute</c>，那**绕过了 uGUI 的射线选取**，只证明"回调链本身通"，
        /// **没有证明"手指按在卡上时事件会派给卡"** —— 本方法补上这一步。
        /// </para>
        /// <para>
        /// <b>判据（走真实射线）</b>：对每一个**可见**卡格，在其矩形内按
        /// <see cref="ProbeSide"/>×<see cref="ProbeSide"/> = 5×5 取 25 个采样点（15%~85% 分位）
        /// 逐个当屏幕点 → <see cref="EventSystem.RaycastAll"/> 拿到排序后的命中 → 取最靠前那个 GameObject →
        /// 按 uGUI 的派发规则 <c>ExecuteEvents.GetEventHandler&lt;IDragHandler&gt;</c>（沿父链冒泡、取最深的处理器）
        /// 求出"事件会派给谁" → 判它**是不是那张卡自己**。
        /// <para>
        /// ⛔ 判据写成"等于卡自己"而不是"非空"：后者在"卡上盖了一层吃了射线的图元"时会**假绿**。
        /// ⛔ 采样取 **25 个点**而不是只取矩形中心：需求是"点住卡上**任意位置**都能拖"
        /// （用户原话「卡组里的卡必须按在窄区域才能起拖」），只判中心的旧版在这条上恒假绿 ——
        /// 卡面上一块吃着射线的子图元（名字 / 圣水数字 / 品质边框 / 圣水水滴）就能把外圈的点全挡掉。
        /// 判据 = 25/25 全绿（<c>ok == checked</c>）。
        /// </para>
        /// <para>
        /// 失败时打印**挡在前面的是谁**（完整节点路径）—— 这是这条判据最有价值的部分：将来若有人在卡面上
        /// 加了一层 <c>raycastTarget = true</c> 的图元，日志会直接点名它，不用再猜。
        /// 两种结果都落一条 <c>[Deck] DRAGPATH-*</c> 的 ASCII 留痕（数值类判据：<c>ok == checked</c> ⇒ PASS）。
        /// </para>
        /// </summary>
        private void VerifyDragPath()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                Game.Logger?.Error(Tag,
                    "[Deck] DRAGPATH-NOES：场景里没有 EventSystem ⇒ 点击 / 拖动**一个都不会派发**"
                    + "（这不是本面板的问题，是场景缺件）");
                return;
            }

            // 布局必须先落定：这一步之前 RectTransform 的宽高可能还是 0 ⇒ 射线打不到任何东西（假红）。
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);

            var cam = UiPointConvertCamera();
            var ped = new PointerEventData(es);
            var hits = new List<RaycastResult>();
            var checkedCount = 0;
            var okCount = 0;
            var offViewport = 0;

            for (var pass = 0; pass < 2; pass++)
            {
                var isSlot = pass == 0;
                var list = isSlot ? _slotCells : _cells;
                for (var i = 0; i < list.Count; i++)
                {
                    var cell = list[i];
                    if (cell == null || cell.Chassis == null) continue;
                    if (!cell.Chassis.gameObject.activeInHierarchy) continue;   // 卡池里被 SetActive(false) 的格子不判
                    if (isSlot && i >= _selected.Count) continue;               // 空槽位：没有可拖的东西，不判

                    // 5×5 = 25 个采样点均匀覆盖**卡面矩形内部**（15%~85% 分位），
                    //   ⛔ 不取矩形边界本身 —— 那已落在卡框圆角之外、不属于"卡面内"。
                    var rt = cell.Chassis.rectTransform;
                    var r = rt.rect;
                    var blocked = 0;
                    var logged = 0;
                    for (var sy = 0; sy < ProbeSide; sy++)
                    {
                        for (var sx = 0; sx < ProbeSide; sx++)
                        {
                            var fx = Mathf.Lerp(ProbeFrom, ProbeTo, (float)sx / (ProbeSide - 1));
                            var fy = Mathf.Lerp(ProbeFrom, ProbeTo, (float)sy / (ProbeSide - 1));
                            var local = new Vector3(Mathf.Lerp(r.xMin, r.xMax, fx), Mathf.Lerp(r.yMin, r.yMax, fy), 0f);
                            var pt = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(local));

                            // 屏幕外 / **被卡池视口裁掉**的采样点都不进入判定：射线用屏幕坐标，
                            // 这类点根本不在"看得见的卡面"上 —— 实测被 `RectMask2D` 裁掉的那几行
                            // 命中的是 `Popup/PopupMask`（卡池视口下方的遮罩），那不是"卡面被挡"。
                            // 判据问的是"看得见的卡面上任意位置能不能起拖" ⇒ 单独计数打印，
                            // ⛔ 不并进 blocked、也不当成 ok（旧版用"中心是否在屏内"判，只挡住了前者）。
                            if (pt.x < 0f || pt.x > Screen.width || pt.y < 0f || pt.y > Screen.height)
                            {
                                offViewport++;
                                continue;
                            }
                            var dragHandle = cell.Chassis.GetComponent<CardDragHandle>();
                            var clip = dragHandle != null ? dragHandle.ScrollViewport : null;
                            if (clip != null && !InsideClip(clip, pt, cam))
                            {
                                offViewport++;
                                continue;
                            }

                            ped.position = pt;
                            hits.Clear();
                            es.RaycastAll(ped, hits);
                            checkedCount++;

                            var top = hits.Count > 0 ? hits[0].gameObject : null;
                            var handler = top != null ? ExecuteEvents.GetEventHandler<IDragHandler>(top) : null;
                            if (handler == cell.Chassis.gameObject)
                            {
                                okCount++;
                                continue;
                            }

                            blocked++;
                            if (logged++ >= MaxBlockedLogPerCell) continue;
                            Game.Logger?.Error(Tag,
                                $"[Deck] DRAGPATH-BLOCKED {cell.Chassis.name} 采样=({sx},{sy}) 屏=({pt.x:0},{pt.y:0}) "
                                + $"命中={NodePath(top != null ? top.transform : null)} "
                                + $"拖动处理器={NodePath(handler != null ? handler.transform : null)} "
                                + "⇒ 按在这个点上事件**不会**派给卡（查遮挡层是不是 raycastTarget = true）");
                        }
                    }
                    if (blocked > MaxBlockedLogPerCell)
                    {
                        Game.Logger?.Error(Tag,
                            $"[Deck] DRAGPATH-BLOCKED {cell.Chassis.name} 另有 {blocked - MaxBlockedLogPerCell} 点同样被挡（已省略，同一格）");
                    }
                }
            }

            var verdict = checkedCount == okCount ? "PASS" : "FAIL";
            if (checkedCount == 0)
            {
                // 非预期分支：一个屏内可见卡格都没有 ⇒ `checked == ok == 0` 会**假绿**，必须单独判。
                Game.Logger?.Error(Tag,
                    $"[Deck] DRAGPATH-EMPTY：屏内没有任何可见卡格可判 ⇒ 这次自检无效（不是 PASS；offViewport={offViewport}）");
                return;
            }
            Game.Logger?.Info(Tag,
                $"[Deck] DRAGPATH-SUMMARY grid={ProbeSide}x{ProbeSide} frac={ProbeFrom:0.##}..{ProbeTo:0.##} "
                + $"checked={checkedCount} ok={okCount} blocked={checkedCount - okCount} "
                + $"offViewport={offViewport} verdict={verdict}");
        }

        /// <summary>
        /// 采样点是否落在滚动视口（<c>RectMask2D</c> 的宿主矩形）**内圈**。
        /// <para>
        /// <b>为什么要留 <see cref="ProbeClipGuardPx"/> 的余量</b>：<c>RectMask2D</c> 的实际裁剪边与
        /// <c>RectTransform.rect</c> 之间会差亚像素（画布缩放 + 像素取整）—— 实测贴边的采样点
        /// （屏 y=25，视口下缘也落在 25 附近）判"在视口内"却被裁掉，射线命中的是视口外的模态遮罩
        /// `Popup/PopupMask`，于是被误报成"卡面被挡"。留 4px 余量把这条**探针边界噪声**排除，
        /// ⛔ 不放宽判据本身（卡面可见区域内的判定仍是 25/25）。
        /// </para>
        /// </summary>
        private static bool InsideClip(RectTransform clip, Vector2 pt, Camera cam)
        {
            if (clip == null) return true;
            var a = RectTransformUtility.WorldToScreenPoint(cam,
                clip.TransformPoint(new Vector3(clip.rect.xMin, clip.rect.yMin, 0f)));
            var b = RectTransformUtility.WorldToScreenPoint(cam,
                clip.TransformPoint(new Vector3(clip.rect.xMax, clip.rect.yMax, 0f)));
            return pt.x >= Mathf.Min(a.x, b.x) + ProbeClipGuardPx
                && pt.x <= Mathf.Max(a.x, b.x) - ProbeClipGuardPx
                && pt.y >= Mathf.Min(a.y, b.y) + ProbeClipGuardPx
                && pt.y <= Mathf.Max(a.y, b.y) - ProbeClipGuardPx;
        }

        /// <summary>把 Transform 打成 <c>A/B/C</c>（日志里要能一眼看出"挡在前面的是谁"）。⛔ 不是给玩家看的。</summary>
        private static string NodePath(Transform t)
        {
            if (t == null) return "null";
            var s = t.name;
            var p = t.parent;
            var guard = 0;
            while (p != null && ++guard < 32)
            {
                s = p.name + "/" + s;
                p = p.parent;
            }
            return s;
        }

        /// <summary>
        /// 指针落在第几**格**卡阵；-1 = 不在卡阵上（松手 = 取消 / 移除）。
        ///
        /// <para>
        /// <b>口径（）</b>：卡阵从「一行 8 个小槽位」变成原版的 **4×2 大卡格**之后，
        /// 落位判定也换了：把屏幕点换算到**面板根（整屏）的局部坐标**，再对每一格取
        /// **格中心**，落在"中心 ± 半格 ×(1+<see cref="SlotSnapPadK"/>) " 之内且**最近**的那一格即命中。
        /// </para>
        /// <para>
        /// <b>为什么不用 <c>RectangleContainsScreenPoint</c> 逐格判</b>：原版手感是"拖到那一带就吸附"，
        /// 严格按格判会要求像素级对准，松手稍偏就"什么都没发生"。K = 0.35 ⇒ 格与格中间那条缝仍然判给
        /// 更近的一格，而格**外**的一圈也算"落在这格上"。
        /// ⚠️ 吸附半径是本项目自定值（原版资源里没有手势参数），登记在 `策划/差异登记.tsv` D146。
        /// </para>
        /// </summary>
        private int HitTestSlot(Vector2 screen)
        {
            if (_content == null || _slotCells.Count == 0) return -1;

            var cam = UiPointConvertCamera();
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, screen, cam, out local))
            {
                return -1;
            }

            // `RectTransformUtility` 返回的是**绕 `_content` 轴心**的局部坐标：面板根由运行时供给者
            // `new GameObject(typeName, typeof(RectTransform))` 造出 ⇒ 轴心 (0.5,0.5)、世界位置 = 屏幕中心
            // （实测 pivot=(0.50,0.50) / rect=(-540,-960,1080,1920) / worldPos=(540,960,0)）。
            // 而下面那组格中心用的是**面板左上角为原点、y 向下为正**（`At()` 的口径）。
            // ⇒ 必须先换算到同一套坐标，否则整片卡阵的吸附圈会平移 (+半宽, −半高)：
            //   落在卡阵上的松手一律判成"不在卡阵上"（拖进卡阵没反应 / 卡阵内换位被判成移除）。
            var p = new Vector2(local.x + _content.pivot.x * _content.rect.width,
                _content.pivot.y * _content.rect.height - local.y);

            // 第 i 格的中心 = ( _slotX0 + col×_slotStepX + CardW/2 , _slotYTop + row×_slotStepY + CardH/2 )
            var padX = CardW * 0.5f * (1f + SlotSnapPadK);
            var padY = CardH * 0.5f * (1f + SlotSnapPadK);

            var best = -1;
            var bestD = float.MaxValue;
            for (var i = 0; i < _slotCells.Count; i++)
            {
                var cx = _slotX0 + (i % Columns) * _slotStepX + CardW * 0.5f;
                var cy = _slotYTop + (i / Columns) * _slotStepY + CardH * 0.5f;
                var dx = Mathf.Abs(p.x - cx);
                var dy = Mathf.Abs(p.y - cy);
                if (dx > padX || dy > padY) continue;         // 落在这一格的吸附圈外
                var d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>已选卡组内部换位（原版：按住卡阵里的一张卡拖到另一格上）。</summary>
        private void MoveSlot(int from, int to)
        {
            if (from < 0 || from >= _selected.Count) return;
            to = Mathf.Clamp(to, 0, _selected.Count - 1);
            if (from == to)
            {
                Game.Logger?.Info(Tag, $"[Deck] DRAG-MOVE-NOOP slot={from}（原位松手）");
                SetStatus($"「{CardName(_selected[from])}」没动（当前 {_selected.Count}/{_maxSelected} 张）",
                    CrUiStyle.TextDim);
                return;
            }

            var id = _selected[from];
            _selected.RemoveAt(from);
            _selected.Insert(to, id);
            RefreshSlots();
            RefreshCells();
            Game.Logger?.Info(Tag, $"[Deck] DRAG-MOVE from={from} to={to} card={id}");
            SetStatus($"「{CardName(id)}」已移动到第 {to + 1} 位（当前 {_selected.Count}/{_maxSelected} 张）",
                CrUiStyle.TextDim);
        }

        /// <summary>把卡池里的一张卡拖进卡阵：已在卡组里 = 换位；落在空格 = 选入；落在已占用格 = 替换。</summary>
        private void DropPoolCardToSlot(int cardId, int slotIndex)
        {
            var existing = _selected.IndexOf(cardId);
            if (existing >= 0)
            {
                // 这张卡已经在卡组里 ⇒ 语义是"把它挪到这一格"（原版同）。
                MoveSlot(existing, slotIndex);
                return;
            }

            if (slotIndex < _selected.Count)
            {
                var replaced = _selected[slotIndex];
                _selected[slotIndex] = cardId;
                RefreshSlots();
                RefreshCells();
                Game.Logger?.Info(Tag, $"[Deck] DRAG-REPLACE slot={slotIndex} out={replaced} in={cardId}");
                SetStatus($"「{CardName(replaced)}」被「{CardName(cardId)}」替换掉（当前 {_selected.Count}/{_maxSelected} 张）",
                    CrUiStyle.TextDim);
                return;
            }

            if (_selected.Count >= _maxSelected)
            {
                Game.Logger?.Info(Tag, $"[Deck] DRAG-REJECT card={cardId}（卡组已满 {_maxSelected} 张）");
                SetStatus($"最多只能带 {_maxSelected} 张 —— 先把卡阵里的卡拖到下方卡池上移除一张", CrUiStyle.ErrorText);
                return;
            }

            _selected.Add(cardId);
            RefreshSlots();
            RefreshCells();
            Game.Logger?.Info(Tag, $"[Deck] DRAG-ADD slot={_selected.Count - 1} card={cardId}");
            SetStatus($"已选入「{CardName(cardId)}」（当前 {_selected.Count}/{_maxSelected} 张）", CrUiStyle.TextDim);
        }

        /// <summary>把卡阵里的卡拖出卡阵 = 从卡组移除（原版：拖出卡组区域即移除）。</summary>
        private void RemoveFromSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _selected.Count) return;
            var id = _selected[slotIndex];
            _selected.RemoveAt(slotIndex);
            RefreshSlots();
            RefreshCells();
            Game.Logger?.Info(Tag, $"[Deck] DRAG-REMOVE slot={slotIndex} card={id}");
            SetStatus($"已移除「{CardName(id)}」（当前 {_selected.Count}/{_maxSelected} 张）", CrUiStyle.TextDim);
        }

        /// <summary>取某格（卡池下标 / 槽位下标）；越界返回 null（调用方都得容忍 null）。</summary>
        private Cell CellAt(int index, bool isSlot)
        {
            var list = isSlot ? _slotCells : _cells;
            if (index < 0 || index >= list.Count) return null;
            return list[index];
        }

        /// <summary>标记 / 取消某格"正在被拖走"（卡面半透明，见 <see cref="Cell.Dragging"/>）。</summary>
        private static void SetCellDragging(Cell cell, bool dragging)
        {
            if (cell == null) return;
            cell.Dragging = dragging;
            ApplyArtTint(cell);
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
                SetStatus($"最多只能带 {_maxSelected} 张 —— 先点槽位里的卡移除一张、或把它拖到卡池里再加", CrUiStyle.ErrorText);
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

        // `OnPrevPage` / `OnNextPage` / `PageCount` 已整段删除 —— 用户第 7 条
        // 「配卡组竟然是点击上下页，不是按住拖动」。翻页由 `PoolScroll`（ScrollRect）承担。

        /// <summary>
        /// 保存：**客户端先校验**（张数 / 不重复 / 都在卡池里），不合法就**一个字节都不发**，
        /// 原因显示在面板上（⛔ 不许只打日志）。校验通过后 `Emit(Events.Deck.Changed, ids)` ——
        /// 这就是与 `DeckManager` 约定的"保存请求"通道（见类注释）。
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
            Game.Logger?.Info(Tag,
                $"[Deck] SAVE-SEND slot={_deckIndex + 1} ids={ids.Length}（事件 {Events.Deck.Changed}）");
            SetStatus($"正在保存卡组 {_deckIndex + 1}…", CrUiStyle.Accent);

            // `_emittingSave` 标记：`Emit` 是**同步分发**，本类自己的订阅会立刻收到这条事件，
            // 用它区分"我自己发的请求的回声"与"管理器通知的权威卡组"（见 OnDeckChanged）。
            _emittingSave = true;
            try
            {
                Game.Event?.Emit(Events.Deck.Changed, new DeckRef { slot = _deckIndex, ids = ids });
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
            // ⚠️ 修正（V4 取证实测 2026-09-21）：这里漏了卡池行 —— `OnOpen` 里 `RefreshPoolLabel` 跑在
            // 卡池到达**之前**（那时 `_pool` 还是 null）⇒ 卡池到货后那行一直停在「共 0 张」，
            // 而屏幕上明明画着 60 张卡（V4 截图实测到的可见错数）。补一行即可与真实卡池一致。
            RefreshPoolLabel();

            // 卡池重新到达 ⇒ 回到列表顶部（否则换过卡池后玩家停在半空，看起来像"卡池空了"）。
            if (_poolScroll != null) _poolScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// **保存成功的回执**（保存请求那条通道的回声，见 <see cref="OnSaveClicked"/>）：
        /// <c>_emittingSave</c> 为真 ⇒ 这是本类刚 `Emit` 出去的那条被自己的订阅收到了（事件同步分发），
        /// 不是服务端回执 ⇒ 忽略。否则 = 服务端确认 `confirm.slot` 号就是这 8 张：切到那个号 + 显示"已保存"。
        /// </summary>
        private void OnDeckChanged(DeckRef confirm)
        {
            if (_emittingSave) return;
            SetSaveBusy(false);
            if (confirm == null) return;

            ApplySlot(confirm.slot, confirm.ids);
            // 卡阵刚从服务端回执重画 ⇒ 此刻的拖放自检才有东西可判（卡阵 8 格到货）。
            RequestDragPathCheck();
            SetStatus($"卡组 {confirm.slot + 1} 已保存（服务端确认 {confirm.ids.Length} 张）", CrUiStyle.Accent);
        }

        /// <summary>
        /// 某个卡组号的内容到达（开面板 / 点卡组号时由 `Module/Deck` 发来）：切到该号并重画。
        /// <para>
        /// 点卡组号先发 <see cref="Events.Deck.SlotRequest"/>、拿到这条才移动金框 —— 高亮与画出来的卡
        /// 永远同源（否则网络慢时会出现"金框在第 3 号、内容还是第 1 号"）。
        /// </para>
        /// </summary>
        private void OnSlotLoaded(DeckRef loaded)
        {
            if (loaded == null) return;
            ApplySlot(loaded.slot, loaded.ids);
            RequestDragPathCheck();
            SetStatus($"卡组 {loaded.slot + 1}：{loaded.ids.Length}/{_maxSelected} 张"
                + (_busy ? "（保存在途…）" : string.Empty), CrUiStyle.TextDim);
        }

        /// <summary>把界面切到某号并画它的内容（编号行金框 + 卡阵 + 卡池 + 卡池行文字）。</summary>
        private void ApplySlot(int slot, int[] ids)
        {
            var list = ids ?? Array.Empty<int>();
            for (var i = 0; i < list.Length; i++)
            {
                var id = list[i];
                if (_cards.Count > 0 && !_cards.ContainsKey(id))
                {
                    Game.Logger?.Warn(Tag, $"服务端卡组里的卡 id={id} 不在卡池里（配表/卡池不一致），仍按 id 显示");
                }
            }

            _deckIndex = slot;
            LoadSelection(list);
            RestyleDeckNumbers();
            SyncSaveButton();
        }

        private void OnSaveFailed(string reason)
        {
            SetSaveBusy(false);
            SetStatus(string.IsNullOrEmpty(reason) ? "保存失败（服务端未给出原因）" : reason, CrUiStyle.ErrorText);
        }

        /// <summary>
        /// 设置「保存在途」并**真的禁用保存按钮**。
        ///
        /// <para>
        /// 只把 `_busy` 置位、只改状态行文案时，按钮仍 `interactable = true` ⇒ 玩家看到一颗"能点"
        /// 的按钮、点下去只换来一句"上一次保存还在进行中"，界面上完全看不出在途状态。
        /// 因此两件事同时做：`_busy` 挡逻辑重入 + 按钮禁用给视觉反馈。
        /// ⛔ 两者都要留：`interactable = false` 只拦**真实指针点击**，脚本 / 快捷键直接 `onClick.Invoke()`
        /// 仍会进来 ⇒ `_busy` 那道判断不能被按钮禁用取代。
        /// </para>
        /// </summary>
        private void SetSaveBusy(bool busy)
        {
            _busy = busy;
            SyncSaveButton();
        }

        /// <summary>
        /// 把保存按钮的可用态同步到 `_busy`（不改标记本身；`OnOpen` 重开面板时也走它）。
        /// 5 个卡组号都能保存（服务端按槽位存），所以可用态只看在途。
        /// </summary>
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
                _onSlotLoaded = OnSlotLoaded;
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
            bus.Off(Events.Deck.SlotLoaded, _onSlotLoaded);
            bus.Off(Events.Deck.SaveFailed, _onSaveFailed);
            bus.On(Events.Deck.PoolLoaded, _onPoolLoaded);
            bus.On(Events.Deck.Changed, _onDeckChanged);
            bus.On(Events.Deck.SlotLoaded, _onSlotLoaded);
            bus.On(Events.Deck.SaveFailed, _onSaveFailed);
        }

        private void Unsubscribe()
        {
            if (_onPoolLoaded == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Deck.PoolLoaded, _onPoolLoaded);
            bus?.Off(Events.Deck.Changed, _onDeckChanged);
            bus?.Off(Events.Deck.SlotLoaded, _onSlotLoaded);
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
                _selectedLabel.text = $"已选 {_selected.Count} / {_maxSelected}（点一下移除；按住卡片拖动可换位）";
            }

            // 底行 pill 的「平均圣水」跟着卡组走（原版 07 那颗 pill 的读数就是它）。
            UpdateAvgElixir();
        }

        /// <summary>
        /// 卡池格子刷新。整池 60 个格子按卡池长度切 `SetActive`，
        /// 可见的那 2 行由 <see cref="ScrollRect"/> 的视口 + <see cref="RectMask2D"/> 裁出来
        /// —— ⛔ 不用「按页码整片关掉非当前页的格子」那种分页做法。
        /// </summary>
        private void RefreshCells()
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var count = _pool?.Length ?? 0;
            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell == null || cell.Chassis == null) continue;

                if (i >= count)
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

            // 判据行（数值类证据 = 运行时日志行）：`D167` 残余「卡组页 RefreshCells 的 60 次主线程裁剪」
            //   到底花在哪 —— 本调用自身的耗时 + 卡面加载请求数 + 自上次调用以来**真正做完**的裁剪张数与累计 ms。
            //   ⚠️ 裁剪不在本调用栈里：`ArtCache` 未命中时它发生在 `LoadAsset` 的**异步回调**里（每帧各一张）。
            var ms = Ms(t0);
            _refreshCellsCalls++;
            _refreshCellsMsTotal += ms;
            Game.Logger?.Info(Tag,
                $"[Deck] PERF RefreshCells ms={ms:F2} cells={_cells.Count} pool={count} " +
                $"artReq={_artRequestsSinceLog} calls={_refreshCellsCalls} callsMsTotal={_refreshCellsMsTotal:F2} " +
                $"cropSinceLast={_cropCountSinceLog} cropMsSinceLast={_cropMsSinceLog:F2} " +
                $"cropTotal={_cropCountTotal} cropMsTotal={_cropMsTotal:F2}");
            _artRequestsSinceLog = 0;
            _cropCountSinceLog = 0;
            _cropMsSinceLog = 0;
        }

        /// <summary>`Stopwatch` 起点 → 毫秒（判据行用；引擎日志的耗时读数一律走这一处换算）。</summary>
        private static double Ms(long since)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0
                   / System.Diagnostics.Stopwatch.Frequency;
        }

        // ── 判据行计数器（D167 残余的耗时读数；⛔ 不参与任何逻辑判定）──
        private int _refreshCellsCalls;                 // `RefreshCells` 调用次数
        private double _refreshCellsMsTotal;            // 其自身耗时累计（ms）
        private int _artRequestsSinceLog;               // 本次调用窗口内发出的卡面加载请求数
        private static int _cropCountSinceLog;          // 自上次判据行以来**做完**的裁剪张数
        private static double _cropMsSinceLog;          // 同上累计 ms
        private static int _cropCountTotal;             // 进程内裁剪张数累计
        private static double _cropMsTotal;             // 进程内裁剪耗时累计（ms）

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
                // 卡面满铺后这些字压在**卡面**上（白字黑描边，见 `CreateCell`）⇒ 未选 = 白字；
                // 已选 = 金色强调（原版卡面的圣水数字本身是彩色的）。
                cell.Elixir.color = selected ? CrUiStyle.Accent : CrUiStyle.TextColor;
            }
            if (cell.Frame != null)
            {
                cell.Rarity = card.rarity;
                ApplyFrame(cell);
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
                // 空格子 = 露出原版白色卡底（卡面已 `SetActive(false)`）⇒ 这里的字要**暗色**，
                // 靠描边那一层不要紧（描边色是黑的，暗字加黑描边在白卡底上仍然清楚）。
                cell.Name.color = CrUiStyle.TextOnLightDim;
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
            if (cell.Frame != null) cell.Frame.gameObject.SetActive(false);   // 空格子没有品质 ⇒ 不画边框
        }

        /// <summary>
        /// 把 <see cref="Cell.Tint"/>（选中态）与 <see cref="Cell.Dragging"/>（正在被拖走）叠成卡面的实际色。
        /// ⛔ 必须收敛到这一个方法：卡面 Sprite 是**异步**到货的（见 <see cref="LoadArt"/> 的回调），
        /// 两处各写一份必然漂移 —— 一处只贴 Tint、一处只贴 alpha，拖动时"刚选中的卡被拖起来又不透明了"。
        /// </summary>
        private static void ApplyArtTint(Cell cell)
        {
            if (cell == null || cell.Art == null) return;
            var c = cell.Tint;
            if (cell.Dragging) c.a = DraggedArtAlpha;
            cell.Art.color = c;
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
            _artRequestsSinceLog++;                     // 判据行计数（D167 残余）
            var path = ResPaths.SpellArtFrame(artIndex);
            LoadArtSprite(artIndex, path, sprite =>
            {
                if (cell.Art == null) return;
                cell.Art.sprite = sprite;
                cell.Art.type = Image.Type.Simple;
                // 素材已按 alpha 包围盒裁过、`.meta` 的 rect 也改成整幅
                // ⇒ 卡面本身就填满卡格，⛔ 不要再按比例缩（会再留边）。
                cell.Art.preserveAspect = false;
                ApplyArtTint(cell);               // 素材异步到货 ⇒ 贴回当前状态色（见 Cell.Tint / Cell.Dragging）
            });
        }

        /// <summary>
        /// 品质边框的原版图元（`ui_out` 532）。**自己加载、自己贴色**，理由见 `CreateCell` 里那段注释：
        /// `CrUiStyle.Dress` 的异步回调会把 `image.color` 覆盖成它创建时收到的 tint（这里会是白）。
        /// 只有一片，所以缓存键就是路径本身。
        /// </summary>
        private static Sprite _frameSprite;

        /// <summary>边框图元已 Warn 过（缺素材只报一次）。</summary>
        private static bool _frameWarned;

        // 说明（D167 预热，已回退）：曾按 `IResourceManager.Preload(paths, onDone)` 在这里加过一版卡面预热
        //（装 59 张卡面 + 在 onDone 里连 `CropCardArt` 一起做、灌进 ArtCache）。实机读数把它否掉：
        //   `[Deck] PRELOAD 卡面 59 张开始` → `[Deck] PRELOAD-DONE 新增裁剪 0 张，ArtCache=0`，
        //   且同一轮 `Card0` 的卡面 sprite = `<null>`（回退前的上一轮同一读数 = `frame_022_0`，
        //   卡组页截图里卡面是画出来的）⇒ 预热把同一批路径置成"加载在途"后，
        //   面板那 60 次 `LoadAsset` 的回调不再回来（与 `IResourceManager.Release`「加载在途时被忽略」
        //   同一类在途语义），**卡面永久不画** —— 是回归，不是收益。故整段回退，不留未验证的改动。
        // ⛔ 要再做必须先弄清引擎 Preload/LoadAsset 的在途回调语义（本次未测出来），不许照搬这一版。

        /// <summary>
        /// 把边框按当前稀有度贴好（图元 / 类型 / 尺寸 / 颜色四处一起定）—— **唯一一处**决定边框外观。
        /// <para>
        /// 分两族：**传说**用原版光晕框 <see cref="ResPaths.CardFrameGlowLegendary"/>（`ui_out` 532，143×185
        /// 含外发光 ⇒ 按 <see cref="RarityFrameScale"/> 放大让开卡面）；**普通 / 稀有 / 史诗**用原版
        /// **无光晕**空心描边框 <see cref="ResPaths.CardFrameOutline"/>（`ui_out` 592，九宫格）
        /// + <see cref="RarityFrameColor"/> 的实测稀有度色。
        /// </para>
        /// <para>
        /// ⚠️ 原版这两档自己的框件（`card_glow_rare` = frame 550/551、`card_frame_glow_epic` = frame 515/516）
        /// 是**带色实心描边**件（橙 / 紫）：纯乘 tint 无法把橙色件染成紫色（目标 B 210 ÷ 件内 B 44 &gt; 1）
        /// ⇒ 三档改用中性空心框（见 <see cref="ResPaths.CardFrameOutline"/> 的说明）。
        /// </para>
        /// <para>本格没有卡时不参与 —— 由 <see cref="ClearCell"/> 关掉整个节点。</para>
        /// </summary>
        private static void ApplyFrame(Cell cell)
        {
            if (cell == null || cell.Frame == null) return;
            var rt = cell.Frame.rectTransform;
            var size = cell.Chassis != null ? cell.Chassis.rectTransform.sizeDelta : Vector2.zero;

            if (cell.Rarity >= RarityLegendary)
            {
                if (_frameSprite == null)
                {
                    // 光晕框素材还没到货 ⇒ 先不画（异步回调到货后会再调一次本方法）。
                    cell.Frame.gameObject.SetActive(false);
                    return;
                }
                cell.Frame.sprite = _frameSprite;
                cell.Frame.type = Image.Type.Simple;
                cell.Frame.preserveAspect = false;
                if (rt != null) rt.sizeDelta = new Vector2(size.x * RarityFrameScale, size.y * RarityFrameScale);
            }
            else
            {
                // 走 `CrUiStyle.Dress`：它用 `Sprite.Create(..., border)` 现造**带 border 的九宫格**
                //（导入态 `.meta` 的 `spriteBorder` 恒为 0 ⇒ 直接设 `type = Sliced` 等于没设）。
                // tint = 本档实测稀有度色；Dress 的异步回调会贴回**它自己收到的这个色**，不会覆盖成白。
                if (rt != null) rt.sizeDelta = size;
                CrUiStyle.Dress(cell.Frame, ResPaths.CardFrameOutline, 0, BorderCardFrameOutline,
                    RarityFrameColor(cell.Rarity));
            }

            cell.Frame.gameObject.SetActive(true);
            cell.Frame.color = RarityFrameColor(cell.Rarity);
        }

        /// <summary>
        /// 取**传说光晕框**图元（`ui_out` 532；普通 / 稀有 / 史诗的框件由 <see cref="ApplyFrame"/> 通过
        /// `CrUiStyle.Dress` 直接取，不经过这里）并贴到格子上；**到货后按 `cell.Rarity` 重新贴一次**
        /// （数据可能在建格之后才到）。与 `LoadArtSprite` 同一范式：走 `Game.Res` 的异步口，命中缓存则同步回填。
        /// </summary>
        private void LoadFrameSprite(Cell cell)
        {
            if (cell == null || cell.Frame == null) return;

            if (_frameSprite != null)
            {
                ApplyFrame(cell);
                return;
            }

            if (Game.Res == null)
            {
                WarnOnce("Game.Res 为空（漏了 CloverRes.Init？），品质边框加载不了，格子只剩卡底 + 卡面");
                return;
            }

            Game.Res.LoadAsset<Sprite>(ResPaths.CardFrameGlowLegendary, sprite =>
            {
                if (sprite == null)
                {
                    if (!_frameWarned)
                    {
                        _frameWarned = true;
                        Game.Logger?.Warn(Tag,
                            "品质边框素材加载不到（格子只画卡底 + 卡面）：" + ResPaths.CardFrameGlowLegendary);
                    }
                    return;
                }
                _frameSprite = sprite;
                ApplyFrame(cell);          // 素材异步到货 ⇒ 重新按当前稀有度贴一次（见 Cell.Rarity）
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
                var ct0 = System.Diagnostics.Stopwatch.GetTimestamp();   // 判据行（D167 残余）
                var made = CrUiStyle.CropCardArt(sprite, artIndex);
                var cropMs = Ms(ct0);
                _cropCountSinceLog++;
                _cropMsSinceLog += cropMs;
                _cropCountTotal++;
                _cropMsTotal += cropMs;
                if (cropMs >= 2.0) Game.Logger?.Info(Tag, $"[Deck] PERF CropCardArt frame={artIndex} ms={cropMs:F2}");
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

        /// <summary>
        /// 卡池那一行说明：卡池张数 + **正在编的卡组号** + 已选张数。
        /// <para>
        /// ⛔ 不出现「第 N/M 页」（本页的卡池是整池滚动，没有分页）。
        /// </para>
        /// </summary>
        private void RefreshPoolLabel()
        {
            if (_poolLabel == null) return;
            var count = _pool?.Length ?? 0;
            // 这一行的宽度 = GridW（968.5）；文字超过它就会折行压到卡池上，所以只留「共几张 + 哪一号 + 已选」。
            _poolLabel.text = count > 0
                ? $"卡池：共 {count} 张 · 卡组 {_deckIndex + 1} · 已选 {_selected.Count}/{_maxSelected}"
                : $"卡池：加载中…（已选 {_selected.Count}/{_maxSelected}）";
        }

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

        /// <summary>
        /// 设置格子可点性（空槽位不可点；已选的格可点 = 移除）。
        /// 色态由 Button 的四态负责，⛔ 本方法不碰 `Image.color`；禁用态**不染暗**（`CreateCell` 把
        /// `disabledColor` 设成白）⇒ 空槽位露出的仍是原版白卡底 `ui_out` 43。
        /// </summary>
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
