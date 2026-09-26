using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CloverEngine;
using CR.Def;
using CR.Module.Deck;
using CR.UI.Panels;
using UnityEngine;

namespace CR.Module.Battle
{
    /// <summary>
    /// 对局的 C2S 门面 + 推送接收 + **10 Hz 快照的转发**（架构契约 D4/D9/D10）。
    ///
    /// <para>
    /// <b>本类不做插值</b>：插值的**唯一实现**在 `View/BattleViewRoot.TickRender`
    /// （它按服务端时间戳推进渲染时钟）。本类只把 `CR.Def.BattleSnapshot` 原件广播出去。
    /// </para>
    ///
    /// <para>
    /// <b>逐条对应已冻结协议</b>（`docs/步骤文档.md` §4.1，消息号一律走 <see cref="MsgDef"/>，
    /// ⛔ 不许出现裸消息号）：
    /// C2S = <c>BattlePlayCard</c> / <c>BattleSurrender</c> / <c>BattleSync</c>；
    /// 推送 = <c>PushBattleStart</c> / <c>PushBattleSnapshot</c> / <c>PushBattleEvent</c> / <c>PushBattleEnd</c>。
    /// </para>
    /// <para>
    /// <b>依赖方向</b>：`Module` 不引 `View`（契约 §1）。本类与 `View/**`、`HudPanel` 之间
    /// 只有 `Core/Events.cs` 的 `Events.Battle.*` 一条通道 —— `View` 与 `HudPanel` ⛔ 不许
    /// `using CR.Module`。因此本类**只发事件**：`Started`（进站点时补发）/ `Snapshot` / `Events` /
    /// `Ended` / `StartFailed`。
    /// </para>
    /// <para>
    /// <b>与 `AppFlow` 的分工（⛔ 不重复发消息）</b>：`MsgAiBattleStart` 的发送在
    /// `AppFlow.RequestStartAiBattle()`；`Events.Battle.Started` 也是 `AppFlow.RequestEnterBattle()`
    /// 在读条**之前**发的（`AppFlow.cs:185`）。本类**只消费推送**，并在**站点切到 `Battle` 之后**
    /// 把开打配置与最新快照**补发**一次 —— 那两条 `AppFlow` 的发布都早于新场景加载，
    /// 晚订阅的 `HudPanel` / `View` 全部收不到（补发模式与 `RoomManager.NotifyState` 同源）。
    /// </para>
    /// </summary>
    public sealed class BattleManager
    {
        private const string Tag = "Battle";

        /// <summary>塔的 kind 取值：0=公主塔（`ProtoDef.TowerState.kind`）。</summary>
        private const int TowerKindPrincess = 0;

        /// <summary>
        /// 快照序号步长上限。服务端 `snapshotEveryTicks = 2`、tick 50 ms ⇒ **每帧 +2**，
        /// 所以 20 ≈ 1 s 的空档 = 一帧都不该跳过去的距离。超过它只可能是
        /// 「收到了别的房间的帧」（跨房间快照污染）或异常跳变 ⇒ 丢弃并自愈。
        /// </summary>
        private const int MaxSeqJump = 20;

        /// <summary>塔的 kind 取值：1=国王塔（`ProtoDef.TowerState.kind`）。</summary>
        private const int TowerKindKing = 1;

        /// <summary>每队的公主塔数量（`GameConst` 的塔位常量只有两座公主塔 x：两个桥中心）。</summary>
        private const int PrincessTowersPerTeam = 2;

        /// <summary>离散事件 kind：3 = 塔毁（`ProtoDef.BattleEvent.kind` 的注释）。</summary>
        private const int EventKindTowerDestroyed = 3;

        /// <summary>`BattleSnapshot.phase` 取值：2 = 已结束。</summary>
        public const int PhaseEnded = 2;

        /// <summary>`BattleSnapshot.phase` 取值：1 = 加时。</summary>
        public const int PhaseOvertime = 1;

        /// <summary>当前实例（纯 C# 单例；跨场景存活 —— `Main`↔`Battle01` 互切会重建场景对象）。</summary>
        public static BattleManager Instance { get; private set; }

