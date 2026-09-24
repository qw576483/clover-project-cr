using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// 卡组编辑的**按住拖动**手势（差异登记见 `策划/差异登记.tsv`）。
    ///
    /// <para>
    /// <b>职责</b>：`DeckEditPanel` 的格子只有 `Button.onClick`（点一下 = 选中/移除），
    /// 拖动由本组件接「按下 → 移动 → 抬起」三段，并在这三段里**分流**两种意图（见下）。
    /// </para>
    ///
    /// <para>
    /// <b>一次手势只有一种意图（关键决策）</b>：卡池是一个 <see cref="ScrollRect"/>（可上下拖动滚动），
    /// 池里的每一格又要能被拖出来放进卡组 —— 同一个 GameObject 上两种拖动必然互相抢事件。
    /// uGUI 的 <c>EventSystem</c> 把 <c>pointerDrag</c> 判给**最靠前（最深层）**的那个
    /// <c>IDragHandler</c>（`PointerInputModule.ProcessDrag`），也就是本组件；所以本组件拿到事件后
    /// **自己决定**这次手势归谁，规则按优先级两条：
    /// <list type="number">
    /// <item>**手指已经拖出滚动列表的上沿**（`PointerAboveScrollTop`）⇒ 拖卡。
    /// （这一条必须**优先于**方向判定：卡组编辑里槽位行在卡池**上面**，"把卡拖到槽位"的主方向是纵向。）</item>
    /// <item>否则按主方向：**纵向占优** ⇒ 转发给 <see cref="Scroll"/>（滚动列表）；
    /// **横向占优** ⇒ 交给卡牌拖放。</item>
    /// </list>
    /// 一旦定为**拖卡**就不再改主意；滚动中的手势若拖出上沿仍会**升级**成拖卡（见 <see cref="OnDrag"/> ②）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么转发 `OnBeginDrag`/`OnDrag`/`OnEndDrag` 而不是自己搬 `content`</b>：
    /// `ScrollRect` 的这三个方法是 `public virtual` 的正式事件入口，转发后惯性/回弹/边界
    /// 全部走它的既有实现（⛔ 不重造轮子）；而且它是在**转发那一刻**才记
    /// `m_PointerStartLocalCursor` / `m_StartPosition`，所以从手指中段接管**不会跳一下**。
    /// </para>
    ///
    /// <para>
    /// <b>为什么要自己压掉"拖完还触发一次点击"</b>：uGUI 只在
    /// `pointerEvent.pointerPress != pointerEvent.pointerDrag` 时才清 `eligibleForClick`
    /// （`PointerInputModule.cs:388-397`）。本项目的格子把 `Button` 与 `CardDragHandle` 挂在
    /// **同一个 GameObject** 上 ⇒ 两者相等 ⇒ 拖完**仍会**触发 `Button.onClick`，
    /// 表现为"拖动换位之后又顺手把这张卡移除/加入了"。
    /// 又因为 `ReleaseMouse`（`StandaloneInputModule.cs:206-228`）的顺序是
    /// **先 click、后 endDrag** ⇒ 不能在 `OnEndDrag` 里打标记。
    /// 所以标记在 <see cref="OnDrag"/> 里就打上，由面板的点击处理读一次即清（见 <see cref="ConsumeClickSuppressed"/>）。
    /// </para>
    /// </summary>
    public sealed class CardDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>
        /// 判定"这是一次拖动而不是一次点击"的位移阈值（像素）。
        /// <para>
        /// 取 12：uGUI 自己先用 `EventSystem.pixelDragThreshold`（默认 10）挡一道，
        /// 本组件这一道只用来**判方向**，所以取略大于它、且小到不迟滞。
        /// ⚠️ **本项目自定**（原版客户端的手势参数不在原版资源里）⇒ 已登记 `策划/差异登记.tsv`。
        /// </para>
        /// </summary>
        public const float DragThresholdPx = 12f;

        /// <summary>本次手势是否已经"真的拖动过"（用来压掉随后那次 <c>Button.onClick</c>）。</summary>
        private static bool _clickSuppressed;

        /// <summary>读一次并清掉标记：面板的点击处理第一行调它，返回 true 表示"这次点击是拖动的尾巴，忽略"。</summary>
        public static bool ConsumeClickSuppressed()
        {
            if (!_clickSuppressed) return false;
            _clickSuppressed = false;
            return true;
        }

        /// <summary>本格所在的滚动列表；为 null 或 <see cref="AllowScroll"/> 为 false 时本格不参与滚动。</summary>
        public ScrollRect Scroll;

        /// <summary>
        /// 滚动列表的**视口**（用来判"手指有没有拖出列表上沿"）。为 null 时回落到
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

        private enum Gesture
        {
            /// <summary>还没超过阈值，意图未定。</summary>
            Undecided,

            /// <summary>本次手势归滚动列表。</summary>
            Scroll,

            /// <summary>本次手势归卡牌拖放。</summary>
            Card
        }

        private Gesture _gesture;
        private Vector2 _startPos;

        /// <summary>最近一次手势实际走的分支（自检 / 驱动脚本读它，避免"只看日志"）。</summary>
        public string LastGesture { get; private set; }

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
        /// 进了上方那片"卡组区"。允许从 `Scroll` **升级**为 `Card`（见 <see cref="OnDrag"/> 的 ②），
        /// 因为真实的拖卡手势一定是"先在列表里、再向上拖出去"。
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

        /// <summary>屏幕点换算要用的相机：Overlay 画布 ⇒ <c>null</c>（理由见 `HudPanel.UiPointConvertCamera` 的说明）。</summary>
        private Camera UiCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _gesture = Gesture.Undecided;
            _startPos = e.position;
            LastGesture = "undecided";
            // 新手势开始 ⇒ 清掉上一手势可能留下的压点击标记（上一手势若在别处结束，点击可能没来）。
            _clickSuppressed = false;
        }

        public void OnDrag(PointerEventData e)
        {
            // ① 已定 = 拖卡 ⇒ 只上报位移（本次手势不再改主意）。
            if (_gesture == Gesture.Card)
            {
                if (OnDragMoved != null) OnDragMoved(e.position);
                return;
            }

            // ② 正在滚动的这次手势里，手指又拖出了列表上沿 ⇒ **升级**为拖卡。
            //    先让 `ScrollRect` 收尾（`OnEndDrag`），否则它会一直停在"拖动中"，
            //    残留的速度/惯性会在下一帧继续挪 content。
            if (_gesture == Gesture.Scroll && PointerAboveScrollTop(e.position))
            {
                if (Scroll != null) Scroll.OnEndDrag(e);
                _gesture = Gesture.Card;
                LastGesture = "card";
                if (OnDragBegan != null) OnDragBegan(e.position);
                if (OnDragMoved != null) OnDragMoved(e.position);
                return;
            }

            // ③ 滚动中 ⇒ 继续转给 `ScrollRect`。
            if (_gesture == Gesture.Scroll)
            {
                if (Scroll != null) Scroll.OnDrag(e);
                return;
            }

            // ④ 意图未定 ⇒ 过了阈值才判：先看"是不是已经拖出列表上沿"，再看主方向。
            var delta = e.position - _startPos;
            if (delta.magnitude < DragThresholdPx) return;   // 还没过阈值：什么也不做，交给 Button 的点击

            if (AllowScroll && Scroll != null && !PointerAboveScrollTop(e.position)
                && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            {
                _gesture = Gesture.Scroll;
                LastGesture = "scroll";
                Scroll.OnBeginDrag(e);       // 此刻接管：ScrollRect 以当前指针/当前位置为起点，不会跳
            }
            else
            {
                _gesture = Gesture.Card;
                LastGesture = "card";
                if (OnDragBegan != null) OnDragBegan(e.position);
            }

            // 从这一刻起，本次手势的尾巴（pointerUp 之后那次 click）必须被压掉。
            _clickSuppressed = true;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (_gesture == Gesture.Scroll)
            {
                if (Scroll != null) Scroll.OnEndDrag(e);
            }
            else if (_gesture == Gesture.Card)
            {
                if (OnDragEnded != null) OnDragEnded(e.position);
            }
            _gesture = Gesture.Undecided;

            // ★ 关键的收尾：把"压点击"标记清掉。
            //   为什么放在这里而不是等下一次 `OnBeginDrag` 清：`StandaloneInputModule.ReleaseMouse`
            //   （`:206-228`）的顺序是**先 pointerClick、后 endDrag** ⇒ 本次手势的尾巴（如果有）
            //   在进入本方法时**已经派发完了**，此刻清掉既不影响它，又能避免"松手落在别的对象上、
            //   那次 click 没来"时把标记留成 true —— 那种情况下，玩家**下一次正常的点击**
            //   （一次没越过阈值、不产生 beginDrag 的纯点击）会被误判成"拖动的尾巴"而被吞掉。
            _clickSuppressed = false;
        }
    }
}
