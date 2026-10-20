using System;
using System.Collections.Generic;
using CloverEngine;
using CR.Def;
using UnityEngine;

namespace CR.View
{
    /// <summary>
    /// 对局音效播放器：把 <c>Core/Events.cs</c> 里**已有**的战斗事件翻成
    /// <c>Game.Sound.PlaySFX(clipName)</c>（引擎 <c>ISoundManager</c>）。
    ///
    /// <para>
    /// <b>放 <c>View/</c> 而不是 <c>Module/</c> 的理由</b>：音效是**表现层**（听感），
    /// 与 <see cref="EffectsView"/>（特效）/ <see cref="UnitView"/> 同级；本类只"读事件、播音"，
    /// ⛔ 不发任何 C2S、不判规则（那是 <c>Module/Battle</c> 的事）。
    /// 契约上 <c>View → Core</c> 允许（本类只碰 <c>Core/Events.cs</c> 的事件名 + clover-engine 门面）。
    /// </para>
    ///
    /// <para>
    /// <b>事件 → 音效映射（逐条对 <c>Core/Events.cs</c>）</b>：
    /// <list type="bullet">
    /// <item>出牌 —— <c>Events.Battle.PlayCardRequest</c>（<c>(int cardId, Vector2 worldPos)</c>）⇒ <see cref="AudioPaths.PlayCard"/>。
    ///   请求只由**本机玩家**抬手触发（<c>HudPanel</c> 发出），语义上必然是"己方出牌"，
    ///   正好对上原版的己方召唤音（<c>summon_own_07</c>，见 <c>AudioPaths</c>）；
    ///   与请求同帧发声才有原版的即时感（服务端裁决一般通过）。
    /// </item>
    /// <item>死亡 —— <c>Events.Battle.Events</c> 的 <c>kind == 2</c>（<c>EvDeath</c>）⇒ <see cref="AudioPaths.Death"/>。
    ///   （与 <see cref="BattleViewRoot"/> 在 kind==2 播的死亡特效同帧：死亡只播音效 + die 档特效。）</item>
    /// <item>命中（非致死）—— <c>Events.Battle.Snapshot</c> 里同 id 的 <c>hp</c> 下降 ⇒ <see cref="AudioPaths.Hit"/>。
    ///   （协议没有「命中」事件，见 <see cref="OnSnapshot"/>；单帧上限 4 声。）</item>
    /// <item>敌方出牌 —— 同事件 <c>kind == 0</c>（<c>EvPlayCard</c>）且 <c>team != 我方队伍</c> ⇒ <see cref="AudioPaths.EnemySummon"/>。
    ///   我方队伍取 <see cref="BattleViewRoot.MyTeam"/>（见 <see cref="Create"/>）。</item>
    /// <item>塔激活 —— 同事件 <c>kind == 5</c>（<c>EvTowerActivated</c>）⇒ <see cref="AudioPaths.TowerActivate"/>。</item>
    /// <item>平局 —— <c>Events.Battle.Ended</c> 的 <c>draw == true</c> ⇒ <see cref="AudioPaths.Draw"/>。</item>
    /// <item>UI 点击 / 卡牌拖起 / 放置被拒 / 倒计时最后 10 秒 —— 分别由 <c>UI/CrUiStyle</c>（按钮统一点）
    ///   与 <c>UI/Panels/HudPanel</c>（拖放 / 计时）直接播 <see cref="AudioPaths.UiClick"/> /
    ///   <see cref="AudioPaths.GrabCard"/> / <see cref="AudioPaths.BadDrop"/> / <see cref="AudioPaths.CountdownTick"/>。</item>
    /// <item>塔毁 —— 同事件 <c>kind == 3</c>（<c>EvTowerDestroyed</c>）⇒ <see cref="AudioPaths.TowerDown"/>
    ///   （与 kind==3 播的 <c>ResPaths.EffectBlast</c> 爆炸同源）。</item>
    /// <item>圣水满 —— 同事件 <c>kind == 4</c>（圣水满）⇒ <see cref="AudioPaths.ElixirFull"/>。</item>
    /// <item>圣水倍率段（2 倍 / 3 倍）与加时赛 —— 由 <c>UI/Panels/HudPanel</c> 播
    ///   （<see cref="AudioPaths.ElixirRate2x"/> / <see cref="AudioPaths.ElixirRate3x"/> /
    ///   <see cref="AudioPaths.OvertimeJingle"/>）：那一条链上才**同时**有 `timeline.elixir_phase_ms`
    ///   与快照的 `server_ms` —— 本组件由对局场景晚建，会**错过** `Battle.Started`（时间线拿不到）。</item>
    /// <item>胜负 —— <c>Events.Battle.Ended</c>（<c>BattleEndNotify</c>，<c>win</c> 字段）⇒ <see cref="AudioPaths.Win"/> / <see cref="AudioPaths.Lose"/>。</item>
    /// <item><b>逐卡攻击音</b> —— 快照里实体 `anim` 由非 2 变 2（每一次挥砍 / 开火的上升沿）⇒
    ///   该卡 key 的攻击音（<see cref="AudioPaths.Cards.TryGetAttack"/>）；法术卡没有实体 ⇒ 挂在
    ///   `kind == 0`（<c>EvPlayCard</c>）上（见 <see cref="PlaySpellCast"/>）。</item>
    /// <item><b>逐卡命中音</b> —— 快照里某实体 `hp` 下降 ⇒ 按**被打者**卡 key 播命中音
    ///   （<see cref="AudioPaths.Cards.TryGetHit"/>）；该卡无命中音 / 卡未知名 ⇒ 回落 <see cref="AudioPaths.Hit"/>。</item>
    /// <item><b>塔开火 / 塔受击</b> —— `kind == 6`（<c>EvTowerShoot</c>）按开火塔的 <c>TowerState.kind</c> 播
    ///   <see cref="AudioPaths.TowerPrincessFire"/> / <see cref="AudioPaths.TowerKingFire"/>；
    ///   塔 hp 下降播 <see cref="AudioPaths.Hit"/>。</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>怎么播（⛔ 只用 clover-engine 的声音 API）</b>：一律 <c>Game.Sound.PlaySFX</c> —— 它内部有音源池 /
    /// 分组音量 / 单帧与同 clip 并发闸门（<c>Sound.cs</c>）。本类**不自己 <c>AddComponent&lt;AudioSource&gt;</c>、
    /// 不自建对象池**：那等于绕开 clover-engine 的音效池与音量分组，SettingsManager 的音效音量就对不上了。
    /// </para>
    ///
    /// <para>
    /// <b>音量</b>：本类**不碰音量** —— 音效走 clover-engine 的 <c>SoundGroup.SFX</c> 分组，音量由
    /// <c>Module/Settings/SettingsManager</c> 经 <c>Game.Sound.SetVolume(SoundGroup.SFX, …)</c> 应用
    ///（只**读**它的既有契约，一字未改）。
    /// </para>
    ///
    /// <para>
    /// <b>挂点</b>：唯一挂点是 <see cref="BattleViewRoot.Build"/> 里对
    /// <see cref="Create"/> 的一次调用（挂在 <c>BattleContent</c> 下，与 <c>EffectsView</c> 并列，
    /// 并把该控制器自身作为队伍来源传进来）——
    /// 于是它的生命周期天然跟着"对局画面"走：进图建、出图（<c>BattleContent</c> 随场景卸载）时
    /// <c>OnDestroy</c> 自动退订。⛔ 别在别处再挂一次。
    /// </para>
    /// </summary>
    public sealed class BattleAudioView : MonoBehaviour
    {
        /// <summary>日志标签。</summary>
        public const string LogTag = "BattleAudioView";

