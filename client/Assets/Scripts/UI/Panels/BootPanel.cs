using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 启动画面（`Boot` 站点，`Main` 场景，Normal 层）—— **竖版**。
    ///
    /// <para>
    /// 内容 = A 的原版加载主视觉背景（铺满）+ **官方 LOGO 图** + **版权字** + **底部 `by clover-engine`**。
    /// 站点推进（停留后自动切 `Main` 场景）由 `AppFlow` 用 `Game.Timer` 控制，
    /// ⛔ 面板不自己切站点（面板只发事件 / 不含流程分支，契约 §1 依赖方向）。
    /// </para>
    ///
    /// <para>
    /// <b>LOGO 用原版图元</b>（⛔ 不用自拼文字 —— 用户口径：UI 必须是原版图元，不是乱拼的）：
    /// A 本体的官方 LOGO 图元 `loading_out` 帧 028（520×224「CLASH ROYALE」蓝底金冠，
    /// 索引 `策划/原版UI素材索引.md` §4.2），按 <see cref="CrUiStyle.LogoOfficial"/> 取。
    /// 位置与宽度是**量出来的**（模板匹配）：
    /// `01_启动页_Logo_1320x2868.jpg` 里 LOGO 的窗口 = x=213..1086（宽 874）、y=117..492，
    /// 归一化到 1080 画布 ⇒ **宽 715px、距屏顶 96px、水平居中**（该参照图与 LOGO 图元的归一化
    /// 互相关系数 NCC=0.66）。
    /// </para>
    ///
    /// <para>
    /// <b>主视觉底 = A 的原版图元</b>：<see cref="ResPaths.BootBackground"/> 指向
    /// `Resources/Sprites/Ui/loading_bg`，内容是 `loading_out` **024**（1152×1880 官方整幅主视觉
    /// 「绿地+建筑+角色」，`策划/原版UI素材索引.md` §4.2 第 24 行 = 该图集里唯一的整幅主视觉）。
    /// ⚠️ 基线图 `01_启动页_Logo_1320x2868.jpg` 是 **2022 版构图**（抛冠那一张），与本项目用的这一张
    /// 是**同一部件、同一官方来源、不同游戏版本** ⇒ 登记为允许差异（`策划/验收表.md` §3）。
    /// ⛔ 不用网页截图当素材（那是降到 JPEG 的二次品）。
    /// </para>
    ///
    /// <para>
    /// <b>`by clover-engine` 是硬要求</b>（项目级约定：引擎自称逐字 `clover-engine`，且必须出现在
    /// 首页画面底部）。这里用引擎**专门**的 `UIFactory.CreateCreditLabel`
    /// （`Runtime/Presentation/UIWidgets.cs:229`，默认文案逐字 `by clover-engine`）—— 它内置
    /// `AnchoredBottom`（锚点与轴心都钉在父节点底边、底部居中），只向 +y 偏 <see cref="BylineBottomOffset"/>。
    /// 引擎注释明确写过：底部元素**不要**用"左上角锚点 + 大负 y"，因为 CanvasScaler 的 match 与真实画布高度
    /// 随设备变化，y 一旦超过画布高度元素就整体掉到屏幕外。
    /// ⚠️ 颜色必须显式写 `CrUiStyle.TextDim`（引擎默认 `(1,1,1,0.55)` 会改变画面）。
    /// </para>
    ///
    /// <para>
    /// <b>版权两行是"用 A 素材"的附带要求</b>（署名 Supercell + 声明非商业用途），A 的启动画面本身没有这两行
    /// —— 本项目新增文案，登记在 `策划/验收表.md` §3「允许的差异」；它们挤在底部署名上方，不侵占 A 的构图区。
    /// </para>
    /// </summary>
    public sealed class BootPanel : UIPanel
    {
        /// <summary>署名离屏幕底边的距离（像素）。本项目自定：只为不贴边、不被安全区圆角裁掉。</summary>
        private const float BylineBottomOffset = 28f;

        /// <summary>
        /// 底部文本宽度（像素）：署名行（引擎 `CreateCreditLabel` 自带宽 600）与上面**版权两行**共用本常量
        /// ⇒ 版权两行仍需本常量。
        /// </summary>
        private const float BylineWidth = 640f;

        /// <summary>底部文本行高（像素）：署名行与版权两行共用。</summary>
        private const float BylineHeight = 32f;

        /// <summary>版权行离屏幕底边的距离（在署名之上，逐行 +34）。本项目自定。</summary>
        private const float CopyrightBottomOffset = 66f;

        /// <summary>版权两行之间的行距（像素）。本项目自定。</summary>
        private const float CopyrightLineGap = 34f;

        /// <summary>LOGO 宽（像素）。出处：A `01_启动页_Logo_1320x2868.jpg` 模板匹配 874px @1320 ⇒ ×(1080/1320) = 715。</summary>
        private const float LogoWidth = 715f;

        /// <summary>LOGO 距屏顶（像素）。出处：同上，y=117 @2868 ⇒ ×(1080/1320) = 96。</summary>
        private const float LogoTopOffset = 96f;

        /// <summary>官方 LOGO 节点（只为运行时把"实测渲染尺寸"写进日志 —— 数值类证据，不靠肉眼）。</summary>
        private Image _logo;

        /// <summary>启动画面保持 Normal 层（架构契约 §4：启动画面 = Normal）。</summary>
        public override UILayer Layer => UILayer.Normal;

        public override void OnOpen(object param)
        {
            var root = (RectTransform)transform;
            // 面板自身必须铺满：UIManager 把面板挂进 Canvas 的层级节点时不会给尺寸，
            // 默认 100×100 的 RectTransform 会让所有子元素挤在屏幕中央的一个小方块里。
            UIFactory.Stretch(root);

            CrUiStyle.Screen("Bg", root);
            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // 官方 LOGO（原版图元，按素材自身比例定尺；贴顶 + 水平居中，位置见类注释的量取出处）。
            _logo = CrUiStyle.AspectImage("Logo", root, CrUiStyle.LogoOfficial, LogoWidth,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -LogoTopOffset), CrUiStyle.PanelBg, false);

            // 版权字：素材来自 A 本体（Supercell APK 解包）⇒ 必须显式署名，并写明非商业用途。
            UIFactory.CreateBottomLabel("Copyright", root, "© Supercell Oy", CrUiStyle.FontSmall,
                new Vector2(0f, CopyrightBottomOffset + CopyrightLineGap),
                new Vector2(BylineWidth, BylineHeight), CrUiStyle.TextDim);

            UIFactory.CreateBottomLabel("CopyrightNote", root, "本作品仅供学习研究 · 非商业用途", CrUiStyle.FontSmall,
                new Vector2(0f, CopyrightBottomOffset), new Vector2(BylineWidth, BylineHeight), CrUiStyle.TextDim);

            // 首页画面底部的引擎署名（硬要求）：走引擎**专门**的 `UIFactory.CreateCreditLabel`
            //   （`Runtime/Presentation/UIWidgets.cs:229`）—— 它内置"贴父节点底边居中"的定位
            //   （`AnchoredBottom` + `TextAnchor.LowerCenter`）、默认文案逐字 `by clover-engine`、
            //   以及"未指定字体 ⇒ 可能被静默渲染成全大写"的降频留痕。
            //   ⛔ 署名一律走 `CreateCreditLabel`，不自拼 `CreateBottomLabel` 的锚底摆法。
            //   ⚠️ **必须显式写 `credit.color`**：引擎默认色是 `(1,1,1,0.55)`，不写就会改变画面；
            //   本项目口径 = `CrUiStyle.TextDim`（与上面版权两行同色）。
            var credit = UIFactory.CreateCreditLabel(root, null, CrUiStyle.FontSmall, BylineBottomOffset);
            credit.color = CrUiStyle.TextDim;

            var logoRt = _logo != null ? _logo.rectTransform : null;
            Game.Logger?.Info("BootPanel",
                "启动画面已打开（原版图元：主视觉 ResPaths.BootBackground=loading_out/024 铺满 · " +
                $"LOGO CrUiStyle.LogoOfficial=loading_out/028 宽 {LogoWidth:0} 实测渲染 {logoRt?.rect.width:0}x{logoRt?.rect.height:0} " +
                $"距顶 {LogoTopOffset:0} · 底部署名逐字 by clover-engine）");
        }
    }
}
