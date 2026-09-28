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
    /// ⛔ 不靠"每个页面各自添一行"（那必漏），⛔ 也不自写第二套贴底定位。
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

        /// <summary>署名行离画布底边的距离（画布单位）。与 `BootPanel` 的版权行口径一致，只为不贴边。</summary>
        private const float BottomOffset = 28f;

        /// <summary>排序权重：必须高于引擎 `[UI]` 根 Canvas 的 `0`（`Runtime/Presentation/UI.cs:50`）。</summary>
        private const int SortingOrder = 100;

        /// <summary>常驻根节点。Unity 的 `!= null` 对已销毁对象返回 false ⇒ 停止 Play 后再进会重建。</summary>
        private static GameObject _root;

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

            // ⚠️ 必须显式写色：引擎默认是 (1,1,1,0.55)，与本项目口径不同；raycastTarget 见类注释。
            label.color = CrUiStyle.TextDim;
            label.raycastTarget = false;

            Game.Logger?.Info(Tag,
                $"常驻署名单件已建立：文案逐字 by clover-engine / 画布 " +
                $"{CloverPresentation.ReferenceResolution.x:0}×{CloverPresentation.ReferenceResolution.y:0}" +
                $"（match={CloverPresentation.MatchWidthOrHeight:0.##}，同引擎 [UI] 根）/ " +
                $"sortingOrder={SortingOrder}（引擎 [UI]=0）/ 底距 {BottomOffset:0} / 不吃点击");
        }
    }
}