        // ── 离散事件 kind：逐字对应 `Def/ProtoDef.cs:194`（`BattleEvent.kind` 的注释）
        //    —— 与服务端 `server/game/core/snapshot.go` 的 `Ev*` 常量同一套编号。──

        /// <summary>`kind == 0`：出牌（`EvPlayCard`）。本类**不在此播**（走 <c>PlayCardRequest</c>，见类注释）。</summary>
        private const int EventKindPlayCard = 0;

        /// <summary>`kind == 1`：生成（`EvSpawn`）。无对应音效槽。</summary>
        private const int EventKindSpawn = 1;

        /// <summary>`kind == 2`：死亡（`EvDeath`）。</summary>
        private const int EventKindDeath = 2;

        /// <summary>`kind == 3`：塔毁（`EvTowerDestroyed`）。</summary>
        private const int EventKindTowerDestroyed = 3;

        /// <summary>`kind == 4`：圣水满。</summary>
        private const int EventKindElixirFull = 4;

        /// <summary>`kind == 5`：塔激活（`EvTowerActivated`）。无对应音效槽。</summary>
        private const int EventKindTowerActivated = 5;

        /// <summary>`kind == 6`：塔开火（`EvTowerShoot`）。载荷带开火塔的 id ⇒ 按该塔类型播开火音。</summary>
        private const int EventKindTowerShoot = 6;

        /// <summary>卡类型 = 法术（`Def/ProtoDef.cs` 的 `CardInfo.type`：`0=部队 1=法术 2=建筑`）。</summary>
        private const int CardTypeSpell = 1;

        /// <summary>塔类型 = 国王塔（`TowerState.kind`：`0=公主塔 1=国王塔`）。</summary>
        private const int TowerKindKing = 1;

        /// <summary>`anim` 档位 = 攻击（`Def/ProtoDef.cs` 的 `EntitySnapshot.anim`：`0=idle 1=walk 2=attack 3=die`）。</summary>
        private const int AnimAttack = 2;

        /// <summary>攻击音单帧上限：一次快照里多个单位同时进入攻击档时，最多播这么多次（防空爆刷屏）。</summary>
        private const int MaxAttackPerSnapshot = 3;

        /// <summary>同一 clip 的最小起播间隔（秒）：并排单位同帧挥砍时错开，⛔ 不叠成一记重击。</summary>
        private const float MinSameClipIntervalSeconds = 0.05f;

        private bool _subscribed;

        /// <summary>`Game.Sound` 为空只报一次（表现域未挂载 ⇒ 整局都播不出声，不该每帧刷屏）。</summary>
        private bool _serviceWarned;

        /// <summary>音效资源缺失只报一次（引擎 <c>Sound.cs</c> 已对同一路径 WarnOnce；这里是我们自己的"问一句在不在"，同样只报一次）。</summary>
        private bool _missingWarned;

