using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI.Panels
{
    /// <summary>
    /// 读条（真进度，System 层）—— **竖版**：`AppFlow` 在 `Game.Scene.Load` 期间打开它，把
    /// `progress` 回调的 0~1 值直接写进进度条。
    ///
    /// <para>
    /// <b>为什么不直接用 <c>Game.UI.ShowLoading</c></b>：引擎那个 Loading 通用件只有"转圈 + 文案"、
    /// **没有进度入参**（`IUIManager.ShowLoading(string text)`），拿它显示不了真进度，
    /// 而任务要求"把真进度显示在面板上"。所以本片自建一个带 `SetProgress` 的面板，
    /// 架构契约 §4 也把 `Loading` 站点列了 `LoadingPanel` 这个选项。
    /// </para>
    /// <para>
    /// <b>层级 = System</b>（最高层，架构契约 §4）：切场景期间它必须盖住一切，
    /// 包括切换途中残留在 Popup 层的面板与 UIManager 的模态遮罩。
    /// </para>
    /// <para>
    /// <b>进度条用锚点宽度而不是 <c>Image.fillAmount</c></b>：后者在 sprite 为空时会走
    /// `Graphic.OnPopulateMesh` 的实心四边形分支而**静默失效**（看着就是"进度条永不动"）——
    /// 引擎专门为此提供了 <see cref="UIFactory.SetBarWidth"/>。
    /// </para>
    /// <para>
    /// <b>竖版落点（全部用底部锚点）</b>：整屏是 A 的原版加载主视觉（铺满），进度条水平居中、
    /// 贴底 <see cref="BarBottomOffset"/>。
    /// </para>
    /// <para>
    /// <b>素材 = A 的原版图元（AP2 复核过）</b>：
    /// 轨道 = `ui_out` **014**（板岩圆角框体，经 <see cref="CrUiStyle.Skin"/> 的 **corner&gt;0 四角镜像** 拼九宫格）
    /// —— ⛔ 不再用 `CrUiStyle.NineSlice` + `BorderButtonDark`：014 是**只有左上角**有圆角+描边的单角件，
    /// 按 border 直接九宫格拉伸会把左上的角贴到四个角上（另外三个角是错的，看图即可见）；
    /// 填充 = `loading_out` **015**（绿色加载读条，115×39）。
    /// </para>
    /// <para>
    /// <b>⚠️ 如实登记一处"未量到"</b>：A 的加载页基线图（`策划/参考图/17_加载页_640x955.png`）**只有整幅美术、
    /// 看不到进度条**（底部队列逐行扫过：y=900..950 全是美术的暗色，没有任何条状亮块）⇒
    /// 进度条的**位置 / 宽 / 高在 A 侧未量到**。这里取"居中 + 宽 900 + 高 64 + 贴底 260"是**本项目自定值**，
    /// 登记为待复核（`.ai-tmp/test/AP2-允许差异.md` D-AP2-3），⛔ 不假装是量出来的。
    /// </para>
    /// <para>
    /// ⛔ <b>别把 `loading_out` 015 用到滑条上</b>：它是**加载页进度条**的填充（本面板是正确用法），
    /// 设置面板的滑条填充必须用原版 ON 按钮的绿（`ui_out` 610）—— 见 `AM2-量取.md` B 段。
    /// </para>
    /// </summary>
    public sealed class LoadingPanel : UIPanel
    {
        /// <summary>进度条宽（像素）。本项目自定（见类注释的"未量到"登记）：= 内容框宽 − 左右各 50。</summary>
        private const float BarW = 900f;

        /// <summary>进度条高（像素）。本项目自定（同上）：比输入框（95）略矮，读起来像"条"。</summary>
        private const float BarH = 64f;

        /// <summary>进度条底边离屏幕底边的距离（像素）。本项目自定（同上）。</summary>
        private const float BarBottomOffset = 260f;

        /// <summary>百分比文字与进度条顶边的间距（像素）。本项目自定。</summary>
        private const float PercentGap = 14f;

        /// <summary>提示文案与进度条底边的间距（像素，向下）。本项目自定。</summary>
        private const float HintGap = 56f;

        private bool _built;
        private RectTransform _barFill;
        private Text _percent;
        private Text _hint;

        /// <summary>读条在 System 层（最顶层）。</summary>
        public override UILayer Layer => UILayer.System;

        public override void OnOpen(object param)
        {
            if (!_built)
            {
                Build();
                _built = true;
            }
            if (_hint != null)
            {
                var text = param as string;
                _hint.text = string.IsNullOrEmpty(text) ? "正在载入…" : text;
            }
            SetProgress(0f);
        }

        private void Build()
        {
            var root = (RectTransform)transform;
            UIFactory.Stretch(root);

            // 不透明底：切场景时下层可能还留着上一个界面的残影，必须完全盖住。
            // 纯色兜底（CrUiStyle.Screen），上面立刻铺原版主视觉 ⇒ 正常情况下这个色块一个像素都看不见。
            CrUiStyle.Screen("Bg", root);

            CrUiStyle.SpriteBackground("BgArt", root, ResPaths.BootBackground);

            // 进度条轨道：A 的原版**板岩圆角框体**（`ui_out` 014）—— 走 `Skin` 的 corner>0 分支把
            // 「左上单角件」镜像拼成九宫格（⛔ 不用 NineSlice+border：那会把同一个角贴到四个角上），
            // 水平居中、贴底。
            var track = CrUiStyle.Skin("Track", root, CrUiStyle.PopupFrameSlate, 24, Vector4.zero,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, BarBottomOffset), new Vector2(BarW, BarH),
                CrUiStyle.BandSlate, true);

            // 填充：A 的原版绿色横条（九宫格），宽度由 SetBarWidth 按比例表达。
            var fill = CrUiStyle.NineSlice("Fill", track.rectTransform, CrUiStyle.LoadingBarFill,
                CrUiStyle.BorderGreenBar,
                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero,
                CrUiStyle.Accent, false);
            _barFill = fill.rectTransform;
            UIFactory.SetBarWidth(_barFill, 0f);

            // 运行时留痕：这是"加载页用的是原版图元"的**可核对**证据（数值类，不靠截图）。
            Game.Logger?.Info("LoadingPanel",
                $"加载界面已打开（原版图元：轨道 ui_out/014 四角镜像九宫格 {BarW:0}×{BarH:0} 贴底 {BarBottomOffset:0} · " +
                $"填充 loading_out/015 绿加载读条 · 主视觉 ResPaths.BootBackground=loading_out/024）");

            // 百分比放进度条正上方（贴底定位，锚点固定在底边 ⇒ 与画布高度无关）。
            _percent = UIFactory.CreateBottomLabel("Percent", root, "0%", CrUiStyle.FontBody,
                new Vector2(0f, BarBottomOffset + BarH + PercentGap), new Vector2(400f, 40f), CrUiStyle.TextColor);

            // 提示文案放进度条下方。
            _hint = UIFactory.CreateBottomLabel("Hint", root, "正在载入…", CrUiStyle.FontSmall,
                new Vector2(0f, BarBottomOffset - HintGap), new Vector2(BarW, 40f), CrUiStyle.TextDim);
        }

        /// <summary>设置进度（0~1）。由 `AppFlow` 在 `Game.Scene.Load` 的 progress 回调里调。</summary>
        public void SetProgress(float progress01)
        {
            var p = Mathf.Clamp01(progress01);
            UIFactory.SetBarWidth(_barFill, p);
            if (_percent != null) _percent.text = Mathf.RoundToInt(p * 100f) + "%";
        }
    }
}
