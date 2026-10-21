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
    /// <h4>三、插值：整条时间轴链委托引擎件 <c>SnapshotInterpolator</c></h4>
    /// 服务端 10 Hz、渲染 ~30~60 FPS 的「低频权威快照 → 高帧率插值表现」由引擎件
    /// `CloverEngine.SnapshotInterpolator`（`clover-client-unity-engine/Runtime/Presentation/SnapshotInterpolator.cs`）
    /// 承担：**渲染时钟推进 + 快照历史 + 插值窗口选择 + 有界比例速率修正**四件事全在它内部。
    /// 本类只做三处接缝（见 <see cref="OnSnapshot"/> / <see cref="TickRender"/> / <see cref="ApplyEntities"/>）：
    /// <list type="bullet">
    /// <item>收到快照 ⇒ `Interp.Push(s.server_ms, 载荷格)`：载荷是"一格 <c>id → EntitySnapshot</c> 字典"，
    ///   由 <see cref="_freeSlots"/> 复用被挤出历史的那一格，⛔ 不每帧新建；</item>
    /// <item>每帧 ⇒ <c>t = Interp.Tick()</c>；</item>
    /// <item>摆单位 ⇒ 用 <c>t</c> 在 `Interp.WindowStartPayload` / `WindowEndPayload` 之间 Lerp，
    ///   朝向基线读更早的格 `Interp.PayloadAt(0 .. HistoryLength-2)`。</item>
    /// </list>
    /// <para>
    /// <b>参数</b>（<see cref="InterpOptions"/>）：快照间隔取 <c>GameConst.SnapshotIntervalMs</c>（100 ms = 10 Hz）口径；
    /// 历史 6 格、渲染落后最新快照 2 个间隔（= 抖动缓冲 200 ms）、稳态速率微调 ±5%、
    /// 灾难级偏差时放开到 2× 真实时间做有界追帧。
    /// </para>
    /// <para>
    /// ⛔ <b>不直接把快照坐标贴到 Transform</b>：那等于每 100 ms 瞬移一次，单位看起来是"一格一格跳"；
    /// 而"显示服务端时间轴上稍早一点的位置"在联机对战里没有可感知代价（对手看到的是同样的落后量），
    /// 却把 10 Hz 变成视觉上的连续运动。
    /// </para>
    /// <para>
    /// <b>代价（明知）</b>：画面比服务端晚 2 个快照间隔（本项目 200ms）。这是"绝不前跳"换来的代价，
    /// 刻意如此：前跳会瞬间把单位推过头（甚至穿过墙）。允许多付的只是**秒级的 ±5% 速率微调**
    /// （把落后量自己走回 200ms），不是位置瞬跳。
    /// </para>
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
        // 素材帧号出处（⛔ 不许挑帧）与落位特效的用途目录 / 起始帧 / 帧数 = `ResPaths.EffectDeploy` /
        // `ResPaths.EffectDeployFirst` / `ResPaths.EffectDeployCount`（唯一定义处 = `Core/ResPaths.cs`，
        // 出处块见那里的 `策划/单位动画分组表.md:5021`：`deploy_arrows_effect` / clip 354 / 60 fps /
        // timeline 16 条**全部指向同一像素帧 f119**；档位列 = spawn）。
        // 为什么不用另一个 spawn 候选 `filter_deploy_unit_*`（同表 `:5149-5153`，唯一像素帧 f258）：
        //   f258 的像素内容 = **一个具体单位的彩色形象**（描出的是哥布林本体，非通用剪影/掩膜），
        //   而 `filter_deploy_unit_default` / `filter_cold` / `filter_damage_*` / `filter_hologram` /
        //   `filter_poison` … **十几个不同滤镜共用同一个 f258**（`策划/单位动画分组表.md:5143-5167`）
        //   ⇒ f258 是"被叠加到单位身上的滤镜底图"，把它当**独立落地特效**播会在任意卡落点上画出一个哥布林。
        //   故取 f119（真正的独立 spawn 特效：绿色上箭头）。

        /// <summary>
        /// 落地绿箭头（f119）的**定格时长**（秒）= 该 export 自己的时间轴长度
        /// （`策划/单位动画分组表.md:5021`：timeline 16 条 ÷ 60 fps ≈ 0.267 s）。
        /// <para>f119 是静止箭头（该 export 的 16 条 timeline 指向同一像素帧）⇒「它播多久」就是这条
        /// 时间轴多长，故按 0.267 s 定格（见 <see cref="PlayDeploy"/>）。</para>
        /// </summary>
        private const float DeployArrowSeconds = 16f / 60f;

        /// <summary>
        /// 落位计时件时长的**兜底**秒数（正常路径取该卡下发的 `CardInfo.deploy_ms`，见 <see cref="DeploySecondsOf"/>）。
        /// <para>出处 = `策划/策划案/皇室战争参考规格.md:156`「落点后 `deploy_time`（官方普遍 1000ms）
        /// 才出现并可行动」，当前 20 张卡的 `deploy_ms` 全是 1000 ⇒ 兜底值与真实值同长。</para>
        /// </summary>
        private const float DeployPeriodFallbackSeconds = 1.0f;

        /// <summary>
        /// 死亡特效三段的原版帧率（`策划/单位动画分组表.md:4697-4700` 的 fps 列：`death_ground` /
        /// `death_particle` / `Death_blue` / `Death_purple` 四条 export 全为 30）。
        /// 整段时长 = 该族 export 的 **timeline 帧数 ÷ 它**（见下面四个常量与
        /// <see cref="OnBattleEventNotify"/> 的死亡分支）。
        /// </summary>
        private const float DeathFxFps = 30f;

        // ── 死亡特效三段的原版时长（两条口径，别混）──
        // 出处 = `策划/单位动画分组表.md:4697-4700` 的「FPS / timeline 帧数」列 + 逐 clip 行同表 `:4961-4974`；fps 全 = 30。
        // ① **扬尘 / 粒子**：timeline 帧数就是它自己那一段（`death_ground_elixir1..3` clip 215..211 timeline 21、
        //    `death_particle_elixir1..2` clip 209..207 timeline 21）⇒ 时长 = 21 ÷ 30 = 0.700 s。
        // ② **闪族（蓝 / 紫）**：`:4699-4700` 的 timeline 150 是**父 clip 的长度**（逐 clip 行显示
        //    `Death_blue_sparkle1..4` 的像素帧只落在 52/65、`Death_purple_sparkle1..4` 只落在 52/63）⇒ 取
        //    **首末像素帧的跨度 ÷ fps** = 这一闪实际占的那一段：蓝 (65−52)/30 = 0.433 s、紫 (63−52)/30 = 0.367 s。
        //    ⛔ 不用 150 ÷ 30 = 5.000 s：那会把 2~3 张像素帧拉成"每帧定格 1.7 s"的白影。
        private const float DeathBlueSeconds = (65f - 52f) / DeathFxFps;
        private const float DeathPurpleSeconds = (63f - 52f) / DeathFxFps;
        private const float DeathGroundSeconds = 21f / DeathFxFps;
        private const float DeathParticleSeconds = 21f / DeathFxFps;

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
        ///（`StationChanged` 没到时仍能出画面）；一旦收到**非 Battle 且非 Pause** 的站点切换就置 false，
        /// 此后的尾随推送一律**忽略**（只 Warn 一次）。再收到 Battle / Pause ⇒ 重新置 true
        ///（Pause 是对局内的覆盖层，画面不拆，见 <see cref="OnStationChanged"/>）。
        /// </para>
        /// </summary>
        private bool _inBattle = true;

        /// <summary>离开对局站点后仍收到对局推送：只 Warn 一次（⛔ 不刷屏）。</summary>
        private bool _staleAfterLeaveWarned;
        private int _myTeam;

        private GameObject _content;
        private Transform _unitRoot;
        private ArenaView _arena;
        private PlacementIndicator _indicator;
        /// <summary>
        /// 就位读条（落位计时件）的**实例池**：一次出牌占一块、空闲回收（见 <see cref="AcquireDeployRing"/>）。
        /// <para>
        /// ⛔ 以前只有**一块**：后到的任何 spawn（含**敌方**出牌、召唤物）都会把那一块挪到自己的落点
        /// 并把倒计时重起 ⇒ 玩家在自己落点看到"表转了好几圈才落地、和落地时间不符"。
        /// 原版是**每个正在落位的单位各有一块**，故这里按出牌批次各占一块。
        /// </para>
        /// </summary>
        private readonly PlacementIndicator[] _deployRings = new PlacementIndicator[DeployRingPoolSize];

        /// <summary>
        /// 落位表盘池容量。依据：落位期 = 该卡 `deploy_ms`（20 张卡**全是 1000 ms**）⇒ 一块表只占 1.0 s；
        /// 一次出牌只起一块，故同屏同时存在的块数 ≈ 1 s 内落地的批次数（我方 + 敌方 + 召唤），
        /// 实战最多两三块。取 **4** 留一倍余量；真超了由 <see cref="AcquireDeployRing"/> 复用最先到期的那块。
        /// </summary>
        private const int DeployRingPoolSize = 4;

        private DeployAreaView _deployArea;   // 拖放时的不可放置区域显示（见 DeployAreaView）
        private EffectsView _effects;
        private BattleAudioView _audio;   // 对局音效（唯一挂点：见 Build；订阅 Core/Events.cs 的战斗事件）
        private Camera _cam;

        /// <summary>
        /// 卡池索引（`card_id → CardInfo`，来自 `Events.Deck.PoolLoaded`）。
        /// 精灵目录（`sprite_dir`）、远程判定（`projectile_key`）、弹道速度（`proj_speed`）、
        /// 落位期（`deploy_ms`）都从这里取 —— 客户端不落地卡 / 单位配表。
        /// </summary>
        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();

        /// <summary>出牌事件的卡不在卡池里：只 Warn 一次（⛔ 不刷屏）。</summary>
        private bool _cardMissingWarned;

        /// <summary>远程卡缺 `proj_speed`（&lt;=0）：只 Warn 一次。</summary>
        private bool _projSpeedMissingWarned;

        /// <summary>卡池里查不到 `deploy_ms`（落位期）⇒ 落位计时件退回兜底秒数：只 Warn 一次。</summary>
        private bool _deployMsMissingWarned;

        /// <summary>
        /// 渲染时钟上的**待办队列**（每帧推一次，见 <see cref="ProcessPendingSpellImpacts"/>）——
        /// 全类只有这一套"还要等多少毫秒"的计时，⛔ 不起第二套。三类条目共用它：
        /// <list type="bullet">
        /// <item><b>到达落点的特效</b>（<see cref="ImpactModeFx"/>）：法术落地段（飞行段飞完才播）
        ///   与投射物命中段（弹道到达才播），延迟 = 该段的飞行时长。</item>
        /// <item><b>战斗中开火</b>（<see cref="ImpactModeFire"/>）：攻击档上升沿（抬手起播）后延迟该目录的
        ///   `load_time_ms`（前摇）再发弹道 —— 到点那一帧取攻击者的**插值坐标**当起点
        ///   （见 <see cref="FireBattleShotNow"/>）。</item>
        /// <item><b>出牌落地标记</b>（<see cref="ImpactModeDeploy"/>）：网络事件到达后延迟到**渲染时钟**
        ///   追平该事件的时刻再播，使标记与单位的显示同帧（见 <see cref="ScheduleDeploy"/>）。</item>
        /// <item><b>出牌落位表盘</b>（<see cref="ImpactModeDeployRing"/>）：同一条延迟口径，使表盘与单位出现同帧
        ///   （见 <see cref="ScheduleDeployRing"/>）。</item>
        /// </list>
        /// </summary>
        private readonly List<PendingImpact> _pendingImpacts = new List<PendingImpact>();

        /// <summary>到点直接播 <see cref="PendingImpact.Use"/> 那一段特效（延迟到落点）。</summary>
        private const int ImpactModeFx = 0;

        /// <summary>到点发一条战斗弹道（见 <see cref="FireBattleShotNow"/>）。</summary>
        private const int ImpactModeFire = 1;

        /// <summary>到点播一次出牌落地标记（见 <see cref="PlayDeployAt"/>）。</summary>
        private const int ImpactModeDeploy = 2;

        /// <summary>到点开一块落位表盘（见 <see cref="PlayDeployRingAt"/>）。</summary>
        private const int ImpactModeDeployRing = 3;

        /// <summary>到点在落点画一次溅射命中的红色描边圆（见 <see cref="PlayAoeRingNow"/>）。</summary>
        private const int ImpactModeAoeRing = 4;

        /// <summary>一条待办（见 <see cref="_pendingImpacts"/>）。</summary>
        private struct PendingImpact
        {
            /// <summary>播什么：<see cref="ImpactModeFx"/> / <see cref="ImpactModeFire"/> / <see cref="ImpactModeDeploy"/>。</summary>
            public int Mode;
            public Vector2 World;
            public string Use;
            public int First;
            public int Count;
            /// <summary>整段时长（秒）；走 <see cref="EffectsView.PlayTimed"/>。</summary>
            public float Seconds;
            /// <summary>定格时长（秒）；<c>&gt; 0</c> 时走 <see cref="EffectsView.PlayHold"/>（单像素帧 export）。</summary>
            public float Hold;
            /// <summary>还要等多少毫秒才到点。</summary>
            public float LeftMs;
            /// <summary>日志标识（法术 key / 投射物名 / 实体 id）。</summary>
            public string Label;
            /// <summary><c>true</c> = 法术的落地段（计入 <see cref="SpellImpactPlayed"/>）。</summary>
            public bool Spell;
            /// <summary>开火条目的攻击者（快照实体 id）。</summary>
            public int AttackerId;
            /// <summary>开火条目的目标（**起手那一刻**索敌的结果；0 = 当时就没有合法目标）。</summary>
            public int TargetId;
            /// <summary>开火条目的**起手时刻目标位置**：`homing == false` 的投射物飞向它，⛔ 不逐帧重瞄。</summary>
            public Vector2 Aim;
            /// <summary>开火条目的投射物是否制导（出处 <see cref="ProjectileHomingTable"/>，按投射物名查表）。</summary>
            public bool Homing;
            /// <summary>开火条目的决定时刻（渲染时钟毫秒）—— 到点发弹道后记进 `_lastShotMs`，按 `hit_speed` 排下一次。</summary>
            public float DecidedMs;
            /// <summary>出牌落地条目的卡 id。</summary>
            public int CardId;
            /// <summary>出牌落地条目的队伍（去重与"敌方环变红"用）。</summary>
            public int Team;
            /// <summary>出牌落地条目的实体 id（日志用）。</summary>
            public int EntityId;
            /// <summary>溅射红圈条目的可见圆半径（格，= 攻击者卡池行的 `aoe_radius_milli / 1000`）。</summary>
            public float AoeRadiusTiles;
        }

        /// <summary>本局已播的弹道条数（自检 / 验收用的日志计数）。</summary>
        private int _projectileShots;

        /// <summary>未知 `kind` 只 Warn 一次（协议扩了 kind 而本类没跟上时一次留痕即可，⛔ 不静默、⛔ 不刷屏）。</summary>
        private bool _unknownKindWarned;

        /// <summary>战斗中开火弹道里"卡池没这张卡"只 Warn 一次。</summary>
        private bool _fireCardMissingWarned;

        /// <summary>出牌弹道的施法者退化到国王塔中心只 Warn 一次。</summary>
        private bool _casterFallbackWarned;

        /// <summary>挂起的出牌弹道等施法者进快照的上限（毫秒，服务端时间轴口径）。</summary>
        private const float CasterWaitMs = 2000f;

        /// <summary>
        /// 挂起中的出牌弹道（等施法者实体进快照）。
        /// <para>
        /// 为什么要挂起：`EvPlayCard` 只带施法者 **id**（`BattleEvent.entity_id`，坐标不在载荷里），
        /// 而该实体是**出牌之后**才生成的，服务端又把事件推在对应快照**之前** ⇒ 事件到达那一刻施法者
        /// 还没进任何快照，取不到它的坐标。留在这里等下一帧快照（通常 100 ms）即可取到。
        /// </para>
        /// </summary>
        private readonly List<PendingCasterShot> _pendingCasterShots = new List<PendingCasterShot>();

        /// <summary>一条等施法者进快照的出牌弹道（见 <see cref="_pendingCasterShots"/>）。</summary>
        private struct PendingCasterShot
        {
            public int EntityId;
            public int CardId;
            public int Team;
            public string ProjectileKey;
            public int ProjSpeed;
            public Vector2 Landing;
            public float GiveUpMs;   // 超过这个服务端时刻还没等到 ⇒ 退回旧口径
        }

        /// <summary>本局出牌弹道里"起点取到施法者实体坐标"的条数。</summary>
        private int _casterOrigins;

        /// <summary>本局出牌弹道里"起点 = 施法者、但施法者就在落点上（距离 0，无飞行段）"的条数。</summary>
        private int _casterZeroDist;

        /// <summary>本局出牌弹道里施法者迟迟没进快照、退回旧口径的条数。</summary>
        private int _casterTimedOut;

        /// <summary>上一帧快照里每个实体的 `anim`（`entity_id → anim`）—— 用来判"这一帧刚进入攻击档"。
        /// 协议里没有"开火"事件（`server/game/core/combat.go:100` / `:112` 只在挥砍那一 tick 把 anim 置 2），
        /// 所以"战斗中开火"的唯一可得信号 = **anim 从非 2 跳到 2** 的上升沿。</summary>
        private Dictionary<int, int> _prevAnim = new Dictionary<int, int>();

        /// <summary>本帧 anim 的暂存（双缓冲）。</summary>
        private Dictionary<int, int> _curAnim = new Dictionary<int, int>();

        // ── 自检计数（供 EditMode 单测 / 实机 driver 读）──

        /// <summary>本局播出的出牌落地特效次数（= 收到的 `EvSpawn` 事件里成功播出的数量）。</summary>
        public int DeployFxPlayed { get { return _deployFxPlayed; } }

        /// <summary>本局被"同一次出牌"去重掉的落位标记条数（多单位卡片的后续单位，见 `PlayDeploy`）。</summary>
        public int DeployDupeSkipped { get { return _deployDupeSkipped; } }

        /// <summary>本局因"攻击档"触发播出的战斗弹道条数。</summary>
        public int BattleProjectileShots { get { return _battleShots; } }

        /// <summary>本局排期的战斗弹道数（含正在等前摇的，见 <see cref="_pendingImpacts"/> 的开火条目）。</summary>
        public int FireShotsScheduled { get { return _fireShotsScheduled; } }

        /// <summary>本局因同一攻击者已有一条在等前摇而折掉的排期数。</summary>
        public int FireShotsDeduped { get { return _fireShotsDeduped; } }

        /// <summary>本局排期后攻击者已离场（前摇期间死亡 / 出图）而没发出的弹道数。</summary>
        public int FireShotsLost { get { return _fireShotsLost; } }

        /// <summary>本局已排期但**前摇被中断**（目标死 / 出图 / 攻击档提前释放）而取消掉的弹道数。</summary>
        public int FireShotsCancelled { get { return _fireShotsCancelled; } }

        /// <summary>
        /// 本局因**出牌事件**（`EvPlayCard` + `proj_speed &gt; 0`）播出的弹道条数
        /// （= <see cref="PlayProjectileFlight"/> 走到 `PlayFlight` 的次数）。
        /// <para>与 <see cref="BattleProjectileShots"/> 分开计：那条 = "单位攻击"（`anim == 2` 反推），
        /// 这条 = "出牌"（有事件可挂）—— 两者的触发源不同，合起来才覆盖任务 3 的两半要求。</para>
        /// </summary>
        public int CardProjectileShots { get { return _projectileShots; } }

        /// <summary>本局出牌弹道里起点取到**施法者实体坐标**的条数（= 服务端 `EvPlayCard` 的施法者 id 被用上的次数）。</summary>
        public int CasterOrigins { get { return _casterOrigins; } }

        /// <summary>本局出牌弹道里施法者**就在落点上**（距离 0）⇒ 无飞行段的条数（单单位卡的常态，出牌即落地）。</summary>
        public int CasterZeroDistance { get { return _casterZeroDist; } }

        /// <summary>本局出牌弹道里施法者没进快照、退回旧口径（本方最近单位 / 国王塔中心）的条数。</summary>
        public int CasterTimedOut { get { return _casterTimedOut; } }

        /// <summary>当前还在等施法者进快照的出牌弹道条数。</summary>
        public int CasterPending { get { return _pendingCasterShots.Count; } }

        /// <summary>
        /// 本局收到的**塔开火**事件（`kind == 6` / `EvTowerShoot`）条数。
        /// <para>判据：<c>TowerShots == 服务端日志里 EvTowerShoot 的条数</c>（塔开火链路端到端通的必要条件）。</para>
        /// </summary>
        public int TowerShots { get { return _towerShots; } }

        /// <summary>本局从 `towers_a/b` 逐 id 比 hp 推出的**塔命中**次数（塔不在 `entities` ⇒ 与 <see cref="HitsObserved"/> 分开计）。</summary>
        public int TowerHitsObserved { get { return _towerHitsObserved; } }

        /// <summary>本局在塔位置真的播出的受击闪光次数。</summary>
        public int TowerHitFxPlayed { get { return _towerHitFxPlayed; } }

        /// <summary>本局塔命中闪光播不出去的次数（特效层没有 / 落点取不到）。</summary>
        public int TowerHitFxSkipped { get { return _towerHitFxSkipped; } }

        /// <summary>本局因单帧上限被丢掉的塔命中次数。</summary>
        public int TowerHitFxCapped { get { return _towerHitFxCapped; } }

        /// <summary>本局塔开火里走了转向那一步的次数（= 收到 `EvTowerShoot` 的次数）。</summary>
        public int TowerAimTotal { get { return _towerAimTotal; } }

        /// <summary>本局炮塔真的选到方向帧的次数。</summary>
        public int TowerAimed { get { return _towerAimed; } }

        /// <summary>本局保持当前方向不动的次数。</summary>
        public int TowerAimKept { get { return _towerAimKept; } }

        /// <summary>
        /// 本局收到的**国王塔激活**事件（`kind == 5` / `EvTowerActivated`）条数。
        /// <para>判据：<c>TowerActivations == 服务端日志里 EvTowerActivated 的条数</c>；且
        /// <c>TowerActivatedShown</c> 应当把其中每一座"本地有塔视图"的算进去。</para>
        /// </summary>
        public int TowerActivations { get { return _towerActivations; } }

        /// <summary>本局真的把**炮塔层点亮**的国王塔座数（`EvTowerActivated` + 塔开火兜底两条路）。</summary>
        public int TowerActivatedShown { get { return _towerActivatedShown; } }

        /// <summary>本局从**塔开火事件**里真的播出弹道（`proj_speed &gt; 0`）的条数。</summary>
        public int TowerShotFlights { get { return _towerShotFlights; } }

        /// <summary>
        /// 本局塔开火里没播出弹道的次数（无目标 / 无速度 / 特效层同屏上限 / 素材目录为空）。
        /// <para><b>自检断言</b>：<c>TowerShots == TowerShotFlights + TowerShotSkipped</c>
        /// （不成立 = 有开火事件没被任何一条路径处理 = 判红）。</para>
        /// </summary>
        public int TowerShotSkipped { get { return _towerShotSkipped; } }

        /// <summary>
        /// 本局收到的**法术卡出牌**事件（`EvPlayCard` 且 `CardInfo.type == 1`）条数 —— 差异登记见 `策划/对照表.md` §D.3。
        /// <para><b>自检断言</b>：<c>SpellCasts == SpellFxPlayed + SpellFxSkipped</c>
        /// （不成立 = 有法术出牌没走到播放入口 = 判红）。</para>
        /// </summary>
        public int SpellCasts { get { return _spellCasts; } }

        /// <summary>本局真的播出去的法术命中特效次数。</summary>
        public int SpellFxPlayed { get { return _spellFxPlayed; } }

        /// <summary>本局被丢弃的法术特效次数（卡池未到 / 这张法术还没接帧 / 特效层同屏上限 / 目录为空）。</summary>
        public int SpellFxSkipped { get { return _spellFxSkipped; } }

        /// <summary>
        /// 本局从快照里推出的**命中**次数（逐 id 比对前后两帧快照的 `hp`，掉血即一次）——
        /// 协议没有独立的命中事件（`Def/ProtoDef.cs` 的 `BattleEvent.kind` 只有 0..6），
        /// 与 <see cref="BattleAudioView"/> 喂命中音效用的是同一条规则。
        /// </summary>
        public int HitsObserved { get { return _hitsObserved; } }

        /// <summary>
        /// 本局真的播出去的命中闪光次数（= <see cref="HitsObserved"/> 里成功播出闪光的那些）。
        /// <para>两者之差 = 同帧超过 <see cref="MaxHitFxPerSnapshot"/> 的溢出（见该常量的注释）+ 特效层同屏上限 / 素材目录为空。</para>
        /// </summary>
        public int HitFxPlayed { get { return _hitFxPlayed; } }

        /// <summary>
        /// 本局**带飞行段**的法术里真的播出飞行段的条数（飞行段来源见 <see cref="SpellFxTable.Entry.FlightUse"/>）。
        /// </summary>
        public int SpellFlights { get { return _spellFlights; } }

        /// <summary>本局**没有飞行段**、直接在落点播落地段的法术条数。</summary>
        public int SpellDirectImpacts { get { return _spellDirectImpacts; } }

        /// <summary>本局落地段真的播出的次数（含飞行段到达后补播的那些）。</summary>
        public int SpellImpactPlayed { get { return _spellImpactPlayed; } }

        /// <summary>本局落地段没播出的次数（素材目录为空 / 特效层同屏上限）。</summary>
        public int SpellImpactSkipped { get { return _spellImpactSkipped; } }

        /// <summary>还在等落地（飞行段没飞完）的落地段条数。</summary>
        public int SpellImpactsPending { get { return _pendingImpacts.Count; } }

        /// <summary>本局投射物**命中段**真的播出的次数（段来源见 <see cref="EffectsView.TryGetProjectileHitFx"/>）。</summary>
        public int ProjectileHitFxPlayed { get { return _projHitFxPlayed; } }

        /// <summary>本局投射物命中段没播出的次数（该投射物没有专属命中段 / 素材为空 / 同屏上限）。</summary>
        public int ProjectileHitFxSkipped { get { return _projHitFxSkipped; } }

        /// <summary>本局塔开火里**定格播枪口闪光**的次数（无速度 / 无目标的降级路径，见 <see cref="PlayTowerShot"/>）。</summary>
        public int TowerShotMuzzles { get { return _towerShotMuzzles; } }

        /// <summary>本局因塔开火真的触发了一遍**乘员攻击动画**的次数（见 <see cref="PlayTowerShot"/>）。</summary>
        public int TowerAttackAnims { get { return _towerAttackAnims; } }

        /// <summary>本局**国王塔**开火事件数（对上的塔是王塔的次数）。</summary>
        public int KingTowerShots { get { return _kingTowerShots; } }

        /// <summary>本局国王塔**炮塔后坐**实际起的次数（自检：应等于 <see cref="KingTowerShots"/>）。</summary>
        public int KingTowerRecoils { get { return _kingTowerRecoils; } }

        /// <summary>本局国王塔开火的**炮口闪光**播出次数（自检：应等于 <see cref="KingTowerShots"/>）。</summary>
        public int KingTowerMuzzleFlashes { get { return _kingMuzzleFlashes; } }

        private int _deployFxPlayed;

        /// <summary>
        /// 已开始过的那一局的 `room_id` / `seed`（`BattleStartNotify` 自带）。
        /// `null` = 本进程还没开过局；判"是不是新的一局"的键，见 <see cref="OnBattleStarted"/>。
        /// <para>
        /// 生命周期 = **跨画面重建保留**（`Build` / `Teardown` 都不清）：首次进图时
        /// `Events.Battle.Started` 早于 `Scene.Load`，建场发生在 `OnBattleStarted` **内部** ——
        /// 若建场把这份基线清掉，同一场景的下一局就判不出"新的一局"（塔的阵亡终态与快照时钟都不会重置）。
        /// </para>
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

        /// <summary>最近一条落位标记所用的卡 id（"一次出牌"去重的键，见 <see cref="PlayDeploy"/>）。</summary>
        private int _lastDeployCardId;

        /// <summary>
        /// 最近一条落位标记所属的**阵营**（与 <see cref="_lastDeployCardId"/> 配对成同一把去重钥匙）。
        /// <para>
        /// 为什么要带阵营：去重窗口 0.5 s 内敌我可能先后落下**同一张卡**（同一个 `card_id`）
        /// ⇒ 只看卡 id 会把敌方的那个误判成"我的后续单位"而吞掉它自己的落位环（应当画红色那半边）。
        /// 初值 <see cref="int.MinValue"/> = 与任何真实阵营（0/1）都不相等 ⇒ 新的一局第一条 spawn 必不被去重。
        /// </para>
        /// </summary>
        private int _lastDeployTeam = int.MinValue;

        /// <summary>最近一条落位标记的时刻（`Time.unscaledTime`，与 <see cref="DeployDedupeSeconds"/> 配对）。</summary>
        private float _lastDeployTime = -999f;

        /// <summary>被"同一次出牌"去重掉的落位标记条数（自检用，见 <see cref="DeployDupeSkipped"/>）。</summary>
        private int _deployDupeSkipped;

        /// <summary>最近一次开出落位表盘的卡 id（与 <see cref="_lastDeployRingTeam"/> 配对，用于报"出牌→落地"间隔）。</summary>
        private int _lastDeployRingCardId;

        /// <summary>最近一次开出落位表盘的阵营（初值 = 与任何真实阵营都不等的哨兵）。</summary>
        private int _lastDeployRingTeam = int.MinValue;

        /// <summary>
        /// 最近一次开出落位表盘的时刻（`Time.unscaledTime`）。
        /// 与 <see cref="PlayDeployAt"/> 里单位出现时刻的差 = "表盘在走、场上还没东西"的时长。
        /// </summary>
        private float _lastDeployRingTime = -999f;

        private int _battleShots;
        private int _towerShots;
        private int _towerActivations;
        private int _towerActivatedShown;
        private int _towerShotFlights;
        private int _towerShotSkipped;
        private int _spellCasts;
        private int _spellFxPlayed;
        private int _spellFxSkipped;
        private int _spellFlights;
        private int _spellDirectImpacts;
        private int _spellImpactPlayed;
        private int _spellImpactSkipped;
        private int _projHitFxPlayed;
        private int _projHitFxSkipped;
        private int _towerShotMuzzles;
        private int _towerAttackAnims;
        private int _kingTowerShots;
        private int _kingTowerRecoils;
        private int _kingMuzzleFlashes;

        /// <summary>落地段 / 命中段播不出去（素材目录空 / 同屏上限）只 Warn 一次（⛔ 不刷屏）。</summary>
        private bool _impactMissingWarned;
        private int _hitsObserved;
        private int _hitFxPlayed;

        /// <summary>
        /// 单帧快照里最多播几个命中闪光。**本项目自定**：一帧里可能同时有十几只单位掉血，
        /// 逐条都播会让画面糊成一片；原版是"每一次真实命中各闪一下"，而我们只能从 `hp` 总量反推、
        /// 分不出"一次大伤害"和"三次小伤害" ⇒ 设上限（只影响同帧并发数，不影响单挑/小规模交火）。
        /// <para>
        /// 取 32 = 特效层同屏上限（<see cref="EffectsView.MaxLive"/>）的一半：群体伤害 / 多目标齐中
        /// 时不再漏播（旧值 8 在 10+ 目标同帧受伤时会静默漏掉后面的闪光），同时仍留一道防"整屏齐闪"的闸门。
        /// </para>
        /// </summary>
        private const int MaxHitFxPerSnapshot = 32;

        /// <summary>上一帧快照里每个实体的 `hp`（`entity_id → hp`）—— 判"这一帧谁掉血了"。</summary>
        private Dictionary<int, int> _prevHp = new Dictionary<int, int>();

        /// <summary>本帧 `hp` 的暂存（双缓冲）。</summary>
        private Dictionary<int, int> _curHp = new Dictionary<int, int>();

        // ── 塔的命中（塔不在 `entities` 里，由 `towers_a` / `towers_b` 报告 ⇒ 单独一套 hp 双缓冲）──
        //    键 = `TowerHpKey(team, towerId)` = 队伍 × 100000 + 塔 id。
        //    塔 id 来自服务端**全局一把实体计数器**（`server/game/core/battle.go:133` `nextID: 1`；
        //    塔的创建顺序见同文件 `:140-141` = 蓝国王 / 蓝左 / 蓝右 / 红国王 / 红左 / 红右 ⇒ id 1..6），
        //    即蓝 1/2/3、红 4/5/6 —— 两队**不重号**；键里带上队伍 = 显式标出「这是哪队的塔」。
        /// <summary>上一帧快照里每座塔的 `hp`（`(team,id) → hp`）。</summary>
        private Dictionary<int, int> _prevTowerHp = new Dictionary<int, int>();

        /// <summary>本帧塔 `hp` 的暂存（双缓冲）。</summary>
        private Dictionary<int, int> _curTowerHp = new Dictionary<int, int>();

        /// <summary>本局从 `towers_a/b` 推出的**塔命中**次数（逐 id 掉血即一次）。</summary>
        private int _towerHitsObserved;

        /// <summary>本局真的在塔位置播出的受击闪光次数。</summary>
        private int _towerHitFxPlayed;

        /// <summary>本局塔命中闪光**播不出去**的次数（特效层没有 / 塔视图取不到落点）。</summary>
        private int _towerHitFxSkipped;

        /// <summary>本局因单帧上限被丢掉的塔命中次数（与单位同一道闸门，见 <see cref="MaxHitFxPerSnapshot"/>）。</summary>
        private int _towerHitFxCapped;

        /// <summary>塔位置取不到只报一次（⛔ 不刷屏）。</summary>
        private bool _towerHitPosWarned;

        // ── 炮塔转向（`EvTowerShoot` 的表现）：转向次数 / 保持不动次数 ──
        /// <summary>本局塔开火里**走了转向那一步**的次数（= 收到 `EvTowerShoot` 的次数）。</summary>
        private int _towerAimTotal;

        /// <summary>本局真的选到方向帧的次数（国王塔有炮塔层）。</summary>
        private int _towerAimed;

        /// <summary>本局保持当前方向不动的次数（公主塔没炮塔层 / 素材缺失 / 取不到目标）。</summary>
        private int _towerAimKept;

        /// <summary>塔 hp 双缓冲的键：队伍 × 100000 + 塔 id（塔 id = 服务端全局实体 id，蓝 1/2/3、红 4/5/6）。</summary>
        private static int TowerHpKey(int team, int id)
        {
            return team * 100000 + id;
        }

        /// <summary>法术特效"这张法术还没接帧"只报一次（⛔ 不刷屏）。</summary>
        private bool _spellFxUnknownWarned;

        /// <summary>塔开火时"塔视图里找不到炮口层"只报一次（⛔ 不刷屏）。</summary>
        private bool _towerMuzzleMissingWarned;

        /// <summary>国王塔激活事件到达时塔视图还没建（只报一次）。</summary>
        private bool _towerActivateMissingWarned;

        /// <summary>国王塔激活事件在塔视图里对不上号（只报一次）。</summary>
        private bool _towerActivateUnmatchedWarned;

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

        /// <summary>
        /// 最近一帧快照的塔数组（只读引用）。塔不在 `entities` 里 ⇒ 也不在插值窗口的载荷里，
        /// 而"塔命中闪光"要在渲染时钟上判（见 <see cref="TrackCombatAtRender"/>）⇒ 在这里留一份引用。
        /// </summary>
        private TowerState[] _latestTowersA, _latestTowersB;

        /// <summary>每个实体上一次**成功播出**弹道的服务端时刻（毫秒）—— 用来按 `hit_speed` 排期。</summary>
        private readonly Dictionary<int, float> _lastShotMs = new Dictionary<int, float>();

        /// <summary>`_lastShotMs` 的清理暂存（⛔ 不在遍历中改字典）。</summary>
        private readonly List<int> _shotPrune = new List<int>();

        /// <summary>
        /// 本帧每个实体的**插值世界坐标**（`entity_id → world`），由 <see cref="ApplyEntities"/> 每帧写入。
        /// 离散特效（命中闪光 / 战斗弹道起点）按它取位置 ⇒ 特效与精灵**同帧同位**
        /// （⛔ 不用快照自带的坐标：那是插值窗口末端的坐标，比画面上的精灵超前一个快照间隔）。
        /// </summary>
        private readonly Dictionary<int, Vector2> _renderWorld = new Dictionary<int, Vector2>();

        /// <summary>已做过边沿判定的**窗口末帧**快照时刻（毫秒）；<c>NaN</c> = 还没做过。</summary>
        private float _signalEndMs = float.NaN;

        /// <summary>本帧该实体的插值坐标取不到（它未被渲染）只 Warn 一次。</summary>
        private bool _renderPosMissingWarned;

        /// <summary>本局排期的战斗弹道数（含正在等前摇的）。</summary>
        private int _fireShotsScheduled;

        /// <summary>本局因同一攻击者已有一条在等前摇而被折掉的排期数。</summary>
        private int _fireShotsDeduped;

        /// <summary>本局排期后攻击者已离场（前摇期间死亡 / 出图）而没发出的弹道数。</summary>
        private int _fireShotsLost;

        /// <summary>本局已排期但前摇被中断而取消掉的弹道数。</summary>
        private int _fireShotsCancelled;

        // ── 时间轴（渲染时钟 / 快照历史 / 插值窗口 / 速率修正）全部由引擎件持有，见类注释三 ──

        /// <summary>
        /// 引擎快照插值器：渲染时钟、快照历史、插值窗口与速率修正都在它内部
        /// （`clover-client-unity-engine/Runtime/Presentation/SnapshotInterpolator.cs`）。
        /// <para>
        /// ⛔ **只能惰性构造，不能在字段初始化 / 构造函数里 `new`**：引擎 ctor 会当场用默认时钟读一次
        /// `Time.realtimeSinceStartup`（`SnapshotInterpolator.cs:807` 的 `Reset` ← `:495` 的 `DefaultClock`），
        /// 而 Unity 禁止在 MonoBehaviour 构造 / 实例字段初始化期调用它 —— 抛 `UnityException` 会让
        /// `AddComponent<BattleViewRoot>()` 整个失败（组件建不出来 ⇒ 对局视图永远不建）。
        /// </para>
        /// </summary>
        private SnapshotInterpolator _interp;

        /// <summary>取插值器（首次访问时才构造；构造前后所有读取都走这里，避免 NRE）。</summary>
        private SnapshotInterpolator Interp
        {
            get
            {
                if (_interp == null) _interp = new SnapshotInterpolator(InterpOptions);
                return _interp;
            }
        }

        /// <summary>
        /// 插值器参数：快照间隔取 <c>GameConst.SnapshotIntervalMs</c>（本项目 100ms = 10 Hz）口径；
        /// 历史 6 格（覆盖 5 个间隔 = 500ms，比 200ms 目标落后量多 3 个间隔余量）、
        /// 渲染落后最新快照 2 个间隔、稳态速率微调 ±5%、灾难级偏差放开到 2× 真实时间。
        /// </summary>
        private static readonly SnapshotInterpolatorOptions InterpOptions = new SnapshotInterpolatorOptions
        {
            SnapshotIntervalMs = GameConst.SnapshotIntervalMs,
            ExpectedRenderFps = 60f,
            HistSlots = 6,
            RenderLagIntervals = 2f,
            LagSteerGain = 0.05f,
            LagSteerMaxRate = 0.05f,
            CatchUpMaxRate = 1f,
            CatchUpThresholdIntervals = 3f,
            ServerNowExtrapCapIntervals = 2f,
            ClockMaxLeadIntervals = 1f,
        };

        /// <summary>
        /// 历史载荷（`id → EntitySnapshot`）的空闲槽：被插值器挤出历史的那一格回收进来复用，
        /// ⛔ 不每次推快照都新建字典。
        /// </summary>
        private readonly Queue<Dictionary<int, EntitySnapshot>> _freeSlots = new Queue<Dictionary<int, EntitySnapshot>>();

        /// <summary>当前仍在插值器历史里的载荷槽（队首 = 最早的那一格，也就是下一次推入时被挤出的那一格）。</summary>
        private readonly Queue<Dictionary<int, EntitySnapshot>> _liveSlots = new Queue<Dictionary<int, EntitySnapshot>>();

        /// <summary>
        /// 空窗口时的占位载荷：插值器还没选出一对窗口时 `WindowEndPayload` 为 null，
        /// 直接 `foreach` 会 NRE —— 这里给一个恒空的只读替身（遍历它自然什么都不做）。
        /// </summary>
        private static readonly Dictionary<int, EntitySnapshot> EmptySnapshotIndex = new Dictionary<int, EntitySnapshot>();

        /// <summary>
        /// 渲染时钟相对"服务端现在"的目标落后量（= <see cref="InterpOptions"/> 的 2 个快照间隔 = 200ms）。
        /// 离线探针（`tools/probes/FlowProbe.SimulateController`）按名读它。
        /// </summary>
        public static float TargetLagMs { get { return InterpOptions.TargetLagMs; } }

        /// <summary>
        /// 速率控制律（转发引擎件 <c>SnapshotInterpolator.SteerRate</c>）：输入"时钟相对目标的偏差"
        /// （<c>目标值 - 时钟现值</c>，正 = 时钟落后了要加速），输出时钟该跑多快。
        /// 偏差 ≤ 追帧阈值时在 ±5% 内按比例微调，超过阈值则放开到 <see cref="InterpOptions"/> 的追帧档。
        /// 离线探针（`tools/probes/FlowProbe.SimulateController`）按名读它。
        /// </summary>
        public static float SteerRate(float lagErrorMs)
        {
            return SnapshotInterpolator.SteerRate(lagErrorMs, InterpOptions);
        }

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

        /// <summary>此时是否已经有一对可插值的窗口（自检用）。</summary>
        public bool HasWindow { get { return Interp.HasWindow; } }

        /// <summary>已收到的有效快照数（丢帧不计；自检用）。</summary>
        public int SnapshotCount { get { return Interp.SnapshotCount; } }

        /// <summary>当前活着的单位视图数（自检用）。</summary>
        public int LiveUnitCount { get { return _units.Count; } }

        /// <summary>
        /// 渲染落后**正在渲染的那一对快照的末端**多少毫秒（= `Interp.RenderLagMs`，自检用）。
        /// <para>
        /// 判据：正常运行时应在 **[0, 一个快照间隔]** 之内波动（本项目 100 ms），
        /// 且**永远 &gt;= 0**（负值 = 渲染跑到了服务端前面 = 外推，引擎件刻意不允许）。
        /// </para>
        /// </summary>
        public float RenderLagMs { get { return Interp.RenderLagMs; } }

        /// <summary>
        /// 渲染落后的**总**落后量 = 最新快照的时间戳 - 渲染时钟（= `Interp.BufferLagMs`，自检用）。
        /// 稳态应落在 <c>[一个间隔, 2 个间隔]</c>（本项目 100~200 ms）—— 多出来的那一个间隔就是
        /// **抖动缓冲**：它换掉了"到达时刻一抖就把窗口推走"。
        /// </summary>
        public float BufferLagMs { get { return Interp.BufferLagMs; } }

        /// <summary>**正在渲染的那一对**快照的起始时间戳（毫秒，自检用；0 = 还没有一对）。</summary>
        public float WindowStartMs { get { return Interp.WindowStartMs; } }

        /// <summary>**正在渲染的那一对**快照的结束时间戳（毫秒，自检用；两者之差应 ≡ 服务端间隔）。</summary>
        public float WindowEndMs { get { return Interp.WindowEndMs; } }

        /// <summary>最新收到的快照的时间戳（毫秒，自检用）。</summary>
        public float NewestSnapshotMs { get { return Interp.NewestSnapshotMs; } }

        /// <summary>渲染时钟当前速率（自检用）：常态 <c>1.0</c>，追帧中 &gt; 1.0（≤ 2×）。</summary>
        public float ClockRate { get { return Interp.ClockRate; } }

        /// <summary>是否正在追帧（自检用）。</summary>
        public bool CatchingUp { get { return Interp.CatchingUp; } }

        /// <summary>当前插值比例 t（自检用；`NaN` = 还没到首帧）。</summary>
        public float InterpRatio { get { return Interp.InterpRatio; } }

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
            // ⛔ 已离开对局站点时不再重建（见 `_inBattle` 的注释）。
            if (_built && _content == null && _inBattle) Build();

            // 还没有画面或还没收到过快照：无事可做（`t` 也无从算起）。
            if (!_built || Interp.SnapshotCount == 0) return;
            TickRender();

            // 法术落地段 / 投射物命中段要等飞行段走完才到落点 ⇒ 每帧推一下待落点队列。
            ProcessPendingSpellImpacts();
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
                for (var i = 0; i < _deployRings.Length; i++) _deployRings[i] = null;
                _deployArea = null;
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
            // 就位读条按**池**建：两条链的生命周期不同（拖动 = 抬手才收；就位读条 = 倒计时到点自己收），
            // 共用一个实例时后者的到点收会把前者收掉（区域显示同理由 `ShowPlacement` 与它并列存在）；
            // 而**池化**还多解一层：多个出牌批次同时落位时各占一块、互不抢（见 AcquireDeployRing 的注释）。
            for (var i = 0; i < _deployRings.Length; i++)
                _deployRings[i] = PlacementIndicator.Create(_content.transform);
            _deployArea = DeployAreaView.Create(_content.transform);
            _effects = EffectsView.Create(_content.transform); // 特效层（原版帧序列，见 ResPaths 特效区段）
            // 音效层（⛔ **唯一挂点**）：挂在 BattleContent 下 ⇒ 生命周期跟着对局画面走
            //（出图时随场景卸载，OnDestroy 自动退订 Core/Events.cs 的战斗事件）。与 EffectsView 同一范式。
            // 传入 `this`：本控制器先于它订阅 `Battle.Started` 且整场常驻 ⇒ 音效侧的敌我判定读 `MyTeam`。
            _audio = BattleAudioView.Create(_content.transform, this);

            _cam = Game.Camera?.Main;
            if (_cam == null)
            {
                // 场景里本来就有一台正交相机；真丢了就补一台，否则画面根本不渲染。
                // 引擎的相机模块只驱动已存在的相机（`ICameraManager` 无"建场景相机"入口）⇒ 兜底留在项目侧。
                var camGo = new GameObject("Battle Camera");
                camGo.tag = "MainCamera";
                _cam = camGo.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.043f, 0.063f, 0.106f, 1f); // 与 SceneBuilder 的清屏色一致
                Game.Logger?.Warn(LogTag, "场景里没有 MainCamera ⇒ 已补建一台（否则黑屏，会被误判成逻辑没跑）");
            }
            ArenaView.SetupCamera(_cam);

            _overCapWarned = false;
            _projectileShots = 0;
            _casterOrigins = 0;
            _casterZeroDist = 0;
            _casterTimedOut = 0;
            _pendingCasterShots.Clear();
            _unknownKindWarned = false;
            _fireCardMissingWarned = false;
            _casterFallbackWarned = false;
            _deployFxPlayed = 0;
            _lastDeployCardId = 0;
            _lastDeployTeam = int.MinValue;
            _lastDeployTime = -999f;
            _deployDupeSkipped = 0;
            _lastDeployRingCardId = 0;
            _lastDeployRingTeam = int.MinValue;
            _lastDeployRingTime = -999f;
            _battleShots = 0;
            _towerShots = 0;
            _towerActivations = 0;
            _towerActivatedShown = 0;
            _towerShotFlights = 0;
            _towerShotSkipped = 0;
            _spellCasts = 0;
            _spellFxPlayed = 0;
            _spellFxSkipped = 0;
            _spellFxUnknownWarned = false;
            _spellFlights = 0;
            _spellDirectImpacts = 0;
            _spellImpactPlayed = 0;
            _spellImpactSkipped = 0;
            _projHitFxPlayed = 0;
            _projHitFxSkipped = 0;
            _towerShotMuzzles = 0;
            _towerAttackAnims = 0;
            _kingTowerShots = 0;
            _kingTowerRecoils = 0;
            _kingMuzzleFlashes = 0;
            _impactMissingWarned = false;
            _towerMuzzleMissingWarned = false;
            _towerActivateMissingWarned = false;
            _towerActivateUnmatchedWarned = false;
            _lastEntities = null;
            _latestTowersA = null;
            _latestTowersB = null;
            _renderWorld.Clear();
            _signalEndMs = float.NaN;
            _renderPosMissingWarned = false;
            _fireShotsScheduled = 0;
            _fireShotsDeduped = 0;
            _fireShotsLost = 0;
            _fireShotsCancelled = 0;
            _prevAnim.Clear();
            _curAnim.Clear();
            _lastShotMs.Clear();
            _shotPrune.Clear();
            _prevHp.Clear();
            _curHp.Clear();
            // 塔的 hp 双缓冲同理（塔不在 entities ⇒ 另一套字典），计数一并归零。
            _prevTowerHp.Clear();
            _curTowerHp.Clear();
            _towerHitsObserved = 0;
            _towerHitFxPlayed = 0;
            _towerHitFxSkipped = 0;
            _towerHitFxCapped = 0;
            _towerHitPosWarned = false;
            _towerAimTotal = 0;
            _towerAimed = 0;
            _towerAimKept = 0;
            // 单位命中的计数与塔命中计数同一生命周期（都是"本局"读数，见各自属性的注释）。
            _hitsObserved = 0;
            _hitFxPlayed = 0;
            // 部署合法性的塔存活输入回到"全部存活"（从严：不给口袋区、塔周围 1 格仍不可放）；
            // 首个快照到达后由 `ApplyTowers` 按服务端覆盖。
            _enemyLeftAlive = true;
            _enemyRightAlive = true;
            _ownLeftAlive = true;
            _ownRightAlive = true;
            // 延迟队列归零：跨局残留的条目会在新场地上凭空播一次落地段。
            _pendingImpacts.Clear();
            // 时间轴归零：插值器复位 ⇒ 新的一局在首个快照上重新对齐一次服务端时间戳；
            // 载荷槽一并回收（`resetWarnOnce` ⇒ 每局都能重新留一次非预期分支的痕）。
            Interp.Reset(resetWarnOnce: true);
            _freeSlots.Clear();
            _liveSlots.Clear();
            // 本局身份（`_matchRoomId/_matchSeed`）由 `OnBattleStarted` 维护，⛔ 建场不清它 ——
            // 判"新的一局"（塔阵亡终态 / 快照时钟是否重置）依赖跨画面重建保留的这份基线。
            _built = true;
            Game.Logger?.Info(LogTag,
                $"对局表现层已建：塔={_arena.TowerCount} 相机={_cam.name} 正交半高={_cam.orthographicSize:F2}（18×32 格竖屏）");
            // 建场后才预热：`Events.Battle.Started` 可能早于本方法（首次进图），此刻资源后端已就绪。
            WarmBattleSprites();
        }

        /// <summary>拆掉画面（幂等）。</summary>
        /// <param name="clearCaches">
        /// true = 连 SpriteBank / UnitView 池一起清（出图、销毁时用）；
        /// false = 同场景「再来一局」重建 —— 静态贴图缓存与单位池保留，避免整目录重载卡顿。
        /// </param>
        public void Teardown(bool clearCaches = true)
        {
            foreach (var kv in _units)
                if (kv.Value != null) kv.Value.Release();
            _units.Clear();
            _recycle.Clear();
            if (clearCaches)
            {
                UnitView.ClearPools();
                SpriteBank.ClearCache();
            }

            if (_content != null)
            {
                Destroy(_content);
                _content = null;
            }

            _arena = null;
            _indicator = null;
            for (var i = 0; i < _deployRings.Length; i++) _deployRings[i] = null;
            _deployArea = null;
            _effects = null;
            _audio = null;
            _unitRoot = null;
            _cam = null;
            Interp.Reset();
            _freeSlots.Clear();
            _liveSlots.Clear();
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

            // 暂停是**对局内的覆盖层**（`Core/Stations.cs` 的 `Pause`：Battle01 场景、Popup 层、
            // 对战不真暂停；`AppFlow.EnterPause` 也不动 timeScale）⇒ 画面必须原样保留。
            // ⛔ 走下面的拆解分支会把竞技场 / 单位 / 特效整片拆掉（暂停时屏后只剩相机底色），
            // 恢复时再整场重建（插值时钟与"本局"计数一并归零、在飞的特效与计时件全丢）。
            if (station == Stations.Pause)
            {
                _inBattle = true;
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
            // ── 同一场景里的**新一局**必须整场重建（见 `策划/对照表.md` §D.3）──
            // 「再来一局」时 `AppFlow.RequestEnterBattle` 只切站点、不重载场景（`CurrentScene == Battle01`）
            // ⇒ 本组件不重建，而这两样东西都是**每局唯一**的、必须归零的：
            //   ① `ArenaView.TowerView` 的阵亡终态锁（`ArenaView._destroyed` 置位后不清零）会把新局
            //      "塔满血存活"的快照挡掉 ⇒ 塔体各层仍隐藏、废墟层仍亮（= 塔还是死亡状态）；
            //   ② 快照时钟（引擎插值器里的历史 / 最新时间戳 / 对齐点）还停在上局末尾，
            //      而新局 `server_ms` 从 0 起 ⇒ `Interp.Push` 的"时间戳没有前进 ⇒ 整帧丢弃"会把新局快照
            //      一直丢到时间追上上一局（画面停在上局末帧）。
            // 判"新的一局"= `room_id` + `seed` 变化（服务端每局新建房间）；`_matchRoomId == null` =
            // 还没开过局（本进程第一局），此时画面本来就是新建的，⛔ 不重建（否则每次进图白重建一次）。
            // `Teardown()` 之后由下方的 `RebuildIfWanted` 重建，`Build()` 把上面两组一起归零
            // ⇒ ⛔ 只清 `_destroyed` 不够（时钟那组不清仍会停帧）。
            var newMatch = _matchRoomId != null && (n.room_id != _matchRoomId || n.seed != _matchSeed);
            if (newMatch)
            {
                Game.Logger?.Info(LogTag,
                    $"新的一局：room_id={n.room_id} seed={n.seed}（上一局 room_id={_matchRoomId} seed={_matchSeed}）" +
                    $"⇒ 对局表现层整场重建（已建={_built}）");
                if (_built) Teardown(false);
            }
            // anim 比对表必须清零：⛔ 不清会让上一局的 `anim==2` 与开局第一帧比出一个假"开火"
            // （同 BattleAudioView.OnBattleStarted 的处置）。
            _prevAnim.Clear();
            _curAnim.Clear();
            _lastShotMs.Clear();
            // 边沿判定按"窗口末帧时刻"去重 ⇒ 新局必须把它清成 NaN，否则新局的第一个窗口末帧会被当成
            // 上一局已判过的那一帧（同 id 的 hp / anim 比对全跳过）。
            _signalEndMs = float.NaN;
            _renderWorld.Clear();
            _latestTowersA = null;
            _latestTowersB = null;
            _fireShotsScheduled = 0;
            _fireShotsDeduped = 0;
            _fireShotsLost = 0;
            _fireShotsCancelled = 0;
            // 上一局的 hp 双缓冲必须清（同 `BattleAudioView.OnBattleStarted`）：不清会把上一局末尾的 hp
            // 与新局同一 id 的 hp 比对出假命中。
            _prevHp.Clear();
            _curHp.Clear();
            _prevTowerHp.Clear();
            _curTowerHp.Clear();
            _towerHitsObserved = 0;
            _towerHitFxPlayed = 0;
            _towerHitFxSkipped = 0;
            _towerHitFxCapped = 0;
            _towerHitPosWarned = false;
            _towerAimTotal = 0;
            _towerAimed = 0;
            _towerAimKept = 0;
            // 上一局没等到施法者的出牌弹道：同一场景连开新局时清掉（那些实体 id 属于上一局）。
            _pendingCasterShots.Clear();
            if (!_built) RebuildIfWanted("Events.Battle.Started"); // 兜底：万一 StationChanged 没到（例如直接由服务端推送进对局）
            // 「本局身份」必须在**建场之后**写：`Build()` 只负责画面、不清这两个字段，
            // 而首次进图时建场就发生在上面这一句里（`Started` 早于 `Scene.Load`）。
            _matchRoomId = n.room_id;
            _matchSeed = n.seed;
            // 精灵预热：画面已建 ⇒ 当场做；画面待建（首次进图，本事件早于 `Scene.Load`）⇒ 记下通知，由 `Build()` 补做。
            _warmNotify = n;
            if (_built) WarmBattleSprites();
            var regulation = n.timeline != null ? n.timeline.regulation_ms : 0;
            Game.Logger?.Info(LogTag,
                $"对局开始：my_team={_myTeam}({(_myTeam == 0 ? "BLUE" : "RED")}) 常规={regulation}ms " +
                $"我的手牌={HandToString(_myTeam == 0 ? n.hand_a : n.hand_b)} 下一张={(int)(_myTeam == 0 ? n.next_a : n.next_b)}");
        }

        // ── 放卡卡顿：战斗期会用到的精灵目录，在进图期先整目录抓一遍 ──

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
            var maps = 0;
            WarmCardList(n.deck_a, ref dirs, ref hits, ref frames, ref maps);
            WarmCardList(n.deck_b, ref dirs, ref hits, ref frames, ref maps);
            WarmCardList(n.hand_a, ref dirs, ref hits, ref frames, ref maps);
            WarmCardList(n.hand_b, ref dirs, ref hits, ref frames, ref maps);
            var cardDirs = dirs;

            // 覆盖 = 本类与 `EffectsView` 能播到的**全部**战斗期目录。⛔ 不是"常用那几个"：
            //    漏一个目录 ⇒ 该特效**第一次**播出时在战斗热路径里同步整目录加载（那一帧现卡一下）。
            //    投射物清单与 `EffectsView.TryGetProjectileFx` 同源（见 `EffectsView.ProjectileUses`）。
            var fixedUses = new[]
            {
                ResPaths.EffectDeploy,
                ResPaths.EffectDeathBlue, ResPaths.EffectDeathPurple, ResPaths.EffectDeathGround,
                ResPaths.EffectDeathParticle,
                ResPaths.EffectBlast, ResPaths.EffectArrow,
                ResPaths.EffectHit,
                ResPaths.EffectSpell, ResPaths.EffectSpellFlight, ResPaths.EffectSpellBarrel,
                ResPaths.EffectRangeRing,
            };
            var uses = new string[fixedUses.Length + EffectsView.ProjectileUses.Length
                                                      + EffectsView.ProjectileHitUses.Length];
            var k = 0;
            for (var i = 0; i < fixedUses.Length; i++) uses[k++] = fixedUses[i];
            for (var i = 0; i < EffectsView.ProjectileUses.Length; i++) uses[k++] = EffectsView.ProjectileUses[i];
            for (var i = 0; i < EffectsView.ProjectileHitUses.Length; i++) uses[k++] = EffectsView.ProjectileHitUses[i];

            for (var i = 0; i < uses.Length; i++)
            {
                var f = SpriteBank.LoadDir(ResPaths.EffectDir(uses[i]), SpriteBank.SpritePivotMode.UnifiedCanvasAnchor);
                dirs++;
                if (f.Length > 0) { hits++; frames += f.Length; }
            }

            // 判据行：`hits` 必须等于 `dirs`（取不到 = 目录名错 / 素材没落地 ⇒ 战斗期仍会首触并留 Warn）。
            Game.Logger?.Info(LogTag,
                $"精灵预热：卡组+手牌目录 {cardDirs} 个 + 战斗特效目录 {uses.Length} 个（含重复）⇒ " +
                $"命中 {hits} / 取不到 {dirs - hits}，共 {frames} 帧；帧号映射预建 {maps} 张；" +
                $"战斗期不再首触 LoadDir / 不再首建帧号映射");
        }

        /// <summary>
        /// 预热一批卡 id 的精灵目录（见 <see cref="WarmBattleSprites"/>）。目录口径与 <see cref="ResolveVisual"/>
        /// 同一条（服务端下发优先、兜底表接着）⇒ 预热的就是真正会加载的那个目录。
        /// 既没有下发也没有兜底项的卡跳过（法术卡走特效目录）。
        /// </summary>
        private void WarmCardList(int[] cardIds, ref int dirs, ref int hits, ref int frames, ref int maps)
        {
            if (cardIds == null) return;
            for (var i = 0; i < cardIds.Length; i++)
            {
                // 法术卡没有单位视图：它的 `sprite_dir` 是卡面目录（`Sprites/Cards/ui_spells_out`），
                // 按单位目录预热会取不到任何帧（Warn「取不到任何帧」）⇒ 这里直接跳过。
                CardInfo info;
                if (_cards.TryGetValue(cardIds[i], out info) && info != null && info.type == 1) continue;
                CardVisual v;
                if (!TryGetCardVisual(cardIds[i], out v) || string.IsNullOrEmpty(v.Dir)) continue;
                // 卡池可能晚于本方法到达（那时 `TryGetCardVisual` 走兜底表），所以除按 type 判外
                // 再按目录兜底判一次：法术卡的 `sprite_dir` 恒为卡面目录 `SpellArtSpriteDir`
                //（`Sprites/Cards/ui_spells_out`），它不是单位目录。
                if (v.Dir == ResPaths.SpellArtSpriteDir) continue;
                var path = v.IsBuilding ? ResPaths.BuildingDir(v.Dir) : ResPaths.UnitDir(v.Dir);
                // ★ 用**预载口径**（帧数组 +「帧号 → 数组下标」映射表一次建好，见引擎 `FrameBank.Preload`）：
                //   映射表是**按目录首次**建的（逐帧解析每个 `frame_NNN` 的名字），658 帧的目录上一次约 6.6 ms、
                //   117 帧的约 4.0 ms —— 建在出场那一帧就是"出一张卡 / 单位出场卡一下"里可归因的那一步。
                //   单位装配（`UnitView`）读同一张表（引擎按 目录+锚点 缓存 ⇒ 这里是**预建**）。
                //   表数落进上面的判据行，可离线核对"预热期建了几张表"。
                var pre = SpriteBank.Preload(path, SpriteBank.SpritePivotMode.UnifiedCanvasAnchor);
                dirs++;
                if (pre.Frames.Length > 0)
                {
                    hits++;
                    frames += pre.Frames.Length;
                    if (pre.FrameMap.Length > 0) maps++;
                }
            }
        }

        private void OnSnapshot(BattleSnapshot s)
        {
            if (s == null) return;
            if (!_built) RebuildIfWanted("Events.Battle.Snapshot");

            var first = Interp.SnapshotCount == 0;

            // 载荷格：优先复用已被挤出历史的那一格（⛔ 不每帧新建字典）。
            var slot = _freeSlots.Count > 0 ? _freeSlots.Dequeue() : new Dictionary<int, EntitySnapshot>();
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

            // 时间轴 / 单调性 / `server_ms<=0` 的退化推定与留痕全由插值器负责（见 SnapshotInterpolator.Push）：
            // 到达只入历史，⛔ 不重置渲染时钟、⛔ 不换正在渲染的窗口。
            if (!Interp.Push(s.server_ms, slot))
            {
                _freeSlots.Enqueue(slot);   // 未入历史 ⇒ 这一格仍空闲
                return;
            }
            _liveSlots.Enqueue(slot);
            // 历史已装满 ⇒ 队首那一格刚被挤出，回收复用。
            if (_liveSlots.Count > Interp.Options.HistSlots) _freeSlots.Enqueue(_liveSlots.Dequeue());

            // 留一份"最近实体名单"给塔开火事件用（事件通道不带目标，见 `_lastEntities` 的注释）。
            // ⛔ 只存引用不拷贝：本类对它是只读的，拷贝反而每帧多一次分配。
            _lastEntities = list;
            // 塔同理（渲染时钟上的塔命中判定要用，见 `_latestTowersA`）。
            _latestTowersA = s.towers_a;
            _latestTowersB = s.towers_b;

            // 施法者进快照了 ⇒ 把挂起的出牌弹道补播（起点 = 施法者实体坐标，见 ResolvePendingCasterShots）。
            ResolvePendingCasterShots();
            if (first)
            {
                Game.Logger?.Info(LogTag,
                    $"渲染时钟已对齐：server_ms={Interp.NewestSnapshotMs:F0} - 目标落后 {Interp.Options.TargetLagMs:F0}ms" +
                    $"（{Interp.Options.RenderLagIntervals:F0}×{GameConst.SnapshotIntervalMs}ms = 抖动缓冲）= 时钟起于 {Interp.RenderClockMs:F0} seq={s.seq}" +
                    "（只在首帧对齐一次；此后快照到达只入历史，⛔ 不重置时钟、⛔ 不换正在渲染的窗口）");
            }

            ApplyTowers(s);

            // 协议没有「命中」/「开火」事件 ⇒ 这两类表现只能从快照反推，但**不在这一帧做**：
            // 快照到达那一刻，渲染时钟还落后它一个抖动缓冲（见类注释三），此时出特效会比画面上的精灵
            // 早一个缓冲（特效锚在最前沿、精灵锚在插值窗口）⇒ 判定与出特效都放到渲染时钟上做
            // （见 TickRender → TrackCombatAtRender）。
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
                        // 的 `Death_blue` / `Death_purple` / `death_ground` / `death_particle`）。蓝方播蓝族、
                        // 红方播紫族，之后都跟一段地面扬尘与一层死亡粒子。只覆盖致死那一击（原版没有全局受击特效）。
                        // 三段各按**自己的**帧数与原版时长播（闪族 = 首末像素帧跨度 ÷ fps，扬尘 / 粒子 = timeline ÷ fps，
                        // 见 `DeathBlueSeconds` / `DeathPurpleSeconds` / `DeathGroundSeconds` / `DeathParticleSeconds` 的出处块）。
                        // ⛔ 不走 `EffectsView.Play`：它按"帧数 ÷ `DefaultFps`(14)"算时长 ⇒ 与原版无关。
                        // 起播顺序 = 闪（蓝族 / 紫族）→ 扬尘 → 粒子。
                        if (_effects != null)
                        {
                            var at = GameConst.MilliToWorld(e.x_milli, e.y_milli);
                            var deathUse = e.team == 0 ? ResPaths.EffectDeathBlue : ResPaths.EffectDeathPurple;
                            var deathFirst = e.team == 0 ? ResPaths.EffectDeathBlueFirst : ResPaths.EffectDeathPurpleFirst;
                            var deathCount = e.team == 0 ? ResPaths.EffectDeathBlueCount : ResPaths.EffectDeathPurpleCount;
                            var burstSeconds = e.team == 0 ? DeathBlueSeconds : DeathPurpleSeconds;
                            _effects.PlayTimed(at, deathUse, deathFirst, deathCount,
                                EffectsView.WorldSize, burstSeconds);
                            _effects.PlayTimed(at, ResPaths.EffectDeathGround, ResPaths.EffectDeathGroundFirst,
                                ResPaths.EffectDeathGroundCount, EffectsView.WorldSize, DeathGroundSeconds);
                            _effects.PlayTimed(at, ResPaths.EffectDeathParticle, ResPaths.EffectDeathParticleFirst,
                                ResPaths.EffectDeathParticleCount, EffectsView.WorldSize, DeathParticleSeconds);
                            Game.Logger?.Info(LogTag,
                                $"死亡特效：ent={e.entity_id} team={e.team} 位置=({at.x:F2},{at.y:F2}) " +
                                $"闪={deathUse} f{deathFirst}..f{deathFirst + deathCount - 1}（{deathCount} 帧 / " +
                                $"{burstSeconds:F3}s = 首末像素帧跨度 ÷ {DeathFxFps:F0}）扬尘={ResPaths.EffectDeathGround} " +
                                $"f{ResPaths.EffectDeathGroundFirst}..f{ResPaths.EffectDeathGroundFirst + ResPaths.EffectDeathGroundCount - 1}" +
                                $"（{ResPaths.EffectDeathGroundCount} 帧 / {DeathGroundSeconds:F3}s = timeline 21 ÷ {DeathFxFps:F0}）" +
                                $"粒子={ResPaths.EffectDeathParticle} f{ResPaths.EffectDeathParticleFirst}.." +
                                $"f{ResPaths.EffectDeathParticleFirst + ResPaths.EffectDeathParticleCount - 1}" +
                                $"（{ResPaths.EffectDeathParticleCount} 帧 / {DeathParticleSeconds:F3}s = timeline 21 ÷ {DeathFxFps:F0}）" +
                                $" 出处 策划/单位动画分组表.md:4697-4700");
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
                        // 落位表盘按**出牌**驱动（一次出牌只有一条事件），⛔ 不再由逐单位的 EvSpawn 开
                        //（群体卡会开多块 / 重开 ⇒ 看起来转好几圈），见 PlayDeployRingAt。
                        ScheduleDeployRing(e);
                        break;

                    case EventKindTowerShoot:
                        // 塔开火（`EvTowerShoot`）：有速度且找得到目标时飞一条弹道。
                        PlayTowerShot(e);
                        break;

                    case EventKindTowerActivated:
                        // 塔激活（`EvTowerActivated`）：把该国王塔的**炮塔层打开**（这一层在原版未激活时
                        // alpha=0 ⇒ "惊醒"在画面上就是它亮起来，出处见 ArenaView.KingTurretAlphaZeroCt）。
                        // 音效仍由 `BattleAudioView` 播 `king_activate_01`（两边互不重复）。
                        PlayTowerActivated(e);
                        break;

                    case EventKindElixirFull:
                        // kind==4 圣水满：**只有音效、没有特效帧**（`BattleAudioView` 播 `ElixirFull`；
                        // `ResPaths` 的特效区段里没有对应用途目录）。显式列出这个 case，是为了让下面的
                        // `default` 只表示"**协议新增的未知 kind**"。
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
            OnPreviewCardPool(cards);   // 静态索引（预览查询用）与实例索引同一份来源
            for (var i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                if (c == null) continue;
                _cards[c.id] = c;
            }
            Game.Logger?.Info(LogTag, $"卡池已索引：{_cards.Count} 张（远程判定 / 弹道速度由此查表）");
        }

        /// <summary>
        /// 出牌落地表现（`EvSpawn`）：**按渲染时钟**排一次落地标记（见 <see cref="ScheduleDeploy"/>），
        /// 到点由 <see cref="PlayDeployAt"/> 在**事件自带落点**定格播原版 `deploy_arrows_effect`
        /// （f119，见上方出处块）与落地扬尘。
        /// <para>
        /// ⛔ 落位表盘**不在这里**开（那由一次出牌的 <see cref="PlayDeployRingAt"/> 负责），
        /// 本方法只管"单位出现的那一下"。
        /// </para>
        /// <para>
        /// ⛔ 单位本身不在这里生成 —— 单位的真假一律以快照的 `entities` 为准（同 <see cref="OnBattleEventNotify"/>
        /// 的注释）；本方法只负责"特效层的那一下"。
        /// </para>
        /// </summary>
        private void PlayDeploy(BattleEvent e)
        {
            if (_effects == null) return;
            ScheduleDeploy(GameConst.MilliToWorld(e.x_milli, e.y_milli), e.card_id, e.team, e.entity_id);
        }

        /// <summary>
        /// 把落地标记排到**渲染时钟**上：事件是墙上时间到的，而画面还落后一个抖动缓冲
        /// （<c>Interp.NewestSnapshotMs − Interp.RenderClockMs</c>）—— 单位精灵要到渲染时钟追上这一帧快照
        /// 才出现。延迟这段滞后量播标记，标记才与单位的显示同帧（见 <see cref="_pendingImpacts"/>）。
        /// </summary>
        private void ScheduleDeploy(Vector2 world, int cardId, int team, int entityId)
        {
            _pendingImpacts.Add(new PendingImpact
            {
                Mode = ImpactModeDeploy,
                World = world,
                CardId = cardId,
                Team = team,
                EntityId = entityId,
                LeftMs = Mathf.Max(0f, Interp.NewestSnapshotMs - Interp.RenderClockMs),
                Label = "deploy:card=" + cardId,
            });
        }

        /// <summary>
        /// 到点播一次出牌落地标记（落点经 <see cref="WorldToTile"/> 换算）。
        /// <para>
        /// 落地扬尘 + 绿色落地标记**共用同一把"一次出牌"去重钥匙**：`EvSpawn` 是逐单位发的
        /// （一张 3 单位的卡 = 3 条事件；召唤物也是同父卡 id 的一条条 spawn），而原版玩家看到的是
        /// "这张卡在就位"的**一枚**标记。钥匙里含**阵营**：敌我同卡在 <see cref="DeployDedupeSeconds"/>
        /// 内先后落下时，敌方的那个仍要画自己的一枚。
        /// </para>
        /// <para>
        /// 落位表盘不在这里 —— 它由 <see cref="PlayDeployRingAt"/> 在出牌时开一块，走一圈就结束。
        /// </para>
        /// </summary>
        private void PlayDeployAt(int team, int cardId, int entityId, Vector2 world)
        {
            if (_effects == null) return;

            var sameCard = cardId == _lastDeployCardId
                           && team == _lastDeployTeam
                           && Time.unscaledTime - _lastDeployTime < DeployDedupeSeconds;
            if (sameCard)
            {
                _deployDupeSkipped++;
                Game.Logger?.Info(LogTag,
                    $"出牌落地标记去重（同一次出牌的后续单位）：ent={entityId} card={cardId} " +
                    $"本局累计：落位={_deployFxPlayed} 去重={_deployDupeSkipped}");
                return;
            }
            _lastDeployCardId = cardId;
            _lastDeployTeam = team;
            _lastDeployTime = Time.unscaledTime;

            // 出牌 → 单位出现之间的间隔：表盘与本条都由出牌事件排到同一段渲染滞后上，
            // 正常应同帧开始（间隔 0）。> 0.3 s 即"表盘在走、场上还没东西"的空窗（见 _lastDeployRingTime）。
            var ringStarted = _lastDeployRingCardId == cardId && _lastDeployRingTeam == team;
            var ringGap = ringStarted ? Mathf.Max(0f, Time.unscaledTime - _lastDeployRingTime) : 0f;
            Game.Logger?.Info(LogTag,
                $"出牌→落地：ent={entityId} card={cardId} 间隔={ringGap:F3}s" +
                (ringStarted ? "（表盘已先开）" : "（表盘与本条同帧开始）"));

            // 绿色落地标记：一枚（理由见上）。⛔ 只影响标记的重复绘制，不影响任何实体生成（单位一律以快照为准）。
            // ⛔ 原版的落位表现 = 这一枚绿色上箭头（`deploy_arrows_effect` f119）+ 落位表盘，没有别的落地元素
            //    ⇒ 这里不再叠别的段（原版那张 21 张卡共用的落地表见 `策划/卡牌表现对照表.md`）。
            var before = _effects.SpawnedTotal;
            _effects.PlayHold(world, ResPaths.EffectDeploy, ResPaths.EffectDeployFirst,
                EffectsView.WorldSize, DeployArrowSeconds);
            if (_effects.SpawnedTotal > before) _deployFxPlayed++;
            Game.Logger?.Info(LogTag,
                $"出牌落地特效：ent={entityId} team={team} card={cardId} " +
                $"落点=({world.x:F2},{world.y:F2}) 帧=f{ResPaths.EffectDeployFirst}" +
                $"（{ResPaths.EffectDeploy}，{ResPaths.EffectDeployCount} 帧）定格={DeployArrowSeconds:F3}s " +
                $"本局累计：落位={_deployFxPlayed} 去重={_deployDupeSkipped}");
        }

        /// <summary>
        /// 把**一次出牌**的落位表盘排到渲染时钟上（`EvPlayCard` 每条出牌只有一条事件）。
        /// <para>
        /// 延迟口径与 <see cref="ScheduleDeploy"/> 相同：表盘要在**单位出现的同一帧**开始走，
        /// 否则会出现"表盘已经转了一半、场上还没有东西"的空窗。
        /// </para>
        /// </summary>
        private void ScheduleDeployRing(BattleEvent e)
        {
            _pendingImpacts.Add(new PendingImpact
            {
                Mode = ImpactModeDeployRing,
                World = GameConst.MilliToWorld(e.x_milli, e.y_milli),
                CardId = e.card_id,
                Team = e.team,
                LeftMs = Mathf.Max(0f, Interp.NewestSnapshotMs - Interp.RenderClockMs),
                Label = "deployRing:card=" + e.card_id,
            });
        }

        /// <summary>
        /// 在落点开**一块**落位表盘，扫过片严格 0 → 360 转一圈（时长 = 该卡落位期，见 <see cref="DeploySecondsOf"/>）。
        /// <para>
        /// 法术卡不生成单位、没有落位期 ⇒ 不开表盘（原版也没有那颗钟）。
        /// 群体卡只开这一块：`EvSpawn` 逐单位到齐时 ⛔ 不再重开（见 <see cref="PlayDeployAt"/>）。
        /// </para>
        /// </summary>
        private void PlayDeployRingAt(int team, int cardId, Vector2 world)
        {
            CardInfo card;
            if (_cards.TryGetValue(cardId, out card) && card != null && card.type == CardTypeSpell)
            {
                Game.Logger?.Info(LogTag,
                    $"出牌落位表盘跳过：card={cardId} 是法术卡（不生成单位，没有落位期）");
                return;
            }

            var tile = WorldToTile(world);
            var seconds = DeploySecondsOf(cardId);
            ShowDeployRing(tile, 1f, seconds, team != _myTeam, cardId);
            _lastDeployRingCardId = cardId;
            _lastDeployRingTeam = team;
            _lastDeployRingTime = Time.unscaledTime;
            Game.Logger?.Info(LogTag,
                $"出牌落位表盘：card={cardId} team={team} 格=({tile.x:F2},{tile.y:F2}) 时长={seconds:F2}s" +
                $"（同屏表盘数={PlacementIndicator.ActiveDeployTimerCount}）");
        }

        /// <summary>
        /// 这张卡的**落位期**（秒）= 服务端随卡池下发的 `CardInfo.deploy_ms / 1000`。
        /// <para>
        /// 这就是单位从出现（`EvSpawn` 那一刻）到可行动之间的时长 —— 服务端 `core.Battle.PlayCard`
        /// 生成该单位时把它记在实体的 `deployMsLeft` 上（`core/battle.go:235-239`），
        /// 所以落位计时件走完一圈的时刻 = 单位可行动的时刻。
        /// 卡池还没到 / 该行没下发 ⇒ 退回 <see cref="DeployPeriodFallbackSeconds"/> 并留痕一次。
        /// </para>
        /// </summary>
        private float DeploySecondsOf(int cardId)
        {
            CardInfo card;
            if (_cards.TryGetValue(cardId, out card) && card != null && card.deploy_ms > 0)
                return card.deploy_ms / 1000f;
            if (!_deployMsMissingWarned)
            {
                _deployMsMissingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"卡 {cardId} 的 CardInfo.deploy_ms 取不到（卡池还没到 / 该行没下发）⇒ 落位计时件退回兜底 " +
                    $"{DeployPeriodFallbackSeconds:F2}s（**不是该卡真实落位期**，只报一次）");
            }
            return DeployPeriodFallbackSeconds;
        }

        /// <summary>
        /// 排一条**到达落点**才播的特效（法术落地段 / 投射物命中段），延迟 = 该段的飞行时长。
        /// 到达后由 <see cref="ProcessPendingSpellImpacts"/> 每帧推出，⛔ 不另起第二套计时。
        /// </summary>
        private void ScheduleImpact(Vector2 world, string use, int first, int count, float seconds, float hold,
            float flightSeconds, string label, bool spell)
        {
            if (_effects == null || string.IsNullOrEmpty(use)) return;
            _pendingImpacts.Add(new PendingImpact
            {
                World = world,
                Use = use,
                First = first,
                Count = count,
                Seconds = seconds,
                Hold = hold,
                LeftMs = Mathf.Max(0f, flightSeconds) * 1000f,
                Label = label,
                Spell = spell,
            });
        }

        /// <summary>排一条法术的落地段（帧位与时长见 <see cref="SpellFxTable.Entry"/>）。</summary>
        private void ScheduleSpellImpact(Vector2 landing, SpellFxTable.Entry fx, float flightSeconds)
        {
            ScheduleImpact(landing, fx.Use, fx.First, fx.Count, fx.Seconds, 0f, flightSeconds, "spell:" + fx.Use, true);
        }

        /// <summary>
        /// 排一条投射物的命中段。该投射物在原版图集里**没有**专属命中段时什么都不排（计入
        /// <see cref="ProjectileHitFxSkipped"/>）——这不是失败，见 <see cref="EffectsView.TryGetProjectileHitFx"/>。
        /// </summary>
        private void ScheduleProjectileHit(Vector2 landing, string projectileKey, int sourceId, float flightSeconds)
        {
            EffectsView.ProjectileFx hitFx;
            float hold;
            if (!EffectsView.TryGetProjectileHitFx(projectileKey, out hitFx, out hold))
            {
                _projHitFxSkipped++;
                return;
            }
            ScheduleImpact(landing, hitFx.Use, hitFx.First, hitFx.Count, 0f, hold, flightSeconds,
                "proj:" + projectileKey, false);
        }

        /// <summary>
        /// 溅射命中的红色描边圆的**显示时长**（秒）。规格给定区间 0.12~0.25s，取 0.2。
        /// </summary>
        private const float AoeRingHoldSeconds = 0.2f;

        /// <summary>本局画出的溅射命中红圈数。</summary>
        private int _aoeRingsPlayed;

        /// <summary>本局溅射命中红圈播不出去的次数（特效层没有 / 素材取不到）。</summary>
        private int _aoeRingsSkipped;

        /// <summary>本局画出的溅射命中红圈数（自检 / 验收读数）。</summary>
        public int AoeRingsPlayed { get { return _aoeRingsPlayed; } }

        /// <summary>本局溅射命中红圈播不出去的次数。</summary>
        public int AoeRingsSkipped { get { return _aoeRingsSkipped; } }

        /// <summary>
        /// 排一条**溅射命中红圈**：命中点画红色描边圆，半径 = 攻击者卡池行的 `aoe_radius_milli / 1000`
        /// （格；女巫走投射物行 `WitchProjectile` 的 `aoe_radius_mt`，出处 `server/game/table/tsv/unit.tsv`，
        /// 下发见 `server/game/logic/cardtable.go` 的 `cardAoeRadius`）；`0` = 单体伤害（火枪手等）⇒ 不画。
        /// <para>
        /// 出现时刻 = 该弹道**到达落点**那一刻（<paramref name="flightSeconds"/> 之后；近战无飞行段传 `0`
        /// = 到点即画），复用 <see cref="_pendingImpacts"/> 队列（⛔ 不另起第二套计时）。
        /// 协议不改：攻击者只有在开火链路里才拿得到（`PlayBattleShot` 的 attacker.card_id），
        /// 命中判定（比 hp）拿不到攻击者 ⇒ 红圈只挂在开火链路上。
        /// </para>
        /// </summary>
        private void ScheduleAoeRing(Vector2 world, CardInfo card, int attackerId, float flightSeconds)
        {
            if (_effects == null || card == null || card.aoe_radius_milli <= 0) return;
            _pendingImpacts.Add(new PendingImpact
            {
                Mode = ImpactModeAoeRing,
                World = world,
                AoeRadiusTiles = card.aoe_radius_milli / 1000f,
                LeftMs = Mathf.Max(0f, flightSeconds) * 1000f,
                Label = "aoe:ent=" + attackerId + " card=" + card.key,
                CardId = card.id,
            });
        }

        /// <summary>到点画一次溅射命中红圈（见 <see cref="ScheduleAoeRing"/>）。</summary>
        private void PlayAoeRingNow(PendingImpact p)
        {
            if (_effects == null) { _aoeRingsSkipped++; return; }
            var before = _effects.SpawnedTotal;
            _effects.PlayRingHold(p.World, p.AoeRadiusTiles, AoeRingHoldSeconds);
            if (_effects.SpawnedTotal > before) _aoeRingsPlayed++;
            else _aoeRingsSkipped++;

            // 数值类证据 = 运行时日志行：半径读数 = 卡池 aoe_radius_milli / 1000。
            Game.Logger?.Info(LogTag,
                $"溅射命中红圈：{p.Label} 位置=({p.World.x:F2},{p.World.y:F2}) " +
                $"半径={p.AoeRadiusTiles:F2}格（aoe_radius_milli={Mathf.RoundToInt(p.AoeRadiusTiles * 1000f)}）" +
                $" 描边=f{ResPaths.EffectRangeRingFriendly}（{ResPaths.EffectRangeRing} 染红）" +
                $" 显示={AoeRingHoldSeconds:F3}s" +
                $" 本局累计：红圈={_aoeRingsPlayed} 跳过={_aoeRingsSkipped}");
        }

        /// <summary>
        /// 推 <see cref="_pendingImpacts"/> 队列（每帧一次，在 `Update` 里调用）：
        /// 到点就按该条自己的 <see cref="PendingImpact.Mode"/> 执行 ——
        /// 特效条目里 <see cref="PendingImpact.Hold"/> &gt; 0 走 <see cref="EffectsView.PlayHold"/>
        /// （单像素帧 export 必须定格播），否则走 <see cref="EffectsView.PlayTimed"/>
        /// （整段时长 = 原版该 export 的时间轴）；开火 / 出牌落地条目见 <see cref="PlayImpact"/>。
        /// </summary>
        private void ProcessPendingSpellImpacts()
        {
            if (_pendingImpacts.Count == 0) return;
            var dtMs = Time.unscaledDeltaTime * 1000f;
            for (var i = _pendingImpacts.Count - 1; i >= 0; i--)
            {
                var p = _pendingImpacts[i];
                p.LeftMs -= dtMs;
                if (p.LeftMs > 0f) { _pendingImpacts[i] = p; continue; }
                _pendingImpacts.RemoveAt(i);
                PlayImpact(p);
            }
        }

        /// <summary>
        /// 执行一条到点的待办（⛔ 播不出去必须留痕，见 <see cref="_impactMissingWarned"/>）：
        /// 开火条目与出牌落地条目各走自己的方法，其余 = 到达落点的特效（计数）。
        /// </summary>
        private void PlayImpact(PendingImpact p)
        {
            if (p.Mode == ImpactModeFire) { FireBattleShotNow(p); return; }
            if (p.Mode == ImpactModeDeploy) { PlayDeployAt(p.Team, p.CardId, p.EntityId, p.World); return; }
            if (p.Mode == ImpactModeDeployRing) { PlayDeployRingAt(p.Team, p.CardId, p.World); return; }
            if (p.Mode == ImpactModeAoeRing) { PlayAoeRingNow(p); return; }
            if (_effects == null) return;

            var before = _effects.SpawnedTotal;
            if (p.Hold > 0f)
                _effects.PlayHold(p.World, p.Use, p.First, EffectsView.WorldSize, p.Hold);
            else
                _effects.PlayTimed(p.World, p.Use, p.First, p.Count, EffectsView.WorldSize, p.Seconds);

            var played = _effects.SpawnedTotal > before;
            if (p.Spell)
            {
                if (played) _spellImpactPlayed++; else _spellImpactSkipped++;
            }
            else
            {
                if (played) _projHitFxPlayed++; else _projHitFxSkipped++;
            }

            if (!played && !_impactMissingWarned)
            {
                _impactMissingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"到达落点的特效播不出去：{p.Label} 用途目录 \"{p.Use}\" 首帧 f{p.First} 取不到" +
                    "（素材没落地 / 没被 Unity 导入 / 特效层同屏上限）⇒ 检查该目录（只报一次）");
            }

            Game.Logger?.Info(LogTag,
                $"到达落点特效：{p.Label} 位置=({p.World.x:F2},{p.World.y:F2}) 用途={p.Use} " +
                $"帧=f{p.First}..f{p.First + p.Count - 1}（{p.Count} 帧，" +
                $"{(p.Hold > 0f ? "定格 " + p.Hold.ToString("F3") + "s" : "整段 " + p.Seconds.ToString("F3") + "s")}）" +
                $" 本局累计：法术落地={_spellImpactPlayed}/跳过{_spellImpactSkipped} " +
                $"投射物命中={_projHitFxPlayed}/跳过{_projHitFxSkipped}");
        }

        /// <summary>
        /// 在**渲染时钟**上补两类"协议没有事件"的表现（由 <see cref="TickRender"/> 每帧调）——
        /// 判定的边沿取"插值窗口末帧"（= 本帧精灵正在显示的那一帧快照），位置一律取**本帧的插值坐标**
        /// （<see cref="_renderWorld"/>）⇒ 特效与精灵同帧同位。
        /// <list type="number">
        /// <item><b>战斗中开火</b>：逐 id 判"是否处在攻击档"（`anim == 2`）且该卡为远程
        ///   （`CardInfo.projectile_key` 非空）⇒ 排一条**延迟前摇**（`load_time_ms`）的弹道
        ///   （见 <see cref="ScheduleBattleShot"/>）。节拍 = 该单位的 `hit_speed`
        ///   （`UnitAnimTable.Table[dir].HitSpeedMs`，与 `UnitView` 播攻击档用的是同一份表），
        ///   因为服务端的 `anim` 是**状态**不是边沿（`server/game/core/combat.go:100` 前摇起就置 2、
        ///   `:112` 出刀再置一次；窗口由 `server/game/core/battle.go:672-688` 的 `stepAnimHold` 到期收回）
        ///   ⇒ 只凭"上升沿"会漏掉后续每一次挥砍。</item>
        /// <item><b>命中</b>：逐 id 比对前后两帧快照的 `hp`（协议没有命中事件，与 `BattleAudioView`
        ///   喂命中音效同一规则）⇒ 在**该实体本帧的插值位置**播闪光（见 <see cref="PlayHitFx"/>）。</item>
        /// </list>
        /// ⛔ 同一个"窗口末帧"只判一次（<see cref="_signalEndMs"/>）：窗口末帧每帧都在，
        /// 判多次会把同一次命中 / 同一刀重复播。
        /// </summary>
        private void TrackCombatAtRender()
        {
            var list = Interp.WindowEndPayload as Dictionary<int, EntitySnapshot>;
            if (list == null) return;
            var endMs = Interp.WindowEndMs;
            if (endMs == _signalEndMs) return;
            _signalEndMs = endMs;

            _curAnim.Clear();
            foreach (var kv in list)
            {
                var e = kv.Value;
                if (e == null) continue;
                _curAnim[e.id] = e.anim;

                // 攻击档的**释放**（上升沿的另一半，见下方开火排期）：正常释放发生在出刀后 ≥200ms
                // （`server/game/core/combat.go` 的 `holdAttackAnim` 保 4 tick），那条排期早已到点发出 ⇒ 这里是空操作；
                // 若还有没到点的排期，说明这次前摇被**中断**了（目标死 / 出射程 / 自己死）⇒ 撤掉那一发。
                if (e.anim != UnitAnimTable.Attack)
                {
                    int wasAnim;
                    if (_prevAnim.TryGetValue(e.id, out wasAnim) && wasAnim == UnitAnimTable.Attack)
                        CancelPendingFire(e.id, "攻击档释放（前摇被中断）");
                    continue;
                }
                if (e.deploy_ms > 0) continue;              // 部署中不攻击（服务端 `acquirable()`：entity.go:170）

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
                    || (endMs - lastShot) >= anim.HitSpeedMs;
                if (!due) continue;

                ScheduleBattleShot(e, anim.LoadTimeMs, endMs, list);
            }

            // 清理已离场的实体（⛔ 不用 `_prevAnim`：它是双缓冲的，这里直接按本帧名单剪）。
            _shotPrune.Clear();
            foreach (var kv in _lastShotMs)
                if (!_curAnim.ContainsKey(kv.Key)) _shotPrune.Add(kv.Key);
            for (var i = 0; i < _shotPrune.Count; i++) _lastShotMs.Remove(_shotPrune[i]);

            // ── 命中：逐 id 比对前后两帧快照的 `hp`（协议没有命中事件，与 `BattleAudioView` 喂命中音效同一规则）──
            // 三条防误报：① 新单位出场那帧不判（`_prevHp` 里还没有它）；② 只有 hp 变小才算；
            // ③ 单帧最多 `MaxHitFxPerSnapshot` 个（理由见该常量）。
            _curHp.Clear();
            var hitSlots = 0;
            foreach (var kv in list)
            {
                var e = kv.Value;
                if (e == null) continue;
                _curHp[e.id] = e.hp;

                int prevHp;
                if (!_prevHp.TryGetValue(e.id, out prevHp)) continue;
                if (e.hp >= prevHp) continue;

                _hitsObserved++;
                if (hitSlots >= MaxHitFxPerSnapshot) continue;
                hitSlots++;
                PlayHitFx(e, prevHp);
            }

            var swapHp = _prevHp; _prevHp = _curHp; _curHp = swapHp;

            // ── 塔的命中：**同一条口径**（逐 id 比 hp），只是塔不在 `entities` 里（由 `towers_a` / `towers_b`
            //    报告，见类注释"四"，故它们也不在插值窗口的载荷里 ⇒ 取 `OnSnapshot` 留的最近一份，
            //    见 `_latestTowersA`）。单位那一条路径的语义一个字都不改，塔单独走这一遍。
            //    三条防误报同上：新出现的塔 id 不判 / 只有 hp 变小才算 / 单帧上限复用 `MaxHitFxPerSnapshot`。
            _curTowerHp.Clear();
            var towerHitSlots = 0;
            for (var team = 0; team < 2; team++)
            {
                var towers = team == 0 ? _latestTowersA : _latestTowersB;
                if (towers == null) continue;
                for (var i = 0; i < towers.Length; i++)
                {
                    var t = towers[i];
                    if (t == null) continue;
                    var key = TowerHpKey(team, t.id);
                    _curTowerHp[key] = t.hp;

                    int prevTowerHp;
                    if (!_prevTowerHp.TryGetValue(key, out prevTowerHp)) continue;
                    if (t.hp >= prevTowerHp) continue;

                    _towerHitsObserved++;
                    if (towerHitSlots >= MaxHitFxPerSnapshot) { _towerHitFxCapped++; continue; }
                    towerHitSlots++;
                    PlayTowerHitFx(team, i, t, prevTowerHp);
                }
            }
            var swapTowerHp = _prevTowerHp; _prevTowerHp = _curTowerHp; _curTowerHp = swapTowerHp;

            var swapAnim = _prevAnim; _prevAnim = _curAnim; _curAnim = swapAnim;
        }

        /// <summary>
        /// 该实体**本帧精灵显示的位置**（= <see cref="ApplyEntities"/> 本帧算出的插值坐标）。
        /// 已离场的（本帧没渲染）退回快照自带的坐标并留痕一次。
        /// </summary>
        private Vector2 RenderWorldOf(EntitySnapshot e)
        {
            Vector2 w;
            if (_renderWorld.TryGetValue(e.id, out w)) return w;
            if (!_renderPosMissingWarned)
            {
                _renderPosMissingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"ent={e.id} 本帧没有插值坐标（未渲染 / 已离场）⇒ 该次表现退回快照自带坐标（只报一次）");
            }
            return GameConst.MilliToWorld(e.x_milli, e.y_milli);
        }

        /// <summary>
        /// 命中闪光：在**被打中那个实体本帧的插值位置**播原版 `effect_Hit1`
        /// （帧段与时长见 <see cref="ResPaths.EffectHit"/> / <see cref="ResPaths.EffectHitSeconds"/>）。
        /// <para>
        /// 触发 = 渲染时钟走到"窗口末帧里该实体 `hp` 下降"的那一帧（协议没有命中事件 ⇒ 只能这么推，
        /// 见 <see cref="TrackCombatAtRender"/>）。位置 = 该帧精灵的显示位置
        /// （<see cref="RenderWorldOf"/>）；快照自带的坐标是**插值窗口末端**的坐标，直接按它播会比
        /// 画面上的精灵超前一个快照间隔（日志里同时打印两者的差，可核对）。
        /// </para>
        /// </summary>
        private void PlayHitFx(EntitySnapshot target, int prevHp)
        {
            if (_effects == null) return;
            var at = RenderWorldOf(target);
            var snap = GameConst.MilliToWorld(target.x_milli, target.y_milli);
            var before = _effects.SpawnedTotal;
            _effects.PlayTimed(at, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount,
                EffectsView.WorldSize, ResPaths.EffectHitSeconds);
            if (_effects.SpawnedTotal > before) _hitFxPlayed++;
            // 数值类证据 = 运行时日志行（本类不靠截图判命中）：目标 id / 队伍 / 掉血前后 / 帧段 /
            // 本帧**闪光位置**与**快照坐标**各自到精灵插值位置的距离（前者恒 0，后者 = 一个快照间隔的位移）。
            Game.Logger?.Info(LogTag,
                $"命中闪光：ent={target.id} team={target.team} hp {prevHp}→{target.hp} " +
                $"闪光位置=({at.x:F2},{at.y:F2}) 单位精灵插值位置=({at.x:F2},{at.y:F2}) 差=0.000格" +
                $" 快照坐标=({snap.x:F2},{snap.y:F2})（按它播则差={Vector2.Distance(at, snap):F3}格）" +
                $" 帧=f{ResPaths.EffectHitFirst}..f{ResPaths.EffectHitFirst + ResPaths.EffectHitCount - 1}" +
                $"（{ResPaths.EffectHit}，{ResPaths.EffectHitCount} 帧）渲染时钟={Interp.RenderClockMs:F0}ms " +
                $"本局累计：命中={_hitsObserved} 播出={_hitFxPlayed}");
        }

        /// <summary>
        /// **塔**的命中闪光：与 <see cref="PlayHitFx"/> 同一支特效、同一时长，只是位置来自塔视图
        /// （塔不是快照实体 ⇒ 它自己没有坐标，见 <see cref="ArenaView.TryTowerHitPoint"/>）。
        /// <para>播不出去一律留痕并计跳过（特效层没有 / 塔视图取不到落点），⛔ 不静默。</para>
        /// </summary>
        /// <param name="team">队伍：0=BLUE 1=RED。</param>
        /// <param name="ordinal">该队 roster 下标：0=国王塔、1=左公主塔、2=右公主塔。</param>
        /// <param name="t">该塔的快照条目。</param>
        /// <param name="prevHp">上一帧的 hp（日志用）。</param>
        private void PlayTowerHitFx(int team, int ordinal, TowerState t, int prevHp)
        {
            if (_effects == null) { _towerHitFxSkipped++; return; }

            Vector2 at;
            if (_arena == null || !_arena.TryTowerHitPoint(team, ordinal, out at))
            {
                _towerHitFxSkipped++;
                if (!_towerHitPosWarned)
                {
                    _towerHitPosWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"塔受击闪光：team={team} roster={ordinal} id={t.id} 在塔视图里取不到落点" +
                        "（塔视图未建 / 下标越界）⇒ 这一发不播（只报一次）");
                }
                return;
            }

            var before = _effects.SpawnedTotal;
            _effects.PlayTimed(at, ResPaths.EffectHit, ResPaths.EffectHitFirst, ResPaths.EffectHitCount,
                EffectsView.WorldSize, ResPaths.EffectHitSeconds);
            if (_effects.SpawnedTotal > before) _towerHitFxPlayed++;
            else _towerHitFxSkipped++;

            Game.Logger?.Info(LogTag,
                $"塔受击闪光：team={team} roster={ordinal} id={t.id} kind={t.kind} hp {prevHp}→{t.hp} " +
                $"位置=({at.x:F2},{at.y:F2}) 帧=f{ResPaths.EffectHitFirst}..f{ResPaths.EffectHitFirst + ResPaths.EffectHitCount - 1}" +
                $"（{ResPaths.EffectHit}，{ResPaths.EffectHitCount} 帧）" +
                $"本局累计：塔命中={_towerHitsObserved} 播出={_towerHitFxPlayed} 跳过={_towerHitFxSkipped} 上限丢={_towerHitFxCapped}");
        }

        /// <summary>
        /// 战斗中开火：从**攻击者本帧的插值位置**向**目标本帧的插值位置**飞一条原版弹道
        /// （两个端点都由调用方按渲染时钟算好，见 <see cref="FireBattleShotNow"/>）。
        /// <para>
        /// <b>目标怎么选</b>：在**起手那一刻**由 <see cref="ScheduleBattleShot"/> 用 <see cref="NearestEnemy"/>
        /// 定下来（镜像服务端的索敌口径 —— <c>acquireTarget</c> 取**最近**的合法目标，
        /// `server/game/core/targeting.go:70-93`：先过 <c>canTarget</c>（不同队 / 存活 / 不在部署中 /
        /// 空中地面匹配），再取 hitbox 间隙最小、id 小者优先；客户端没有半径数据，用**中心距**近似取最近，
        /// 本项目口径，登记在报告里）。
        /// </para>
        /// <para>
        /// ⚠️ **契约缺口（登记见 `策划/对照表.md` §D.3）**：协议里没有"开火"事件 ⇒ 客户端拿不到
        /// "这一帧哪个单位开了火"、也拿不到**真实目标**，只能这样反推（`EvPlayCard` 的施法者 id 只覆盖
        /// "出牌"那一条路，不覆盖单次挥砍）。正解 = 服务端为每次开火发一条带 `caster_entity_id` +
        /// `target_entity_id` 的事件（⛔ 客户端不改服务端协议）。
        /// </para>
        /// </summary>
        /// <returns>真的播出去了才返回 true（用于记录该实体的下一次开火时刻）—— 远程卡 = 播出了弹道；近战恒为 false。</returns>
        private bool PlayBattleShot(EntitySnapshot attacker, EntitySnapshot target, Vector2 from, Vector2 to)
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
            // 近战卡（无弹道）：没有可播的弹道表现；溅射近战（瓦基丽武神等）仍有命中红圈。
            if (string.IsNullOrEmpty(card.projectile_key))
            {
                ScheduleAoeRing(to, card, attacker.id, 0f);
                return false;
            }
            if (card.proj_speed <= 0) return false;                     // 无速度 ⇒ 算不出飞行时长

            var dist = Vector2.Distance(from, to);
            if (dist <= 0f) return false;
            var seconds = dist * 60f / card.proj_speed; // 格 ÷ (格/分钟 ÷ 60)，与出牌弹道同一口径

            var fx = ResolveProjectileFx(card.projectile_key);

            var before = _effects.SpawnedTotal;
            // ⛔ 必须走**带 `ProjectileFx` 的重载**：那一个才读 `fx.Scale`（尺寸系数）与 `fx.Tint`（染色）；
            // 不带 fx 的重载 ⇒ 战斗中的弹体是「原版尺寸 + 无染色」，与出牌那一发不是同一颗球。
            _effects.PlayFlight(from, to, fx, seconds);
            if (_effects.SpawnedTotal <= before) return false;

            _battleShots++;
            // 弹体段的**尺寸系数**与**染色**（战斗路径与出牌路径同源 ⇒ 同一颗球、同一套变换）。
            var scaleTxt = fx.Scale > 0f ? $" 缩放=×{fx.Scale:F2}" : string.Empty;
            var tintTxt = fx.Tint
                ? $" 色调=mul({fx.MulR},{fx.MulG},{fx.MulB})+add({fx.AddR},{fx.AddG},{fx.AddB})"
                : string.Empty;
            Game.Logger?.Info(LogTag,
                $"战斗中开火弹道：ent={attacker.id}（card={attacker.card_id} key={card.projectile_key}）" +
                $"从攻击者位置=({from.x:F2},{from.y:F2}) 飞向起手锁定的目标 ent={target.id} =({to.x:F2},{to.y:F2}) " +
                $"距离={dist:F2}格 飞行={seconds:F3}s 图元={fx.Use} f{fx.First}..f{fx.First + fx.Count - 1}（{fx.Count} 帧）" +
                scaleTxt + tintTxt +
                // 弹道出现 / **到达**两个时钟都打出来：服务端在**到达那一 tick** 才落伤害
                // （`server/game/core/combat.go` 的 `advanceProjectile` + `battle.go` 的 `stepProjectiles`），
                // 所以复核"到达 vs 伤害落点"不用再手算 `渲染时钟 + 飞行`。
                $" 出现时钟={Interp.RenderClockMs:F0}ms 到达时钟={Interp.RenderClockMs + seconds * 1000f:F0}ms " +
                $"本局累计={_battleShots}");

            // 命中段：这条弹道**到达**落点那一刻，在落点播该投射物的原版命中段
            //（只有原版图集里有专属命中段的投射物才有，见 EffectsView.TryGetProjectileHitFx；
            //  没有专属段的（箭矢 / 长矛 / 飞斧 / 炸弹 …）保持既有 `Hit` 闪光表现不动）。
            ScheduleProjectileHit(to, card.projectile_key, attacker.id, seconds);
            // 溅射命中红圈：弹道到达落点那一刻画（半径 = 攻击者卡池 `aoe_radius_milli`）。
            ScheduleAoeRing(to, card, attacker.id, seconds);
            return true;
        }

        /// <summary>
        /// 排一条**延迟前摇**的战斗弹道（由 <see cref="TrackCombatAtRender"/> 在攻击档的到点帧调）：
        /// 一次挥砍的动画 = 前摇 + 出刀，而客户端的攻击档在上升沿**立即起播**（抬手）⇒ 弹道要推迟该目录的
        /// `load_time_ms` 再发，才落在抬手动作之后（`load_time_ms` 出处 = `server/game/table/tsv/unit.tsv`
        /// 的 `load_time_ms` 列，经 `UnitAnimTable.LoadTimeMs` 带进来）。
        /// <para>延迟复用 <see cref="_pendingImpacts"/> 队列（⛔ 不起第二套计时）；同一攻击者已有一条在等前摇时
        /// 不再排第二条（`load_time_ms &gt; hit_speed_ms` 的目录会连续到点）。</para>
        /// <para>排期即记时刻（`_lastShotMs`）：下一次挥砍按"决定 → 决定"的间隔算，与到点早晚无关。</para>
        /// </summary>
        private void ScheduleBattleShot(EntitySnapshot attacker, int loadMs, float decidedMs,
            Dictionary<int, EntitySnapshot> window)
        {
            for (var i = 0; i < _pendingImpacts.Count; i++)
            {
                var q = _pendingImpacts[i];
                if (q.Mode != ImpactModeFire || q.AttackerId != attacker.id) continue;
                _fireShotsDeduped++;
                return;
            }

            // 目标在**起手这一刻**就定下来（⛔ 不在到点那一帧重找）：服务端 lockTarget 也是在这一刻锁目标
            // （`server/game/core/targeting.go`），而"目标中途死了 ⇒ 这一刀不落伤害"要靠它比对
            // （见 FireBattleShotNow / CancelPendingFire）。
            var target = NearestEnemy(window.Values, attacker);
            // 落点：制导的投射物到点再瞄目标当前位置；**不制导**的（原版 `homing=false`，出处
            // `ProjectileHomingTable`）飞向**起手这一刻**锁定的那个点，⛔ 不逐帧重瞄。
            var pkey = ProjectileKeyOf(attacker.card_id);
            bool pkeyed;
            var homing = ProjectileHomingTable.IsHoming(pkey, out pkeyed);
            _pendingImpacts.Add(new PendingImpact
            {
                Mode = ImpactModeFire,
                AttackerId = attacker.id,
                TargetId = target == null ? 0 : target.id,
                Aim = target == null ? Vector2.zero : GameConst.MilliToWorld(target.x_milli, target.y_milli),
                Homing = homing,
                LeftMs = Mathf.Max(0f, loadMs),
                DecidedMs = decidedMs,
                Label = "shot:ent=" + attacker.id,
            });
            _fireShotsScheduled++;
            _lastShotMs[attacker.id] = decidedMs;
            var keyText = (string.IsNullOrEmpty(pkey) ? "-" : pkey)
                          + (pkeyed ? (homing ? "(制导)" : "(不制导⇒飞向起手锁定点)") : "(表外⇒按制导)");
            var targetText = target == null
                ? "无（起手时就没有合法敌方）"
                : "ent=" + target.id + " 起手落点=(" + (target.x_milli / 1000f).ToString("F2") + ","
                  + (target.y_milli / 1000f).ToString("F2") + ")";
            Game.Logger?.Info(LogTag,
                $"攻击排期：ent={attacker.id}（card={attacker.card_id} key={keyText}）前摇={loadMs}ms " +
                $"目标={targetText} ⇒ 到点发弹道 决定时钟={decidedMs:F0}ms（= 该刀窗口末帧）" +
                $"本局累计：排期={_fireShotsScheduled} 折掉={_fireShotsDeduped} 取消={_fireShotsCancelled}");
        }

        /// <summary>该卡的投射物 key（卡池还没到 / 这张卡不是远程 ⇒ 空串）。</summary>
        private string ProjectileKeyOf(int cardId)
        {
            CardInfo card;
            return _cards.TryGetValue(cardId, out card) && card != null ? card.projectile_key : string.Empty;
        }

        /// <summary>
        /// 取消某攻击者**还没到点**的开火排期并留痕。两条触发路径：
        /// ① 攻击档提前**释放**（<see cref="TrackCombatAtRender"/> 的 anim 释放分支）；
        /// ② 到点那一帧目标已死 / 已出图（<see cref="FireBattleShotNow"/>）。
        /// 服务端在前摇被中断时窗口到点自然释放但**不落伤害**（`server/game/core/combat.go` 的 `advanceAttack`）
        /// ⇒ 客户端也要把那一发撤掉，否则画面上一支箭飞出去却没有伤害。
        /// </summary>
        private void CancelPendingFire(int attackerId, string why)
        {
            for (var i = _pendingImpacts.Count - 1; i >= 0; i--)
            {
                var q = _pendingImpacts[i];
                if (q.Mode != ImpactModeFire || q.AttackerId != attackerId) continue;
                _pendingImpacts.RemoveAt(i);
                _fireShotsCancelled++;
                Game.Logger?.Info(LogTag,
                    $"开火排期取消：ent={attackerId}（{why}）⇒ 这一发不发 " +
                    $"本局累计：排期={_fireShotsScheduled} 未发={_fireShotsLost} 取消={_fireShotsCancelled}");
            }
        }

        /// <summary>
        /// 开火排期到点：发一条战斗弹道。**起点 = 攻击者本帧的插值坐标**、**落点 = 目标本帧的插值坐标**
        /// （都与精灵同帧同位，见 <see cref="RenderWorldOf"/>）。
        /// 起手后攻击者离场（死于前摇 / 出图）、或目标已死 / 已出图 ⇒ 这一发撤掉并留痕（见 <see cref="CancelPendingFire"/>）。
        /// </summary>
        private void FireBattleShotNow(PendingImpact p)
        {
            var cur = Interp.WindowEndPayload as Dictionary<int, EntitySnapshot>;
            EntitySnapshot attacker;
            if (cur == null || !cur.TryGetValue(p.AttackerId, out attacker) || attacker == null)
            {
                _fireShotsLost++;
                Game.Logger?.Info(LogTag,
                    $"开火排期到点但 ent={p.AttackerId} 已不在快照里（前摇期间死亡 / 出图）⇒ 这一发不发 " +
                    $"本局累计：排期={_fireShotsScheduled} 未发={_fireShotsLost} 取消={_fireShotsCancelled}");
                return;
            }

            // 这一条已经由 `ProcessPendingSpellImpacts` 从队列里取出 ⇒ ⛔ 不能再走 `CancelPendingFire`：
            // 它按 attacker 在队列里删条目，此刻删不到任何东西、取消计数也不会增（这一发就成了无记账的静默丢弃）。
            if (p.TargetId == 0)
            {
                _fireShotsCancelled++;
                Game.Logger?.Info(LogTag,
                    $"开火排期到点但没有合法敌方（起手时就没锁到目标）⇒ 这一发不发 " +
                    $"本局累计：排期={_fireShotsScheduled} 未发={_fireShotsLost} 取消={_fireShotsCancelled}");
                return;
            }
            EntitySnapshot target;
            if (!cur.TryGetValue(p.TargetId, out target) || target == null || target.hp <= 0)
            {
                _fireShotsCancelled++;
                Game.Logger?.Info(LogTag,
                    $"开火排期到点但目标已{(target == null ? "出图" : "死")}：ent={p.TargetId} ⇒ 这一发不发 " +
                    $"本局累计：排期={_fireShotsScheduled} 未发={_fireShotsLost} 取消={_fireShotsCancelled}");
                return;
            }
            PlayBattleShot(attacker, target, RenderWorldOf(attacker),
                p.Homing ? RenderWorldOf(target) : p.Aim);   // 不制导 ⇒ 飞向起手那一刻锁定的点（见 ScheduleBattleShot）
        }

        /// <summary>
        /// 国王塔**激活**（`EvTowerActivated` / `kind == 5`）的一次性表现：**亮出该塔的炮塔层**。
        ///
        /// <para>
        /// <b>为什么"亮炮塔"就是原版的"惊醒"</b>：原版国王塔未激活时炮塔整层透明
        /// （`.sc` 给 turret 子件的颜色变换 alpha = 0，出处见 <see cref="ArenaView.KingTurretAlphaZeroCt"/>），
        /// 被激怒后 3300 ms（`KingActivationMs`）才升起并开火 ⇒ 客户端的可见变化就是这一层由关到开。
        /// 音效（`king_activate_01`）由 <see cref="BattleAudioView"/> 独立播，两边不重复。
        /// </para>
        ///
        /// <para>
        /// <b>塔怎么认</b>：事件载荷带的就是该塔**自己的坐标**（服务端 `stepKingActivation` 填
        /// `k.xMilli / k.yMilli`）⇒ 交给 <see cref="ArenaView.TryActivateKingTower"/> 按"队伍 + 最近"对号。
        /// 塔视图还没建（进对局第一帧就激活）⇒ 留痕并计数，不静默。
        /// </para>
        /// </summary>
        private void PlayTowerActivated(BattleEvent e)
        {
            _towerActivations++;
            float tx, ty;
            if (_arena == null)
            {
                if (!_towerActivateMissingWarned)
                {
                    _towerActivateMissingWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"国王塔激活（kind={EventKindTowerActivated}）时塔视图还没建 ⇒ 炮塔层不会亮（只报一次）");
                }
                return;
            }
            var shown = _arena.TryActivateKingTower(e.x_milli, e.y_milli, e.team, (int)Interp.WindowEndMs, out tx, out ty);
            if (!shown)
            {
                // 幂等分支：同一座塔的重复激活事件（服务端 `arm` 只认第一次，但事件可能重发）
                // 或本地找不到该队伍的国王塔。只在"找不到"时留痕。
                if (!_towerActivateUnmatchedWarned)
                {
                    _towerActivateUnmatchedWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"国王塔激活事件 team={e.team} 坐标=({e.x_milli / 1000f:F2},{e.y_milli / 1000f:F2}) " +
                        "在塔视图里找不到未激活的国王塔 ⇒ 本次不改动（只报一次）");
                }
                return;
            }
            _towerActivatedShown++;
            Game.Logger?.Info(LogTag,
                $"国王塔激活表现：team={e.team} 塔位=({tx:F1},{ty:F1}) 炮塔层已亮 " +
                $"本局累计：激活事件={_towerActivations} 已亮塔数={_towerActivatedShown}");
        }

        /// <summary>
        /// 塔开火（`EvTowerShoot` / `kind == 6`）的一次性表现：**有速度时从炮口飞一条弹道**。
        ///
        /// <para>
        /// <b>与 <see cref="PlayBattleShot"/> 的分工</b>：那条是"从快照反推单位开火"（协议没有单位开火事件）；
        /// 这条是**服务端明确发来的**塔开火事件，位置与速度都是真值 —— 塔的射击链路
        /// （国王塔 + 公主塔）的攻击特效就是靠它播出来的。
        /// </para>
        ///
        /// <para>
        /// <b>炮口怎么取</b>：优先问 <see cref="ArenaView.TryTowerMuzzle"/> —— 塔贴图里"炮塔"是**独立一层**
        /// （`Princess` / `Turret`），用塔根坐标会把弹道起点画在塔底座。取不到才退回事件自带的
        /// 塔根坐标（`x_milli/y_milli`，服务端 `emitTowerShoot` 填的就是塔根）并**留痕一次**。
        /// </para>
        ///
        /// <para>
        /// <b>目标怎么来</b>：事件载荷**只有塔自己**、没有目标（`Event.ProjSpeed` 的注释里写明）。
        /// 故用**最近一帧快照**近似取"该塔阵营的最近合法敌方"，口径与 <see cref="NearestEnemy"/> 完全一致。
        /// 快照还没到（进对局第一帧就开火）⇒ 取不到目标 ⇒ 不编造飞行方向。
        /// </para>
        ///
        /// <para>
        /// <b>速度口径</b>：`proj_speed` 单位 = 格/分钟，同官方 `cards_stats_projectile.json` 的 `speed`
        /// ⇒ <c>飞行秒数 = 距离(格) × 60 / proj_speed</c>（与出牌弹道 / 单位开火同一口径）。
        /// `proj_speed &lt;= 0`（该塔没有投射物或投射物表缺速度）⇒ 不播弹道。
        /// </para>
        ///
        /// <para>
        /// <b>计数自检</b>：<c>TowerShots == TowerShotFlights + TowerShotSkipped</c>
        /// —— 不成立 = 有开火事件没被任何一条路径处理（判红）。
        /// </para>
        ///
        /// <para>
        /// <b>国王塔开火表现</b>：炮塔层后坐（2.5 px / 60 ms，见 `ArenaView.TowerView.PlayKingRecoil`）
        /// + 炮口闪一帧原版 <c>effect_Hit1</c>；公主塔走乘员攻击档（<see cref="ArenaView.TryPlayTowerAttack"/>），
        /// 两者互不替代。计数自检：<c>KingTowerShots == KingTowerRecoils == KingTowerMuzzleFlashes</c>。
        /// </para>
        /// </summary>
        private void PlayTowerShot(BattleEvent e)
        {
            _towerShots++;

            // ① 炮口位置：塔视图的炮口层 → 事件自带的塔根坐标（兜底 + 留痕）。
            //    ⛔ 兜底值必须先单独存一份：`TryTowerMuzzle` 在"没找到"时会把 `out` 形参写成
            //    `default(Vector2)`（见该方法的实现）⇒ 把塔根坐标直接当 in/out 传进去，
            //    两次失败查找之后兜底值就成了世界原点，弹道 / 枪口闪光会画到场地正中。
            var towerRoot = GameConst.MilliToWorld(e.x_milli, e.y_milli);
            var muzzle = towerRoot;
            // 先按**队伍 + 事件坐标**对号（塔位是固定几何 ⇒ 同一队伍里离事件坐标最近的那座塔唯一），
            // 再退回按塔实体 id 对号。
            var fromMuzzleLayer = false;
            if (_arena != null)
            {
                Vector2 byPos;
                Vector2 byId;
                if (_arena.TryTowerMuzzle(e.x_milli, e.y_milli, e.team, out byPos))
                {
                    muzzle = byPos;
                    fromMuzzleLayer = true;
                }
                else if (_arena.TryTowerMuzzle(e.entity_id, out byId))
                {
                    muzzle = byId;
                    fromMuzzleLayer = true;
                }
            }
            if (!fromMuzzleLayer && !_towerMuzzleMissingWarned)
            {
                _towerMuzzleMissingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"塔开火：塔 id={e.entity_id} team={e.team} 在塔视图里找不到炮口层（塔视图未建 / 坐标对不上）⇒ " +
                    $"用事件自带的塔根坐标 ({muzzle.x:F2},{muzzle.y:F2}) 兜底（弹道起点会偏到塔底）（只报一次）");
            }

            // ★ 兜底：这座国王塔**正在开火**却还是"未激活"（激活事件早于塔视图建好 / 本局中途加入）
            //   ⇒ 补亮炮塔层。判据是"它开火了"：服务端只让已激活的国王塔参战
            //   （`server/game/core/tower.go` 的 `canFight` / `battle.go` 的 `stepAttacks`）⇒ 这个推断必然成立。
            float atx, aty;
            if (_arena != null && _arena.TryActivateKingTower(e.x_milli, e.y_milli, e.team, (int)Interp.WindowEndMs, out atx, out aty))
            {
                _towerActivatedShown++;
                Game.Logger?.Info(LogTag,
                    $"塔开火兜底：team={e.team} 塔位=({atx:F1},{aty:F1}) 此前未收到激活事件 ⇒ 补亮炮塔层 " +
                    $"本局累计：激活事件={_towerActivations} 已亮塔数={_towerActivatedShown}");
            }

            // ★ 乘员攻击动画：开火事件一到就让该塔的乘员播一遍攻击档（播完自动回 idle）。
            //   与弹道 / 枪口闪光**互不依赖** —— 特效层缺失时动画照样播（反之亦然）。
            if (_arena != null && _arena.TryPlayTowerAttack(e.x_milli, e.y_milli, e.team))
                _towerAttackAnims++;

            // ② 目标：事件不带 ⇒ 用最近一帧快照近似（同 `NearestEnemy` 口径）。取不到就不编方向。
            var target = _lastEntities == null
                ? null
                : NearestEnemy(_lastEntities, e.team, e.entity_id, e.x_milli, e.y_milli);

            // ③ 炮塔朝向：**与下面那条弹道同一个目标**（同一次 `NearestEnemy` 结果，⛔ 不另起一套索敌）。
            //    取不到目标 ⇒ 保持当前方向帧不动（⛔ 不猜方向、不许空转一圈）；公主塔没有炮塔层 ⇒ 同样保持不动。
            //    （逐次一行日志由 `ArenaView.TryAimTowerTurret` 打 —— 它才是拿得到 roster / 角度 / 帧号 / flipX
            //      的那一层，见那里的 "塔瞄准：" 行；这里只留计数。）
            _towerAimTotal++;
            Vector2 aimTo = muzzle;
            if (target != null && _arena != null)
            {
                aimTo = GameConst.MilliToWorld(target.x_milli, target.y_milli);
                int aimFrame;
                float aimAngle;
                bool aimFlip;
                if (_arena.TryAimTowerTurret(e.x_milli, e.y_milli, e.team, aimTo,
                        out aimFrame, out aimAngle, out aimFlip)) _towerAimed++;
                else _towerAimKept++;
            }
            else
            {
                _towerAimKept++;
            }

            // ★ 国王塔开火表现：**炮塔后坐 + 炮口闪一帧原版火光**。
            //   国王塔在原版 `building_tower_v215.sc` 里只有 idle、没有攻击帧 ⇒ 开火的"动"由炮塔层后坐表达；
            //   公主塔已有乘员攻击档（上一段），⛔ 不叠第二套动作。
            //   ⚠️ 与弹道 / 特效层**互不依赖**：特效层缺失时后坐照样播（下面才做 `_effects` 的空检查）。
            var isKingShot = false;
            if (_arena != null)
            {
                int recoilRoster;
                bool recoilStarted;
                if (_arena.TryPlayKingRecoil(e.x_milli, e.y_milli, e.team, aimTo, out recoilRoster, out recoilStarted))
                {
                    isKingShot = true;
                    _kingTowerShots++;
                    if (recoilStarted) _kingTowerRecoils++;
                    var flashPlayed = false;
                    if (_effects != null)
                    {
                        var beforeFlash = _effects.SpawnedTotal;
                        // 炮口火光 = 原版 `effect_Hit1` 的暖色星芒（f295-298），按它自己的时间轴 5/60 s 播；
                        // 与降级分支的枪口闪光同一段素材、同一时长口径。
                        _effects.PlayTimed(muzzle, ResPaths.EffectHit, ResPaths.EffectHitFirst,
                            ResPaths.EffectHitCount, EffectsView.WorldSize, ResPaths.EffectHitSeconds);
                        flashPlayed = _effects.SpawnedTotal > beforeFlash;
                        if (flashPlayed) _kingMuzzleFlashes++;
                    }
                    Game.Logger?.Info(LogTag,
                        $"王塔开火：塔 id={e.entity_id} team={e.team} roster={recoilRoster} " +
                        $"炮口=({muzzle.x:F2},{muzzle.y:F2}) 后坐={(recoilStarted ? "已触发" : "未触发（无目标方向 / 炮塔层缺失）")} " +
                        $"闪光={( _effects == null ? "特效层缺失" : (flashPlayed ? "EffectHit f" + ResPaths.EffectHitFirst + "..f" + (ResPaths.EffectHitFirst + ResPaths.EffectHitCount - 1) : "未播（素材空）"))} " +
                        $"本局累计：王塔开火={_kingTowerShots} 后坐={_kingTowerRecoils} 闪光={_kingMuzzleFlashes}（总开火={_towerShots}）");
                }
            }

            if (_effects == null) { _towerShotSkipped++; return; }

            // ③ 飞行段：只有"有速度 且 有目标 且 距离>0"三个条件齐了才飞。
            if (e.proj_speed > 0 && target != null)
            {
                var to = GameConst.MilliToWorld(target.x_milli, target.y_milli);
                var dist = Vector2.Distance(muzzle, to);
                if (dist > 0f)
                {
                    var seconds = dist * 60f / e.proj_speed;
                    var beforeFlight = _effects.SpawnedTotal;
                    // 国王塔打的是**大炮弹**（`projectile_cannonball_large` f481）；公主塔仍是箭。
                    var fx = isKingShot
                        ? EffectsView.KingCannonballFx
                        : new EffectsView.ProjectileFx
                        {
                            Use = ResPaths.EffectArrow,
                            First = ResPaths.EffectArrowFirst,
                            Count = ResPaths.EffectArrowCount,
                        };
                    _effects.PlayFlight(muzzle, to, fx, seconds);
                    if (_effects.SpawnedTotal > beforeFlight)
                    {
                        _towerShotFlights++;
                        Game.Logger?.Info(LogTag,
                            $"塔开火弹道：塔 id={e.entity_id} team={e.team} 炮口=({muzzle.x:F2},{muzzle.y:F2})" +
                            $"{(fromMuzzleLayer ? "" : "（兜底：塔根）")} 飞向最近敌方 ent={target.id} " +
                            $"=({to.x:F2},{to.y:F2}) 距离={dist:F2}格 速度={e.proj_speed}格/分 飞行={seconds:F3}s " +
                            $"本局累计：开火={_towerShots} 飞行={_towerShotFlights} 跳过={_towerShotSkipped}");
                        return;
                    }
                }
            }

            // ④ 降级：没播出弹道（无速度 / 无目标 / 飞行素材为空 / 特效层同屏上限）⇒ 在炮口**定格播一帧
            //    该塔的弹体**当枪口闪光（原版塔开火必然有出膛那一帧；段 = `EffectArrow` 首帧 = 塔的弹体首帧，
            //    定格时长取 `EffectHitSeconds`：原版 `effect_Hit1` 自己的时间轴 = 5 帧 ÷ 60 fps，同属瞬时一帧档）。
            //    仍计入跳过（跳过的语义 = "这一发没有飞出去的弹道"，计数自检不变）。
            var beforeMuzzle = _effects.SpawnedTotal;
            var fallbackFx = isKingShot ? EffectsView.KingCannonballFx : EffectsView.ArrowFx;
            _effects.PlayHold(muzzle, fallbackFx.Use, fallbackFx.First,
                EffectsView.WorldSize, ResPaths.EffectHitSeconds);
            if (_effects.SpawnedTotal > beforeMuzzle) _towerShotMuzzles++;

            _towerShotSkipped++;
            Game.Logger?.Info(LogTag,
                $"塔开火降级：塔 id={e.entity_id} team={e.team} 炮口=({muzzle.x:F2},{muzzle.y:F2}) " +
                $"速度={e.proj_speed}格/分 目标={(target == null ? "无" : target.id.ToString())} ⇒ " +
                $"{( _effects.SpawnedTotal > beforeMuzzle ? "播枪口闪光" : "枪口闪光也没播（素材空）")} " +
                $"本局累计：开火={_towerShots} 飞行={_towerShotFlights} 跳过={_towerShotSkipped} 枪口闪光={_towerShotMuzzles}");
        }

        /// <summary>
        /// 本帧快照里离 <paramref name="self"/> 最近的**合法**敌方实体（镜像服务端 `canTarget` 的硬条件：
        /// 不同队 / 存活（hp&gt;0）/ 不在部署中；`server/game/core/targeting.go:16-52, 76-93`）。
        /// ⛔ 客户端没有半径 / 空中地面 / only_* 这些字段，故只做这几条可得条件（登记为近似口径）。
        /// </summary>
        private static EntitySnapshot NearestEnemy(IEnumerable<EntitySnapshot> all, EntitySnapshot self)
        {
            return NearestEnemy(all, self.team, self.id, self.x_milli, self.y_milli);
        }

        /// <summary>
        /// 同上，但**不需要一个快照实体当"自己"** —— 供塔开火用（`EvTowerShoot` 的位置来自事件载荷，
        /// 塔在快照里也可能已被查表删掉）。口径与快照重载逐条相同。
        /// </summary>
        private static EntitySnapshot NearestEnemy(IEnumerable<EntitySnapshot> all, int team, int selfId, int xMilli, int yMilli)
        {
            if (all == null) return null;
            EntitySnapshot best = null;
            float bestSq = 0f;
            foreach (var c in all)
            {
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
        /// <b>为什么必须分流</b>（差异登记见 `策划/对照表.md` §D.3）：这两条路的判据是**反的** ——
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
        /// <b>两段式</b>（帧位与时长见 <see cref="SpellFxTable.Entry"/>）：表里给了
        /// <see cref="SpellFxTable.Entry.FlightUse"/> 的法术（火球）先播**飞行段** —— 从
        /// <see cref="SpellOrigin"/>（本方国王塔中心）飞到落点；落地段挂进待落点队列，
        /// 到点（= 飞行段时间）才在落点播。没有飞行段的法术保持"在落点直接播"。
        /// </para>
        ///
        /// <para>
        /// <b>落地段时长取原版时间轴</b>：<see cref="EffectsView.PlayTimed"/>(`fx.Seconds` = 帧数 ÷
        /// 该 export 的 fps，出处 `策划/单位动画分组表.md` 的 fps 列)，⛔ 不吃
        /// <see cref="EffectsView.DefaultFps"/>(14 fps) —— 那会把 25 帧的落地爆炸拉成 1.8 s。
        /// </para>
        ///
        /// <para>
        /// <b>落点取真值</b>：`EvPlayCard` 的 `x_milli/y_milli` 就是玩家点的那一点
        ///（服务端 `core/battle.go:219-221` 原样填），⛔ 不做任何"往塔中心靠"之类的降级。
        /// </para>
        ///
        /// <para>
        /// <b>尺寸</b>：落地段传 <see cref="EffectsView.WorldSize"/> ⇒ `EffectsView` 里
        /// `scale = size / WorldSize` × 该段的 `.sc` 换算系数（见 `EffectsView.SegmentScale`），
        /// 与既有 `Hit` / `Blast` / `Arrow` 三个用途目录同一口径，⛔ 不按"法术半径"另算一个缩放
        /// （客户端没有半径数据；要按半径缩放得先让服务端随卡池下发，那是另一片）。
        /// 飞行段再乘 <see cref="SpellFxTable.Entry.FlightScale"/>（弹体只占整帧画布的一小块）。
        /// </para>
        ///
        /// <para>
        /// <b>计数自检</b>（三条都要成立，不成立即有法术出牌没被任何一条路径处理）：
        /// <c>SpellCasts == SpellFxPlayed + SpellFxSkipped</c>；
        /// <c>SpellFxPlayed == SpellFlights + SpellDirectImpacts</c>；
        /// <c>SpellImpactPlayed + SpellImpactSkipped == SpellFlights + SpellDirectImpacts - SpellImpactsPending</c>。
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

            // 两段式：有飞行段的法术（<see cref="SpellFxTable.Entry.FlightUse"/>）先从**本方国王塔中心**
            // 飞到落点，飞到了再在落点播落地段 —— 落地段挂进待落点队列，与弹道到达共用一套时钟
            //（见 ProcessPendingSpellImpacts），⛔ 不在这里自己计时。
            if (!string.IsNullOrEmpty(fx.FlightUse))
            {
                var origin = SpellOrigin(e.team, world);
                var beforeFlight = _effects.SpawnedTotal;
                // 弹体段是整帧画布里的一小块 ⇒ 尺寸系数取 `FlightScale`（见
                // SpellFxTable.Entry.FlightScale）；`ProjectileFx.Scale <= 0` 按 1 处理。
                var flightFx = new EffectsView.ProjectileFx
                {
                    Use = fx.FlightUse,
                    First = fx.FlightFirst,
                    Count = fx.FlightCount,
                    Scale = fx.FlightScale,
                    ArcHeight = fx.FlightArcHeight,
                };
                _effects.PlayFlight(origin, world, flightFx, fx.FlightSeconds);
                if (_effects.SpawnedTotal > beforeFlight)
                {
                    _spellFxPlayed++;
                    _spellFlights++;
                    ScheduleSpellImpact(world, fx, fx.FlightSeconds);
                    Game.Logger?.Info(LogTag,
                        $"法术飞行段：card={e.card_id} key={card.key} team={e.team} " +
                        $"起点（本方国王塔）=({origin.x:F2},{origin.y:F2}) 落点=({world.x:F2},{world.y:F2}) " +
                        $"距离={Vector2.Distance(origin, world):F2}格 飞行={fx.FlightSeconds:F3}s 弧高={flightFx.ArcHeight:F2}格 " +
                        $"帧=f{fx.FlightFirst}..f{fx.FlightFirst + fx.FlightCount - 1}（{fx.FlightUse}，{fx.FlightCount} 帧）" +
                        $" 尺寸={fx.FlightScale:F2}x " +
                        $" 落地段已挂起：{fx.Use} f{fx.First}..f{fx.First + fx.Count - 1}（{fx.Seconds:F3}s）" +
                        $" 本局累计：出牌={_spellCasts} 飞行={_spellFlights} 待落地={_pendingImpacts.Count}");
                    return;
                }
                // 非预期分支：飞行段素材取不到（目录没落地 / 没被导入）⇒ 退回落点直接播落地段，⛔ 不静默丢。
                if (!_impactMissingWarned)
                {
                    _impactMissingWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"法术飞行段播不出去：用途目录 \"{fx.FlightUse}\"（首帧 f{fx.FlightFirst}）取不到 ⇒ " +
                        "本次退回在落点直接播落地段（只报一次）");
                }
            }

            // 无飞行段的法术 / 飞行段播不出：直接在落点播落地段（到点即播，延迟 0）。
            _spellFxPlayed++;
            _spellDirectImpacts++;
            PlayImpact(new PendingImpact
            {
                World = world,
                Use = fx.Use,
                First = fx.First,
                Count = fx.Count,
                Seconds = fx.Seconds,
                Label = "spell:" + card.key,
                Spell = true,
            });
        }

        /// <summary>
        /// 法术的**施法起点**（世界坐标）= **本方国王塔中心**。
        /// <para>
        /// 为什么是国王塔：法术卡的 `EvPlayCard` 事件里 `entity_id` 恒为 0 —— 服务端
        /// `server/game/core/snapshot.go` 的 `Event` 只有卡 id / 队伍 / 落点，法术不生成施法者实体
        ///（`server/game/core/battle.go:220-226`）⇒ 事件里**拿不到起点**。我方施法者只可能在自家半场，
        /// 取本方国王塔为起点是**本项目自定**的口径（差异登记见 `策划/对照表.md` §D.3）。
        /// </para>
        /// <para>坐标一律走 <see cref="GameConst"/> 的场地常量 + <see cref="GameConst.MirrorTileYForTeam"/>，
        /// ⛔ 不硬编码坐标数字。</para>
        /// </summary>
        /// <param name="team">施法方队伍（0=BLUE 1=RED）。</param>
        /// <param name="landing">落点（世界坐标）。本口径不用它，签名保留给调用方对齐"起点→落点"这一对参数。</param>
        private static Vector2 SpellOrigin(int team, Vector2 landing)
        {
            return GameConst.TileToWorld(GameConst.KingTowerTileX,
                GameConst.MirrorTileYForTeam(GameConst.KingTowerTileY, team));
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
        /// <b>「施法者位置」怎么取</b>：`EvPlayCard` 的 `entity_id` = **施法者实体**
        /// （服务端 `Battle.PlayCard` 在生成单位后回填，见 `server/game/core/battle.go`），
        /// 坐标按该 id 在快照里取（<see cref="TryCasterWorld"/>）。该实体是**出牌之后**才生成的、
        /// 而事件又推在对应快照**之前** ⇒ 事件到达那一刻取不到坐标，先挂起
        /// （<see cref="_pendingCasterShots"/>），等它进了快照再由 <see cref="ResolvePendingCasterShots"/> 补播；
        /// 挂起超时（或载荷里没有施法者 id）才退回旧口径（<see cref="CasterWorld"/>：本方离落点最近单位 →
        /// 国王塔中心 + 留痕）。**落点 = 事件自带的 `x_milli/y_milli`（真值，未降级）。**
        /// </para>
        ///
        /// <para>
        /// 出牌单位通常**就落在落点上**（单单位卡的 `SummonLayout` 偏移 = 0，见 `server/game/core/units.go`）
        /// ⇒ 起点与落点重合、距离 0、没有飞行段可播（<see cref="PlayCasterFlight"/> 会记一行读数）；
        /// 群体卡的第一支单位带环/打包偏移 ⇒ 才有短飞行段。
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

            // 远程但服务端没给出速度（`core.ProjectileOf` 在投射物行缺失时返回 0）⇒ 算不出飞行时长，不播。
            if (card.proj_speed <= 0)
            {
                if (!_projSpeedMissingWarned)
                {
                    _projSpeedMissingWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"远程卡 card={e.card_id}（projectile_key={card.projectile_key}）的 proj_speed={card.proj_speed}（<=0）" +
                        "⇒ 算不出飞行时长，跳过弹道（只报一次）");
                }
                return;
            }

            if (e.entity_id > 0)
            {
                Vector2 caster;
                if (TryCasterWorld(e.entity_id, out caster))
                {
                    PlayCasterFlight(e.card_id, card.projectile_key, card.proj_speed, caster, landing, true, e.entity_id);
                    return;
                }

                // 施法者还没进快照（事件先于对应快照到达）⇒ 挂起，等它出现再播。
                _pendingCasterShots.Add(new PendingCasterShot
                {
                    EntityId = e.entity_id,
                    CardId = e.card_id,
                    Team = e.team,
                    ProjectileKey = card.projectile_key,
                    ProjSpeed = card.proj_speed,
                    Landing = landing,
                    GiveUpMs = Interp.NewestSnapshotMs + CasterWaitMs,
                });
                Game.Logger?.Info(LogTag,
                    $"出牌弹道：card={e.card_id} key={card.projectile_key} 施法者 ent={e.entity_id} 还没进快照 ⇒ " +
                    $"挂起等它出现再播（当前挂起 {_pendingCasterShots.Count} 条）");
                return;
            }

            // 载荷里没有施法者 id（法术卡 / 不带该字段的服务端）：旧口径。
            PlayCasterFlight(e.card_id, card.projectile_key, card.proj_speed,
                CasterWorld(e.team, landing), landing, false, 0);
        }

        /// <summary>
        /// 用给定起点播一条出牌弹道。<paramref name="fromCasterEntity"/> = 起点是不是**施法者实体**的坐标
        /// （false = 退回旧口径取的本方最近单位 / 国王塔中心）。
        /// <para>起点与落点重合（距离 0）时没有"飞行"可言 ⇒ 不播，只记一行读数。</para>
        /// </summary>
        private void PlayCasterFlight(int cardId, string projectileKey, int projSpeed,
            Vector2 from, Vector2 landing, bool fromCasterEntity, int casterEntityId)
        {
            if (_effects == null) return;

            if (fromCasterEntity) _casterOrigins++;
            var origin = fromCasterEntity
                ? $"弹道起点 = 出牌单位 ent={casterEntityId}=({from.x:F2},{from.y:F2})"
                : $"弹道起点 = 旧口径（本方最近单位/国王塔中心）=({from.x:F2},{from.y:F2})";

            var dist = Vector2.Distance(from, landing);
            var seconds = dist * 60f / projSpeed; // 格 ÷ (格/分钟 ÷ 60)
            if (seconds <= 0f)
            {
                _casterZeroDist++;
                Game.Logger?.Info(LogTag,
                    $"出牌弹道：card={cardId} key={projectileKey} {origin} 落点=({landing.x:F2},{landing.y:F2}) " +
                    $"距离=0.00格 ⇒ 无飞行段可播（出牌即落地）" +
                    $" 本局累计：起点=出牌单位 {_casterOrigins} 零距 {_casterZeroDist} 超时 {_casterTimedOut} 播出 {_projectileShots}");
                return;
            }

            var fx = ResolveProjectileFx(projectileKey);

            _effects.PlayFlight(from, landing, fx, seconds);
            _projectileShots++;
            Game.Logger?.Info(LogTag,
                $"弹道 card={cardId} key={projectileKey} proj_speed={projSpeed}格/分钟 {origin} " +
                $"落点=({landing.x:F2},{landing.y:F2}) " +
                $"距离={dist:F2}格 飞行={seconds:F3}s（={dist:F2}×60/{projSpeed}） " +
                $"图元={fx.Use} f{fx.First}..f{fx.First + fx.Count - 1}（{fx.Count} 帧）" +
                $"{(fx.Scale > 0f ? " 缩放=×" + fx.Scale.ToString("F2") : "")}" +
                $"{(fx.Tint ? " 色调=mul(" + fx.MulR + "," + fx.MulG + "," + fx.MulB + ")+add(" + fx.AddR + "," + fx.AddG + "," + fx.AddB + ")" : "")}" +
                $" 本局累计={_projectileShots}");
        }

        /// <summary>按实体 id 在**最近一帧快照**里扫出该实体的世界坐标；它还没进快照 ⇒ false。</summary>
        private bool TryCasterWorld(int entityId, out Vector2 world)
        {
            world = default(Vector2);
            var list = _lastEntities;
            if (list == null) return false;
            for (var i = 0; i < list.Length; i++)
            {
                var c = list[i];
                if (c == null || c.id != entityId) continue;
                world = GameConst.MilliToWorld(c.x_milli, c.y_milli);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 补播挂起中的出牌弹道（每帧快照到达时调）：施法者一进快照就取它的坐标当起点
        /// （见 <see cref="_pendingCasterShots"/>）；超过 <see cref="CasterWaitMs"/> 还没出现 ⇒
        /// 退回旧口径（<see cref="CasterWorld"/>）并留痕。
        /// </summary>
        private void ResolvePendingCasterShots()
        {
            if (_pendingCasterShots.Count == 0) return;
            for (var i = _pendingCasterShots.Count - 1; i >= 0; i--)
            {
                var p = _pendingCasterShots[i];
                Vector2 caster;
                if (TryCasterWorld(p.EntityId, out caster))
                {
                    _pendingCasterShots.RemoveAt(i);
                    PlayCasterFlight(p.CardId, p.ProjectileKey, p.ProjSpeed, caster, p.Landing, true, p.EntityId);
                    continue;
                }
                if (Interp.NewestSnapshotMs < p.GiveUpMs) continue;

                _pendingCasterShots.RemoveAt(i);
                _casterTimedOut++;
                Game.Logger?.Warn(LogTag,
                    $"出牌弹道：施法者 ent={p.EntityId}（card={p.CardId}）在 {CasterWaitMs:F0}ms 内没进快照 ⇒ " +
                    $"退回旧口径（本方离落点最近单位 / 国王塔中心） 本局累计超时 {_casterTimedOut} 次");
                PlayCasterFlight(p.CardId, p.ProjectileKey, p.ProjSpeed,
                    CasterWorld(p.Team, p.Landing), p.Landing, false, p.EntityId);
            }
        }

        /// <summary>
        /// 按官方投射物名取该投射物的原版图元段（表见 <see cref="EffectsView.TryGetProjectileFx"/>）。
        /// 表里没有该投射物（原版图集里没有它的段落）⇒ **留痕一次**并退回箭矢段。
        /// </summary>
        private static EffectsView.ProjectileFx ResolveProjectileFx(string projectileKey)
        {
            EffectsView.ProjectileFx fx;
            if (EffectsView.TryGetProjectileFx(projectileKey, out fx)) return fx;

            // 「只报一次」走引擎的进程级去重（`Runtime/Core/LogThrottle.cs`），键里带投射物名
            // ⇒ 每个"原版无对应图元"的投射物各报一次。
            LogThrottle.WarnOnce(LogTag, "fx.proj.unknown:" + projectileKey,
                $"投射物 \"{projectileKey}\" 在原版 effects 图集里没有对应段落 ⇒ 退回箭矢图元 " +
                $"{EffectsView.ArrowFx.Use} f{EffectsView.ArrowFx.First}..f{EffectsView.ArrowFx.First + EffectsView.ArrowFx.Count - 1}" +
                "（补法：在 策划/单位动画分组表.md 的 effects 小节查到该投射物的段，加进 EffectsView.TryGetProjectileFx + copy-fx-assets.py）");
            return EffectsView.ArrowFx;
        }

        /// <summary>
        /// 出牌弹道的**退回口径**起点（只在施法者实体取不到时才走这里）：
        /// **优先 = 本方在 <paramref name="landing"/> 附近最近的一支单位**
        /// （用当前插值窗口末尾那一帧快照的实体坐标）；本方在该帧**一个单位都没有**时，
        /// 才回退到**出牌方国王塔中心**（固定几何 `GameConst.KingTowerTileX/Y`，出处 `anchors.json`）并留痕一次。
        ///
        /// <para>
        /// <b>为什么不直接用它当默认值</b>：它是**近似**（离落点最近的单位不一定是施法者），
        /// 且全场本方无单位时会退成国王塔中心 —— 画面上就是"箭从本方塔里射出来"。
        /// 正解是施法者实体（`EvPlayCard` 的 `entity_id` + 快照坐标，见 <see cref="PlayProjectileFlight"/>），
        /// 本方法只在那个实体取不到（超时 / 载荷没带）时兜底。
        /// </para>
        /// </summary>
        private Vector2 CasterWorld(int team, Vector2 landing)
        {
            EntitySnapshot best = null;
            var bestSq = 0f;
            var cur = Interp.WindowEndPayload as Dictionary<int, EntitySnapshot> ?? EmptySnapshotIndex;
            foreach (var kv in cur)
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
                    "施法者退化为本方国王塔中心；出现即说明施法者实体也没能按 id 取到（只报一次）");
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

        // 渲染时钟推进 / 快照历史 / 插值窗口选择 / 有界比例速率修正 / 时钟超前上限，全在引擎件
        // 插值器 `Interp`（`SnapshotInterpolator`）内部，并各自带「只报一次」的留痕（见类注释三）。
        // 本类只保留一处接缝：每帧 Tick 一次拿 `t`，再用它摆单位（见 TickRender / ApplyEntities）。

        /// <summary>
        /// 每帧渲染：<c>Interp.Tick()</c> 推进渲染时钟（只按真实时间走，⛔ 不因快照到达而重置）、
        /// 做有界比例速率修正、按渲染时钟选出"夹住时钟的那一对快照"，返回插值比例 <c>t</c>；
        /// 再用 <c>t</c> 把本帧实体摆到窗口起始 / 窗口结束之间（见 <see cref="ApplyEntities"/>）。
        /// 引擎件在非预期分支（乱序丢弃 / 掉窗 / 时钟超前上限 / 灾难级追帧）各自留一次痕。
        /// <para>摆完**紧接着**做战斗表现的边沿判定（<see cref="TrackCombatAtRender"/>）——
        /// 它用的就是这一步刚算出的插值坐标（<see cref="_renderWorld"/>），
        /// 故特效与本帧精灵同帧同位。</para>
        /// </summary>
        private void TickRender()
        {
            ApplyEntities(Interp.Tick());
            TrackCombatAtRender();
        }

        private void ApplyEntities(float t)
        {
            // 引擎件按渲染时钟选出的"被渲染的那一对"快照；未就绪（首帧快照前）时为 null。
            var cur = Interp.WindowEndPayload as Dictionary<int, EntitySnapshot>;
            if (cur == null) return;
            var prev = Interp.HasWindow ? Interp.WindowStartPayload as Dictionary<int, EntitySnapshot> : null;

            // 本帧每个实体的插值坐标（见 `_renderWorld`）：每帧重算，只留本帧在场的那些 id。
            _renderWorld.Clear();

            foreach (var kv in cur)
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
                // 朝向用的**基线位移**：从"历史里最旧的那个含该 id 的快照"量到位移（历史 6 格 ⇒ 默认 5 个间隔
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
                if (prev != null)
                {
                    EntitySnapshot p;
                    if (prev.TryGetValue(e.id, out p) && p != null)
                    {
                        var prevWorld = GameConst.MilliToWorld(p.x_milli, p.y_milli);
                        var baselineWorld = prevWorld;
                        // 更早的快照按槽序读（0 = 最旧，HistoryLength-1 = 最新；最新那一格本帧已单独取过）。
                        for (var k = 0; k < Interp.HistoryLength - 1; k++)
                        {
                            var d = Interp.PayloadAt(k) as Dictionary<int, EntitySnapshot>;
                            EntitySnapshot q;
                            if (d != null && d.TryGetValue(e.id, out q) && q != null)
                            {
                                baselineWorld = GameConst.MilliToWorld(q.x_milli, q.y_milli);
                                break;
                            }
                        }
                        moveDir = world - baselineWorld;
                        world = Vector2.Lerp(prevWorld, world, t);
                    }
                }

                // 攻击档朝向**目标**（原版"打谁朝谁"）：服务端随快照下发 `target_id`
                //（`def.EntitySnapshot.TargetID`），目标同帧在场 ⇒ 用「目标 − 自身」当朝向向量，
                // 覆盖下面按位移选出来的方向。
                // ⛔ 为什么必须覆盖：位移方向只在**行进**时有意义 —— 一个"往北走 → 停下开打"的单位，
                // 位移方向仍是北 ⇒ 视角永远是背身档 `_1`，而原版是转向目标。典型表现：
                // `chr_wizard_out` 整段攻击 100 帧恒为背身、其最近敌 30 帧全在身后 1.6~3.0 格。
                // 目标不在 `Entities` 里（打塔）或未下发（`target_id == 0`）⇒ 保持原位移口径。
                if (e.anim == UnitAnimTable.Attack && e.target_id != 0 && e.target_id != e.id)
                {
                    EntitySnapshot tgt;
                    if (cur.TryGetValue(e.target_id, out tgt) && tgt != null)
                    {
                        var toTarget = GameConst.MilliToWorld(tgt.x_milli, tgt.y_milli) - world;
                        if (toTarget.sqrMagnitude > 0.0001f) moveDir = toTarget;
                    }
                }

                view.Apply(e, world, e.deploy_ms > 0 ? DeployAlpha : 1f, moveDir);
                // 本帧精灵显示的位置（= 特效要锚的位置，见 `_renderWorld` 的注释）。
                _renderWorld[e.id] = world;
            }

            // 回收：本帧快照里没有的实体（快照是全量 ⇒ 缺席 = 已消失）。
            _recycle.Clear();
            foreach (var kv in _units)
                if (!cur.ContainsKey(kv.Key)) _recycle.Add(kv.Key);
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
            var input = DeployInputNow();
            return PlacementIndicator.IsLegalDeploy(input, tileXY.x, tileXY.y, isSpell);
        }

        /// <summary>当前这一帧的合法性输入（塔的存活状态 + 我方队伍）—— 判定与区域显示共用同一份。</summary>
        private PlacementIndicator.DeployInput DeployInputNow()
        {
            return new PlacementIndicator.DeployInput
            {
                MyTeam = _myTeam,
                EnemyLeftPrincessAlive = _enemyLeftAlive,
                EnemyRightPrincessAlive = _enemyRightAlive,
                OwnLeftPrincessAlive = _ownLeftAlive,
                OwnRightPrincessAlive = _ownRightAlive
            };
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
            // 区域显示（哪些地方放不下这副牌）—— 整次拖动里区域是常量，本调用只切显隐（见 DeployAreaView）。
            if (_deployArea != null)
                _deployArea.Show(_myTeam, isSpell, _enemyLeftAlive, _enemyRightAlive, _ownLeftAlive, _ownRightAlive);
            _indicator.SetDropCard(cardArt);
            _indicator.Show(tileXY, radiusTiles, IsDeployLegal(tileXY, isSpell));
        }

        /// <summary>
        /// 落点处的**落位计时件**：出牌落位期间在落点摆原版 `troopDeployTimer`（时长 = 服务端 `deploy_time`）。
        /// 由 <see cref="PlayDeploy"/> 调用 —— 它只表示"这张卡在就位"，⛔ 不表示落点是否合法。
        /// </summary>
        /// <param name="hostile">落位的是不是敌方单位（决定扫过片用我方蓝还是敌方红，见 PlacementIndicator）。</param>
        /// <param name="cardId">这块表盘属于哪张卡（同一次出牌的去重钥匙之一，见 PlacementIndicator.ShowDeployRing）。</param>
        public void ShowDeployRing(Vector2 tileXY, float radiusTiles, float seconds, bool hostile = false, int cardId = 0)
        {
            var ring = AcquireDeployRing();
            if (ring == null) return;
            ring.ShowDeployRing(tileXY, radiusTiles, seconds, hostile, cardId);
        }

        /// <summary>
        /// 从池里取一块落位表盘给**这一次出牌**用：优先空闲的（<see cref="PlacementIndicator.DeployTimerActive"/>
        /// 为 false）；全忙时复用**剩余时间最少**的那块（先到期的先让位，尽量保住"转得最久的那块"的完整一圈）。
        /// <para>
        /// ⛔ 池没建起来（对局画面未建 / 已销毁）⇒ 返回 <c>null</c>，调用方静默跳过（与旧的 `_deployRing == null` 同口径）。
        /// </para>
        /// </summary>
        private PlacementIndicator AcquireDeployRing()
        {
            if (_deployRings == null || _deployRings.Length == 0) return null;
            PlacementIndicator best = null;
            for (var i = 0; i < _deployRings.Length; i++)
            {
                var r = _deployRings[i];
                if (r == null) continue;
                if (!r.DeployTimerActive) return r;                                  // 空闲优先
                if (best == null || r.DeployLeft < best.DeployLeft) best = r;      // 否则留最后再复用
            }
            return best;
        }

        /// <summary>隐藏落点指示与不可放置区域（抬手 / 取消拖放）。</summary>
        public void HidePlacement()
        {
            if (_deployArea != null) _deployArea.Hide();
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
        /// 卡 id → 精灵目录的**兜底表**。目录的权威来源是服务端随卡池下发的 `CardInfo.sprite_dir`
        /// （见 <see cref="TryGetCardVisual"/>）；本表只在该字段为空时生效（旧服务端 / 卡池尚未到达），
        /// 保证"旧服务端 + 新客户端"也有图。
        /// <para>
        /// 表值 = `server/game/table/tsv/card.tsv` 的 `id` 与 `sprite_dir` 两列。
        /// 多个卡指向同一目录是正常的（骷髅兵 / 骷髅军团共用 `chr_skeleton_out`、
        /// 黑暗王子与王子共用 `chr_prince_out`）。
        /// </para>
        /// <para>
        /// 判目录走 id 而不是 key：id 直接来自 `EntitySnapshot.card_id`，快照先于卡池到达时也无时序依赖。
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, CardVisual> CardVisuals = new Dictionary<int, CardVisual>
        {
            { 26010001, new CardVisual("chr_knight_out", false) },  // knight
            { 26010002, new CardVisual("chr_archer_out", false) },  // archers
            { 26010003, new CardVisual("chr_goblin_out", false) },  // goblins
            { 26010004, new CardVisual("chr_goblin_archer_out", false) },  // spear-goblins
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

        /// <summary>
        /// 静态卡池索引（`card_id → CardInfo`）：供不依赖对局实例的预览查询使用
        /// （见 <see cref="TryGetDeployPreview"/>）。由卡池事件与 <see cref="OnCardPool"/> 共同填充。
        /// </summary>
        private static readonly Dictionary<int, CardInfo> PreviewCards = new Dictionary<int, CardInfo>();

        /// <summary>静态卡池索引是否已订阅卡池事件（只订阅一次）。</summary>
        private static bool _previewPoolSubscribed;

        /// <summary>卡池事件 → 静态索引（见 <see cref="PreviewCards"/>）。</summary>
        private static void OnPreviewCardPool(CardInfo[] cards)
        {
            if (cards == null) return;
            for (var i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                if (c != null) PreviewCards[c.id] = c;
            }
        }

        /// <summary>首次查询时把静态索引接到卡池事件上（事件可能在面板打开前就发过，见调用方）。</summary>
        private static void EnsurePreviewPoolSubscribed()
        {
            if (_previewPoolSubscribed) return;
            var bus = Game.Event;
            if (bus == null) return;
            bus.On<CardInfo[]>(Events.Deck.PoolLoaded, OnPreviewCardPool);
            _previewPoolSubscribed = true;
        }

        /// <summary>
        /// 部署预览用的**一张卡的 idle 首帧**（卡池 `sprite_dir` + 已预热的 <see cref="SpriteBank"/> 帧表）
        /// 与**逐只落点布局**（一张卡出几个就摆几个，偏移与服务端 `spawnGroup` 同一套）。
        /// <para>
        /// 精灵目录优先取卡池下发的 `CardInfo.sprite_dir`（type=2 走建筑目录）；卡池还没到时回落
        /// <see cref="CardVisuals"/> 兜底表。法术卡（<c>type == 1</c> / `ui_spells_*` 目录）返回 false。
        /// </para>
        /// <para>
        /// <paramref name="scale"/> = 该目录在战场上实际生效的逐单位缩放（<see cref="UnitView.PreviewScaleFor"/>），
        /// 预览按它摆即可与实战同尺寸。
        /// </para>
        /// <para>
        /// <paramref name="offsetsTiles"/> = 逐只模型相对落点的偏移（**格**，长度 = 只数）：
        /// 群体卡按 <see cref="DeployGroups"/> 的 (n, 环半径, 身体半径) 过 <see cref="DeployLayoutTiles"/>
        /// （= 服务端 `core.SummonLayout` 同一三分支），单体 = 1 只 (0,0)。法术卡 0 只（不显示）。
        /// </para>
        /// </summary>
        public static bool TryGetDeployPreview(int cardId, out Sprite sprite, out float scale, out bool isSpell,
            out Vector2[] offsetsTiles)
        {
            sprite = null;
            scale = 1f;
            isSpell = false;
            offsetsTiles = null;
            EnsurePreviewPoolSubscribed();

            string dir;
            var isBuilding = false;
            CardInfo card;
            if (PreviewCards.TryGetValue(cardId, out card) && card != null)
            {
                isSpell = card.type == CardTypeSpell;
                if (isSpell) return false;
                dir = card.sprite_dir;
                isBuilding = card.type == 2;
            }
            else
            {
                CardVisual v;
                if (!CardVisuals.TryGetValue(cardId, out v)) return false;
                dir = v.Dir;
                isBuilding = v.IsBuilding;
                isSpell = !string.IsNullOrEmpty(dir) && dir.StartsWith("ui_spells", System.StringComparison.Ordinal);
                if (isSpell) return false;
            }
            if (string.IsNullOrEmpty(dir)) return false;

            var path = isBuilding ? ResPaths.BuildingDir(dir) : ResPaths.UnitDir(dir);
            var frames = SpriteBank.Preload(path, SpriteBank.SpritePivotMode.UnifiedCanvasAnchor).Frames;
            if (frames == null || frames.Length == 0) return false;

            var frameNo = IdleFirstFrameNo(dir);
            sprite = FindFrameByNo(frames, frameNo) ?? frames[0];
            scale = UnitView.PreviewScaleFor(dir);
            var group = DeployGroupOf(cardId);
            offsetsTiles = DeployLayoutTiles(group.Count, group.RadiusMt, group.BodyRadiusMt);
            return true;
        }

        /// <summary>
        /// 一张卡的**落点召唤**口径：只数 / 环形铺设半径 / 单位身体半径（后两者单位 = milli-tile）。
        /// </summary>
        private struct DeployGroup
        {
            /// <summary>落点一次铺出几只（单体 / 建筑 = 1）。</summary>
            public readonly int Count;

            /// <summary>环形铺设半径（milli-tile；0 = 无环，走包圆布局）。</summary>
            public readonly int RadiusMt;

            /// <summary>召唤出的单位身体半径（milli-tile，包圆布局的间距依据）。</summary>
            public readonly int BodyRadiusMt;

            public DeployGroup(int count, int radiusMt, int bodyRadiusMt)
            {
                Count = count;
                RadiusMt = radiusMt;
                BodyRadiusMt = bodyRadiusMt;
            }
        }

        /// <summary>
        /// 群体卡（落点召唤）的 (只数, 环半径, 身体半径) 表。表值 = `server/game/table/tsv/unit.tsv`
        /// 的 `summon_n` / `summon_radius_mt` / 召唤行 `radius_mt` 三列（卡 id 与
        /// <see cref="CardVisuals"/> 同源 `card.tsv` 的 `id` 列）：archers 2/0、goblins 4/700、
        /// spear-goblins 3/0、minions 3/0、minion-horde 6/600、barbarians 5/700、skeletons 3/700、
        /// skeleton-army 15/0、bats 5/750；其余卡单体 = 1。
        /// <para>
        /// 协议 `CardInfo` 不带召唤数量/半径字段 ⇒ 按本表回落（口径即 `unit.tsv`）。
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, DeployGroup> DeployGroups = new Dictionary<int, DeployGroup>
        {
            { 26010002, new DeployGroup(2, 0, 500) },     // archers（Archer）
            { 26010003, new DeployGroup(4, 700, 500) },   // goblins（Goblin）
            { 26010004, new DeployGroup(3, 0, 500) },     // spear-goblins（SpearGoblin）
            { 26010007, new DeployGroup(3, 0, 500) },     // minions（Minion）
            { 26010008, new DeployGroup(6, 600, 500) },   // minion-horde（Minion）
            { 26010011, new DeployGroup(5, 700, 500) },   // barbarians（Barbarian）
            { 26010012, new DeployGroup(3, 700, 500) },   // skeletons（Skeleton）
            { 26010013, new DeployGroup(15, 0, 500) },    // skeleton-army（Skeleton）
            { 26010038, new DeployGroup(5, 750, 500) },   // bats（Bat）
        };

        /// <summary>
        /// milli-tile 取整到整数（.5 远离零，与服务端 `math.Round` 同款 —— ⛔ `Mathf.Round` 是银行家舍入，
        /// 边界值会与服务端的落点错开半格）。
        /// </summary>
        private static float RoundMt(float v)
        {
            return Mathf.Sign(v) * Mathf.Floor(Mathf.Abs(v) + 0.5f);
        }

        /// <summary>这张卡的落点召唤口径（不在表里 = 单体 1 只）。</summary>
        private static DeployGroup DeployGroupOf(int cardId)
        {
            DeployGroup g;
            if (DeployGroups.TryGetValue(cardId, out g)) return g;
            return new DeployGroup(1, 0, 500);
        }

        /// <summary>
        /// 一张卡的逐只落点偏移（**格**）—— 与服务端 `core.SummonLayout`（`server/game/core/units.go:381`）
        /// 同一三分支：① 1 只 = 原点；② 环半径 ≤ 0 ⇒ 包圆（<see cref="PackOffsetsTiles"/>）；
        /// ③ 环周长装不下整组（2πr &lt; n·2R）⇒ 仍包圆；否则等分角的环（<see cref="RingOffsetsTiles"/>）。
        /// ⛔ 不随机 —— 与服务端落点逐只对齐。
        /// </summary>
        private static Vector2[] DeployLayoutTiles(int count, int radiusMt, int bodyRadiusMt)
        {
            if (count < 1) count = 1;
            if (count == 1) return new[] { Vector2.zero };
            if (radiusMt <= 0) return PackOffsetsTiles(count, bodyRadiusMt);
            if (bodyRadiusMt > 0)
            {
                var circumference = 2f * Mathf.PI * radiusMt;
                var need = (float)count * 2f * bodyRadiusMt;
                if (circumference < need) return PackOffsetsTiles(count, bodyRadiusMt);
            }
            return RingOffsetsTiles(count, radiusMt, 0);
        }

        /// <summary>
        /// 等分角的环形布局（milli-tile → 格）：`units.go:346 RingOffsets` 同款
        /// （步长 2π/n、起始角 = startEighth·π/4）。
        /// </summary>
        private static Vector2[] RingOffsetsTiles(int count, int radiusMt, int startEighth)
        {
            if (count < 1) count = 1;
            if (count == 1 || radiusMt <= 0) return new[] { Vector2.zero };
            var step = 2f * Mathf.PI / count;
            var phase = startEighth * Mathf.PI / 4f;
            var outv = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var a = phase + step * i;
                outv[i] = new Vector2(
                    RoundMt(radiusMt * Mathf.Cos(a)) / 1000f,
                    RoundMt(radiusMt * Mathf.Sin(a)) / 1000f);
            }
            return outv;
        }

        /// <summary>
        /// 包圆布局（milli-tile → 格）：`units.go:283 PackOffsets` 同款 ——
        /// ≤ 8 只一圈（半径 = max(间距, 间距/(2·sin(π/n)))，相邻相切）；更多只自内向外同心环。
        /// </summary>
        private static Vector2[] PackOffsetsTiles(int count, int bodyRadiusMt)
        {
            if (count < 1) count = 1;
            if (count == 1 || bodyRadiusMt <= 0) return new[] { Vector2.zero };
            var spacing = 2f * bodyRadiusMt;
            if (count <= 8)
            {
                var radius = spacing;
                var r = spacing / (2f * Mathf.Sin(Mathf.PI / count));
                if (r > radius) radius = r;
                var step = 2f * Mathf.PI / count;
                var outv = new Vector2[count];
                for (var i = 0; i < count; i++)
                {
                    outv[i] = new Vector2(
                        RoundMt(radius * Mathf.Cos(step * i)) / 1000f,
                        RoundMt(radius * Mathf.Sin(step * i)) / 1000f);
                }
                return outv;
            }
            var list = new List<Vector2>(count) { Vector2.zero };
            for (var ring = 1; list.Count < count; ring++)
            {
                var radius = ring * spacing;
                var capacity = (int)(2f * Mathf.PI * radius / spacing);
                if (capacity < 1) capacity = 1;
                var take = capacity;
                var rest = count - list.Count;
                if (rest < take) take = rest;
                var step = 2f * Mathf.PI / take;
                var phase = ring % 2 == 1 ? step / 2f : 0f;
                for (var i = 0; i < take; i++)
                {
                    var a = phase + step * i;
                    list.Add(new Vector2(
                        RoundMt(radius * Mathf.Cos(a)) / 1000f,
                        RoundMt(radius * Mathf.Sin(a)) / 1000f));
                }
            }
            return list.ToArray();
        }

        /// <summary>该目录 idle 档的首帧号；表里没有该目录 / 该档不可用 ⇒ -1（调用方取帧表首帧）。</summary>
        private static int IdleFirstFrameNo(string dir)
        {
            UnitAnimTable.Entry entry;
            if (!UnitAnimTable.TryGet(dir, out entry) || entry.Tiers == null || entry.Tiers.Length == 0) return -1;
            var idle = entry.Tiers[0];
            if (!idle.Known || idle.Runs == null || idle.Runs.Length < 2) return -1;
            return idle.Runs[0];
        }

        /// <summary>按原版帧号在帧表里找精灵（帧名规则见 <see cref="SpriteBank.ParseFrameIndex"/>）。</summary>
        private static Sprite FindFrameByNo(Sprite[] frames, int frameNo)
        {
            if (frames == null || frameNo < 0) return null;
            for (var i = 0; i < frames.Length; i++)
            {
                if (frames[i] != null && SpriteBank.ParseFrameIndex(frames[i].name) == frameNo) return frames[i];
            }
            return null;
        }

        /// <summary>
        /// 一张卡的绘制信息：**优先用服务端随卡池下发的 `CardInfo.sprite_dir`**（type=2 走建筑目录，
        /// 其余走单位目录）；该字段为空（旧服务端 / 卡池还没到）时回落到 <see cref="CardVisuals"/> 兜底表。
        /// </summary>
        private bool TryGetCardVisual(int cardId, out CardVisual v)
        {
            CardInfo card;
            if (_cards.TryGetValue(cardId, out card) && card != null && !string.IsNullOrEmpty(card.sprite_dir))
            {
                v = new CardVisual(card.sprite_dir, card.type == 2);
                return true;
            }
            return CardVisuals.TryGetValue(cardId, out v);
        }

        private CardVisual ResolveVisual(EntitySnapshot e)
        {
            if (e.kind == 2)
                // kind=2 是塔，⛔ 不该出现在 entities 里（塔由 towers_a/b 报告）——
                // 真出现就当"没有单位精灵"，不编一个 chr_* 出来。
                return default(CardVisual);

            // 召唤物按**自身目录**画（服务端逐实体下发）：女巫召唤的骷髅 card_id 仍是女巫，
            // 只按 card_id 会画成女巫。旧服务端没有该字段时回落 card_id 那一套。
            if (!string.IsNullOrEmpty(e.sprite_dir))
                return new CardVisual(e.sprite_dir, e.kind == 1);

            CardVisual v;
            if (TryGetCardVisual(e.card_id, out v)) return v;

            if (UnknownCards.Add(e.card_id))
                Game.Logger?.Warn(LogTag,
                    $"卡 {e.card_id}（kind={e.kind}）既没有下发的 sprite_dir 也不在兜底表里 ⇒ 用占位色");
            return default(CardVisual);
        }
    }
}