        /// <summary>未识别的 `kind` 只报一次（协议扩了 kind 而本类没跟上时，一次留痕即可，⛔ 不静默）。</summary>
        private bool _unknownKindWarned;

        // 订阅回调字段（缓存委托，退订才能配对 —— 传方法组会新建委托、Off 不掉）。
        private Action<int, Vector2> _onPlayCardRequest;
        private Action<BattleEventNotify> _onEvents;
        private Action<BattleEndNotify> _onEnded;
        private Action<BattleStartNotify> _onStarted;
        private Action<BattleSnapshot> _onSnapshot;
        private Action<CardInfo[]> _onCardPool;

        /// <summary>`BattleStartNotify.my_team`（0=BLUE 1=RED）。仅作 <see cref="_root"/> 缺失时的兜底，见 <see cref="MyTeam"/>。</summary>
        private int _myTeam;

        /// <summary>
        /// 上一帧快照里每个实体的 `hp`（`entity_id → hp`）。
        /// <para>
        /// **命中音的判据**：协议没有「命中」事件，只有每 10 Hz 的全量快照 ⇒
        /// 「同 id 的 `hp` 掉了多少」就是"这一帧被打了"的唯一可得信号（见 <see cref="AudioPaths.Hit"/>）。
        /// 没被 `_prevHp` 覆盖到的 id（新生成的单位）**不判命中** —— 否则出场第一帧会被误判成挨打。
        /// </para>
        /// </summary>
        private Dictionary<int, int> _prevHp = new Dictionary<int, int>();

        /// <summary>本帧 hp 的暂存（双缓冲：比完 <see cref="_prevHp"/> 后交换，⛔ 不每帧 new Dictionary）。</summary>
        private Dictionary<int, int> _curHp = new Dictionary<int, int>();

        /// <summary>命中音单帧上限：一次快照里有多个单位同时掉血时，最多播这么多次（防空爆刷屏）。</summary>
        private const int MaxHitPerSnapshot = 4;

        /// <summary>
        /// 上一帧每个实体的 `anim`（`entity_id → anim`）。攻击音的判据 = **上升沿**
        /// （`anim` 由非 2 变成 2）。服务端每挥一次刀都先置 2、`holdAttackAnim` 保住 4 tick 再收回
        /// （`server/game/core/combat.go:67` / `battle.go:672`）⇒ 每次挥砍都有独立的上升沿，
        /// 与 `UnitView` 重播攻击 clip 用的是同一条边沿。
        /// </summary>
        private Dictionary<int, int> _prevAnim = new Dictionary<int, int>();

        /// <summary>本帧 `anim` 的暂存（双缓冲，用法同 <see cref="_curHp"/>）。</summary>
        private Dictionary<int, int> _curAnim = new Dictionary<int, int>();

        /// <summary>卡池索引（`card_id → CardInfo`）：卡 key 与类型（0=部队 1=法术 2=建筑）的唯一来源。</summary>
        private readonly Dictionary<int, CardInfo> _cards = new Dictionary<int, CardInfo>();

        /// <summary>本局卡池还没到时查不到的 card_id（只 Warn 一次 / id，⛔ 不刷屏）。</summary>
        private readonly HashSet<int> _cardMissingWarned = new HashSet<int>();

        /// <summary>卡表里没有攻击音的卡 key（只 Warn 一次 / key）。</summary>
        private readonly HashSet<string> _missingAttackWarned = new HashSet<string>();

        /// <summary>每个 clip 上一次起播的时刻（`Time.unscaledTime`）：<see cref="MinSameClipIntervalSeconds"/> 的限流依据。</summary>
        private readonly Dictionary<string, float> _lastClipAt = new Dictionary<string, float>();

        /// <summary>塔 id → 塔类型（0=公主塔 1=国王塔），每帧从快照的 `towers_a` / `towers_b` 重建。</summary>
        private readonly Dictionary<int, int> _towerKind = new Dictionary<int, int>();

        /// <summary>上一帧塔 hp：键 = 队伍 × 1000 + 塔 id（塔 id = 服务端全局实体 id，蓝 1/2/3、红 4/5/6）。</summary>
        private Dictionary<int, int> _prevTowerHp = new Dictionary<int, int>();

        /// <summary>本帧塔 hp 的暂存（双缓冲）。</summary>
        private Dictionary<int, int> _curTowerHp = new Dictionary<int, int>();

        /// <summary>塔开火事件在快照之前到达、查不到塔类型时只 Warn 一次。</summary>
        private bool _towerKindWarned;

        /// <summary>卡池一直没到（<see cref="_cards"/> 为空）只 Warn 一次。</summary>
        private bool _poolMissingWarned;

        // ── 本局计数（玩法自检 / 探针读数；一行一个判据，⛔ 不只看日志）──

        /// <summary>本局真的提交播放的**卡牌攻击音**条数。</summary>
        public int CardAttackPlays { get; private set; }

        /// <summary>本局真的提交播放的**卡牌命中音**条数（专属 + 通用回落都算）。</summary>
        public int CardHitPlays { get; private set; }

        /// <summary>本局真的提交播放的**塔开火音**条数。</summary>
        public int TowerAttackPlays { get; private set; }

