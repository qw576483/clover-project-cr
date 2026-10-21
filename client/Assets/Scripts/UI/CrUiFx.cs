using System.Collections.Generic;
using CloverEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CR.UI
{
    /// <summary>
    /// 交互反馈与过场：按钮三态位移、手牌悬停抬起、弹窗淡入 / 淡出、Toast 上滑、页签选中边。
    /// <para>
    /// 本类只改**表现**（透明度 / 缩放 / 本地位移），不参与任何点击语义：
    /// 按钮的 <c>onClick</c> 仍由原建件处绑，面板尺寸 / 锚点 / 层级不变。
    /// 动画一律用 <c>Time.unscaledDeltaTime</c>（暂停 / 结算时仍要动）。
    /// </para>
    /// </summary>
    public static class CrUiFx
    {
        // ── 面板过场（120ms 打开 / 90ms 关闭）──
        public const float PanelOpenSeconds = 0.12f;
        public const float PanelCloseSeconds = 0.09f;
        public const float PanelFromScale = 0.96f;

        // ── 按钮三态 ──
        public const float ButtonHoverTint = 1.06f;
        public const float ButtonPressedTint = 0.92f;
        public const float ButtonPressOffsetY = -2f;
        public const float ButtonPressScale = 0.97f;
        public const float ButtonReleaseSeconds = 0.08f;

        // ── 手牌悬停 ──
        public const float CardHoverSeconds = 0.2f;
        public const float CardHoverLift = 6f;
        public const float CardHoverScale = 1.03f;

        // ── 文字层级 ──
        /// <summary>数值金（原版数值/读数用色）。</summary>
        public static readonly Color NumberGold = new Color32(255, 214, 120, 255);

        /// <summary>标题放大倍率（只缩放文字节点，不改字号 / 不改布局矩形）。</summary>
        public const float TitleScale = 1.06f;

        // ── 页签选中边 ──
        public const float TabEdge = 2f;
        public const float TabUnderline = 6f;

        private static bool _driverInstalled;

        /// <summary>日志标签。</summary>
        public const string LogTag = "CrUiFx";

        /// <summary>
        /// Toast 容器（引擎 Top 层下的 <c>Toasts</c> 节点）：由建层处经 <see cref="BindToastRoot"/> 注入一次，
        /// <see cref="ToastMotionDriver"/> 按它的子节点跟踪新 Toast。未注入 ⇒ 位移不播（只报一次）。
        /// </summary>
        public static Transform ToastRoot { get; private set; }

        /// <summary>绑定 Toast 容器（建层处调用；<c>null</c> 忽略，重复调用以最后一次为准）。</summary>
        public static void BindToastRoot(Transform root)
        {
            if (root == null) return;
            ToastRoot = root;
        }

        /// <summary>装一次 Toast 动效驱动（幂等）。由面板建件处调用。</summary>
        public static void Install()
        {
            if (_driverInstalled || !Application.isPlaying) return;
            _driverInstalled = true;
            var go = new GameObject("CrUiFxDriver");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ToastMotionDriver>();
        }

        /// <summary>给按钮底图挂三态反馈（幂等）。</summary>
        public static void ButtonFx(Graphic target)
        {
            if (target == null) return;
            var fx = target.GetComponent<UiButtonFx>();
            if (fx == null) fx = target.gameObject.AddComponent<UiButtonFx>();
            fx.Target = target;
        }

        /// <summary>
        /// 按钮四态色（常态 = 白 = 原版图元原色；hover 提亮 6%、pressed 压暗 8%、disabled 0.55）。
        /// 全工程按钮共用这一个口径。
        /// </summary>
        public static void ApplyButtonColors(Button btn)
        {
            if (btn == null) return;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(ButtonHoverTint, ButtonHoverTint, ButtonHoverTint, 1f);
            colors.pressedColor = new Color(ButtonPressedTint, ButtonPressedTint, ButtonPressedTint, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = CrUiStyle.ButtonFade;
            btn.colors = colors;
        }

        /// <summary>面板打开：内容节点 alpha 0→1、scale 0.96→1（Unscaled，120ms）。</summary>
        public static void PanelIn(RectTransform content)
        {
            if (content == null) return;
            var fx = content.GetComponent<PanelIntroFx>();
            if (fx == null) fx = content.gameObject.AddComponent<PanelIntroFx>();
            fx.Play(PanelOpenSeconds);
        }

        /// <summary>
        /// 面板关闭：把内容子节点摘到同层的临时容器上，播 90ms 反相淡出后销毁。
        /// <para>
        /// 必须在 <c>OnClose</c> 里调用 —— 引擎 <c>UIManager.Close</c> 紧接着就会销毁面板根节点，
        /// 直接在本体上播动画是看不到的；摘出去的那份是同一个视觉树（不复制、不改结构）。
        /// </para>
        /// </summary>
        public static void PanelOut(Transform panelRoot)
        {
            if (panelRoot == null || panelRoot.childCount == 0) return;
            var parent = panelRoot.parent;
            if (parent == null) return;

            var holder = new GameObject("PanelFadeOut", typeof(RectTransform));
            var rt = (RectTransform)holder.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            var children = new List<Transform>(panelRoot.childCount);
            for (var i = 0; i < panelRoot.childCount; i++) children.Add(panelRoot.GetChild(i));
            for (var i = 0; i < children.Count; i++) children[i].SetParent(rt, true);

            var group = holder.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            holder.AddComponent<PanelExitFx>().Play(rt, group, PanelCloseSeconds);
        }

        /// <summary>项目自管覆盖层（对战入口 / 资料弹窗）：淡入显示。</summary>
        public static void ShowOverlay(GameObject overlay)
        {
            if (overlay == null) return;
            var pending = overlay.GetComponent<OverlayExitFx>();
            if (pending != null) Object.Destroy(pending);
            overlay.SetActive(true);
            var group = overlay.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
            PanelIn((RectTransform)overlay.transform);
        }

        /// <summary>项目自管覆盖层：淡出后 <c>SetActive(false)</c>（90ms）。</summary>
        public static void HideOverlay(GameObject overlay)
        {
            if (overlay == null || !overlay.activeSelf) return;
            var fx = overlay.GetComponent<OverlayExitFx>();
            if (fx == null) fx = overlay.AddComponent<OverlayExitFx>();
            fx.Play(PanelCloseSeconds);
        }

        /// <summary>页签选中态：2px 亮边 + 底部高亮条（纯色件，不改页签几何）。</summary>
        public static void SelectedTabEdge(RectTransform tab, float width, float height, Color color)
        {
            if (tab == null) return;

            var top = UIFactory.CreatePanel("SelEdgeTop", tab, color, false);
            UIFactory.Place(top.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(width, TabEdge));

            var left = UIFactory.CreatePanel("SelEdgeLeft", tab, color, false);
            UIFactory.Place(left.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(TabEdge, height));

            var right = UIFactory.CreatePanel("SelEdgeRight", tab, color, false);
            UIFactory.Place(right.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                Vector2.zero, new Vector2(TabEdge, height));

            var bottom = UIFactory.CreatePanel("SelUnderline", tab, color, false);
            UIFactory.Place(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, new Vector2(width, TabUnderline));
        }

        internal static float EaseOut(float t)
        {
            return UiTween.Ease01(Ease.OutCubic, t);
        }
    }

    /// <summary>按钮三态：hover 提亮（由 <see cref="Button"/> 的 ColorBlock 表达）+ pressed 下移 2px / 缩到 0.97。</summary>
    public sealed class UiButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public Graphic Target;

        /// <summary>当前是否处于按下态。</summary>
        public bool Pressed { get { return _pressed; } }

        private RectTransform _rt;
        private Button _button;
        private Vector2 _basePos;
        private Vector3 _baseScale;
        private bool _captured;
        private bool _pressed;
        private float _releaseT = 1f;

        private void Awake()
        {
            Capture();
        }

        private void OnEnable()
        {
            Capture();
            _pressed = false;
            _releaseT = 1f;
            Apply(0f);
        }

        private void OnDisable()
        {
            if (!_captured || _rt == null) return;
            _rt.anchoredPosition = _basePos;
            _rt.localScale = _baseScale;
        }

        private void Capture()
        {
            if (_captured) return;
            _rt = (RectTransform)transform;
            _button = GetComponent<Button>();
            _basePos = _rt.anchoredPosition;
            _baseScale = _rt.localScale;
            _captured = true;
        }

        public void OnPointerEnter(PointerEventData e) { }

        public void OnPointerExit(PointerEventData e)
        {
            Release();
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_button != null && !_button.interactable) return;
            Capture();
            _pressed = true;
            _releaseT = 1f;
            Apply(1f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            Release();
        }

        private void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            _releaseT = 0f;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进松开回弹（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (!_captured || _pressed || _releaseT >= 1f) return;
            _releaseT = Mathf.Clamp01(_releaseT + dt / CrUiFx.ButtonReleaseSeconds);
            Apply(1f - _releaseT);
        }

        /// <summary>参数 = 按下程度（1 = 完全按下，0 = 复位）。</summary>
        private void Apply(float k)
        {
            if (!_captured || _rt == null) return;
            var e = CrUiFx.EaseOut(k);
            _rt.anchoredPosition = _basePos + new Vector2(0f, CrUiFx.ButtonPressOffsetY * e);
            _rt.localScale = Vector3.Lerp(_baseScale, _baseScale * CrUiFx.ButtonPressScale, e);
        }
    }

    /// <summary>面板打开：alpha 0→1、scale 0.96→1（Unscaled）。播完自摘。</summary>
    public sealed class PanelIntroFx : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _rt;
        private Vector3 _targetScale;
        private float _duration;
        private float _t;

        public void Play(float duration)
        {
            _rt = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _targetScale = _rt.localScale;
            _duration = duration > 0f ? duration : CrUiFx.PanelOpenSeconds;
            _t = 0f;
            _group.alpha = 0f;
            Apply(0f);
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进打开过场（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (_rt == null) { Destroy(this); return; }
            if (_duration <= 0f) _duration = CrUiFx.PanelOpenSeconds;
            _t = Mathf.Clamp01(_t + dt / _duration);
            var e = CrUiFx.EaseOut(_t);
            if (_group != null) _group.alpha = e;
            Apply(e);
            if (_t >= 1f) Destroy(this);
        }

        private void Apply(float e)
        {
            _rt.localScale = Vector3.Lerp(_targetScale * CrUiFx.PanelFromScale, _targetScale, e);
        }
    }

    /// <summary>面板关闭反相（90ms）：alpha 1→0、scale 1→0.96，播完销毁临时容器。</summary>
    public sealed class PanelExitFx : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _rt;
        private float _duration;
        private float _t;

        public void Play(RectTransform rt, CanvasGroup group, float duration)
        {
            _rt = rt;
            _group = group;
            _duration = duration > 0f ? duration : CrUiFx.PanelCloseSeconds;
            _t = 0f;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进关闭反相（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (_duration <= 0f) _duration = CrUiFx.PanelCloseSeconds;
            _t = Mathf.Clamp01(_t + dt / _duration);
            var k = CrUiFx.EaseOut(_t);
            if (_group != null) _group.alpha = 1f - k;
            if (_rt != null) _rt.localScale = Vector3.Lerp(Vector3.one, Vector3.one * CrUiFx.PanelFromScale, k);
            if (_t >= 1f) Destroy(gameObject);
        }
    }

    /// <summary>项目自管覆盖层关闭：淡出后 SetActive(false)。</summary>
    public sealed class OverlayExitFx : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _duration;
        private float _t;
        private GameObject _go;

        public void Play(float duration)
        {
            _go = gameObject;
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _duration = duration > 0f ? duration : CrUiFx.PanelCloseSeconds;
            _t = 0f;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进覆盖层淡出（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (_duration <= 0f) _duration = CrUiFx.PanelCloseSeconds;
            _t = Mathf.Clamp01(_t + dt / _duration);
            if (_group != null) _group.alpha = 1f - CrUiFx.EaseOut(_t);
            if (_t >= 1f)
            {
                if (_group != null) _group.alpha = 1f;
                if (_go != null) _go.SetActive(false);
                Destroy(this);
            }
        }
    }

    /// <summary>手牌悬停：可用卡上抬 6px + 放大 1.03（200ms），不可用卡不响应。</summary>
    public sealed class UiCardHoverFx : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _basePos;
        private Vector3 _baseScale;
        private bool _available = true;
        private bool _hover;
        private float _t;

        /// <summary>该卡当前是否可点（不可点卡不响应 hover）。</summary>
        public bool Available { get { return _available; } }

        /// <summary>当前是否处于抬起态。</summary>
        public bool Hovered { get { return _hover; } }

        private void Awake()
        {
            Capture();
        }

        private void OnEnable()
        {
            Capture();
            _t = 0f;
            _hover = false;
            Apply(0f);
        }

        private void Capture()
        {
            if (_rt != null) return;
            _rt = (RectTransform)transform;
            _basePos = _rt.anchoredPosition;
            _baseScale = _rt.localScale;
        }

        /// <summary>可点 / 不可点（不可点 = hover 不响应；灰色规则本身不在这里改）。</summary>
        public void SetAvailable(bool available)
        {
            _available = available;
            if (!available) SetHover(false);
        }

        public void SetHover(bool on)
        {
            _hover = on && _available;
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进抬起 / 复位（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (_rt == null) return;
            var target = _hover ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            var step = dt / CrUiFx.CardHoverSeconds;
            _t = Mathf.MoveTowards(_t, target, step);
            Apply(CrUiFx.EaseOut(_t));
        }

        /// <summary>参数 = 抬起程度（1 = 完全抬起，0 = 复位）。</summary>
        private void Apply(float e)
        {
            if (_rt == null) return;
            _rt.anchoredPosition = _basePos + new Vector2(0f, CrUiFx.CardHoverLift * e);
            _rt.localScale = Vector3.Lerp(_baseScale, _baseScale * CrUiFx.CardHoverScale, e);
        }
    }

    /// <summary>
    /// Toast 上滑 8px（120ms）：引擎 ToastLayer 已负责淡入，这里只补位移。
    /// 驱动挂在 <c>DontDestroyOnLoad</c> 的常驻节点上，按实例 id 跟踪新出现的 Toast 节点。
    /// </summary>
    public sealed class ToastMotionDriver : MonoBehaviour
    {
        private const float SlideDistance = 8f;
        private const float SlideSeconds = 0.12f;

        private sealed class Entry
        {
            public RectTransform Rt;
            public float Elapsed;
            public Vector2 Target;
            public bool Animating;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly List<int> _dead = new List<int>();
        private Transform _root;

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>推进 Toast 位移（参数 = 帧时长，秒）。</summary>
        public void Tick(float dt)
        {
            if (_root == null)
            {
                // 容器只来自建层处的注入（引擎 UIManager 不把 Toast 容器暴露给业务，⛔ 不按名字查场景）。
                _root = CrUiFx.ToastRoot;
                if (_root == null)
                {
                    LogThrottle.WarnOnce(CrUiFx.LogTag, "toast.root.missing",
                        "未注入 Toast 容器（CrUiFx.BindToastRoot 未被调用）⇒ Toast 上滑位移不播");
                    return;
                }
            }

            for (var i = 0; i < _root.childCount; i++)
            {
                var child = _root.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                var rt = child as RectTransform;
                if (rt == null) continue;
                var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(rt);
                if (!_entries.ContainsKey(id))
                {
                    _entries[id] = new Entry
                    {
                        Rt = rt,
                        Target = rt.anchoredPosition,
                        Elapsed = 0f,
                        Animating = true,
                    };
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y - SlideDistance);
                }
            }

            _dead.Clear();
            foreach (var kv in _entries)
            {
                var e = kv.Value;
                if (e.Rt == null || !e.Rt.gameObject.activeSelf)
                {
                    _dead.Add(kv.Key);
                    continue;
                }
                if (!e.Animating) continue;
                e.Elapsed += dt;
                var k = CrUiFx.EaseOut(e.Elapsed / SlideSeconds);
                e.Rt.anchoredPosition = new Vector2(e.Target.x, e.Target.y - SlideDistance * (1f - k));
                if (e.Elapsed >= SlideSeconds)
                {
                    e.Rt.anchoredPosition = e.Target;
                    e.Animating = false;
                }
            }
            for (var i = 0; i < _dead.Count; i++) _entries.Remove(_dead[i]);
        }
    }
}
