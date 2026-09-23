using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 对局表现层的**根**：进图时建、出图时拆；订阅 `Events.Battle.*`；驱动
    /// <see cref="ArenaView"/> / <see cref="UnitView"/> / <see cref="PlacementIndicator"/>。
    ///
    /// <h4>一、为什么它自己把自己建出来（`RuntimeInitializeOnLoadMethod`）</h4>
    /// `Battle01` 场景里**只有一台正交相机**（见 `Assets/Editor/SceneBuilder.cs`「本片故意不往里放东西」），
    /// 场景本身不带任何表现层节点；而 `Assets/Editor/**` 不归本片改。若没人创建本组件，
    /// `Events.Battle.*` 就没有订阅者 —— 现象是"进对局黑屏、不是报错"，最难查。
    /// 因此：<see cref="AutoInstall"/> 在 `AfterSceneLoad` 时建一个 `DontDestroyOnLoad` 的**控制器**，
    /// 它只负责订阅与状态；真正的画面节点（<c>BattleContent</c>）建在**当前场景**里，
    /// 于是出图时随场景一起卸载，不会把竞技场留在主菜单上。
    ///
    /// <h4>二、坐标从哪来（★ 主 agent 要核对的两个接缝，这里写清）</h4>
    /// 契约允许两条路：① 由 `Module/Battle` 在事件里带插值后的坐标；② 由本类自己插值。
    /// **本片选 ②：`BattleViewRoot` 自己插值**。理由（可核对）：
    /// <list type="bullet">
    /// <item>`Core/Events.cs` 的 `Events.Battle.Snapshot` 参数**就是** `CR.Def.BattleSnapshot` 原件，
    ///   事件表里**不存在**"插值后坐标"的事件（已逐条读过 `Events.cs`：`Started/Snapshot/Events/Ended/PlayCardRequest/...`，
    ///   没有任何一条带插值坐标）⇒ 选 ① 就必须改 `Events.cs`（冻结）或让 07a 另发一条事件（会漂移）。</item>
    /// <item>插值只需要"上一帧快照 + 当前快照 + 到达时刻"，全部在收事件时就地可得，不需要 Module 的任何类型。</item>
    /// </list>
    /// <b>所以与 07a 的接缝是：07a 只要把 `PushBattleSnapshot` 原样 `Emit(Events.Battle.Snapshot, snapshot)` 即可，
    /// ⛔ 不需要（也不该）为表现层做插值。</b>
    ///
    /// <h4>三、插值：**渲染时钟按真实时间推进 + 窗口按时钟从快照历史里选**</h4>
    /// 服务端 10 Hz、渲染 ~30~60 FPS，做法（四步，全部在 <see cref="TickRender"/> /
    /// <see cref="RenderClockMs"/> / <see cref="SelectWindow"/>）：
    /// <list type="number">
    /// <item>渲染时钟 = <c>对齐点 + (Time.realtimeSinceStartup - 对齐时刻) × 1000</c> ——
    ///   **它只按真实时间前进，收到快照时绝不重置**。它渲染的是"服务端时间轴上的哪一毫秒"。
    ///   ⛔ 不写"每帧累加 `Time.deltaTime`"：那个值被 Unity 夹在 `Time.maximumDeltaTime`（默认 1/3 秒），
    ///   一次卡顿就让时钟**永久落后**（实测 1915~2108ms），于是 <c>t</c> 恒为 0、单位冻住不动。</item>
    /// <item>**速率恒为 1**（纯被动跟随）。只有灾难级偏差（&gt; <see cref="CatchUpThresholdMs"/> = 3 个间隔）
    ///   才用 <see cref="SteerRate"/> 做**有界**追帧。<b>为什么不再做稳态速率微调</b>：位置的导数就是速度，
    ///   而"改时钟速度"= 直接改单位速度 ⇒ 只要速率被调制，单位就在**忽快忽慢**（实测 ±15% 调制下
    ///   速度 cv 0.24~0.28）。用户对人眼感知最敏感的就是速度变化，所以校正绝不能走速率这条路。</item>
    /// <item>**按渲染时钟**从 4 格快照历史里选"夹住时钟的那一对"（<see cref="SelectWindow"/>），
    ///   渲染落后**最新快照 2 个间隔**（<see cref="RenderLagIntervals"/>）：
    ///   <list type="bullet">
    ///   <item>手里始终多握一格快照 ⇒ 到达时刻抖 ±半个间隔也**推不动**正在渲染的窗口；</item>
    ///   <item>换窗口的时刻由时钟跨过时间戳决定（不是包到达）⇒ 边界只在 <c>t=1</c> 那一刻跨过，
    ///     而上一段的 <c>t=1</c> 与下一段的 <c>t=0</c> 是**同一个坐标** ⇒ 位置连续、速度恒定。</item>
    ///   </list></item>
    /// <item>比例 <c>t = Clamp01((_renderMs - prev.server_ms) / max(1, cur.server_ms - prev.server_ms))</c>，
    ///   位置 <c>= Lerp(prevPos, curPos, t)</c>。分母是**两个快照自己的时间戳之差**（真实间隔），
    ///   不是编译期常量。时钟跑到最新快照之后 ⇒ <c>t</c> 夹到 1（冻在最后一个已知位置，⛔ 不外推）。</item>
    /// </list>
    /// <b>第一代旧写法错在哪（用户第一次报的"模型抖动的厉害"）</b>：分母写死 100 ms，且**每收到一帧快照
    /// 就把"当前帧到达时刻"重置** ⇒ `t` 每收一帧从 0 重来一次。快照间隔只要短于 100 ms，上一次已经插值到
    /// t=0.6 的位置就被整段丢弃、直接跳到新起点 ⇒ **每收一帧前跳一次**。
    /// <b>第二代旧写法错在哪（用户第二次报的"还是抖动"，本轮修复）</b>：时钟按真实时间走、分母也用真实间隔
    /// 这两条已经对了，但**换窗口还是绑在"包到达"上** —— 每收一帧就把 (<c>_prevIndex</c>, <c>_curIndex</c>)
    /// 换成最新的一对（`OnSnapshot` 里交换两个字典）。于是"换窗口那一刻"由**网络到达时刻**决定，
    /// 而时钟是按真实时间走的：换的瞬间时钟离窗口末端还差 0~1 帧，窗口末端那一小段位移就被**塞进换窗口的
    /// 那一帧**交付。<b>实测（`.ai-tmp/test/T3-flow-after.tsv`，同一台机器）</b>：<c>t</c> 最高只到 0.707
    /// （= 窗口从来没走完）、<c>RenderLagMs</c> 最大 119.5ms（**超过一个快照间隔**）、单帧速度在
    /// 0.782~1.049 之间跳（cv 0.24，峰值比 1.23）—— 人眼看到的就是 10 Hz 的"哆嗦"。
    /// 离线仿真把这条路钉死：速率微调关掉（速率恒 1）但保留"到达驱动换窗口"⇒ 速度 cv 反而升到 0.37~0.47；
    /// **改成按时钟选窗口 ⇒ 速度 cv 0.000、峰值比 1.000、零位移帧 0**（`tools/probes/jitter-sim.py`）。
    /// <b>为什么不直接把快照坐标贴到 Transform</b>（D4 明确禁止）：那等于每 100 ms 瞬移一次，
    /// 单位看起来是"一格一格跳"；而"显示服务端时间轴上稍早一点的位置"在联机对战里没有可感知代价
    ///（对手看到的是同样的落后量），却把 10 Hz 变成视觉上的连续运动。
    /// <b>代价（明知）</b>：画面比服务端晚 <see cref="RenderLagIntervals"/> 个间隔（本项目 200ms）。
    /// 这是"绝不前跳"+"绝不改速率"换来的代价，刻意如此：前跳会瞬间把单位推过头（甚至穿过墙）。
    ///
    /// <h4>四、塔的状态与部署合法性</h4>
    /// 塔不在快照的 `entities` 里（`Def/ProtoDef.cs:143` 注释），由 `towers_a` / `towers_b` 报告；
    /// 6 座塔的位置是固定几何（`GameConst`），所以塔由 <see cref="ArenaView"/> 摆、由这里转发状态。
    /// 部署合法性的**规则**在 <see cref="PlacementIndicator.IsLegalDeploy"/>（同一套 `GameConst` 几何），
    /// 这里只负责把"哪几座塔还活着"填进去。
    /// </summary>
    public sealed class BattleViewRoot : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "BattleViewRoot";

        /// <summary>同屏实体上限（超出只 Warn 一次并忽略新增，⛔ 不做"无上限增长"这种只有上线才炸的设计）。</summary>
        public const int MaxEntities = 512;

        // ── 离散事件 kind：逐字对应 `Def/ProtoDef.cs:194`（`BattleEvent.kind` 的注释）
        //    与服务端 `server/game/core/snapshot.go` 的 `Ev*` 常量（同一套编号）。──

        /// <summary>`kind == 0`：出牌（`EvPlayCard`）。</summary>
        private const int EventKindPlayCard = 0;

        /// <summary>`kind == 2`：死亡（`EvDeath`）。</summary>
        private const int EventKindDeath = 2;

        /// <summary>`kind == 3`：塔毁（`EvTowerDestroyed`）。</summary>
        private const int EventKindTowerDestroyed = 3;

        /// <summary>部署期（`deploy_ms &gt; 0`）的透明度 —— 原版未激活单位是半透明的。</summary>
        public const float DeployAlpha = 0.55f;

        private static BattleViewRoot _instance;

        /// <summary>当前实例（可能为 null：还没 Install）。</summary>
        public static BattleViewRoot Instance { get { return _instance; } }

        private bool _subscribed;
        private bool _built;
        private bool _overCapWarned;

        /// <summary>
        /// 当前是否**处在对局站点**（由 `Events.Flow.StationChanged` 维护）。
        ///
        /// <para>
        /// <b>为什么必须记住这个（D12 残留缺陷的根因，AI2 实测）</b>：`Teardown()` 之后
        /// `_built == false`，而 `OnBattleStarted` / `OnSnapshot` / `OnBattleEventNotify` 里各有一条
        /// "兜底重建"`if (!_built) Build();`（本意是"站点切换事件没到也要能进图"）。回主菜单时
        /// 服务端仍在对推快照（暂停/回菜单都不结束对局），这些**尾随推送**于是把 `BattleContent`
        /// **在 Main 场景里重建了一份**：AI2 实测"回主菜单后画面无可见残留（被主菜单面板盖住），
        /// 但对象树里留着 1 个激活的 `UnitView(chr_pekka_out)` + `EffectsView` + `BattleAudio`"。
        /// </para>
        ///
        /// <para>
        /// <b>口径</b>：初值 <c>true</c> = "还没被告知离开对局" ⇒ 首次进图的兜底重建照旧生效
        ///（`StationChanged` 没到时仍能出画面）；一旦收到**非 Battle** 的站点切换就置 false，
        /// 此后的尾随推送一律**忽略**（只 Warn 一次）。再收到 Battle ⇒ 重新置 true。
        /// </para>
        /// </summary>
        private bool _inBattle = true;

        /// <summary>离开对局站点后仍收到对局推送：只 Warn 一次（⛔ 不刷屏）。</summary>
        private bool _staleAfterLeaveWarned;
        private bool _hasPrev;
        private int _myTeam;
        private int _snapshotCount;

        private GameObject _content;
        private Transform _unitRoot;
        private ArenaView _arena;
        private PlacementIndicator _indicator;
        private EffectsView _effects;
        private BattleAudioView _audio;   // 对局音效（唯一挂点：见 Build；订阅 Core/Events.cs 的战斗事件）
        private Camera _cam;

        /// <summary>
        /// 卡池索引（`card_id → CardInfo`，来自 `Events.Deck.PoolLoaded`）。
        /// 判「这张卡是否远程」（`CardInfo.projectile_key`）与取弹道速度（`proj_speed`）都靠它 ——
        /// 客户端不落地卡 / 单位配表，这两个字段只在卡池协议里（`Def/ProtoDef.cs` 的 `CardInfo`）。
        /// </summary>
        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();

        /// <summary>出牌事件的卡不在卡池里：只 Warn 一次（⛔ 不刷屏）。</summary>
        private bool _cardMissingWarned;

        /// <summary>远程卡缺 `proj_speed`（&lt;=0）：只 Warn 一次。</summary>
        private bool _projSpeedMissingWarned;

        /// <summary>本局已播的弹道条数（自检 / 验收用的日志计数）。</summary>
        private int _projectileShots;

        /// <summary>
        /// **插值窗口（= 被渲染的那一对快照）**的起始索引。由 <see cref="SelectWindow"/> **按渲染时钟**
        /// 从 <see cref="_idxHist"/> 里选出，⛔ 不再"每收到一帧就换一对"（那是"还是抖动"的根因，见类注释三）。
        /// </summary>
        private Dictionary<int, EntitySnapshot> _prevIndex = new Dictionary<int, EntitySnapshot>();

        /// <summary>插值窗口的结束索引（与 <see cref="_prevIndex"/> 同属 <see cref="_idxHist"/> 的两格）。</summary>
        private Dictionary<int, EntitySnapshot> _curIndex = new Dictionary<int, EntitySnapshot>();

        /// <summary>
        /// 快照历史槽数（最旧 … 最新）。取 4 = "渲染落后 2 个间隔" + 2 格冗余 ⇒ 到达时刻抖动
        /// 小半个间隔也**换不掉**正在渲染的那一对（窗口只在时钟跨过时间戳时才动）。
        /// </summary>
        private const int HistSlots = 4;

        /// <summary>各历史槽的服务端时间戳（毫秒），下标 0 最旧、<c>HistSlots-1</c> 最新。</summary>
        private readonly float[] _msHist = new float[HistSlots];

        /// <summary>各历史槽的实体索引（<c>id → EntitySnapshot</c>），与 <see cref="_msHist"/> 一一对应。</summary>
        private readonly Dictionary<int, EntitySnapshot>[] _idxHist =
        {
            new Dictionary<int, EntitySnapshot>(),
            new Dictionary<int, EntitySnapshot>(),
            new Dictionary<int, EntitySnapshot>(),
            new Dictionary<int, EntitySnapshot>()
        };

        /// <summary>已填充的历史槽数（≤ <see cref="HistSlots"/>；首帧为 1）。</summary>
        private int _histLen;

        // ── 插值的时间轴：全部以**服务端时间戳**（`BattleSnapshot.server_ms`）为单位 ──

        /// <summary>
        /// **被渲染的那一对快照**的起始时间戳（毫秒）。由 <see cref="SelectWindow"/> 按渲染时钟选出，
        /// ⛔ 不再等于"上一次收到的快照"（见类注释三：到达驱动的换窗口就是抖动的根因）。
        /// </summary>
        private float _prevMs;

        /// <summary>**被渲染的那一对快照**的结束时间戳（毫秒）。</summary>
        private float _currMs;

        /// <summary>
        /// **最新收到的快照**的服务端时间戳（毫秒）。与 <see cref="_prevMs"/><see cref="_currMs"/>"被渲染的一对"
        /// 区分开：渲染落后它是 1~2 个间隔（= 抖动缓冲）。`ServerNowMs` 由它外推。
        /// </summary>
        private float _newestMs;

        /// <summary>
        /// 渲染时钟（毫秒）：**它渲染的是"服务端时间轴上的哪一毫秒"**。
        /// 由"对齐点 + 真实时间 × 速率"算出（见 <see cref="RenderClockMs"/>），
        /// **快照到达时绝不重置**（旧写法"每收到一帧就把到达时刻重置"正是"每收一帧前跳一次"的根因）。
        /// `NaN` = 还没对齐过（首个快照到达时对齐一次）。
        /// </summary>
        private float _renderMs = float.NaN;

        /// <summary>时钟对齐点：该真实时刻（<see cref="_clockBaseReal"/>）对应的服务端时间轴毫秒值。</summary>
        private float _clockBaseMs;

        /// <summary>时钟对齐点：`Time.realtimeSinceStartup`（秒）。</summary>
        private float _clockBaseReal;

        /// <summary>
        /// **当前快照的到达时刻**（`Time.realtimeSinceStartup`，秒）。
        /// 用途：把阶梯状的 `_currMs` 外推成**连续的**"服务端现在"（见 <see cref="ServerNowMs"/>）。
        /// </summary>
        private float _arrivalReal;

        /// <summary>
        /// 时钟相对真实时间的**速率**（常态 = 1）。
        /// 只在"落后太多"时被临时抬高（≤ <see cref="CatchUpMaxRate"/>）做**平滑追帧**。
        /// </summary>
        private float _clockRate = 1f;

        /// <summary>
        /// 速率控制律（**纯函数**，便于离线断言 —— 见 `tools/probes/FlowProbe.SimulateController`）：
        /// 输入"时钟相对目标的偏差"（<c>目标值 - 时钟现值</c>，正 = 时钟落后了要加速），输出时钟该跑多快。
        /// <list type="bullet">
        /// <item>偏差在一个快照间隔以内：±<see cref="LagSteerMaxRate"/> 之内线性微调（人眼无感）；</item>
        /// <item>偏差超过 <see cref="CatchUpThresholdMs"/>：允许更大速率（落后太多时追赶、超前时放慢）。</item>
        /// </list>
        /// </summary>
        public static float SteerRate(float lagErrorMs)
        {
            if (Mathf.Abs(lagErrorMs) > CatchUpThresholdMs)
                return lagErrorMs > 0f ? 1f + CatchUpMaxRate : 1f - CatchUpMaxRate * 0.5f;
            return Mathf.Clamp(1f + lagErrorMs / Mathf.Max(1f, TargetLagMs) * LagSteerGain,
                1f - LagSteerMaxRate, 1f + LagSteerMaxRate);
        }

        /// <summary>是否正在做灾难级追赶（只用来"进入/退出各报一次"，⛔ 不刷屏）。</summary>
        private bool _catchingUp;

        /// <summary>
        /// 追帧阈值：落后服务端超过这么多毫秒就启动"时钟快走"。
        /// 取 3 个快照间隔（300ms）：正常波动（网络抖动 + 一帧渲染时间）远达不到，
        /// 而一旦达到就说明**已经或即将永久冻结**（见 <see cref="CatchUpMaxRate"/> 的注释）。
        /// </summary>
        public const float CatchUpThresholdMs = 3f * GameConst.SnapshotIntervalMs;

        /// <summary>
        /// **渲染落后"最新快照"几个快照间隔**（= 抖动缓冲深度）。
        /// <para>
        /// 取 2（不是 1）：渲染的那一对快照是 <c>[s(k-2), s(k-1)]</c>、而手里已经握着 <c>s(k)</c>，
        /// 于是"渲染窗口的末端"离最新快照还有**整整一个间隔**的余量 —— 到达时刻抖 ±半个间隔
        /// 也推不动正在渲染的窗口。取 1 时窗口末端就是最新快照 ⇒ 每收一帧就换一对，
        /// 换的那一刻时钟离窗口末端还有 0~1 帧 ⇒ 那一帧要交付"剩下的任意份额"（0~100% 的间隔位移）
        /// ⇒ 实测速度 cv 0.24~0.28、峰值达均值 2.4 倍（= 用户看到的"抖动"）。
        /// </para>
        /// <para>
        /// 代价（明知）：画面比服务端**晚两个间隔**（本项目 200ms）。联机对战里对手看到的是同样的
        /// 落后量，而换来的是一条**严格匀速**的位置曲线（离线仿真：速度 cv 0.000、峰值比 1.000）。
        /// </para>
        /// </summary>
        public const float RenderLagIntervals = 2f;

        /// <summary>
        /// 渲染时钟相对 <see cref="ServerNowMs"/> 的目标落后量 = <see cref="RenderLagIntervals"/> 个快照间隔。
        /// 用途只剩"灾难级偏差的恢复判据"；稳态下**不参与速率控制**（见 <see cref="TickRender"/>）。
        /// </summary>
        public const float TargetLagMs = RenderLagIntervals * GameConst.SnapshotIntervalMs;

        /// <summary>
        /// 稳态速率控制的增益：<c>rate = 1 + (落后量 - 目标) / 目标 × Gain</c>。
        /// 取 0.5 = "偏差一个目标量时把速度改一半"，收敛快且不过冲振荡。
        /// </summary>
        public const float LagSteerGain = 0.5f;

        /// <summary>
        /// 稳态速率允许的偏离（±15%）。人眼对"整体快/慢 15%"几乎没有感觉，但足以把时钟拉回目标
        /// ⇒ 用**渐变**代替"瞬跳"（瞬跳正是用户报的"抖"）。
        /// </summary>
        public const float LagSteerMaxRate = 0.15f;

        /// <summary>
        /// 追帧时时钟最多跑多快（1 = 最高 2× 真实时间）。
        /// <para>
        /// <b>为什么必须有追帧（这一条是"实测逼出来"的，不是设计洁癖）</b>：渲染时钟**只能停在**
        /// 当前快照的时间戳上（<see cref="TickRender"/> 的夹取），而真实帧时间会被 Unity 夹在
        /// `Time.maximumDeltaTime`（默认 1/3 秒）—— 实测数据里能看到 `dtMs=333.33` 这种被夹过的帧。
        /// 一次 2 秒的卡顿只会让"累加式"时钟前进 333ms，于是它**永久落后**服务端 2000ms；
        /// 此时 `t = (_renderMs - _prevMs) / span` 恒为 0 ⇒ **单位冻在原地不再前进**
        ///（实测：改动后的第一次采集 `lagMs` 1915~2108ms、`t` 恒为 0.000 整整 16 秒）。
        /// 结论：只夹取、不追帧 = 卡顿一次就永久冻住；所以必须有一条**有界、渐变**的追赶路径
        ///（渐变而不是瞬跳 —— 瞬跳正是用户报的"抖"）。
        /// </para>
        /// </summary>
        public const float CatchUpMaxRate = 1f;

        /// <summary>时间戳非预期（缺失 / 不递增）只报一次，⛔ 不刷屏。</summary>
        private bool _timeWarned;

        // 部署合法性用：敌我公主塔存活（顺序 = 左、右）
        private bool _enemyLeftAlive = true;
        private bool _enemyRightAlive = true;
        private bool _ownLeftAlive = true;
        private bool _ownRightAlive = true;

        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private readonly List<int> _recycle = new List<int>();
        private static readonly HashSet<int> UnknownCards = new HashSet<int>();

        /// <summary>我方队伍（0=BLUE 1=RED）—— `BattleStartNotify.my_team`。</summary>
        public int MyTeam { get { return _myTeam; } }

        /// <summary>画面根是否已建（出图后为 false）。</summary>
        public bool Ready { get { return _built; } }

        /// <summary>竞技场视图（塔的状态在它内部）。</summary>
        public ArenaView Arena { get { return _arena; } }

        /// <summary>落点指示器（HUD 拖放时用）。</summary>
        public PlacementIndicator Indicator { get { return _indicator; } }

        /// <summary>已收到的快照数（自检用）。</summary>
        public int SnapshotCount { get { return _snapshotCount; } }

        /// <summary>当前活着的单位视图数（自检用）。</summary>
        public int LiveUnitCount { get { return _units.Count; } }

        /// <summary>
        /// 渲染落后**正在渲染的那一对快照的末端**多少毫秒（`_currMs - _renderMs`，自检用）。
        /// <para>
        /// 判据：正常运行时应在 **[0, 一个快照间隔]** 之内波动（本项目 100 ms），
        /// 且**永远 &gt;= 0**（负值 = 渲染跑到了服务端前面 = 外推，本类刻意不允许）。
        /// 修好"到达驱动换窗口"之前，这个量实测 29.3..119.5（**超过一个间隔**）且 <c>t</c> 最高只到
        /// 0.707（= 窗口没走完就换掉了）—— 那正是速度脉动。按时钟选窗口后 <c>t</c> 走满 0..1、
        /// 本量恒在 [0, 一个间隔]。
        /// </para>
        /// </summary>
        public float RenderLagMs { get { return _currMs - _renderMs; } }

        /// <summary>
        /// 渲染落后的**总**落后量 = 最新快照的时间戳 - 渲染时钟（自检用）。
        /// 稳态应落在 <c>[一个间隔, RenderLagIntervals 个间隔]</c>（本项目 100~200 ms）——
        /// 这多出来的那一个间隔就是**抖动缓冲**：它换掉了"到达时刻一抖就把窗口推走"。
        /// </summary>
        public float BufferLagMs { get { return _newestMs - _renderMs; } }

        /// <summary>**正在渲染的那一对**快照的起始时间戳（毫秒，自检用；0 = 还没有一对）。</summary>
        public float WindowStartMs { get { return _prevMs; } }

        /// <summary>**正在渲染的那一对**快照的结束时间戳（毫秒，自检用；两者之差应 ≡ 服务端间隔）。</summary>
        public float WindowEndMs { get { return _currMs; } }

        /// <summary>最新收到的快照的时间戳（毫秒，自检用）。</summary>
        public float NewestSnapshotMs { get { return _newestMs; } }

        /// <summary>渲染时钟当前速率（自检用）：常态 <c>1.0</c>，追帧中 &gt; 1.0（≤ <c>1 + CatchUpMaxRate</c>）。</summary>
        public float ClockRate { get { return _clockRate; } }

        /// <summary>是否正在追帧（自检用）。</summary>
        public bool CatchingUp { get { return _catchingUp; } }

        /// <summary>当前插值比例 t（自检用；`NaN` = 还没到首帧）。</summary>
        public float InterpRatio
        {
            get
            {
                if (_snapshotCount == 0 || float.IsNaN(_renderMs)) return float.NaN;
                var span = Mathf.Max(1f, _currMs - _prevMs);
                return _hasPrev ? Mathf.Clamp01((_renderMs - _prevMs) / span) : 1f;
            }
        }

        // ───────────────────────────── 建 / 拆 ─────────────────────────────

        /// <summary>
        /// 自安装：**进对局前**把控制器建好（`Battle01` 场景里没有任何本片的节点，见类注释一）。
        /// 用 `AfterSceneLoad` 而不是 `SubsystemRegistration`：本方法只建一个空壳控制器，
        /// 真正订阅要等 `Game.Event` 可用（`Game.Launch` 之后），那一步在 `Update` 里轮询完成。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            Ensure();
        }

        /// <summary>幂等创建控制器（供 `Bootstrap` / 面板等外部显式调用；重复调用返回同一实例）。</summary>
        public static BattleViewRoot Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("BattleViewRoot");
            DontDestroyOnLoad(go);
            return go.AddComponent<BattleViewRoot>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Game.Logger?.Warn(LogTag, "已存在 BattleViewRoot 实例，销毁重复的那个");
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            Unsubscribe();
            Teardown();
        }

        private void Update()
        {
            // 时序：本组件可能早于 `Game.Launch` 存在（那时 `Game.Event` 为 null），所以轮询到可订阅为止。
            if (!_subscribed)
            {
                if (Game.IsRunning && Game.Event != null) Subscribe();
            }

            // 画面被场景切换销毁了 ⇒ 先重建（见 Build 的注释）。放在这里是为了不依赖
            // 事件顺序：即使 `Flow.StationChanged` 没来 / 来晚了，下一帧也会自愈。
            // ⛔ 已离开对局站点时不再重建（D12 残留修复，见 `_inBattle` 的注释）。
            if (_built && _content == null && _inBattle) Build();

            // 还没有画面或还没收到过快照：无事可做（`t` 也无从算起）。
            if (!_built || _snapshotCount == 0) return;
            TickRender();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            var bus = Game.Event;
            if (bus == null) return;

            bus.On<BattleStartNotify>(Events.Battle.Started, OnBattleStarted);
            bus.On<BattleSnapshot>(Events.Battle.Snapshot, OnSnapshot);
            bus.On<BattleEventNotify>(Events.Battle.Events, OnBattleEventNotify);
            bus.On<BattleEndNotify>(Events.Battle.Ended, OnBattleEnded);
            bus.On<string>(Events.Flow.StationChanged, OnStationChanged);
            // 卡池：`CardInfo.projectile_key` / `proj_speed`（判远程 + 弹道时长）的唯一来源。
            // `Module/Deck` 只在进主菜单时发一次，`BattleManager` 会在进对局时按同一事件补发（见其 ReplayCardPoolForHud）。
            bus.On<CardInfo[]>(Events.Deck.PoolLoaded, OnCardPool);

            _subscribed = true;
            // 日志里刻意不写事件名字面量（事件名只有 `Core/Events.cs` 一处定义；写进日志会污染对「裸事件名」的静态检查）。
            Game.Logger?.Info(LogTag, "已订阅：对局四类事件（开始/快照/离散事件/结束）+ 站点切换 + 卡池");
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var bus = Game.Event;
            if (bus != null)
            {
                bus.Off<BattleStartNotify>(Events.Battle.Started, OnBattleStarted);
                bus.Off<BattleSnapshot>(Events.Battle.Snapshot, OnSnapshot);
                bus.Off<BattleEventNotify>(Events.Battle.Events, OnBattleEventNotify);
                bus.Off<BattleEndNotify>(Events.Battle.Ended, OnBattleEnded);
                bus.Off<string>(Events.Flow.StationChanged, OnStationChanged);
                bus.Off<CardInfo[]>(Events.Deck.PoolLoaded, OnCardPool);
            }
            _subscribed = false;
        }

        /// <summary>建出画面（幂等）。</summary>
        public void Build()
        {
            // ★★ 场景切换会把上一份 `BattleContent` 一起卸掉（它是**当前场景**的对象，见下面建它的那行注释），
            //    而 `Events.Battle.Started` 是在 `Scene.Load(Battle01)` **之前**就发出的
            //    ⇒ 第一份画面建在 **Main** 里、随 Main 卸载而消失，但 `_built` 仍是 true、
            //    `OnStationChanged(Battle)` 里的 `Build()` 又被 `_built` 挡回去 ⇒ **整局空场**。
            //    实测症状（本轮踩到，有截图）：HUD 全对（冠数 / 计时 / 圣水 / 手牌），
            //    但竞技场与 6 座塔一个都不显示；单位因父节点被销毁而掉成场景根节点；
            //    而且**一条报错都没有**。判据用 Unity 的假 null（销毁后 `_content == null` 成立）。
            if (_built && _content == null)
            {
                Game.Logger?.Warn(LogTag,
                    "对局画面已被场景切换销毁（BattleContent 随旧场景卸载）——重建它；" +
                    "这条 Warn 出现即说明 Build() 抢在场景加载之前跑过");
                _arena = null;
                _indicator = null;
                _unitRoot = null;
                _cam = null;
                _units.Clear();
                _recycle.Clear();
                _built = false;
            }

            if (_built) return;

            _content = new GameObject("BattleContent"); // ⛔ 不 DontDestroyOnLoad：出图要随场景卸载
            _arena = ArenaView.Create(_content.transform);

            var units = new GameObject("Units");
            units.transform.SetParent(_content.transform, false);
            _unitRoot = units.transform;

            _indicator = PlacementIndicator.Create(_content.transform);
            _effects = EffectsView.Create(_content.transform); // 特效层（原版帧序列，见 ResPaths 特效区段）
            // 音效层（⛔ **唯一挂点**）：挂在 BattleContent 下 ⇒ 生命周期跟着对局画面走
            //（出图时随场景卸载，OnDestroy 自动退订 Core/Events.cs 的战斗事件）。与 EffectsView 同一范式。
            _audio = BattleAudioView.Create(_content.transform);

            _cam = Camera.main;
            if (_cam == null)
            {
                // 场景里本来就有一台正交相机；真丢了就补一台，否则画面根本不渲染。
                var camGo = new GameObject("Battle Camera");
                camGo.tag = "MainCamera";
                _cam = camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.043f, 0.063f, 0.106f, 1f); // 与 SceneBuilder 的清屏色一致
                Game.Logger?.Warn(LogTag, "场景里没有 MainCamera ⇒ 已补建一台（否则黑屏，会被误判成逻辑没跑）");
            }
            ArenaView.SetupCamera(_cam);

            _hasPrev = false;
            _snapshotCount = 0;
            _overCapWarned = false;
            _projectileShots = 0;
            // 时间轴归零：`_renderMs = NaN` ⇒ 新的一局会在首个快照上重新对齐一次服务端时间戳。
            _prevMs = 0f;
            _currMs = 0f;
            _newestMs = 0f;
            _histLen = 0;
            for (var i = 0; i < HistSlots; i++)
            {
                _msHist[i] = 0f;
                _idxHist[i].Clear();
            }
            _renderMs = float.NaN;
            _clockBaseMs = 0f;
            _clockBaseReal = Time.realtimeSinceStartup;
            _clockRate = 1f;
            _arrivalReal = Time.realtimeSinceStartup;
            _catchingUp = false;
            _timeWarned = false;
            _built = true;
            Game.Logger?.Info(LogTag,
                $"对局表现层已建：塔={_arena.TowerCount} 相机={_cam.name} 正交半高={_cam.orthographicSize:F2}（18×32 格竖屏）");
        }

        /// <summary>拆掉画面（幂等）。</summary>
        public void Teardown()
        {
            foreach (var kv in _units)
                if (kv.Value != null) kv.Value.Release();
            _units.Clear();
            _recycle.Clear();
            UnitView.ClearPools();
            SpriteBank.ClearCache();

            if (_content != null)
            {
                Destroy(_content);
                _content = null;
            }

            _arena = null;
            _indicator = null;
            _effects = null;
            _audio = null;
            _unitRoot = null;
            _cam = null;
            for (var i = 0; i < HistSlots; i++) _idxHist[i].Clear();
            _histLen = 0;
            _hasPrev = false;
            _built = false;
            if (_subscribed) Game.Logger?.Info(LogTag, "对局表现层已拆（出图）");
        }

        // ───────────────────────────── 事件 ─────────────────────────────

        private void OnStationChanged(string station)
        {
            if (station == Stations.Battle)
            {
                _inBattle = true;
                Build();
                return;
            }

            // 离开对局（含回主菜单）⇒ 拆画面，并**记住"已不在对局站点"**：
            // 服务端尾随推送（回菜单后仍在推快照）从此不再把 BattleContent 重建回 Main 场景。
            // ⛔ 不在这里退订阅：Battle 站点可能再次进入。
            var wasInBattle = _inBattle;
            _inBattle = false;
            if (_built)
            {
                Teardown();
                Game.Logger?.Info(LogTag,
                    $"离开对局站点（{station}）⇒ 对局表现层已拆；此后对局推送一律忽略（不再重建 BattleContent，" +
                    "修复「回主菜单后残留 UnitView/Effects/BattleAudio」）");
            }
            else if (wasInBattle)
            {
                // 没建过画面也要置位（例如进图失败直接回菜单）：否则下一条尾随推送会把它建出来。
                Game.Logger?.Info(LogTag, $"离开对局站点（{station}）：画面本就未建，只标记「不在对局」");
            }
            _staleAfterLeaveWarned = false;
        }

        /// <summary>
        /// 对局事件里的"兜底重建"：**只在还处于对局站点时**建画面。
        /// <para>
        /// 原写法是无条件 `if (!_built) Build();`，正是 D12 残留的根因（见 `_inBattle` 的注释）。
        /// 离开站点后的尾随推送在这里被拦住并**留痕一次**（⛔ 不静默丢弃、⛔ 不刷屏）。
        /// </para>
        /// </summary>
        private void RebuildIfWanted(string source)
        {
            if (_built) return;
            if (_inBattle)
            {
                Build();
                return;
            }
            if (_staleAfterLeaveWarned) return;
            _staleAfterLeaveWarned = true;
            Game.Logger?.Warn(LogTag,
                $"已离开对局站点后仍收到 {source} ⇒ 忽略（不重建 BattleContent / 不建单位视图）；" +
                "这是修复「回主菜单后残留对局对象」的预期分支，只报一次");
        }

        private void OnBattleStarted(BattleStartNotify n)
        {
            if (n == null) return;
            _myTeam = n.my_team;
            if (!_built) RebuildIfWanted("Events.Battle.Started"); // 兜底：万一 StationChanged 没到（例如直接由服务端推送进对局）
            var regulation = n.timeline != null ? n.timeline.regulation_ms : 0;
            Game.Logger?.Info(LogTag,
                $"对局开始：my_team={_myTeam}({(_myTeam == 0 ? "BLUE" : "RED")}) 常规={regulation}ms " +
                $"我的手牌={HandToString(_myTeam == 0 ? n.hand_a : n.hand_b)} 下一张={(int)(_myTeam == 0 ? n.next_a : n.next_b)}");
        }

        private void OnSnapshot(BattleSnapshot s)
        {
            if (s == null) return;
            if (!_built) RebuildIfWanted("Events.Battle.Snapshot");

            var first = _snapshotCount == 0;
            var serverMs = s.server_ms;
            if (serverMs <= 0)
            {
                // 非预期分支（协议字段缺失/为 0）：退化时间轴 = 按标称周期推定，并**留痕一次**。
                // 退化的代价是节奏退化为固定 100ms，但画面依然不跳（时钟照旧只按真实时间走）。
                if (!_timeWarned)
                {
                    _timeWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"快照 server_ms={serverMs}（<=0，字段缺失或未填）⇒ 插值时间轴退化为按标称周期 " +
                        $"{GameConst.SnapshotIntervalMs}ms 推定；请检查服务端是否在快照里填了 server_ms（只报一次）");
                }
                serverMs = Mathf.RoundToInt(_newestMs) + GameConst.SnapshotIntervalMs;
            }
            else if (!first && serverMs <= _newestMs)
            {
                // 非预期分支（乱序 / 服务端时间没走）：**整帧丢弃**（⛔ 不入历史），只留痕一次。
                // 为什么不入历史：历史必须**按时间戳单调**，否则 SelectWindow 的"找夹住时钟的一对"会选错。
                if (!_timeWarned)
                {
                    _timeWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"快照时间戳没有前进（最新={_newestMs:F0} 本帧={serverMs}）⇒ 丢弃本帧（⛔ 不入插值历史）；" +
                        "若持续出现说明服务端 server_ms 不是单调递增，或快照乱序（只报一次）");
                }
                return;
            }

            // ── 入历史（窗口由一个 4 格环组成；**到达只入队，绝不改正在渲染的那一对**） ──
            for (var i = 0; i < HistSlots - 1; i++)
            {
                var tmp = _idxHist[i];
                _idxHist[i] = _idxHist[i + 1];
                _idxHist[i + 1] = tmp;
                _msHist[i] = _msHist[i + 1];
            }
            var slot = _idxHist[HistSlots - 1];
            slot.Clear();
            var list = s.entities;
            if (list != null)
            {
                if (list.Length > MaxEntities && !_overCapWarned)
                {
                    _overCapWarned = true;
                    Game.Logger?.Warn(LogTag, $"快照实体数 {list.Length} 超过上限 {MaxEntities} ⇒ 超出部分不渲染（只告警一次）");
                }
                var count = Mathf.Min(list.Length, MaxEntities);
                for (var i = 0; i < count; i++)
                {
                    var e = list[i];
                    if (e == null) continue;
                    slot[e.id] = e;
                }
            }
            _msHist[HistSlots - 1] = serverMs;
            if (_histLen < HistSlots) _histLen++;
            _snapshotCount++;

            // ── 服务端时间戳 → 时间轴（★ 快照到达只做这一件事，⛔ 绝不碰 `_renderMs`） ──
            _newestMs = serverMs;
            // 记下这一帧快照的**到达时刻**：`ServerNowMs` 靠它把阶梯状的时间戳外推成连续时间轴。
            _arrivalReal = Time.realtimeSinceStartup;
            if (first)
            {
                // 渲染时钟**只在首帧对齐一次**：对齐到"服务端现在 - 目标落后量"，
                // 于是从第一帧起就落在 **2 个间隔前**那一对快照之间，不会先贴到最新再被推回去。
                _clockBaseMs = _newestMs - TargetLagMs;
                _clockBaseReal = Time.realtimeSinceStartup;
                _clockRate = 1f;
                _catchingUp = false;
                _renderMs = _clockBaseMs;
                Game.Logger?.Info(LogTag,
                    $"渲染时钟已对齐：server_ms={_newestMs:F0} - 目标落后 {TargetLagMs:F0}ms（{RenderLagIntervals:F0}×{GameConst.SnapshotIntervalMs}ms，" +
                    $"= 抖动缓冲）= 时钟起于 {_clockBaseMs:F0} seq={s.seq}（只在首帧对齐一次；此后快照到达只入历史，⛔ 不重置时钟、⛔ 不换正在渲染的窗口）");
            }

            ApplyTowers(s);
        }

        private void OnBattleEventNotify(BattleEventNotify n)
        {
            if (n == null || n.events == null) return;
            if (!_built) RebuildIfWanted("Events.Battle.Events"); // 兜底：事件可能早于快照 / 站点切换到达
            for (var i = 0; i < n.events.Length; i++)
            {
                var e = n.events[i];
                if (e == null) continue;
                // 表现层只消费"离散事件里必须有的一次性表现"。**不在这里生成单位** ——
                // 单位的真假一律以快照的 entities 为准（事件与快照都会到，双份生成会画出两个兵）。
                Game.Logger?.Info(LogTag,
                    $"对局事件 kind={e.kind} team={e.team} card={e.card_id} ent={e.entity_id} " +
                    $"pos=({e.x_milli / 1000f:F2},{e.y_milli / 1000f:F2}){(string.IsNullOrEmpty(e.text) ? "" : " text=" + e.text)}");

                // ── 一次性特效接线（原版帧序列，见 `ResPaths` 的特效区段）──
                // 位置一律取**事件自带**的 `x_milli/y_milli`：服务端在 `EvDeath`（`combat.go:428-431`）
                // 与 `EvTowerDestroyed`（`combat.go:423-426`）里都填了**实体自己的位置** ⇒ 客户端无须另算。
                if (_effects == null) continue;
                switch (e.kind)
                {
                    case EventKindDeath:
                        // 命中 / 受击闪光：在死亡位置播 `Hit` 类原版帧序列（f050..f056）。
                        _effects.Play(GameConst.MilliToWorld(e.x_milli, e.y_milli),
                            ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount, EffectsView.WorldSize);
                        break;

                    case EventKindTowerDestroyed:
                        // 塔毁爆炸：在塔位置播 `Blast` 类原版帧序列（f418..f427）。
                        _effects.Play(GameConst.MilliToWorld(e.x_milli, e.y_milli),
                            ResPaths.EffectBlast, ResPaths.EffectBlastFirst, ResPaths.EffectBlastCount, EffectsView.WorldSize);
                        break;

                    case EventKindPlayCard:
                        // 远程弹道：判定 = 该卡 `projectile_key` 非空（服务端随卡池下发，见 `Def/ProtoDef.cs`）。
                        PlayProjectileFlight(e);
                        break;
                }
            }
        }

        /// <summary>
        /// 卡池到达：建 `card_id → CardInfo` 索引。判「这张卡是否远程」（`projectile_key`）与取弹道速度
        /// （`proj_speed`）都靠它。`Module/Deck` 只在进主菜单时发一次，`BattleManager` 会在进对局时
        /// 按同一事件补发（见其 `ReplayCardPoolForHud`）⇒ 进对局时这里必被调到。
        /// </summary>
        private void OnCardPool(CardInfo[] cards)
        {
            if (cards == null) return;
            for (var i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                if (c == null) continue;
                _cards[c.id] = c;
            }
            Game.Logger?.Info(LogTag, $"卡池已索引：{_cards.Count} 张（远程判定 / 弹道速度由此查表）");
        }

        /// <summary>
        /// 出牌 ⇒ 若该卡为远程（`CardInfo.projectile_key` 非空），从**施法者位置**飞到**落点**播一条原版弹道。
        ///
        /// <para>
        /// <b>时长完全由 `proj_speed` 推出</b>（⛔ 不用兜底常量）：
        /// `proj_speed` 单位 = **格/分钟**，与官方 `cards_stats_projectile.json` 的 `speed` 同口径
        /// （出处 `策划/策划案/皇室战争参考规格.md` §4：`anchors.json` → `tiles_per_minute_per_speed_unit: 1`）
        /// ⇒ 格/秒 = `proj_speed / 60` ⇒ <c>时长(秒) = 距离(格) × 60 / proj_speed</c>。
        /// </para>
        ///
        /// <para>
        /// <b>⚠️「施法者位置」是降级值</b>：事件载荷（`BattleEvent`）只有单点 `x_milli/y_milli` + `team`，
        /// 且 `EvPlayCard` 的 `entity_id` 恒为 0（`server/game/core/battle.go` 填 `EvPlayCard` 时未填 `EntityID`）
        /// ⇒ **载荷里没有施法者**。故退化为「出牌方**本方国王塔中心**」（固定几何 `GameConst.KingTowerTileX/Y`，
        /// 出处 `anchors.json`）。**落点 = 事件自带的 `x_milli/y_milli`（真值，未降级）。**
        /// </para>
        /// </summary>
        private void PlayProjectileFlight(BattleEvent e)
        {
            if (_effects == null) return;

            CardInfo card;
            if (!_cards.TryGetValue(e.card_id, out card) || card == null)
            {
                if (!_cardMissingWarned)
                {
                    _cardMissingWarned = true;
                    // 非预期分支（卡池没到）：判不出是否远程 ⇒ 只能跳过，留痕一次。
                    Game.Logger?.Warn(LogTag,
                        $"出牌事件 card={e.card_id} 不在已收到的卡池里 ⇒ 判不出是否远程、跳过弹道。" +
                        "卡池来源 = Events.Deck.PoolLoaded（进对局时由 BattleManager 补发）（只报一次）");
                }
                return;
            }

            if (string.IsNullOrEmpty(card.projectile_key)) return; // 非远程（近战部队 / 法术）

            var landing = GameConst.MilliToWorld(e.x_milli, e.y_milli);

            // 远程但服务端没给出速度（`core.ProjectileOf` 在投射物行缺失时返回 0）⇒ 降级为落点命中闪光。
            if (card.proj_speed <= 0)
            {
                if (!_projSpeedMissingWarned)
                {
                    _projSpeedMissingWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"远程卡 card={e.card_id}（projectile_key={card.projectile_key}）的 proj_speed={card.proj_speed}（<=0）" +
                        "⇒ 算不出飞行时长，降级为落点播一次命中闪光（只报一次）");
                }
                _effects.Play(landing, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount, EffectsView.WorldSize);
                return;
            }

            var from = CasterWorld(e.team);
            var dist = Vector2.Distance(from, landing);
            var seconds = dist * 60f / card.proj_speed; // 格 ÷ (格/分钟 ÷ 60)
            if (seconds <= 0f)
            {
                // 距离为 0（落点正好压在国王塔上）：没有"飞行"可言 ⇒ 退化为落点命中闪光。
                _effects.Play(landing, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount, EffectsView.WorldSize);
                return;
            }

            _effects.PlayFlight(from, landing, ResPaths.EffectArrow, ResPaths.EffectArrowFirst, ResPaths.EffectArrowCount,
                EffectsView.WorldSize, seconds);
            _projectileShots++;
            Game.Logger?.Info(LogTag,
                $"弹道 card={e.card_id} key={card.projectile_key} proj_speed={card.proj_speed}格/分钟 " +
                $"施法者[本方国王塔·降级]=({from.x:F2},{from.y:F2}) 落点=({landing.x:F2},{landing.y:F2}) " +
                $"距离={dist:F2}格 飞行={seconds:F3}s（={dist:F2}×60/{card.proj_speed}） 本局累计={_projectileShots}");
        }

        /// <summary>
        /// 出牌方「施法者」世界位置 —— ⚠️ **降级值**（事件载荷不携带施法者，见 <see cref="PlayProjectileFlight"/>）：
        /// 退化为**出牌方本方国王塔中心**（固定几何 `GameConst.KingTowerTileX/Y`，出处 `anchors.json`）。
        /// </summary>
        private static Vector2 CasterWorld(int team)
        {
            return GameConst.TileToWorld(GameConst.KingTowerTileX,
                GameConst.MirrorTileYForTeam(GameConst.KingTowerTileY, team));
        }

        private void OnBattleEnded(BattleEndNotify r)
        {
            if (r == null) return;
            // 结算面板由 agent-08 负责；表现层**不拆画面**（结算要盖在最后一帧场面上）。
            Game.Logger?.Info(LogTag,
                $"对局结束：win={r.win} draw={r.draw} 冠={r.crowns_a}:{r.crowns_b} reason={r.reason} " +
                $"塔血率={r.hp_rate_a}/{r.hp_rate_b}（画面保留，拆解在离开 Battle 站点时）");
        }

        // ───────────────────────────── 每帧渲染 ─────────────────────────────

        /// <summary>
        /// 渲染时钟的当前值（毫秒，服务端时间轴口径）：
        /// <c>对齐点 + (真实时间 - 对齐时刻) × 1000 × 速率</c>。
        /// <para>
        /// ⛔ **不用"每帧累加 `Time.deltaTime`"的写法**：`Time.deltaTime` 被 Unity 夹在
        /// `Time.maximumDeltaTime`（默认 1/3 秒），一次 2 秒卡顿只推进 333 ms ⇒ 时钟**永久落后**
        ///（实测 `lagMs` 1915~2108 且 `t` 恒为 0 = 单位冻住）。用绝对真实时间算就与卡顿无关。
        /// </para>
        /// </summary>
        private float RenderClockMs
        {
            get { return _clockBaseMs + (Time.realtimeSinceStartup - _clockBaseReal) * 1000f * _clockRate; }
        }

        /// <summary>
        /// **连续**的"服务端现在"（毫秒）：把**最新**快照的时间戳按"它到达后过了多少真实时间"外推。
        /// <para>
        /// 用途只剩"灾难级偏差的恢复判据"（见 <see cref="TickRender"/> 第 ② 步）：
        /// 时钟的**稳态推进不引用它**，于是到达时刻的抖动（网络抖动、编辑器卡顿）不会通过它
        /// 调制渲染速度 —— 那正是"速度脉动"的传播路径，已实测（离线仿真：速率被 ±15% 调制时
        /// 速度 cv 0.24~0.28，而速率恒 1 时 cv 0.000）。
        /// </para>
        /// </summary>
        private float ServerNowMs
        {
            get { return _newestMs + (Time.realtimeSinceStartup - _arrivalReal) * 1000f; }
        }

        /// <summary>
        /// **按渲染时钟选插值窗口**：在历史里找一对"夹住时钟"的快照 ⇒ 写 <see cref="_prevMs"/> /
        /// <see cref="_currMs"/> / <see cref="_prevIndex"/> / <see cref="_curIndex"/>，并返回插值比例 <c>t</c>。
        /// <para>
        /// 为什么必须"按时钟选"而不是"每收到一帧就换一对"：后者把换窗口的时刻绑在**包到达时刻**上，
        /// 而时钟是按真实时间走的 ⇒ 换窗口那一刻时钟离窗口末端还差 0~1 帧（实测 <c>t</c> 最大只到
        /// 0.707，而窗口末端才是 t=1）⇒ 那一帧要交付"剩下的任意份额"（0~100% 的一个间隔位移）
        /// ⇒ 实测速度 cv 0.24~0.28、峰值达匀速的 2.4 倍。按时钟换窗口时，窗口边界**只在 t=1 那一刻**
        /// 跨过，而 t=1 与下一段的 t=0 是同一个坐标 ⇒ 位置连续、速度严格恒定。
        /// </para>
        /// 越界（时钟早于最旧 / 晚于最新）时夹到最边上一对，⛔ 绝不外推。
        /// </summary>
        private float SelectWindow(float clock)
        {
            var lo = HistSlots - _histLen;
            var pi = HistSlots - 1;
            var ci = HistSlots - 1;
            if (_histLen >= 2)
            {
                pi = lo;
                ci = lo + 1;
                for (var i = lo; i < HistSlots - 1; i++)
                {
                    if (clock <= _msHist[i + 1]) { pi = i; ci = i + 1; break; }
                    pi = i;
                    ci = i + 1;
                }
            }

            _prevMs = _msHist[pi];
            _currMs = _msHist[ci];
            _prevIndex = _idxHist[pi];
            _curIndex = _idxHist[ci];
            var span = Mathf.Max(1f, _currMs - _prevMs);
            return _histLen >= 2 ? Mathf.Clamp01((clock - _prevMs) / span) : 1f;
        }

        /// <summary>改时钟速率并**重新锚定**对齐点（保证改速率那一刻时钟值连续 —— 不产生跳变）。</summary>
        private void SetClockRate(float rate, float realNow)
        {
            var current = RenderClockMs;
            _clockBaseMs = current;
            _clockBaseReal = realNow;
            _clockRate = rate;
        }

        /// <summary>
        /// 每帧渲染：把"服务端时间轴上的现在"插值出来。四步（见类注释三）：
        /// ① 从真实时间算出渲染时钟（⛔ 不因快照到达而重置）；② **稳态速率恒为 1**，
        /// 只有灾难级偏差才启用**有界追帧**；③ **按时钟**从历史里选插值窗口（<see cref="SelectWindow"/>）；
        /// ④ 用**两个快照自己的时间戳之差**做分母算 t。
        /// <para>
        /// ⛔ 第 ③ 步是本轮修复的核心：**换窗口的时刻由时钟决定，⛔ 不由包到达决定**。
        /// ⛔ 也不再"把时钟夹到 `_currMs`"：那是"每帧都贴着窗口末端"的写法，会把时钟**永久钉死**
        /// （然后必须靠速率追赶才能松开，而速率追赶本身就是速度脉动）。现在夹取语义改由
        /// <see cref="SelectWindow"/> 承担：时钟跑到最新快照之后 ⇒ t 夹到 1（冻在最后一个已知位置，
        /// ⛔ 不外推），而时钟**本身继续按真实时间前进**，新快照一到就自然回到窗口内。
        /// </para>
        /// </summary>
        private void TickRender()
        {
            var real = Time.realtimeSinceStartup;

            // ① 时钟：绝对真实时间 × 速率。本项目对战不真暂停（`AppFlow.EnterPause` 不动 timeScale），
            //    所以用 realtimeSinceStartup 而不是 time 是安全的；将来若有真暂停，那时冻住时钟才对。
            var clock = RenderClockMs;

            // ② 速率：**稳态恒为 1**（纯被动跟随，绝不用"改速度"去做校正 —— 改速度 = 速度脉动 = 抖）。
            //    只有灾难级偏差（> 3 个间隔，实测只在长卡顿/断网重连后出现）才用有界速率追帧。
            var err = (ServerNowMs - TargetLagMs) - clock;
            var rate = Mathf.Abs(err) > CatchUpThresholdMs ? SteerRate(err) : 1f;
            if (!Mathf.Approximately(rate, _clockRate)) SetClockRate(rate, real);
            clock = RenderClockMs;

            if (Mathf.Abs(err) > CatchUpThresholdMs && !_catchingUp)
            {
                _catchingUp = true;
                Game.Logger?.Warn(LogTag,
                    $"渲染时钟偏离目标 {err:F0}ms（目标落后 {TargetLagMs:F0}ms）⇒ 启动有界追帧（速率 {rate:F2}×）；" +
                    "常见成因：长卡顿 / 断网重连 / 服务端停推（本机卡顿会把 `Time.deltaTime` 夹在 `Time.maximumDeltaTime`）");
            }
            else if (Mathf.Abs(err) <= CatchUpThresholdMs && _catchingUp)
            {
                _catchingUp = false;
                Game.Logger?.Info(LogTag, $"渲染时钟已回到目标附近（偏离 {err:F0}ms）⇒ 速率回到 {rate:F2}×");
            }
            _renderMs = clock;

            // ③④ 按时钟选窗口 + 算 t：窗口边界只在 t=1 那一刻跨过 ⇒ 位置连续、速度恒定。
            var t = SelectWindow(_renderMs);
            _hasPrev = _histLen >= 2;
            ApplyEntities(t);
        }

        private void ApplyEntities(float t)
        {
            foreach (var kv in _curIndex)
            {
                var e = kv.Value;
                if (e == null) continue;

                UnitView view;
                if (!_units.TryGetValue(e.id, out view) || view == null)
                {
                    if (_units.Count >= MaxEntities)
                    {
                        if (!_overCapWarned)
                        {
                            _overCapWarned = true;
                            Game.Logger?.Warn(LogTag, $"单位视图数达到上限 {MaxEntities} ⇒ 不再新建（只告警一次）");
                        }
                        continue;
                    }
                    var visual = ResolveVisual(e);
                    view = UnitView.Acquire(_unitRoot, visual.Dir, visual.IsBuilding);
                    _units[e.id] = view;
                    Game.Logger?.Info(LogTag,
                        $"新建单位视图：id={e.id} kind={e.kind} card={e.card_id} " +
                        $"dir={(string.IsNullOrEmpty(visual.Dir) ? "(无索引，占位色)" : visual.Dir)}");
                }

                var world = GameConst.MilliToWorld(e.x_milli, e.y_milli);
                if (_hasPrev)
                {
                    EntitySnapshot p;
                    if (_prevIndex.TryGetValue(e.id, out p) && p != null)
                        world = Vector2.Lerp(GameConst.MilliToWorld(p.x_milli, p.y_milli), world, t);
                }

                view.Apply(e, world, e.deploy_ms > 0 ? DeployAlpha : 1f);
            }

            // 回收：本帧快照里没有的实体（快照是全量 ⇒ 缺席 = 已消失）。
            _recycle.Clear();
            foreach (var kv in _units)
                if (!_curIndex.ContainsKey(kv.Key)) _recycle.Add(kv.Key);
            for (var i = 0; i < _recycle.Count; i++)
            {
                UnitView v;
                if (_units.TryGetValue(_recycle[i], out v) && v != null) v.Release();
                _units.Remove(_recycle[i]);
            }
        }

        private void ApplyTowers(BattleSnapshot s)
        {
            if (_arena != null) _arena.ApplyTowers(s.towers_a, s.towers_b);

            var enemy = 1 - _myTeam;
            _ownLeftAlive = PrincessAlive(s, _myTeam, true);
            _ownRightAlive = PrincessAlive(s, _myTeam, false);
            _enemyLeftAlive = PrincessAlive(s, enemy, true);
            _enemyRightAlive = PrincessAlive(s, enemy, false);
        }

        /// <summary>
        /// 取某队"左/右"公主塔是否存活。
        /// <para>
        /// 服务端 roster 顺序是 `[国王塔, 左公主塔, 右公主塔]`（`server/game/core/tower.go:22-35`），
        /// 这里**先用 `Kind` 过滤掉国王塔、再按出现顺序数公主塔**，不硬依赖数组下标
        ///（下标错的后果是"口袋区判反"，玩家会在非法区看到绿色落点）。
        /// 取不到时返回 **true**（= 视为存活 ⇒ 不给口袋区）——**从严**：宁可少一块可选区域，
        /// 也不要让玩家看着能放、被服务端拒。
        /// </para>
        /// </summary>
        private static bool PrincessAlive(BattleSnapshot s, int team, bool left)
        {
            var arr = team == 0 ? s.towers_a : s.towers_b;
            if (arr == null) return true;
            var ordinal = left ? 0 : 1;
            var seen = 0;
            for (var i = 0; i < arr.Length; i++)
            {
                var t = arr[i];
                if (t == null) continue;
                if (t.kind == 1) continue; // 1 = 国王塔
                if (seen == ordinal) return t.alive;
                seen++;
            }
            return true;
        }

        // ───────────────────────── HUD 用的公开接口（拖放出牌） ─────────────────────────

        /// <summary>
        /// 该落点**看起来**是否合法（客户端预校验；规则在 <see cref="PlacementIndicator.IsLegalDeploy"/>）。
        /// </summary>
        /// <param name="tileXY">落点，**格**坐标（竞技场绝对坐标：y 小 = BLUE 后方）。</param>
        /// <param name="isSpell">该卡是否为法术（法术可落河面/敌方半场）。</param>
        public bool IsDeployLegal(Vector2 tileXY, bool isSpell)
        {
            var input = new PlacementIndicator.DeployInput
            {
                MyTeam = _myTeam,
                EnemyLeftPrincessAlive = _enemyLeftAlive,
                EnemyRightPrincessAlive = _enemyRightAlive,
                OwnLeftPrincessAlive = _ownLeftAlive,
                OwnRightPrincessAlive = _ownRightAlive
            };
            return PlacementIndicator.IsLegalDeploy(input, tileXY.x, tileXY.y, isSpell);
        }

        /// <summary>显示落点指示（合法绿 / 非法红）。HUD 在**每次指针移动**时调一次即可。</summary>
        public void ShowPlacement(Vector2 tileXY, float radiusTiles, bool isSpell)
        {
            if (_indicator == null) return;
            _indicator.Show(tileXY, radiusTiles, IsDeployLegal(tileXY, isSpell));
        }

        /// <summary>隐藏落点指示（抬手 / 取消拖放）。</summary>
        public void HidePlacement()
        {
            if (_indicator != null) _indicator.Hide();
        }

        /// <summary>屏幕坐标 → 世界坐标（格，中心为原点）。HUD 抬起手牌时把结果交给 `Events.Battle.PlayCardRequest`。</summary>
        public Vector2 ScreenToWorld(Vector3 screenPos)
        {
            if (_cam == null) return Vector2.zero;
            var w = _cam.ScreenToWorldPoint(screenPos);
            return new Vector2(w.x, w.y);
        }

        /// <summary>屏幕坐标 → **格**坐标（竞技场绝对坐标，x∈[0,18)、y∈[0,32)）。</summary>
        public Vector2 ScreenToTile(Vector3 screenPos)
        {
            return WorldToTile(ScreenToWorld(screenPos));
        }

        /// <summary>世界坐标 → 格坐标（只用 `GameConst` 的两个半场常量，⛔ 不散落 9/16 这类数字）。</summary>
        public static Vector2 WorldToTile(Vector2 world)
        {
            return new Vector2(world.x + GameConst.ArenaTilesW * 0.5f, world.y + GameConst.ArenaTilesH * 0.5f);
        }

        private static string HandToString(int[] hand)
        {
            if (hand == null || hand.Length == 0) return "(空)";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < hand.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(hand[i]);
            }
            return sb.ToString();
        }

        // ───────────────────────────── 卡 → 精灵目录 ─────────────────────────────

        /// <summary>一条"卡 → 怎么画"的映射。</summary>
        private struct CardVisual
        {
            /// <summary>`ResPaths` 里的精灵目录名（`chr_*_out` / `building_*_out`）；空串 = 没有对应素材。</summary>
            public readonly string Dir;

            /// <summary>true ⇒ 从 `ResPaths.BuildingDir` 取（建筑卡）。</summary>
            public readonly bool IsBuilding;

            public CardVisual(string dir, bool isBuilding)
            {
                Dir = dir;
                IsBuilding = isBuilding;
            }
        }

        /// <summary>
        /// 卡 id → 精灵目录。**来源 = `server/game/table/tsv/card.tsv` 的 `id` 与 `sprite_dir` 两列机械转写**，
        /// 50 张部队/建筑卡（type 0/2）全命中、**0 条目录不存在**（生成时逐条核对了
        /// `Assets/Resources/Sprites/{Units,Buildings}` 下的目录名）。
        /// <para>
        /// <b>⛔ 为什么客户端必须自带这张表</b>：协议里的 `CardInfo`（`Def/ProtoDef.cs:206-217`）**只有 `icon`**
        /// （值是 `card_knight` 这类**卡面**名，工程里并没有对应的卡面素材目录），
        /// **没有 `sprite_dir`**；服务端 `cardPool()`（`server/game/logic/cardtable.go:248-262`）
        /// 也确认只下发 id/key/name/…/icon。而单位渲染必须要 `chr_*_out` 这类的目录名 ⇒ 只能客户端补映射。
        /// 这是**已知契约缺口**（正解 = 服务端把 `sprite_dir` 一并下发，回报里已登记）。
        /// </para>
        /// <para>
        /// <b>为什么用 id 而不是 key</b>：key 要等 `Deck.PoolLoaded`（卡池到达）才能从 `CardInfo` 查到，
        /// 而快照可能先到（时序竞争）；id 直接就在 `EntitySnapshot.card_id` 里，**无时序依赖**。
        /// </para>
        /// <para>
        /// 表里多个 key 指向同一目录是**正常的**（骷髅兵/骷髅军团共用 `chr_skeleton_out`、
        /// 黑暗王子与王子共用 `chr_prince_out`、投矛手与哥布林共用 `chr_goblin_out`）。
        /// ⚠️ 已知不精确处：熔岩猎犬分裂出的"小猎犬"（`chr_lava_pups_out`）与法术召唤物若沿用原卡的 id，
        /// 会画成原卡精灵 —— 素材里有 `chr_lava_pups_out` 但**协议没有"这是哪个召唤物"的字段**，
        /// 属于同一类缺口，已登记。
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, CardVisual> CardVisuals = new Dictionary<int, CardVisual>
        {
            { 26010001, new CardVisual("chr_knight_out", false) },  // knight
            { 26010002, new CardVisual("chr_archer_out", false) },  // archers
            { 26010003, new CardVisual("chr_goblin_out", false) },  // goblins
            { 26010004, new CardVisual("chr_goblin_out", false) },  // spear-goblins
            { 26010005, new CardVisual("chr_giant_out", false) },  // giant
            { 26010006, new CardVisual("chr_pekka_out", false) },  // pekka
            { 26010007, new CardVisual("chr_minion_out", false) },  // minions
            { 26010008, new CardVisual("chr_minion_out", false) },  // minion-horde
            { 26010009, new CardVisual("chr_balloon_out", false) },  // balloon
            { 26010010, new CardVisual("chr_witch_out", false) },  // witch
            { 26010011, new CardVisual("chr_barbarian_out", false) },  // barbarians
            { 26010012, new CardVisual("chr_skeleton_out", false) },  // skeletons
            { 26010013, new CardVisual("chr_skeleton_out", false) },  // skeleton-army
            { 26010014, new CardVisual("chr_valkyrie_out", false) },  // valkyrie
            { 26010015, new CardVisual("chr_bomber_out", false) },  // bomber
            { 26010016, new CardVisual("chr_musketeer_out", false) },  // musketeer
            { 26010017, new CardVisual("chr_baby_dragon_out", false) },  // baby-dragon
            { 26010018, new CardVisual("chr_prince_out", false) },  // prince
            { 26010019, new CardVisual("chr_wizard_out", false) },  // wizard
            { 26010020, new CardVisual("chr_mini_pekka_out", false) },  // mini-pekka
            { 26010021, new CardVisual("chr_giant_skeleton_out", false) },  // giant-skeleton
            { 26010022, new CardVisual("chr_hog_rider_out", false) },  // hog-rider
            { 26010023, new CardVisual("chr_ice_wizard_out", false) },  // ice-wizard
            { 26010024, new CardVisual("chr_royal_giant_out", false) },  // royal-giant
            { 26010025, new CardVisual("chr_princess_out", false) },  // princess
            { 26010026, new CardVisual("chr_prince_out", false) },  // dark-prince
            { 26010027, new CardVisual("chr_lava_hound_out", false) },  // lava-hound
            { 26010028, new CardVisual("chr_ice_spirits_out", false) },  // ice-spirit
            { 26010029, new CardVisual("chr_fire_firespirit_out", false) },  // fire-spirit
            { 26010030, new CardVisual("chr_miner_out", false) },  // miner
            { 26010031, new CardVisual("chr_bowler_out", false) },  // bowler
            { 26010032, new CardVisual("chr_battle_ram_out", false) },  // battle-ram
            { 26010033, new CardVisual("chr_mega_minion_out", false) },  // mega-minion
            { 26010034, new CardVisual("chr_goblin_blowdart_out", false) },  // dart-goblin
            { 26010035, new CardVisual("chr_electro_wizard_out", false) },  // electro-wizard
            { 26010036, new CardVisual("chr_axe_man_out", false) },  // executioner
            { 26010037, new CardVisual("chr_bandit_out", false) },  // bandit
            { 26010038, new CardVisual("chr_bats_out", false) },  // bats
            { 26010039, new CardVisual("chr_mega_knight_out", false) },  // mega-knight
            { 26010040, new CardVisual("chr_movingcannon_out", false) },  // cannon-cart
            { 26010041, new CardVisual("building_basic_cannon_out", true) },  // cannon
            { 26010042, new CardVisual("building_goblin_hut_out", true) },  // goblin-hut
            { 26010043, new CardVisual("building_mortar_out", true) },  // mortar
            { 26010044, new CardVisual("building_inferno_tower_out", true) },  // inferno-tower
            { 26010045, new CardVisual("building_bomb_tower_out", true) },  // bomb-tower
            { 26010046, new CardVisual("building_barbarian_hut_out", true) },  // barbarian-hut
            { 26010047, new CardVisual("building_tesla_out", true) },  // tesla
            { 26010048, new CardVisual("building_elixir_collector_out", true) },  // elixir-collector
            { 26010049, new CardVisual("building_xbow_out", true) },  // x-bow
            { 26010050, new CardVisual("building_tombstone_out", true) },  // tombstone
        };

        private static CardVisual ResolveVisual(EntitySnapshot e)
        {
            if (e.kind == 2)
                // kind=2 是塔，⛔ 不该出现在 entities 里（塔由 towers_a/b 报告）——
                // 真出现就当"没有单位精灵"，不编一个 chr_* 出来。
                return default(CardVisual);

            CardVisual v;
            if (CardVisuals.TryGetValue(e.card_id, out v)) return v;

            if (UnknownCards.Add(e.card_id))
                Game.Logger?.Warn(LogTag,
                    $"卡 {e.card_id}（kind={e.kind}）不在「卡→精灵目录」表里 ⇒ 用占位色（表来源见 CardVisuals 注释）");
            return default(CardVisual);
        }
    }
}