        /// <summary>本局真的提交播放的**塔受击音**条数。</summary>
        public int TowerHitPlays { get; private set; }

        /// <summary>本局因单帧上限 / 同 clip 限流而没播的卡牌攻击音条数。</summary>
        public int CardAttackSkipped { get; private set; }

        /// <summary>我方队伍（0=BLUE 1=RED）—— 统一从 <see cref="BattleViewRoot.MyTeam"/> 读，见 <see cref="Create"/>。</summary>
        private BattleViewRoot _root;

        /// <summary>
        /// 建出音效节点（幂等）。照 <see cref="EffectsView.Create"/> 的范式。
        ///
        /// <para>
        /// <paramref name="root"/> = <see cref="BattleViewRoot"/>（由它在 <c>Build()</c> 里传入）。
        /// 本组件由对局场景建出、订阅时刻**晚于** <c>Events.Battle.Started</c>（首次进图时该事件先于
        /// <c>Scene.Load</c> 发出）⇒ 拿不到 <c>my_team</c>。该控制器先于本组件订阅同一事件、且整场常驻，
        /// 所以「我方是哪一队」一律取 <see cref="BattleViewRoot.MyTeam"/>（View → View 的一条只读引用）。
        /// </para>
        /// </summary>
        public static BattleAudioView Create(Transform parent, BattleViewRoot root)
        {
            if (parent != null)
            {
                var existing = parent.GetComponentInChildren<BattleAudioView>(true);
                if (existing != null)
                {
                    existing.Bind(root);
                    return existing;
                }
            }
            var go = new GameObject("BattleAudio");
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<BattleAudioView>();
            view.Bind(root);
            // 当场订阅：本组件在 `BattleViewRoot.Build()` 内被建出，同一帧稍后 `Module/Battle` 会补发
            // 卡池事件（`ReplayCardPoolForHud`）⇒ 等到下一次 Update 才订阅就会错过它，逐卡音效查不到卡 key。
            if (Game.IsRunning && Game.Event != null) view.Subscribe();
            return view;
        }

        /// <summary>绑定队伍来源（幂等：重复绑定只更新引用）。</summary>
        public void Bind(BattleViewRoot root)
        {
            _root = root;
        }

        /// <summary>
        /// 我方队伍（0=BLUE 1=RED）：优先取 <see cref="BattleViewRoot.MyTeam"/>（先于本组件订阅、整场常驻），
        /// <see cref="_root"/> 缺失时退回本组件自己订阅到的 <see cref="_myTeam"/>。
        /// </summary>
        public int MyTeam { get { return _root != null ? _root.MyTeam : _myTeam; } }

        private void Update()
        {
            // 时序：本组件可能早于 `Game.Launch` 存在（那时 `Game.Event` 为 null），轮询到可订阅为止。
            // 与 `BattleViewRoot.Update` 同一处置（同相位钩子，顺序不保证）。
            if (!_subscribed && Game.IsRunning && Game.Event != null) Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            var bus = Game.Event;
            if (bus == null) return;

            _onPlayCardRequest = OnPlayCardRequest;
            _onEvents = OnBattleEvents;
            _onEnded = OnBattleEnded;
            _onStarted = OnBattleStarted;
            _onSnapshot = OnSnapshot;
            _onCardPool = OnCardPool;

            bus.On<int, Vector2>(Events.Battle.PlayCardRequest, _onPlayCardRequest);
            bus.On<BattleEventNotify>(Events.Battle.Events, _onEvents);
            bus.On<BattleEndNotify>(Events.Battle.Ended, _onEnded);
            bus.On<BattleStartNotify>(Events.Battle.Started, _onStarted);
            bus.On<BattleSnapshot>(Events.Battle.Snapshot, _onSnapshot);
            bus.On<CardInfo[]>(Events.Deck.PoolLoaded, _onCardPool);

            _subscribed = true;
            // 日志里刻意不写事件名字面量（事件名只有 `Core/Events.cs` 一处定义；写进日志会污染对「裸事件名」的静态检查）。
            Game.Logger?.Info(LogTag, "音效已订阅：出牌请求 + 离散事件（命中 / 死亡 / 塔毁 / 塔激活 / 圣水满 / 敌方出牌 / 塔开火）+ 结算（胜/负/平）+ 快照（非致死命中 / 攻击档）+ 卡池（逐卡音效）");
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var bus = Game.Event;
            if (bus != null)
            {
                bus.Off<int, Vector2>(Events.Battle.PlayCardRequest, _onPlayCardRequest);
                bus.Off<BattleEventNotify>(Events.Battle.Events, _onEvents);
                bus.Off<BattleEndNotify>(Events.Battle.Ended, _onEnded);
                bus.Off<BattleStartNotify>(Events.Battle.Started, _onStarted);
                bus.Off<BattleSnapshot>(Events.Battle.Snapshot, _onSnapshot);
                bus.Off<CardInfo[]>(Events.Deck.PoolLoaded, _onCardPool);
            }
            _subscribed = false;
        }

        // ═════════════════════════ 事件 → 音效 ═════════════════════════

        private void OnPlayCardRequest(int cardId, Vector2 worldPos)
        {
            Play(AudioPaths.PlayCard);
        }

