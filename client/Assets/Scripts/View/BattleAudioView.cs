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
    ///   <b>为什么用"请求"而不是 kind==0 的"出牌事件"</b>：请求只由**本机玩家**抬手触发（<c>HudPanel</c> 发出），
    ///   语义上必然是"己方出牌"，正好对上原版的己方召唤音（<c>summon_own_07</c>，见 <c>AudioPaths</c>）；
    ///   而 kind==0 的 <c>EvPlayCard</c> 是**双方共用**的（带 <c>team</c>），用它就得额外跟踪 <c>my_team</c> 才能区分敌我，
    ///   徒增状态且容易在重连/换边时漂移。音效是"手感反馈"，与请求同时发声才有原版的即时感（服务端裁决一般通过）。
    /// </item>
    /// <item>死亡 —— <c>Events.Battle.Events</c> 的 <c>kind == 2</c>（<c>EvDeath</c>）⇒ <see cref="AudioPaths.Death"/>。
    ///   （与 <see cref="BattleViewRoot"/> 在 kind==2 播的 <c>ResPaths.EffectHit</c> 受击闪光**同源同帧**。）</item>
    /// <item>命中（非致死）—— <c>Events.Battle.Snapshot</c> 里同 id 的 <c>hp</c> 下降 ⇒ <see cref="AudioPaths.Hit"/>。
    ///   （协议没有「命中」事件，见 <see cref="OnSnapshot"/>；单帧上限 4 声。）</item>
    /// <item>敌方出牌 —— 同事件 <c>kind == 0</c>（<c>EvPlayCard</c>）且 <c>team != my_team</c> ⇒ <see cref="AudioPaths.EnemySummon"/>。</item>
    /// <item>塔激活 —— 同事件 <c>kind == 5</c>（<c>EvTowerActivated</c>）⇒ <see cref="AudioPaths.TowerActivate"/>。</item>
    /// <item>平局 —— <c>Events.Battle.Ended</c> 的 <c>draw == true</c> ⇒ <see cref="AudioPaths.Draw"/>。</item>
    /// <item>UI 点击 / 卡牌拖起 / 放置被拒 / 倒计时最后 10 秒 —— 分别由 <c>UI/CrUiStyle</c>（按钮统一点）
    ///   与 <c>UI/Panels/HudPanel</c>（拖放 / 计时）直接播 <see cref="AudioPaths.UiClick"/> /
    ///   <see cref="AudioPaths.GrabCard"/> / <see cref="AudioPaths.BadDrop"/> / <see cref="AudioPaths.CountdownTick"/>。</item>
    /// <item>塔毁 —— 同事件 <c>kind == 3</c>（<c>EvTowerDestroyed</c>）⇒ <see cref="AudioPaths.TowerDown"/>
    ///   （与 kind==3 播的 <c>ResPaths.EffectBlast</c> 爆炸同源）。</item>
    /// <item>圣水满 —— 同事件 <c>kind == 4</c>（圣水满）⇒ <see cref="AudioPaths.ElixirFull"/>。</item>
    /// <item>胜负 —— <c>Events.Battle.Ended</c>（<c>BattleEndNotify</c>，<c>win</c> 字段）⇒ <see cref="AudioPaths.Win"/> / <see cref="AudioPaths.Lose"/>。</item>
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
    /// <see cref="Create"/> 的一次调用（挂在 <c>BattleContent</c> 下，与 <c>EffectsView</c> 并列）——
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

        /// <summary>我方队伍（0=BLUE 1=RED）—— `BattleStartNotify.my_team`。用于区分「己方/敌方出牌」。</summary>
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

        /// <summary>建出音效节点（幂等）。照 <see cref="EffectsView.Create"/> 的范式。</summary>
        public static BattleAudioView Create(Transform parent)
        {
            if (parent != null)
            {
                var existing = parent.GetComponentInChildren<BattleAudioView>(true);
                if (existing != null) return existing;
            }
            var go = new GameObject("BattleAudio");
            if (parent != null) go.transform.SetParent(parent, false);
            return go.AddComponent<BattleAudioView>();
        }

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

            bus.On<int, Vector2>(Events.Battle.PlayCardRequest, _onPlayCardRequest);
            bus.On<BattleEventNotify>(Events.Battle.Events, _onEvents);
            bus.On<BattleEndNotify>(Events.Battle.Ended, _onEnded);
            bus.On<BattleStartNotify>(Events.Battle.Started, _onStarted);
            bus.On<BattleSnapshot>(Events.Battle.Snapshot, _onSnapshot);

            _subscribed = true;
            // 日志里刻意不写事件名字面量（事件名只有 `Core/Events.cs` 一处定义；写进日志会污染对「裸事件名」的静态检查）。
            Game.Logger?.Info(LogTag, "音效已订阅：出牌请求 + 离散事件（命中 / 死亡 / 塔毁 / 塔激活 / 圣水满 / 敌方出牌）+ 结算（胜/负/平）+ 快照（非致死命中）");
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
                        // **死亡**（与 BattleViewRoot 在 kind==2 播的受击闪光同帧）。
                        // ⛔ 这里不再播 Hit：非致死命中走快照 hp 比对（见 OnSnapshot）。
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
                        if (e.team != _myTeam) Play(AudioPaths.EnemySummon);
                        break;

                    case EventKindTowerActivated:
                        // 塔激活（kind==5）：原版国王塔激活音（`king_activate_01`）。
                        Play(AudioPaths.TowerActivate);
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
        /// 开打：记下 `my_team`（区分己方 / 敌方出牌的唯一依据），并清空 hp 比对表
        ///（⛔ 不清会把上一局的 hp 与新局比对出假命中）。
        /// </summary>
        private void OnBattleStarted(BattleStartNotify start)
        {
            if (start == null) return;
            _myTeam = start.my_team;
            _prevHp.Clear();
            _curHp.Clear();
            Game.Logger?.Info(LogTag, $"对局开始：my_team={_myTeam}（音效按此区分己方出牌 summon_own_07 / 敌方 enemy_summon_01）");
        }

        /// <summary>
        /// 周期快照（10 Hz）：**逐 id 比对 `hp`** ⇒ 任何一次"掉血"都算一次命中，播 <see cref="AudioPaths.Hit"/>。
        ///
        /// <para>
        /// <b>为什么必须靠快照而不能靠事件</b>：协议（`Def/ProtoDef.cs:194`）只有 0..5 六种 `kind`，
        /// **没有独立的「命中」事件**，而 `kind==2` 只在死亡时发 ⇒ 命中必须由快照 `hp` 比对推出。
        /// </para>
        /// <para>
        /// 三条防误报：① 首次出现（新 id）不判命中；② 只有"上一帧有、这一帧 hp 变小"才算；
        /// ③ 单帧最多 <see cref="MaxHitPerSnapshot"/> 声（多个单位同时掉血时不炸音）。
        /// </para>
        /// </summary>
        private void OnSnapshot(BattleSnapshot s)
        {
            if (s == null) return;
            var list = s.entities;
            if (list == null) return;

            _curHp.Clear();
            var hits = 0;
            for (var i = 0; i < list.Length; i++)
            {
                var e = list[i];
                if (e == null) continue;
                _curHp[e.id] = e.hp;

                int prev;
                if (!_prevHp.TryGetValue(e.id, out prev)) continue; // 新单位：出场那帧不判命中
                if (e.hp >= prev) continue;                          // 没掉血

                if (hits < MaxHitPerSnapshot)
                {
                    hits++;
                    Play(AudioPaths.Hit);
                }
            }

            var swap = _prevHp;
            _prevHp = _curHp;
            _curHp = swap;
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