        // ───────────────────────── 开打配置（PushBattleStart 为唯一权威） ─────────────────────────

        private BattleStartNotify _start;

        // ───────────────────────── 最近一帧快照（D4：只做转发，插值在 View 侧） ─────────────────────────

        private BattleSnapshot _curr;        // 最近一帧快照（转发给 View / 给 HUD 读数值）
        private int _lastSeq = int.MinValue; // 序号闸门（BestEffort 快照可能乱序，见 ApplySnapshot）

        // ───────────────────────── 去重 / 告警只报一次 ─────────────────────────

        private readonly HashSet<int> _destroyedTowers = new HashSet<int>(); // 塔毁事件只播一次（按塔 id）
        private bool _seqWarned;
        private bool _roomWarned;     // 丢掉"别的房间"的快照只报一次（跨房间快照污染）
        private bool _seqJumpWarned;  // 序号跳变只报一次（纯客户端兜底闸门）
        private bool _resyncInFlight; // 序号跳变后的自愈同步在途（防抖，别连环发）
        // 塔位与左右归属由「插值 + 摆位」那套代码产生；本类只做快照转发，不在此维护"每队应有 2 座公主塔"之类的告警标志。
        private bool _poolReplayMissingWarned;

        // ───────────────────────── 订阅与在途标记 ─────────────────────────

        private IEventBus _bus;              // 已装订阅的那条事件总线（判据：对象标识，见 Install）
        private bool _pushHooked;            // 四条推送处理器是否已挂到 Router 上
        private bool _playInFlight;          // 出牌请求在途（防连点：同一瞬间的重复抬起）
        private bool _surrenderInFlight;     // 投降在途
        private bool _hudOpened;             // HUD 是否已打开（站点切到 Battle 时开）

        private Action<string> _onStationChanged;
        private Action<int, Vector2> _onPlayCardRequest;
        private Action _onSurrenderRequest;

        private MsgHandler _onBattleStartPush;
        private MsgHandler _onBattleSnapshotPush;
        private MsgHandler _onBattleEventPush;
        private MsgHandler _onBattleEndPush;

        /// <summary>最近一次开打配置（未开打时为 null）。</summary>
        public BattleStartNotify StartConfig => _start;

        /// <summary>最近一帧快照（未收到时为 null）。⛔ 它是**离散**的 10 Hz 数据：只给 HUD 读数值用
        ///（血量 / 圣水 / 冠数）；**坐标一律由 `View/BattleViewRoot` 插值**（插值唯一实现处，见该类注释三）。</summary>
        public BattleSnapshot CurrentSnapshot => _curr;

        /// <summary>本机所在队伍：0=BLUE 1=RED（`my_team`，由 `PushBattleStart` 给出）。</summary>
        public int MyTeam => _start != null ? _start.my_team : 0;

        /// <summary>当前房间号（开打配置里的 room_id）。</summary>
        public string CurrentRoomId => _start != null ? _start.room_id : null;

        /// <summary>是否已经收到过快照。</summary>
        public bool HasSnapshot => _curr != null;

        /// <summary>取（或首次创建）对局模块并装上订阅。由启动钩子调用，幂等。</summary>
        public static BattleManager EnsureCreated()
        {
            Instance ??= new BattleManager();
            Instance.Install();
            return Instance;
        }

        /// <summary>
        /// 装上事件订阅 + 推送处理器（幂等：同一条事件总线只装一次）。
        /// 判据是 `Game.Event` 的对象标识 —— 每次 `Game.Launch` 都新建 `EventBus`（`Game.cs:381`），
        /// 先前的订阅随它消失，必须重装；同一次 Launch 里重复调用直接返回。
        /// </summary>
        public void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：启动钩子早于 Launch 的核心初始化（不该发生）。留痕，别静默。
                Game.Logger?.Error(Tag, "Game.Event 为空（启动钩子时机异常），对局模块未订阅任何事件");
                return;
            }

            if (ReferenceEquals(bus, _bus)) return; // 同一条总线：已装过

            if (_bus != null)
            {
                Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），对局模块重装订阅与推送处理器");
            }