        private void OnBattleEvents(BattleEventNotify n)
        {
            if (n == null || n.events == null) return;
            for (var i = 0; i < n.events.Length; i++)
            {
                var e = n.events[i];
                if (e == null) continue;

                switch (e.kind)
                {
                    case EventKindDeath:
                        // **死亡**（与 BattleViewRoot 在 kind==2 播的死亡特效同帧；命中音另走 OnSnapshot 的 hp 比对）。
                        Play(AudioPaths.Death);
                        break;

                    case EventKindTowerDestroyed:
                        // 塔毁（与 kind==3 的爆炸帧序列同源）。
                        Play(AudioPaths.TowerDown);
                        break;

                    case EventKindElixirFull:
                        Play(AudioPaths.ElixirFull);
                        break;

                    case EventKindPlayCard:
                        // 出牌：**己方**走 PlayCardRequest（`summon_own_07`，只由本机玩家拖放触发）；
                        // **敌方**没有那条请求事件 ⇒ 这里按 team 补 `enemy_summon_01`（A 有 ⇒ 做）。
                        // 两侧互斥 ⇒ 不会对同一次出牌播两声。
                        if (e.team != MyTeam)
                        {
                            // 这一支的判据就是"事件队伍 ≠ 我方队伍"，两个取值都必须可见（否则验不了红蓝两侧）。
                            Game.Logger?.Info(LogTag, $"敌方出牌：事件 team={e.team} 我方={MyTeam}（0=BLUE 1=RED）");
                            Play(AudioPaths.EnemySummon);
                        }
                        // 法术卡不生成实体（没有攻击档），它的「攻击音」只能挂在出牌事件上。
                        // 部队 / 建筑卡的攻击音走快照的 `anim` 上升沿（见 OnSnapshot），这里不播 ⇒ 不会双响。
                        PlaySpellCast(e.card_id, e.team);
                        break;

                    case EventKindTowerActivated:
                        // 塔激活（kind==5）：原版国王塔激活音（`king_activate_01`）。
                        Play(AudioPaths.TowerActivate);
                        break;

                    case EventKindTowerShoot:
                        // 塔开火（kind==6）：按开火塔的类型播公主塔 / 国王塔开火音。
                        PlayTowerShot(e);
                        break;

                    case EventKindSpawn:
                        // kind==1 生成：**登记为允许差异**，不播音 ——
                        // 它是同一次召唤在出牌（kind==0）之后紧随的第二次事件，再播一次会与出牌音**双响**；
                        // 且整包 2953 个 ogg 里没有独立的「生成/落地」音（最接近的 `Game/summon_own_07` 已用于出牌）。
                        break;

                    default:
                        // 非预期分支：协议新增了 kind 而本类没跟上。留痕一次（⛔ 不静默）。
                        if (!_unknownKindWarned)
                        {
                            _unknownKindWarned = true;
                            Game.Logger?.Warn(LogTag, $"收到未知的对局事件 kind={e.kind}（协议扩了 kind？本类未接）⇒ 不播音（只报一次）");
                        }
                        break;
                }
            }
        }

        private void OnBattleEnded(BattleEndNotify result)
        {
            if (result == null)
            {
                // 非预期分支：`Emit` 走 `DynamicInvoke`，参数类型不对会在这里显形。留痕。
                Game.Logger?.Warn(LogTag, "收到 null 的结算（发送方参数有误？）⇒ 胜负音效不播放");
                return;
            }

            if (result.draw)
            {
                // 平局：原版结算页是**三态** jingle（`scroll_win_02` / `scroll_lose_01` / `scroll_draw_01`，
                // 同一目录 `Music/Jingles/`）⇒ 平局有原版音可用。
                Play(AudioPaths.Draw);
                return;
            }

            Play(result.win ? AudioPaths.Win : AudioPaths.Lose);
        }

        /// <summary>
        /// 开打：清空 hp 比对表（⛔ 不清会把上一局的 hp 与新局比对出假命中），并把本事件带的
        /// `my_team` 记为兜底值（正常判定走 <see cref="MyTeam"/>，见 <see cref="Create"/>）。
        /// </summary>
        private void OnBattleStarted(BattleStartNotify start)
        {
            if (start == null) return;
            _myTeam = start.my_team;
            _prevHp.Clear();
            _curHp.Clear();
            _prevAnim.Clear();
            _curAnim.Clear();
            _prevTowerHp.Clear();
            _curTowerHp.Clear();
            _towerKind.Clear();
            _lastClipAt.Clear();
            CardAttackPlays = 0;
            CardHitPlays = 0;
            TowerAttackPlays = 0;
            TowerHitPlays = 0;
            CardAttackSkipped = 0;
            Game.Logger?.Info(LogTag, $"对局开始：my_team={_myTeam}（兜底值；出牌敌我判定读 BattleViewRoot.MyTeam）卡池={_cards.Count} 张");
        }

