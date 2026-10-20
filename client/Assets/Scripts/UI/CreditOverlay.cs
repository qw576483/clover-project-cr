using CloverEngine;
using UnityEngine;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// 跨页面常驻的引擎署名单件（逐字 <c>by clover-engine</c>）。
    ///
    /// <para>
    /// <b>契约（为什么必须是"常驻件"而不是"每页各加一行"）</b>：署名是常驻 logo —— 切页面 /
    /// 开关面板 / 读条 / 进对局全都在，⛔ 不是"只在启动画面出现一次"。落地口径 = 启动链路里建
    /// **一个**跨场景常驻节点：独立小 Canvas（<c>DontDestroyOnLoad</c>、<c>sortingOrder</c> 高于
    /// 引擎 <c>[UI]</c> 根 Canvas 的 <c>0</c>）+ 引擎自带的 <c>UIFactory.CreateCreditLabel</c>。
    /// ⛔ 不靠"每个页面各自添一行"（那必漏）；落点在本类统一改到**屏底右下角小字**
    /// （引擎默认是贴底居中，会压在通栏导航带的「战斗」按钮那一行上）。
    /// </para>
    /// <para>
    /// <b>常驻范围</b>：由 <c>App/Bootstrap.Start</c> 调起 —— 它在 <c>Boot</c> / <c>Main</c> 两个场景
    /// 各挂一次、且只有第一次走完整拉起路径；本件用 <see cref="EnsureCreated"/> 幂等，
    /// 于是从启动画面起活到退出，不随 <c>Main</c> ↔ <c>Battle01</c> 场景切换（<c>Battle01</c> 无 Bootstrap）
    /// 被带走。
    /// </para>
    /// <para>
    /// ⛔ **不加 <c>GraphicRaycaster</c>、行 <c>raycastTarget = false</c>**：本件纯展示，
    /// 参与射线命中就会挡住它盖住的 UI。
    /// </para>
    /// </summary>
    public static class CreditOverlay
    {
        private const string Tag = "CreditOverlay";

        /// <summary>常驻根节点名（只用于日志与场景排查；⛔ 不靠它查找 —— `GameObject.Find` 是禁用 API）。</summary>
        private const string RootName = "[CreditOverlay]";

        /// <summary>
        /// 署名行离画布底边的距离 = **182**。
        /// <para>
        /// 主菜单底栏是通栏导航带（高 172.8，右半格 = 「战斗」按钮的矩形），署名若落在带内必然
        /// 与按钮矩形相交 ⇒ 抬到带顶上方 9px，仍是屏底右下角，但不压任何按钮。
        /// </para>
        /// </summary>
        private const float BottomOffset = 182f;

        /// <summary>署名行离画布右沿的距离 = **14**（贴在右下角，⛔ 不占导航带主区域）。</summary>
        private const float CornerPadX = 14f;

        /// <summary>署名行字高 = **20**（小字；原 28 号在底栏正中间会压住导航带）。</summary>
        private const int CornerFontSize = 20;

        /// <summary>署名行矩形宽 = **206**（右对齐，足够放 “by clover-engine”；左沿落在 x = 860）。</summary>
        private const float CornerW = 206f;

        /// <summary>对局内的署名行离画布底 = **34**（屏底右下角，在圣水条下沿之下，不压任何按钮）。</summary>
        private const float BattleBottomOffset = 34f;

        /// <summary>对局内署名的文字不透明度 = **0.55**（压在深蓝底板上仍读得出，但不抢 HUD）。</summary>
        private const float BattleAlpha = 0.55f;

        /// <summary>署名行矩形高 = **26**。</summary>
        private const float CornerH = 26f;

        /// <summary>排序权重：必须高于引擎 `[UI]` 根 Canvas 的 `0`（`Runtime/Presentation/UI.cs:50`）。</summary>
        private const int SortingOrder = 100;

        /// <summary>常驻根节点。Unity 的 `!= null` 对已销毁对象返回 false ⇒ 停止 Play 后再进会重建。</summary>
        private static GameObject _root;

        /// <summary>署名文本节点（对局内要改落点与不透明度，建好时留引用）。</summary>
        private static Text _label;

        /// <summary>
        /// 对局内 / 对局外两种落点：对局外贴在主菜单导航带上方（<see cref="BottomOffset"/>），
        /// 对局内在屏底右下角（<see cref="BattleBottomOffset"/>，α <see cref="BattleAlpha"/>）。
        /// </summary>
        public static void SetBattlePlacement(bool battle)
        {
            if (_label == null) return;
            var rt = _label.rectTransform;
            rt.anchoredPosition = new Vector2(-CornerPadX, battle ? BattleBottomOffset : BottomOffset);
            _label.color = battle
                ? new Color(CrUiStyle.TextDim.r, CrUiStyle.TextDim.g, CrUiStyle.TextDim.b, BattleAlpha)
                : CrUiStyle.TextDim;
        }

        /// <summary>建（一次）跨场景常驻署名单件。可重复调用：已建则直接返回。</summary>
        public static void EnsureCreated()
        {
            if (_root != null) return;

            _root = new GameObject(RootName);
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // 画布适配与引擎 [UI] 根同参：直接读引擎的生效值，⛔ 不在这里写第二份常量。
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = CloverPresentation.ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = CloverPresentation.MatchWidthOrHeight;

            // 字体显式传入引擎内置字体：`CreateCreditLabel` 的 font=null 分支会打一条降频 Warn
            // （"未指定字体可能被静默渲染成全大写"），这里不需要那条留痕 —— 字体是已知的。
            var label = UIFactory.CreateCreditLabel(_root.transform, UIFactory.DefaultFont(),
                CrUiStyle.FontSmall, BottomOffset);
            if (label == null)
            {
                // 非预期分支：署名是硬要求，建不出来必须留痕（否则现象是"某些页面少了那行"且零报错）。
                Game.Logger?.Error(Tag, "署名行创建失败（UIFactory.CreateCreditLabel 返回 null）");
                return;
            }

            // 引擎的 `CreateCreditLabel` 落点是“贴底居中”；本项目要的是**屏底右下角**小字：
            // 底栏是通栏导航带，居中署名会压在「战斗」按钮那一行上 ⇒ 这里改锚点 / 字号 / 对齐。
            _label = label;
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(CornerW, CornerH);
            rt.anchoredPosition = new Vector2(-CornerPadX, BottomOffset);
            label.alignment = TextAnchor.LowerRight;
            label.fontSize = CornerFontSize;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            // ⚠️ 必须显式写色：引擎默认是 (1,1,1,0.55)，与本项目口径不同；raycastTarget 见类注释。
            label.color = CrUiStyle.TextDim;
            label.raycastTarget = false;

            Game.Logger?.Info(Tag,
                $"常驻署名单件已建立：文案逐字 by clover-engine / 画布 " +
                $"{CloverPresentation.ReferenceResolution.x:0}×{CloverPresentation.ReferenceResolution.y:0}" +
                $"（match={CloverPresentation.MatchWidthOrHeight:0.##}，同引擎 [UI] 根）/ " +
                $"sortingOrder={SortingOrder}（引擎 [UI]=0）/ 底距 {BottomOffset:0} / 右距 {CornerPadX:0} / 不吃点击");
        }
    }
}
