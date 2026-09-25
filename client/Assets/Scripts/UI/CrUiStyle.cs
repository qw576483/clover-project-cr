using System;
using System.Collections.Generic;
using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// 本项目的 UI 风格与共用建件（**竖版**布局常量 / 原版素材九宫格 / 配色 / 字号档位）。
    ///
    /// <para>
    /// <b>为什么要有这个文件</b>：引擎的 `UIFactory` 只给"造节点"的机械函数、**刻意不带任何配色与字号**
    /// （`Runtime/Presentation/UIWidgetControls.cs` 开头的"没有下沉"清单：配色 / 文案 / 字号档位属业务）。
    /// 6 个面板各写一套 `new Color(...)` 必然漂移，所以收敛到这里一处。
    /// </para>
    ///
    /// <para>
    /// <b>朝向 = 竖版（portrait），已在 T1 判定、本文件不再复议</b>：
    /// A《皇室战争》是竖版游戏（官方截图 1320×2868 / 1242×2208 / 750×1334 全部高 &gt; 宽，
    /// 见 `策划/参考图/清单.md` §0）；用户 2026-09-20 原话「**竖版游戏，你用横板ui，真有你的**」。
    /// 画布 = <see cref="DesignW"/>×<see cref="DesignH"/> = 1080×1920、`match = 0`（宽恒 1080、高随设备浮动，
    /// 设置处 = `App/Bootstrap.LaunchEngine` 的 `CloverPresentation.ReferenceResolution/MatchWidthOrHeight`）。
    /// ⛔ 各面板**不许**再散落 1920 / 1080 字面量 —— 一律引用本类常量，或按锚点自适应。
    /// </para>
    ///
    /// <para>
    /// <b>坐标系约定（⛔ 全项目统一，混用必然错位）</b>：面板**内部**的元素按父节点**左上角**为原点定位
    /// （`UIFactory.AnchoredTopLeft` 口径，pos.y 为负 = 向下）—— 引擎的 Slider / InputField / Selector /
    /// ToggleRow 工厂全是左上角口径，而 `CreateButton` 默认是"父节点中心"口径，所以本类的按钮**先建后重定位**
    /// （与引擎自己的 `CreateNonAccentButton` 同一手法）。
    /// <b>竖版的纵向落点一律用锚点</b>：贴顶的用 <see cref="PanelTopOffset"/> + 顶部锚点，
    /// 贴底的用 `UIFactory.AnchoredBottom` —— ⛔ 不许"左上角 + 大负 y"把底部元素顶出屏
    /// （CanvasScaler match=0 时画布高度随设备浮动，y 一旦超过画布高度元素就整体掉到屏幕外，
    /// 引擎 `UIWidgetControls.AnchoredBottom` 的注释里记了这条实测）。
    /// </para>
    ///
    /// <para>
    /// <b>外观素材：一律 A 本体的原版图元</b>（APK 解包 → `client/Assets/Resources/Sprites/Ui/`，
    /// 路径全部走 <see cref="ResPaths"/>）。"面板底 / 按钮 / 标题条 / 输入框底 / 进度条"这类**可拉伸**部件
    /// 必须走**九宫格**（`Image.type = Sliced` + `Sprite.border`），见 <see cref="NineSlice"/> 的注释；
    /// ⛔ 不许把原图直接拉成任意尺寸（圆角/描边会被拉变形）。
    /// 缺素材时才允许纯色兜底，且必须打一条 Warn（只报一次）并登记进 `策划/验收表.md` §3「允许的差异」`。
    /// </para>
    ///
    /// <para>
    /// <b>尺寸与字号的量取口径</b>：每个数字都记了基线图 `策划/参考图/` 里的像素读数
    /// （像素值 + 占屏百分比 + 折算到 1080 画布的值）。
    /// 折算比例：基线图宽 750 → 1080 画布，**横纵同比例** ×(1080/750)=1.44
    /// （⛔ 不按高度百分比折算：match=0 下画布高度会浮动，按宽度折算才能保证元素尺寸一致）。
    /// 量不到的（A 没有该界面 / 该部件在基线图里看不见）**如实写"未量到"并登记待复核**，⛔ 不编数。
    /// </para>
    /// </summary>
    public static class CrUiStyle
    {
        // ═══════════════════ 设计画布（全项目唯一的画布尺寸出处） ═══════════════════

        /// <summary>设计画布宽度（竖版 1080，`match=0` ⇒ 真机/编辑器里宽度恒为它）。</summary>
        public const float DesignW = 1080f;

        /// <summary>设计画布高度（竖版 1920；`match=0` 下高度随设备浮动，故纵向一律用锚点）。</summary>
        public const float DesignH = 1920f;

        // ═══════════════════ 竖版布局常量（每个都有实测出处） ═══════════════════

        /// <summary>
        /// 内容框宽（面板底 / 标题条 / 输入框所在的那一列）。
        /// <para>
        /// <b>出处</b>：竖版口径「内容框宽 ≤ 1000」。A 侧可量值 = 12_主菜单_750x1334 的
        /// Player Profile 面板（census 实测 x=17..727 ⇒ 711px @750 宽 = 屏宽 94.8%）
        /// ⇒ 折算 1080 画布 = 711×1.44 = **1023.84**。为守住 ≤1000 的口径取 <b>1000</b>，
        /// 与 A 的差值 −23.84px（−2.3%）登记在 `策划/验收表.md` 的「允许的差异」。
        /// </para>
        /// </summary>
        public const float ContentW = 1000f;

        /// <summary>
        /// 面板底离**画布顶边**的距离。
        /// <para>出处：A 面板顶边 y=116 @1334 高（12_主菜单 census/RLE 实测）= 屏高 8.70% ⇒ 8.70%×1920 = <b>167</b>。</para>
        /// </summary>
        public const float PanelTopOffset = 167f;

        /// <summary>面板内左右留白（标题条/输入框/按钮都按它内缩）。本项目新增界面自定：见 <c>G1-量取.md</c> 的如实登记。</summary>
        public const float PanelPad = 40f;

        /// <summary>
        /// 标题条高度。
        /// <para>出处：A 面板标题带 y=126..188（12_主菜单 RLE 实测，h=63px @750）⇒ 63×1.44 = 90.7 ⇒ <b>92</b>。</para>
        /// </summary>
        public const float TitleBarH = 92f;

        /// <summary>
        /// 输入框 / 字段高度。
        /// <para>出处：A 面板字段带 y=449..514（12_主菜单 RLE 实测，h=66px @750）⇒ 66×1.44 = <b>95.0</b>。</para>
        /// </summary>
        public const float FieldH = 95f;

        /// <summary>标签行高（输入框上方的"账号 / 密码 / 昵称"）。本项目新增界面自定。</summary>
        public const float LabelH = 44f;

        /// <summary>主操作按钮高度（比次按钮高，视觉权重）。本项目新增界面自定。</summary>
        public const float ButtonPrimaryH = 110f;

        /// <summary>次操作按钮高度。本项目新增界面自定。</summary>
        public const float ButtonSecondaryH = 95f;

        /// <summary>按钮宽（居中摆放）。本项目新增界面自定。</summary>
        public const float ButtonW = 620f;

        // ═══════════════════ 配色 ═══════════════════
        //
        // <b>角色</b>：这些 Color 现在只剩**兜底**用途（素材取不到时的底色、UI 未接素材的通用件），
        // 不再是最终外观 —— 最终外观一律是 A 的原版图元（见 NineSlice / ContentPanel / ActionButton）。

        /// <summary>整屏底色（背景图未铺满 / 未取到时的兜底底色）。</summary>
        public static readonly Color ScreenBg = new Color(0.043f, 0.063f, 0.106f, 1f);

        /// <summary>内容框底色（**仅**在 <see cref="ResPaths.PanelPaper"/> 取不到时可见）。</summary>
        public static readonly Color PanelBg = new Color(0.055f, 0.086f, 0.149f, 0.96f);

        /// <summary>输入框底色（**仅**在原版框体图元取不到时可见）。</summary>
        public static readonly Color FieldBg = new Color(0.02f, 0.03f, 0.05f, 0.85f);

        /// <summary>按钮常态底色（**仅**在原版按钮图元取不到时可见）。</summary>
        public static readonly Color ButtonBg = new Color(0.102f, 0.169f, 0.278f, 1f);

        /// <summary>按钮悬停底色（九宫格按钮的 hover 用 Image 的 tint 表达）。</summary>
        public static readonly Color ButtonHighlighted = new Color(0.145f, 0.243f, 0.392f, 1f);

        /// <summary>按钮按下底色。</summary>
        public static readonly Color ButtonPressed = new Color(0.078f, 0.129f, 0.212f, 1f);

        /// <summary>按钮禁用底色。</summary>
        public static readonly Color ButtonDisabled = new Color(0.086f, 0.094f, 0.110f, 1f);

        /// <summary>主按钮底色（**仅**在原版金按钮图元取不到时可见）。</summary>
        public static readonly Color PrimaryBg = new Color(0.180f, 0.373f, 0.243f, 1f);

        /// <summary>主按钮悬停底色。</summary>
        public static readonly Color PrimaryHighlighted = new Color(0.235f, 0.478f, 0.310f, 1f);

        /// <summary>主按钮按下底色。</summary>
        public static readonly Color PrimaryPressed = new Color(0.129f, 0.278f, 0.180f, 1f);

        /// <summary>
        /// 正文色（也是按钮文字色：压在金/深蓝按钮上都要看得清）。
        /// <para>
        /// <b>AV1 标定 = 纯白 (255,255,255)</b>：原版 UI 的亮字全是纯白，四处读数一致 ——
        /// `24_设置_499x1080.jpg` 的 CONNECT 钮白字众数 **(255,254,255)**（150 最亮像素均值 (254,254,255)）、
        /// 同图 English 钮白字 **(255,255,255)**、同图标题带 "Settings" **(255,255,255)**（150/150 px）、
        /// `12_主菜单_750x1334.png` 深色字段盘上的 "2786" **(255,255,255)**（74 px）。
        /// 量取口径 = 区域内取最亮 N 像素求众数。
        /// 改前值 (240,243,250) ⇒ 与原版差 **(−15,−12,−5)**；改后差值 **0**。
        /// </para>
        /// </summary>
        public static readonly Color TextColor = new Color(1f, 1f, 1f, 1f);

        /// <summary>次要文字 / 占位文字色。</summary>
        public static readonly Color TextDim = new Color(0.639f, 0.686f, 0.753f, 1f);

        /// <summary>强调色（金色，取自原版主视觉里最亮的那一支；用于状态提示/选中态）。</summary>
        public static readonly Color Accent = new Color(0.984f, 0.780f, 0.251f, 1f);

        /// <summary>错误 / 失败提示色。</summary>
        public static readonly Color ErrorText = new Color(1f, 0.435f, 0.400f, 1f);

        /// <summary>按钮颜色过渡时长（秒）。</summary>
        public const float ButtonFade = 0.08f;

        // ═══════════════════ 字号档位（竖版画布 1080 宽，像素） ═══════════════════
        //
        // <b>标定口径（每个档位都写清来源）</b>：
        //   ① 先从基线图量出**字形高度**（像素）：在指定窗口里取目标颜色像素的 bbox 高度；
        //   ② 折算到 1080 画布 = 字形高度 × 1.44（基线宽 750 → 1080）；
        //   ③ 字号 = 折算后的字形高度 ÷ 字形高度比（LegacyRuntime/Arial 大写字母 ≈ 0.70、数字 ≈ 0.72）。
        //   ⛔ 第 ③ 步的 0.70/0.72 是**字体度量常数**（不是量出来的）；
        //      量不到字形高度的档位如实写"未量到 + 暂用 X（登记待复核）"。

        /// <summary>
        /// 启动画面标题字（**boot 已改用原版 LOGO 图** ⇒ 现在只有结算标题用本档位）。
        /// <para>
        /// <b>未量到 + 暂用 72</b>：A 的启动画面只有 LOGO 图形、没有等价的标题文字；结算界面在
        /// `策划/参考图/清单.md` §2 记着"未取到" ⇒ 无基线可量。登记为待复核（`G1-量取.md` D 段）。
        /// </para>
        /// </summary>
        public const int FontLogo = 72;

        /// <summary>
        /// 面板标题（标题条里的字）。
        /// <para>
        /// 出处：12_主菜单_750x1334 的 "Player Profile"（白字压在标题带上）实测字形 bbox 高 **27px**
        /// @750 ⇒ ×1.44 = 38.9px @1080 ÷ 0.70 = 55.5 ⇒ **56**。
        /// </para>
        /// </summary>
        public const int FontTitle = 56;

        /// <summary>
        /// 正文 / 按钮 / 输入框字。
        /// <para>
        /// 出处：12_主菜单_750x1334 面板字段值 "667"（白字压在深色字段上）实测字形 bbox 高 **16px**
        /// @750 ⇒ ×1.44 = 23.0px @1080 ÷ 0.72 = 32.0 ⇒ **32**。
        /// </para>
        /// </summary>
        public const int FontBody = 32;

        /// <summary>
        /// 小字（提示 / 状态 / 次要说明）。
        /// <para>
        /// <b>未量到 + 暂用 24</b>：A 面板里的小字（如 "#0GJR08UG" 标签）字形太细，量取窗口里
        /// 与背景对比不足、bbox 不可信 ⇒ 不编数；暂用 FontBody×0.75 = 24，登记待复核。
        /// </para>
        /// </summary>
        public const int FontSmall = 24;

        /// <summary>
        /// **宣传大标题字**（卡组页底部那张宣传插图上的「百張卡牌 / 組建牌組」两行）。
        /// <para>
        /// 出处 **E64**：`策划/参考图/07_卡组编辑_1242x2208.jpg` 里，
        /// 用 `V&gt;200` 抓白色字形像素，得到两行的行段与包围盒 ——
        /// 第 1 行 y1719..1914（字高 **196px@1242**，x227..1013 宽 787）、
        /// 第 2 行 y1949..2140（字高 **192px@1242**，x225..1015 宽 791）。
        /// 196 × 0.8696 = **170.4@1080**；四个字摊 787@1242 ⇒ 每字 ≈171@1080（≈ 字高，CJK 方块字距）⇒ 取 **170**。
        /// </para>
        /// <para>
        /// ⚠️ <b>第三片为什么发现这个</b>：第二片只落了这两行的**中心 y**（<c>BannerLine1Y/2Y</c>，
        /// 实测 1579.7/1778@1080 —— 是对得上的），但字号沿用了 <see cref="FontTitle"/> = 56
        /// ⇒ 原版占满半屏的两个大字被画成小字，用户 2026-09-24「**你的UI都不是原版UI啊**」
        /// 里一眼可见的一条。本常量消除它。
        /// </para>
        /// </summary>
        public const int FontBanner = 170;

        // ═══════════════════ 九宫格边框（像素；每一条都是从图元自身像素量出来的） ═══════════════════
        //
        // <b>为什么必须九宫格</b>：原版图元是**定尺寸**画出来的（圆角、描边、斜面都在像素里），
        // 直接拉到别的尺寸 = 圆角被拉成椭圆、描边粗细不均（"变形"）。
        // 九宫格只拉伸"四边中间那一块**本来就均匀**的区域"，四角/四边原样保留。
        //
        // <b>量法</b>：对图元逐列求 `相邻两列的最大通道差`，找出"差 ≤ 容差"的最长连续区间 = 可拉伸区，
        //   区间之外即切边；下表的值同时用肉眼核过逐列逐行剖面。
        //
        // <b>⚠️ Vector4 的字段顺序 = (x=左, y=下, z=右, w=上)</b>（Unity `Sprite.border` 的定义），
        // 与"从上往下读"的习惯不同 —— 下面每个都写成 `(左, 下, 右, 上)` 并注明来源，⛔ 别照抄别处的顺序。

        /// <summary>
        /// 羊皮纸面板底 <see cref="ResPaths.PanelPaper"/>（`ui_out` 806，91×51）。
        /// <para>实测：x=0..1 是深色描边（colR 1→176），x≥2 起米黄且近均匀（colR 229..231）；
        /// 行方向同样只有最外 1..2px 是描边 ⇒ 切边 **(2,2,2,2)**（左,下,右,上）。</para>
        /// </summary>
        public static readonly Vector4 BorderPaper = new Vector4(2f, 2f, 2f, 2f);

        /// <summary>
        /// 金色按钮底 <see cref="ResPaths.ButtonGold"/>（`ui_out` 300，93×94）。
        /// <para>实测：左/右斜面宽 ≈12px（colR 202→240 在 x=11 处转平；右端 233→230 在 x=85 前转平），
        /// 顶/底斜面 ≈12/16px（rowR y=0 是灰色描边、y=11 起亮金、y=82 转深橙）
        /// ⇒ 切边 **(13,16,13,12)**（左,下,右,上）。</para>
        /// </summary>
        public static readonly Vector4 BorderButtonGold = new Vector4(13f, 16f, 13f, 12f);

        /// <summary>
        /// 深蓝灰圆角块 <see cref="ResPaths.ButtonDarkGrey"/>（`ui_out` 014，96×95）—— 次按钮底 / 输入框底。
        /// <para>实测：左/上/下行都是"外圈 1..2px 深色 + 24px 内斜面上坡 + 纯色底"（colR 36→80→98→100 转平在
        /// x≈24；rowR 36→96→92 在 y≈24 转平）⇒ 切边 **(24,24,24,24)**。</para>
        /// <para>同组的 <see cref="ResPaths.ButtonDarkGreyAlt"/>（`ui_out` 015，96×96）是它的 **1px 变体**、
        /// 索引里与 014 同行标注同一用途 ⇒ 沿用本切边（已在 `G1-量取.md` 如实登记）。</para>
        /// </summary>
        public static readonly Vector4 BorderButtonDark = new Vector4(24f, 24f, 24f, 24f);

        /// <summary>
        /// 金色标题条 <see cref="ResPaths.TitleBarGold"/>（`ui_out` 069，1067×62）。
        /// <para>实测：行方向 y=0 是暗线、y≈7 是亮高光、y≥20 起是均匀条体 ⇒ 上切边 **20**、下切边 2；
        /// 列方向两端是圆头（colR 142→132（x≈133）→155（x≈300）再平稳到 x≈816），取 24px 保住圆头
        /// ⇒ 切边 **(24,2,24,20)**（左,下,右,上）。条体沿纵向轻微渐变 ⇒ 纵向可安全拉伸。</para>
        /// </summary>
        public static readonly Vector4 BorderTitleBar = new Vector4(24f, 2f, 24f, 20f);

        /// <summary>
        /// 绿色进度条 <see cref="LoadingBarFill"/>（`loading_out` 015，115×39）—— 读条填充。
        /// <para>实测：列方向完全均匀（colG 恒 211，列间差 &lt; 容差）⇒ 左/右切边 2；
        /// 行方向 y=0 是 35 的暗绿上缘、y≈8 之后恒 228 ⇒ 上切边 **8**、下切边 2
        /// ⇒ 切边 **(2,2,2,8)**（左,下,右,上）。</para>
        /// </summary>
        public static readonly Vector4 BorderGreenBar = new Vector4(2f, 2f, 2f, 8f);

        // ═════════════ 设置面板（AM2）：按**基线图比对**选出来的原版帧与量取值 ═════════════
        //
        // <b>为什么另起一套</b>：用户 2026-09-2x 原话「你这 ui 也太丑了，原版 ui 不长这样啊！！！
        // 原版，界面 按钮 根本不长这样啊！！」并附截图（米色纸面板 + 棕色标题条 + 三条绿色滑条 +
        // 蓝色三角箭头 + 黄色「关闭」宽条 + 黄字百分比）。
        // 选帧口径 = **先取设置界面基线图，再按外观比对选帧**；
        // ⛔ 不按 `策划/原版UI素材索引.md` 的「建议用途」列挑帧（那一列是按缩略图猜的）。
        //
        // <b>基线图</b> = `策划/参考图/24_设置_499x1080.jpg`（499×1080，原版设置界面整屏）
        // 来源 URL = https://www.gameuidatabase.com/uploads/Clash-Royale01022022-071826-52305.jpg
        // 量法 = 按颜色连通块求外接矩形 + 逐点取样，⛔ 读数没有一个是估的。

        /// <summary>设置基线图的像素宽（`策划/参考图/24_设置_499x1080.jpg`）。</summary>
        public const float SettingsBaselineW = 499f;

        /// <summary>基线图 → 1080 画布的折算比（1080/499 = 2.1643）。</summary>
        public const float SettingsBaselineScale = DesignW / SettingsBaselineW;

        /// <summary>设置弹窗体宽。基线：面板亮面 x=33..464（w=432 @499）⇒ 432×2.1643 = <b>935</b>。</summary>
        public const float PopupW = 935f;

        /// <summary>设置弹窗标题带高。基线：y=168..194（h=27 @499）⇒ 27×2.1643 = <b>58</b>。</summary>
        public const float PopupTitleH = 58f;

        /// <summary>弹窗外框描边厚。基线：y=660 处 x=32 是 (95,101,115) 深板岩描边、x=33 起亮面 ⇒ 1px@499 ⇒ <b>2</b>。</summary>
        public const float PopupBorder = 2f;

        /// <summary>弹窗内左右留白。基线：面板左 33、首个按钮左 47 ⇒ 14px@499 ⇒ <b>30</b>。</summary>
        public const float PopupPad = 30f;

        /// <summary>设置行高（行间节距）。基线：绿 ON 行 y=489..518、下一行 y=552..581 ⇒ 节距 63px@499 ⇒ 63×2.1643 = <b>136</b>。</summary>
        public const float PopupRowPitch = 136f;

        /// <summary>设置行里的控件高。基线：绿/蓝按钮 h=30px@499（x=47..242 / 256..451 实测）⇒ <b>65</b>。</summary>
        public const float PopupControlH = 65f;

        /// <summary>设置行里的控件宽（单列一行一颗按钮的原版宽度）。基线 w=196px@499 ⇒ <b>424</b>。</summary>
        public const float PopupControlW = 424f;

        /// <summary>设置行的标签行高（标签压在控件上方，与原版 Music/SFx 同构）。</summary>
        public const float PopupLabelH = 40f;

        /// <summary>设置行里标签与控件之间的缝。</summary>
        public const float PopupLabelGap = 6f;

        /// <summary>弹窗标题字号。基线：「Settings」字形 bbox 高 16px@499 ⇒ ×2.1643 = 34.6 ⇒ ÷0.70 = 49.5 ⇒ <b>48</b>（放进 58 高的带）。</summary>
        public const int FontPopupTitle = 48;

        // ── 基线实测配色（逐点取样，读数见 AM2-量取.md）──

        /// <summary>弹窗体亮面。基线 x=250/y=660 等多点实测 **(229,236,242)**。</summary>
        public static readonly Color PanelLight = new Color32(229, 236, 242, 255);

        /// <summary>标题带 / 外框板岩灰蓝。基线 y=172..194 @x=250 实测 **(99,104,123)**。</summary>
        public static readonly Color BandSlate = new Color32(99, 104, 123, 255);

        /// <summary>亮面上的深色标签字（原版 Music/SFx/Language 标签色）。基线最暗像素 (42,44,43)/(48,48,46)（含黑描边）⇒ 取 **深墨蓝 (42,46,56)**。</summary>
        public static readonly Color TextOnLight = new Color32(42, 46, 56, 255);

        /// <summary>
        /// 亮面上的**次要**字（说明 / 提示行）—— 本项目自定，⛔ 不是从基线量得的：
        /// 取 <see cref="TextOnLight"/> 与 <see cref="PanelLight"/> 的中间调 **(110,118,132)**。
        /// <para>
        /// <b>为什么必须有</b>（AP1 实机实测 2026-09-22）：亮面体 (229,236,242) 上放 `TextDim`(163,175,192)
        /// 或 `TextColor`(240,243,250) 等于**看不见**（`AP1-roomlist-mine.png` / `AP1-room-mine.png` 首版
        /// 的说明文字就是白的压在白底上）—— AM2 在 `24_设置` 上给的解法就是"亮面上的字一律用暗色"。
        /// </para>
        /// </summary>
        public static readonly Color TextOnLightDim = new Color32(110, 118, 132, 255);

        // ── 本节用到的原版帧（⛔ 全部经 `ResPaths.UiFrame`，本类不写裸路径；⛔ 不改 `ResPaths.cs`）──

        /// <summary>弹窗外框 / 标题带 / 滑块轨道 / 灰按钮（`ui_out` 014，96×95，左上圆角件，填充 (96,102,119)）。</summary>
        public static string PopupFrameSlate { get { return ResPaths.UiFrame(ResPaths.UiPanelsDir, ResPaths.UiSrcUi, 14); } }

        /// <summary>弹窗体 / 滑块手柄（`ui_out` 019，21×21，左上圆角件，填充 (216,230,236)）。</summary>
        public static string PopupSkinLight { get { return ResPaths.UiFrame(ResPaths.UiPanelsDir, ResPaths.UiSrcUi, 19); } }

        /// <summary>绿色按钮底（`ui_out` 610，69×69，四角对称的亮绿圆角块 (55,208,77)）—— 原版 ON 按钮。</summary>
        public static string ButtonGreen { get { return ResPaths.UiFrame(ResPaths.UiButtonsDir, ResPaths.UiSrcUi, 610); } }

        /// <summary>红色按钮底（`ui_out` 477，69×69，四角对称的亮红圆角块 (235,65,68)）—— 原版 Off / 关闭底。</summary>
        public static string ButtonRed { get { return ResPaths.UiFrame(ResPaths.UiButtonsDir, ResPaths.UiSrcUi, 477); } }

        /// <summary>
        /// 蓝色按钮底 —— 原版 Language/Name 那类蓝按钮。**用 `ui_out` 166**（38×39）。
        /// <para>
        /// <b>AF3（2026-09-22）换帧：165 → 166</b>。原因 = <see cref="MakeRounded"/>（本文件 478-534）
        /// 只取源帧的**左上** c×c 块做四角镜像，而：
        /// ① `ui_out` 165（38×40）的圆角在**左下**（透明区 bbox x=0..11 / y=27..39），它的**左上 10×10 全不透明**
        /// ⇒ 镜像拼出来的九宫格必然是**方角**（AE1 实机量得按钮四角 inset 恒 0，与 c=10/14 无关）；
        /// ② `ui_out` 166（38×39）的圆角就在**左上**（透明区 bbox x=0..12 / y=0..10，逐行 inset = 13,9,7,6,5,4,3,2,1,1,1,0）
        /// ⇒ 它才是 `MakeRounded` 需要的「左上圆角件」。两帧都已在 `ResPaths` 登记
        /// （`ButtonBlueCorner` = 165 / `ButtonBlueCornerAlt` = 166），此处改引后者，⛔ `ResPaths.cs` 一字未改。
        /// </para>
        /// <para>
        /// 量法 = 逐帧左上 inset 剖面 + 透明 bbox。
        /// ⚠️ 该帧的圆角半径 ≈13px（透明区 bbox x=0..12 / y=0..10 ⇒ 弧到第 13 列/第 11 行才收）
        /// <b>小于</b>原版的 r≈15px@1080 ⇒ 实机复现到 ≈7~13px（见 <see cref="BlueCorner"/> 的 AF4 段），
        /// ⛔ 不写"一致"。
        /// </para>
        /// </summary>
        public static string ButtonBlue { get { return ResPaths.ButtonBlueCornerAlt; } }

        /// <summary>橙色左箭头（`ui_out` 037，69×44，原版实心箭头图元）。▶ 用同一帧水平镜像。</summary>
        public static string ArrowLeftIcon { get { return ResPaths.UiFrame(ResPaths.UiIconsDir, ResPaths.UiSrcUi, 37); } }

        // ── 「左上圆角」原版图元 → 九宫格 Sprite（四角镜像拼，只用原版像素）──

        /// <summary>(路径, 圆角边长) → 现造的九宫格 Sprite 缓存。</summary>
        private static readonly Dictionary<string, Sprite> RoundCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// 造一个**九宫格**原版图元节点，两种取值方式：
        /// <list type="bullet">
        /// <item><paramref name="corner"/> &gt; 0 ⇒ 该帧是「**左上圆角**」件（如 `ui_out` 014/019/165）：
        /// 取它左上 <c>corner×corner</c> 的四角、四边、中心，**镜像**拼成一块 (2c+1)² 的九宫格
        /// （四角原样、四边/中心拉伸）。⛔ 直接用这种帧 + `border` 会把同一个角贴到四个角上（三个角是错的）。</item>
        /// <item><paramref name="corner"/> = 0 ⇒ 该帧本身四角对称（如 `ui_out` 610/477），直接用
        /// <paramref name="border"/> 九宫格拉伸。</item>
        /// </list>
        /// ⛔ 取不到图时退化为 <paramref name="fallback"/> 纯色并打一条 Warn（只报一次）。
        /// </summary>
        public static Image Skin(string name, Transform parent, string resPath, int corner, Vector4 border,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fallback, bool raycast = false,
            Color? tint = null)
        {
            var img = UIFactory.CreatePanel(name, parent, fallback, raycast);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            Dress(img, resPath, corner, border, tint);
            return img;
        }

        /// <summary>给**已存在**的 Image 换上九宫格原版图元（引擎工厂已建好的滑块填充/手柄只能走这里）。</summary>
        public static void Dress(Image img, string resPath, int corner, Vector4 border)
        {
            Dress(img, resPath, corner, border, null);
        }

        /// <summary>
        /// <see cref="Dress(Image, string, int, Vector4)"/> 的**带 tint 重载**（AV1 加）。
        /// <para>
        /// 用途：素材帧自身色 ≠ 那条基线读数时（蓝按钮帧内填色 (48,156,255) vs 原版 (48,112,224)），
        /// 在**不换帧、不改几何**的前提下把外观标到原版读数的唯一杠杆。
        /// <paramref name="tint"/> = <c>null</c> ⇒ 不加 tint（`Color.white` = 原图原色，不加任何滤镜）。
        /// </para>
        /// </summary>
        public static void Dress(Image img, string resPath, int corner, Vector4 border, Color? tint)
        {
            if (img == null) return;
            var face = tint ?? Color.white;

            if (corner > 0)
            {
                var rkey = resPath + "#" + corner;
                Sprite hit;
                if (RoundCache.TryGetValue(rkey, out hit) && hit != null)
                {
                    ApplySliced(img, hit, face);
                    return;
                }
                LoadSprite(resPath, s =>
                {
                    var made = MakeRounded(s, corner, rkey);
                    if (made != null) ApplySliced(img, made, face);
                });
                return;
            }

            LoadSprite(resPath, s =>
            {
                var made = MakeSliced(s, border, resPath);
                if (made != null) ApplySliced(img, made, face);
            });
        }

        /// <summary>
        /// 造一个**原样贴**（`Image.Type.Simple`、不做九宫格）的原版图元节点 —— 图标 / 箭头 / 整幅小图。
        /// 引擎 `UIFactory` 没有"建一个带 sprite 的 Image"的工厂（只有 CreatePanel 给纯色），所以补这一个。
        /// </summary>
        public static Image Icon(string name, Transform parent, string resPath,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, bool raycast = false)
        {
            var img = UIFactory.CreatePanel(name, parent, Color.white, raycast);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            LoadSprite(resPath, s =>
            {
                if (img == null || s == null) return;
                img.sprite = s;
                img.type = Image.Type.Simple;
                img.color = Color.white;
            });
            return img;
        }

        /// <summary>
        /// 造一个**平铺**（<c>Image.Type.Tiled</c>）的原版底纹节点 —— 用于本身就是**无缝重复**
        /// 的整幅纹理（如 <see cref="ResPaths.MenuBackdropTile"/>：源帧的一个 192×192 周期）。
        /// <para>
        /// ⛔ 与 <see cref="Icon"/> 的区别只在 <c>type</c>：图标是整幅拉伸（`Simple`），
        /// 底纹必须**重复**而不是拉大（拉大会把斜格间距放大到原版的数倍）。
        /// 取不到图时保持兜底色并把 <c>color</c> 置白（⛔ 不留 tint 当滤镜）。
        /// </para>
        /// </summary>
        public static Image Backdrop(string name, Transform parent, string resPath, Color fallback,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color tint, bool raycast = false)
        {
            var img = UIFactory.CreatePanel(name, parent, fallback, raycast);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            LoadSprite(resPath, s =>
            {
                if (img == null || s == null) return;
                img.sprite = s;
                img.type = Image.Type.Tiled;
                img.color = tint;
            });
            return img;
        }

        /// <summary>
        /// UI 点击音的唯一公开入口：除 `<see cref="ActionButton"/>/<see cref="Button"/>` 之外，
        /// 各面板自建的 `Button`（设置面板的关闭 / 画质箭头 / 全屏）也要发声，所以把私有的 <see cref="PlayUiClick"/>
        /// 包一层 —— ⛔ 调用方不要自己 `Game.Sound.PlaySFX`（会漏日志、双响）。
        /// </summary>
        public static void PlayClick(string buttonName)
        {
            PlayUiClick(buttonName);
        }

        private static void ApplySliced(Image img, Sprite sprite)
        {
            ApplySliced(img, sprite, Color.white);
        }

        private static void ApplySliced(Image img, Sprite sprite, Color face)
        {
            if (img == null || sprite == null) return;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            // 有图时不能再用兜底色去乘（兜底色会当色调滤镜）；face 默认白 = 原图原色。
            // face ≠ 白时是**标定 tint**（原版读数 ÷ 帧内填色，见 ButtonBlueTint），⛔ 不是随手调色。
            img.color = face;
        }

        /// <summary>
        /// 把「左上圆角」图元镜像拼成 (2c+1)² 的九宫格纹理并造 Sprite（带缓存）。
        /// 四角 = 原像素与它的水平/垂直/双向镜像；四边与中心 = 同一角件最内侧那一行/列/像素
        /// ⇒ **一个像素都不是自造的**。
        /// </summary>
        private static Sprite MakeRounded(Sprite source, int corner, string cacheKey)
        {
            if (source == null || source.texture == null) return null;

            var rect = source.rect;
            var w = Mathf.RoundToInt(rect.width);
            var h = Mathf.RoundToInt(rect.height);
            var c = Mathf.Clamp(corner, 1, Mathf.Max(1, Mathf.Min(w, h) / 2));
            var n = 2 * c + 1;

            Sprite cached;
            if (RoundCache.TryGetValue(cacheKey, out cached) && cached != null) return cached;

            // ⚠️ 帧图**不可读**（全工程 UI 图元的 `.meta` 都是 `isReadable: 0`，
            //    见 `client/Assets/Resources/Sprites/Ui/Panels/ui_out/frame_806.png.meta`）
            //    ⇒ 不能直接 `GetPixels32()`（会抛 "Texture is not readable"）。
            //    走 GPU 侧：Blit 进一张同尺寸的临时 RenderTexture，再 ReadPixels 回 CPU。
            //    落地帧本身已经裁到元件包围盒（几十~一百多像素），临时 RT 很小、且每个素材只做一次（带缓存）。
            var tw = source.texture.width;
            var rt = RenderTexture.GetTemporary(tw, source.texture.height, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Graphics.Blit(source.texture, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(tw, source.texture.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, tw, source.texture.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            var src = readable.GetPixels32();
            var ox0 = Mathf.RoundToInt(rect.x);
            var oy0 = Mathf.RoundToInt(rect.y);
            var outPx = new Color32[n * n];

            for (var oy = 0; oy < n; oy++)
            {
                // oy 从**上边**数；镜像到中间行用最内侧那一行（h-1-c…这里取 c-1，与四角同尺寸）
                // 镜像：右侧第 j 列（j 从 0 起）= 左侧第 (c-1-j) 列 ⇒ sx = 2c-oy/ox（⛔ 别写成 -1，会越界）
                var sy = oy < c ? oy : (oy == c ? c - 1 : 2 * c - oy);
                for (var ox = 0; ox < n; ox++)
                {
                    var sx = ox < c ? ox : (ox == c ? c - 1 : 2 * c - ox);
                    // GetPixels32 的行是**自下而上** ⇒ 输出第 oy 行写到底部起的第 n-1-oy 行
                    outPx[(n - 1 - oy) * n + ox] = src[(oy0 + h - 1 - sy) * tw + ox0 + sx];
                }
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            tex.Apply();
            UnityEngine.Object.Destroy(readable);
            var made = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(c, c, c, c));
            made.name = "Rounded:" + source.name;
            RoundCache[cacheKey] = made;
            return made;
        }

        /// <summary>
        /// 设置弹窗：**板岩灰蓝外框**（`ui_out` 014 左上圆角件拼九宫格）+ 内嵌的**亮面体**
        /// （`ui_out` 019）+ 顶部**同色标题带**。⛔ 不再用 `PanelPaper`（米色羊皮纸）当面板底 ——
        /// 基线图 `24_设置` 上弹窗底实测是亮灰蓝 **(229,236,242)**，米色是**错的**。
        /// <para>锚点 = 画布正中（基线图弹窗水平居中：面板 x=33..464，屏宽 499 ⇒ 中心 248.5 ≈ 249.5）。</para>
        /// </summary>
        public static Image SettingsPopup(string name, Transform parent, float height, float width = PopupW)
        {
            const float c0 = 24f; // 014 的圆角边长（96×95 件，圆角约 24px —— 与 BorderButtonDark 同口径）

            var back = Skin(name, parent, PopupFrameSlate, Mathf.RoundToInt(c0), Vector4.zero,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, height), BandSlate, true);

            var bodyH = height - PopupTitleH - PopupBorder;
            var body = Skin("Body", back.rectTransform, PopupSkinLight, 10, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(PopupBorder, -(PopupTitleH)),
                new Vector2(width - 2f * PopupBorder, bodyH), PanelLight, true);
            body.name = name + "Body";

            return back;
        }

        // ⚠️ 弹窗**亮面体**宽 = 外框宽 − 2×`PopupBorder`，由调用方按常量算式自己写
        // （`CrUiStyle.PopupW - 2f * CrUiStyle.PopupBorder`）—— 因为面板要用它初始化 `const`，
        // 而 `const` 只能吃常量表达式、不能吃方法调用（实测 CS0133）。
        // <b>`width` 的取值口径</b>：**内容真的需要多宽就给多宽** —— 房间两个面板的内容是
        // "字段行 + 整宽按钮"（≈871 够用）⇒ 与主菜单 / 设置同宽 `PopupW`=935；
        // 卡组编辑要放**原版 4 列卡阵**（基线 `07_卡组编辑_1242x2208.jpg` 的卡阵几乎顶到屏边）
        // ⇒ 给 `ContentW`=1000（**仍守住"内容框 ≤ 1000"的竖版口径**）。

        // ═══════════════════ 原版素材路径（⛔ 全部经 ResPaths，本类不写裸路径） ═══════════════════
        //
        // <b>为什么这里会出现一个 `loading_out` 字面量</b>：`ResPaths` 只登记了 `ui_out` / `ui_battle_end_out`
        // 两个源图集常量，而 Logo 与进度条填充来自第三个图集 `loading_out`（索引 §4.2）。
        // `ResPaths.UiFrame(purpose, srcDir, frame)` 是通用签名，所以这里用**常量**传源图集名
        // （⛔ 不直接拼路径、也不改 `ResPaths.cs` —— 那是并行任务的 owner）。
        // 正解是给 `ResPaths` 补一个 `UiSrcLoading` 常量。

        /// <summary>源图集目录名：加载/启动画面（索引 §4.2，34 帧）。</summary>
        private const string SrcLoading = "loading_out";

        /// <summary>
        /// 官方 LOGO（`loading_out` 028，520×224「CLASH ROYALE」蓝底金冠）——
        /// `BootPanel` 的标题图。⛔ 首页标题只许用这张原版 LOGO，不许用文字拼。
        /// </summary>
        public static string LogoOfficial { get { return ResPaths.UiFrame(ResPaths.UiIconsDir, SrcLoading, 28); } }

        /// <summary>读条填充绿条（`loading_out` 015，115×39）—— `LoadingPanel` 的进度条填充。</summary>
        public static string LoadingBarFill { get { return ResPaths.UiFrame(ResPaths.UiBarsDir, SrcLoading, 15); } }

        // ═══════════ 卡面（`ui_spells_out`）：帧号表 + 裁剪口径 —— 全工程唯一一处 ═══════════
        //
        // <b>为什么必须收敛到一处</b>：这张「卡 key → 卡面帧号」表若在 `HudPanel` 与
        // `DeckEditPanel` **各存一份**，两份条数不一致时对局手牌里会有一批卡
        // 查不到帧号，按降级口径只画「原版卡槽底 + 卡名」
        // （玩家看到的就是「卡牌图片和框都对不上」）。
        // ⇒ 结构上消除不同步的唯一办法 = 上收到本类**一处**；⛔ 两个面板都不许再存第二份。
        //
        // <b>表从哪来（⛔ 不是看图猜的）</b>：`策划/原版UI素材名称索引.md` **§3.5** —— 它登记的是原版
        // `原版资源/sc/ui_spells_v215.sc` 的 Export 表（**95 条 export 名** → clip id → `frame_NNN` 全表）。
        // 本表 = 该表按**服务端卡池** `server/game/table/tsv/card.tsv` 的 `key` 逐行对名的结果；
        // 每行行尾注明它对应的**原版 export 名**（卡池 key 与原版内部名不同的那些，对上名的依据就在行尾）。
        // 60 张卡池里 **59 张查得中**，唯一缺口 = `goblin-hut`（见 <see cref="CardArtFrameMissing"/>）。

        /// <summary>
        /// 「已登记、但原版 `ui_spells` 的 95 条 export 里**没有**对应名」的哨兵值
        /// （当前只有 `goblin-hut` ⇒ 该卡只画卡底 + 卡名，⛔ 不猜帧号；已登记进 `策划/验收表.md`）。
        /// </summary>
        public const int CardArtFrameMissing = -1;

        /// <summary>
        /// 卡 `key` → `ui_spells_out` 帧号。键 = `server/game/table/tsv/card.tsv` 的 `key`（60 条）；
        /// 值 = 原版 export 名对照后的 `frame_NNN` 的 NNN（出处见本节抬头）。
        /// </summary>
        private static readonly Dictionary<string, int> CardArtFrameTable = new Dictionary<string, int>
        {
            // ── 部队（40）──
            { "knight", 22 },            // export `knight`
            { "archers", 23 },           // export `archers`
            { "goblins", 49 },           // export `goblins`
            { "spear-goblins", 53 },     // export `goblin_archer`（持矛哥布林的原版内部名）
            { "giant", 55 },             // export `giant`
            { "pekka", 30 },             // export `pekka`
            { "minions", 34 },           // export `minion`
            { "minion-horde", 33 },      // export `minion_horde`
            { "balloon", 69 },           // export `chr_balloon`
            { "witch", 67 },             // export `chr_witch`
            { "barbarians", 80 },        // export `barbarians`
            { "skeletons", 8 },          // export `skeletons`
            { "skeleton-army", 10 },     // export `skeleton_horde`
            { "valkyrie", 3 },           // export `valkyrie`
            { "bomber", 76 },            // export `bomber`
            { "musketeer", 32 },         // export `musketeer`
            { "baby-dragon", 83 },       // export `baby_dragon`
            { "prince", 28 },            // export `prince`
            { "wizard", 2 },             // export `wizard`
            { "mini-pekka", 35 },        // export `mini_pekka`
            { "giant-skeleton", 54 },    // export `giant_skeleton`
            { "hog-rider", 45 },         // export `hog_rider`
            { "ice-wizard", 43 },        // export `ice_wizard`
            { "royal-giant", 21 },       // export `royal_giant`
            { "princess", 27 },          // export `princess`
            { "dark-prince", 65 },       // export `dark_prince`
            { "lava-hound", 41 },        // export `lava_hound`
            { "ice-spirit", 7 },         // export `snow_spirits`（冰雪精灵）
            { "fire-spirit", 60 },       // export `fire_spirits`（烈焰精灵）
            { "miner", 36 },             // export `miner`
            { "bowler", 75 },            // export `bowler`
            { "battle-ram", 78 },        // export `battle_ram`
            { "mega-minion", 37 },       // export `mega_minion`
            { "dart-goblin", 50 },       // export `blowdart_goblin`（吹箭哥布林）
            { "electro-wizard", 63 },    // export `electro_wizard`
            { "executioner", 62 },       // export `executioner`
            { "bandit", 82 },            // export `bandit`
            { "bats", 79 },              // export `bats`
            { "mega-knight", 38 },       // export `mega_knight`
            { "cannon-cart", 71 },       // export `cannon_cart`
            // ── 建筑（10）──
            { "cannon", 70 },            // export `chaos_cannon`（原版加农炮的内部名）
            { "goblin-hut", CardArtFrameMissing },  // ⛔ 缺口：ui_spells 无 `goblin_hut` 命名（只有 barbarian_hut=81 / firespirit_hut=59=火炉）
            { "mortar", 91 },            // export `building_mortar`
            { "inferno-tower", 73 },     // export `building_inferno`
            { "bomb-tower", 77 },        // export `bomb_tower`
            { "barbarian-hut", 81 },     // export `barbarian_hut`
            { "tesla", 90 },             // export `building_tesla`
            { "elixir-collector", 74 },  // export `building_elixir_collector`
            { "x-bow", 72 },             // export `building_xbow`
            { "tombstone", 5 },          // export `tombstone`
            // ── 法术（10）──
            { "fireball", 61 },          // export `fire_fireball`
            { "arrows", 31 },            // export `order_volley`（万箭齐发的原版内部名）
            { "rage", 26 },              // export `rage`
            { "rocket", 24 },            // export `rocket`
            { "goblin-barrel", 52 },     // export `goblin_barrel`
            { "freeze", 57 },            // export `freeze`
            { "lightning", 40 },         // export `lightning`
            { "zap", 1 },                // export `zap`
            { "poison", 29 },            // export `poison`
            { "the-log", 39 },           // export `the_log`
        };

        /// <summary>
        /// 查卡面帧号。返回 <c>true</c> = 有可画的帧（<paramref name="artFrame"/> ≥ 0）。
        /// <para>
        /// 返回 <c>false</c> 有两种情形，调用方要能分开留痕：
        /// ① <see cref="IsCardArtKnownGap"/> = true ⇒ **已知缺口**（原版素材里本来就没有这张卡的 export 名）；
        /// ② 否则 = 服务端卡池出现了本表没登记的新卡（卡池与表脱节）。
        /// </para>
        /// </summary>
        public static bool TryGetCardArtFrame(string cardKey, out int artFrame)
        {
            artFrame = CardArtFrameMissing;
            if (string.IsNullOrEmpty(cardKey)) return false;
            if (!CardArtFrameTable.TryGetValue(cardKey, out artFrame)) return false;
            return artFrame != CardArtFrameMissing;
        }

        /// <summary>该 key 是否**已登记但原版无对应 export 名**（值的哨兵 = <see cref="CardArtFrameMissing"/>）。</summary>
        public static bool IsCardArtKnownGap(string cardKey)
        {
            if (string.IsNullOrEmpty(cardKey)) return false;
            int v;
            return CardArtFrameTable.TryGetValue(cardKey, out v) && v == CardArtFrameMissing;
        }

        /// <summary>表里已登记的卡 key 数（自检/日志用：卡池 60 张应当一条不少）。</summary>
        public static int CardArtFrameCount { get { return CardArtFrameTable.Count; } }

        /// <summary>
        /// 卡面帧的**透明包围盒**（`ui_spells_out` 每帧都是 403×377 画布，图的 alpha 包围盒在
        /// (98,0)-(295,251)）。出处 `策划/原版UI素材名称索引.md` §3.5 的「尺寸」列（197×251）
        /// + AO1 落盘目录逐帧 `PIL.Image.getbbox()` 复核（60 张卡池帧里 43 张恰为 (98,0,295,251)，
        /// 其余为 ±1px 抖动，见 <see cref="CardArtCropOffsetX"/> 的例外表说明）。
        /// </summary>
        public const float CardArtBboxX = 98f;
        public const float CardArtBboxY = 0f;
        public const float CardArtBboxW = 197f;
        public const float CardArtBboxH = 251f;

        /// <summary>
        /// **逐帧** x 裁剪偏移的例外表（默认 = <see cref="CardArtBboxX"/>）。
        /// <para>
        /// <b>为什么需要</b>（AO1 实测）："包围盒固定 (98,0)-(295,251)" **不是对每一帧都成立**。
        /// 对卡池用到的 60 帧逐张量 alpha 包围盒：43 张 = (98,0,295,251)、9 张 = (98,0,294,251)、
        /// 4 张 = (99,0,295,250)、2 张 = (99,0,295,251)（都是 ±1px 抖动，不必例外），
        /// 但 **`frame_022`（= `knight`）在 x=0..197** ⇒ 仍按 98 裁就是「只截到右半身 + 右边一片透明」。
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, float> CardArtCropOffsetXTable = new Dictionary<int, float>
        {
            { 22, 0f },   // knight：实测 alpha bbox = (0,0)-(197,251)
        };

        /// <summary>取该帧的裁剪 x 偏移（未登记例外的帧 = <see cref="CardArtBboxX"/>）。</summary>
        public static float CardArtCropOffsetX(int artFrame)
        {
            float v;
            return CardArtCropOffsetXTable.TryGetValue(artFrame, out v) ? v : CardArtBboxX;
        }

        /// <summary>
        /// 把 <see cref="UnityEngine.Sprite"/> 裁成卡面（去掉帧自身的透明边 + 逐帧 x 偏移）。
        /// `HudPanel` / `DeckEditPanel` **共用这一个口径**（原先各写一份，`HudPanel` 那份少了对
        /// `frame_022` 的例外 ⇒ 骑士的手牌卡面被裁掉一半）。<c>null</c> = 这张纹理裁不出合法矩形。
        /// </summary>
        public static Sprite CropCardArt(Sprite source, int artFrame)
        {
            if (source == null || source.texture == null) return null;
            var r = source.rect;

            // 关键闸门：`ui_spells_out/*.png` 的导入器
            //   已经把**每一帧**裁好了 —— 每张 png 的 .meta 里 `sprites[0].rect` 就是那一帧的内容窗
            //   （实测：`frame_049` = (x=97, y=125, 198×252)、`frame_022` = (x=0, y=125, 198×252)，
            //   两个例外都自带）⇒ 这时 `source.rect` **就是**我们要的窗口，
            //   ⛔ 再加一次 `CardArtBboxX`（98）会把窗口右移 98px（frame_049 实际读到 195..392），
            //   实机表现 = 手牌卡面被横向切掉一半 + 四张卡看起来大小不一（用户第 4 条"图片和框对不上"）。
            //   所以：**只有** sprite 仍覆盖整张纹理（403×377）时才套用「帧窗」。
            if (r.width <= CardArtBboxW + 8f && r.height <= CardArtBboxH + 8f)
                return source;

            var crop = new Rect(CardArtCropOffsetX(artFrame), CardArtBboxY, CardArtBboxW, CardArtBboxH);
            var made = Sprite.Create(source.texture, crop, new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            made.name = "CardArt:" + source.name;
            return made;
        }

        // ═══════════════════ 共用建件 ═══════════════════

        /// <summary>
        /// 铺满整屏的底色（同时挡射线：避免点到下层已关闭面板之外的残留节点）。
        /// 每个面板的根节点都先铺它，保证任意分辨率下没有"露出主相机清屏色"的边。
        /// </summary>
        public static Image Screen(string name, Transform parent)
        {
            var img = UIFactory.CreatePanel(name, parent, ScreenBg, true);
            UIFactory.Stretch(img.rectTransform);
            return img;
        }

        /// <summary>
        /// 居中定尺的内容框（面板主体）。返回的 Image 是普通节点，**子元素按左上角口径定位**
        /// （与引擎的 Slider / InputField / Selector / ToggleRow 工厂一致，见类注释的坐标系约定）。
        /// <para>⚠️ 竖版的新面板不要用它（它把面板摆在屏幕正中、尺寸写死），改用
        /// <see cref="ContentPanel"/>：面板顶按 A 的实测比例贴顶，宽 = <see cref="ContentW"/>。
        /// 保留本方法是因为房间/设置/卡组等面板仍在用。</para>
        /// </summary>
        public static Image CenteredBox(string name, Transform parent, Vector2 size, Color color, bool raycast = true)
        {
            var img = UIFactory.CreatePanel(name, parent, color, raycast);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        /// <summary>居中文本（面板标题 / 居中提示）。坐标是相对父节点的**中心**偏移。</summary>
        public static Text CenteredText(string name, Transform parent, string content, int fontSize,
            Vector2 pos, Vector2 size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var text = UIFactory.CreateText(name, parent, content, fontSize, anchor, color);
            UIFactory.Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            return text;
        }

        // ─────────────── 描边文字（原版 UI 的标题 / 按钮标签全是「白字 + 黑描边」） ───────────────
        //
        // <b>为什么并进本类</b>：AO1 片在 `MainMenuPanel.cs` 里自备过一份 `Outlined()`、
        // AM2 在 `SettingsPanel.cs` 里也自备过同口径的一份 ⇒ 同一件事已经写了三遍（必漂移）。
        // 它是**原版所有面板共用**的文字件，与 `Skin` / `ActionButton` 同级 ⇒ 收敛到这里唯一一处。
        // 依据 = `策划/参考图/12_主菜单_750x1334.png`：「Player Profile」/「Stats Royale」/「Battle Deck」
        // 全是白字带黑描边（底色再亮也读得出）。

        /// <summary>描边色：黑（基线图上每个白字的描边都是黑）。</summary>
        public static readonly Color OutlineDark = Color.black;

        /// <summary>描边距离（px，`(x, y)` = `(2, −2)`）。基线 750 宽上描边约 1.5~2px ⇒ 取 2（登记待复核）。</summary>
        public static readonly Vector2 OutlineDist = new Vector2(2f, -2f);

        /// <summary>
        /// 带**描边**的文本（白字 + 黑描边）。描边用 Unity 自带的 <see cref="Outline"/> 组件
        /// （ugui 内置，⛔ 不是自造图元，⛔ 不是 `new Color` 外观）。
        /// 定位口径与 <see cref="Skin"/> 一致：锚点 / 轴心由调用方给（竖版布局要能贴顶 / 贴左）。
        /// </summary>
        public static Text Outlined(string name, Transform parent, string content, int fontSize,
            Color color, Color outline, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            TextAnchor textAnchor)
        {
            var text = UIFactory.CreateText(name, parent, content, fontSize, textAnchor, color);
            UIFactory.Place(text.rectTransform, anchor, pivot, pos, size);
            var outlineComp = text.gameObject.AddComponent<Outline>();
            outlineComp.effectColor = outline;
            outlineComp.effectDistance = OutlineDist;
            outlineComp.useGraphicAlpha = true;
            return text;
        }

        /// <summary>带描边文本的**最常用形态**：白字 + 黑描边（原版 UI 的绝对多数文字）。</summary>
        public static Text Outlined(string name, Transform parent, string content, int fontSize,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, TextAnchor textAnchor)
        {
            return Outlined(name, parent, content, fontSize, TextColor, OutlineDark,
                anchor, pivot, pos, size, textAnchor);
        }

        /// <summary>
        /// 铺满整屏的**原版素材**背景图。资源路径必须来自 <see cref="ResPaths"/>（⛔ 不许写字面量）。
        /// <para>
        /// **铺满**（`Image.preserveAspect` 保持 Unity 默认的 `false`，本方法刻意不再改它）：
        /// 原版主视觉是竖构图（1154×1882），本项目画布也是竖版（<see cref="DesignW"/>×<see cref="DesignH"/>
        /// = 1080×1920），两者**朝向一致** ⇒ 直接铺满。
        /// </para>
        /// <para>
        /// ⛔ **不许**再用 `preserveAspect`（按原比例缩放 + 两侧留底色）—— 那是"**原图是竖的、画布却是横的**"
        /// 时用来遮左右黑边的补丁，而那种情形的正解是**把朝向判对**，不是把黑边藏起来。
        /// 出处：用户 2026-09-20 原话「竖版游戏，你用横板ui，真有你的」；全局 skill §6 闸门 0（朝向只判一次）。
        /// </para>
        /// <para>取不到图时**保留 <see cref="ScreenBg"/> 纯色底并打一条 Warn**（绝不静默留白屏）。</para>
        /// </summary>
        public static Image SpriteBackground(string name, Transform parent, string resPath)
        {
            var img = UIFactory.CreatePanel(name, parent, ScreenBg, false);
            UIFactory.Stretch(img.rectTransform);
            LoadSprite(resPath, sprite =>
            {
                if (img == null) return;
                img.sprite = sprite;
                img.color = Color.white; // 有图时不能再用底色去乘（底色会当色调滤镜）
            });
            return img;
        }

        /// <summary>
        /// 按**素材自身宽高比**定尺的原版图元（LOGO / 图标这类"一整幅图"的部件）：
        /// 只给目标宽度，高度在加载回调里用 `sprite.rect` 算出来 ⇒ 换了素材也不会变形，
        /// ⛔ 代码里不出现"宽高比"这种会与素材脱钩的魔法数。
        /// <para>⛔ 这类部件**不**做九宫格（它不是可拉伸的框体，拉伸就是变形）。</para>
        /// </summary>
        public static Image AspectImage(string name, Transform parent, string resPath, float width,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Color fallback, bool raycast = false)
        {
            var img = UIFactory.CreatePanel(name, parent, fallback, raycast);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, new Vector2(width, width));
            LoadSprite(resPath, sprite =>
            {
                if (img == null) return;
                var ratio = sprite.rect.width > 0.01f ? sprite.rect.height / sprite.rect.width : 1f;
                img.sprite = sprite;
                img.rectTransform.sizeDelta = new Vector2(width, width * ratio);
                img.color = Color.white; // 有图时不能再用兜底色去乘（兜底色会当色调滤镜）
            });
            return img;
        }

        // ─────────────────── 九宫格建件（面板底 / 按钮 / 标题条 / 输入框底 唯一入口） ───────────────────

        /// <summary>
        /// 造一个**九宫格拉伸**的原版图元节点：`Image.type = Sliced` + `Sprite.border`。
        ///
        /// <para>
        /// <b>为什么要现场重建 Sprite</b>：图元 PNG 是 Unity 按默认设置导入的
        /// （`.meta` 的 `spriteBorder: {0,0,0,0}`），导入态的 `Sprite.border` 恒为 0 ⇒
        /// 直接把它设成 `Image.type = Sliced` **什么也不会发生**（退化成 Simple 拉伸 = 变形）。
        /// 所以这里用 `Sprite.Create(..., SpriteMeshType.FullRect, border)` 现造一个带 border 的 Sprite
        /// （`FullRect` 是九宫格的必需项：Tight 网格只覆盖不透明像素、没有可拉伸的中间块）。
        /// </para>
        /// <para>
        /// <b>复用防泄漏</b>：同一个 (路径, 边框) 只造一次并缓存（`Sprite.Create` 造出来的 Sprite
        /// 不归 Resources 管；每次打开面板都新建会一直涨）。
        /// </para>
        /// <para>
        /// <b>定位用 `Place` 的锚点/轴心口径</b>（不是左上角口径）：竖版布局要能"贴顶 / 贴底 / 居中"，
        /// 所以把锚点交给调用方。<paramref name="size"/> 的宽高按调用方定；四角与四边由 border 保原样。
        /// </para>
        /// <para>⛔ 取不到图时退化为 <paramref name="fallback"/> 纯色**并打一条 Warn（只报一次）**；
        /// 每个退化都必须登记进 `策划/验收表.md` §3「允许的差异」`。</para>
        /// </summary>
        public static Image NineSlice(string name, Transform parent, string resPath, Vector4 border,
            Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fallback, bool raycast = false)
        {
            var img = UIFactory.CreatePanel(name, parent, fallback, raycast);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            LoadSprite(resPath, sprite =>
            {
                if (img == null) return;
                var sliced = MakeSliced(sprite, border, resPath);
                if (sliced == null) return;
                img.sprite = sliced;
                img.type = Image.Type.Sliced;
                img.color = Color.white; // 有图时不能再用兜底色去乘（兜底色会当色调滤镜）
            });
            return img;
        }

        /// <summary>
        /// 竖版面板底：**贴顶居中**、宽 <see cref="ContentW"/>、高由调用方给（内容驱动）。
        /// <para>贴顶距离 = <see cref="PanelTopOffset"/>（A 的面板顶边实测比例）；宽 = <see cref="ContentW"/>。</para>
        /// <para>素材 = <see cref="ResPaths.PanelPaper"/>（A 的原版羊皮纸面板底）+ <see cref="BorderPaper"/>。
        /// 与 A 的浅灰蓝面板（12_主菜单）**不是同一张** —— A 图集里没有可九宫格拉伸的浅灰蓝**实心**面板帧
        /// （最接近的 `ui_out` 592 是空心描边、014/015 是深蓝灰实心），该差异登记在 `策划/验收表.md` §3「允许的差异」。</para>
        /// </summary>
        public static Image ContentPanel(string name, Transform parent, float height)
        {
            return NineSlice(name, parent, ResPaths.PanelPaper, BorderPaper,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -PanelTopOffset), new Vector2(ContentW, height), PanelBg, true);
        }

        /// <summary>
        /// 面板标题条（贴面板顶、满宽）+ 居中标题字。返回标题文本节点。
        /// <para>素材 = <see cref="ResPaths.TitleBarGold"/>（A 的原版金色标题条）+ <see cref="BorderTitleBar"/>；
        /// 条高 = <see cref="TitleBarH"/>（A 的标题带实测）。文字用 <see cref="TextColor"/>（白金压木色才读得出；
        /// <see cref="Accent"/> 金字压在金条上等于看不见）。</para>
        /// </summary>
        public static Text TitleBar(string name, Transform parent, string title, float width)
        {
            var bar = NineSlice(name, parent, ResPaths.TitleBarGold, BorderTitleBar,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(width, TitleBarH), PanelBg, true);
            var text = UIFactory.CreateText("Title", bar.rectTransform, title, FontTitle,
                TextAnchor.MiddleCenter, TextColor);
            UIFactory.Stretch(text.rectTransform);
            return text;
        }

        /// <summary>
        /// 操作按钮（原版图元九宫格 + 居中文字 + 四态）。
        /// <para><paramref name="primary"/> = true 用 <see cref="ResPaths.ButtonGold"/>（金色主按钮），
        /// false 用 <see cref="ResPaths.ButtonDarkGrey"/>（深蓝灰次按钮）—— 两者都是 A 的原版按钮底。</para>
        /// <para>四态用 `Button.colors` 的 **tint 倍乘**表达（原版只有常态底图，没有 hover/pressed 图 ⇒
        /// 用亮度差表达反馈；这不是"给素材加滤镜"，因为常态 = tint 白 = 原图原色）。
        /// 文字用 <see cref="TextColor"/>（白金），压在金/深蓝底上都读得出。</para>
        /// </summary>
        public static Image ActionButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick, bool primary)
        {
            var img = UIFactory.CreatePanel(name, parent, primary ? PrimaryBg : ButtonBg, true);
            UIFactory.AnchoredTopLeft(img.rectTransform, pos, size);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            // UI 点击音（D8）：**所有面板按钮的统一点就是这里**（与 CrUiStyle.Button 一起，全工程仅有这两处建按钮，
            // `UIFactory.CreateButton` 只被本文件调用）⇒ 音效只挂这两处，⛔ 不逐个面板复制粘贴。
            if (onClick != null) btn.onClick.AddListener(() => { PlayUiClick(name); onClick(); });
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = ButtonFade;
            btn.colors = colors;

            var text = UIFactory.CreateText("Label", img.rectTransform, label, FontBody,
                TextAnchor.MiddleCenter, TextColor);
            UIFactory.Stretch(text.rectTransform);

            var resPath = primary ? ResPaths.ButtonGold : ResPaths.ButtonDarkGrey;
            var border = primary ? BorderButtonGold : BorderButtonDark;
            LoadSprite(resPath, sprite =>
            {
                if (img == null) return;
                var sliced = MakeSliced(sprite, border, resPath);
                if (sliced == null) return;
                img.sprite = sliced;
                img.type = Image.Type.Sliced;
                // ⛔ 这里**不**把 color 设成白就完事：按钮的四态靠 tint 驱动，
                //    常态 tint = 白 = 原图原色（见上面的注释）。
                img.color = Color.white;
            });
            return img;
        }

        // ─────────── 原版「蓝按钮」读数与常态标定 tint（AV1 片；⛔ 每个读数都有出处，量法可复跑） ───────────
        //
        // <b>复核结论（AV1 2026-09-22）</b>：AQ2 引的「原版蓝钮实测 (48,112,224)」**可复现**，
        // 但必须写清它的出处 —— 它是 `24_设置_499x1080.jpg` 里**深蓝那颗**按钮（CONNECT 钮
        // x=46..187 / y=375..419）的蓝面读数：众数 **(50,110,224)**（307 / 5 724 蓝像素）、中位 (57,118,228)、
        // 均值 (65,123,226)。同图还有一颗**亮蓝**按钮（English 钮 x=47..241 / y=616..645，
        // 蓝面众数 **(104,173,248)**、981 / 4 989 px）⇒ 原版蓝按钮有**两种**读数，⛔ 不能只写一个数就完事。
        // 量法 = 按蓝色掩膜在指定窗口内求众数/中位/均值 + 垂直剖面；
        // 全图扫过：全 37 张原版图里与 (48,112,224) 每通道差 ≤2 的像素只出现在 `24_设置` 的
        // y=407..413 / x=51..182 这条带上。
        //
        // <b>我们的按钮为什么不是这个色</b>：`ui_out` 165 帧自身的内部填充（该帧 (9,9) 像素，
        // 九宫格镜像拼贴后铺满按钮内部的就是它）= **(48,156,255)**，实机截图逐点读数同为 (48,156,255)
        // 即**当前渲染没有任何 tint/滤镜**（`Image.color` 被设成纯白）。
        // ⇒ 偏差来源 = 帧自身色 ≠ 原版读数（ΔR −2 / ΔG **+44** / ΔB +31），⛔ 不是面板代码问题。

        /// <summary>
        /// 原版**深蓝按钮**蓝面读数（`24_设置` CONNECT 钮众数 (50,110,224)；标定取 (48,112,224)）。
        /// <para>按它标定 <see cref="BlueButton"/>；同时用作蓝按钮**取不到帧时的兜底色**。</para>
        /// </summary>
        public static readonly Color32 ButtonBlueReading = new Color32(48, 112, 224, 255);

        /// <summary>
        /// 原版**亮蓝按钮**蓝面读数（`24_设置` English 钮众数 (104,173,248)）。
        /// <para>⛔ **不**把它当标定目标 —— 标定目标是深蓝读数 <see cref="ButtonBlueReading"/>。</para>
        /// </summary>
        public static readonly Color32 ButtonBlueLightReading = new Color32(104, 173, 248, 255);

        /// <summary>
        /// 蓝按钮的**常态标定 tint** = 原版读数 <see cref="ButtonBlueReading"/> ÷ 素材帧自身的内填色。
        /// <para>
        /// 源帧 = `ui_out/166`（见 <see cref="ButtonBlue"/>）：九宫格铺满按钮内部的那个像素 =
        /// 源帧的 (c−1, c−1)；c = <see cref="BlueCorner"/> = **19**（取值见本文件 `BlueCorner` 声明处）
        /// ⇒ 取角块中心像素 = **(18,18)**，实测其 RGB = **(76,172,255)**；
        /// 分母取**实测值** (76,**172**,255) ⇒ tint = (48/76, **112/172**, 224/255)（×255 ≈ `0xA1A6E0`）。
        /// ⛔ 分母不能用 (76,176,255)：`/176` 令渲染 G = 172×0.6364 ≈ 109，比原版读数低 4（ΔG=−4）。
        /// </para>
        /// <para>
        /// ⛔ **c 必须 ≥ 13**：`166` 的**第 0 行前 13 列全透明**（row0 transparent cols = 0..12），
        /// 而镜像九宫格最外一行/列取自源帧 row 0 / col (c−1)=11 ⇒ c 小于 13 时画出来的按钮
        /// **顶边与底边各掉约 4px**（实测蓝块高 57px，四边完整时应为 65px），且角只到 r≈7px。
        /// 取 c ≥ 13 后九宫格最外一行/列落在 row/col ≥ 12（**全不透明**，`row 12+ / col 13+ 无透明像素`）
        /// ⇒ 顶/底边完整，且整条弧（≤13px）都装进了取角块。
        /// ⚠️ 帧 166 的角部上限经量取 = **≈11px**（逐行 top-left inset 收敛）= 加大 border 追不到原版 ≈15px@1080。
        /// </para>
        /// <para>
        /// 分母口径 = **落盘 PNG 逐像素读数**（当前源帧 = `ui_out/166`，分母 = (76,172,255)），
        /// 目标值 = <see cref="ButtonBlueReading"/> = (48,112,224)。
        /// 另一套读法（帧内取样）在同像素上记作 (48,156,255)，两种口径存在 **+4/+4/0 的方法差**。
        /// ✅ 实机复核：蓝面 **mode=median=mean=(48,111,224)**（两枚画质箭头）＝ 原版读数 (50,110,224) 的
        /// **Δ(−2,+1,0)**（G 差 1 即上述方法差）。
        /// </para>
        /// <para>
        /// tint 仍是**纯乘**：帧自己的高光/暗缘按同一比例缩放，⛔ 不插值、不加特效。
        /// 常态 tint 与四态的关系保持不变（`colors` 仍用白/1.08/0.82/0.55 那套倍率，乘在 tint 之上）。
        /// </para>
        /// </summary>
        public static readonly Color ButtonBlueTint =
            new Color(48f / 76f, 112f / 172f, 224f / 255f, 1f);

        /// <summary>
        /// 原版蓝色按钮件的**取角块边长** = <b>19</b>，全工程唯一来源（`SettingsPanel` 也引用本常量）。
        /// <para>
        /// <b>判值（基线图 + 帧 + 实机三层量）</b>：
        /// ① **基线图** `策划/参考图/24_设置_499x1080.jpg` 里那颗蓝钮（蓝像素连通块 bbox x=46..187 / y=358..419 @499，
        /// 与读蓝用的条带 x=51..182 / y=407..413 同属该块）**四个角**逐行内缩：
        /// 上左 y=358..365 = 6,4,3,2,2,1,1,1（y=366 起 0）、上右同值、下左 y=412..419 = 1,1,1,2,2,3,5,7、
        /// 下右同值 ⇒ 原版圆角 r ≈ 7px @499 ≈ 15px @1080。
        /// ② 素材帧必须是**左上圆角件**：`MakeRounded` 取的是源帧**左上** c×c 块，圆角若落在帧的下左
        /// （如 `ui_out/165`）则不会被复现（实机量取：按钮四角 inset 恒 0 = 方角，⛔ 与 c 无关）。
        /// ③ c 的另一作用 = 九宫格中心像素取自帧的 (c−1, c−1)，它决定乘 tint 后的实机读数
        /// （当前源帧 `166` ⇒ (18,18) = (76,172,255)，见 <see cref="ButtonBlueTint"/>）。
        /// </para>
        /// <para>
        /// <para>
        /// ⛔ c 必须同时满足「弧不被截断」与「最外一行/列不透明」：166 的弧逐行 inset
        /// = 13,9,7,6,5,4,3,2,1,1,1,0（row 11 才收敛 ⇒ c ≥ 12），而 **row 0 前 13 列全透明**
        /// （实测 transparent cols = 0..12），镜像九宫格**最外一行/一列**取自源帧 row 0 / col (c−1)
        /// ⇒ c ≥ 13 时最外一行/列落在 **row 12+ / col 13+（无任何透明像素）**，四边完整、整条弧都在取角块内；
        /// c=12 时实测蓝块高 57px（四边完整时 65px），顶/底各掉约 4px。
        /// 九宫格中心像素随 c 变化，<see cref="ButtonBlueTint"/> 按 c=19 的 **(18,18) = (76,172,255)**
        /// （该帧真正的内部平填色）标定为 (0.6316, 0.6512, 0.8784)。
        /// </para>
        /// </summary>
        public const int BlueCorner = 19;

        /// <summary>
        /// **页签底**用的「左上圆角」件 = <see cref="ResPaths.ButtonBlueCornerBig"/>（`ui_out` 447）。
        /// <para>
        /// 为什么页签不用 <see cref="ButtonBlue"/>（`ui_out` 166）：<see cref="Skin"/> 的镜像拼法
        /// 只能复现**源帧自身圆弧**那么大的圆角 —— 166 的弧 = 11px、165 的左上角是方角，
        /// 都做不出参考图页签的 **25@1242 = 21.7@1080**（`策划/参考图/几何量取.md` E68）。
        /// 447 的弧 = 23px（右下角另量 21px），内填色与 166 **逐像素相同** (76,172,255)
        /// ⇒ 换件不动 tint 分母。
        /// </para>
        /// </summary>
        public static string TabCornerArt { get { return ResPaths.ButtonBlueCornerBig; } }

        /// <summary>
        /// <see cref="TabCornerArt"/> 的取角块边长 = **24**（全工程唯一来源）。
        /// <para>
        /// 取值判据（同 <see cref="BlueCorner"/> 的两条约束）：① 弧不被截断 ⇒ c ≥ 弧 = 23；
        /// ② 镜像九宫格最外一行/一列取自源帧 row 0 / col (c−1)，必须不透明 ⇒ c ≥ 24
        /// （447 的 row 0 在 x=23 起不透明、col 23 整列不透明）。实机圆角 = 23@1080 = 26.5@1242，
        /// 与参考图 25@1242 差 +1.5px（在 ±5px@1242 容差内）。
        /// </para>
        /// </summary>
        public const int TabCornerSize = 24;

        /// <summary>
        /// 菜单 / 卡组页**顶区斜格底纹**的 tint = <c>参考图读数均值 ÷ 素材帧灰度均值</c>。
        /// <para>
        /// 分母 = `ui_out/276` 的灰度均值 **153.5**（该帧 R=G=B，逐像素 max|R−G| = max|G−B| = 0
        /// ⇒ 纯乘 tint 能把中性灰纹染成任意原版色）；
        /// 分子 = 参考图 `07` 顶区纹理实测均值 **(11.4, 49.8, 100.1)**（x0..100 / y26..156，
        /// 排除了页签本体）⇒ tint = (0.0740, 0.3243, 0.6521)。
        /// </para>
        /// <para>
        /// ⚠️ <b>如实登记的残余</b>：该 tint 对**均值**吻合（预判中位 (11,50,100) = 实测），
        /// 但素材帧的灰度跨度 60..231 比参考图同区实测跨度（B 通道 64..118）宽 ⇒ 最亮那几格
        /// 我们会偏亮（预判 B=150 vs 实测 118）。单值纯乘无法同时对上两端，⛔ 不为此改成拟合曲线。
        /// </para>
        /// </summary>
        public static readonly Color BackdropTint = new Color(0.0740f, 0.3243f, 0.6521f, 1f);

        /// <summary>
        /// **蓝底按钮**（原版蓝色圆角件 <see cref="ButtonBlue"/>（`ui_out` **166**，左上圆角件 ⇒ 走
        /// <see cref="Skin"/> 的四角镜像九宫格）+ 白字黑描边标签 + 四态 tint）。
        /// <para>
        /// <b>依据</b>：A 12_主菜单里 "Clan" 按钮 / 各分区蓝 ribbon **全是蓝底白字**（基线图逐点取样
        /// 蓝 ≈ (24,119,233)~(77,175,254)，与素材帧填充 (40,123,201) 同族），
        /// 而金色立体按钮（`ui_out` 300）是商店 / 宝箱的语言 ⇒ 功能面板不用它。
        /// </para>
        /// <para>定位口径 = **左上角**（与 <see cref="ActionButton"/> 完全一致，可直接替换调用）。</para>
        /// <para>
        /// <b>为什么并进本类</b>：`MainMenuPanel`、房间列表 / 房间内 / 卡组编辑都要同一种蓝底按钮，
        /// 各处自备必漂移。
        /// </para>
        /// </summary>
        public static Image BlueButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick)
        {
            return BlueButton(name, parent, label, pos, size, onClick, TextColor);
        }

        /// <summary>
        /// <see cref="BlueButton"/> 的**标签色可指定**重载（登录 / 注册 / 昵称三面板要**纯白**标签
        /// ⇒ 把标签色显式传进来）。
        /// </summary>
        public static Image BlueButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick, Color labelColor)
        {
            // 常态 face = ButtonBlueTint（使渲染结果 = 原版 (48,112,224)）；兜底色 = 同一个原版读数。
            var img = Skin(name, parent, ButtonBlue, BlueCorner, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, ButtonBlueReading, true, ButtonBlueTint);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            // UI 点击音：与 ActionButton / Button 同一处口径（本类是全工程唯一的建按钮处）。
            if (onClick != null) btn.onClick.AddListener(() => { PlayUiClick(name); onClick(); });
            var colors = btn.colors;
            colors.normalColor = Color.white;   // 常态 = 白 = 原版图元原色（⛔ 不用底色当滤镜）
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = ButtonFade;
            btn.colors = colors;

            Outlined("Label", img.rectTransform, label, FontBody, labelColor, OutlineDark,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAnchor.MiddleCenter);
            return img;
        }

        /// <summary>
        /// **板岩按钮**（次操作）：底 = <see cref="PopupFrameSlate"/>（`ui_out` 014 左上圆角件 ⇒ 四角镜像九宫格）
        /// + 白字黑描边 + 四态 tint。定位口径 = **左上角**（与 <see cref="ActionButton"/> /
        /// <see cref="BlueButton"/> 完全一致，可直接替换调用）。
        /// <para>
        /// <b>依据</b>：基线 `24_设置_499x1080.jpg` 的灰按钮行「API Token」实测 **(103,106,121)**
        /// 与弹窗外框板岩 **#636B7B** 同色 ⇒ 次操作 = 板岩底白字。
        /// </para>
        /// <para>
        /// <b>为什么并进本类</b>：`SettingsPanel` / `LoginPanel` / `RegisterPanel` 都要同一种板岩按钮，
        /// 各处自备必漂移。
        /// </para>
        /// </summary>
        public static Image SlateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick)
        {
            return SlateButton(name, parent, label, pos, size, onClick, Color.white);
        }

        /// <summary><see cref="SlateButton"/> 的**标签色可指定**重载（口径同 <see cref="BlueButton"/> 的那个）。</summary>
        public static Image SlateButton(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick, Color labelColor)
        {
            var img = Skin(name, parent, PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, BandSlate, true);

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => { PlayUiClick(name); onClick(); });
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = ButtonFade;
            btn.colors = colors;

            Outlined("Label", img.rectTransform, label, FontBody, labelColor, OutlineDark,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAnchor.MiddleCenter);
            return img;
        }

        /// <summary>
        /// **输入框**（引擎建件 + 底板 = <see cref="PopupFrameSlate"/>（`ui_out` 014）的四角镜像九宫格）。
        /// <para>
        /// <b>⛔ 不能先 <see cref="Field"/> 再 <see cref="Dress"/></b>（AP2 首轮 Play 探针实测）：`Field` 内部会
        /// **异步**把底板换成 `ui_out` 015 的 `NineSlice` 版，两张 sprite 抢同一张 Image、加载完成顺序不定
        /// ⇒ 实测 dump 里底板仍是 `Sliced:frame_015_0`（= 单角件被按 border 拉到四角，三角是错的）。
        /// 这里直接调**同一个引擎工厂**（连线与 `Field` 逐字同口径），底板只由本方法设一次。
        /// </para>
        /// <para>
        /// <b>为什么并进本类</b>：登录 / 注册 / 昵称三面板各写过一份同口径自备实现（连注释都逐字复制），
        /// 收敛到这里唯一一处（AQ1）。
        /// </para>
        /// </summary>
        /// <param name="logTag">建件失败时 Warn 的 tag（保持各面板原来的日志 tag，⛔ 不合并成同一个）。</param>
        public static InputField SlateField(string name, Transform parent, Vector2 pos, Vector2 size,
            string placeholder, int characterLimit, string logTag)
        {
            var input = UIFactory.CreateInputField(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                pos, size, placeholder, characterLimit, InputStyle());
            if (input == null)
            {
                // 非预期分支（必须留痕）：引擎的建件失败 ⇒ 玩家看不到输入框。
                Game.Logger?.Warn(logTag, "输入框建件失败（UIFactory.CreateInputField 返回 null）：" + name);
                return null;
            }
            Dress(input.GetComponent<Image>(), PopupFrameSlate, 24, Vector4.zero);
            return input;
        }

        /// <summary>
        /// **状态按钮**（开 / 关两态）的九宫格切边：<see cref="ButtonGreen"/>（610）/ <see cref="ButtonRed"/>（477）
        /// 都是 69×69 的四角对称圆角块 ⇒ 直接用 `border` 九宫格拉伸（`corner = 0`）。
        /// <para>取值依据 = `SettingsPanel` 上对同一对帧的实测用法（`SettingsPanel.cs:139/259/358`）。</para>
        /// </summary>
        public static readonly Vector4 BorderStateButton = new Vector4(14f, 14f, 14f, 14f);

        /// <summary>
        /// 把一颗按钮底换成**原版状态色**：开 = 绿（<see cref="ButtonGreen"/>，`ui_out` 610）、
        /// 关 = 红（<see cref="ButtonRed"/>，`ui_out` 477）。
        /// <para>
        /// <b>依据</b>：基线 `24_设置_499x1080.jpg` 的 Music/SFx 是**绿 ON**、Filter Clan Chat 是**红 Off**
        /// （实测）；房间面板的「AI 补位」是同一种"开/关"语义 ⇒ 用同一对帧，
        /// ⛔ 不用文字自造（只改文案时底图仍是深蓝灰，与原版语言不符）。
        /// </para>
        /// </summary>
        public static void DressStateButton(Image img, bool on)
        {
            Dress(img, on ? ButtonGreen : ButtonRed, 0, BorderStateButton);
        }

        /// <summary>
        /// 弹窗**顶部板岩带上的居中标题**（白字 + 黑描边）。
        /// <para>
        /// <b>为什么标题压板岩带而不是亮面体</b>：亮面体 (229,236,242) 压白字对比度太低（实测标题发灰）；
        /// 板岩带 (99,104,123) 压白字读得出（基线 `24_设置` 就是板岩带 + 白字）。
        /// </para>
        /// <para><paramref name="back"/> = <see cref="SettingsPopup"/> 返回的那个外框节点。</para>
        /// </summary>
        public static Text BandTitle(string name, Image back, string title, float width)
        {
            var text = Outlined(name, back.rectTransform, title, FontTitle,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(width, PopupTitleH), TextAnchor.MiddleCenter);
            // 标题带必须压在亮面体**之上**：亮面体是后建的兄弟节点（`Skin` 的 body 在 back 之后 AddChild）
            // ⇒ 不置顶的话标题会被亮面体盖住（实测：只看见带子、看不见字）。
            text.rectTransform.SetAsLastSibling();
            return text;
        }

        /// <summary>
        /// 纯色按钮（左上角定位 + `new Color` 底色）。
        /// <para>⚠️ 竖版重排后的新面板**不要**用它 —— 最终外观必须是原版图元，见 <see cref="ActionButton"/>。
        /// 保留本方法是因为房间 / 设置 / 卡组编辑 / 结算等面板仍在用，删掉会直接编译不过。</para>
        /// </summary>
        public static Image Button(string name, Transform parent, string label, Vector2 pos, Vector2 size,
            Action onClick, bool primary = false, int fontSize = FontBody)
        {
            // UI 点击音（D8）：与 ActionButton 同一处口径 —— 本文件是**全工程唯一**建按钮的地方，
            // 所以音效在这里挂一次即覆盖所有面板（⛔ 不许各面板自己再挂，会双响）。
            var img = UIFactory.CreateButton(name, parent, label, size, Vector2.zero,
                primary ? PrimaryBg : ButtonBg, () => { PlayUiClick(name); if (onClick != null) onClick(); });
            UIFactory.AnchoredTopLeft(img.rectTransform, pos, size);

            var btn = img.GetComponent<Button>();
            if (btn != null)
            {
                var colors = btn.colors;
                colors.normalColor = primary ? PrimaryBg : ButtonBg;
                colors.highlightedColor = primary ? PrimaryHighlighted : ButtonHighlighted;
                colors.pressedColor = primary ? PrimaryPressed : ButtonPressed;
                colors.selectedColor = primary ? PrimaryBg : ButtonBg;
                colors.disabledColor = ButtonDisabled;
                colors.colorMultiplier = 1f;
                colors.fadeDuration = ButtonFade;
                btn.colors = colors;
            }

            var labelText = img.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                labelText.fontSize = fontSize;
                labelText.color = TextColor;
            }
            return img;
        }

        /// <summary>
        /// 单行文本输入框（左上角口径定位）+ 原版框体底图（九宫格）。
        /// <para>内部仍走引擎的 <see cref="UIFactory.CreateInputField"/>（textComponent / placeholder /
        /// 光标 / 选区的连线较多，重写风险大于收益），建好后只替换它底板的 `<see cref="Image"/>` ——
        /// `DefaultControls` 产出的 InputField 根节点自带一个 Image，引擎也正是在那里写 `style.Background`。</para>
        /// </summary>
        public static InputField Field(string name, Transform parent, Vector2 pos, Vector2 size,
            string placeholder, int characterLimit)
        {
            var input = UIFactory.CreateInputField(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                pos, size, placeholder, characterLimit, InputStyle());
            if (input == null) return null;

            var bg = input.GetComponent<Image>();
            if (bg != null)
            {
                LoadSprite(ResPaths.ButtonDarkGreyAlt, sprite =>
                {
                    if (bg == null) return;
                    var sliced = MakeSliced(sprite, BorderButtonDark, ResPaths.ButtonDarkGreyAlt);
                    if (sliced == null) return;
                    bg.sprite = sliced;
                    bg.type = Image.Type.Sliced;
                    bg.color = Color.white;
                });
            }
            return input;
        }

        /// <summary>把按钮置灰/恢复（不可用状态不隐藏 —— 隐藏会让布局看起来"少了东西"）。</summary>
        public static void SetButtonEnabled(Image button, bool enabled)
        {
            if (button == null) return;
            var btn = button.GetComponent<Button>();
            if (btn != null) btn.interactable = enabled;
        }

        /// <summary>输入框的统一样式（与 <see cref="UIFactory.CreateInputField"/> 配套）。</summary>
        public static WidgetInputFieldStyle InputStyle()
        {
            var colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.90f, 0.94f, 1f),
                pressedColor = new Color(0.80f, 0.86f, 0.96f),
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = ButtonFade,
            };

            return new WidgetInputFieldStyle
            {
                Background = FieldBg,
                Colors = colors,
                TextColor = TextColor,
                TextFontSize = FontBody,
                PlaceholderColor = TextDim,
                PlaceholderFontSize = FontBody,
                CaretColor = Accent,
                SelectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f),
            };
        }

        /// <summary>滑块的统一样式（音量三条走它）。</summary>
        public static WidgetSliderStyle SliderStyle()
        {
            var handleColors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1f, 0.95f, 0.80f),
                pressedColor = new Color(0.90f, 0.84f, 0.66f),
                selectedColor = Color.white,
                disabledColor = new Color(0.5f, 0.5f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = ButtonFade,
            };

            return new WidgetSliderStyle
            {
                TrackColor = FieldBg,
                FillColor = Accent,
                HandleColor = TextColor,
                HandleColors = handleColors,
                HandleWidth = 18f,
            };
        }

        /// <summary>「◀ 值 ▶」选择行的统一样式（画质档位走它）。</summary>
        public static WidgetSelectorStyle SelectorStyle(string valueText)
        {
            return new WidgetSelectorStyle
            {
                RowHeight = 44f,
                LabelFontSize = FontBody,
                LabelColor = TextColor,
                ValueFontSize = FontBody,
                ValueColor = Accent,
                ValueText = valueText,
                PrevText = "\u25C0", // ◀
                NextText = "\u25B6", // ▶
                ArrowButton = ArrowButtonStyle(),
            };
        }

        /// <summary>开关行（全屏用）。</summary>
        public static WidgetToggleRowStyle ToggleRowStyle(string buttonText)
        {
            return new WidgetToggleRowStyle
            {
                RowHeight = 44f,
                LabelFontSize = FontBody,
                LabelColor = TextColor,
                ButtonText = buttonText,
                Button = ArrowButtonStyle(),
            };
        }

        private static WidgetButtonStyle ArrowButtonStyle()
        {
            return new WidgetButtonStyle
            {
                Background = ButtonBg,
                Highlighted = ButtonHighlighted,
                Pressed = ButtonPressed,
                Disabled = ButtonDisabled,
                FadeDuration = ButtonFade,
                TextColor = TextColor,
                TextFontSize = FontBody,
            };
        }

        // ═══════════════════ 内部：素材加载 / 九宫格 Sprite ═══════════════════

        /// <summary>(路径, 边框) → 现造的九宫格 Sprite（`Sprite.Create` 造的 Sprite 不归 Resources 管，必须复用）。</summary>
        private static readonly Dictionary<string, Sprite> SliceCache = new Dictionary<string, Sprite>();

        /// <summary>已经 Warn 过的路径（缺素材只报一次，避免每次开面板刷屏）。</summary>
        private static readonly HashSet<string> WarnedMissing = new HashSet<string>();

        private static void LoadSprite(string resPath, Action<Sprite> onLoaded)
        {
            if (Game.Res == null)
            {
                // 非预期分支：CloverRes.Init 缺失 ⇒ 全部素材都加载不到。留痕（否则表现为"UI 莫名其妙是纯色"）。
                WarnOnce("Game.Res 为空（漏了 CloverRes.Init？），原版图元加载不了，退化为纯色兜底");
                return;
            }
            Game.Res.LoadAsset<Sprite>(resPath, sprite =>
            {
                if (sprite == null)
                {
                    // 素材没进工程 / 没导入为 Sprite（Texture Type 不是 Sprite 时 LoadAsset<Sprite> 取到 null）。
                    WarnOnce("原版图元加载不到（退化为纯色兜底）：" + resPath);
                    return;
                }
                onLoaded(sprite);
            });
        }

        private static void WarnOnce(string message)
        {
            if (!WarnedMissing.Add(message)) return;
            Game.Logger?.Warn("CrUiStyle", message + "（本条只报一次；兜底外观已登记进 策划/验收表.md` §3「允许的差异」）");
        }

        /// <summary>
        /// UI 按钮点击音（D8 补：矩阵判「全工程无 UI 点击音」为不一致）。
        /// <para>
        /// 素材 = <see cref="AudioPaths.UiClick"/>（`Menu/button_click_02.ogg`，原版通用按钮点击音）。
        /// 挂点 = 本类的两个建件方法（<see cref="ActionButton"/> / <see cref="Button"/>）—— 全工程唯一的建按钮处。
        /// </para>
        /// <para>
        /// ⛔ 不在这里探资源是否存在：<c>Game.Sound.PlaySFX</c> 自己按缺失 WarnOnce（引擎 `Sound.cs`），
        /// 每个按钮点击都探一次是白开销。
        /// </para>
        /// </summary>
        private static void PlayUiClick(string buttonName)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                WarnOnce("Game.Sound 为空（表现域未挂载），UI 点击音播不出来");
                return;
            }
            // 每次点击留一条 Info：**这是"UI 点击音真的响了"的唯一运行时判据**
            //（数值类证据 = 运行时日志行；⛔ 不靠截图/听感）。带按钮名 ⇒ 能对上是哪颗按钮的统一点。
            Game.Logger?.Info("CrUiStyle",
                $"播放音效 {AudioPaths.UiClick}（按钮 {buttonName}，SoundGroup.SFX 音量 {sound.GetVolume(SoundGroup.SFX):F2}）");
            sound.PlaySFX(AudioPaths.UiClick);
        }

        /// <summary>
        /// 用导入态 Sprite 现造一个带 border 的九宫格 Sprite（**带缓存**）。
        /// <para>边框先夹到合法范围：Unity 要求 `左+右 &lt; 宽`、`上+下 &lt; 高`（越界时 Sprite.Create 会报错/退化），
        /// 而且九宫格自己也需要中间至少留 1px 可拉伸区。</para>
        /// </summary>
        private static Sprite MakeSliced(Sprite source, Vector4 border, string cacheKey)
        {
            if (source == null || source.texture == null) return null;

            var w = source.rect.width;
            var h = source.rect.height;
            var maxX = Mathf.Max(0f, (w - 1f) * 0.5f);
            var maxY = Mathf.Max(0f, (h - 1f) * 0.5f);
            var b = new Vector4(
                Mathf.Clamp(border.x, 0f, maxX),
                Mathf.Clamp(border.y, 0f, maxY),
                Mathf.Clamp(border.z, 0f, maxX),
                Mathf.Clamp(border.w, 0f, maxY));

            var key = cacheKey + "|" + b.x + "," + b.y + "," + b.z + "," + b.w;
            Sprite cached;
            if (SliceCache.TryGetValue(key, out cached) && cached != null) return cached;

            // FullRect：九宫格必须整块矩形网格（Tight 只覆盖不透明像素，没有可拉伸的中间块）。
            var made = Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, b);
            made.name = "Sliced:" + source.name;
            SliceCache[key] = made;
            return made;
        }
    }
}