        /// <summary>
        /// 周期快照（10 Hz）：逐 id 比对 `hp` 与 `anim`，推出**命中音**与**攻击音**；塔另走
        /// `towers_a` / `towers_b` 的 hp 比对。
        ///
        /// <para>
        /// <b>命中音</b>：任何一次"掉血"都算一次命中。按**被打者**的卡 key 播该卡的命中音
        /// （<see cref="AudioPaths.Cards.TryGetHit"/>），该卡目录没有命中音时回落通用 <see cref="AudioPaths.Hit"/>；
        /// 塔被打时同样播通用 <see cref="AudioPaths.Hit"/>（原版整包 `Towers/` 下无独立受击音）。
        /// </para>
        /// <para>
        /// <b>攻击音</b>：实体的 `anim` 由非 2 变成 2 = 这一次挥砍 / 开火的上升沿 ⇒ 播该卡攻击音。
        /// 服务端每次挥砍都会先置 2、保住 4 tick 再收回（`server/game/core/combat.go:67`），
        /// 所以上升沿 ≠ 每个单位一次，而是**每一刀一次**。法术没有实体 ⇒ 攻击音挂在出牌事件上（见
        /// <see cref="PlaySpellCast"/>）。
        /// </para>
        /// <para>
        /// 防误报 / 防炸音：① 首次出现（新 id）不判命中、也不判攻击；② 只有"上一帧有、这一帧变小 / 变 2"才算；
        /// ③ 单帧最多 <see cref="MaxHitPerSnapshot"/> 声命中、<see cref="MaxAttackPerSnapshot"/> 声攻击；
        /// ④ 同一 clip 在 <see cref="MinSameClipIntervalSeconds"/> 内只起播一次。
        /// </para>
        /// </summary>
        private void OnSnapshot(BattleSnapshot s)
        {
            if (s == null) return;
            var list = s.entities;
            if (list == null) return;

            // 塔 id → 类型（0=公主塔 1=国王塔）：塔开火事件（kind=6）靠它选音。
            _towerKind.Clear();
            RememberTowerKinds(s.towers_a);
            RememberTowerKinds(s.towers_b);

            _curHp.Clear();
            _curAnim.Clear();
            var hits = 0;
            var attacks = 0;
            for (var i = 0; i < list.Length; i++)
            {
                var e = list[i];
                if (e == null) continue;
                _curHp[e.id] = e.hp;
                _curAnim[e.id] = e.anim;

                int prevHp;
                if (!_prevHp.TryGetValue(e.id, out prevHp)) continue; // 新单位：出场那帧不判命中 / 攻击

                if (e.hp < prevHp)
                {
                    if (hits < MaxHitPerSnapshot)
                    {
                        hits++;
                        PlayCardHit(e.card_id, e.id, prevHp, e.hp);
                    }
                }

                int prevAnim;
                var rising = _prevAnim.TryGetValue(e.id, out prevAnim) && prevAnim != AnimAttack && e.anim == AnimAttack;
                if (!rising) continue;
                if (e.deploy_ms > 0) continue;                        // 部署中不攻击（服务端 `acquirable()`）
                if (attacks >= MaxAttackPerSnapshot)
                {
                    CardAttackSkipped++;
                    continue;
                }
                attacks++;
                PlayCardAttack(e.card_id, e.id);
            }

            // 塔的命中：塔不在 `entities` 里（由 towers_a / towers_b 报告），同一条 hp 比对口径。
            _curTowerHp.Clear();
            var towerHits = 0;
            AccumulateTowerHits(s.towers_a, 0, ref towerHits);
            AccumulateTowerHits(s.towers_b, 1, ref towerHits);

            var swapHp = _prevHp; _prevHp = _curHp; _curHp = swapHp;
            var swapAnim = _prevAnim; _prevAnim = _curAnim; _curAnim = swapAnim;
            var swapTower = _prevTowerHp; _prevTowerHp = _curTowerHp; _curTowerHp = swapTower;
        }

        /// <summary>把一队的塔类型记进 <see cref="_towerKind"/>（幂等覆盖）。</summary>
        private void RememberTowerKinds(TowerState[] towers)
        {
            if (towers == null) return;
            for (var i = 0; i < towers.Length; i++)
            {
                var t = towers[i];
                if (t != null) _towerKind[t.id] = t.kind;
            }
        }

        /// <summary>累计一队的塔命中：hp 变小 ⇒ 播通用受击音（单帧上限同单位命中）。</summary>
        private void AccumulateTowerHits(TowerState[] towers, int team, ref int towerHits)
        {
            if (towers == null) return;
            for (var i = 0; i < towers.Length; i++)
            {
                var t = towers[i];
                if (t == null) continue;
                var key = team * 1000 + t.id;
                _curTowerHp[key] = t.hp;

                int prev;
                if (!_prevTowerHp.TryGetValue(key, out prev)) continue; // 首次出现不判
                if (t.hp >= prev) continue;                             // 没掉血
                if (towerHits >= MaxHitPerSnapshot) continue;
                towerHits++;

                TowerHitPlays++;
                Play(AudioPaths.Hit);
                Game.Logger?.Info(LogTag,
                    $"播放塔受击音：team={team} tower={t.id} kind={(t.kind == TowerKindKing ? "国王塔" : "公主塔")} " +
                    $"hp {prev}→{t.hp} clip={AudioPaths.Hit}（原版 Towers/ 无独立受击音，回落通用）");
            }
        }

        // ═════════════════════════ 逐卡（攻击 / 命中 / 法术 / 塔） ═════════════════════════

