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
    /// `Battle01` 场景里**只有一台正交相机**（见 `Assets/Editor/SceneBuilder.cs`「不往里放占位物」），
    /// 场景本身不带任何表现层节点，场景生成器也不建这类节点。若没人创建本组件，
    /// `Events.Battle.*` 就没有订阅者 —— 现象是"进对局黑屏、不是报错"，最难查。
    /// 因此：<see cref="AutoInstall"/> 在 `AfterSceneLoad` 时建一个 `DontDestroyOnLoad` 的**控制器**，
    /// 它只负责订阅与状态；真正的画面节点（<c>BattleContent</c>）建在**当前场景**里，
    /// 于是出图时随场景一起卸载，不会把竞技场留在主菜单上。
    ///
    /// <h4>二、坐标从哪来（两个接缝，这里写清）</h4>
    /// 契约允许两条路：① 由 `Module/Battle` 在事件里带插值后的坐标；② 由本类自己插值。
    /// **本项目选 ②：`BattleViewRoot` 自己插值**。理由（可核对）：
    /// <list type="bullet">
    /// <item>`Core/Events.cs` 的 `Events.Battle.Snapshot` 参数**就是** `CR.Def.BattleSnapshot` 原件，
    ///   事件表里**不存在**"插值后坐标"的事件（已逐条读过 `Events.cs`：`Started/Snapshot/Events/Ended/PlayCardRequest/...`，
    ///   没有任何一条带插值坐标）⇒ 选 ① 就必须改 `Events.cs`（冻结）或让对局模块另发一条事件（会漂移）。</item>
    /// <item>插值只需要"上一帧快照 + 当前快照 + 到达时刻"，全部在收事件时就地可得，不需要 Module 的任何类型。</item>
    /// </list>
    /// <b>所以与对局模块的接缝是：它只要把 `PushBattleSnapshot` 原样 `Emit(Events.Battle.Snapshot, snapshot)` 即可，
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
    /// <item>**速率走有界比例修正**：<c>rate = clamp(1 + (落后量 − 目标)/目标 × <see cref="LagSteerGain"/>,
    ///   1 ± <see cref="LagSteerMaxRate"/>)</c>（稳态 ±5%），偏差超过 <see cref="CatchUpThresholdMs"/> 时
    ///   放开到 <see cref="CatchUpMaxRate"/> 做有界追帧。<b>为什么必须常开</b>：位置的导数就是速度，
    ///   所以校正绝不能"瞬跳时钟"（那会让单位前跳一大截）；但也**不能完全不校正** —— 一旦落后量涨到
    ///   超过快照历史的覆盖范围，`SelectWindow` 就只能夹到最旧一对、<c>t</c> 恒为 0，
    ///   **插值静默失效**（实测：7953 帧里 <c>t</c> 只有 3 帧取到中间值，其余非 0 即 1，
    ///   单位实际是每 100 ms 跳一格）。5% 的速率调制只在那几秒存在（偏差衰减到 0 后速率自动回 1），
    ///   换来的是插值永不失活。<b>为什么不用旧的 15% 档</b>：离线仿真里 15% 调制对应速度 cv 0.24~0.28。</item>
    /// <item>**按渲染时钟**从 <c>HistSlots</c>（本项目 6 = 5 个间隔）格快照历史里选"夹住时钟的那一对"
    ///   （<see cref="SelectWindow"/>），渲染落后**最新快照 2 个间隔**（<see cref="RenderLagIntervals"/>）：
    ///   <list type="bullet">
    ///   <item>手里始终多握一格快照 ⇒ 到达时刻抖 ±半个间隔也**推不动**正在渲染的窗口；</item>
    ///   <item>换窗口的时刻由时钟跨过时间戳决定（不是包到达）⇒ 边界只在 <c>t=1</c> 那一刻跨过，
    ///     而上一段的 <c>t=1</c> 与下一段的 <c>t=0</c> 是**同一个坐标** ⇒ 位置连续、速度恒定。</item>
    ///   </list></item>
    /// <item>比例 <c>t = Clamp01((_renderMs - prev.server_ms) / max(1, cur.server_ms - prev.server_ms))</c>，
    ///   位置 <c>= Lerp(prevPos, curPos, t)</c>。分母是**两个快照自己的时间戳之差**（真实间隔），
    ///   不是编译期常量。时钟跑到最新快照之后 ⇒ <c>t</c> 夹到 1（冻在最后一个已知位置，⛔ 不外推）。</item>
    /// </list>
    /// <b>分母与换窗口时刻的口径（两条都不是可有可无的）</b>：分母取**两个快照自己的时间戳之差**（真实间隔，
    /// ⛔ 不写死 100 ms）；换窗口的时刻由**时钟**决定，⛔ 不绑在"包到达"上。
    /// <list type="bullet">
    /// <item>若分母写死 100 ms 且**每收到一帧就把"当前帧到达时刻"重置** ⇒ `t` 每收一帧从 0 重来一次，
    ///   上一次已插值到 t=0.6 的位置被整段丢弃 ⇒ **每收一帧前跳一次**。</item>
    /// <item>若换窗口仍绑在"包到达"上（每收一帧就把 (<c>_prevIndex</c>, <c>_curIndex</c>) 换成最新一对）：
    ///   换的瞬间时钟离窗口末端还差 0~1 帧，窗口末端那一小段位移被**塞进换窗口的那一帧**交付。
    ///   <b>实测（同一台机器）</b>：<c>t</c> 最高只到 0.707
    ///   （= 窗口从来没走完）、<c>RenderLagMs</c> 最大 119.5ms（**超过一个快照间隔**）、单帧速度在
    ///   0.782~1.049 之间跳（cv 0.24，峰值比 1.23）—— 人眼看到的就是 10 Hz 的"哆嗦"。
    ///   离线仿真：速率微调关掉（速率恒 1）但保留"到达驱动换窗口"⇒ 速度 cv 0.37~0.47；
    ///   **按时钟选窗口 ⇒ 速度 cv 0.000、峰值比 1.000、零位移帧 0**。</item>
    /// </list>
    /// <b>为什么不直接把快照坐标贴到 Transform</b>（⛔ 明令禁止）：那等于每 100 ms 瞬移一次，
    /// 单位看起来是"一格一格跳"；而"显示服务端时间轴上稍早一点的位置"在联机对战里没有可感知代价
    ///（对手看到的是同样的落后量），却把 10 Hz 变成视觉上的连续运动。
    /// <b>代价（明知）</b>：画面比服务端晚 <see cref="RenderLagIntervals"/> 个间隔（本项目 200ms）。
    /// 这是"绝不前跳"换来的代价，刻意如此：前跳会瞬间把单位推过头（甚至穿过墙）。
    /// 允许多付的只是**秒级的 ±5% 速率微调**（把落后量自己走回 200ms），不是位置瞬跳。
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

        // ── 离散事件 kind：逐字对应 `Def/ProtoDef.cs:199`（`BattleEvent.kind` 的注释；字段在 `:197-206` 的
        //    `class BattleEvent` 里）
        //    与服务端 `server/game/core/snapshot.go:4-11` 的 `Ev*` 常量（同一套编号）。──

        /// <summary>`kind == 0`：出牌（`EvPlayCard`）。</summary>
        private const int EventKindPlayCard = 0;

        /// <summary>`kind == 1`：生成（`EvSpawn`）。服务端在 `spawnUnit` 里发射，带落点 + 实体 id
        /// （`server/game/core/combat.go:389-391`）—— 本类用它播出牌落地表现（见 <see cref="PlayDeploy"/>）。</summary>
        private const int EventKindSpawn = 1;

        /// <summary>`kind == 2`：死亡（`EvDeath`）。</summary>
        private const int EventKindDeath = 2;

        /// <summary>`kind == 3`：塔毁（`EvTowerDestroyed`）。</summary>
        private const int EventKindTowerDestroyed = 3;

        /// <summary>`kind == 4`：圣水满（`EvElixirFull`）。表现层无特效帧（音效在 <see cref="BattleAudioView"/>）。</summary>
        private const int EventKindElixirFull = 4;

        /// <summary>`kind == 5`：塔激活（`EvTowerActivated`）。表现层无特效帧（音效在 <see cref="BattleAudioView"/>）。</summary>
        private const int EventKindTowerActivated = 5;

        /// <summary>`kind == 6`：**塔开火**（`EvTowerShoot`，服务端 `core.snapshot.go` 的 `EvTowerShoot`）。
        /// 载荷 = 塔 id（`entity_id`）+ 塔根坐标（`x_milli/y_milli`）+ `team` + 投射物速度
        /// （`proj_speed`，格/分钟；0 = 该塔没有投射物/表里没速度 ⇒ 只播枪口闪光）。
        /// ⛔ 载荷里**没有目标** ⇒ 目标由客户端用最近一帧快照近似（见 <see cref="PlayTowerShot"/>）。</summary>
        private const int EventKindTowerShoot = 6;

        /// <summary>
        /// 卡类型 = **法术**（`Def/ProtoDef.cs` 的 `CardInfo.type` 注释：`0=部队 1=法术 2=建筑`；
        /// 与服务端 `server/game/core/card.go:38-42` 的 `CardTypeSpell = 1` 是同一套编号）。
        /// 本类只用它做一件事：把出牌事件分流到法术特效（见 <see cref="PlayCardFx"/>）。
        /// </summary>
        private const int CardTypeSpell = 1;

        // ── 出牌落地表现（`EvSpawn`）的原版素材 ──
        //
        // 素材帧号出处（⛔ 不许挑帧）：
        //   `策划/单位动画分组表.md:5021` —— export `deploy_arrows_effect` / clip 354 / 60 fps /
        //   timeline 16 条**全部指向同一像素帧 f119**（原始 PNG = `原版资源/cr-assets-png/assets/sc/effects_out/effects_sprite_119.png`）；
        //   同表 `:4725` 的 `deploy_arrows`（clip 354）与它同 clip ⇒ 二者是同一段动画的两种导出名。
        //   档位列 = **spawn**（`策划/单位动画分组表.md:5021`、"spawn" 分类）。
        // 为什么不用另一个 spawn 候选 `filter_deploy_unit_*`（同表 `:5149-5153`，唯一像素帧 f258）：
        //   f258 的像素内容 = **一个具体单位的彩色形象**（描出的是哥布林本体，非通用剪影/掩膜），
        //   而 `filter_deploy_unit_default` / `filter_cold` / `filter_damage_*` / `filter_hologram` /
        //   `filter_poison` … **十几个不同滤镜共用同一个 f258**（`策划/单位动画分组表.md:5143-5167`）
        //   ⇒ f258 是"被叠加到单位身上的滤镜底图"，把它当**独立落地特效**播会在任意卡落点上画出一个哥布林。
        //   故取 f119（真正的独立 spawn 特效：绿色上箭头）。

        /// <summary>落地特效用途目录名（`ResPaths.EffectDir` 拼 `Sprites/Effects/Deploy`）。</summary>
        private const string DeployFxDir = "Deploy";

        /// <summary>落地特效起始帧（原版 f119；见上方出处块）。</summary>
        private const int DeployFxFirst = 119;

        /// <summary>
        /// 落地表现时长（秒）。出处 = `策划/策划案/皇室战争参考规格.md:156`「落点后 `deploy_time`
        /// （官方普遍 1000ms）才出现并可行动」⇒ 落地表现与部署延迟同长。
        /// （该效果的自身时间轴长度 = 16 条 timeline ÷ 60 fps ≈ 0.267 s，见 `策划/单位动画分组表.md:5021`；
        ///  因为 f119 是静止箭头，按自身 0.267 s 播会一闪而过，故取与 deploy 同长的 1 s。）
        /// </summary>
        private const float DeployFxSeconds = 1.0f;

        /// <summary>
        /// 落位标记（绿色上箭头 f119）的**同卡去重窗口**（秒）。
        /// <para>
        /// <b>本项目自定</b>：多单位卡片由服务端**逐单位**发 `EvSpawn`（同一 tick 内的若干条相隔 &lt; 10 ms），
        /// 而原版玩家看到的落地标记是"这张卡在就位"这**一枚** ⇒ 窗口只需覆盖"同一批生成"。
        /// 取 0.5 s 是为了容纳服务端分帧生成（例如召唤建筑的第二波小兵）。
        /// </para>
        /// </summary>
        private const float DeployDedupeSeconds = 0.5f;

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
        /// <b>为什么必须记住这个</b>：`Teardown()` 之后 `_built == false`，而
        /// `OnBattleStarted` / `OnSnapshot` / `OnBattleEventNotify` 里各有一条"兜底重建"
        /// `if (!_built) Build();`（本意是"站点切换事件没到也要能进图"）。回主菜单时
        /// 服务端仍在对推快照（暂停/回菜单都不结束对局），这些**尾随推送**会把 `BattleContent`
        /// **在 Main 场景里重建一份**：画面无可见残留（被主菜单面板盖住），
        /// 但对象树里会留下激活的 `UnitView` + `EffectsView` + `BattleAudio`。
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

        /// <summary>未知 `kind` 只 Warn 一次（协议扩了 kind 而本类没跟上时一次留痕即可，⛔ 不静默、⛔ 不刷屏）。</summary>
        private bool _unknownKindWarned;

        /// <summary>战斗中开火弹道里"卡池没这张卡"只 Warn 一次。</summary>
        private bool _fireCardMissingWarned;

        /// <summary>战斗中开火弹道"找不到任何敌方实体"只 Warn 一次。</summary>
        private bool _fireNoTargetWarned;

        /// <summary>出牌弹道的施法者退化到国王塔中心只 Warn 一次。</summary>
        private bool _casterFallbackWarned;

        // ── 命中特效：协议没有「命中」事件，只能靠逐 id 比对前后两帧快照的 `hp` ──
        //    （同 `BattleAudioView.OnSnapshot` 的做法，见该类 `:287-320` 的类注释：`Def/ProtoDef.cs:199`
        //     只有 0..5 六种 kind，`kind==2` 只在死亡时发 ⇒ 非致死命中没有任何事件可挂。）

        /// <summary>上一帧快照里每个实体的 `hp`（`entity_id → hp`）。</summary>
        private Dictionary<int, int> _prevHp = new Dictionary<int, int>();

        /// <summary>本帧 hp 的暂存（双缓冲：比完交换，⛔ 不每帧 new Dictionary）。</summary>
        private Dictionary<int, int> _curHp = new Dictionary<int, int>();

        /// <summary>上一帧快照里每个实体的 `anim`（`entity_id → anim`）—— 用来判"这一帧刚进入攻击档"。
        /// 协议里没有"开火"事件（`server/game/core/combat.go:68-73` 只在挥砍那一 tick 把 anim 置 2），
        /// 所以"战斗中开火"的唯一可得信号 = **anim 从非 2 跳到 2** 的上升沿。</summary>
        private Dictionary<int, int> _prevAnim = new Dictionary<int, int>();

        /// <summary>本帧 anim 的暂存（双缓冲）。</summary>
        private Dictionary<int, int> _curAnim = new Dictionary<int, int>();

        // ── 自检计数（供 EditMode 单测 / 实机 driver 读；判据 = "每次 hp 下降 ⇒ 恰好一次命中特效"）──

        /// <summary>本局观察到的"某实体 hp 下降且**未死**"次数（= 应当播出的命中特效次数）。</summary>
        public int HpDropObserved { get { return _hpDropObserved; } }

        /// <summary>本局观察到"某实体 hp 降到 ≤0"的次数（由 `EvDeath` 的闪光分支负责，不计入命中特效）。</summary>
        public int LethalDropObserved { get { return _lethalDropObserved; } }

        /// <summary>本局真的播出去的命中特效次数。</summary>
        public int HitFxPlayed { get { return _hitFxPlayed; } }

        /// <summary>本局被丢掉的命中特效次数（同屏上限 / 素材目录为空）。
        /// <b>自检断言</b>：<c>HitFxPlayed + HitFxSkipped == HpDropObserved</c>（不成立 = 有 hp 下降没播到特效 = 判红）。</summary>
        public int HitFxSkipped { get { return _hitFxSkipped; } }

        /// <summary>本局播出的出牌落地特效次数（= 收到的 `EvSpawn` 事件里成功播出的数量）。</summary>
        public int DeployFxPlayed { get { return _deployFxPlayed; } }

        /// <summary>本局被"同一次出牌"去重掉的落位标记条数（多单位卡片的后续单位，见 `PlayDeploy`）。</summary>
        public int DeployDupeSkipped { get { return _deployDupeSkipped; } }

        /// <summary>本局因"攻击档"触发播出的战斗弹道条数。</summary>
        public int BattleProjectileShots { get { return _battleShots; } }

        /// <summary>
        /// 本局因**出牌事件**（`EvPlayCard` + `proj_speed &gt; 0`）播出的弹道条数
        /// （= <see cref="PlayProjectileFlight"/> 走到 `PlayFlight` 的次数）。
        /// <para>与 <see cref="BattleProjectileShots"/> 分开计：那条 = "单位攻击"（`anim == 2` 反推），
        /// 这条 = "出牌"（有事件可挂）—— 两者的触发源不同，合起来才覆盖任务 3 的两半要求。</para>
        /// </summary>
        public int CardProjectileShots { get { return _projectileShots; } }

        /// <summary>
        /// 本局收到的**塔开火**事件（`kind == 6` / `EvTowerShoot`）条数。
        /// <para>判据：<c>TowerShots == 服务端日志里 EvTowerShoot 的条数</c>（塔开火链路端到端通的必要条件）。</para>
        /// </summary>
        public int TowerShots { get { return _towerShots; } }

        /// <summary>本局从**塔开火事件**里真的播出弹道（`proj_speed &gt; 0`）的条数。</summary>
        public int TowerShotFlights { get { return _towerShotFlights; } }

        /// <summary>本局塔开火里因取不到炮口 / 无目标 / 无速度而只播枪口闪光（不播飞行段）的条数。</summary>
        public int TowerShotMuzzles { get { return _towerShotMuzzles; } }

        /// <summary>
        /// 本局塔开火里**两段都没播出去**的次数（特效层同屏上限 / 素材目录为空）。
        /// <para><b>自检断言</b>：<c>TowerShots == TowerShotFlights + TowerShotMuzzles + TowerShotSkipped</c>
        /// （不成立 = 有开火事件没被任何一条路径处理 = 判红）。</para>
        /// </summary>
        public int TowerShotSkipped { get { return _towerShotSkipped; } }

        /// <summary>
        /// 本局收到的**法术卡出牌**事件（`EvPlayCard` 且 `CardInfo.type == 1`）条数 —— 差异登记见 `策划/差异登记.tsv`。
        /// <para><b>自检断言</b>：<c>SpellCasts == SpellFxPlayed + SpellFxSkipped</c>
        /// （不成立 = 有法术出牌没走到播放入口 = 判红）。</para>
        /// </summary>
        public int SpellCasts { get { return _spellCasts; } }

        /// <summary>本局真的播出去的法术命中特效次数。</summary>
        public int SpellFxPlayed { get { return _spellFxPlayed; } }

        /// <summary>本局被丢弃的法术特效次数（卡池未到 / 这张法术还没接帧 / 特效层同屏上限 / 目录为空）。</summary>
        public int SpellFxSkipped { get { return _spellFxSkipped; } }

        /// <summary>命中特效的逐条日志上限（超过只计数）—— ⛔ 不刷屏，但计数仍然是全量的。</summary>
        private const int HitLogLimit = 5;

        private int _hpDropObserved;
        private int _lethalDropObserved;
        private int _hitFxPlayed;
        private int _hitFxSkipped;
        private int _deployFxPlayed;

        /// <summary>
        /// 本场画面（`Build()` 之后）已开始过的那一局的 `room_id` / `seed`（`BattleStartNotify` 自带）。
        /// `null` = 本场画面还没开过局；判"是不是新的一局"的键，见 <see cref="OnBattleStarted"/>。
        /// </summary>
        private string _matchRoomId;

        /// <summary>同上那一局的 `seed`（与 <see cref="_matchRoomId"/> 配对）。</summary>
        private long _matchSeed = long.MinValue;

        /// <summary>
        /// 待预热精灵目录的那一局（<see cref="OnBattleStarted"/> 写下、<see cref="WarmBattleSprites"/> 消费）。
        /// <para>
        /// 为什么要留一手：`Events.Battle.Started` 在 **`Scene.Load` 之前**就发出（见 `Build()` 开头的注释），
        /// 首次进图时它早于建场 ⇒ 预热只能在建场之后补做（那时资源后端已就绪）；同一场景连开下一局时
        /// 画面已建好 ⇒ 当场就做。<c>null</c> = 没有待预热的局。
        /// </para>
        /// </summary>
        private BattleStartNotify _warmNotify;

        /// <summary>最近一条落位标记所用的卡 id（同卡去重的键，见 <see cref="PlayDeploy"/>）。</summary>
        private int _lastDeployCardId;

        /// <summary>最近一条落位标记的时刻（`Time.unscaledTime`，与 <see cref="DeployDedupeSeconds"/> 配对）。</summary>
        private float _lastDeployTime = -999f;

        /// <summary>被"同一次出牌"去重掉的落位标记条数（自检用，见 <see cref="DeployDupeSkipped"/>）。</summary>
        private int _deployDupeSkipped;

        private int _battleShots;
        private int _towerShots;
        private int _towerShotFlights;
        private int _towerShotMuzzles;
        private int _towerShotSkipped;
        private int _spellCasts;
        private int _spellFxPlayed;
        private int _spellFxSkipped;

        /// <summary>法术特效"这张法术还没接帧"只报一次（⛔ 不刷屏）。</summary>
        private bool _spellFxUnknownWarned;

        /// <summary>塔开火时"塔视图里找不到炮口层"只报一次（⛔ 不刷屏）。</summary>
        private bool _towerMuzzleMissingWarned;

        /// <summary>
        /// **最近一帧快照的实体数组**（只读，用于塔开火事件里挑"最近敌方"）。
        /// <para>
        /// 为什么需要它：`EvTowerShoot`（`kind==6`）只带塔自己的位置和速度，**不带目标**
        /// （服务端 `core.emitTowerShoot` 的载荷定义如此）⇒ 客户端要知道把弹道飞向哪里，
        /// 只能拿最近一帧快照自己近似。事件与快照是不同的推送通道，事件可能略早于对应快照，
        /// 故这里只当**近似**用（登记在报告里，和 <see cref="NearestEnemy"/> 的近似口径同源）。
        /// </para>
        /// </summary>
        private EntitySnapshot[] _lastEntities;

        /// <summary>每个实体上一次**成功播出**弹道的服务端时刻（毫秒）—— 用来按 `hit_speed` 排期。</summary>
        private readonly Dictionary<int, float> _lastShotMs = new Dictionary<int, float>();

        /// <summary>`_lastShotMs` 的清理暂存（⛔ 不在遍历中改字典）。</summary>
        private readonly List<int> _shotPrune = new List<int>();

        /// <summary>
        /// **插值窗口（= 被渲染的那一对快照）**的起始索引。由 <see cref="SelectWindow"/> **按渲染时钟**
        /// 从 <see cref="_idxHist"/> 里选出，⛔ 不是"每收到一帧就换一对"（见类注释三）。
        /// </summary>
        private Dictionary<int, EntitySnapshot> _prevIndex = new Dictionary<int, EntitySnapshot>();

        /// <summary>插值窗口的结束索引（与 <see cref="_prevIndex"/> 同属 <see cref="_idxHist"/> 的两格）。</summary>
        private Dictionary<int, EntitySnapshot> _curIndex = new Dictionary<int, EntitySnapshot>();

        /// <summary>
        /// 快照历史槽数（最旧 … 最新）。取 <b>6</b>：历史覆盖 <c>HistSlots-1 = 5</c> 个间隔（= 500 ms），
        /// 而设计落后量只有 <see cref="RenderLagIntervals"/> = 2 个间隔（200 ms）⇒ 留 **3 个间隔**的余量。
        /// <para>
        /// ⛔ 这个余量**不是**可有可无的冗余，而是"插值能不能活着"的硬边界：`SelectWindow` 要求时钟落在
        /// 历史区间内；时钟一旦跑到最旧那一格之前，就只能夹到最旧的一对 ⇒ <c>t</c> 恒为 0
        /// （= 位置按快照周期阶梯跳、**插值静默失效**）。实测（逐帧采样，7953 帧）：
        /// <c>newest - renderMs</c> 稳定在 **417 ms**（= 4 个间隔），而 <c>HistSlots = 4</c> 只能覆盖
        /// 3 个间隔 ⇒ 时钟整段跑在窗口之外，<c>t</c> 取值统计 = <b>0(5150 帧) / 1(2355 帧) / 中间值(仅 3 帧)</b>
        /// ⇒ 单位实际是"每 100 ms 跳一格"= 人眼看到的"又飘又抖"。取 6 之后余量 500 ms > 实测漂移量。
        /// </para>
        /// <para>
        /// 余量的**源头**（为什么时钟会漂到 4 个间隔）：`ServerNowMs` 靠"最新快照时间戳 + 到达后的真实时间"
        /// 外推，进入对战那一刻服务端会把积压快照**成批**下发（实测 0.12 s 内 newest 从 300 跳到 2750），
        /// 于是时钟相对服务端时间轴一下落后 2 个多间隔；若稳态速率微调被关掉（见类注释三·第 2 条），
        /// 300 ms 以内不校正 ⇒ 这个落后量**永久留下**。因此余量（本行）与 <see cref="SteerRate"/>
        /// 的稳态微调（让它自己走回 200 ms）两者都必须有。
        /// </para>
        /// </summary>
        private const int HistSlots = 6;

        /// <summary>各历史槽的服务端时间戳（毫秒），下标 0 最旧、<c>HistSlots-1</c> 最新。</summary>
        private readonly float[] _msHist = new float[HistSlots];

        /// <summary>各历史槽的实体索引（<c>id → EntitySnapshot</c>），与 <see cref="_msHist"/> 一一对应。</summary>
        private readonly Dictionary<int, EntitySnapshot>[] _idxHist =
        {
            new Dictionary<int, EntitySnapshot>(),
            new Dictionary<int, EntitySnapshot>(),
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
        /// ⛔ 不等于"最新收到的快照"（见类注释三：换窗口由时钟决定，不由包到达决定）。
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
        /// **快照到达时绝不重置**（按到达时刻重置会让每收一帧前跳一次）。
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
        /// <item>偏差 ≤ <see cref="CatchUpThresholdMs"/>：±<see cref="LagSteerMaxRate"/>（5%）之内**按比例**
        ///   微调（人眼无感），偏差越小修正越小、归零则速率回 1；</item>
        /// <item>偏差超过阈值：放开到 <see cref="CatchUpMaxRate"/>（落后太多时追赶、超前时放慢）。</item>
        /// </list>
        /// ⛔ 调用方必须**每帧都调**（<see cref="TickRender"/> 第 ② 步）—— 见类注释三·第 2 条。
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

        /// <summary>「渲染时钟掉出快照历史」告警的去重标志（进入掉窗报一次、回到窗内复位）。</summary>
        private bool _outOfWindowWarned;

        /// <summary>
        /// 追帧阈值：落后服务端超过这么多毫秒就把速率放开到 <see cref="CatchUpMaxRate"/>。
        /// 取 3 个快照间隔（300ms）—— ⛔ 它**只是"放开档位"的界线，不是"要不要校正"的开关**：
        /// 300ms 以内同样在按比例校正（±5%），否则时钟会被成批到达的快照**永久**推到历史之外
        /// （见 <see cref="HistSlots"/> 与 <see cref="TickRender"/> 的实测说明）。
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
        /// <para>
        /// 取 <c>0.05</c> = <see cref="LagSteerMaxRate"/> ⇒ 偏差恰好一个目标量（200 ms）时刚好到满偏 5%，
        /// 偏差更小就**按比例**变小（20 ms ⇒ 0.5%）。⛔ 不取旧的 0.5 —— 那个值让任何 ≥60 ms 的偏差都
        /// **直接顶到满偏**，等于"要么不动、要么一直 5%"，闭环变成开关式而非比例式。
        /// </para>
        /// </summary>
        public const float LagSteerGain = LagSteerMaxRate;

        /// <summary>
        /// 稳态速率允许的偏离（±5%）。人眼对"整体快/慢 5%"没有感觉，但足以把时钟**平滑**拉回目标
        /// （一阶收敛，时间常数 ≈ <c>目标量 / 满偏 = 200ms / 0.05 = 4 s</c>），⇒ 用**渐变**代替"瞬跳"
        /// （瞬跳就是"抖"）。
        /// <para>
        /// ⛔ 不取 0.15 这一档：它在离线仿真里的速度 cv 是 0.24~0.28（"速率被调制 = 速度脉动"）；
        /// 5% 档对应的速度调制是 2.2 格/s × 5% = 0.11 格/s，且**只在必要的那几秒存在**（收敛到目标后
        /// 偏差→0 ⇒ 速率自动回到 1）。这一档换来的是"插值永不失效"。
        /// </para>
        /// </summary>
        public const float LagSteerMaxRate = 0.05f;

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
        ///（渐变而不是瞬跳 —— 瞬跳就是"抖"）。
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
        /// 自安装：**进对局前**把控制器建好（`Battle01` 场景里没有任何表现层节点，见类注释一）。
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
            //    实测症状：HUD 全对（冠数 / 计时 / 圣水 / 手牌），
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
            _unknownKindWarned = false;
            _fireCardMissingWarned = false;
            _fireNoTargetWarned = false;
            _casterFallbackWarned = false;
            _hpDropObserved = 0;
            _lethalDropObserved = 0;
            _hitFxPlayed = 0;
            _hitFxSkipped = 0;
            _deployFxPlayed = 0;
            _lastDeployCardId = 0;
            _lastDeployTime = -999f;
            _deployDupeSkipped = 0;
            _battleShots = 0;
            _towerShots = 0;
            _towerShotFlights = 0;
            _towerShotMuzzles = 0;
            _towerShotSkipped = 0;
            _spellCasts = 0;
            _spellFxPlayed = 0;
            _spellFxSkipped = 0;
            _spellFxUnknownWarned = false;
            _towerMuzzleMissingWarned = false;
            _lastEntities = null;
            _prevHp.Clear();
            _curHp.Clear();
            _prevAnim.Clear();
            _curAnim.Clear();
            _lastShotMs.Clear();
            _shotPrune.Clear();
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
            // 「本场画面还没开过局」：⛔ 不清 ⇒ 同一场景连开下一局时被判成"同一局"，塔的阵亡态与快照时钟
            // 都不会重置（见 `OnBattleStarted`）。上面那组时间轴字段同时归零 ⇒ 新局 `server_ms` 从 0 重走。
            _matchRoomId = null;
            _matchSeed = long.MinValue;
            _built = true;
            Game.Logger?.Info(LogTag,
                $"对局表现层已建：塔={_arena.TowerCount} 相机={_cam.name} 正交半高={_cam.orthographicSize:F2}（18×32 格竖屏）");
            // 建场后才预热：`Events.Battle.Started` 可能早于本方法（首次进图），此刻资源后端已就绪。
            WarmBattleSprites();
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
            _warmNotify = null;         // 画面已拆 ⇒ 那份待预热（见 WarmBattleSprites）没有落点了
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
        /// ⛔ 不能写成无条件 `if (!_built) Build();`（见 `_inBattle` 的注释：离开站点后的尾随推送会把画面重建到 `Main` 里）。
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
            // ── 同一场景里的**新一局**必须整场重建（差异登记 D173）──
            // 「再来一局」时 `AppFlow.RequestEnterBattle` 只切站点、不重载场景（`CurrentScene == Battle01`）
            // ⇒ 本组件不重建，而这两样东西都是**每局唯一**的、必须归零的：
            //   ① `ArenaView.TowerView` 的阵亡终态锁（`ArenaView._destroyed` 置位后不清零）会把新局
            //      "塔满血存活"的快照挡掉 ⇒ 塔体各层仍隐藏、废墟层仍亮（= 塔还是死亡状态）；
            //   ② 快照时钟（`_snapshotCount/_newestMs/_msHist/_histLen/_clockBaseMs`）还停在上局末尾，
            //      而新局 `server_ms` 从 0 起 ⇒ `OnSnapshot` 的"时间戳没有前进 ⇒ 整帧丢弃"会把新局快照
            //      一直丢到时间追上上一局（画面停在上局末帧）。
            // 判"新的一局"= `room_id` + `seed` 变化（服务端每局新建房间）；`_matchRoomId == null` = 本场画面
            // 还没开过局（`Build()` 里清），此时画面本来就是新建的，⛔ 不重建（否则每次进图白重建一次）。
            // `Teardown()` 之后由下方的 `RebuildIfWanted` 重建；`Build()` 里把上面两组一起归零
            // ⇒ ⛔ 只清 `_destroyed` 不够（时钟那组不清仍会停帧）。
            var newMatch = _matchRoomId != null && (n.room_id != _matchRoomId || n.seed != _matchSeed);
            if (newMatch)
            {
                Game.Logger?.Info(LogTag,
                    $"新的一局：room_id={n.room_id} seed={n.seed}（上一局 room_id={_matchRoomId} seed={_matchSeed}）" +
                    $"⇒ 对局表现层整场重建（已建={_built}）");
                _matchRoomId = n.room_id;
                _matchSeed = n.seed;
                if (_built) Teardown();
            }
            else if (_matchRoomId == null)
            {
                _matchRoomId = n.room_id;
                _matchSeed = n.seed;
            }
            // hp / anim 比对表必须清零：⛔ 不清会把上一局的 hp 与新局比对出**假命中**，
            // 也会让上一局的 `anim==2` 与开局第一帧比出一个假"开火"（同 BattleAudioView.OnBattleStarted 的处置）。
            _prevHp.Clear();
            _curHp.Clear();
            _prevAnim.Clear();
            _curAnim.Clear();
            _lastShotMs.Clear();
            if (!_built) RebuildIfWanted("Events.Battle.Started"); // 兜底：万一 StationChanged 没到（例如直接由服务端推送进对局）
            // 精灵预热：画面已建 ⇒ 当场做；画面待建（首次进图，本事件早于 `Scene.Load`）⇒ 记下待办、由 `Build()` 补做。
            _warmNotify = n;
            if (_built) WarmBattleSprites();
            var regulation = n.timeline != null ? n.timeline.regulation_ms : 0;
            Game.Logger?.Info(LogTag,
                $"对局开始：my_team={_myTeam}({(_myTeam == 0 ? "BLUE" : "RED")}) 常规={regulation}ms " +
                $"我的手牌={HandToString(_myTeam == 0 ? n.hand_a : n.hand_b)} 下一张={(int)(_myTeam == 0 ? n.next_a : n.next_b)}");
        }

        // ── D167 放卡卡顿：战斗期会用到的精灵目录，在进图期先整目录抓一遍 ──

        /// <summary>
        /// 预热本局会用到的精灵目录（消费 <see cref="_warmNotify"/>，一局一次）。
        /// <para>
        /// <b>为什么必须预热</b>：<see cref="SpriteBank.LoadDir(string,SpriteBank.SpritePivotMode)"/> 是
        /// **同步整目录加载**（引擎 <c>IResourceManager.LoadAll{T}</c>，契约见 <see cref="SpriteBank"/> 的类注释：
        /// "同步、阻塞主线程，请在进图前 / 读条阶段调用"），而 `UnitView.Bind` 与 `EffectsView.Spawn`
        /// 都在**战斗热路径**里第一次碰到某目录时才加载它 ⇒ 出牌 / 单位出场 / 首次命中那一帧要等这次加载，
        /// 表现为"放一张卡卡一下、单位出现又卡一下"。<see cref="Teardown"/>（出图）会
        /// <see cref="SpriteBank.ClearCache"/> ⇒ 每局都从头踩一遍，所以预热也必须**每局做一次**。
        /// </para>
        /// <para>
        /// 范围 = 双方卡组 + 双方手牌（卡 id → 目录走 <see cref="CardVisuals"/>，法术卡不在表里、由下方的
        /// 特效目录覆盖）+ 战斗期特效目录（落地 / 死亡 / 命中 / 爆炸 / 弹道 / 法术命中）。
        /// `FrameBank` 按 (锚点模式, 路径) 缓存 ⇒ 重复目录再调一次不重复加载。
        /// </para>
        /// </summary>
        private void WarmBattleSprites()
        {
            var n = _warmNotify;
            if (n == null) return;
            _warmNotify = null;

            var dirs = 0;
            var hits = 0;
            var frames = 0;
            WarmCardList(n.deck_a, ref dirs, ref hits, ref frames);
            WarmCardList(n.deck_b, ref dirs, ref hits, ref frames);
            WarmCardList(n.hand_a, ref dirs, ref hits, ref frames);
            WarmCardList(n.hand_b, ref dirs, ref hits, ref frames);
            var cardDirs = dirs;

            var uses = new[]
            {
                DeployFxDir,
                ResPaths.EffectDeathBlue, ResPaths.EffectDeathPurple, ResPaths.EffectDeathGround,
                ResPaths.EffectHit, ResPaths.EffectBlast, ResPaths.EffectArrow,
                ResPaths.EffectSpell, ResPaths.EffectSpellBarrel,
            };
            for (var i = 0; i < uses.Length; i++)
            {
                var f = SpriteBank.LoadDir(ResPaths.EffectDir(uses[i]), SpriteBank.SpritePivotMode.UnifiedCanvasAnchor);
                dirs++;
                if (f.Length > 0) { hits++; frames += f.Length; }
            }

            // 判据行：`hits` 必须等于 `dirs`（取不到 = 目录名错 / 素材没落地 ⇒ 战斗期仍会首触并留 Warn）。
            Game.Logger?.Info(LogTag,
                $"精灵预热：卡组+手牌目录 {cardDirs} 个 + 战斗特效目录 {uses.Length} 个（含重复）⇒ " +
                $"命中 {hits} / 取不到 {dirs - hits}，共 {frames} 帧；战斗期不再首触 LoadDir");
        }

        /// <summary>预热一批卡 id 的精灵目录（见 <see cref="WarmBattleSprites"/>）。⛔ 表外的卡跳过（法术卡走特效目录）。</summary>
        private static void WarmCardList(int[] cardIds, ref int dirs, ref int hits, ref int frames)
        {
            if (cardIds == null) return;
            for (var i = 0; i < cardIds.Length; i++)
            {
                CardVisual v;
                if (!CardVisuals.TryGetValue(cardIds[i], out v) || string.IsNullOrEmpty(v.Dir)) continue;
                var path = v.IsBuilding ? ResPaths.BuildingDir(v.Dir) : ResPaths.UnitDir(v.Dir);
                var f = SpriteBank.LoadDir(path, SpriteBank.SpritePivotMode.UnifiedCanvasAnchor);
                dirs++;
                if (f.Length > 0) { hits++; frames += f.Length; }
            }
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
            // 留一份"最近实体名单"给塔开火事件用（事件通道不带目标，见 `_lastEntities` 的注释）。
            // ⛔ 只存引用不拷贝：本类对它是只读的，拷贝反而每帧多一次分配。
            _lastEntities = list;
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

            // 协议没有「命中」/「开火」事件 ⇒ 这两类表现只能从快照反推（见 TrackCombatSignals）。
            // ⚠️ 放在**入历史之后**：它用的是"本帧 vs 上一帧"，与插值历史无关。
            TrackCombatSignals(s);
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
                // 位置一律取**事件自带**的 `x_milli/y_milli`：服务端在 `EvSpawn`（`combat.go:389-391`）、
                // `EvDeath`（`combat.go:428-431`）与 `EvTowerDestroyed`（`combat.go:423-426`）里都填了
                // **实体自己的位置** ⇒ 客户端无须另算。
                switch (e.kind)
                {
                    case EventKindSpawn:
                        // 出牌落地（`EvSpawn`）：在落点定格播原版 `deploy_arrows_effect`（f119）。
                        // ⛔ 单位本身不在这里生成（"单位的真假一律以快照的 entities 为准"，见上文）。
                        PlayDeploy(e);
                        break;

                    case EventKindDeath:
                        // 死亡：原版 **die 档**（出处 = `ResPaths.EffectDeathBlue` 上方的块：原版 `effects_out`
                        // 的 `Death_blue` / `Death_purple` / `death_ground`）。蓝方播蓝族、红方播紫族，
                        // 之后都跟一段地面扬尘。⛔ 不用 `Hit`（f050..f056 是命中光球，不是死亡表现）。
                        // ⚠️ 只覆盖**致死**那一击；**非致死**命中由快照 hp 下降补播（见 OnSnapshot），
                        // 两者互斥（非致死才走 hp 下降分支），⛔ 不会同一击播两次。
                        if (_effects != null)
                        {
                            var at = GameConst.MilliToWorld(e.x_milli, e.y_milli);
                            var deathUse = e.team == 0 ? ResPaths.EffectDeathBlue : ResPaths.EffectDeathPurple;
                            var deathFirst = e.team == 0 ? ResPaths.EffectDeathBlueFirst : ResPaths.EffectDeathPurpleFirst;
                            var deathCount = e.team == 0 ? ResPaths.EffectDeathBlueCount : ResPaths.EffectDeathPurpleCount;
                            _effects.Play(at, deathUse, deathFirst, deathCount, EffectsView.WorldSize);
                            _effects.Play(at, ResPaths.EffectDeathGround, ResPaths.EffectDeathGroundFirst,
                                ResPaths.EffectDeathGroundCount, EffectsView.WorldSize);
                        }
                        break;

                    case EventKindTowerDestroyed:
                        // 塔毁爆炸：在塔位置播 `Blast` 类原版帧序列（f418..f427）。
                        if (_effects != null)
                            _effects.Play(GameConst.MilliToWorld(e.x_milli, e.y_milli),
                                ResPaths.EffectBlast, ResPaths.EffectBlastFirst, ResPaths.EffectBlastCount, EffectsView.WorldSize);
                        break;

                    case EventKindPlayCard:
                        // 出牌：**法术走法术特效、远程卡走弹道**（分流见 PlayCardFx）。
                        PlayCardFx(e);
                        break;

                    case EventKindTowerShoot:
                        // 塔开火（`EvTowerShoot`）：炮口闪光 + 有速度时飞一条弹道。
                        PlayTowerShot(e);
                        break;

                    case EventKindElixirFull:
                    case EventKindTowerActivated:
                        // kind==4 圣水满 / kind==5 塔激活：**只有音效、没有特效帧**（`BattleAudioView` 播
                        // `ElixirFull` / `TowerActivate`；`ResPaths` 的特效区段里没有对应用途目录）。
                        // 显式列出这两个 case，是为了让下面的 `default` 只表示"**协议新增的未知 kind**"。
                        break;

                    default:
                        // 非预期分支：协议新增了 kind 而本类没跟上 ⇒ 留痕一次（⛔ 不静默丢弃、⛔ 不刷屏）。
                        // 对齐 `BattleAudioView.cs:241-248` 的写法。
                        if (!_unknownKindWarned)
                        {
                            _unknownKindWarned = true;
                            Game.Logger?.Warn(LogTag,
                                $"收到未知的对局事件 kind={e.kind}（协议扩了 kind？本类未接）⇒ 不播特效（只报一次）");
                        }
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
        /// 出牌落地表现：在**事件自带落点**定格播原版 `deploy_arrows_effect`（f119，见上方出处块）。
        /// <para>
        /// ⛔ 单位本身不在这里生成 —— 单位的真假一律以快照的 `entities` 为准（同 <see cref="OnBattleEventNotify"/>
        /// 的注释）；本方法只负责"特效层的那一下"。
        /// </para>
        /// </summary>
        private void PlayDeploy(BattleEvent e)
        {
            if (_effects == null) return;
            var world = GameConst.MilliToWorld(e.x_milli, e.y_milli);

            // ① 就位读条：落点处转圈，时长 = 落位期。多单位卡片的若干条 `EvSpawn` 落在同一窗口内
            //    ⇒ 看起来是一条连续的读条（每条事件都把倒计时重起，见 ShowDeployRing）。
            ShowDeployRing(WorldToTile(world), 1f, DeployFxSeconds);

            // ② 绿色落地标记：**按一次出牌去重**。`EvSpawn` 是逐单位发的（一张 3 单位的卡 = 3 条事件），
            //    而玩家看到的是"这张卡在就位"这一枚标记 —— 逐单位各画一枚就成了"每个单位头上一个绿点"。
            //    ⛔ 只影响标记的重复绘制，不影响任何实体生成（单位一律以快照为准）。
            var sameCard = e.card_id == _lastDeployCardId
                           && Time.unscaledTime - _lastDeployTime < DeployDedupeSeconds;
            if (sameCard)
            {
                _deployDupeSkipped++;
                Game.Logger?.Info(LogTag,
                    $"出牌落地标记去重（同一次出牌的后续单位）：ent={e.entity_id} card={e.card_id} " +
                    $"本局累计：落位={_deployFxPlayed} 去重={_deployDupeSkipped}");
                return;
            }
            _lastDeployCardId = e.card_id;
            _lastDeployTime = Time.unscaledTime;

            var before = _effects.SpawnedTotal;
            _effects.PlayHold(world, DeployFxDir, DeployFxFirst, EffectsView.WorldSize, DeployFxSeconds);
            if (_effects.SpawnedTotal > before) _deployFxPlayed++;
            Game.Logger?.Info(LogTag,
                $"出牌落地特效：ent={e.entity_id} team={e.team} card={e.card_id} " +
                $"落点=({world.x:F2},{world.y:F2}) 帧=f{DeployFxFirst}（{DeployFxDir}）定格={DeployFxSeconds:F2}s " +
                $"本局累计：落位={_deployFxPlayed} 去重={_deployDupeSkipped}");
        }

        /// <summary>
        /// 从**快照**里补两类"协议没有事件"的表现（位置一律取快照自带的实体坐标，⛔ 不另编）：
        /// <list type="number">
        /// <item><b>非致死命中</b>：逐 id 比对 `hp` 下降 ⇒ 在**受击者位置**播 `Hit` 闪光
        ///   （做法与 <see cref="BattleAudioView.OnSnapshot"/> 同源：协议只有 0..5 六种 `kind`、
        ///    `kind==2` 只在死亡时发 ⇒ 非致死命中没有事件可挂，见 `Def/ProtoDef.cs:199` /
        ///    `server/game/core/snapshot.go:4-11`）。致死（hp≤0）**不在这里播** —— 那一击由
        ///    `EvDeath` 分支负责（⛔ 同一击不许闪两次）。</item>
        /// <item><b>战斗中开火</b>：逐 id 判"是否处在攻击档"（`anim == 2`）且该卡为远程
        ///   （`CardInfo.projectile_key` 非空）⇒ 从**攻击者自己的位置**飞一条弹道。节拍 = 该单位的
        ///   `hit_speed`（`UnitAnimTable.Table[dir].HitSpeedMs`，与 `UnitView` 播攻击档用的是同一份表），
        ///   因为服务端的 `anim` 是**状态**不是边沿（`server/game/core/combat.go:68-73` 每次挥砍都置 2、
        ///   站桩时 `battle.go:511-515` 又不动它）⇒ 只凭"上升沿"会漏掉后续每一次挥砍。</item>
        /// </list>
        /// </summary>
        private void TrackCombatSignals(BattleSnapshot s)
        {
            var list = s.entities;
            if (list == null) return;

            _curHp.Clear();
            _curAnim.Clear();
            for (var i = 0; i < list.Length; i++)
            {
                var e = list[i];
                if (e == null) continue;
                _curHp[e.id] = e.hp;
                _curAnim[e.id] = e.anim;

                var world = GameConst.MilliToWorld(e.x_milli, e.y_milli);

                // ① 非致死命中（hp 下降且未死）。
                int prevHp;
                if (_prevHp.TryGetValue(e.id, out prevHp) && e.hp < prevHp)
                {
                    if (e.hp > 0)
                    {
                        _hpDropObserved++;
                        PlayHitFx(world, e);
                    }
                    else
                    {
                        // 致死一击：走 `EvDeath` 的闪光分支（见 OnBattleEventNotify），只在计数器上留痕。
                        _lethalDropObserved++;
                    }
                }

                // ② 战斗中开火（anim == 2 且到点）。
                if (e.anim != UnitAnimTable.Attack) continue;
                if (e.deploy_ms > 0) continue;              // 部署中不攻击（服务端 `acquirable()`：entity.go:149）

                int prevAnim;
                var rising = _prevAnim.TryGetValue(e.id, out prevAnim) && prevAnim != UnitAnimTable.Attack;

                var dir = ResolveVisual(e).Dir;
                if (string.IsNullOrEmpty(dir)) continue;
                UnitAnimTable.Entry anim;
                if (!UnitAnimTable.Table.TryGetValue(dir, out anim)) continue;
                if (anim.HitSpeedMs <= 0) continue;         // 该目录无攻击节拍（表里记 0）⇒ 只信上升沿也没法排期

                float lastShot;
                var due = rising
                    || !_lastShotMs.TryGetValue(e.id, out lastShot)
                    || (s.server_ms - lastShot) >= anim.HitSpeedMs;
                if (!due) continue;

                if (PlayBattleShot(e, list, world)) _lastShotMs[e.id] = s.server_ms;
            }

            // 清理已离场的实体（⛔ 不用 `_prevAnim`：它是双缓冲的，这里直接按本帧名单剪）。
            _shotPrune.Clear();
            foreach (var kv in _lastShotMs)
                if (!_curAnim.ContainsKey(kv.Key)) _shotPrune.Add(kv.Key);
            for (var i = 0; i < _shotPrune.Count; i++) _lastShotMs.Remove(_shotPrune[i]);

            var swapHp = _prevHp; _prevHp = _curHp; _curHp = swapHp;
            var swapAnim = _prevAnim; _prevAnim = _curAnim; _curAnim = swapAnim;
        }

        /// <summary>
        /// 在 <paramref name="world"/> 播一次 `Hit` 受击闪光。计数判据见类字段：`HitFxPlayed +
        /// HitFxSkipped == HpDropObserved`（不成立 = 有 hp 下降没播到特效 = 判红）。
        /// </summary>
        private void PlayHitFx(Vector2 world, EntitySnapshot hurt)
        {
            if (_effects == null) return;
            var before = _effects.SpawnedTotal;
            _effects.Play(world, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount, EffectsView.WorldSize);
            if (_effects.SpawnedTotal > before)
            {
                _hitFxPlayed++;
                if (_hitFxPlayed <= HitLogLimit)
                    Game.Logger?.Info(LogTag,
                        $"命中特效（非致死）：ent={hurt.id} team={hurt.team} hp={hurt.hp}/{hurt.max_hp} " +
                        $"位置=({world.x:F2},{world.y:F2}) 前 n={HitLogLimit} 条逐条记录，其后只计数（本局累计={_hitFxPlayed}）");
            }
            else
            {
                _hitFxSkipped++;
            }
        }

        /// <summary>
        /// 战斗中开火：从**攻击者自己的位置**向"最近的合法敌方实体"飞一条原版弹道。
        /// <para>
        /// <b>目标怎么选</b>：镜像服务端的索敌口径 —— <c>acquireTarget</c> 取**最近**的合法目标
        /// （`server/game/core/targeting.go:70-93`：先过 <c>canTarget</c>（不同队 / 存活 / 不在部署中 /
        /// 空中地面匹配），再取 hitbox 间隙最小、id 小者优先）。客户端没有半径数据，用**中心距**近似
        /// 取最近（本项目口径，登记在报告里）。
        /// </para>
        /// <para>
        /// ⚠️ **契约缺口（登记见 `策划/差异登记.tsv`）**：协议里没有"开火"事件、`EvPlayCard` 的 `entity_id` 恒 0
        /// （`server/game/core/battle.go:219-221`）⇒ 客户端既拿不到施法者、也拿不到真实目标，
        /// 只能这样反推。正解 = 服务端为每次开火发一条带 `caster_entity_id` + `target_entity_id` 的事件
        /// （⛔ 客户端不改服务端协议）。
        /// </para>
        /// </summary>
        /// <returns>真的播出去了才返回 true（用于记录该实体的下一次开火时刻）。</returns>
        private bool PlayBattleShot(EntitySnapshot attacker, EntitySnapshot[] all, Vector2 from)
        {
            if (_effects == null) return false;

            CardInfo card;
            if (!_cards.TryGetValue(attacker.card_id, out card) || card == null)
            {
                if (!_fireCardMissingWarned)
                {
                    _fireCardMissingWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"开火弹道：卡 {attacker.card_id} 不在已收到的卡池里 ⇒ 判不出是否远程、跳过（只报一次）");
                }
                return false;
            }
            if (string.IsNullOrEmpty(card.projectile_key)) return false; // 近战：服务端直接 applyHit，没有弹道
            if (card.proj_speed <= 0) return false;                     // 无速度 ⇒ 算不出飞行时长

            var target = NearestEnemy(all, attacker);
            if (target == null)
            {
                if (!_fireNoTargetWarned)
                {
                    _fireNoTargetWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"开火弹道：ent={attacker.id}（card={attacker.card_id}）在射程态但本帧找不到任何合法敌方实体 ⇒ 不播弹道（只报一次）");
                }
                return false;
            }

            var to = GameConst.MilliToWorld(target.x_milli, target.y_milli);
            var dist = Vector2.Distance(from, to);
            if (dist <= 0f) return false;
            var seconds = dist * 60f / card.proj_speed; // 格 ÷ (格/分钟 ÷ 60)，与出牌弹道同一口径

            var before = _effects.SpawnedTotal;
            _effects.PlayFlight(from, to, ResPaths.EffectArrow, ResPaths.EffectArrowFirst, ResPaths.EffectArrowCount,
                EffectsView.WorldSize, seconds);
            if (_effects.SpawnedTotal <= before) return false;

            _battleShots++;
            Game.Logger?.Info(LogTag,
                $"战斗中开火弹道：ent={attacker.id}（card={attacker.card_id} key={card.projectile_key}）" +
                $"从攻击者位置=({from.x:F2},{from.y:F2}) 飞向最近敌方 ent={target.id} =({to.x:F2},{to.y:F2}) " +
                $"距离={dist:F2}格 飞行={seconds:F3}s 本局累计={_battleShots}");
            return true;
        }

        /// <summary>
        /// 塔开火（`EvTowerShoot` / `kind == 6`）的一次性表现：**炮口闪光 + 有速度时飞一条弹道**。
        ///
        /// <para>
        /// <b>与 <see cref="PlayBattleShot"/> 的分工</b>：那条是"从快照反推单位开火"（协议没有单位开火事件，
        /// 见 D48）；这条是**服务端明确发来的**塔开火事件，位置与速度都是真值 —— 塔的射击链路
        /// （国王塔 + 公主塔）的攻击特效就是靠它播出来的。
        /// </para>
        ///
        /// <para>
        /// <b>炮口怎么取</b>：优先问 <see cref="ArenaView.TryTowerMuzzle"/> —— 塔贴图里"炮塔"是**独立一层**
        /// （`Princess` / `Turret`），用塔根坐标会把闪光画在塔底座、看起来像没开枪。取不到才退回事件自带的
        /// 塔根坐标（`x_milli/y_milli`，服务端 `emitTowerShoot` 填的就是塔根）并**留痕一次**。
        /// </para>
        ///
        /// <para>
        /// <b>目标怎么来</b>：事件载荷**只有塔自己**、没有目标（`Event.ProjSpeed` 的注释里写明）。
        /// 故用**最近一帧快照**近似取"该塔阵营的最近合法敌方"，口径与 <see cref="NearestEnemy"/> 完全一致。
        /// 快照还没到（进对局第一帧就开火）⇒ 取不到目标 ⇒ 只播枪口闪光，不编造飞行方向。
        /// </para>
        ///
        /// <para>
        /// <b>速度口径</b>：`proj_speed` 单位 = 格/分钟，同官方 `cards_stats_projectile.json` 的 `speed`
        /// ⇒ <c>飞行秒数 = 距离(格) × 60 / proj_speed</c>（与出牌弹道 / 单位开火同一口径）。
        /// `proj_speed &lt;= 0`（该塔没有投射物或投射物表缺速度）⇒ 只播枪口闪光。
        /// </para>
        ///
        /// <para>
        /// <b>计数自检</b>：<c>TowerShots == TowerShotFlights + TowerShotMuzzles + TowerShotSkipped</c>
        /// —— 不成立 = 有开火事件没被任何一条路径处理（判红）。
        /// </para>
        /// </summary>
        private void PlayTowerShot(BattleEvent e)
        {
            _towerShots++;

            // ① 炮口位置：塔视图的炮口层 → 事件自带的塔根坐标（兜底 + 留痕）。
            //    先填塔根坐标再让 TryTowerMuzzle 覆写：`out` 形参一定被方法赋值，这样即使
            //    `_arena != null` 为假（短路、不调用方法）也不会留下"可能未赋值"的变量
            //    —— 若先声明 `Vector2 muzzle;` 再用 `&&` 短路调用，编辑器会报
            //    `CS0165 Use of unassigned local variable 'muzzle'`。
            Vector2 muzzle = GameConst.MilliToWorld(e.x_milli, e.y_milli);
            // 先按**队伍 + 事件坐标**对号（塔实体 id 实测恒为 0 ⇒ 按 id 永远匹配不上，见
            // ArenaView.TryTowerMuzzle 的注释），再退回按 id。
            var fromMuzzleLayer = _arena != null
                && (_arena.TryTowerMuzzle(e.x_milli, e.y_milli, e.team, out muzzle)
                    || _arena.TryTowerMuzzle(e.entity_id, out muzzle));
            if (!fromMuzzleLayer && !_towerMuzzleMissingWarned)
            {
                _towerMuzzleMissingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"塔开火：塔 id={e.entity_id} team={e.team} 在塔视图里找不到炮口层（塔视图未建 / 坐标对不上）⇒ " +
                    $"用事件自带的塔根坐标 ({muzzle.x:F2},{muzzle.y:F2}) 兜底（炮口闪光看起来会偏到塔底）（只报一次）");
            }

            if (_effects == null) { _towerShotSkipped++; return; }

            // ② 目标：事件不带 ⇒ 用最近一帧快照近似（同 `NearestEnemy` 口径）。取不到就不编方向。
            var target = _lastEntities == null
                ? null
                : NearestEnemy(_lastEntities, e.team, e.entity_id, e.x_milli, e.y_milli);

            // ③ 炮口闪光：无论有没有飞行段都该有（这是"塔开了枪"的那一下）。
            var beforeHit = _effects.SpawnedTotal;
            _effects.Play(muzzle, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount,
                EffectsView.WorldSize);
            var hitPlayed = _effects.SpawnedTotal > beforeHit;

            // ④ 飞行段：只有"有速度 且 有目标 且 距离>0"三个条件齐了才飞。
            if (e.proj_speed > 0 && target != null)
            {
                var to = GameConst.MilliToWorld(target.x_milli, target.y_milli);
                var dist = Vector2.Distance(muzzle, to);
                if (dist > 0f)
                {
                    var seconds = dist * 60f / e.proj_speed;
                    var beforeFlight = _effects.SpawnedTotal;
                    _effects.PlayFlight(muzzle, to, ResPaths.EffectArrow, ResPaths.EffectArrowFirst,
                        ResPaths.EffectArrowCount, EffectsView.WorldSize, seconds);
                    if (_effects.SpawnedTotal > beforeFlight)
                    {
                        _towerShotFlights++;
                        Game.Logger?.Info(LogTag,
                            $"塔开火弹道：塔 id={e.entity_id} team={e.team} 炮口=({muzzle.x:F2},{muzzle.y:F2})" +
                            $"{(fromMuzzleLayer ? "" : "（兜底：塔根）")} 飞向最近敌方 ent={target.id} " +
                            $"=({to.x:F2},{to.y:F2}) 距离={dist:F2}格 速度={e.proj_speed}格/分 飞行={seconds:F3}s " +
                            $"本局累计：开火={_towerShots} 飞行={_towerShotFlights} 闪光={_towerShotMuzzles} 跳过={_towerShotSkipped}");
                        return;
                    }
                }
            }

            // ⑤ 降级：只播枪口闪光（无速度 / 无目标 / 飞行素材为空）。两段都没播 = 计入跳过。
            if (hitPlayed) _towerShotMuzzles++;
            else _towerShotSkipped++;
        }

        /// <summary>
        /// 本帧快照里离 <paramref name="self"/> 最近的**合法**敌方实体（镜像服务端 `canTarget` 的硬条件：
        /// 不同队 / 存活（hp&gt;0）/ 不在部署中；`server/game/core/targeting.go:16-52, 76-93`）。
        /// ⛔ 客户端没有半径 / 空中地面 / only_* 这些字段，故只做这几条可得条件（登记为近似口径）。
        /// </summary>
        private static EntitySnapshot NearestEnemy(EntitySnapshot[] all, EntitySnapshot self)
        {
            return NearestEnemy(all, self.team, self.id, self.x_milli, self.y_milli);
        }

        /// <summary>
        /// 同上，但**不需要一个快照实体当"自己"** —— 供塔开火用（`EvTowerShoot` 的位置来自事件载荷，
        /// 塔在快照里也可能已被查表删掉）。口径与快照重载逐条相同。
        /// </summary>
        private static EntitySnapshot NearestEnemy(EntitySnapshot[] all, int team, int selfId, int xMilli, int yMilli)
        {
            if (all == null) return null;
            EntitySnapshot best = null;
            float bestSq = 0f;
            for (var i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || c.id == selfId) continue;
                if (c.team == team) continue;
                if (c.hp <= 0) continue;
                if (c.deploy_ms > 0) continue;                 // 服务端 `acquirable()`：部署中不可被选中
                var dx = (float)(c.x_milli - xMilli);
                var dy = (float)(c.y_milli - yMilli);
                var d2 = dx * dx + dy * dy;
                if (best == null || d2 < bestSq) { best = c; bestSq = d2; }
            }
            return best;
        }

        /// <summary>
        /// 出牌事件（`EvPlayCard`）的表现**分流**：法术卡 → <see cref="PlaySpellFx"/>，
        /// 其余（远程卡）→ <see cref="PlayProjectileFlight"/>。
        ///
        /// <para>
        /// <b>为什么必须分流</b>（差异登记见 `策划/差异登记.tsv`）：这两条路的判据是**反的** ——
        /// <see cref="PlayProjectileFlight"/> 只认"`projectile_key` 非空的远程卡"，
        /// 而法术卡的 `projectile_key` **必然为空**（法术没有弹体）⇒ 一张法术打下去会
        /// **在 `PlayProjectileFlight` 第一行就被 return 掉，画面上什么都没有**。
        /// </para>
        ///
        /// <para>
        /// <b>判据取哪一列</b>：用 `CardInfo.type`（`Def/ProtoDef.cs`：`0=部队 1=法术 2=建筑`，
        /// 与服务端 `core/card.go:38-42` 的 `CardTypeSpell = 1` 同一套编号），
        /// ⛔ 不用"`projectile_key` 为空"当法术判据 —— 近战部队（骑士、皮卡）的 `projectile_key`
        /// 也是空的，那样会把近战当法术播特效。卡池里查不到 `card_id` 时落回
        /// <see cref="PlayProjectileFlight"/>（由它留那条"卡池没到"的痕）。
        /// </para>
        /// </summary>
        private void PlayCardFx(BattleEvent e)
        {
            CardInfo card;
            if (_cards.TryGetValue(e.card_id, out card) && card != null && card.type == CardTypeSpell)
            {
                PlaySpellFx(e, card);
                return;
            }
            PlayProjectileFlight(e);
        }

        /// <summary>
        /// 法术卡的命中表现：在**事件自带的落点**播该法术的原版帧序列（帧来源见 <see cref="SpellFxTable"/>）。
        ///
        /// <para>
        /// <b>落点取真值</b>：`EvPlayCard` 的 `x_milli/y_milli` 就是玩家点的那一点
        ///（服务端 `core/battle.go:219-221` 原样填），⛔ 不做任何"往塔中心靠"之类的降级。
        /// </para>
        ///
        /// <para>
        /// <b>尺寸取原版 1:1</b>：传 <see cref="EffectsView.WorldSize"/> ⇒ `EffectsView` 里
        /// `scale = size / WorldSize = 1` ⇒ 精灵按美术自己的像素尺寸（PPU=100）落位。
        /// 这与既有 `Hit` / `Blast` / `Arrow` 三个用途目录同一口径，⛔ 不按"法术半径"另算一个缩放
        /// （客户端没有半径数据；要按半径缩放得先让服务端随卡池下发，那是另一片）。
        /// </para>
        ///
        /// <para>
        /// <b>计时自检</b>：<c>SpellCasts == SpellFxPlayed + SpellFxSkipped</c>
        /// —— 不成立 = 有法术出牌没走到这里（判红）。
        /// </para>
        /// </summary>
        private void PlaySpellFx(BattleEvent e, CardInfo card)
        {
            _spellCasts++;

            SpellFxTable.Entry fx;
            if (!SpellFxTable.TryGet(card.key, out fx))
            {
                _spellFxSkipped++;
                if (!_spellFxUnknownWarned)
                {
                    _spellFxUnknownWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"法术卡 key=\"{card.key}\"（id={e.card_id}）在 SpellFxTable 里没有帧位 ⇒ 不播特效。" +
                        "补法：在 策划/单位动画分组表.md 的 effects 小节查到该法术的分组，" +
                        "按行号加进 SpellFxTable + ResPaths + .ai-tmp/hosts/copy_spell_fx.py（只报一次）");
                }
                return;
            }

            if (_effects == null) { _spellFxSkipped++; return; }

            var world = GameConst.MilliToWorld(e.x_milli, e.y_milli);
            var before = _effects.SpawnedTotal;
            _effects.Play(world, fx.Use, fx.First, fx.Count, EffectsView.WorldSize);
            if (_effects.SpawnedTotal > before)
            {
                _spellFxPlayed++;
                Game.Logger?.Info(LogTag,
                    $"法术命中特效：card={e.card_id} key={card.key} team={e.team} " +
                    $"落点=({world.x:F2},{world.y:F2}) 帧=f{fx.First}..f{fx.First + fx.Count - 1}" +
                    $"（{fx.Use}，{fx.Count} 帧）本局累计：出牌={_spellCasts} 播出={_spellFxPlayed} 跳过={_spellFxSkipped}");
            }
            else
            {
                // 非预期分支：素材目录没落地（帧文件缺失 / 没被 Unity 导入）⇒ 留痕并计数（⛔ 不静默丢）。
                _spellFxSkipped++;
                if (!_spellFxUnknownWarned)
                {
                    _spellFxUnknownWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"法术特效播不出去：用途目录 \"{fx.Use}\" 在 Resources 里取不到帧（首帧 f{fx.First}）⇒ " +
                        "检查 .ai-tmp/hosts/copy_spell_fx.py 是否跑过、以及新 PNG 是否已被 Unity 导入（只报一次）");
                }
            }
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
        /// <b>「施法者位置」怎么取</b>：事件载荷（`BattleEvent`）只有单点 `x_milli/y_milli` + `team`，
        /// 且 `EvPlayCard` 的 `entity_id` 恒为 0（`server/game/core/battle.go:219-221` 未填 `EntityID`）
        /// ⇒ **载荷里没有施法者**。这里不再退化成"本方国王塔中心"（那是 D48 的降级，用户看到的是
        /// "箭从塔里射出"），而是取**本方在落点附近最近的一个单位**（通常就是刚落下的那张卡自己）。
        /// 都取不到（例如全场本方无单位）时才回退国王塔中心并**留痕**。
        /// **落点 = 事件自带的 `x_milli/y_milli`（真值，未降级）。**
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

            var from = CasterWorld(e.team, landing);
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
                $"施法者[本方最近单位，见 CasterWorld]=({from.x:F2},{from.y:F2}) 落点=({landing.x:F2},{landing.y:F2}) " +
                $"距离={dist:F2}格 飞行={seconds:F3}s（={dist:F2}×60/{card.proj_speed}） 本局累计={_projectileShots}");
        }

        /// <summary>
        /// 出牌方「施法者」世界位置。**优先 = 本方在 <paramref name="landing"/> 附近最近的一支单位**
        /// （用当前插值窗口末尾那一帧快照的实体坐标；通常是刚落下的那张卡自己）；
        /// 本方在该帧**一个单位都没有**时，才回退到**出牌方国王塔中心**（固定几何
        /// `GameConst.KingTowerTileX/Y`，出处 `anchors.json`）并留痕一次。
        ///
        /// <para>
        /// <b>为什么不再无条件用国王塔中心</b>：那是 D48 登记的降级值 —— 现象是"箭从本方塔里射出来"。
        /// 事件载荷不带施法者（`BattleEvent` 只有单点 + `team`，`EvPlayCard` 的 `entity_id` 恒 0，
        /// 见 `server/game/core/battle.go:219-221`），但快照里**有**本方实体的实时坐标 ⇒ 可以退而取
        /// "离落点最近的本方单位"，比国王塔贴合实际。真正正解 = 服务端在事件里补 `caster_entity_id`（D48）。
        /// </para>
        /// </summary>
        private Vector2 CasterWorld(int team, Vector2 landing)
        {
            EntitySnapshot best = null;
            var bestSq = 0f;
            foreach (var kv in _curIndex)
            {
                var c = kv.Value;
                if (c == null || c.team != team || c.hp <= 0) continue;
                var cw = GameConst.MilliToWorld(c.x_milli, c.y_milli);
                var dx = cw.x - landing.x;
                var dy = cw.y - landing.y;
                var d2 = dx * dx + dy * dy;
                if (best == null || d2 < bestSq) { best = c; bestSq = d2; }
            }

            if (best != null)
                return GameConst.MilliToWorld(best.x_milli, best.y_milli);

            if (!_casterFallbackWarned)
            {
                _casterFallbackWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"出牌弹道：team={team} 在落点({landing.x:F2},{landing.y:F2})附近**找不到任何本方单位** ⇒ " +
                    "施法者退化为本方国王塔中心（D48 的降级值）；若持续出现说明出牌与单位落地不在同一帧快照里（只报一次）");
            }
            return GameConst.TileToWorld(GameConst.KingTowerTileX,
                GameConst.MirrorTileYForTeam(GameConst.KingTowerTileY, team));
        }

        private void OnBattleEnded(BattleEndNotify r)
        {
            if (r == null) return;
            // 结算面板由结算流程负责；表现层**不拆画面**（结算要盖在最后一帧场面上）。
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
        /// **连续**的"服务端现在"（毫秒）：把**最新**快照的时间戳按"它到达后过了多少真实时间"外推，
        /// 但外推量**有上限** <see cref="ServerNowExtrapCapMs"/>。
        /// <para>
        /// 用途只剩"灾难级偏差的恢复判据"（见 <see cref="TickRender"/> 第 ② 步）：
        /// 时钟的**稳态推进不引用它**，于是到达时刻的抖动（网络抖动、编辑器卡顿）不会通过它
        /// 调制渲染速度 —— 那正是"速度脉动"的传播路径，已实测（离线仿真：速率被 ±15% 调制时
        /// 速度 cv 0.24~0.28，而速率恒 1 时 cv 0.000）。
        /// </para>
        /// <para>
        /// <b>为什么必须给外推封顶</b>（实机取证）：快照**停推**（对局结束 / 断线 /
        /// 服务端不再发）之后，下面是"`_newestMs` + 无限外推"，于是"服务端现在"**一直往前走**，
        /// 渲染时钟就跟着它跑飞 —— 实测 `newest − renderMs` 中位数 = **−50785 ms**（时钟比最新快照
        /// **超前 50.8 s**；时钟表里 12902 行有 **7891 行 `t` 被夹成 1.0**）。一旦快照恢复，
        /// 时钟要按 ±5% 的速率把 50 s 的偏差拉回来要上千秒 ⇒ 那段时间单位全部冻在最后一帧。
        /// 封顶后"没有新数据就不许发明时间"：停推时停在"最新 + 1 个间隔"，恢复到目标落后量的
        /// 偏差只有几百毫秒，比例修正 1~2 秒内就收敛。
        /// </para>
        /// </summary>
        private float ServerNowMs
        {
            get
            {
                var extrap = (Time.realtimeSinceStartup - _arrivalReal) * 1000f;
                return _newestMs + Mathf.Min(extrap, ServerNowExtrapCapMs);
            }
        }

        /// <summary>
        /// "服务端现在"允许比最新快照**最多**超前多少毫秒（= 2 个快照间隔）。
        /// 取 2 个间隔：正常的到达抖动（丢 1 个包 = 1 个间隔）必须能被外推吸收掉，
        /// 而 ≥2 个间隔的沉默已经不是抖动、是"停推"，此时继续外推只会让时钟跑飞。
        /// </summary>
        private const float ServerNowExtrapCapMs = 2f * GameConst.SnapshotIntervalMs;

        /// <summary>
        /// 渲染时钟允许比**最新快照**最多超前多少毫秒（= 1 个快照间隔）。
        /// 判据出处见 <see cref="TickRender"/> 第 ②′ 步：停推时时钟会一路跑飞（实测超前 50.8 s），
        /// 而用 ±5% 速率把它拉回来要上千秒 ⇒ 单位长期冻住。取 1 个间隔 = 刚好够吸收"丢 1 个包"
        /// 且不允许凭空多出更多"未来时间"。
        /// </summary>
        private const float ClockMaxLeadMs = 1f * GameConst.SnapshotIntervalMs;

        /// <summary>时钟超前上限告警是否已报过（只报一次，⛔ 不刷屏）。</summary>
        private bool _clockLeadCapped;

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
        /// ⛔ 第 ③ 步是插值稳定的核心：**换窗口的时刻由时钟决定，⛔ 不由包到达决定**。
        /// ⛔ 不"把时钟夹到 `_currMs`"：那是"每帧都贴着窗口末端"的写法，会把时钟**永久钉死**
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

            // ② 速率：**有界比例修正**（稳态 ±5%，灾难级偏差时放开到 <see cref="CatchUpMaxRate"/>）。
            //    ⛔ 不能写成 `|err| > CatchUpThresholdMs ? SteerRate(err) : 1f` —— 那个门控 = "300 ms 以内
            //    一律不校正"，而时钟的落后量一旦被成批到达的快照推过 300 ms（实测稳定在 417 ms）就**再也
            //    回不来**：它会一直跑在快照历史之外 ⇒ SelectWindow 夹到最旧一对 ⇒ t 恒为 0 ⇒ **插值静默失效**
            //    （实测：7953 帧里 t 只有 3 帧取到中间值，单位每 100 ms 跳一格）。比例项必须常开，
            //    偏差自己衰减到 0（一阶，τ≈4 s），插值窗口始终处于"被时钟跨过"的正常状态。
            //    为什么走速率而不是"直接把时钟瞬跳到目标"：位置的导数就是速度，瞬跳 = 单位前跳一大截。
            var err = (ServerNowMs - TargetLagMs) - clock;
            var rate = SteerRate(err);
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

            // ②′ 时钟**超前上限**（"没有新数据就不许发明时间"）。
            //    为什么必须有：快照**停推**（对局结束 / 断线 / 服务端不再推）之后时钟会跟着
            //    `ServerNowMs` 一路往前走，而把 50 s 的偏差用 ±5% 的速率拉回来要上千秒
            //    ⇒ 这段时间单位全部冻在最后一帧。实测（第二轮逐帧表）：`newest − renderMs`
            //    中位数 = **−50785 ms**（超前 50.8 s），12902 行里 7891 行 `t` 被夹成 1.0。
            //    ⛔ 这个夹取**不产生画面跳变**：夹取前后 `clock` 都在最新快照之后 ⇒
            //    `SelectWindow` 两边都把 `t` 夹成 1 ⇒ 位置完全相同（本来就冻着）。
            //    ✅ 而且恢复是**立刻**的：上限挂在 `_newestMs` 上，快照一恢复 `_newestMs` 就前进，
            //    上限跟着松开（不需要靠速率追）。
            if (_histLen >= 1)
            {
                var leadCap = _newestMs + ClockMaxLeadMs;
                if (clock > leadCap)
                {
                    if (!_clockLeadCapped)
                    {
                        _clockLeadCapped = true;
                        Game.Logger?.Warn(LogTag,
                            $"渲染时钟超前最新快照 {clock - _newestMs:F0}ms（上限 {ClockMaxLeadMs:F0}ms = " +
                            $"{ClockMaxLeadMs / GameConst.SnapshotIntervalMs:F0} 个间隔）⇒ 就地锚回并保持；" +
                            "成因：快照停推（对局结束 / 断线 / 服务端不再推）。⛔ 不是把时钟永久钉死：" +
                            "上限跟着 `_newestMs` 走，快照一恢复立即松开（只报一次）");
                    }
                    // 就地锚回（锚在**绝对**值 leadCap 上，⛔ 不是 `SetClockRate` —— 那个会锚在
                    // 未夹取的 `RenderClockMs` 上，等于没夹）。速率不动（仍是比例修正给的那个）。
                    _clockBaseMs = leadCap;
                    _clockBaseReal = real;
                    clock = leadCap;
                }
                else if (clock < _newestMs - ClockMaxLeadMs)
                {
                    _clockLeadCapped = false;   // 回到正常范围 ⇒ 复位告警（下次停推还能再报一次）
                }
            }
            _renderMs = clock;

            // ③④ 按时钟选窗口 + 算 t：窗口边界只在 t=1 那一刻跨过 ⇒ 位置连续、速度恒定。
            var t = SelectWindow(_renderMs);
            _hasPrev = _histLen >= 2;

            // ③′ 脱窗不变量（**必须留痕**）：`SelectWindow` 在时钟早于最旧快照时会夹到最旧一对、
            //     使 `t` 恒为 0 —— 这一刻**插值已经死了**，但画面上只是"动得一顿一顿"，不会报任何错。
            //     这是"最隐蔽的静默失效"，所以显式记一次（只报一次，⛔ 不刷屏）。
            if (_histLen >= 2)
            {
                var oldest = _msHist[HistSlots - _histLen];
                var newest = _msHist[HistSlots - 1];
                if (_renderMs < oldest || _renderMs > newest)
                {
                    if (!_outOfWindowWarned)
                    {
                        _outOfWindowWarned = true;
                        Game.Logger?.Warn(LogTag,
                            $"渲染时钟 {_renderMs:F0}ms 掉出快照历史 [{oldest:F0}, {newest:F0}]ms " +
                            $"（落后最新 {newest - _renderMs:F0}ms，历史覆盖 {HistSlots - 1} 个间隔 = " +
                            $"{(HistSlots - 1) * GameConst.SnapshotIntervalMs}ms）⇒ 插值被夹成阶梯（t≡0/1）；" +
                            $"速率 {_clockRate:F3}× 会把它拉回目标 {TargetLagMs:F0}ms（只报一次）");
                    }
                }
                else
                {
                    _outOfWindowWarned = false;
                }
            }

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
                // 朝向用的**基线位移**：从"历史里最旧的那个含该 id 的快照"量到位移（HistSlots=6 ⇒ 默认 5 个间隔
                // = 500 ms），⛔ 不是只量**一个**间隔。
                // 为什么必须拉长基线（实测 id=9 帧 1903..1912 的逐帧表）：
                //   一队亡灵沿纵向推进时，真实前进是 y 方向 0.05~0.07 格/帧；但"互相推开"的解算器
                //   在 x 方向持续给 ±0.03 格/帧 的**微推挤**。只量一个 100 ms 间隔时，两者同量级 ⇒
                //   位移向量的极角在 45° 档位边界附近来回越界 ⇒ 视角档 8→7→8 各翻 3 帧 = 视觉上的
                //   "抽搐"（判据 A4）。把基线拉到 500 ms 后，推挤是**往复**的、大部分互相抵消，
                //   而行进是**单调**的、线性累积 ⇒ 极角稳定落在 8 档内。
                // ⛔ 也不能交给 UnitView 自己按逐帧插值位置去推：服务端坐标是**毫格**量化的，
                //    站立单位的逐帧位移在 ±0.002 格之间抖，方向会被量化噪声翻 180°
                //   （实测见 UnitView.FacingMinMoveTiles 的注释与 id=320 的逐帧表）。
                var moveDir = Vector2.zero;
                if (_hasPrev)
                {
                    EntitySnapshot p;
                    if (_prevIndex.TryGetValue(e.id, out p) && p != null)
                    {
                        var prevWorld = GameConst.MilliToWorld(p.x_milli, p.y_milli);
                        var baselineWorld = prevWorld;
                        for (var i = HistSlots - _histLen; i < HistSlots - 1; i++)
                        {
                            EntitySnapshot q;
                            if (_idxHist[i].TryGetValue(e.id, out q) && q != null)
                            {
                                baselineWorld = GameConst.MilliToWorld(q.x_milli, q.y_milli);
                                break;
                            }
                        }
                        moveDir = world - baselineWorld;
                        world = Vector2.Lerp(prevWorld, world, t);
                    }
                }

                view.Apply(e, world, e.deploy_ms > 0 ? DeployAlpha : 1f, moveDir);
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

        /// <summary>
        /// 显示落点指示（合法绿 / 非法红）+ 落点处的**卡面虚影**。HUD 在**每次指针移动**时调一次即可。
        /// </summary>
        /// <param name="cardArt">
        /// 正在拖的那张卡的**卡面**（由 HUD 从手牌卡面直接给出）；<c>null</c> = 不显示虚影。
        /// ⛔ 不用任何兜底图形顶替 —— 取不到卡面时宁可不显示（否则又变成"一个小图标"，见 `_dropCard`）。
        /// </param>
        public void ShowPlacement(Vector2 tileXY, float radiusTiles, bool isSpell, Sprite cardArt = null)
        {
            if (_indicator == null) return;
            _indicator.SetDropCard(cardArt);
            _indicator.Show(tileXY, radiusTiles, IsDeployLegal(tileXY, isSpell));
        }

        /// <summary>
        /// 落点处的**就位读条**：出牌落位期间在落点转圈（时长 = 服务端 `deploy_time`）。
        /// 由 <see cref="PlayDeploy"/> 调用 —— 它只表示"这张卡在就位"，⛔ 不表示落点是否合法。
        /// </summary>
        public void ShowDeployRing(Vector2 tileXY, float radiusTiles, float seconds)
        {
            if (_indicator == null) return;
            _indicator.ShowDeployRing(tileXY, radiusTiles, seconds);
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
        /// 这是**已知契约缺口**（正解 = 服务端把 `sprite_dir` 一并下发）。
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