            _bus = bus;
            _pushHooked = false; // 推送处理器挂在 Router 上，Router 随 CloverNet.Init 重建
            _hudOpened = false;

            _onStationChanged = OnStationChanged;
            _onPlayCardRequest = OnPlayCardRequest;
            _onSurrenderRequest = OnSurrenderRequest;

            _onBattleStartPush = OnBattleStartPush;
            _onBattleSnapshotPush = OnBattleSnapshotPush;
            _onBattleEventPush = OnBattleEventPush;
            _onBattleEndPush = OnBattleEndPush;

            bus.On<string>(Events.Flow.StationChanged, _onStationChanged);
            bus.On<int, Vector2>(Events.Battle.PlayCardRequest, _onPlayCardRequest);
            bus.On(Events.Battle.SurrenderRequest, _onSurrenderRequest);

            EnsurePushHandlers();

            Game.Logger?.Info(Tag, "对局模块已装载（Flow.StationChanged + Battle.PlayCardRequest/SurrenderRequest + 4 条推送）");
        }

        /// <summary>
        /// 挂四条推送处理器。**必须晚于 `CloverNet.Init`**：`Game.OnMsg` 在 router 未挂时会打 Error
        /// 并拒绝注册（`Game.cs:551-558`，刻意的"不静默"设计），而本类的启动钩子跑在 `Game.Launch`
        /// **内部**、`Bootstrap` 的 `CloverNet.Init` 之前 ⇒ 钩子里注册必然失败。
        /// 所以做成"能装就装，装不上等下一次时机"（覆盖站点切换与每一次上行操作，都晚于网络就绪）。
        /// </summary>
        private void EnsurePushHandlers()
        {
            if (_pushHooked) return;
            if (Game.Net == null)
            {
                Game.Logger?.Info(Tag, "网络未就绪（CloverNet.Init 还没跑），对局推送订阅推迟到站点切换时注册");
                return;
            }

            // ⚠️ `PushBattleStart` 上已经有 `AppFlow`（和 `Module/Room`）的处理器 —— 引擎的 `Router`
            //    支持同一 msgID 多处理器（`Runtime/Network/Router.cs:14-28`，按注册顺序依次调用），
            //    所以这里追加是安全的。⛔ 本类**绝不**调 `Game.OffMsg`：它按 msgID 整表清，
            //    会把 `AppFlow` 的注册一起摘掉（"人机对战进不了图"）。
            Game.OnMsg(MsgDef.PushBattleStart, _onBattleStartPush);
            Game.OnMsg(MsgDef.PushBattleSnapshot, _onBattleSnapshotPush);
            Game.OnMsg(MsgDef.PushBattleEvent, _onBattleEventPush);
            Game.OnMsg(MsgDef.PushBattleEnd, _onBattleEndPush);
            _pushHooked = true;
            Game.Logger?.Info(Tag, "对局推送订阅已注册（PushBattleStart / PushBattleSnapshot / PushBattleEvent / PushBattleEnd）");
        }
        // ═════════════════════════ 事件订阅（面板请求 → C2S） ═════════════════════════

        private void OnPlayCardRequest(int cardId, Vector2 worldPos)
        {
            _ = PlayCardAsync(cardId, worldPos);
        }

        private void OnSurrenderRequest()
        {
            _ = SurrenderAsync();
        }

        // ═════════════════════════ 上行 C2S 门面 ═════════════════════════
        //
        // 约定（与 `RoomManager` 一致）：方法内部把失败收干净（打日志 + Emit(Battle.StartFailed)），
        // 调用方拿 null / false 即"失败且原因已显示给玩家"，不需要再包 try。

        /// <summary>
        /// 出牌（`MsgBattlePlayCard`）。<paramref name="worldPos"/> 用**世界坐标（格）**，
        /// 换算成服务端要的 1/1000 格**只**经 <see cref="GameConst"/>（⛔ 无魔法数字）。
        /// 合法性最终由服务端裁决；被拒时把服务端的 <c>err</c> 原样 `Emit` 给面板显示。
        /// </summary>
        public async Task<BattlePlayCardReply> PlayCardAsync(int cardId, Vector2 worldPos)
        {
            var roomId = RequireRoomId("出牌");
            if (roomId == null) return null;

            if (_playInFlight)
            {
                // 非预期分支：拖放抬起被重复触发（同一帧两次抬起 / 两个输入后端同时生效）。
                Game.Logger?.Warn(Tag, $"上一次出牌请求还在途，忽略重复的出牌 card={cardId}");
                return null;
            }

            // 世界坐标（格）→ 绝对格 → 1/1000 格。竞技场中心在世界原点、y 不翻转（见 GameConst 的坐标约定），
            // 因此这里是 `TileToWorld` 的逆运算，用的是同一批常量。
            var xMilli = GameConst.TileToMilli(worldPos.x + GameConst.ArenaTilesW * 0.5f);
            var yMilli = GameConst.TileToMilli(worldPos.y + GameConst.ArenaTilesH * 0.5f);

            _playInFlight = true;
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                Game.Logger?.Info(Tag,
                    $"请求出牌 card={cardId} world=({worldPos.x:0.00},{worldPos.y:0.00}) → " +
                    $"milli=({xMilli},{yMilli}) team={MyTeam}");

                var reply = await Game.Net.Call<BattlePlayCardReply>(MsgDef.BattlePlayCard,
                    new BattlePlayCardReq { room_id = roomId, card_id = cardId, x_milli = xMilli, y_milli = yMilli });

                if (reply == null || !reply.ok)
                {
                    // ⛔ 不许只打日志：原因必须回到面板（玩家点了一下却什么都没发生是最差的反馈）。
                    Fail("出牌被服务端拒绝：" + ErrText(reply != null ? reply.err : null));
                    return null;
                }

                Game.Logger?.Info(Tag, $"出牌已被接受 card={cardId}（表现等快照/事件）");
                return reply;
            }
            catch (Exception e)
            {
                Fail("出牌失败：" + e.Message);
                return null;
            }
            finally
            {
                _playInFlight = false;
            }
        }

        /// <summary>投降（`MsgBattleSurrender`）。结果由服务端推 `PushBattleEnd` 收尾。</summary>
        public async Task<BattleSurrenderReply> SurrenderAsync()
        {
            var roomId = RequireRoomId("投降");
            if (roomId == null) return null;

            if (_surrenderInFlight)
            {
                Game.Logger?.Warn(Tag, "投降请求已在途，忽略重复请求");
                return null;
            }

            _surrenderInFlight = true;
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                Game.Logger?.Info(Tag, "请求投降");
                var reply = await Game.Net.Call<BattleSurrenderReply>(MsgDef.BattleSurrender,
                    new BattleSurrenderReq { room_id = roomId });

                if (reply == null || !reply.ok)
                {
                    // 回包只有 ok 没有 err ⇒ 必须把"可能的原因"写给玩家看。
                    Fail("投降被服务端拒绝（对局可能已结束，或你已不在这一局）");
                    return null;
                }

                Game.Logger?.Info(Tag, "投降已被接受，等 PushBattleEnd");
                return reply;
            }
            catch (Exception e)
            {
                Fail("投降失败：" + e.Message);
                return null;
            }
            finally
            {
                _surrenderInFlight = false;
            }
        }

        /// <summary>
        /// 主动拉一次全量状态（`MsgBattleSync`）。进场时先拉一次，再等 10 Hz 周期快照 ——
        /// 这样第一帧就有东西可画（否则要等服务端下一个快照周期，最长 100 ms 空场）。
        /// </summary>
        public async Task<BattleSnapshot> SyncAsync()
        {
            var roomId = _start != null ? _start.room_id : null;
            if (string.IsNullOrEmpty(roomId))
            {
                Game.Logger?.Warn(Tag, "还没有开打配置（不知道 room_id），无法 BattleSync");
                return null;
            }

            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                var reply = await Game.Net.Call<BattleSyncReply>(MsgDef.BattleSync, new BattleSyncReq { room_id = roomId });
                if (reply == null || reply.snapshot == null)
                {
                    Fail("拉取对局全量状态失败（空回包）");
                    return null;
                }

                Game.Logger?.Info(Tag, $"BattleSync 已回全量 seq={reply.snapshot.seq}");
                ApplySnapshot(reply.snapshot, true);   // 主动拉的全量 = 权威，跳过闸门（也是跳变后的自愈路径）
                EmitSnapshot();
                return reply.snapshot;
            }
            catch (Exception e)
            {
                Fail("拉取对局全量状态失败：" + e.Message);
                return null;
            }
        }

        // ═════════════════════════ 推送 ═════════════════════════

        /// <summary>
        /// 开打（`PushBattleStart`，Reliable）。只**记录**配置：进图与站点切换由 `AppFlow` 负责
        /// （`AppFlow.RequestEnterBattle`，按 roomId 幂等）。本类 ⛔ 不重复发 `MsgAiBattleStart`。
        /// </summary>
        private void OnBattleStartPush(NetCtx ctx)
        {
            var start = ctx.Bind<BattleStartNotify>();
            if (start == null)
            {
                Game.Logger?.Error(Tag, "PushBattleStart 反序列化失败（Bind 返回 null），本局配置未登记");
                return;
            }

            ApplyStart(start);

            // 已经在 `Battle` 站点时（同站点「再来一局」/ 重连后服务端重推开打）：站点没变 ⇒
            // `AppFlow` 不会广播 `StationChanged`，本类的补发也不会触发 ⇒ 这里主动补一次，
            // 让 HUD/View 拿到新配置，并补发一次「进场」回执（服务端的模拟推进闸门等这一条）。
            if (Game.Fsm != null && Game.Fsm.Current == Stations.Battle)
            {
                Game.Logger?.Info(Tag, $"已在 Battle 站点，补发开打配置（重连/重推）room={start.room_id}");
                EmitStarted();
                _ = SyncAsync();
            }
        }

        /// <summary>
        /// 周期快照（`PushBattleSnapshot`，10 Hz，BestEffort）。服务端无 UDP 端点时引擎自动降级为
        /// 可靠 TCP（不会丢，但可能**乱序**）⇒ 用 <c>seq</c> 做闸门，丢弃倒退的快照。
        /// </summary>
        private void OnBattleSnapshotPush(NetCtx ctx)
        {
            var snap = ctx.Bind<BattleSnapshot>();
            if (snap == null)
            {
                // 非预期分支：`Bind` 失败返回 null 而不抛，极易被静默吞掉。
                Game.Logger?.Error(Tag, "PushBattleSnapshot 反序列化失败（Bind 返回 null），本帧快照被丢弃");
                return;
            }

            ApplySnapshot(snap);
            EmitSnapshot();
        }

        /// <summary>离散事件（`PushBattleEvent`，Reliable）→ 广播给 `View` 播表现。</summary>
        private void OnBattleEventPush(NetCtx ctx)
        {
            var notify = ctx.Bind<BattleEventNotify>();
            if (notify == null)
            {
                Game.Logger?.Error(Tag, "PushBattleEvent 反序列化失败（Bind 返回 null），事件被丢弃");
                return;
            }

            var filtered = FilterBattleEvents(notify);
            if (filtered == null) return; // 全部是重复事件（见 FilterBattleEvents）
            Game.Event?.Emit(Events.Battle.Events, filtered);
        }

        /// <summary>结算（`PushBattleEnd`，Reliable）→ 广播；面板由 `ResultPanel` 负责。</summary>
        private void OnBattleEndPush(NetCtx ctx)
        {
            var result = ctx.Bind<BattleEndNotify>();
            if (result == null)
            {
                Game.Logger?.Error(Tag, "PushBattleEnd 反序列化失败（Bind 返回 null），结算未显示给玩家");
                return;
            }

            Game.Logger?.Info(Tag,
                $"对局结束 win={result.win} draw={result.draw} crowns={result.crowns_a}:{result.crowns_b} reason={result.reason}");
            Game.Event?.Emit(Events.Battle.Ended, result);
        }

        /// <summary>
        /// **去重**：离散事件与快照都会到（快照 10 Hz、事件可靠），同一件事可能被两条路各报一次。
        /// 本类只对**幂等性无关痛痒、重复播会很显眼**的一种做去重：塔毁（kind=3）按
        /// <c>entity_id</c> 只放行一次。其余事件（出牌/生成/死亡/圣水满/塔激活）都是"一瞬间的表现"，
        /// 服务端本身就会各自只报一次，这里不拦。
        /// </summary>
        private BattleEventNotify FilterBattleEvents(BattleEventNotify notify)
        {
            var events = notify.events;
            if (events == null || events.Length == 0)
            {
                // 非预期分支：空的 events 数组（服务端不该发）。留痕一次即可，不必刷屏。
                Game.Logger?.Info(Tag, "PushBattleEvent 的 events 为空，忽略");
                return null;
            }

            var kept = new List<BattleEvent>(events.Length);
            var dropped = 0;
            for (var i = 0; i < events.Length; i++)
            {
                var e = events[i];
                if (e == null) continue;

                if (e.kind == EventKindTowerDestroyed && !_destroyedTowers.Add(e.entity_id))
                {
                    dropped++;
                    continue;
                }
                kept.Add(e);
            }

            if (dropped == 0) return notify;   // 一个都没重复 ⇒ 原样转发（不白建数组）
            Game.Logger?.Info(Tag, $"过滤掉 {dropped} 条重复的塔毁事件（同一座塔只播一次）");
            if (kept.Count == 0) return null;
            return new BattleEventNotify { events = kept.ToArray() };
        }

        // ═════════════════════════ 站点联动 ═════════════════════════

        /// <summary>
        /// 站点切换。`Battle` ⇒ 开 HUD + 补发开打配置与最新快照 + 先 `BattleSync` 拉一次全量。
        /// 其余站点 ⇒ 收掉 HUD 的本地态（HUD 的开关由 `AppFlow.CloseAll` 负责，这里只清缓存）。
        /// </summary>
        private void OnStationChanged(string station)
        {
            EnsurePushHandlers();

            if (station == Stations.Battle)
            {
                EnterBattleStation();
                return;
            }

            if (station == Stations.MainMenu && _hudOpened)
            {
                ClearLocalBattle("已回主菜单，本地对局态收尾");
            }
        }

        private void EnterBattleStation()
        {
            if (_start == null)
            {
                // 非预期分支：有人直接触发 ToBattle 但服务端从没推过开打配置。留痕（否则现象是
                // "进了对局场景但 HUD 全空、也没有任何报错"）。
                Game.Logger?.Warn(Tag, "站点进入 Battle 但还没有开打配置（PushBattleStart 没到？），HUD 将显示等待态");
            }

            if (Game.UI == null)
            {
                Game.Logger?.Error(Tag, "Game.UI 为空（表现域未挂载），无法打开对局 HUD");
                return;
            }

            // ⚠️ 顺序要紧：先开面板（它的 OnOpen 里才订阅事件），**再**补发开打配置与快照 ——
            //    颠过来的话 HUD 会错过这两条（`Emit` 是同步分发，晚订阅者收不到历史事件）。
            Game.UI.Open<HudPanel>();
            _hudOpened = true;

            EmitStarted();
            ReplayCardPoolForHud();
            if (_curr != null) EmitSnapshot();

            // 进场先拉一次全量（否则最长要等 100 ms 才有第一帧可画；也让"重连回来"立刻有画面）。
            _ = SyncAsync();
        }

        /// <summary>
        /// 把**已缓存的卡池**补发给晚订阅的 HUD。
        ///
        /// <para>
        /// <b>为什么需要这一步</b>：`HudPanel` 要显示「圣水数 + 中文名 + 类型色」（卡面图不可得，
        /// 与 `DeckEditPanel` 同一降级方案），而卡名/费用只有 `CardInfo` 有 —— 它在
        /// `Events.Deck.PoolLoaded` 里下发，且只由 `Module/Deck` 在**进主菜单时**发一次。
        /// HUD 是在 `Battle` 站点才开的，那时早已错过 ⇒ 不补发的话手牌只能显示 "卡 id=N"。
        /// </para>
        /// <para>
        /// ⚠️ 这是跨模块**只读**取一份缓存后按原事件协议补发（`Module → Module` 的边与
        /// `RoomManager` 引 `AppFlow` 同类），**不改**任何 `Deck` 的文件、也不替它做任何决定。
        /// 若 `Deck` 模块不存在 / 卡池还没拉到，就只打一条 Info（HUD 退化为 "卡 id=N"，不崩）。
        /// </para>
        /// </summary>
        private void ReplayCardPoolForHud()
        {
            var pool = DeckManager.Instance != null ? DeckManager.Instance.CardPool : null;
            if (pool == null || pool.Length == 0)
            {
                if (!_poolReplayMissingWarned)
                {
                    _poolReplayMissingWarned = true;
                    Game.Logger?.Info(Tag,
                        "卡池尚未缓存（DeckManager.CardPool 为空），HUD 手牌暂时只能显示卡 id；" +
                        "正常流程下进主菜单时已预取，这里为空说明卡组模块未装载或卡池拉取失败");
                }
                return;
            }

            Game.Event?.Emit(Events.Deck.PoolLoaded, pool);
        }

        // ═════════════════════════ 本地态维护 ═════════════════════════

        /// <summary>登记开打配置（清掉上一局的快照 —— 上一局的实体 id 与本局无关，沿用会画出幽灵单位）。</summary>
        private void ApplyStart(BattleStartNotify start)
        {
            _start = start;
            _curr = null;
            _lastSeq = int.MinValue;
            _destroyedTowers.Clear();
            _seqWarned = false;
            _roomWarned = false;     // 新一局：跨房间丢帧重新留痕
            _seqJumpWarned = false;  // 新一局：序号跳变重新留痕

            Game.Logger?.Info(Tag,
                $"开打配置已登记 room={start.room_id} my_team={start.my_team} seed={start.seed} " +
                $"deck_a={start.deck_a?.Length ?? 0} deck_b={start.deck_b?.Length ?? 0} hand_a={start.hand_a?.Length ?? 0}");
        }

        /// <summary>
        /// 登记最新一帧快照（D4）。三道闸门依次判：**房间归属 → 序号前进 → 序号跳变**；
        /// ⛔ **不记录到达时刻** —— 插值唯一实现在 `View/BattleViewRoot`，它按快照自带的
        /// `server_ms` 推进渲染时钟（本类只负责"最新一帧是哪个"）。
        ///
        /// <para>
        /// <b>为什么需要闸门 1（room_id）</b>：旧房间的对局 tick 若没停下来，它推来的快照
        /// seq 比新村大；只按 seq 判就会把**新村的所有帧**当"倒退"丢掉 ⇒ HUD 永远停在旧房
        /// 数值（实测 `10000`）。所以第一件事是问"这一帧是不是我这一局的"。
        /// `trusted=true` 只给 `BattleSync` 的回包（那是"本房当前状态"的权威答案，直接采纳）。
        /// </para>
        /// </summary>
        /// <param name="snap">收到的一帧。</param>
        /// <param name="trusted">是否来自 `BattleSync`（主动拉取的全量，跳过三道闸门）。</param>
        private void ApplySnapshot(BattleSnapshot snap, bool trusted = false)
        {
            // ── 闸门 1：房间归属 ──────────────────────────────────────────────
            // room_id 为空 = 旧服务端（不发本字段）⇒ 不拦，退回闸门 3。
            if (!trusted && !string.IsNullOrEmpty(snap.room_id))
            {
                var mine = CurrentRoomId;
                if (!string.IsNullOrEmpty(mine) && snap.room_id != mine)
                {
                    if (!_roomWarned)
                    {
                        _roomWarned = true;
                        // 非预期分支：收到了别的房间的快照。留痕一次。
                        Game.Logger?.Warn(Tag,
                            $"丢弃其它房间的快照 room={snap.room_id}（本局 room={mine}）seq={snap.seq}；" +
                            "持续出现说明旧房间的对局 tick 没有停下来");
                    }
                    return;
                }
            }

            // ── 闸门 2：序号必须前进（乱序 / 重复）────────────────────────────
            if (!trusted && _curr != null && snap.seq <= _lastSeq)
            {
                if (!_seqWarned)
                {
                    _seqWarned = true;
                    // 非预期分支：快照乱序/重复（BestEffort 降级到 TCP 时可能发生）。留痕一次。
                    Game.Logger?.Warn(Tag,
                        $"收到倒退的快照 seq={snap.seq}（当前 {_lastSeq}），已丢弃；" +
                        "若持续出现说明服务端快照顺序或 UDP 乱序策略有问题");
                }
                return;
            }

            // ── 闸门 3：序号跳变（纯客户端兜底；服务端漏发 room_id 时也能挡住污染）──
            if (!trusted && _curr != null && snap.seq > _lastSeq + MaxSeqJump)
            {
                if (!_seqJumpWarned)
                {
                    _seqJumpWarned = true;
                    // 非预期分支：一帧不许跳过 MaxSeqJump。留痕一次。
                    Game.Logger?.Warn(Tag,
                        $"快照序号跳变 seq={snap.seq}（上一帧 {_lastSeq}，步长恒 2），已丢弃并主动拉一次全量；" +
                        "通常意味着收到了别的房间的帧（跨房间快照污染）");
                }
                ResyncAfterSeqJump();
                return;
            }

            _curr = snap;
            _lastSeq = snap.seq;
        }

        /// <summary>
        /// 序号跳变后的自愈：拉一次全量并**采纳**（`trusted`）。没有这一步的话
        /// `_lastSeq` 会停在小值上 ⇒ 之后每一帧都被闸门 3 拒掉（画面永久冻住）。
        /// </summary>
        private void ResyncAfterSeqJump()
        {
            if (_resyncInFlight) return;   // 防抖：自愈在途就别再发
            _resyncInFlight = true;
            _ = ResyncAfterSeqJumpAsync();
        }

        private async Task ResyncAfterSeqJumpAsync()
        {
            try
            {
                await SyncAsync();
            }
            finally
            {
                _resyncInFlight = false;
            }
        }

        private void ClearLocalBattle(string why)
        {
            Game.Logger?.Info(Tag, $"清掉本地对局态（{why}）：room={CurrentRoomId ?? "无"}");
            _start = null;
            _curr = null;
            _destroyedTowers.Clear();
            _hudOpened = false;
        }

        // ═════════════════════════ 广播 ═════════════════════════

        /// <summary>
        /// 补发开打配置。⚠️ `AppFlow.RequestEnterBattle` 已经发过一条同样的（在旧场景里、读条之前），
        /// 本条的用途是让**新场景里**才创建的 `View`/`HudPanel` 拿得到（它们收不到历史事件）。
        /// 订阅方必须把 `Started` 处理成**幂等**的（重建竞技场 = 可重复执行）。
        /// </summary>
        private void EmitStarted()
        {
            if (_start == null) return;
            Game.Event?.Emit(Events.Battle.Started, _start);
        }

        private void EmitSnapshot()
        {
            if (_curr == null) return;
            Game.Event?.Emit(Events.Battle.Snapshot, _curr);
        }

        /// <summary>失败的唯一出口：打日志 + 广播（面板据此把原因显示给玩家，⛔ 不许只打日志）。</summary>
        private void Fail(string reason)
        {
            Game.Logger?.Warn(Tag, "对局操作失败：" + reason);
            // ⚠️ `Events.cs` 的 Battle 段里唯一一条 (string reason) 事件就是 `StartFailed`
            //    （语义"开一局失败（卡组非法 / 建房失败等）"），因此复用它承载"对局操作失败原因"；
            //    与 `DeckManager` 复用 `Deck.Changed` 承载"保存请求"同一处理方式。
            Game.Event?.Emit(Events.Battle.StartFailed, reason);
        }

        private string RequireRoomId(string what)
        {
            if (!string.IsNullOrEmpty(CurrentRoomId)) return CurrentRoomId;
            Fail($"还没有对局（不知道 room_id），无法{what}");
            return null;
        }

        /// <summary>服务端 err 为空时的兜底文案（回包体里没有 err 字段的情形）。</summary>
        private static string ErrText(string err)
        {
            return string.IsNullOrEmpty(err) ? "服务端未给出原因（空回包 / 未确认）" : err;
        }
    }
}