        /// <summary>卡池到达：建 `card_id → CardInfo` 索引（卡 key 与类型从服务端下发，客户端不落地配表）。</summary>
        private void OnCardPool(CardInfo[] cards)
        {
            if (cards == null) return;
            for (var i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                if (c != null) _cards[c.id] = c;
            }
            Game.Logger?.Info(LogTag, $"卡池已索引：{_cards.Count} 张（逐卡攻击 / 命中音按卡 key 查表）");
        }

        /// <summary>按 card_id 取 CardInfo；查不到时每个 id 只 Warn 一次（⛔ 不静默、⛔ 不刷屏）。</summary>
        private bool TryResolveCard(int cardId, out CardInfo info)
        {
            if (_cards.TryGetValue(cardId, out info) && info != null) return true;
            info = null;
            if (_cards.Count == 0)
            {
                if (!_poolMissingWarned)
                {
                    _poolMissingWarned = true;
                    Game.Logger?.Warn(LogTag, "卡池为空 ⇒ 逐卡音效查不到卡 key（本次及后续只报一次）");
                }
                return false;
            }
            if (_cardMissingWarned.Add(cardId))
                Game.Logger?.Warn(LogTag, $"card_id={cardId} 不在卡池里 ⇒ 该卡不播专属音效（只报一次 / id）");
            return false;
        }

        /// <summary>卡牌**攻击音**：单位 `anim` 上升沿（近战挥砍 / 远程开火）⇒ 该卡 key 的攻击音。</summary>
        private void PlayCardAttack(int cardId, int entityId)
        {
            CardInfo info;
            if (!TryResolveCard(cardId, out info)) { CardAttackSkipped++; return; }
            string clip;
            if (!AudioPaths.Cards.TryGetAttack(info.key, out clip))
            {
                CardAttackSkipped++;
                if (_missingAttackWarned.Add(info.key))
                    Game.Logger?.Warn(LogTag, $"卡 {info.key}（id={info.id}）没有配攻击音 ⇒ 这一刀不播（只报一次 / 卡）");
                return;
            }
            if (!PlayClip(clip)) { CardAttackSkipped++; return; }
            CardAttackPlays++;
            Game.Logger?.Info(LogTag,
                $"播放卡牌攻击音：card={info.key} ent={entityId} 音效={clip} 资源={AudioPaths.SfxPath(clip)} " +
                $"提交PlaySFX=true 本局累计={CardAttackPlays}（跳过 {CardAttackSkipped}）");
        }

        /// <summary>
        /// 卡牌**命中音**：按**被打者**的卡 key 播该卡命中音；目录里没有命中音（minions / cannon / zap）
        /// 或卡 key 未知时回落通用 <see cref="AudioPaths.Hit"/>（既有通用命中链路不删）。
        /// </summary>
        private void PlayCardHit(int cardId, int entityId, int prevHp, int hp)
        {
            CardInfo info;
            var known = TryResolveCard(cardId, out info);
            string clip = null;
            var dedicated = known && AudioPaths.Cards.TryGetHit(info.key, out clip);
            if (!dedicated) clip = AudioPaths.Hit;

            if (!PlayClip(clip)) return;
            CardHitPlays++;
            Game.Logger?.Info(LogTag,
                $"播放卡牌命中音：card={(known ? info.key : "?")} ent={entityId} hp {prevHp}→{hp} " +
                $"音效={clip} 资源={AudioPaths.SfxPath(clip)} 提交PlaySFX=true " +
                $"{(dedicated ? "专属" : "回落通用")} 本局累计={CardHitPlays}");
        }

        /// <summary>
        /// 法术卡的攻击 / 命中音。法术不生成实体（没有 `anim`），只能挂 `EvPlayCard`：
        /// 落点即时生效的法术（万箭 / 电击）施法音与落地音同帧；
        /// 有飞行段的法术（火球）落地音按 <see cref="SpellFxTable.Entry.FlightSeconds"/> 延迟。
        /// </summary>
        private void PlaySpellCast(int cardId, int team)
        {
            CardInfo info;
            if (!TryResolveCard(cardId, out info)) return;
            if (info.type != CardTypeSpell) return;

            string clip;
            if (AudioPaths.Cards.TryGetAttack(info.key, out clip) && PlayClip(clip))
            {
                CardAttackPlays++;
                Game.Logger?.Info(LogTag,
                    $"播放法术施法音：card={info.key} team={team} 音效={clip} 资源={AudioPaths.SfxPath(clip)} " +
                    $"提交PlaySFX=true 本局累计={CardAttackPlays}");
            }

            string hit;
            if (!AudioPaths.Cards.TryGetHit(info.key, out hit)) return;

            SpellFxTable.Entry fx;
            var flightSeconds = SpellFxTable.TryGet(info.key, out fx) && !string.IsNullOrEmpty(fx.FlightUse)
                ? Mathf.Max(0f, fx.FlightSeconds)
                : 0f;
            var key = info.key;
            if (flightSeconds <= 0f)
            {
                PlaySpellImpact(key, hit);
                return;
            }
            var timer = Game.Timer;
            if (timer == null)
            {
                // 非预期分支：计时服务缺失 ⇒ 落地音只能立即播（留痕，⛔ 不静默）。
                Game.Logger?.Warn(LogTag, $"Game.Timer 为空 ⇒ 法术 {key} 的落地音改为立即播（不等待飞行段）");
                PlaySpellImpact(key, hit);
                return;
            }
            timer.After(flightSeconds, () => PlaySpellImpact(key, hit));
        }

