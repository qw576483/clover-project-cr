using System;
using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 对局 HUD（`Battle` 站点，Normal 层，架构契约 §4）：圣水条 / 4 张手牌 + 下一张预览 / 计时 + 阶段 /
    /// 冠数 / **拖放出牌**。由 `Module/Battle/BattleManager` 在站点切到 `Battle` 时打开
    /// （面板自身不认识它，见下）。
    ///
    /// <para>
    /// <b>依赖方向（契约 §1 硬线）</b>：本面板⛔**不许** `using CR.Module` —— 与 `View/**` 一样，
    /// 只能 <c>On</c> / <c>Emit</c> `Core/Events.cs` 的 `Events.Battle.*`。所以：
    /// 数据一律从 `Battle.Started`（`my_team` / 时间线 / 手牌初值）、`Battle.Snapshot`（10 Hz 数值）、
    /// `Battle.Ended`、`Battle.StartFailed` 进来；请求一律 `Emit(Battle.PlayCardRequest)` 出去。
    /// </para>
    /// <para>
    /// <b>为什么读的是快照而不是插值</b>：HUD 显示的是**离散数值**（圣水 / 冠数 / 阶段 / 手牌 / 计时），
    /// 它们没有"中间态"，插值只会显示假值；插值只对**位置**有意义，那是 `View/UnitView` 的事
    /// （`BattleManager.Sample`）。所以本面板直接消费快照原值。
    /// </para>
    /// <para>
    /// <b>卡面（真卡面 + 原版战斗 HUD 卡槽，⛔ 不再是"类型色底"）</b>：卡槽底 = 原版图元
    /// `ResPaths.HudHandSlot`（`ui_out/200`，原版 `slots` 子元件，索引 §3.2；整幅拉伸），
    /// 卡面 = 原版素材帧 `ResPaths.SpellArtFrame(i)`，
    /// 圣水费用 = 原版水滴 `ResPaths.IconElixirDrop` + 数字。卡 `key` → 卡面帧号取自
    /// <see cref="CrUiStyle"/> 的**唯一一张**表（与 `DeckEditPanel` 同一真源，出处
    /// `策划/原版UI素材名称索引.md` §3.5）；表里没有的卡**不猜帧号**，
    /// 只画原版卡槽底 + 卡名（同一降级口径）。卡名/费用来自 `Events.Deck.PoolLoaded`（`CardInfo`），
    /// 卡池还没到时显示 "卡 id=N" 并留痕。
    /// </para>
    /// <para>
    /// <b>⚠️ 与 `View/HandView` 的分工</b>：`HudPanel` 负责"4 张手牌 + 下一张 + 拖放出牌"，
    /// 即**只做 HUD 侧**：UI 手牌上按下 / 跟随 / 抬起 → `Emit(Battle.PlayCardRequest)`。
    /// **合法性两色落点指示不在本面板**：⛔ 不许在面板里另写一套判定（§5.4）。
    /// 共享几何的合法落点常量只有 `Core/GameConst.cs` 一处、面板又⛔不许引 `CR.Module`
    /// ⇒ 判定由 `View/HandView` 用同一套 `GameConst` 常量做。
    /// 本面板的拖放提示是**中性**的（"松手即请求出牌，合法性由服务端裁决"）+ 把服务端的拒因原文显示出来。
    /// </para>
    /// </summary>
    public sealed class HudPanel : UIPanel
    {
        private const string Tag = "HudPanel";

        // ───────────────────────── 常量（本项目自定：引擎 UIFactory 只给"造节点"，不给布局） ─────────────────────────
        //
        // 画布参考分辨率 1920×1080（`UIManager` 的 CanvasScaler 设定），且 `matchWidthOrHeight = 0.5`
        // ⇒ 真实画布高度会随窗口变小（1600×900 窗口约 972）。所以**底部元素一律用
        // `UIFactory.AnchoredBottom` 定位**，⛔ 不许用"左上角 + 大负 y"把元素顶到屏幕外
        //（`UIWidgetControls.cs:188` 有实测记录）。

        /// <summary>
        /// 手牌槽位数 = 4。
        /// 出处：参考规格（原版手牌固定 4 张）。
        /// 服务端下发的 <c>hand_a[]</c> 长度超出它时只显示前 4 张并留一条 Warn。
        /// </summary>
        private const int HandSlots = 4;

        /// <summary>
        /// 圣水的定点单位 = 1/1000 格。
        /// 出处：`Def/ProtoDef.cs:151`「`elixir_a`（1/1000，0..10000）」与 `Core/GameConst.cs:26`
        /// 的 `MilliTilePerTile`（本项目"定点单位 = 1/1000"的定义处）。
        /// 起这个名字是为了让"圣水也用 1/1000 定点"这件事在读取处一眼可见，
        /// ⛔ 不在业务代码里写裸 `1000`（契约 D11）。
        /// </summary>
        private const int ElixirMilliPerUnit = GameConst.MilliTilePerTile;

        /// <summary>`BattleSnapshot.phase == 1`（加时）。取值口径见 `Def/ProtoDef.cs:150`。</summary>
        private const int PhaseOvertime = 1;

        /// <summary>`BattleSnapshot.phase == 2`（已结束）。面板⛔不许引 `CR.Module`，
        /// 所以拿不到 `BattleManager.PhaseEnded` 那个常量，这里按同一口径本地化一份。</summary>
        private const int PhaseEnded = 2;

        // ═══════════ 竖版几何（每个数字都能反查到出处） ═══════════
        //
        // <b>基准与比例</b>：`策划/参考图/几何量取.md` §1.3「对局 HUD 底部」的基线图是
        // `18_对局HUD_1080x1920.jpg`，它与本项目竖版画布（`CrUiStyle.DesignW×DesignH` = 1080×1920）
        // **同尺寸** ⇒ 该节原文「18 图 = 1080×1920，与本项目画布同尺寸 ⇒ @1080 列 = 像素值（k=1.0）」
        // ⇒ 下面每条后面的 `Dnn` 就是那节的条目号，数值**就是画布像素、不再乘任何比例**。
        // ⚠️ 18 与 19 是同一张图（MD5 相同，§1.3 注）⇒ 本文件只用 18 的读数，⛔ 不当两张图交叉验证。
        //
        // <b>纵向一律用底部锚点</b>：CanvasScaler `match=0`（宽恒 1080、高随设备浮动）⇒ 贴底元素走
        // `UIFactory.AnchoredBottom`，⛔ 不用"左上角 + 大负 y"（会整体掉出屏外，见 `UIWidgetControls.cs:188` 实测记录）。
        //
        // <b>顶部左块的冠数在原版基线图里未到镜</b>（§2 C4）⇒ 该项位置/尺寸**保持现状**，
        // 并在常量注释里逐条写明「未量到（见几何量取.md §2）」。

        // ── 手牌（出处：§1.3 D9/D11/D12/D13，读数来自 **18 图** `gaps` 扫描） ──
        //
        // 几何基线 = `18_对局HUD_1080x1920.jpg`（2.1.5）；`20_对局_1080x1920.jpg` **不是同一版本**，
        // ⛔ 不作几何基线，依据（逐图基线审计）：
        //     ① 20 图右上**没有计时板、没有暂停键**，只有一个 `×2` 圣水双倍徽标 + 裸冠数文字；
        //     ② 20 图 HUD 下方**露出竞技场**（圣水条行 1888..1907 ⇒ 1908..1919 是地图）⇒ 不是完整整屏 HUD；
        //     ③ 20 图手牌是**金框 + 卡顶双菱形紫帽**，而 18（2.1.5）的手牌框是**灰白那族**（= `ui_out/200`）；
        //     ④ `策划/基线图/索引.md:25` 早就把 20 记为"**未取证**；仅作『扫过顶部未见冠数/暂停』的旁证"。
        //   ⇒ 20 归入"另一版本/模式"，⛔ 不作几何基线（相关素材 `frame_547` 留档但**不接线**，见 `ResPaths`）。

        /// <summary>单卡宽 <b>136</b>。
        /// <para>
        /// <b>出处 = 18 图直接量取</b>（`18_对局HUD_1080x1920.jpg` = 唯一几何基线，1080×1920 与本画布同尺寸
        /// ⇒ 像素值即画布值）：对每列在 y1640..1779 上做「蓝度 B−R &lt; 45」多数表决，取**卡体外沿**（含卡框那圈
        /// 深色边）的进出点 ⇒ 卡1 x143..278、卡2 x286..421、卡4 x571..706 三张都 = <b>136</b>
        /// （卡3 x430..562 = 133，受卡面内容干扰）。量法：逐格像素量取。
        /// </para>
        /// <para>
        /// <b>为什么不用 D11 的「140 / 3」</b>：`策划/参考图/几何量取.md:60` 的 D11 出处列写的是
        /// 「**由 D9/D10**」⇒ 它不是量取值，而且与自己的输入不自洽（D9=144 / D10=704 ⇒ 整排 560，
        /// 而 4×140+3×3 = 569）—— 该不一致就是已登记的 D54「手牌整排右端 +9px」。
        /// </para>
        /// </summary>
        private const float CardW = 136f;

        /// <summary>单卡高 <b>171</b> = 卡顶 y=1614 → 卡底 y=1785（1785−1614）。
        /// 复核（x=350 逐行：卡外沿 y1614..1784）⇒ 171 ✔ 差值 0。</summary>
        private const float CardH = 171f;

        /// <summary>卡间距 <b>7</b>。
        /// 出处 = 18 图实测的**卡缝**（纯竞技场蓝）x279..285 / 422..429 / 563..570 = 7 / 8 / 8 px，
        /// 且卡左沿实测 143 / 286 / 430 / 571 ⇒ 步距 142.7 − 卡宽 136 = <b>7</b>（与三处卡缝读数同量级）。</summary>
        private const float CardGap = 7f;

        /// <summary>
        /// 手牌 4 张整排宽 = 4×136 + 3×7 = <b>565</b>。整排实占 = 左边 144 + 565 = 709 vs 18 图实测右沿 706~707
        /// （差 ≤ 3px，JPEG 模糊级）。
        /// </summary>
        private const float HandBarW = HandSlots * CardW + (HandSlots - 1) * CardGap;

        /// <summary>手牌整排**左边** x = <b>144</b>。出处：D9（18 图 gaps「手牌整排左边 x=144」）。
        /// 原版该排**不是居中**的（「下一张」在它左边）⇒ 用左下角锚点定位。</summary>
        private const float HandRowLeft = 144f;

        /// <summary>手牌整排底边距画布底 = DesignH − 卡底 y(1785) = <b>135</b>。出处：D13（18 图）。
        /// ⚠️ 20 图量到卡底 y≈1862（距底 58），但同图圣水条也整体低 ~59px ⇒ 只动卡会让卡片压到条上；
        /// 该差属基线档位问题，见本段上方说明（⛔ 不用 20 图的值）。</summary>
        private const float HandBottomOffset = 135f;

        // ── 「下一张」预览（出处：§1.3 D14/D15） ──
        //
        // 纠正：原版「下一张」在**底排左端**（左下角那张更小的卡），⛔ 不在右侧 ——
        //   上一版把它放在右端是错的（那时基线图底部被宣传字压住、未量到；§1.3 已用 18 图补量）。

        /// <summary>「下一张」卡左边 x = <b>33</b>。出处：D14（18 图 `c18_nextcard_zoom`：x 33..97）。</summary>
        private const float NextLeft = 33f;

        /// <summary>「下一张」卡宽 <b>64</b>。出处：D14（x 33..97 ⇒ 64）。</summary>
        private const float NextW = 64f;

        /// <summary>「下一张」卡高 <b>83</b>。出处：D14（@1080 列标注 64×83；端点 y 1634..1716 = 82，取表内标注值）。</summary>
        private const float NextH = 83f;

        /// <summary>「下一张」卡底边距画布底 = DesignH − y(1716) = <b>204</b>。出处：D14 的 y 端点。</summary>
        private const float NextBottomOffset = 204f;

        /// <summary>「下一张」标签左边 x = <b>10</b>。出处：D15（x 10..115 / y 1718..1755）。</summary>
        private const float NextLabelLeft = 10f;

        /// <summary>「下一张」标签底边距画布底 = DesignH − y(1755) = <b>165</b>。出处：D15 的 y 端点。</summary>
        private const float NextLabelBottom = 165f;

        /// <summary>「下一张」标签宽 <b>105</b>。出处：D15（x 10..115）。</summary>
        private const float NextLabelW = 105f;

        /// <summary>「下一张」标签高 <b>37</b>。出处：D15（y 1718..1755）。</summary>
        private const float NextLabelH = 37f;

        // ── 圣水条（出处：§1.3 D1~D7，18 图 col/row dump） ──

        /// <summary>圣水条高 <b>44</b>。出处：D3（D1 上边 y=1797、D2 下边 y=1841 ⇒ 1841−1797 = 44）。</summary>
        private const float ElixirBarH = 44f;

        /// <summary>圣水条底边距画布底 = DesignH − y(1841) = <b>79</b>。出处：D2（外框下边 y=1841）。
        /// 顺序纠正：原版**圣水条在手牌下方**（条 y1797..1841、卡 y1614..1785），⛔ 不是"条在手牌上方"。</summary>
        private const float ElixirBarBottom = 79f;

        /// <summary>圣水条左端 x = <b>88</b>。出处：D5（18 图 dump row y=1819；左端被圣水徽章遮住，可见起 x≈88）。</summary>
        private const float ElixirBarLeft = 88f;

        /// <summary>圣水条右端 x = <b>1044</b>（右留白 1080−1044 = 36）。出处：D4（18 图 dump row y=1819）。</summary>
        private const float ElixirBarRight = 1044f;

        /// <summary>圣水条宽 = 右端 − 左端 = 1044 − 88 = <b>956</b>（由 D4/D5 算得）。
        /// ⚠️ D4 的 1044 与 18 图实测的条右外沿 1045 差 1px ✔（在容差内）；
        /// 但 D5 的 88 是「**徽章遮挡后的可见起点**」（D5 原文），**不是槽的真左沿** —— 真左沿见
        /// <see cref="ElixirTickGridLeft"/>（= 103，由 9 条刻度最小二乘反解，残差 ≤ 1.3px）。</summary>
        private const float ElixirBarW = ElixirBarRight - ElixirBarLeft;

        // ── 圣水条的 10 格刻度（消解 D66「刻度已登记键但未绘制」） ──
        //
        // 原版结构（出处 `策划/战斗HUD素材索引.md` §3.1）：`elixir_bar`(clip 1080) 的子元件里
        // **`d1`…`d9` 共 9 个，同属 clip 909、落地帧 = `ui_out` 160（1×1），原文注明「10 格刻度分隔
        // （原版同一 clip 复用 9 次）」** ⇒ 9 条分隔线把条分成 **10 格**，一格 = 1.0 圣水。
        // 索引 §2.1 第 2 条明说「坐标/缩放未解出 ⇒ 摆放尺寸要等坐标解出或按原版截图量」
        // ⇒ 下面每个常量按基线图量取（都带量法）。

        /// <summary>
        /// 圣水条**刻度栅格左端** x = <b>103</b>（第 0 格的左沿）。
        /// <para>
        /// <b>为什么不是 <see cref="ElixirBarLeft"/>(88)</b>：18 图 y=1820 逐点实测 x94 (255,33,238) /
        /// x98 (185,28,177) / x100 (107,0,107) 都还是**圣水徽章的亮边与深色描边**（徽章心 x=63、宽 60 ⇒
        /// 圆身到 x93，亮边一直盖到 ~102）⇒ 88 只是"条从徽章后面露出来的地方"，槽的真左沿被盖住了，
        /// 只能用刻度栅格反解。
        /// </para>
        /// <para>
        /// <b>量法</b>（`18_对局HUD_1080x1920.jpg`，像素值即画布值）：逐列做「列均值 y1802..1837 对
        /// 移动中值基线(k=40)」的差，取每条暗线最深的那一列 ⇒ 9 条刻度中心
        /// x = 198 / 289.5 / 384 / 479 / 572 / 666 / 759 / 853 / 947（k=1..9；k=1 落在**品红填充内**，
        /// k=2 = 填充(2 圣水)的右沿，k=3..9 在空槽暗底区）；对 (k, x) 做最小二乘
        /// ⇒ 截距 <b>103.32</b>、斜率 <b>93.72</b>、9 点最大残差 1.3px，取 <b>103</b> / 93.72。
        /// 交叉验证：① 自动检测出的 7 条暗线（198/384/479/666/759/853/947）**全部**落在这条栅格 ±2px 内；
        /// ② 填充 2 圣水的右沿应 = 第 2 格右沿 = 103.32+2×93.72 = 290.8（18 图实测 289~290 ✔）；
        /// ③ 末格右沿 = 103.32+10×93.72 = 1040.5（= 条右端 1044 再减去端头件的占位）。
        /// 量法：逐格像素量取。
        /// </para>
        /// </summary>
        private const float ElixirTickGridLeft = 103f;

        /// <summary>圣水条**每格宽** = <b>93.72</b>（1 格 = 1.0 圣水）。出处：见
        /// <see cref="ElixirTickGridLeft"/> 的 9 点最小二乘斜率。</summary>
        private const float ElixirTickStep = 93.72f;

        /// <summary>圣水条**刻度栅格宽** = 10 格 × <see cref="ElixirTickStep"/> = <b>937.2</b>。
        /// 填充与刻度共用它 ⇒ 每 1.0 圣水正好铺满 1 格。</summary>
        private const float ElixirTickGridW = ElixirTickStep * 10f;

        /// <summary>填充区在轨道内的**左偏移** = 栅格左端 − 条左端 = 103 − 88 = <b>15</b>。</summary>
        private const float ElixirFillInsetLeft = ElixirTickGridLeft - ElixirBarLeft;

        /// <summary>
        /// 刻度分隔线宽 <b>4</b>。出处：18 图 9 条刻度在**半深**处的横向宽度实测 3~5px
        /// （k=8 那条半深覆盖 x944..948 = 5px；k=3 那条 x383..385 = 3px）⇒ 取 4。
        /// 原版图元本身是 1×1（<see cref="ResPaths.ElixirBarTick"/>）⇒ 宽高由放置矩阵给，
        /// 而索引 §2.1 明说矩阵未解出 ⇒ 按基线量取，⛔ 不猜。
        /// </summary>
        private const float ElixirTickW = 4f;

        /// <summary>
        /// 刻度分隔线高 = 条高 − 上下各 6px = 44 − 12 = <b>32</b>。出处：18 图列剖面（x=947 对邻列 x=941）
        /// 的暗线纵向范围实测 y1803..1834 = 32px，而条外框是 y1798..1841（高 44）⇒ 上下各让 ~6px 内边。
        /// </summary>
        private const float ElixirTickH = ElixirBarH - 12f;

        /// <summary>
        /// 刻度分隔线的**不透明度** = <b>0.30</b>（还原"原版怎么画刻度"，不是随手调色）。
        /// <para>
        /// 18 图实测：刻度处 = 底色 × ≈0.85（空槽 (32,48,82)→(28,41,73)；品红填充 (207,39,212)→(169,27,175)），
        /// 即一层 ~15% 的**暗**覆盖。uGUI 默认混合做不出乘算 ⇒ 用原版帧 `frame_160` 的自色 (35,35,35)
        /// 配 α = 0.30 逼近：空槽合成 ≈ (33,44,68) vs 实测 (28,41,73)、满条合成 ≈ (155,34,159)
        /// vs 实测 (169,27,175)，两处每通道差 ≤ 14/255。
        /// </para>
        /// </summary>
        private static readonly Color ElixirTickTint = new Color(1f, 1f, 1f, 0.30f);

        /// <summary>刻度线取不到图时的兜底色（同 <see cref="ElixirTickTint"/> 的等效暗覆盖）。</summary>
        private static readonly Color ElixirTickFallback = new Color(0.14f, 0.14f, 0.14f, 0.30f);

        /// <summary>圣水徽章（水滴底 + 数字）直径 <b>60</b>。出处：D7（18 图 bb mag：心 (63,1815)、径 60）。</summary>
        private const float ElixirBadgeD = 60f;

        /// <summary>圣水徽章中心距画布左 = <b>63</b>。出处：D7（心 x=63）。</summary>
        private const float ElixirBadgeCx = 63f;

        /// <summary>圣水徽章中心距画布底 = DesignH − y(1815) = <b>105</b>。出处：D7（心 y=1815）。</summary>
        private const float ElixirBadgeCy = 105f;

        /// <summary>
        /// 条端图元 `bar_end`（`ui_out/158`，原生 10×59，出处 `策划/战斗HUD素材索引.md` §3.1）的显示宽
        /// = 条高 44 × 10/59 ≈ <b>7.5</b>（等比缩放 ⇒ 端头不被拉扁，⛔ 不横向拉成 956 宽）。
        /// </summary>
        private const float ElixirEndW = ElixirBarH * 10f / 59f;

        /// <summary>
        /// 卡内文字（**费用数字**）字号 <b>32</b>。
        /// <para>
        /// <b>量取</b>：18 图手牌第 2 张的**白色费用数字**
        /// near_white 包围盒 = (344,1752)-(390,1777) ⇒ 字面高 ≈ 25px；本项目字体字面高/字号 ≈ 0.78
        /// ⇒ 字号 ≈ 25/0.78 ≈ 32。量法：逐像素量取 §3。
        /// </para>
        /// </summary>
        private const int CardFontSize = 32;

        // ── 卡框 + 卡面（两个节点）在卡槽里的贴合 ──
        //
        // **卡框**：`ResPaths.SlotCard`（`ui_out/43`，原生 107×159）铺满整个卡槽 `CardW×CardH`；
        //   `CrUiStyle.Skin(corner: HudCardBodyCorner)` 的九宫格角块**按 1:1 绘制**
        //   ⇒ 卡框那圈深色**恒为 6px**、不随卡片缩放变化。
        //   （43 的右边没有深色边：mid-row 剖面 = x0..5 黑、x6..106 全白 ⇒ 走九宫格会在右带画出白块，
        //     `Skin` 的四角镜像路径四边都是深色边。）
        // **卡面**：画在卡框**内部**的子节点（`HandArt{i}`），不参与卡框绘制。
        //
        // <b>卡面内缩 = 5px</b>（四个方向同值）—— 口径 = 原版手牌"卡缘可见厚度"：
        //   `策划/参考图/18_对局HUD_1080x1920.jpg` 手牌第 3 张逐列中位色 —— x571..574 是亮卡缘
        //   （亮占比 0.90~0.93、色 ≈(201,202,207)），x≤570 即背景 ⇒ 卡缘可见 ≈5px（细深线 1px + 亮带 4px）。
        //   ⚠️ 卡框自带的深色环是 6px，比原版可见厚度厚 1px（差异登记 D111）。
        //   卡面尺寸因此 = 136−2×5 × 171−2×5 = **126×161**（四边同值 ⇒ 卡面中心与卡槽中心重合）。
        //
        // 内缩按**比例**折到 `CardW`/`CardH`（本文件画布单位 = 画布像素）；
        // 卡面尺寸 = `CardW×(1−左−右)` × `CardH×(1−上−下)`。
        // ⚠️ 「下一张」小卡按**同一比例**套用（原版是同一张卡设计按比例缩小）——见 `BuildNextPreview`。

        /// <summary>卡面在卡框里的**左**内缩比例 = 5/136（口径见上方「卡框 + 卡面」段）。</summary>
        private const float ArtInsetLeftFrac = 5f / 136f;

        /// <summary>卡面在卡框里的**右**内缩比例 = 5/136。</summary>
        private const float ArtInsetRightFrac = 5f / 136f;

        /// <summary>卡面在卡框里的**上**内缩比例 = 5/171。</summary>
        private const float ArtInsetTopFrac = 5f / 171f;

        /// <summary>卡面在卡框里的**下**内缩比例 = 5/171。</summary>
        private const float ArtInsetBottomFrac = 5f / 171f;

        /// <summary>卡面宽占卡宽的比例 = 1 − 左内缩 − 右内缩（= 126/136 ≈ 0.9265）。</summary>
        private const float ArtFillX = 1f - ArtInsetLeftFrac - ArtInsetRightFrac;

        /// <summary>卡面高占卡高的比例 = 1 − 上内缩 − 下内缩（= 161/171 ≈ 0.9415）。</summary>
        private const float ArtFillY = 1f - ArtInsetTopFrac - ArtInsetBottomFrac;

        /// <summary>卡面中心相对卡槽中心的纵向偏移比例 =（下内缩 − 上内缩）/2（四边同值 ⇒ 恒 0）。</summary>
        private const float ArtOffsetYFrac = (ArtInsetBottomFrac - ArtInsetTopFrac) * 0.5f;

        /// <summary>
        /// 手牌费用泡宽 = <b>40</b>。
        /// <para>
        /// <b>（位置 + 尺寸都按 18 量取）</b>：旧值 50 出自 `07_卡组编辑` 的"卡角泡"，
        /// 位置也放在**卡左上角**。18 图对局手牌第 2 张实测：泡（=`IconElixirDrop` 水滴）宽 ≈ 38~40
        /// （= 卡宽 140 的 0.286），且**挂在卡底中央**（不是卡角）⇒ 泡宽取 40、位置见
        /// <see cref="CostIconBottom"/>。量法：逐像素量取 §3
        /// （原版白色数字 bbox 中心 x=367 ≈ 卡中心 357；泡下沿 y≈1783 ≈ 卡底 1785）。
        /// </para>
        /// </summary>
        private const float CostIconW = 40f;

        /// <summary>
        /// 费用泡**中心**距卡底 = <b>25</b>（泡心在卡内、水平居中）。
        /// <para>
        /// 出处：18 图手牌第 2 张 —— 泡内白色数字 near_white bbox (344,1752)-(390,1777) 中心 y = 1764.5、
        /// 卡底 y = 1785 ⇒ 泡心距卡底 ≈ 20；泡的下沿 y ≈ 1783（贴住卡底）⇒ 泡心 = 卡底 + 泡高/2
        /// ≈ 25（泡高 = 40 × 69/57 ≈ 48.4 ⇒ 半高 24.2）。取 25。
        /// </para>
        /// </summary>
        private const float CostIconBottom = 25f;

        /// <summary>拖动幽灵卡的宽 = **单卡宽**（跟着指针走的就是那张卡本身）。</summary>
        private const float GhostW = CardW;

        /// <summary>拖动幽灵卡的高 = **单卡高**（⛔ 不是 `CardH * 0.5`：那会把卡片压成一条"小图标"）。</summary>
        private const float GhostH = CardH;

        // ── 顶部右：倒计时（出处：§1.3 D16，18 图 `z1_18_top_x2` 读数） ──

        /// <summary>计时板宽 <b>198</b>。出处：D16（x 882..1080）。
        /// 板素材 = `HudTopRightPlate`（`ui_out/193`，原版 `HUD_topRight` 的底板，原生 212×124，
        /// 出处 `策划/战斗HUD素材索引.md` §3.4）⇒ 按量取值 198×100 铺（0.93×/0.81× 轻微缩放；
        /// 该帧**切边未量到** ⇒ 用 border = 0 的整幅拉伸，差值登记在 `策划/验收表.md`（D63））。</summary>
        private const float TimerBoxW = 198f;

        /// <summary>计时板高 <b>100</b>。出处：D16（y 0..100）。</summary>
        private const float TimerBoxH = 100f;

        /// <summary>计时板离画布顶 = <b>0</b>（贴顶）。出处：D16（y 起点 = 0）。</summary>
        private const float TimerBoxTop = 0f;

        /// <summary>计时数字字号：现状 <b>48</b>（09 图「2:32」字形高 40px × 0.8696 ÷ 0.72 ≈ 48）。
        /// ⚠️ §1.3 未重标定字号 ⇒ 保持现状（字号未量到，见几何量取.md §2）。</summary>
        private const int TimerFontSize = 48;

        /// <summary>计时板内时钟图标宽 = 板高 × 0.30 = <b>30</b>（本项目自定：原版 `Clock_middle`
        /// （`ui_out/042`，原生 15×15，出处 索引 §3.4）的**摆放尺寸未解出** —— 索引 §2.1 第 2 条明说
        /// 「摆放尺寸要等坐标解出或按原版截图量」⇒ 不编绝对值，按板高比例给；见几何量取.md §2）。</summary>
        private const float ClockIconW = TimerBoxH * 0.30f;

        /// <summary>
        /// 计时板**板面**底色（半透明冷灰紫）。
        /// <para>
        /// <b>⛔ 不能用 `ui_out/177`（1×1 **不透明黑**）+ `NineSlice` 铺</b>：那样板面是"近黑"，
        /// 与原版"透出场景的半透明板"不同。原版同窗口板面在 y=50 的逐点采样是
        /// (63,61,75)/(81,77,91)/(40,38,43)/(49,46,65)/(46,37,54) ⇒ 均值 ≈ <b>(56,52,66)</b>，
        /// 而我方同一行是 (21,33,16)/(20,32,16) ⇒ 板面明显更黑（`cr-v2-hud-measure.py` §4）。
        /// </para>
        /// <para>
        /// <b>取值口径</b>：板面件换成**原版浅色实心件** <see cref="ResPaths.SlotCardPlain"/>（`ui_out/531`）
        /// + tint（`CrUiStyle.Skin` 的 `tint` 参数，既有的 tint 机制）。反解 tint 使实机合成值 ≈ 原版 (56,52,66)：
        /// tint × 531 的中心色 (216,228,255) ≈ (35,34,77)，在 α=0.80 下压在本机背景上
        /// ⇒ 0.80×(35,34,77) + 0.20×背景 ≈ (56,52,66)。⛔ 不改任何几何（`TimerBoxW/H/Top` 仍是 D16 的冻结值）。
        /// </para>
        /// <para>
        /// ⚠️ **归因分离**：18 图右上板底是**石塔**、本项目该处是**草地/金路**
        /// （8 图实测：我方 (900,50) = (251,199,64) 金光路面）⇒ 板面合成色**不可能**逐像素相等，
        /// 本项只负责"板自身的色/透明度"，残余差归**竞技场美术**（CR-T6 域）。
        /// </para>
        /// </summary>
        private static readonly Color TimerPlateTint = new Color(0.16f, 0.15f, 0.30f, 0.80f);

        /// <summary>
        /// 计时板**外框**（`ResPaths.HudTopRightPlate` = `ui_out/193`）的不透明倍乘 = <b>(1,1,1,0.35)</b>。
        /// <para>
        /// **为什么**：193 的环是**纯黑 (0,0,0,255)**（逐点实测 row y=2 / col x=2 / mid row 全为
        /// (0,0,0,255)）⇒ 它对任何 rgb 乘算都还是黑 ⇒ 只能压 **alpha**。板面亮起后这圈黑环会显出来
        /// （3× 实机放大是一圈粗黑框），而原版 18 该处只有一条暗边
        /// （y=20 行采样 (74,70,97)/(82,79,90) 无黑、y=95 行才是 (2,0,8) 的暗底）。
        /// ⇒ 0.35 让环变成"一条压暗的边"。⛔ 不改几何、⛔ 不换素材（那是自造）。
        /// </para>
        /// </summary>
        private static readonly Color TimerFrameTint = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>计时板标题「剩余时间」字号 = <b>20</b>。（旧值 = `CrUiStyle.FontSmall` 24）。
        /// 出处：18 图板内标题的 near_white 命中仅 15px、bbox (925,10)-(990,28) ⇒ **字面高 ≈ 18**；
        /// 我方旧值实测 bbox (948,10)-(1043,33) = 95×23 ⇒ 字面高 ≈ 23，比原版大 5px。
        /// 20 × 0.78 ≈ 15.6，加 2px 描边 ⇒ ≈ 18 ✔。</summary>
        private const int TimerLabelFontSize = 20;

        // ── 顶部左：冠数（未量到 ⇒ 位置/尺寸保持现状，只把图元换成原版三件） ──
        //
        // 出处：几何量取.md §2 C4 —— 冠数在 02/09 + 18/19/20/21/23 七张图顶部各扫一遍，**均未见冠数控件**
        // ⇒ 位置/尺寸**保持现状**（沿用原版顶部左块的落点）。图元 = 原版三件
        // （`策划/战斗HUD素材索引.md` §1 第 3 行 + §3.3）：
        //   `HudScoreNamePlate`（`ui_out/196`，原版 `printScore_*` 的裸子件，原生 247×56）
        //   `HudStarPlayer`（`ui_out/187`，原版 `starPlayer`/`star1..3`，原生 120×98）
        //   `HudStarEnemy`（`ui_out/188`，原版 `starEnemy`，同尺寸）。

        // 冠数控件：**整块定位 + 定形**（出处 = 18 图量取，`cr-v2-hud-measure.py` §5/§6）。
        //   ⛔ 不用"左上角一块 260×59 的名条 + 左右两枚冠徽 + 『0 : 0』"那套本项目自定值
        //   （其位置/尺寸在几何量取.md §2 C4 记为未量到）：
        //     · 顶部**中央**：purple 命中 bbox = (477,0,571,36) px=838（窗口 455..585 × 0..46）；
        //       窗口内 gold bbox = (455,0,564,26) px=560、white bbox = (455,0,567,33) px=530；
        //     · 左上窗口 (10,10)-(300,92)：near_white 命中的是**场景石塔/金饰**（原版该处无冠数板）。
        //   ⇒ 冠数 = **顶部中央的徽章**，不是左上名条。本项目按原版位置重建（图元全部取原版）。
        //
        //   ⚠️ 18 与 2.1.5 的徽章**底色不同**（18 = 紫底 (164,34,160)/(221,44,222) 圆徽；
        //   2.1.5 的权威冠徽 = `ui_out/187` 蓝底 / `188` 红底，见 `策划/战斗HUD素材索引.md` §3.3）
        //   ⇒ 我方**紫底**那一枚 = 原版 `SlotCorner`（`ui_out/11`，24×24 圆角件）染成 **18 量取的紫**；
        //   对方那一枚 = 直接用原版 `HudStarEnemy`（`ui_out/188`，`starEnemy`）原色，⛔ 不给它编颜色。

        /// <summary>徽章直径 = <b>40</b>。出处：18 图顶部中央紫徽的 5× 放大圈定 = x 476..516
        /// （crop (450,0,600,50) 逐像素取色）⇒ 圆径 ≈ 40；
        /// 它的可见高度只有 36 是因为**上沿被屏顶裁掉**（圆心上移出屏）⇒ 圆径取 40、上沿取 -4。</summary>
        private const float CrownMedalD = 40f;

        /// <summary>我方徽章**中心离画布中线** = <b>-44</b>（= 紫徽圆心 x 496 − 画布中线 540）。
        /// 出处：18 图紫徽 x 476..516 ⇒ 圆心 496；画布宽 1080 ⇒ 中线 540。</summary>
        private const float CrownMedalMineDx = -44f;

        /// <summary>两枚徽章间距 = <b>6</b>（本项目自定：原版该处只出现一枚徽，18 图给不出两枚并排的量值；
        /// 取 6 使两枚 (36+6+36=78) 仍落在原版量到的 94px 窗口内）。</summary>
        private const float CrownMedalGap = 6f;

        /// <summary>徽章上沿相对屏顶 = <b>-4</b>。出处：18 图紫徽的**可见** bbox y 起点 = 0、
        /// 但圆径量到 40 而可见高只有 36 ⇒ 上沿在 y = -4（徽章有一小截被屏顶裁掉，原版就是这样）。</summary>
        private const float CrownMedalTop = -4f;

        /// <summary>徽章内金色冠徽宽 = 徽章径 × 0.62 ≈ <b>22</b>（本项目自定：18 图紫徽内的金冠被白数字压住、
        /// 逐像素分不出边界；按"金冠占徽章内约六成宽"给，且金冠件取原版 <see cref="ResPaths.IconCrownGold"/>）。
        /// </summary>
        private const float CrownInnerCrownW = CrownMedalD * 0.62f;

        /// <summary>我方徽章底色 = 18 图量取的紫。实测紫核像素 (164,34,160)/(171,35,161)/(221,44,222)
        /// （`cr-v2-hud-measure.py` §5 + `cr-v2-explore2.py` 的 col x=480/500/520 采样）⇒ 取 (0.671,0.137,0.631)。
        /// 用法 = 原版 `SlotCorner` 的 tint（原素材是白色件 ⇒ tint 即最终色，⛔ 不是"给素材加滤镜"）。</summary>
        private static readonly Color CrownMineTint = new Color(0.671f, 0.137f, 0.631f, 1f);

        /// <summary>冠数数字字号 = <see cref="CrUiStyle.FontBody"/>（32）。
        /// 出处：18 图白色数字 near_white bbox (455,0,567,33) 在 36px 徽章内 ⇒ 字面高 ≈ 24 ⇒ 字号 ≈ 30~32。</summary>
        private const int CrownFontSize = CrUiStyle.FontBody;

        // ── 原版 HUD 图元的切边 ──
        //
        // 切边口径（圣水条 155/157/158、手牌槽 200、计时板 193、按钮底板 163）：
        //   ① `ui_out/516/517/518` 的切边不适用于当前帧 ⇒ 本文件不设这三条切边；
        //   ② 当前帧里 `bar_bg`(155) 是 **1×74 的 1 像素宽竖条**、`bar_body`(157) 是 **59×1 的细线**
        //      （出处 索引 §3.1）⇒ 原版本来就靠矩阵拉伸铺，**切边无定义**，⛔ 不给细线编切边；
        //   ③ 其余新帧（200/193/163）的切边**未量到**（索引只给整幅 bbox，未做逐列/逐行差分）
        //      ⇒ 一律用 `Vector4.zero`（= 整幅拉伸，**不是**九宫格），使用处均注明"切边未量到"。

        /// <summary>整幅拉伸（⛔ 不是九宫格）：给细长条图元，以及"切边未量到"的整幅面板用。</summary>
        private static readonly Vector4 BorderNone = Vector4.zero;

        /// <summary>
        /// 轨道**右端圆头件** = 原版 `ui_out/156`（`elixir_bar` 的**同容器裸子件**，原生 15×75）。
        /// <para>
        /// <b>为什么需要它</b>：原版 18 图轨道右端是**深色圆头**（右下角实测 (1,11,38)，
        /// ），而 `bar_bg`(155) 是 **1 像素宽**竖条 ⇒ 横向铺开后左右两端必然是**直角**
        /// （1px 宽的图没有圆头），且 原本摆在轨道右端的 `bar_end`(158) 是**品红**件 ⇒
        /// 实机在轨道右端露出一截**孤立的品红**（实机截图），与原版该处的深色圆头不符。
        /// 156 的剖面（剖面实测）：内部 (32,32,32,**255**) 不透明、**右端带圆角**
        /// （col13/14 只覆盖 y10..63）⇒ 正是"深色 + 圆头"的端件，且不透明 ⇒ 实机必然渲染。
        /// </para>
        /// <para>出处：`策划/战斗HUD素材索引.md` §3.1「`elixir_bar` 同族件 = frame 156 / 15×75」。⛔ 帧号非推断。</para>
        /// </summary>
        private static readonly string ElixirTrackEnd = ResPaths.UiFrame(ResPaths.UiBarsDir, ResPaths.UiSrcUi, 156);

        /// <summary>
        /// 计时板**实心底** = 原版 `ui_out/177`（`HUD_topRight` 的**无名子件**，原生 1×1）。
        /// <para>
        /// <b>为什么必须有</b>：`HUD_topRight` 的底板 `193` 经逐列/逐行剖面实测是**空心圆角框**
        /// （只有 1~2px 的边是不透明的，内部全透明）⇒ 单用它画出来的"计时板"
        /// 内部是**透出竞技场**的。
        /// 原版把**实心**交给同容器的 1×1 子件 177（索引 §3.4 原文：「`HUD_topRight`(15 帧) 的无名子件 | 1005 | 177(1×1) / 193(212×124)」）
        /// ⇒ 按原版结构：先铺 177（实心）、再压 193（外框）。
        /// </para>
        /// <para>本帧由 `tools/probes/copy-ui-assets.py` 落地（`("ui_out", 177, "HudTimerPlateFill")` 一行），
        /// 与相邻帧同导入口径；`LoadAll&lt;Sprite&gt;` 有断言把关。</para>
        /// </summary>
        private static readonly string TimerPlateFill = ResPaths.UiFrame(ResPaths.UiPanelsDir, ResPaths.UiSrcUi, 177);

        /// <summary>轨道端件 `156`（15×75）的显示宽 = 条高 44 × 15/75 ≈ <b>8.8</b>（⛔ 不横向拉成 956 宽）。</summary>
        private const float ElixirTrackEndW = ElixirBarH * 15f / 75f;

        /// <summary>
        /// 手牌卡槽底 `200`（96×137）的**切边** = (左 10, 下 8, 右 10, 上 8)。
        /// <para>
        /// <b>量到的</b>（逐行/逐列 alpha 剖面）：
        /// 上边第 0 行只覆盖 x11..83 且到第 8 行才铺满 0..95 ⇒ **上圆角 ≈8**；
        /// 下边第 128 行起收窄、第 136 行只覆盖 x6..88 ⇒ **下圆角 ≈8**；
        /// 左边第 0 列只覆盖 y8..130、第 11 列起铺满 ⇒ **左圆角 ≈10**；右对称 ⇒ **右圆角 ≈10**。
        /// ⇒ 设 `Image.type = Sliced` 后四角保留原像素，⛔ 不再把 96×137 整幅非等比拉到 140×171（1.46×/1.25×）
        /// 让圆角变椭圆。
        /// </para>
        /// </summary>
        private static readonly Vector4 HudSlotBorder = new Vector4(10f, 8f, 10f, 8f);

        // `HudCardFrameBorder`（frame_547 的九宫格切边 11）随接线一并撤销 —— 该素材属于
        //   "另一版本"的观感（见本文件手牌段落的基线说明）。
        //
        // **手牌槽底换成"卡体"**（原版结构 = 卡体 + 卡面内缩，⛔ 不是"描边 + 卡面"）。
        //   实测依据（卡体帧逐像素比对）：
        //     ① 原版 18 图卡 2 的卡缘 = 蓝 HUD 底 → **1px 深色描边** → **浅灰卡体 8px+**（RGB≈(215,213,216)）；
        //        ⚠️ 18 的手牌是**灰化态**（当时 2 圣水 ⇒ 4 张卡都不可出），CR 的灰化 ≈ ×0.87 去饱和 ⇒
        //        反推**未灰化**卡体 ≈ 215/0.87 ≈ **247** ⇒ 与 `ui_out/43` 的主色 **(248,248,248)** 一致
        //        （`frame_043` 占比 0.85，且实测左/上缘有 **6px 深色带**、圆角半径 ≈20px ⇒ 正好是
        //        "卡体 + 深色描边 + 圆角"三件）。这也就是 `DeckEditPanel` 卡格底用的同一件。
        //     ② 旧槽底 `ui_out/200` 实测 **全图 α ≤ 60（23.5%）**、色 ≈(18,12,10) ⇒ 是"半透明深色覆盖层"，
        //        实机在卡缘处的像素 = **竞技场草地原色（161，纯草 ~158）** ⇒ **视觉上没有框**。
        //   ⇒ 槽底 = `ResPaths.SlotCard`（`ui_out` 43），切边沿用 deck 的实测值 20。
        /// <summary>手牌卡体（`ResPaths.SlotCard` = `ui_out` 43，原生 107×159）的九宫格切边 = **20**（四边同值）。
        /// 出处：与 `DeckEditPanel.BorderCard` 同一量取值（该帧圆角半径 ≈20px；
        /// 复核：顶行 alpha 宽度 82 → 第 15 行才到 103 ⇒ 半径确实 ≈20）。</summary>
        /// <h1>⛔ 不要用本 `border` 常量：卡体走 <see cref="HudCardBodyCorner"/> 的 `Skin(corner: 20)` 路径，
        /// 本常量无任何调用点（仅留存上述量取值）。</h1>
        private static readonly Vector4 HudCardBodyBorder = new Vector4(20f, 20f, 20f, 20f);

        /// <summary>
        /// 卡体（`ResPaths.SlotCard` = `ui_out` 43）的**四角镜像边长** = <b>20</b>（= 该帧的圆角半径）。
        /// <para>
        /// <b></b>：卡体改走 `CrUiStyle.Skin(..., corner: 20, ...)`（`MakeRounded`：取该帧左上
        /// 20×20 的四角/四边镜像拼成对称九宫格）而不是 `NineSlice` —— 因为实测 43 的**右边没有深色边**
        /// （mid-row 剖面 x0..5 黑、x6..106 白），`NineSlice` 会把白块画到卡的右边
        /// （实机读数 `x 421..427 = (248,248,248)`）。
        /// 量法：逐像素量取。
        /// </para>
        /// </summary>
        private const int HudCardBodyCorner = 20;

        /// <summary>状态行文字保留时长（秒）。本项目自定：只为不让上一条提示永远留在屏幕上。</summary>
        private const float StatusHoldSeconds = 4f;

        // ───────────────────────── 真卡面（G4 改：⛔ 不再是"类型色底"） ─────────────────────────
        //
        // <b>为什么不用类型色当最终外观</b>：那是占位物（全局 skill §0.1 ②：纯色块不可交付）。
        // 现在卡面 = 原版素材帧 `ResPaths.SpellArtFrame(i)`（`ui_spells_out`）+ 原版战斗 HUD 卡槽图元
        // `ResPaths.HudHandSlot`（`ui_out/200`）底。
        //
        // <b>卡 `key` → 帧号 表从哪来（本面板不再自存一份）</b>：
        // 唯一真源 = `CrUiStyle.CardArtFrameTable`（出处 `策划/原版UI素材名称索引.md` §3.5）。
        // ⛔ 本面板不自存「同值副本」：与 `CrUiStyle` 那份不同步会让部分手牌卡面查不到
        // 帧号、只画「卡槽底 + 卡名」（"卡牌图片和框都对不上"）。上收之后**结构上不可能再不同步**。
        // 表里没有的卡**不猜帧号**：只画原版卡槽底 + 卡名（与 `DeckEditPanel` 同一降级口径）。

        /// <summary>(帧号 → 裁剪后的卡面 Sprite)。`Sprite.Create` 造的 Sprite 不归 Resources 管，必须复用。</summary>
        private static readonly Dictionary<int, Sprite> ArtCache = new Dictionary<int, Sprite>();

        /// <summary>已经 Warn 过的卡面路径（缺素材只报一次）。</summary>
        private static readonly HashSet<string> WarnedArtMissing = new HashSet<string>();

        /// <summary>拖放中的幽灵卡色调（半透白：真卡面保持原色，只降不透明度表示"跟着手走"）。</summary>
        private static readonly Color GhostColor = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>卡面帧号没登记时幽灵卡的兜底色（该卡本来就不画卡面 ⇒ 只是能看见手指下有东西）。</summary>
        private static readonly Color GhostColorFallback = new Color(1f, 1f, 1f, 0.35f);

        /// <summary>幽灵卡上的文字色（压在浅色底上，用近黑）。</summary>
        // `GhostTextColor` 随 `_ghostText` 一并删除（幽灵卡不再有文字）。

        // ───────────────────────── 运行时状态 ─────────────────────────

        private bool _built;

        private BattleStartNotify _start;
        private BattleSnapshot _snapshot;

        private int _myTeam;                                       // 0=BLUE 1=RED（Battle.Started 给出）
        private int _maxElixirMilli = GameConst.MaxElixirMilli;     // 圣水上限（1/1000）

        private int _regulationMs;                                  // 常规时间（0 = 还没拿到时间线）
        private int _overtimeMs;                                    // 加时

        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();
        private readonly int[] _handIds = new int[HandSlots];       // 每个槽位当前的卡 id（0 = 空）
        private int _nextId;

        private int _lastUnknownType = int.MinValue;
        private int _lastUnknownPhase = int.MinValue;
        private bool _handTooLongWarned;

        /// <summary>上一次打印「本局手牌帧号表」时的手牌组成（`_handIds` 拼串）—— 只在真的变了时才打印。</summary>
        private string _handSigLogged;
        // 「只告警一次」的四处（无相机 / 非正交 / 池缺失 / 无 timeline）里，
        //   前三处生命周期 = **进程级**（实例字段从不重置，且这三条说的是"环境/装配缺陷"，
        //   跨面板重开也不该再刷屏）⇒ 改用引擎的 `LogThrottle.WarnOnce`（`Runtime/Core/LogThrottle.cs:164`），
        //   ⛔ 不再自持 `_noCameraWarned` / `_nonOrthoWarned` / `_poolMissingWarned` 三个字段。
        //
        // ⚠️ 但**保留** `_noTimelineWarned`：它在 `OnStarted` 里有 `_noTimelineWarned = false;`
        //   （"新一局：时间线还没到，重新留痕"）⇒ 是**每局重置**语义，而 `LogThrottle.*Once` 是
        //   **进程级**（`LogThrottle.Reset()` 是全量清 ⇒ 会连带清掉别的系统的限频记录，是越界副作用）
        //   ⇒ 换成 WarnOnce 会让"第 2 局又没时间线"这件事静默。这与 `BattleManager` 的
        //   `_seqWarned / _roomWarned / _seqJumpWarned` 同一条理由（那三处也**必须保留**）。

        /// <summary>`start.timeline == null` 只报一次 —— **每局重置**（见 `OnStarted`），⛔ 不换 `WarnOnce`。</summary>
        private bool _noTimelineWarned;

        /// <summary>倒计时最后 <see cref="CountdownWarnMs"/> 毫秒的滴答：上一声播在"剩余第几秒"（-1 = 还没进窗口）。</summary>
        private long _lastTickSec = -1;

        /// <summary>`Game.Sound` 为空只报一次（表现域未挂载 ⇒ HUD 音效全不响，⛔ 不刷屏也不静默）。</summary>
        private static bool _sfxWarned;

        /// <summary>HUD 音效资源缺失只报一次（落地漏拷时能一眼看出，⛔ 不静默跳声）。</summary>
        private static bool _sfxMissingWarned;

        private RectTransform _root;                                // 面板根（屏幕 → 世界 换算要用它）

        // ── 顶部信息 ──
        // 冠数从"左上角一块 0 : 0 名条"改成**顶部中央两枚徽章**（原版位置，见常量段）
        //   ⇒ 一个 Text 拆成两枚徽章各自的数字（⛔ 不是把 "0 : 0" 塞进 36px 的徽章里）。
        private Text _crownsMineText;
        private Text _crownsEnemyText;
        private Text _timerText;
        private Text _phaseText;
        private Text _statusText;
        private float _statusUntil;

        // ── 圣水 ──
        private RectTransform _elixirFill;
        private Text _elixirText;

        /// <summary>徽章内的圣水**整数**（原版 `elixir_bar/elixirBarLeftNumbers` 的 `elixirAmount` 口径）。</summary>
        private Text _elixirBadgeText;

        // ── 手牌 ──
        private readonly Image[] _handCards = new Image[HandSlots];   // 卡槽底（原版 `SlotCard` 九宫格）
        private readonly Image[] _handArts = new Image[HandSlots];    // 真卡面（原版 `ui_spells_out` 帧）
        private readonly Text[] _handCosts = new Text[HandSlots];     // 圣水费用（压在原版圣水水滴上）
        // 原版卡面上没有卡名 ⇒ `_handTexts` 已整条删除（连带 `HandText{i}` 节点，
        //   见 `BuildHand` 的注释）。⛔ 不要再加回来。
        private Image _nextCard;
        private Image _nextArt;
        private Text _nextCost;
        private Text _nextText;

        // ── 拖放 ──
        private bool _dragging;
        private int _dragCardId;
        private Image _ghost;

        /// <summary>
        /// 本次拖动那张卡的**卡面**（<see cref="BeginDrag"/> 时从手牌槽取一次）。
        /// 用途 = 落点处的卡面虚影（`BattleViewRoot.ShowPlacement` 的 `cardArt`）——
        /// 指针每动一次都要重画落点，⛔ 不在那条路径上再查一次卡池。
        /// </summary>
        private Sprite _dragArt;

        /// <summary>「非 Battle 站点里按下手牌被站点守卫拦住」最近一次留痕时读到的站点名（去重标记）。
        /// 空 = 还没留过痕。⛔ 只用于日志去重，不参与任何判定。</summary>
        private string _stationGuardLogged;
        // 幽灵卡不带字（原版无卡名件）⇒ 本类没有 `_ghostText` 字段。

        // ── 订阅（OnOpen 挂 / OnClose 摘，成对） ──
        private Action<BattleStartNotify> _onStarted;
        private Action<BattleSnapshot> _onSnapshot;
        private Action<BattleEndNotify> _onEnded;
        private Action<string> _onFailed;
        private Action<CardInfo[]> _onPoolLoaded;

        /// <summary>对局 HUD 是 `Battle` 站点的 Normal 层面板（架构契约 §4）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }

            Subscribe();
            RefreshAll();
            SetStatus("拖动手牌到场上松手即请求出牌；最终合法性由服务端裁决。", CrUiStyle.TextDim);
        }

        public override void OnClose()
        {
            Unsubscribe();
            CancelDrag("面板关闭");
        }

        // ───────────────────────── 视觉树（D1：OnOpen 里用 UIFactory 自建） ─────────────────────────

        private void Build()
        {
            _root = (RectTransform)transform;
            UIFactory.Stretch(_root);

            // HUD **不铺全屏底、不挡射线**：对局画面要能看见（与站点面板不同）。
            // 也⛔不铺全屏 Image：那会挡住操作（本面板的输入是自己轮询的，但挡射线仍会干扰其它 UI）。

            BuildTopLabels();
            BuildElixir();
            BuildHand();
            BuildNextPreview();
            BuildGhost();
            BuildPauseButton();
        }

        private void BuildTopLabels()
        {
            // ── 顶部**中央**：冠数徽章（整块从"左上名条"搬到这里 + 重定形）──
            // 位置出处：18 图 purple 命中 bbox = (477,0,571,36)（窗口 455..585 × 0..46），
            //   即紫徽中心 x≈495 / y 起点 0；原版**左上**窗口 (10,10)-(300,92) 的 near_white 是场景石塔，
            //   ⇒ 冠数控件在顶部中央，不在左上。量法：逐像素量取 §5/§6。
            // 结构：左 = 我方（原版 `SlotCorner`(11) 染成 18 量取的紫 + 原版金冠 `IconCrownGold`(50) + 白数字）
            //       右 = 对方（原版 `HudStarEnemy`(188) 原色 + 白数字）
            // 左右 = 我方/对方 的固定口径（HUD 是玩家视角，`printScore_player/_enemy` 同理），
            //   ⛔ 不随 `_myTeam` 翻转。
            var mineMedal = CrUiStyle.Skin("CrownMedalMine", _root, ResPaths.SlotCorner, 0, BorderNone,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(CrownMedalMineDx, -CrownMedalTop), new Vector2(CrownMedalD, CrownMedalD),
                CrownMineTint, false, CrownMineTint);

            // 徽章内的原版金冠（`ui_out/50`，106×85，原版 `IconCrownGold`）：按素材比例缩到 22 宽、居中。
            CrUiStyle.AspectImage("CrownIconMine", mineMedal.rectTransform, ResPaths.IconCrownGold, CrownInnerCrownW,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, CrUiStyle.Accent);

            _crownsMineText = UIFactory.CreateText("CrownsMine", mineMedal.rectTransform, "0", CrownFontSize,
                TextAnchor.MiddleCenter, CrUiStyle.TextColor);
            UIFactory.Stretch(_crownsMineText.rectTransform);

            var enemyBox = UIFactory.CreatePanel("CrownMedalEnemy", _root, Color.clear, false);
            UIFactory.Place(enemyBox.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(CrownMedalMineDx + CrownMedalD + CrownMedalGap, -CrownMedalTop),
                new Vector2(CrownMedalD, CrownMedalD));

            CrUiStyle.AspectImage("StarEnemy", enemyBox.rectTransform, ResPaths.HudStarEnemy, CrownMedalD,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, CrUiStyle.Accent);

            _crownsEnemyText = UIFactory.CreateText("CrownsEnemy", enemyBox.rectTransform, "0", CrownFontSize,
                TextAnchor.MiddleCenter, CrUiStyle.TextColor);
            UIFactory.Stretch(_crownsEnemyText.rectTransform);

            // ── 右上：倒计时（原版位置就是右上角，且**贴顶贴右**：出处 几何量取.md §1.3 D16  x 882..1080 / y 0..100）──
            // 板素材 = `HudTopRightPlate`（原版 `HUD_topRight` 的底板 `ui_out/193`，原生 212×124，索引 §3.4）；
            // 193 实测是**空心圆角框**（内部全透明）⇒ 先用实心件铺底，再压 193 外框。
            //   ① 板面件 = **原版浅色实心件 531 + 量取 tint**（反解使实机板面 ≈ 原版 18 图同一处的
            //      采样值，推导见 `TimerPlateTint` 注释）；⛔ 不用 `ui_out/177`（不透明黑）。
            //   ② `193` 的环是**纯黑 (0,0,0,255)**（逐点实测 row/col/mid）—— 板面变亮后这条黑环会
            //      从"看不见"变成**一圈粗黑框**（实机 3× 放大可见），而原版 18 该处只有**一条暗边**。
            //      ⇒ 用 `Skin(..., tint)` 把环的 alpha 降到 0.35（⛔ 不改几何、⛔ 不换素材，只调不透明度）。
            CrUiStyle.Skin("TimerPlateFill", _root, ResPaths.SlotCardPlain, 0, BorderNone,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TimerBoxTop),
                new Vector2(TimerBoxW, TimerBoxH), TimerPlateTint, false, TimerPlateTint);

            var timerBox = CrUiStyle.Skin("TimerBox", _root, ResPaths.HudTopRightPlate, 0, BorderNone,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -TimerBoxTop),
                new Vector2(TimerBoxW, TimerBoxH), TimerFrameTint, false, TimerFrameTint);

            // `ClockIcon`（原版 `Clock_middle`/`ui_out/042`）**不在** 18 图的计时板里
            //   （18 图板内只有「剩余时间:」+ 大字，）；
            //   ⛔ 原版没有的东西不加 ⇒ 整条节点删除，文字区改为**整板居中**（旧代码为避开图标把文字右移了
            //   `ClockIconW*0.5`，实测把标题中心推到 x≈995，而原版是 957）。
            //   ⛔ 不动 `TimerBoxW/H/Top`（D16 冻结几何）。
            var textW = TimerBoxW - 12f;
            const float textX = 0f;

            // 原版 18 图这两行都是**白字 + 黑描边**（实测「1:51」是
            //   白字黑描边、「剩余时间：」同为白字黑描边），实机原来是 `Accent`(金) + `TextDim`(灰蓝) 无描边
            //   ⇒ 压在深色板/竞技场上都读不出原版那种"白字压深底"的观感。描边件走 `CrUiStyle.Outlined`
            //   （全项目唯一的描边文字件，⛔ 不在这里自造 Outline）。
            var timerLabel = CrUiStyle.Outlined("TimerLabel", timerBox.rectTransform, "剩余时间",
                TimerLabelFontSize,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(textX, -6f), new Vector2(textW, 30f), TextAnchor.MiddleCenter);
            if (timerLabel != null) timerLabel.color = CrUiStyle.TextColor;

            _timerText = CrUiStyle.Outlined("Timer", timerBox.rectTransform, "--:--", TimerFontSize,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(textX, -36f), new Vector2(textW, 62f), TextAnchor.MiddleCenter);

            // 阶段（常规 / 加时 / 已结束）：顶部居中、**冠数徽章之下**（原版该处是场景区，本项目自定：原版无此文字条）。
            // ⛔ 这一行必须落在徽章**下方**：冠数徽章在**顶部正中**，实占 px x 476..516 / y −4..36
            //    （出处 18 图 purple bbox；另有实机 dump 与节点树读数交叉验证）。
            //    若取原 `-10` ⇒ px y 10..40 ⇒ **与徽章重叠 26px**（「常规时间」四字压到紫徽 + 金星上）。
            //    徽章坐标是**量取来的**、⛔ 不让位 ⇒ 让这一行让位：px y **44..74**（徽章底边 y=36 之下留 8px 缝）。
            //    阶段/状态两行是本项目自定件（验收表 D34 已登记「多出 Phase/Status 两行」），
            //    ⛔ 不动任何量取来的几何（TimerBox 冻结几何、徽章位、手牌、圣水条）。
            _phaseText = UIFactory.CreateText("Phase", _root, string.Empty, CrUiStyle.FontSmall,
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);
            UIFactory.Place(_phaseText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -44f), new Vector2(420f, 30f));

            // 状态行（提示 / 服务端拒因）：阶段下一行。本项目自定（原版无此文字条）。
            // ⛔ 同上让位：原 `-44` 正是阶段行现在的位置 ⇒ 下移到 `-78`（px y 78..108）。
            // 宽度 900 → 680 是为了**不压右上计时板**：计时板 TimerBox 实占 px x 882..1080 / y 0..100
            //   900 居中 ⇒ x 90..990，与板在 x 882..990 上相交；
            //   680 居中 ⇒ x 200..880 ⇒ 与计时板零相交（状态行会显示服务端拒因，不许被板压住）。
            _statusText = UIFactory.CreateText("Status", _root, string.Empty, CrUiStyle.FontSmall,
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);
            UIFactory.Place(_statusText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -78f), new Vector2(680f, 30f));
        }

        private void BuildElixir()
        {
            // 圣水条：原版在**手牌下方**（条 y1797..1841、卡 y1614..1785。出处 几何量取.md §1.3 D1/D2/D12）
            // ⇒ 用左下角锚点定位在 (88, 79)、尺寸 956×44（D4/D5/D3）。
            // 三件套 = A 的原版 `elixir_bar` 子元件（出处 `策划/战斗HUD素材索引.md` §1 第 1 行 + §3.1）：
            //   槽底 `bar_bg`(`ui_out/155`, 1×74) + 填充 `bar_body`(`ui_out/157`, 59×1) + 条端 `bar_end`(`ui_out/158`, 10×59)。
            // ⛔ 155/157 是 1 像素宽/高的细线（原版靠矩阵拉伸铺）⇒ 切边无定义，用 `BorderNone` 整幅拉伸，不编切边。
            var track = CrUiStyle.NineSlice("ElixirTrack", _root, ResPaths.ElixirBarTrack, BorderNone,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(ElixirBarLeft, ElixirBarBottom), new Vector2(ElixirBarW, ElixirBarH),
                CrUiStyle.FieldBg, false);

            // **填充区**（正好占刻度栅格那一块，见 `ElixirTickGridLeft`）。
            //   为什么必须有这一层：`UIFactory.SetBarWidth` 是按**父节点宽度**的比例摆填充的
            //   （`UIWidgetControls.cs:238-246`：`anchorMax.x = p`、`offsetMin/Max` 归零）⇒ 填充若直接挂在
            //   轨道（956 宽）上，2/10 时右沿 = 88 + 191.2 = 279，而 18 图实测 = 289~290（差 ~11px）。
            //   挂进"左沿 102、宽 937.6"的容器后：2/10 右沿 = 102 + 0.2×937.6 = 289.5 ✔
            //   ⇒ 每 1.0 圣水 = 正好 1 格（用户判词「一个圣水不是一个格子吗」），同时消解已登记的 D58。
            var fillArea = UIFactory.CreateNode("ElixirFillArea", track.rectTransform);
            UIFactory.Place(fillArea, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(ElixirFillInsetLeft, 0f), new Vector2(ElixirTickGridW, ElixirBarH));

            // ⛔ 不用无 sprite 的 `Image.fillAmount` 画进度（sprite 空时它走实心四边形分支、静默失效，
            //    见 `UIWidgetControls.cs:232-238`）；用引擎给的 `SetBarWidth`。
            // 锚点/轴心 (0,0) + 尺寸零 = 与 `LoadingPanel` 完全相同的口径（那是已验证可跑的条目填充写法）。
            var fill = CrUiStyle.NineSlice("ElixirFill", fillArea, ResPaths.ElixirBarFill, BorderNone,
                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero,
                CrUiStyle.Accent, false);
            _elixirFill = fill.rectTransform;
            UIFactory.SetBarWidth(_elixirFill, 0f);

            // 条端（两个用途分开、⛔ 不混用一个件）：
            //   ① 轨道右端 = `156`（原版 `elixir_bar` 同族裸子件，**深色 + 右端圆头 + 内部不透明**）。
            //      出处：索引 §3.1；原版 18 图轨道右端实测 (1,11,38) 就是深色圆头。
            //      实机判据：剖面实测其内部 (32,32,32,255)，而 155 的中间是 (0,0,0,94)。
            //   ② 填充右端 = `158`（原版 `bar_end`，10×59，**品红圆头**）—— 挂到 **fill** 的右端：
            //      158 是品红件，语义就是"填充条的圆头端"；原来挂在轨道右端时填充没铺满就会在轨道尽头
            //      露出一截**孤立品红**（实机截图），与原名/原版都不符。
            //   两件都等比缩到条高（⛔ 不横向拉成 956 宽 —— 端件拉出去会被抹成一条线，就不是原版图元了）。
            CrUiStyle.AspectImage("ElixirTrackEnd", track.rectTransform, ElixirTrackEnd, ElixirTrackEndW,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, CrUiStyle.Accent);

            CrUiStyle.AspectImage("ElixirFillEnd", fill.rectTransform, ResPaths.ElixirBarFrame, ElixirEndW,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, CrUiStyle.Accent);

            // **10 格刻度分隔**（消解 D66「刻度素材已登记键但未绘制」）。
            //   原版结构 = `elixir_bar`(clip 1080) 的子元件 **`d1`…`d9` 共 9 个**（同属 clip 909、
            //   落地帧 `ResPaths.ElixirBarTick` = `ui_out/160`，1×1），原文注明「**10 格**刻度分隔
            //   （原版同一 clip 复用 9 次）」⇒ 在第 1..9 格的分界各画一条，把条正好分成 10 格。
            //   出处：`策划/战斗HUD素材索引.md` §2 的 `⇒ elixir_bar → clip 1080 → 子元件表` 与 §3.1 表
            //   `d1…d9` 行 + §4 落地表（`Bars/ui_out frame_160`）。
            //   挂 **track**（⛔ 不挂 fill）⇒ 位置/宽度不随圣水值变化；在 fill 之后创建 ⇒ 压在填充之上
            //   （原版满条时也数得出 10 段 —— 用户判词就是"满格时能数出 10 段"）。
            //   位置 = 栅格左沿 + k×格宽（量法与残差见 `ElixirTickGridLeft`）；宽/高/不透明度见同名常量。
            for (var k = 1; k < 10; k++)
            {
                CrUiStyle.Skin($"ElixirTick{k}", track.rectTransform, ResPaths.ElixirBarTick, 0, BorderNone,
                    new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(ElixirFillInsetLeft + k * ElixirTickStep, 0f),
                    new Vector2(ElixirTickW, ElixirTickH),
                    ElixirTickFallback, false, ElixirTickTint);
            }

            // 圣水徽章（大圣水图标）：位置/直径**不改** —— 心 (63,1815)、径 60（出处 §1.3 D7，G4 片的量取值）。
            // 圣水徽章素材 = `ResPaths.IconElixirBarLeft`（`ui_out/159`，94×115）——
            //   它就是原版圣水条左侧那颗大图标（`elixir_bar`(clip 1080) → 子元件 **`elixirBarLeft`**，
            //   出处 `策划/战斗HUD素材索引.md` §3.1；159 = 大紫水滴图元）。
            //   `client/Assets/Resources/Sprites/Ui/Icons/ui_out/frame_159.png` 在盘（8391 B），
            //   `ResPaths.IconElixirBarLeft` 的键与注释见 `ResPaths.cs:423`。
            var badge = CrUiStyle.AspectImage("ElixirBadge", _root, ResPaths.IconElixirBarLeft, ElixirBadgeD,
                new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(ElixirBadgeCx, ElixirBadgeCy), CrUiStyle.Accent);

            // （原版口径）：原版 18 图上那颗水滴里就是**当前圣水整数**
            //   （放大后可读到 **白色「2」+ 黑描边**，而条上没有文字）；
            //   原 `elixirBarLeftNumbers`（`elixirAmount` 文本域，出处 索引 §3.1）也印证这个数字是**圣水值**。
            //   我方原来是「159 水滴 + 条中央文字『圣水 8.2 / 10』」⇒ 数字位置/字形都不是原版。
            //   ⇒ 数字移进徽章（白字黑描边，走全项目唯一的 `CrUiStyle.Outlined`），条上文字按原版**移除**。
            _elixirBadgeText = CrUiStyle.Outlined("ElixirBadgeText", badge.rectTransform, string.Empty,
                CrUiStyle.FontBody,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(ElixirBadgeD, ElixirBadgeD), TextAnchor.MiddleCenter);

            // 条上不再写字（原版 18 图条上是刻度分隔 + 填充，没有文字）。节点保留但停用，
            // ⛔ 不删字段（`RefreshElixir` 仍按同一字段刷新，改成"写空 + 停用"最小改动）。
            _elixirText = UIFactory.CreateText("ElixirText", track.rectTransform, string.Empty, CrUiStyle.FontSmall,
                TextAnchor.MiddleCenter, CrUiStyle.TextColor);
            UIFactory.Stretch(_elixirText.rectTransform);
            _elixirText.gameObject.SetActive(false);
        }

        private void BuildHand()
        {
            // 底排 = 手牌 4 张 + 间隙 + 「下一张」，整排居中、贴底。
            var bar = UIFactory.CreateNode("HandBar", _root);
            // 整排按**左下角**定位：左边 x = 144（§1.3 D9）、底边距画布底 = 135（D13）。
            // ⚠️ 原版这一排**不是居中**的（「下一张」在它**左边**，见 `BuildNextPreview`）⇒ 用 `TextAnchor.LowerLeft`。
            // 上一版用 `LowerCenter` + 居中推算（并因此踩过"又往左推一次、最左一张出屏"的坑）——
            // 那是把"整排居中"当了前提；这里用 18 图的量取值直接定位，⛔ 不靠居中推算。
            UIFactory.AnchoredBottom(bar, new Vector2(HandRowLeft, HandBottomOffset),
                new Vector2(HandBarW, CardH), TextAnchor.LowerLeft);

            for (var i = 0; i < HandSlots; i++)
            {
                var index = i; // 闭包捕获：槽位下标只用于建节点，卡 id 在刷新时按下标取

                // 卡框 = 原版 `ui_out/43`（18 图未灰化卡体 ≈ 247 与 43 的主色 248 一致，
                // 且 43 自带 6px 深色描边 + 圆角半径 ≈20 ⇒ 它就是"卡体 + 深色卡框"那一件），铺满整个槽位。
                // 用 `Skin(corner: 20)` 而不是 `NineSlice`：43 的**右边没有深色边**
                //   （mid-row 剖面 = x0..5 黑、x6..106 全白）⇒ 九宫格的右带会画成白块
                //   （右侧出现 6px 白色条，与另外三边的深色边不对称）。
                //   `Skin` 走引擎既有的"取左上 20×20 四角镜像拼"路径（`CrUiStyle.MakeRounded`，
                //   014/019/165 等件同一条路）⇒ **四边都是 6px 深色边**。
                var card = CrUiStyle.Skin($"Hand{index}", bar, ResPaths.SlotCard, HudCardBodyCorner, BorderNone,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(index * (CardW + CardGap), 0f), new Vector2(CardW, CardH),
                    CrUiStyle.ButtonBg, false);
                _handCards[index] = card;

                // 卡面：原版 `ui_spells_out` 帧（透明包围盒裁掉），画在卡框**内部**（后建 ⇒ 画在卡框之上）。
                // 位置/尺寸按量取的 4 边内缩 = 5px（见 `ArtInset*Frac` 上方那段）。
                var art = UIFactory.CreatePanel($"HandArt{index}", card.rectTransform, Color.white, false);
                UIFactory.Place(art.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, CardH * ArtOffsetYFrac), new Vector2(CardW * ArtFillX, CardH * ArtFillY));
                art.gameObject.SetActive(false);
                _handArts[index] = art;

                // **卡面上不再画卡名** —— 原版对局手牌的卡面上没有卡名（出处：
                //   `策划/参考图/20_对局_1080x1920.jpg` 手牌排的逐格量取：卡面上只有卡面图 + 左上圣水泡，
                //   没有文字带）。可读性由"圣水数字 + 卡面本身"承担；排查靠日志的
                //   `本局手牌帧号表`（key → 帧号）与 `卡面就绪[...]` 行，⛔ 不靠屏幕上留字。
                //   ⛔ 这里**删掉节点本身**（不是置空文本）：留一个空 Text 仍会参与布局与 overdraw。

                // 圣水费用：从「卡**左上角**」移到「**卡底中央**」（原版位置）。
                //   出处：18 图手牌第 2 张的费用水滴挂在卡底中央（白色数字 near_white bbox 中心 x=367
                //   ≈ 卡中心 357；泡下沿 y≈1783 ≈ 卡底 1785）⇒ 泡心 = 卡底上方 `CostIconBottom`(25)。
                //   锚点 (0.5,0) + 轴心 (0.5,0.5) ⇒ pos 的 y 就是"泡心距卡底"。
                var costIcon = CrUiStyle.AspectImage($"HandCost{index}", card.rectTransform,
                    ResPaths.IconElixirDrop, CostIconW, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, CostIconBottom), CrUiStyle.Accent);
                var costText = UIFactory.CreateText($"HandCostText{index}", costIcon.rectTransform, string.Empty,
                    CardFontSize, TextAnchor.MiddleCenter, CrUiStyle.TextColor);
                UIFactory.Stretch(costText.rectTransform);
                _handCosts[index] = costText;
            }
        }

        private void BuildNextPreview()
        {
            // 位置纠正：「下一张」在原版里是**底排左端**的一张更小的卡
            //（出处 几何量取.md §1.3 D14：x 33..97 / y 1634..1716 ⇒ 64×83），标签在它下方（D15）。
            // 上一版把它放在右端是错的 —— 那一版做的时候基线图底部被宣传字压住、该项未量到；§1.3 已用 18 图补量。
            // 卡槽底沿用同一件原版槽底 `HudHandSlot`（原版 `slots`，索引 §3.2）。
            // 与手牌同一条口径 —— `Skin(corner: 20)`（四边都有深色卡框，见手牌处注释）。
            _nextCard = CrUiStyle.Skin("NextCard", _root, ResPaths.SlotCard, HudCardBodyCorner, BorderNone,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(NextLeft, NextBottomOffset),
                new Vector2(NextW, NextH), CrUiStyle.ButtonBg, false);

            var label = UIFactory.CreateText("NextLabel", _root, "下一张", CrUiStyle.FontSmall,
                TextAnchor.MiddleCenter, CrUiStyle.TextDim);
            UIFactory.AnchoredBottom(label.rectTransform, new Vector2(NextLabelLeft, NextLabelBottom),
                new Vector2(NextLabelW, NextLabelH), TextAnchor.LowerLeft);

            // 贴合值与手牌**同一比例**（原版「下一张」就是同一张卡设计按比例缩小；
            //   它在量取用的基线图里只有 66px 宽，逐像素量内缩的误差会放大到 1.5%/px ⇒ 不单独编数）。
            _nextArt = UIFactory.CreatePanel("NextArt", _nextCard.rectTransform, Color.white, false);
            UIFactory.Place(_nextArt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, NextH * ArtOffsetYFrac), new Vector2(NextW * ArtFillX, NextH * ArtFillY));
            _nextArt.gameObject.SetActive(false);

            // 费用数字从"卡左上角裸字"改成"压在**原版圣水水滴**上、贴卡底中央"
            //   —— 与手牌同一口径（原版手牌的费用泡都在卡底中央）。泡径按「下一张」卡宽等比缩到
            //   `NextW/CardW × CostIconW`，⛔ 不另编一个数。
            var nextCostIcon = CrUiStyle.AspectImage("NextCostIcon", _nextCard.rectTransform,
                ResPaths.IconElixirDrop, CostIconW * NextW / CardW,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, CostIconBottom * NextW / CardW), CrUiStyle.Accent);
            _nextCost = UIFactory.CreateText("NextCost", nextCostIcon.rectTransform, string.Empty, CardFontSize,
                TextAnchor.MiddleCenter, CrUiStyle.TextColor);
            UIFactory.Stretch(_nextCost.rectTransform);

            _nextText = UIFactory.CreateText("NextText", _nextCard.rectTransform, "—", CardFontSize,
                TextAnchor.LowerCenter, CrUiStyle.TextDim);
            UIFactory.Place(_nextText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(NextW - 6f, 28f));
        }

        private void BuildGhost()
        {
            // 拖放幽灵卡：跟着指针走的小色块 + 卡名。**中性色**（不做合法/非法判定，见类注释）。
            _ghost = UIFactory.CreatePanel("DragGhost", _root, GhostColor, false);
            // 锚点/轴心都放中心，位置在 `MoveGhost` 里直接写 `position`（世界坐标），不依赖 anchoredPosition 口径。
            UIFactory.Place(_ghost.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(GhostW, GhostH));

            // **幽灵卡上也不画卡名** —— 原版拖放时跟着手指的就是那张卡面本身，没有文字带
            //   （出处：`策划/参考图/20_对局_1080x1920.jpg` 手牌/出牌区域的逐格量取）。
            //   ⛔ 这里连节点都不建（原 `DragGhostText` 已删），不是置空。
            _ghost.gameObject.SetActive(false);
        }

        // ═══════════════ 暂停按钮 ═══════════════
        //
        // 出处：架构契约 §4「暂停」行（`Pause` 站点 ↔ `PausePanel`，Popup 层）+ 交付形态要求
        // 对局内可进暂停菜单。按钮**只发一条事件**，不直接开面板、⛔ 不认识 `CR.Module`（契约 §1）：
        // `Events.Flow.StationEnterRequest` 是"请求切到某站点"的唯一入口（`Events.cs:47-52` 的注释
        // 写明它的用途就是让这类面板请求进 `Pause` / 回 `Battle`）。`AppFlow` 收到后切站点，
        // `CR.UI.BattleUiHost` 收到请求 / 站点变化后开 `PausePanel`（面板的开关只有那一处）。
        // 位置选**右上角**：左上角是冠数、顶部居中往下是计时/阶段/状态行、底部是圣水条与手牌
        //（`AnchoredBottom`），右上角是唯一不与它们重叠的空区。锚点/轴心用 (1,1)（右上），
        // ⛔ 不用"左上角 + 大负 y"—— 见类注释里 CanvasScaler 的实测记录。

        /// <summary>暂停按钮边长 = 现状 <b>48</b>（原状态的高）。
        /// ⚠️ **未量到**（几何量取.md §2 C4c：18/20/21/23 四图未见暂停/齿轮按钮）⇒ 尺寸保持现状；
        /// 「宽」改成等于「高」：素材底板 `ui_out/163` 原生 219×219 是**正方形**，拉成 132×48 会把圆角压扁。</summary>
        private const float PauseButtonW = PauseButtonH;

        /// <summary>暂停按钮高度（现状 48）。未量到（见几何量取.md §2 C4c）。</summary>
        private const float PauseButtonH = 48f;

        /// <summary>暂停图标宽 = 按钮边长 × 0.5 = <b>24</b>（本项目自定：原版 `play_pause_button`
        /// 底板/图标 = 219 / 79（比例 0.36），本项目按钮只有 48 边长 ⇒ 取 0.5 保证辨识度；见几何量取.md §2 C4c）。</summary>
        private const float PauseIconW = PauseButtonH * 0.5f;

        /// <summary>暂停按钮右缘内缩 = 现状 <b>12</b>。未量到（见几何量取.md §2 C4c）。</summary>
        private const float PauseRightInset = 12f;

        /// <summary>暂停按钮与计时板下缘的间隔 = 现状 <b>8</b>。未量到（见几何量取.md §2 C4c）。</summary>
        private const float PauseTopGap = 8f;

        private void BuildPauseButton()
        {
            // 素材（出处 `策划/战斗HUD素材索引.md` §1 第 5 行 + §3.5）：
            //   底板 `HudPauseButtonPlate`(`ui_out/163`, 219×219) + 暂停图标 `HudPauseIconPause`(`ui_out/171`, 84×90 ‖)。
            // ⚠️ 如实登记的**子项缺口**：这组三件在原版里属**回放 HUD**（`replay_HUD_left` 的 `play_pause_button`），
            //   战斗内暂停按钮在 `HUD_*` 里**没有**独立命名元件 ⇒ 取最接近的那个原版元件，
            //   ⛔ 不是原版的战斗内暂停按钮（索引 §1 第 5 行已把这个推断写明，此处照抄，不升级成"原版命名"）。
            //   同族的播放态 `HudPauseIconPlay`(170, ▶) **未使用**：这颗按钮恒定请求进暂停菜单，没有"播放态"要显示。
            // 位置：右上角被计时板占住（贴顶贴右，§1.3 D16）⇒ 按钮放在**计时板正下方**、右缘对齐（现状口径，未量到）。
            var plate = CrUiStyle.NineSlice("PauseButton", _root, ResPaths.HudPauseButtonPlate, BorderNone,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-PauseRightInset, -(TimerBoxTop + TimerBoxH + PauseTopGap)),
                new Vector2(PauseButtonW, PauseButtonH), CrUiStyle.ButtonBg, true);

            // 点击照旧只发一条事件（见 `OnPauseClicked`）；四态用 tint 倍乘表达
            //（与 `CrUiStyle.ActionButton` 同口径：常态 tint = 白 = 原图原色，⛔ 不是给素材加滤镜）。
            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            // 本处是**手工建 Button** 的一条路径，必须与 `CrUiStyle.ActionButton` / `SlateButton` 同口径：
            //   **先发点击音、再执行点击**（否则少了统一点击音 —— 其余建按钮路径都经 `CrUiStyle.PlayUiClick`）。
            //   `CrUiStyle.PlayClick` 是那个私有入口的公开包装（`CrUiStyle.cs:471`），⛔ 不在这里自己 `Game.Sound.PlaySFX`
            //   （会漏日志、且将来双响）。
            btn.onClick.AddListener(() =>
            {
                CrUiStyle.PlayClick("PauseButton");
                OnPauseClicked();
            });
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = CrUiStyle.ButtonFade;
            btn.colors = colors;

            CrUiStyle.AspectImage("PauseIcon", plate.rectTransform, ResPaths.HudPauseIconPause, PauseIconW,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, CrUiStyle.TextColor);
        }

        /// <summary>
        /// 点击「暂停」：只请求切到 `Pause` 站点（面板开关由 `BattleUiHost` 负责）。
        /// ⛔ 不在这里 `Game.UI.Open&lt;PausePanel&gt;()`（开关必须只有一处）；
        /// ⛔ 不挂 `UnityEngine.Input` 快捷键（契约：输入一律走 `Game.Input`；本按钮走 UI 点击）。
        /// </summary>
        private void OnPauseClicked()
        {
            Game.Logger?.Info(Tag, "请求暂停（Emit Flow.StationEnterRequest → Pause）");
            Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Pause);
        }

        // ───────────────────────── 逐帧：拖放轮询 + 状态行计时 ─────────────────────────

        /// <summary>
        /// 每帧被 `UIManager.Tick` 调用（`Runtime/Presentation/UI.cs:363-379` 对每个已打开面板调 `OnUpdate`）。
        /// 用**轮询**而不是 `EventSystem` 的按下/拖动事件：本面板的输入要求是"按下 → 跟随 → 抬起"三段，
        /// 且必须走 `Game.Input`（⛔ 契约禁止裸 `UnityEngine.Input`）。
        /// </summary>
        public override void OnUpdate(float dt)
        {
            PollDrag();

            if (_statusText != null && _statusUntil > 0f && Time.unscaledTime >= _statusUntil)
            {
                _statusUntil = 0f;
                SetStatus(string.Empty, CrUiStyle.TextDim);
            }
        }

        private void PollDrag()
        {
            var input = Game.Input;
            if (input == null || _root == null) return;

            // 弹窗（暂停 / 设置 / 结算）开着时**一律不许拖放出牌**。
            //
            // 为什么必须显式挡：本面板的输入是**轮询** `Game.Input`，
            // 不是 EventSystem 的按下/拖动事件 —— 而 Popup 层的全屏遮罩**只挡 uGUI 点击**，挡不住轮询。
            // 症状：暂停菜单开着，玩家在弹窗上按下、拖到某个"手牌位"、松手 ⇒ 真的发出一条出牌请求，
            // 而画面看起来只是"点了下暂停菜单"。这类"看得见的 UI 与真正生效的输入不一致"必须堵死。
            //
            // 站点守卫：只有处在 `Battle` 站点才允许拖放出牌。
            //   为什么光靠"弹窗开着"不够 —— 暂停 → 设置 → 关设置 之后，`UIManager` 的同层互斥
            //   （`UI.cs:155-159`）会把 `PausePanel` 收掉；若没有代码重开它，就出现
            //   `station=Pause` + 屏幕上没有任何菜单 + HUD 仍在 的中间态；此时"弹窗开着"的守卫
            //   看到"没弹窗"就放行，玩家在这种"看起来像正常对局"的画面上按下手牌，
            //   **一次完整的拖放出牌请求真的发到了服务端**（`拖放抬起 → 请求出牌`）。
            //   站点守卫是**结构性判据**：不论菜单有没有开起来，非 Battle 站点都不是操作战场的时候。
            //   配套处理在 `SettingsPanel.OnClose`（关设置时把暂停菜单放回来）；本条是纵深防御。
            var station = Game.Fsm != null ? Game.Fsm.Current : null;
            var popupOpen = Game.UI != null &&
                (Game.UI.IsOpen<PausePanel>() || Game.UI.IsOpen<ResultPanel>() || Game.UI.IsOpen<SettingsPanel>());
            if (station != Stations.Battle || popupOpen)
            {
                // 拖到一半时弹窗才打开 / 站点被切走（例如拖放途中按了暂停）：必须收尾，
                // 否则幽灵卡在屏幕上、且本面板再也不接受新的按下。
                if (_dragging)
                {
                    CancelDrag(popupOpen
                        ? "暂停/弹窗打开，本次拖放作废"
                        : $"已不在 Battle 站点（{station ?? "未初始化"}），本次拖放作废");
                }
                else if (station != Stations.Battle && input.GetMouseButtonDown(0))
                {
                    // 非预期分支留痕（去重）：站点已不是 Battle 而 HUD 还开着、且真的有人在按手牌。
                    LogStationGuardOnce(station);
                }
                return;
            }

            var screen = input.MousePosition;

            if (!_dragging)
            {
                if (!input.GetMouseButtonDown(0)) return;

                var slot = HitTestHand(screen);
                if (slot < 0) return;

                var cardId = _handIds[slot];
                if (cardId == 0)
                {
                    SetStatus("这个手牌位是空的（等下一帧快照刷新）", CrUiStyle.TextDim);
                    return;
                }
                BeginDrag(cardId, slot, screen);
                return;
            }

            // 拖放中：幽灵跟手；抬起 ⇒ 发 C2S。
            MoveGhost(screen);

            if (input.GetMouseButtonUp(0))
            {
                EndDrag(screen);
                return;
            }

            if (!input.GetMouseButton(0))
            {
                // 非预期分支：抬起事件没被我们看到（例如编辑器里焦点被切走）。留痕并收尾，
                // 否则幽灵会永远粘在屏幕上、且 HUD 再也不接受新的按下。
                CancelDrag("按键状态与拖放态不一致（按下丢了？）");
            }
        }

        /// <summary>
        /// 「非 Battle 站点里按下手牌被站点守卫拦住」的**去重**留痕。
        /// <para>
        /// 去重理由：`PollDrag` 是**逐帧轮询**，一次按下沿可能被读到多帧 —— 不去重会把日志刷爆
        /// （引擎的日志文件是本机磁盘 IO，刷屏会盖掉别的证据）。按站点名去重：同一站点只记第一条。
        /// </para>
        /// </summary>
        private void LogStationGuardOnce(string station)
        {
            if (_stationGuardLogged == station) return;
            _stationGuardLogged = station;
            Game.Logger?.Info(Tag,
                $"按下手牌被站点守卫拦住：当前站点 {station ?? "未初始化"}（仅 Battle 站点可拖放出牌）");
        }

        /// <summary>指针落在哪个手牌槽位上；-1 = 不在任何手牌上。</summary>
        private int HitTestHand(Vector3 screen)
        {
            var cam = UiPointConvertCamera();
            var point = new Vector2(screen.x, screen.y);

            for (var i = 0; i < HandSlots; i++)
            {
                var card = _handCards[i];
                if (card == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(card.rectTransform, point, cam)) return i;
            }
            return -1;
        }

        private void BeginDrag(int cardId, int slot, Vector3 screen)
        {
            _dragging = true;
            _dragCardId = cardId;

            var card = FindCard(cardId);
            // 幽灵卡不写卡名（见 `BuildGhost` 的注释）
            if (_ghost != null)
            {
                // 幽灵卡用**同一张真卡面**（G4：⛔ 不再是一个纯色小块跟着手走），半透明表示"跟着手"。
                var art = slot >= 0 && slot < HandSlots ? _handArts[slot] : null;
                var sprite = art != null ? art.sprite : null;
                _dragArt = sprite;   // 落点虚影用同一张卡面（见 _dragArt）
                _ghost.sprite = sprite;
                _ghost.type = Image.Type.Simple;
                _ghost.preserveAspect = false;
                _ghost.color = sprite != null ? GhostColor : GhostColorFallback;
                _ghost.gameObject.SetActive(true);
                // 卡面取不到时**必须留痕**：否则现象是"拖卡时手指下只有一块半透明白色方块"，
                // 看起来就像"放卡没有卡模型"（用户第 5 条的原话），而日志里一条线索都没有。
                if (sprite == null && !_ghostArtFallbackWarned)
                {
                    _ghostArtFallbackWarned = true;
                    var key = card != null ? card.key : ("id=" + cardId);
                    Game.Logger?.Warn(Tag,
                        $"幽灵卡拿不到卡面（card={key} slot={slot}）⇒ 退成半透明纯色块。" +
                        "可能原因：卡面帧号表没有这张卡（`CrUiStyle.TryGetCardArtFrame` 返回 false）、" +
                        "或 `ui_spells_out` 素材没落地（只报一次）");
                }
            }
            MoveGhost(screen);

            // 卡牌拖起音（D8 补：矩阵判「拖起/放下无音」为不一致）。
            // 源 = `Game/grabcard_01.ogg`（原版「抓牌」）⇒ 见 Core/AudioPaths.cs 的 GrabCard。
            PlaySfx(AudioPaths.GrabCard);

            // 圣水不足**在本地就提示**（但**仍然发送**：服务端才是裁决者）：费用来自卡池数据，
            // 这不是几何判定、也不是第二套玩法规则。
            var elixir = CurrentElixirMilli();
            var costMilli = card != null ? card.elixir * ElixirMilliPerUnit : 0;
            if (card != null && elixir >= 0 && costMilli > elixir)
            {
                // 圣水不足被拒音（D8 补）：原版无效投放音 `bad_drop_03`（整包唯一的"拒绝/无效投放"音）。
                PlaySfx(AudioPaths.BadDrop);
                SetStatus(
                    $"圣水不足：「{card.name_cn}」需要 {card.elixir}，当前 {elixir / (float)ElixirMilliPerUnit:0.0}",
                    CrUiStyle.ErrorText);
            }
            else
            {
                SetStatus($"拖动「{CardNameOrId(cardId)}」中…松手即请求出牌（合法性由服务端裁决）", CrUiStyle.Accent);
            }

            Game.Logger?.Info(Tag, $"开始拖放：槽位 {slot} card={cardId} 屏幕=({screen.x:0},{screen.y:0})");
        }

        private void MoveGhost(Vector3 screen)
        {
            if (_ghost == null || _root == null) return;

            // 屏幕坐标 → **世界**坐标后直接写 `position`：这是 `RectTransformUtility` 专门为此提供的入口，
            // 对 Overlay / Screen Space - Camera 两种画布都成立；⛔ 不自己拿 `anchoredPosition` 去凑
            //（锚点/轴心口径不同会整体偏一次，且画布模式不同偏的量不一样）。
            var cam = UiPointConvertCamera();
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    _root, new Vector2(screen.x, screen.y), cam, out var world))
            {
                _ghost.rectTransform.position = world;
            }

            // 落点指示必须在**每次指针移动**时刷新（D9：合法绿 / 非法红）。
            UpdatePlacementPreview(screen);
        }

        private void EndDrag(Vector3 screen)
        {
            var cardId = _dragCardId;
            CancelDrag(null);

            var world = TryScreenToWorld(screen, out var ok);
            if (!ok)
            {
                SetStatus("拿不到主相机，落点无法换算成场内坐标，本次出牌未发出", CrUiStyle.ErrorText);
                return;
            }

            // 卡牌放下音（D8 补）：**落点非法**时播原版"无效投放"音（`bad_drop_03`）；
            // 合法时不额外播 —— 那次出牌的声音由 `PlayCardRequest` 的己方召唤音（`summon_own_07`）负责，
            // 两个一起播会是"放下 + 召唤"双响（原版只有一声）。
            // 判定复用 `BattleViewRoot.IsDeployLegal`（与落点指示器**同一套几何**，⛔ 不在这里另写一份）。
            var view = CR.View.BattleViewRoot.Instance;
            if (view != null)
            {
                var dragCard = FindCard(cardId);
                var dropIsSpell = dragCard != null && dragCard.type == CardTypeSpell;
                if (!view.IsDeployLegal(CR.View.BattleViewRoot.WorldToTile(world), dropIsSpell))
                {
                    PlaySfx(AudioPaths.BadDrop);
                    Game.Logger?.Info(Tag, $"放下落点非法 ⇒ 播无效投放音（{AudioPaths.BadDrop}）");
                }
            }

            Game.Logger?.Info(Tag,
                $"拖放抬起 → 请求出牌 card={cardId} 屏幕=({screen.x:0},{screen.y:0}) 世界=({world.x:0.00},{world.y:0.00})");
            SetStatus($"已请求出牌「{CardNameOrId(cardId)}」，等快照/服务端确认…", CrUiStyle.Accent);
            Game.Event?.Emit(Events.Battle.PlayCardRequest, cardId, world);
        }

        private void CancelDrag(string why)
        {
            if (_dragging && !string.IsNullOrEmpty(why))
            {
                Game.Logger?.Info(Tag, $"取消拖放（{why}）card={_dragCardId}");
            }
            _dragging = false;
            _dragCardId = 0;
            _dragArt = null;
            if (_ghost != null) _ghost.gameObject.SetActive(false);

            // 拖放结束（正常抬手 / 取消 / 面板关闭）都要收掉落点指示，
            // 否则场上会留一个红/绿圈，看起来像"还能继续放"。
            CR.View.BattleViewRoot.Instance?.HidePlacement();
        }

        /// <summary>
        /// 屏幕坐标 → 世界坐标（格）。算法与引擎自带的 `IsoLayout.ScreenToWorldOnGround`
        /// （`Runtime/Core/IsoLayout.cs:105-125`）**逐行同源**：正交相机下"到地面的深度"就是
        /// `-camera.position.z`，少了它点击位置会整体偏移（引擎那里也为此打告警）。
        /// 相机取 `UIFactory.UICamera()`（引擎给 UI 侧取相机的唯一入口）。
        /// </summary>
        private Vector2 TryScreenToWorld(Vector3 screen, out bool ok)
        {
            ok = false;
            var cam = UIFactory.UICamera();
            if (cam == null)
            {
                // 只报一次（进程级）：引擎 `LogThrottle.WarnOnce`。
                LogThrottle.WarnOnce(Tag, "hud.ui.camera.missing",
                    "UIFactory.UICamera() 返回 null（主相机缺失且场景里没有启用的相机）：" +
                    "拖放出牌的落点无法换算，⛔ 不发请求（宁可让玩家看到提示，也不发一个坐标错误的请求）");
                return Vector2.zero;
            }

            if (!cam.orthographic)
            {
                // 非预期分支：竞技场相机应当是正交（`IsoLayout.ScreenToWorldOnGround` 的前提）。
                // 留痕（现象是"落点随视角/距离漂移"，很难猜）。
                LogThrottle.WarnOnce(Tag, "hud.ui.camera.nonortho",
                    $"主相机 {cam.name} 不是正交相机，落点换算可能整体偏移（IsoLayout 的前提是正交）");
            }

            var depth = -cam.transform.position.z;
            if (Mathf.Approximately(depth, 0f))
            {
                Game.Logger?.Warn(Tag, $"相机 z={cam.transform.position.z} 导致到地面距离为 0，按 10 处理（引擎同口径）");
                depth = 10f;
            }

            var p = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
            ok = true;
            return new Vector2(p.x, p.y); // z 丢掉：竞技场在地面平面 z=0 上
        }

        // ───────────────────── 落点指示（与 View 层的唯一接缝） ─────────────────────

        /// <summary>`CardInfo.type` 的法术取值（`Def/ProtoDef.cs`：0=部队 1=法术 2=建筑）。</summary>
        private const int CardTypeSpell = 1;

        /// <summary>
        /// 落点指示半径（格）**兜底值**。⚠️ `CardInfo` 里没有部队的碰撞半径字段（协议未下发），
        /// 所以部队用固定 1 格 —— 这是**表现近似**，只影响那个圈画多大，**不影响任何裁决**
        /// （放置合法性由服务端判，客户端这个圈只是"看起来能不能放"）。
        /// <para>
        /// **法术不再走这里**：法术半径 = 服务端下发的 `CardInfo.aoe_radius_milli`
        /// （出处 `spell.tsv` 的 `radius_mt`，10 张各不相同）。<see cref="PlacementRadiusTilesSpell"/>
        /// 只在"卡池还没到 / 该行缺半径"时兜底，并在日志里留痕（⛔ 它不是原版数值）。
        /// </para>
        /// </summary>
        private const float PlacementRadiusTilesDrop = 1.0f;

        /// <summary>法术圈半径的**兜底**值（格）。正常路径用 `CardInfo.aoe_radius_milli`，见上。</summary>
        private const float PlacementRadiusTilesSpell = 3.0f;

        /// <summary>「法术半径缺值、退到兜底常量」只告警一次。</summary>
        private bool _spellRadiusFallbackWarned;

        /// <summary>「幽灵卡拿不到卡面、退成纯色块」只告警一次。</summary>
        private bool _ghostArtFallbackWarned;

        /// <summary>「表现层根未建」只告警一次（避免每帧刷屏）。</summary>
        private bool _noBattleViewWarned;

        /// <summary>
        /// 刷新落点指示（D9：合法绿 / 非法红）。
        ///
        /// <para>
        /// <b>为什么几何判定一行都不在这里写</b>：部署规则（半场 / 河桥 / 摧毁公主塔后的口袋区）
        /// 在 <see cref="CR.View.BattleViewRoot.IsDeployLegal"/>，它内部走的是与
        /// <c>Core/GameConst</c> 同一套常量。面板里再写一套 = 第二处规则，必然与服务端漂移，
        /// 症状是"看起来能放、点下去被拒"。所以这里只做"把屏幕坐标转成格坐标、问 View 合不合法、让它画圈"。
        /// </para>
        /// <para>
        /// 与 <see cref="TryScreenToWorld"/> 的关系：那个函数负责**抬手时**交给
        /// <c>Events.Battle.PlayCardRequest</c> 的世界坐标（保持既有行为不变）；
        /// 这里用 <c>ScreenToTile</c> 拿格坐标给指示器。两条路都源自同一台相机，不会打架。
        /// </para>
        /// </summary>
        private void UpdatePlacementPreview(Vector3 screen)
        {
            var view = CR.View.BattleViewRoot.Instance;
            if (view == null)
            {
                // 非预期分支：表现层根没建起来（例如直接单开 Battle01 场景调试、AutoInstall 还没跑）。
                // 必须留痕 —— 否则现象是"拖了半天没有落点提示"，看起来像功能没做。
                if (!_noBattleViewWarned)
                {
                    _noBattleViewWarned = true;
                    Game.Logger?.Warn(Tag,
                        "CR.View.BattleViewRoot.Instance 为 null（对局表现层根未建）⇒ 本次拖放没有落点指示；" +
                        "出牌请求仍会发出，最终由服务端裁决");
                }
                return;
            }

            var card = FindCard(_dragCardId);
            var isSpell = card != null && card.type == CardTypeSpell;
            var tile = view.ScreenToTile(screen);
            // 法术圈的半径**必须用服务端下发的真实作用半径**（`CardInfo.aoe_radius_milli`
            // ← `spell.tsv` 的 `radius_mt`）：10 张法术半径各不相同（万箭齐发 1.4 格 …
            // 雷电/毒药 3.5 格），写死一个常量会让玩家照着圈放却打空。
            // ⚠️ 只有"卡池还没到 / 该行缺半径"（`aoe_radius_milli <= 0`）才退到旧常量兜底，
            // 并留痕一次 —— 兜底值**不是**原版数值，只是不让圈消失。
            var spellRadius = PlacementRadiusTilesSpell;
            if (isSpell && card.aoe_radius_milli > 0)
            {
                spellRadius = card.aoe_radius_milli / 1000f;
            }
            else if (isSpell && !_spellRadiusFallbackWarned)
            {
                _spellRadiusFallbackWarned = true;
                Game.Logger?.Warn(Tag,
                    $"法术卡 card={(card != null ? card.key : "?")} 的 aoe_radius_milli=" +
                    $"{(card != null ? card.aoe_radius_milli : -1)}（<=0）⇒ 落点圈半径退到兜底常量 " +
                    $"{PlacementRadiusTilesSpell} 格（**不是原版数值**）。检查服务端 CardInfo 是否下发了 aoe_radius_milli（只报一次）");
            }
            // `_dragArt` = 正在拖的那张卡的卡面 ⇒ 落点处画的是**这张卡本身**（半透明虚影），
            //   不是一个小图标。取不到卡面时传 null（宁可不画，⛔ 不用别的图形顶替）。
            view.ShowPlacement(tile, isSpell ? spellRadius : PlacementRadiusTilesDrop, isSpell, _dragArt);
        }

        /// <summary>
        /// 屏幕点 ↔ 画布矩形/世界点 换算要用的相机。
        ///
        /// <para>
        /// <b>为什么这么做（实机读数）</b>：常驻画布是
        /// <c>ScreenSpaceOverlay</c>（引擎 `Runtime/Presentation/UI.cs:49`
        /// `canvas.renderMode = RenderMode.ScreenSpaceOverlay`），其世界坐标<b>就是屏幕像素</b>。
        /// 而 <see cref="RectTransformUtility"/> 收到<b>非空</b>相机时，会把屏幕点当成"相机视锥里的一个方向"
        /// 再投到画布平面上 ⇒ 两者相差一次相机投影，判定<b>恒为 false</b>。
        /// 实测（1080×1920 画布 + 正交半高 16 的竞技场相机，相机在 (0,0,-10)）：
        /// 手牌槽 0 的卡面中心真屏幕点 =(214,221)，
        /// <c>RectangleContainsScreenPoint(rect, (214,221), mainCam)=False</c>、
        /// 传 <c>null</c> 时为 <c>True</c>；<c>HitTestHand((214,221))</c> 返回 <b>-1</b>
        /// ⇒ 按下手牌<b>根本不进入拖放</b>（`_dragging` 恒 false），症状 = "卡牌拖不动 / 放不上战场"。
        /// </para>
        /// <para>
        /// <b>⛔ 所以这里按画布模式取相机，不是"取一台相机就完事"</b>：
        /// Overlay ⇒ <c>null</c>；ScreenSpaceCamera / WorldSpace 才用画布自己的 <c>worldCamera</c>。
        /// 竞技场那边的"屏幕 → 格"换算（<see cref="TryScreenToWorld"/> /
        /// `BattleViewRoot.ScreenToTile`）是<b>另一回事</b>：它要的正是相机的投影，仍用
        /// <see cref="UIFactory.UICamera"/>（引擎 `UIWidgets.cs:189-195` 的官方入口）。
        /// </para>
        /// </summary>
        private Camera UiPointConvertCamera()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        // ───────────────────────── 事件（OnOpen 挂 / OnClose 摘，成对） ─────────────────────────

        private void Subscribe()
        {
            if (_onStarted == null)
            {
                _onStarted = OnStarted;
                _onSnapshot = OnSnapshot;
                _onEnded = OnEnded;
                _onFailed = OnFailed;
                _onPoolLoaded = OnPoolLoaded;
            }

            var bus = Game.Event;
            if (bus == null)
            {
                Game.Logger?.Error(Tag, "Game.Event 为空（引擎未 Launch？），HUD 收不到任何对局数据");
                return;
            }

            // 幂等：`UIManager` 对已打开的面板会再次调用 OnOpen（UI.cs:107-117），重复 On 会让一条消息走两遍。
            bus.Off(Events.Battle.Started, _onStarted);
            bus.Off(Events.Battle.Snapshot, _onSnapshot);
            bus.Off(Events.Battle.Ended, _onEnded);
            bus.Off(Events.Battle.StartFailed, _onFailed);
            bus.Off(Events.Deck.PoolLoaded, _onPoolLoaded);

            bus.On(Events.Battle.Started, _onStarted);
            bus.On(Events.Battle.Snapshot, _onSnapshot);
            bus.On(Events.Battle.Ended, _onEnded);
            bus.On(Events.Battle.StartFailed, _onFailed);
            // 卡名/费用（`CardInfo`）：HUD 在 `Battle` 站点才打开，那时 `Deck` 模块在主菜单发的
            // `PoolLoaded` 早已过去 ⇒ `BattleManager` 会在站点切换时按同一条事件补发一次（见那边注释）。
            bus.On(Events.Deck.PoolLoaded, _onPoolLoaded);
        }

        private void Unsubscribe()
        {
            if (_onStarted == null) return;
            var bus = Game.Event;
            bus?.Off(Events.Battle.Started, _onStarted);
            bus?.Off(Events.Battle.Snapshot, _onSnapshot);
            bus?.Off(Events.Battle.Ended, _onEnded);
            bus?.Off(Events.Battle.StartFailed, _onFailed);
            bus?.Off(Events.Deck.PoolLoaded, _onPoolLoaded);
        }

        private void OnStarted(BattleStartNotify start)
        {
            if (start == null)
            {
                // 非预期分支：`Emit` 用 DynamicInvoke，参数类型不对会在这里显形。留痕。
                Game.Logger?.Warn(Tag, "收到 null 的开打配置（发送方参数有误？），HUD 不刷新");
                return;
            }

            _start = start;
            _myTeam = start.my_team;

            var maxUnits = start.timeline != null ? start.timeline.max_elixir : 0;
            _maxElixirMilli = maxUnits > 0 ? maxUnits * ElixirMilliPerUnit : GameConst.MaxElixirMilli;
            _regulationMs = start.timeline != null ? start.timeline.regulation_ms : 0;
            _overtimeMs = start.timeline != null ? start.timeline.overtime_ms : 0;
            _noTimelineWarned = false;

            Game.Logger?.Info(Tag,
                $"开打配置到达 my_team={_myTeam}（0=蓝 1=红）圣水上限={_maxElixirMilli / (float)ElixirMilliPerUnit:0} " +
                $"常规={_regulationMs}ms 加时={_overtimeMs}ms");

            RefreshAll();
        }

        private void OnSnapshot(BattleSnapshot snap)
        {
            if (snap == null)
            {
                Game.Logger?.Warn(Tag, "收到 null 的快照（发送方参数有误？），HUD 不刷新");
                return;
            }

            _snapshot = snap;
            ReadHand();
            RefreshTop();
            RefreshElixir();
            RefreshHand();
            RefreshNext();
        }

        private void OnEnded(BattleEndNotify result)
        {
            if (result == null)
            {
                Game.Logger?.Warn(Tag, "收到 null 的结算（发送方参数有误？），HUD 保持原样");
                return;
            }

            CancelDrag("对局已结束");
            SetStatus(string.Empty, CrUiStyle.TextDim);

            // 结算面板（`ResultPanel`）负责展示详情；HUD 只把冠数定格在服务端给的结算值上
            //（快照停了，不再依赖它）并把阶段写成"已结束"。
            // 两枚徽章各写一个数字（左=我方 / 右=对方，口径与 `RefreshAll` 同）。
            //   平局不塞进 36px 的徽章里（会溢出）⇒ 只在日志里说，屏幕上由 `ResultPanel` 的结算文案承担。
            if (_crownsMineText != null) _crownsMineText.text = result.crowns_a.ToString();
            if (_crownsEnemyText != null) _crownsEnemyText.text = result.crowns_b.ToString();
            if (result.draw)
            {
                Game.Logger.Info(Tag, $"结算为平局：冠数 {result.crowns_a} : {result.crowns_b}（冠数徽章按原版只显示数字）");
            }
            if (_phaseText != null)
            {
                _phaseText.text = PhaseText(PhaseEnded);
                _phaseText.color = CrUiStyle.TextDim;
            }
        }

        private void OnFailed(string reason)
        {
            var text = string.IsNullOrEmpty(reason) ? "对局操作失败（服务端未给出原因）" : reason;
            SetStatus(text, CrUiStyle.ErrorText);
        }

        private void OnPoolLoaded(CardInfo[] cards)
        {
            if (cards == null)
            {
                Game.Logger?.Warn(Tag, "收到 null 的卡池，手牌只能显示卡 id");
                return;
            }

            _cards.Clear();
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                _cards[card.id] = card;
            }

            Game.Logger?.Info(Tag, $"卡池已到达 {_cards.Count} 张，手牌文案可用（圣水数 + 中文名 + 类型色）");
            RefreshHand();
            RefreshNext();
        }

        // ───────────────────────── 刷新 ─────────────────────────

        private void RefreshAll()
        {
            ReadHand();
            RefreshTop();
            RefreshElixir();
            RefreshHand();
            RefreshNext();
        }

        /// <summary>
        /// 从快照（或开打配置的初值）读出手牌与下一张。**取哪一侧由 `my_team` 决定**
        /// （契约：自己的手牌/圣水也在快照里；`hand_a` / `next_a` 是 BLUE 侧，`hand_b` / `next_b` 是 RED 侧）。
        /// </summary>
        private void ReadHand()
        {
            int[] hand;
            int next;
            if (_snapshot != null)
            {
                hand = _myTeam == 0 ? _snapshot.hand_a : _snapshot.hand_b;
                next = _myTeam == 0 ? _snapshot.next_a : _snapshot.next_b;
            }
            else if (_start != null)
            {
                hand = _myTeam == 0 ? _start.hand_a : _start.hand_b;
                next = _myTeam == 0 ? _start.next_a : _start.next_b;
            }
            else
            {
                hand = null;
                next = 0;
            }

            var count = hand != null ? hand.Length : 0;
            if (count > HandSlots && !_handTooLongWarned)
            {
                _handTooLongWarned = true;
                // 非预期分支：服务端手牌数超过 HUD 预建的槽位（原版固定 4 张）。留痕，别静默丢卡。
                Game.Logger?.Warn(Tag,
                    $"服务端给出 {count} 张手牌 > HUD 预建的 {HandSlots} 个槽位，多出的卡不显示（手牌口径变了？）");
            }

            for (var i = 0; i < HandSlots; i++)
            {
                _handIds[i] = i < count ? hand[i] : 0;
            }
            _nextId = next;
        }

        private void RefreshTop()
        {
            var snap = _snapshot;

            // 每枚徽章只写**一个数字**（原版 18 图的顶中徽章就是一个数字；
            //   两枚徽章的直径都是 36 ⇒ 塞 "0 : 0" 必然溢出）。左=我方 / 右=对方（固定口径，不随 `_myTeam` 翻转）。
            if (_crownsMineText != null || _crownsEnemyText != null)
            {
                var mine = snap != null ? (_myTeam == 0 ? snap.crowns_a : snap.crowns_b) : 0;
                var theirs = snap != null ? (_myTeam == 0 ? snap.crowns_b : snap.crowns_a) : 0;
                if (_crownsMineText != null) _crownsMineText.text = mine.ToString();
                if (_crownsEnemyText != null) _crownsEnemyText.text = theirs.ToString();
            }

            if (_timerText != null) _timerText.text = RemainingText(snap);
            TickCountdown(snap);

            if (_phaseText != null)
            {
                var phase = snap != null ? snap.phase : 0;
                _phaseText.text = PhaseText(phase);
                _phaseText.color = phase == PhaseEnded ? CrUiStyle.TextDim : CrUiStyle.Accent;
            }
        }

        /// <summary>
        /// 剩余时间：常规阶段 = `regulation_ms - server_ms`；加时 = `regulation_ms + overtime_ms - server_ms`；
        /// 已结束 = 00:00。
        /// <para>
        /// ⛔ 用服务端的 `server_ms` 而不是本地计时：客户端掉帧 / 卡顿 / 挂起都会让本地计时与服务器漂移，
        /// 而"还剩多久"是**服务端的规则量**（谁先超时判定归属就靠它）。
        /// </para>
        /// </summary>
        private string RemainingText(BattleSnapshot snap)
        {
            if (snap == null) return "--:--";
            if (snap.phase == PhaseEnded) return "00:00";

            var total = _regulationMs + (snap.phase == PhaseOvertime ? _overtimeMs : 0);
            if (total <= 0)
            {
                if (!_noTimelineWarned)
                {
                    _noTimelineWarned = true;
                    // 非预期分支：还没拿到时间线（`Battle.Started` 没到）⇒ 计时无意义。留痕一次。
                    Game.Logger?.Warn(Tag, "还没收到时间线（Battle.Started），计时无法显示，显示 --:--");
                }
                return "--:--";
            }

            var remainMs = Mathf.Max(0, total - snap.server_ms);
            var totalSec = remainMs / 1000;
            return $"{totalSec / 60:00}:{totalSec % 60:00}";
        }

        /// <summary>
        /// 倒计时进入「最后 10 秒」的阈值（毫秒）。
        /// 出处：音效矩阵「倒计时最后10秒提示音 / 边界值 `timer&lt;=10s` /
        /// 原版有滴答提示」。本项目取 10 000 ms 与矩阵边界逐字一致。
        /// </summary>
        private const long CountdownWarnMs = 10000L;

        /// <summary>
        /// 倒计时最后 10 秒的**每秒滴答**（D8 补：矩阵判「倒计时仅文本、无音效挂点」为不一致）。
        /// <para>
        /// 音源 = <see cref="AudioPaths.CountdownTick"/>（`Game/deploy_timer_tick_01v4.ogg`，原版对局计时滴答）。
        /// 用**服务端口径的剩余时间**（与 <see cref="RemainingText"/> 同源）= 规则量，
        /// 不用本地计时（掉帧/挂起会让本地计时漂移，滴答位置就与画面上的秒数对不上）。
        /// </para>
        /// <para>每"剩余整数秒"只播一声（<see cref="_lastTickSec"/> 去重）⇒ 10 Hz 快照下不会一声变十声。</para>
        /// </summary>
        private void TickCountdown(BattleSnapshot snap)
        {
            if (snap == null || snap.phase == PhaseEnded)
            {
                _lastTickSec = -1;
                return;
            }

            var total = _regulationMs + (snap.phase == PhaseOvertime ? _overtimeMs : 0);
            if (total <= 0) return; // 还没拿到时间线（RemainingText 已单独留痕）

            var remainMs = Mathf.Max(0, total - snap.server_ms);
            if (remainMs > CountdownWarnMs)
            {
                _lastTickSec = -1;  // 还没进窗口：复位，下一次进窗口重新从"第 10 秒"开始滴答
                return;
            }

            var sec = remainMs / 1000;
            if (sec == _lastTickSec) return;
            _lastTickSec = sec;
            if (sec > 0) PlaySfx(AudioPaths.CountdownTick);
        }

        /// <summary>
        /// 播一个 HUD 音效（D8）。⛔ 只用 clover-engine 的 <c>Game.Sound.PlaySFX</c>
        /// （音量走 <c>SoundGroup.SFX</c>，由 `Module/Settings/SettingsManager` 应用）；
        /// ⛔ 不自己建 <c>AudioSource</c> / 不建池。
        /// <para>
        /// 每次播放留一条 Info：**这是"哪个挂点真的响了"的唯一运行时判据**（数值类证据 = 运行时日志行）。
        /// 资源缺失与 <c>Game.Sound</c> 为空各只报一次 Warn（⛔ 不静默跳声、⛔ 不刷屏）。
        /// </para>
        /// </summary>
        private static void PlaySfx(string clipName)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                if (!_sfxWarned)
                {
                    _sfxWarned = true;
                    Game.Logger?.Warn(Tag, "Game.Sound 为空（表现域未挂载）⇒ HUD 音效无法播放（只报一次）");
                }
                return;
            }

            var res = Game.Res;
            if (res != null && !res.Exists(AudioPaths.SfxPath(clipName)))
            {
                if (!_sfxMissingWarned)
                {
                    _sfxMissingWarned = true;
                    Game.Logger?.Warn(Tag,
                        $"HUD 音效资源缺失（Resources/{AudioPaths.SfxPath(clipName)}）⇒ 该音不播放（只报一次，检查是否漏拷）");
                }
                return;
            }

            Game.Logger?.Info(Tag, $"播放音效 {clipName}（SoundGroup.SFX 音量 {sound.GetVolume(SoundGroup.SFX):F2}）");
            sound.PlaySFX(clipName);
        }

        private string PhaseText(int phase)
        {
            switch (phase)
            {
                case 0: return "常规时间";
                case PhaseOvertime: return "加时赛";
                case PhaseEnded: return "对局已结束";
                default:
                    // 非预期分支：协议只定义 0/1/2（`Def/ProtoDef.cs:150`）。同一取值只报一次。
                    if (_lastUnknownPhase != phase)
                    {
                        _lastUnknownPhase = phase;
                        Game.Logger?.Warn(Tag, $"未知的对局阶段 phase={phase}（协议只定义 0=常规 1=加时 2=已结束）");
                    }
                    return "未知阶段";
            }
        }

        private void RefreshElixir()
        {
            var elixir = CurrentElixirMilli();
            var shown = elixir < 0 ? 0 : elixir;
            var maxUnits = _maxElixirMilli / (float)ElixirMilliPerUnit;
            var units = shown / (float)ElixirMilliPerUnit;

            if (_elixirFill != null)
            {
                UIFactory.SetBarWidth(_elixirFill, _maxElixirMilli > 0 ? shown / (float)_maxElixirMilli : 0f);
            }

            // 数字进徽章（原版口径），条上不写字（`_elixirText` 已在 BuildElixir 里停用）。
            // 取**整数**（原版 18 图徽章里就是一位整数「2」；`elixirAmount` 是整数文本域，不是 "8.2/10"）。
            if (_elixirBadgeText != null)
            {
                _elixirBadgeText.text = ((int)units).ToString();
            }
        }

        /// <summary>自己那侧的圣水（1/1000 单位）；没有快照时退到开打初值，仍无则 -1（= 未知）。</summary>
        private int CurrentElixirMilli()
        {
            if (_snapshot != null) return _myTeam == 0 ? _snapshot.elixir_a : _snapshot.elixir_b;
            return _start != null ? GameConst.StartingElixirMilli : -1;
        }

        /// <summary>空手牌位的卡槽染色（把原版卡槽图元压暗，表达"空位"；本项目自定，不是素材替换）。</summary>
        private static readonly Color EmptySlotTint = new Color(0.55f, 0.55f, 0.62f, 1f);

        private void RefreshHand()
        {
            for (var i = 0; i < HandSlots; i++)
            {
                var card = _handCards[i];
                var art = _handArts[i];
                var cost = _handCosts[i];
                if (card == null) continue;

                var id = _handIds[i];
                if (id == 0)
                {
                    if (cost != null) cost.text = string.Empty;
                    HideArt(art);
                    card.color = EmptySlotTint;
                    continue;
                }

                var info = FindCard(id);
                if (cost != null) cost.text = info != null ? info.elixir.ToString() : string.Empty;
                ApplyArt(art, info, id, $"手牌#{i + 1}");
                // 原版卡槽图元原色（⛔ 不再按类型染色 —— 类型色底是占位物，G4 已废除）
                card.color = Color.white;
            }

            // 判据行（数值类证据 = 运行时日志行 + 断言）：手牌**组成变化时**报一次
            //   「4 张卡的 key → 帧号」；单卡的"载入成功"由 `ApplyArt` 的「卡面就绪」行给出。
            //   ⛔ 不随 10 Hz 快照刷屏（只在 `_handIds` 真的变了时打印）。
            var handSig = string.Join(",", _handIds);
            if (handSig == _handSigLogged) return;
            _handSigLogged = handSig;

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < HandSlots; i++)
            {
                var id = _handIds[i];
                var info = FindCard(id);
                var key = info != null ? info.key : null;
                int frame;
                var hasFrame = CrUiStyle.TryGetCardArtFrame(key, out frame);
                if (i > 0) sb.Append(" | ");
                sb.Append("手牌#").Append(i + 1).Append(" id=").Append(id)
                  .Append(" key=").Append(string.IsNullOrEmpty(key) ? "—" : key)
                  .Append(" → ").Append(hasFrame
                        ? "帧号 " + frame
                        : (CrUiStyle.IsCardArtKnownGap(key) ? "已知缺口（原版无此 export 名）" : "无卡面（表与卡池脱节）"));
            }
            Game.Logger?.Info(Tag, $"本局手牌帧号表（帧号表 = CrUiStyle 唯一一份，共 {CrUiStyle.CardArtFrameCount} 条）：{sb}");
        }

        private void RefreshNext()
        {
            if (_nextCard == null) return;
            var info = FindCard(_nextId);
            if (_nextText != null) _nextText.text = TextFit.Clamp(_nextText, _nextId == 0 ? "—" : CardLabel(info, _nextId));
            if (_nextCost != null) _nextCost.text = info != null ? info.elixir.ToString() : string.Empty;
            ApplyArt(_nextArt, info, _nextId, "下一张");
            _nextCard.color = _nextId == 0 ? EmptySlotTint : Color.white;
        }

        /// <summary>
        /// 卡名文案。G4 后卡名只写**名字**（圣水数走卡角原版水滴、类型不再写 —— 真卡面本身已表达类型）。
        /// 卡池没到时与服务端给的 id 一致地写「卡 id=N」并留痕（⛔ 不静默留空）。
        /// </summary>
        private string CardLabel(CardInfo card, int id)
        {
            if (card == null)
            {
                // 非预期分支：卡池没到（`BattleManager` 的补发也拿不到）⇒ 只能显示 id。只报一次（进程级）。
                LogThrottle.WarnOnce(Tag, "hud.card.pool.missing",
                    "卡池还没到达（Events.Deck.PoolLoaded 没收到），手牌只能显示卡 id；" +
                    "正常流程下 BattleManager 会在进对局时补发一次卡池");
                return $"卡 id={id}";
            }

            return string.IsNullOrEmpty(card.name_cn) ? $"卡 id={id}" : card.name_cn;
        }

        private static string CardName(CardInfo card)
        {
            return !string.IsNullOrEmpty(card.name_cn) ? card.name_cn : "未知卡";
        }

        private string CardNameOrId(int id)
        {
            var card = FindCard(id);
            return card != null ? CardName(card) : $"卡 id={id}";
        }

        /// <summary>类型名（`CardInfo.type`：0=部队 1=法术 2=建筑，见 `Def/ProtoDef.cs:212`）。</summary>
        private string TypeName(int type)
        {
            switch (type)
            {
                case 0: return "部队";
                case 1: return "法术";
                case 2: return "建筑";
                default:
                    // 非预期分支：配表出现协议注释外的取值。同一值只报一次。
                    if (_lastUnknownType != type)
                    {
                        _lastUnknownType = type;
                        Game.Logger?.Warn(Tag,
                            $"未知的卡牌类型 type={type}（协议只定义 0=部队 1=法术 2=建筑），按「未知」显示");
                    }
                    return "未知";
            }
        }

        // ───────────────────────── 真卡面加载（G4 新增） ─────────────────────────

        /// <summary>
        /// 按卡 `key` 把真卡面贴到卡槽上（帧号表 = `CrUiStyle` 的**唯一**一份；表里没有的卡 ⇒
        /// **不画卡面**、只留原版卡槽底 + 卡名，与 `DeckEditPanel` 同一降级口径；
        /// ⛔ 不猜帧号、⛔ 不按类型涂色）。
        /// <para>
        /// 本方法被 10 Hz 快照链路调用 ⇒ 所有日志都必须**只报一次**（否则刷屏）。
        /// </para>
        /// </summary>
        /// <param name="slotTag">日志定位用（"手牌#1"…"下一张"）—— 日志里据此分辨是哪一格。</param>
        private void ApplyArt(Image target, CardInfo card, int id, string slotTag)
        {
            if (target == null) return;

            int artFrame;
            if (card == null || string.IsNullOrEmpty(card.key)
                || !CrUiStyle.TryGetCardArtFrame(card.key, out artFrame))
            {
                HideArt(target);
                if (card != null && !string.IsNullOrEmpty(card.key) && _noArtWarned.Add(card.key))
                {
                    // 非预期分支/如实登记（只留痕一次）：本卡不画卡面。两种原因**必须能从日志一眼分开**：
                    // ① 已知缺口 = 原版 ui_spells 的 95 条 export 里本来就没有这张卡；② 新卡没跟上表。
                    var why = CrUiStyle.IsCardArtKnownGap(card.key)
                        ? "是**已知缺口**（原版 ui_spells 的 95 条 export 里没有对应名）"
                        : "**没有登记帧号**（卡池与帧号表脱节）";
                    Game.Logger?.Warn(Tag,
                        $"卡面无帧号[{slotTag}]：卡「{card.name_cn}」(key={card.key} id={id} 类型={TypeName(card.type)}) "
                        + why + "，本卡只画原版卡槽底 + 卡名（⛔ 不猜帧号、⛔ 不按类型涂色）");
                }
                return;
            }

            var resPath = ResPaths.SpellArtFrame(artFrame);
            LoadArtSprite(artFrame, resPath, sprite =>
            {
                if (target == null) return;
                target.sprite = sprite;
                target.type = Image.Type.Simple;
                target.preserveAspect = false; // 裁剪后的 rect 已是素材自身比例，再按比例缩会再留边
                target.color = Color.white;
                target.gameObject.SetActive(true);
                // 判据行（数值类证据 = 运行时日志行 + 断言）：**每个 (key, 帧号) 只报一次**。
                if (_artReadyLogged.Add(card.key + "#" + artFrame))
                {
                    Game.Logger?.Info(Tag,
                        $"卡面就绪[{slotTag}]：key={card.key} → 帧号 {artFrame} 载入成功（{resPath}）；"
                        + $"卡面 {CardW * ArtFillX:F1}×{CardH * ArtFillY:F1} 贴进 {CardW:F0}×{CardH:F0} 卡槽"
                        + $"（内缩 左{ArtInsetLeftFrac * 100f:F2}% / 右{ArtInsetRightFrac * 100f:F2}% / "
                        + $"上{ArtInsetTopFrac * 100f:F2}% / 下{ArtInsetBottomFrac * 100f:F2}%，"
                        + $"纵向偏移 {CardH * ArtOffsetYFrac:F2}px）");
                }
            });
        }

        private static void HideArt(Image target)
        {
            if (target == null) return;
            target.sprite = null;
            target.gameObject.SetActive(false);
        }

        /// <summary>没登记过卡面帧号的 `key`（只报一次，避免每帧刷屏）。</summary>
        private readonly HashSet<string> _noArtWarned = new HashSet<string>();

        /// <summary>已经打过「卡面就绪」判据行的 `key#帧号`（10 Hz 链路上只报一次）。</summary>
        private readonly HashSet<string> _artReadyLogged = new HashSet<string>();

        /// <summary>已经 Warn 过卡面路径的（缺素材只报一次）。</summary>
        private static void WarnArtOnce(string message)
        {
            if (!WarnedArtMissing.Add(message)) return;
            Game.Logger?.Warn(Tag, message);
        }

        /// <summary>
        /// 取「帧号 → 裁剪过的卡面 Sprite」。
        /// 口径 = <see cref="CrUiStyle.CropCardArt"/>（**与 `DeckEditPanel` 同一处**，⛔ 不各写一份）。
        /// ⚠️ `ui_spells_out` 每张 png 的导入器**已经裁好那一帧**（.meta 的 `sprites[0].rect` 就是内容窗）
        /// ⇒ `CropCardArt` 原样返回它，⛔ 不能叠加 `CardArtBboxX=98` 的横向偏移
        /// （叠了会把窗口右移 98px ⇒ 手牌卡面被横切一半）。
        /// `Sprite.Create` 造的 Sprite 不归 Resources 管 ⇒ 必须缓存复用。
        /// </summary>
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
                WarnArtOnce("Game.Res 为空（漏了 CloverRes.Init？），卡面加载不了，卡槽只剩原版底图");
                return;
            }

            Game.Res.LoadAsset<Sprite>(resPath, sprite =>
            {
                if (sprite == null)
                {
                    WarnArtOnce("卡面素材加载不到（该卡槽只画原版底图）：" + resPath);
                    return;
                }

                var made = CrUiStyle.CropCardArt(sprite, artIndex);
                if (made == null)
                {
                    // 非预期分支：纹理裁不出合法矩形（该卡槽只画原版底图）。留痕（只报一次）。
                    WarnArtOnce("卡面帧裁不出合法矩形（该卡槽只画原版底图）：" + resPath);
                    return;
                }
                // ⚠️ 只在**新建**了 Sprite 时才改名：`CropCardArt` 在"导入器已裁好"这条路上直接把
                // `source` 原样返回 ⇒ 原地改名会**反复改同一个 Resources 资产的名字**
                // （实测会累积成 `HudCardArt:HudCardArt:…:frame_049_0`）。
                if (!ReferenceEquals(made, sprite)) made.name = "HudCardArt:" + made.name;
                ArtCache[artIndex] = made;
                onLoaded(made);
            });
        }

        private CardInfo FindCard(int id)
        {
            if (id == 0) return null;
            return _cards.TryGetValue(id, out var card) ? card : null;
        }

        private void SetStatus(string text, Color color)
        {
            if (_statusText == null) return;
            _statusText.text = TextFit.Clamp(_statusText, text);
            _statusText.color = color;
            _statusUntil = string.IsNullOrEmpty(text) ? 0f : Time.unscaledTime + StatusHoldSeconds;
        }
    }
}
