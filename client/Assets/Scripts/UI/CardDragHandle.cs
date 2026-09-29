using System;
using CloverEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// 卡组编辑的**按住拖动**手势接线件。
    ///
    /// <para>
    /// <b>职责</b>：`DeckEditPanel` 的格子只有 `Button.onClick`（点一下 = 选中/移除），
    /// 拖动由本组件接「按下 → 移动 → 抬起」三段，把每段转成一次
    /// <see cref="DragGestureRouter"/> 调用，再按它返回的 <see cref="DragGestureRouter.Step"/>
    /// 转发给 `ScrollRect` 或本组件的三个回调。
    /// </para>
    ///
    /// <para>
    /// <b>为什么用引擎的仲裁器而不是在这里自己判</b>：卡池是一个 <see cref="ScrollRect"/>（可上下拖动滚动），
    /// 池里的每一格又要能被拖出来放进卡组 —— 同一个 GameObject 上两种拖动必然互相抢事件。
    /// uGUI 的 <c>EventSystem</c> 把 <c>pointerDrag</c> 判给**最靠前（最深层）**的那个
    /// <c>IDragHandler</c>（`PointerInputModule.ProcessDrag`），也就是本组件；归属规则
    /// （拖出滚动区优先 / 纵向占优归滚动 / 滚动中升格为拖卡 / 阈值）全部由
    /// `Runtime/Presentation/DragGestureRouter.cs` 回答，本组件只做"把 Step 变成动作"。
    /// 本组件**唯一要自己算的**是 `escapedScrollRegion`（指针有没有拖出滚动区）——
    /// 那是几何，属调用方的画布结构（见 <see cref="PointerAboveScrollTop"/>）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么转发 `OnBeginDrag`/`OnDrag`/`OnEndDrag` 而不是自己搬 `content`**：
    /// `ScrollRect` 的这三个方法是 `public virtual` 的正式事件入口，转发后惯性/回弹/边界
    /// 全部走它的既有实现（⛔ 不重造轮子）；而且它是在**转发那一刻**才记
    /// `m_PointerStartLocalCursor` / `m_StartPosition`，所以从手指中段接管**不会跳一下**。
    /// </para>
    ///
    /// <para>
    /// <b>为什么要压掉"拖完还触发一次点击"</b>：uGUI 只在
    /// `pointerEvent.pointerPress != pointerEvent.pointerDrag` 时才清 `eligibleForClick`
    /// （`PointerInputModule.cs:388-397`）。本项目的格子把 `Button` 与 `CardDragHandle` 挂在
    /// **同一个 GameObject** 上 ⇒ 两者相等 ⇒ 拖完**仍会**触发 `Button.onClick`，
    /// 表现为"拖动换位之后又顺手把这张卡移除/加入了"。
    /// 又因为 `ReleaseMouse`（`StandaloneInputModule.cs:206-228`）的顺序是
    /// **先 click、后 endDrag** ⇒ 不能在 `OnEndDrag` 里打标记。
    /// 标记由 <see cref="DragGestureRouter"/> 在拖动过程中打上，由面板的点击处理读一次即清
    /// （见 <see cref="ConsumeClickSuppressed"/>）。整个工程只有一个指针、一次手势只有一个归属
    /// ⇒ 标记做成**静态**的，面板的点击处理拿不到本实例也能问。
    /// </para>
    /// </summary>
    public sealed class CardDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>
        /// 最近一段手势的仲裁器（引擎件）。
        /// <para>静态的理由见类注释（面板的点击处理只拿得到 `int index`，读不了本实例）。</para>
        /// </summary>
        private static DragGestureRouter _router;

        /// <summary>本格所在的滚动列表；为 null 或 <see cref="AllowScroll"/> 为 false 时本格不参与滚动。</summary>
        public ScrollRect Scroll;

        /// <summary>
        /// 滚动列表的**视口**（用来判"指针有没有拖出列表上沿"）。为 null 时回落到
        /// <see cref="Scroll"/> 自己的 `viewport`。
        /// </summary>
        public RectTransform ScrollViewport;

        /// <summary>本格是否允许"纵向拖动 ⇒ 滚动列表"（卡池格 true；已选槽位 false —— 槽位行不滚动）。</summary>
        public bool AllowScroll;

        /// <summary>本次手势被判定为**拖卡**时回调：参数 = 指针屏幕坐标。</summary>
        public Action<Vector2> OnDragBegan;

        /// <summary>拖卡过程中的每次移动（参数 = 指针屏幕坐标）。</summary>
        public Action<Vector2> OnDragMoved;

        /// <summary>拖卡松手（参数 = 指针屏幕坐标）；落位判定由面板做。</summary>
        public Action<Vector2> OnDragEnded;

        /// <summary>
        /// 最近一次手势实际走的分支（自检 / 驱动脚本读它，避免"只看日志"）。
        /// 取值 = <see cref="DragGestureRouter.LastGestureName"/>（`undecided` / `scroll` / `content`）。
        /// </summary>
        public string LastGesture => _router != null ? _router.LastGestureName : "undecided";

        /// <summary>读一次并清掉"压点击"标记：面板的点击处理第一行调它，返回 true 表示"这次点击是拖动的尾巴，忽略"。</summary>
        public static bool ConsumeClickSuppressed()
        {
            return _router != null && _router.ConsumeClickSuppressed();
        }

        /// <summary>
        /// 把**指针拖到滚动列表上沿之外**当成"拖卡"，优先于"纵向拖动 = 滚动"那条方向判定。
        ///
        /// <para>
        /// <b>为什么必须加这一条</b>：卡组编辑里卡池在**下面**、已选槽位行在**上面**
        /// （原版 `07_卡组编辑` 就是这样排的）。于是"把卡从卡池拖到槽位行"这个手势的主方向恰恰是
        /// **纵向**（|dy| &gt; |dx|）⇒ 只按方向判会把它当滚动，卡永远拖不出来。
        /// 实测：`Card3` 被拖向 `Slot7`（Δ=(62.8, +251.3)）时会被判成 `scroll`，
        /// 卡组一张都不变 —— 光看方向是不够的。
        /// </para>
        /// <para>
        /// 判据 = 指针的屏幕 y **高于视口上沿**（屏幕坐标 y 向上为正）⇒ 手指已经离开列表、
        /// 进了上方那片"卡组区"。允许从滚动**升格**为拖卡（见 <see cref="OnDrag"/> 的
        /// <see cref="DragGestureRouter.Step.EscalateToContent"/> 分支），
        /// 因为真实的拖卡手势一定是"先在列表里、再向上拖出去"。
        /// </para>
        /// <para>
        /// 本方法就是引擎件要求的 `escapedScrollRegion`（它自己不引用 `ScrollRect` / `RectTransform`，
        /// 见 `DragGestureRouter.cs` 的类注释）。
        /// </para>
        /// </summary>
        private bool PointerAboveScrollTop(Vector2 screen)
        {
            if (!AllowScroll || Scroll == null) return false;
            var vp = ScrollViewport != null ? ScrollViewport : Scroll.viewport;
            if (vp == null) return false;
            var top = RectTransformUtility.WorldToScreenPoint(
                UiCamera(), vp.TransformPoint(new Vector3(0f, vp.rect.yMax, 0f)));
            return screen.y > top.y;
        }

        /// <summary>
        /// 世界点 → 屏幕点要用的相机：Overlay 画布 ⇒ <c>null</c>。
        /// <para>相机按画布模式取的口径唯一实现在 `Runtime/Core/ScreenPointUtil.cs`（<see cref="ScreenPointUtil.CameraForUi"/>）：
        /// Overlay 画布的世界坐标就是屏幕像素，喂进相机反而会整体偏一次投影。</para>
        /// </summary>
        private Camera UiCamera()
        {
            return ScreenPointUtil.CameraForUi(this);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (_router == null) _router = new DragGestureRouter();
            // 让位规则的前提是"真的有一个可滚动的列表"：没有列表时任何越阈值手势都归拖卡
            //（已选槽位那一行 `AllowScroll = false`，槽位行不滚动）。
            _router.AllowScroll = AllowScroll && Scroll != null;
            _router.Begin(e.position);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_router == null) return;   // 没经过 OnBeginDrag（非预期）⇒ 什么也不做，交给 Button 的点击

            switch (_router.Move(e.position, PointerAboveScrollTop(e.position)))
            {
                case DragGestureRouter.Step.BeginScroll:
                    if (Scroll != null) Scroll.OnBeginDrag(e);
                    break;

                case DragGestureRouter.Step.MoveScroll:
                    if (Scroll != null) Scroll.OnDrag(e);
                    break;

                case DragGestureRouter.Step.EscalateToContent:
                    // 升格前**先给滚动收尾**：否则 `ScrollRect` 会一直停在"拖动中"，
                    // 残留的速度 / 惯性会在下一帧继续挪 content。
                    if (Scroll != null) Scroll.OnEndDrag(e);
                    if (OnDragBegan != null) OnDragBegan(e.position);
                    if (OnDragMoved != null) OnDragMoved(e.position);
                    break;

                case DragGestureRouter.Step.BeginContent:
                    if (OnDragBegan != null) OnDragBegan(e.position);
                    break;

                case DragGestureRouter.Step.MoveContent:
                    if (OnDragMoved != null) OnDragMoved(e.position);
                    break;

                case DragGestureRouter.Step.None:
                default:
                    // 还没过阈值（也可能是本段手势已经结束）：什么也不做，这次手势仍可能被 Button 当点击收走。
                    break;
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_router == null) return;

            // `End()` 一并清掉"压点击"标记：`ReleaseMouse`（`StandaloneInputModule.cs:206-228`）是
            // **先 click 后 endDrag** ⇒ 本次手势的尾巴（如果有）在进入本方法时已经派发完了，
            // 此刻清掉既不影响它，又不会把标记留成 true 去吞掉下一次正常点击。
            var step = _router.End();
            if (step == DragGestureRouter.Step.EndScroll)
            {
                if (Scroll != null) Scroll.OnEndDrag(e);
            }
            else if (step == DragGestureRouter.Step.EndContent)
            {
                if (OnDragEnded != null) OnDragEnded(e.position);
            }
        }
    }
}