        /// <summary>法术落地音（飞行段到点 / 即时生效两种触发共用）。</summary>
        private void PlaySpellImpact(string cardKey, string clip)
        {
            if (!PlayClip(clip)) return;
            CardHitPlays++;
            Game.Logger?.Info(LogTag,
                $"播放法术命中音：card={cardKey} 音效={clip} 资源={AudioPaths.SfxPath(clip)} " +
                $"提交PlaySFX=true 本局累计={CardHitPlays}");
        }

        /// <summary>塔开火音：按 `EvTowerShoot` 的开火塔 id 查 `TowerState.kind` ⇒ 公主塔 / 国王塔开火音。</summary>
        private void PlayTowerShot(BattleEvent e)
        {
            int kind;
            if (!_towerKind.TryGetValue(e.entity_id, out kind))
            {
                // 事件可能早于第一帧快照到达；塔 id 只有快照里的 `TowerState.kind` 是权威来源。
                if (!_towerKindWarned)
                {
                    _towerKindWarned = true;
                    Game.Logger?.Warn(LogTag,
                        $"塔开火事件 tower={e.entity_id} 在快照里还没有类型 ⇒ 本次按公主塔音播（只报一次）");
                }
                kind = 0;
            }

            var clip = kind == TowerKindKing ? AudioPaths.TowerKingFire : AudioPaths.TowerPrincessFire;
            if (!PlayClip(clip)) return;
            TowerAttackPlays++;
            Game.Logger?.Info(LogTag,
                $"播放塔开火音：tower={e.entity_id} kind={(kind == TowerKindKing ? "国王塔" : "公主塔")} " +
                $"音效={clip} 资源={AudioPaths.SfxPath(clip)} 提交PlaySFX=true 本局累计={TowerAttackPlays}");
        }

        // ═════════════════════════ 播放（只走 clover-engine API） ═════════════════════════

        /// <summary>
        /// 播一个音效：先确认 clover-engine 的声音服务在、资源在，再交给 <c>Game.Sound.PlaySFX</c>。
        /// ⛔ 不自己建 <c>AudioSource</c> / 不建池（见类注释）。
        /// </summary>
        private void Play(string clipName)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                if (!_serviceWarned)
                {
                    _serviceWarned = true;
                    Game.Logger?.Warn(LogTag, "Game.Sound 为空（表现域未挂载）⇒ 音效无法播放（只报一次）");
                }
                return;
            }

            if (!ClipExists(clipName)) return;

            // 每次播放都留一条 Info：**这是"哪个挂点真的响了"的唯一运行时判据**
            //（数值类证据 = 运行时日志行，⛔ 不靠截图/听感；一行一个 clipName，可逐条对 AudioPaths）。
            // 顺带把分组音量打出来 ⇒ 证明读的是 SettingsManager 应用过的 `SoundGroup.SFX` 档，而不是写了死值。
            var vol = sound.GetVolume(SoundGroup.SFX);
            Game.Logger?.Info(LogTag, $"播放音效 {clipName}（SoundGroup.SFX 音量 {vol:F2}）");

            sound.PlaySFX(clipName);
        }

        /// <summary>
        /// 逐卡 / 塔音效的起播：资源在 + 同一 clip 的最小间隔未到 ⇒ 真正调 <c>PlaySFX</c>。
        /// 返回 false = 服务缺失 / 资源缺失 / 被限流（调用方据此计数，⛔ 不把"没播"当"播了"）。
        /// <para>
        /// 限流的对象是**同一个 clip**：并排三个骷髅兵同帧挥砍时只响一下（<see cref="MinSameClipIntervalSeconds"/>），
        /// 不同卡的音各响各的。
        /// </para>
        /// </summary>
        private bool PlayClip(string clipName)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                if (!_serviceWarned)
                {
                    _serviceWarned = true;
                    Game.Logger?.Warn(LogTag, "Game.Sound 为空（表现域未挂载）⇒ 音效无法播放（只报一次）");
                }
                return false;
            }
            if (!ClipExists(clipName)) return false;

            var now = Time.unscaledTime;
            float last;
            if (_lastClipAt.TryGetValue(clipName, out last) && now - last < MinSameClipIntervalSeconds) return false;
            _lastClipAt[clipName] = now;

            sound.PlaySFX(clipName);
            return true;
        }

        /// <summary>
        /// 资源是否落地。<c>Game.Res.Exists</c> 是 clover-engine 的**按路径缓存**探测
        ///（<c>Contracts.cs:1105</c>，同一路径只探一次）。缺失时留痕一次并返回 false。
        /// 探测通道不可用（<c>Game.Res</c> 为空）⇒ 不拦，交给 clover-engine 自己按缺失处理（它也有 WarnOnce）。
        /// </summary>
        private bool ClipExists(string clipName)
        {
            var res = Game.Res;
            if (res == null) return true;

            if (res.Exists(AudioPaths.SfxPath(clipName))) return true;

            if (!_missingWarned)
            {
                _missingWarned = true;
                Game.Logger?.Warn(LogTag,
                    $"音效资源缺失（Resources/{AudioPaths.SfxPath(clipName)}）⇒ 本次及后续缺失音效不播放（只报一次）");
            }
            return false;
        }
    }
}
