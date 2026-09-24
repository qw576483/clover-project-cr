using System.Collections.Generic;
using CloverEngine;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 对局特效播放器（轻量）。
    ///
    /// <b>职责</b>：给 `(世界坐标, 原版帧序列, 帧率)` ⇒ 播完自动销毁 / 归池复用。
    /// 素材全部是原版解包 `effects_out`，登记在 <see cref="ResPaths"/> 特效区段。
    ///
    /// <para>
    /// <b>对外契约（⛔ 不许改签名）</b>：<see cref="Create(Transform)"/> 建层；
    /// <see cref="Play(Vector2, string, int, int, float)"/> 定点播一段；
    /// <see cref="PlayFlight(Vector2, Vector2, string, int, int, float)"/> 从 A 飞到 B 播一段。
    /// `use` 取 <see cref="ResPaths.EffectHit"/> 等用途目录名；`firstFrame` = 原版起始帧号
    /// （`ResPaths.EffectHitFirst` 之类），`frameCount` = 帧数，`size` = 目标世界高度（格）。
    /// </para>
    ///
    /// <para>
    /// <b>帧怎么定位</b>：`ResPaths.EffectDir(use)` 目录里就是该用途的**全部帧**（按用途目录
    /// 落地的原版帧，文件名 = 原版源帧号 `frame_NNN`）。这里按 <see cref="SpriteBank.ParseFrameIndex"/>
    /// 解析每帧的原版帧号，从等于 `firstFrame` 的那帧开始、往后取 `frameCount` 帧
    ///（⇒ `firstFrame` 与目录内容对齐，⛔ 不硬编码下标）。
    /// </para>
    ///
    /// <para>
    /// <b>锚点</b>：走 <see cref="SpriteBank.SpritePivotMode.UnifiedCanvasAnchor"/>。特效帧与单位帧同构
    ///（一帧一张 PNG、同一张 474×537 画布），逐帧 pivot 若各按自己裁剪框中心会让特效在画布上"跳位"，
    /// 统一锚点是同一套修法（见该模式的注释）。
    /// </para>
    /// </summary>
    public sealed class EffectsView : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "EffectsView";

        /// <summary>默认播放帧率。出处：`View/UnitView.cs` 的 `AnimFps` 攻击档（14 fps）。</summary>
        public const float DefaultFps = 14f;

        /// <summary>同屏特效上限（超出忽略 + 只 Warn 一次）。</summary>
        public const int MaxLive = 64;

        /// <summary>特效世界高度（格）。原版画布 474x537，meta 的 PPU=100 ⇒ 537/100 = 5.37 格。</summary>
        public const float WorldSize = 5.37f;

        /// <summary>
        /// 飞行弹道**兜底**时长（秒）。出处：**本项目自定**。
        /// <para>
        /// ⚠️ 调用方（`BattleViewRoot.PlayProjectileFlight`）**一律显式传入**由
        /// `CardInfo.proj_speed`（格/分钟）算出的时长 ⇒ 本常量只在调用方传 <c>0</c> 时生效。
        /// </para>
        /// </summary>
        private const float FlightSeconds = 0.4f;

        private Transform _root;
        private readonly List<Node> _live = new List<Node>();
        private readonly Stack<Node> _idle = new Stack<Node>();

        /// <summary>
        /// 已**成功**建出的特效节点总数（自检用：调用方读它前后的差，就能知道"这一次 Play 到底有没有真的播出去"）。
        /// <para>
        /// 为什么不用"改 <see cref="Play"/> 的返回值"：<see cref="Play"/> / <see cref="PlayFlight(Vector2, Vector2, string, int, int, float)"/>
        /// 是**对外契约、⛔ 不许改签名**（见类注释）。计数是"新增"而不是"改签名"。
        /// </para>
        /// </summary>
        public int SpawnedTotal { get; private set; }

        /// <summary>
        /// 被**丢弃**的播放请求数（帧目录为空 / 同屏数量达 <see cref="MaxLive"/>）—— 与 <see cref="SpawnedTotal"/>
        /// 配对使用：自检断言 = <c>SpawnedTotal + SkippedTotal == 总请求数</c>（不成立的帧就是静默失效）。
        /// </summary>
        public int SkippedTotal { get; private set; }

        /// <summary>当前正在播的特效节点数（自检用）。</summary>
        public int LiveCount { get { return _live.Count; } }

        /// <summary>一个在播的特效（池化对象，⛔ 不每次 `new GameObject`）。</summary>
        private sealed class Node
        {
            /// <summary>节点。</summary>
            public GameObject Go;

            /// <summary>渲染器（`sortingOrder = ArenaLayers.Instance.Effect`）。</summary>
            public SpriteRenderer Renderer;

            /// <summary>该用途目录的**全部**帧（`ResPaths.EffectDir(use)`）。</summary>
            public Sprite[] Frames;

            /// <summary>起始帧在 <see cref="Frames"/> 里的下标（= `firstFrame` 命中处）。</summary>
            public int Start;

            /// <summary>本次要播几帧。</summary>
            public int Count;

            /// <summary>当前帧（相对 <see cref="Start"/>）。</summary>
            public int FrameIndex;

            /// <summary>上一次**真正写进** `Renderer.sprite` 的帧下标（`-1` = 还没贴过）。</summary>
            public int Shown = -1;

            /// <summary>帧计时器（不足 1 = 未到下一帧）。</summary>
            public float Timer;

            /// <summary>飞行起点（格；定点播放时 == <see cref="To"/>）。</summary>
            public Vector2 From;

            /// <summary>飞行终点（格）。</summary>
            public Vector2 To;

            /// <summary>飞行时长（秒）；`0` = 定点不动。</summary>
            public float Duration;

            /// <summary>飞行进度 0..1。</summary>
            public float T;

            /// <summary>
            /// 定格时长（秒）：`&gt; 0` 时本节点**只显示第 0 帧**并持续这么久（由 <see cref="PlayHold"/> 用）。
            /// <para>
            /// 为什么需要它：原版有些特效的 timeline 是**同一个像素帧重复 N 次**（例：`deploy_arrows_effect`
            /// = 16 条 timeline 记录全指向 f119，60 fps ⇒ 自然时长 16/60 ≈ 0.267 s）。这类效果按
            /// "帧数 ÷ DefaultFps(14)" 播只有 1/14 s ≈ 71 ms，肉眼几乎看不到 ⇒ 必须按**它自己的时间轴长度**
            /// 定格显示，而不是靠把同一张图复制 16 份来凑帧数（复制会平白多出 16 个资源文件）。
            /// </para>
            /// </summary>
            public float Hold;
        }

        /// <summary>建出特效层节点（幂等）。</summary>
        public static EffectsView Create(Transform parent)
        {
            if (parent != null)
            {
                var existing = parent.GetComponentInChildren<EffectsView>(true);
                if (existing != null) return existing;
            }
            var go = new GameObject("Effects");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<EffectsView>();
        }

        /// <summary>在 <paramref name="world"/> 播一段帧序列。</summary>
        public void Play(Vector2 world, string use, int firstFrame, int frameCount, float size)
        {
            Spawn(world, world, use, firstFrame, frameCount, size, 0f, 0f);
        }

        /// <summary>
        /// 在 <paramref name="world"/> **定格**显示 <paramref name="firstFrame"/> 这一帧，持续
        /// <paramref name="holdSeconds"/> 秒（用途与理由见 <see cref="Node.Hold"/>）。
        /// </summary>
        /// <param name="holdSeconds">定格时长（秒）；<c>&lt;= 0</c> 时按普通单帧播放（= 1/DefaultFps）。</param>
        public void PlayHold(Vector2 world, string use, int firstFrame, float size, float holdSeconds)
        {
            Spawn(world, world, use, firstFrame, 1, size, 0f, holdSeconds);
        }

        /// <summary>从 <paramref name="from"/> 飞到 <paramref name="to"/> 的弹道。</summary>
        public void PlayFlight(Vector2 from, Vector2 to, string use, int firstFrame, int frameCount, float size)
        {
            Spawn(from, to, use, firstFrame, frameCount, size, FlightSeconds, 0f);
        }

        /// <summary>
        /// 从 <paramref name="from"/> 飞到 <paramref name="to"/> 的弹道，**显式指定飞行时长**（秒）。
        /// <para>
        /// 用途：调用方按真实速度算时长 —— 例如 `BattleViewRoot` 用 `CardInfo.proj_speed`（**格/分钟**）
        /// 推 <c>时长 = 距离(格) × 60 / proj_speed</c>，⛔ 不再落到 <see cref="FlightSeconds"/> 这个兜底常量。
        /// </para>
        /// <para>⛔ 不改上面那条签名（`EffectsView` 的对外契约不许改）—— 本方法只是另一个带时长的重载。</para>
        /// </summary>
        /// <param name="durationSeconds">飞行时长（秒）；<c>&lt;= 0</c> 时退回 <see cref="FlightSeconds"/>。</param>
        public void PlayFlight(Vector2 from, Vector2 to, string use, int firstFrame, int frameCount, float size, float durationSeconds)
        {
            Spawn(from, to, use, firstFrame, frameCount, size, durationSeconds > 0f ? durationSeconds : FlightSeconds, 0f);
        }

        private void Spawn(Vector2 from, Vector2 to, string use, int firstFrame, int frameCount, float size, float duration, float hold)
        {
            var frames = SpriteBank.LoadDir(ResPaths.EffectDir(use), SpriteBank.SpritePivotMode.UnifiedCanvasAnchor);
            if (frames.Length == 0) { SkippedTotal++; return; }

            if (_live.Count >= MaxLive)
            {
                // 「只报一次」走引擎的**进程级**去重闸门（`Runtime/Core/LogThrottle.cs`），
                // ⛔ 本类不自持去重字段 —— 引擎口径：「已有同类能力不准再起第二套去重」。
                // 生命周期：本处是"同屏特效上限"告警，与任何"每局重置"语义无关（进程级才对）。
                LogThrottle.WarnOnce(LogTag, "fx.live.cap",
                    $"同屏特效达到上限 {MaxLive} ⇒ 忽略后续（只报一次）");
                SkippedTotal++;
                return;
            }

            // 起始帧 = 原版帧号等于 firstFrame 的那帧；找不到就从目录头开始（并留痕）。
            var start = 0;
            var found = false;
            for (var i = 0; i < frames.Length; i++)
            {
                if (SpriteBank.ParseFrameIndex(frames[i].name) == firstFrame) { start = i; found = true; break; }
            }
            if (!found && firstFrame != 0)
                Game.Logger?.Warn(LogTag, $"目录 {use} 里找不到原版起始帧 {firstFrame} ⇒ 从目录头开始播（非预期，检查 ResPaths 的 first/count 与落地帧号是否一致）");

            var count = frameCount > 0 ? Mathf.Min(frameCount, frames.Length - start) : frames.Length - start;
            if (count <= 0) { SkippedTotal++; return; }

            var node = Rent();
            node.Frames = frames;
            node.Start = start;
            node.Count = count;
            node.FrameIndex = 0;
            node.Shown = 0;
            node.Timer = 0f;
            node.From = from;
            node.To = to;
            node.Duration = duration;
            node.T = 0f;
            node.Hold = hold;

            var scale = size > 0f ? size / WorldSize : 1f;
            node.Go.transform.position = new Vector3(from.x, from.y, 0f);
            node.Go.transform.localScale = new Vector3(scale, scale, 1f);
            node.Renderer.sprite = frames[start];
            node.Go.SetActive(true);
            _live.Add(node);
            SpawnedTotal++;
        }

        /// <summary>取一个空闲特效节点（池空则新建）。</summary>
        private Node Rent()
        {
            while (_idle.Count > 0)
            {
                var reused = _idle.Pop();
                // Unity 的"假 null"：节点随场景卸载被 Destroy 时这里会拿到 null，跳过继续找。
                if (reused != null && reused.Go != null) return reused;
            }
            var go = new GameObject("Fx");
            if (_root != null) go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            // 特效层：全场最高层（须在单位层 `ArenaLayers.Instance.Actor` = 1000 + 世界深度之上）。
            sr.sortingOrder = ArenaLayers.Instance.Effect;
            return new Node { Go = go, Renderer = sr };
        }

        /// <summary>归还到池（不 Destroy —— 下一个特效还会用）。</summary>
        private void Recycle(Node node)
        {
            if (node.Go != null)
            {
                node.Renderer.sprite = null; // 松开对帧精灵的引用（帧精灵本身在 SpriteBank 缓存里）
                node.Go.SetActive(false);
            }
            node.Frames = null;
            node.Hold = 0f;
            _idle.Push(node);
        }

        private void Awake()
        {
            _root = transform;
        }

        private void Update()
        {
            if (_live.Count == 0) return;
            var dt = Time.deltaTime;
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var node = _live[i];
                if (node == null || node.Go == null) { _live.RemoveAt(i); continue; }

                // 定格节点（`PlayHold`）：只显示第 0 帧，按真实时间倒数，到点即回收。
                // ⛔ 不走下面的"逐帧推进"（`Count == 1` 时那套只会让它 1/DefaultFps 秒就消失）。
                if (node.Hold > 0f)
                {
                    node.Hold -= dt;
                    if (node.Hold <= 0f) { _live.RemoveAt(i); Recycle(node); }
                    continue;
                }

                node.Timer += dt * DefaultFps;
                var finished = false;
                while (node.Timer >= 1f)
                {
                    node.Timer -= 1f;
                    node.FrameIndex++;
                    if (node.FrameIndex >= node.Count) { finished = true; break; }
                }
                if (finished)
                {
                    _live.RemoveAt(i);
                    Recycle(node);
                    continue;
                }

                // ⛔ 只在帧真的变了时写 `Renderer.sprite`（同 `UnitView.Update` 的理由）。
                if (node.FrameIndex != node.Shown)
                {
                    node.Shown = node.FrameIndex;
                    node.Renderer.sprite = node.Frames[node.Start + node.FrameIndex];
                }

                if (node.Duration > 0f)
                {
                    node.T += dt / node.Duration;
                    if (node.T > 1f) node.T = 1f;
                    var p = Vector2.Lerp(node.From, node.To, node.T);
                    node.Go.transform.position = new Vector3(p.x, p.y, 0f);
                }
            }
        }
    }
}
