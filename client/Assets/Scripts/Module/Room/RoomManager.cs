using System;
using System.Threading.Tasks;
using CloverEngine;
using CR.Def;
using CR.Module.Flow;
using CR.UI.Panels;

namespace CR.Module.Room
{
    /// <summary>
    /// 房间的 C2S 门面 + 推送接收（列表 / 创建 / 加入 / 离开 / 准备 / 开打 / AI 补位）。
    ///
    /// <para>
    /// <b>逐条对应已冻结协议</b>（`docs/步骤文档.md` §4.1，消息号常量一律走 <see cref="MsgDef"/>，
    /// 业务脚本里⛔不许出现裸消息号）：
    /// C2S = <c>RoomCreate</c> / <c>RoomList</c> / <c>RoomJoin</c> / <c>RoomLeave</c> /
    /// <c>RoomReady</c> / <c>RoomStart</c> / <c>RoomSetAi</c>；
    /// 推送 = <c>PushRoomList</c> / <c>PushRoomState</c> / <c>PushBattleStart</c>。
    /// </para>
    /// <para>
    /// <b>与面板的关系（架构契约 §1 依赖方向）</b>：三个房间面板⛔不许 <c>using CR.Module</c>，
    /// 只能 <c>Emit(Events.Room.*)</c>；本类订阅这些事件、发 C2S、再把结果 <c>Emit</c> 回面板。
    /// 因此面板完全不认识本类，换皮不动链路。
    /// </para>
    /// <para>
    /// <b>谁创建本类</b>：<see cref="RoomModuleHost"/>（`[RuntimeInitializeOnLoadMethod]` +
    /// <c>Game.RegisterLaunchHook</c>）。⛔ 不改 `App/Bootstrap.cs`（agent-05 的冻结产出）是有意的：
    /// 引擎的启动钩子本来就是「上层模块把自己挂接到门面」的正规入口
    /// （`clover-client-unity-engine/Runtime/Core/Game.cs:661-671`，注释写明「可安全配合
    /// RuntimeInitializeOnLoadMethod 使用」，并在 `Launch` 内 `RunLaunchHooks()`，`Game.cs:401-405`）。
    /// 项目级 skill 的 `registry.md` 记的是「由 Flow 持有」—— 那需要改 `AppFlow`，本片无此权限，
    /// 差异已写进回报（登记在 skill 里的挂载方式与实际实现不一致，请主 agent 裁决其一）。
    /// </para>
    /// <para>
    /// <b>⛔ 本类不实现「人机对战」的发送</b>：`AppFlow.RequestStartAiBattle()` 已经实现了
    /// <c>MsgAiBattleStart</c> 的发送；本类只消费推送（见 <see cref="OnBattleStartPush"/>）。
    /// </para>
    /// </summary>
    public sealed class RoomManager
    {
        private const string Tag = "Room";

        /// <summary>
        /// 角色 ID 的前缀，用来把「服务端给的房主 ID」映射回「本机玩家」。
        ///
        /// <para>
        /// <b>为什么需要它</b>：服务端房间态只给 <c>host</c>（角色 ID，形如 <c>p_&lt;账号&gt;</c>）与
        /// 成员 <c>player_id</c>，而**协议里没有任何"我自己是谁"的字段**
        /// （`RoomJoinReply` 只有 ok/room_id/err/ai_fill，`RoomCreateReply` 只有 ok/room_id/err）
        /// ⇒ 客户端只能按服务端的派生规则反推自己。规则出处：
        /// `server/game/logic/player.go:20`（`playerIDPrefix = "p_"`）与 `:45`（`pid := playerIDPrefix + acc`）。
        /// </para>
        /// <para>
        /// ⚠️ 这是**服务端实现细节的副本**（协议未冻结它）—— 一旦服务端改前缀，本类会退化为
        /// "认不出自己"：那时会打 Warn 并把房主按钮放行，由服务端裁决（见 <see cref="ResolveSelfPlayerId"/>）。
        /// 建议主 agent 在协议里补一个"回包带自己的 player_id / is_host"字段，彻底去掉这份副本。
        /// </para>
        /// </summary>
        private const string PlayerIdPrefix = "p_";

        /// <summary>当前实例（纯 C# 单例；跨场景存活，理由同 `AppFlow`：`Main`↔`Battle01` 互切会重建场景对象）。</summary>
        public static RoomManager Instance { get; private set; }

        // ───────────────────────── 当前房间态（服务端推送为唯一权威） ─────────────────────────

        private string _roomId;                 // 本地认定的当前房间号（发出创建/加入成功后才置）
        private RoomStateNotify _state;         // 最近一次 PushRoomState（缓存，供面板打开时补发）
        private bool _createdByMe;              // 这个房是不是我创建的（认不出自己时的兜底判据）
        private string _selfPlayerId;           // 本机角色 ID（每次进房时重算，见 ResolveSelfPlayerId）
        private bool _selfIdWarned;             // 「认不出自己」只告警一次（避免每次进房刷屏）
        private bool _selfMissingWarned;        // 「成员列表里没有我」只告警一次

        // ───────────────────────── 大厅列表缓存 ─────────────────────────

        private RoomInfo[] _rooms = Array.Empty<RoomInfo>();

        // ───────────────────────── 订阅与在途标记 ─────────────────────────

        private IEventBus _bus;                 // 已经订阅过的那条事件总线（判据：同一条只装一次）
        private bool _pushHooked;               // 三条推送处理器是否已挂到 Router 上
        private bool _busy;                     // 建房/加入/离开在途（防连点）

        private Action _onOpenListRequest;
        private Action _onRefreshRequest;
        private Action<string> _onCreateRequest;
        private Action<string> _onJoinRequest;
        private Action _onLeaveRequest;
        private Action<bool> _onReadyRequest;
        private Action _onStartRequest;
        private Action<bool> _onSetAiRequest;
        private Action<string> _onStationChanged;

        private MsgHandler _onRoomListPush;
        private MsgHandler _onRoomStatePush;
        private MsgHandler _onBattleStartPush;

        /// <summary>当前房间号（未进房时为空串）。</summary>
        public string CurrentRoomId => _roomId;

        /// <summary>最近一次服务端房间态（未进房 / 还没收到推送时为 null）。</summary>
        public RoomStateNotify CurrentState => _state;

        /// <summary>最近一次大厅房间列表（未拉到时为空数组）。</summary>
        public RoomInfo[] CurrentRooms => _rooms;

        /// <summary>
        /// 本机是不是当前房间的房主（服务端给的 <c>host</c> 与本地角色 ID 相等）。
        /// 认不出自己时退回「这个房是我创建的」这一启发式 —— 房主移交（原房主离房后由剩下的真人接任）
        /// 在这条退化路径上认不出来，所以那样判只为"非房主按钮置灰"这类展示用途，开打的最终裁决永远在服务端。
        /// </summary>
        public bool IsLocalHost
        {
            get
            {
                if (_state == null) return _createdByMe;
                if (string.IsNullOrEmpty(_selfPlayerId)) return _createdByMe;
                return !string.IsNullOrEmpty(_state.host) && _state.host == _selfPlayerId;
            }
        }

        /// <summary>
        /// 取（或首次创建）房间模块并装上订阅。由启动钩子调用，幂等。
        /// </summary>
        public static RoomManager EnsureCreated()
        {
            Instance ??= new RoomManager();
            Instance.Install();
            return Instance;
        }

        /// <summary>
        /// 装上事件订阅 + 推送处理器（幂等：同一条事件总线只装一次）。
        /// 「换总线才重装」的判据是 <c>Game.Event</c> 的对象标识 —— 每次 <c>Game.Launch</c> 都会新建
        /// `EventBus`（`Game.cs:381`），上一轮的订阅随它一起消失，所以必须重装；而同一轮里重复调用则直接返回。
        /// </summary>
        public void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：启动钩子早于 Launch 的核心初始化（不该发生）。留痕，别静默。
                Game.Logger?.Error(Tag, "Game.Event 为空（启动钩子时机异常），房间模块未订阅任何事件");
                return;
            }

            if (ReferenceEquals(bus, _bus)) return; // 同一条总线：已装过

            if (_bus != null)
            {
                Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），房间模块重新装载订阅与推送处理器");
            }

            _bus = bus;
            _pushHooked = false; // 推送处理器挂在 Router 上，Router 也随 CloverNet.Init 重建

            _onOpenListRequest = OnOpenListRequest;
            _onRefreshRequest = OnRefreshRequest;
            _onCreateRequest = OnCreateRequest;
            _onJoinRequest = OnJoinRequest;
            _onLeaveRequest = OnLeaveRequest;
            _onReadyRequest = OnReadyRequest;
            _onStartRequest = OnStartRequest;
            _onSetAiRequest = OnSetAiRequest;
            _onStationChanged = OnStationChanged;

            _onRoomListPush = OnRoomListPush;
            _onRoomStatePush = OnRoomStatePush;
            _onBattleStartPush = OnBattleStartPush;

            bus.On(Events.Room.OpenListRequest, _onOpenListRequest);
            bus.On(Events.Room.RefreshRequest, _onRefreshRequest);
            bus.On<string>(Events.Room.CreateRequest, _onCreateRequest);
            bus.On<string>(Events.Room.JoinRequest, _onJoinRequest);
            bus.On(Events.Room.LeaveRequest, _onLeaveRequest);
            bus.On<bool>(Events.Room.ReadyRequest, _onReadyRequest);
            bus.On(Events.Room.StartRequest, _onStartRequest);
            bus.On<bool>(Events.Room.SetAiRequest, _onSetAiRequest);
            // 站点变化：本类据此开关房间面板（`Room` 站点）/ 收尾本地房间态（`Battle`）。
            bus.On<string>(Events.Flow.StationChanged, _onStationChanged);

            EnsurePushHandlers();

            Game.Logger?.Info(Tag, "房间模块已装载（Room.* 8 条请求 + Flow.StationChanged + 3 条推送）");
        }

        /// <summary>
        /// 挂三条推送处理器。**必须晚于 `CloverNet.Init`**：`Game.OnMsg` 在 router 还没挂时
        /// 会打 Error 并**拒绝注册**（`Game.cs:551-558`，这是刻意的"不静默"设计），而本类的启动钩子
        /// 跑在 `Game.Launch` **内部**、`Bootstrap` 的 `CloverNet.Init` 之前（`App/Bootstrap.cs:76-100` 的
        /// 第 ③ 步）⇒ 钩子里注册必然失败。所以这里做成"能装就装，装不上等下一次时机"，
        /// 调用点覆盖了站点切换与每一次房间操作（都晚于网络就绪）。
        /// </summary>
        private void EnsurePushHandlers()
        {
            if (_pushHooked) return;
            if (Game.Net == null)
            {
                Game.Logger?.Info(Tag, "网络未就绪（CloverNet.Init 还没跑），房间推送订阅推迟到首次房间操作时注册");
                return;
            }

            Game.OnMsg(MsgDef.PushRoomList, _onRoomListPush);
            Game.OnMsg(MsgDef.PushRoomState, _onRoomStatePush);
            Game.OnMsg(MsgDef.PushBattleStart, _onBattleStartPush);
            _pushHooked = true;
            Game.Logger?.Info(Tag, "房间推送订阅已注册（PushRoomList / PushRoomState / PushBattleStart）");
        }

        // ═════════════════════════ 面板请求 → C2S ═════════════════════════

        private void OnOpenListRequest()
        {
            EnsurePushHandlers();
            if (Game.UI == null)
            {
                Game.Logger?.Error(Tag, "Game.UI 为空（表现域未挂载），无法打开房间列表");
                return;
            }

            Game.UI.Open<RoomListPanel>(null);
            // 先把缓存喂给面板，再拉一次真列表：服务端往返期间面板不是空的（拉到后本类会再发一次通知）。
            NotifyRooms();
            _ = FetchRoomsAsync();
        }

        private void OnRefreshRequest()
        {
            EnsurePushHandlers();
            _ = FetchRoomsAsync();
        }

        private void OnCreateRequest(string name)
        {
            EnsurePushHandlers();
            _ = CreateRoomAsync(name);
        }

        private void OnJoinRequest(string roomId)
        {
            EnsurePushHandlers();
            _ = JoinRoomAsync(roomId);
        }

        private void OnLeaveRequest()
        {
            _ = LeaveRoomAsync();
        }

        private void OnReadyRequest(bool ready)
        {
            _ = SetReadyAsync(ready);
        }

        private void OnStartRequest()
        {
            _ = StartAsync();
        }

        private void OnSetAiRequest(bool aiFill)
        {
            _ = SetAiFillAsync(aiFill);
        }

        // ═════════════════════════ C2S 门面（公开，供其它模块/调试驱动复用） ═════════════════════════
        //
        // 约定：全部方法**把异常与失败都收在自己内部**（catch → 打日志 → Emit(Room.Failed)），
        // 因此调用方拿 null 即"失败且原因已显示给玩家"，不需要（也不该）再包一层 try。
        // 唯一例外是 OutOfMemory 之类的致命异常 —— 那不该被业务吞掉。

        /// <summary>拉大厅房间列表（`MsgRoomList`）。同时把本连接登记为"大厅观察者"（服务端随后会推变化）。</summary>
        public async Task<RoomListReply> FetchRoomsAsync()
        {
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                var reply = await Game.Net.Call<RoomListReply>(MsgDef.RoomList, new RoomListReq());
                if (reply == null)
                {
                    Fail("拉取房间列表失败（空回包）");
                    return null;
                }

                _rooms = reply.rooms ?? Array.Empty<RoomInfo>();
                Game.Logger?.Info(Tag, $"房间列表已更新 {_rooms.Length} 间");
                NotifyRooms();
                return reply;
            }
            catch (Exception e)
            {
                Fail("拉取房间列表失败：" + e.Message);
                return null;
            }
        }

        /// <summary>创建房间（`MsgRoomCreate`）。成功后本类把 <c>createdByMe</c> 置真并请求切到 `Room` 站点。</summary>
        public async Task<RoomCreateReply> CreateRoomAsync(string name)
        {
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "已有一次房间操作在途，忽略重复的建房请求（按钮连点？）");
                return null;
            }

            _busy = true;
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                // 房名允许为空：服务端会用「<昵称>的房间」兜底（`server/game/logic/room.go:895-902`），
                // 客户端**不重复**这套兜底逻辑（那会与服务端漂移，且界面无法知道昵称）。
                var reply = await Game.Net.Call<RoomCreateReply>(MsgDef.RoomCreate,
                    new RoomCreateReq { name = name ?? string.Empty });
                if (reply == null || !reply.ok || string.IsNullOrEmpty(reply.room_id))
                {
                    Fail("创建房间失败：" + ErrText(reply != null ? reply.err : null));
                    return null;
                }

                _createdByMe = true;
                EnterLocalRoom(reply.room_id, null);
                return reply;
            }
            catch (Exception e)
            {
                Fail("创建房间失败：" + e.Message);
                return null;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>加入房间（`MsgRoomJoin`）。成功后本类把 <c>createdByMe</c> 置假并请求切到 `Room` 站点。</summary>
        public async Task<RoomJoinReply> JoinRoomAsync(string roomId)
        {
            if (string.IsNullOrEmpty(roomId))
            {
                Fail("房号为空的加入请求，已忽略");
                return null;
            }
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "已有一次房间操作在途，忽略重复的加入请求");
                return null;
            }

            _busy = true;
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                var reply = await Game.Net.Call<RoomJoinReply>(MsgDef.RoomJoin, new RoomJoinReq { room_id = roomId });
                if (reply == null || !reply.ok)
                {
                    Fail("加入房间失败：" + ErrText(reply != null ? reply.err : null));
                    return null;
                }

                _createdByMe = false;
                EnterLocalRoom(string.IsNullOrEmpty(reply.room_id) ? roomId : reply.room_id, null);
                return reply;
            }
            catch (Exception e)
            {
                Fail("加入房间失败：" + e.Message);
                return null;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// 离开当前房间（`MsgRoomLeave`）。房间号取本地当前值 —— 面板只知道"我要离开"，
        /// 不需要（也不该）知道房间号（面板⛔不许引 CR.Module，拿不到这个字段）。
        /// 成功后清掉本地房间态并请求切回 `MainMenu` 站点。
        /// </summary>
        public async Task<RoomLeaveReply> LeaveRoomAsync()
        {
            var roomId = _roomId;
            if (string.IsNullOrEmpty(roomId))
            {
                Game.Logger?.Warn(Tag, "不在任何房间里，忽略离开请求");
                return null;
            }
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "已有一次房间操作在途，忽略重复的离开请求");
                return null;
            }

            _busy = true;
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return null;
                }

                var reply = await Game.Net.Call<RoomLeaveReply>(MsgDef.RoomLeave, new RoomLeaveReq { room_id = roomId });
                if (reply == null || !reply.ok)
                {
                    // 服务端在对局进行中会拒绝退房（`room.go:1014-1019`：先投降再退房），
                    // 回包只有 ok 没有 err ⇒ 这里必须把"可能的原因"写给玩家看（否则只看到一个失败的按钮）。
                    Fail("离开房间被服务端拒绝（对局进行中请先投降，或房间已被回收）");
                    return null;
                }

                ClearLocalRoom("已离开房间 " + roomId);
                Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.MainMenu);
                return reply;
            }
            catch (Exception e)
            {
                Fail("离开房间失败：" + e.Message);
                return null;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// 准备 / 取消准备（`MsgRoomReady`）。本地**不改**准备态：服务端随后推 `PushRoomState`
        /// （它同时会刷新卡组快照，见 `room.go:1052-1057`），权威态一律以推送为准。
        /// </summary>
        public async Task<RoomReadyReply> SetReadyAsync(bool ready)
        {
            var roomId = RequireRoomId("准备");
            if (roomId == null) return null;

            try
            {
                var reply = await Game.Net.Call<RoomReadyReply>(MsgDef.RoomReady,
                    new RoomReadyReq { room_id = roomId, ready = ready });
                if (reply == null || !reply.ok)
                {
                    Fail("准备状态更新失败（服务端未确认，可能对局已开始）");
                    return null;
                }
                Game.Logger?.Info(Tag, $"已请求把准备态改为 {ready}，等 PushRoomState 确认");
                return reply;
            }
            catch (Exception e)
            {
                Fail("准备状态更新失败：" + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 房主开打（`MsgRoomStart`）。成功后**不走本类的任何收尾**：服务端会推 `PushBattleStart`，
        /// 由 <see cref="OnBattleStartPush"/> 交给 `AppFlow.RequestEnterBattle` 读条进图。
        /// </summary>
        public async Task<RoomStartReply> StartAsync()
        {
            var roomId = RequireRoomId("开打");
            if (roomId == null) return null;

            try
            {
                var reply = await Game.Net.Call<RoomStartReply>(MsgDef.RoomStart, new RoomStartReq { room_id = roomId });
                if (reply == null || !reply.ok)
                {
                    // 服务端把拒因写得很具体（"只有房主可以开打" / "人数不足…" / "<昵称> 还没有准备" /
                    // "座位 N 还没有 8 张卡组"）—— 原样显示给玩家，比自己编一套提示准确得多。
                    Fail("开打失败：" + ErrText(reply != null ? reply.err : null));
                    return null;
                }
                Game.Logger?.Info(Tag, "开打请求已被接受，等服务端推 PushBattleStart");
                return reply;
            }
            catch (Exception e)
            {
                Fail("开打失败：" + e.Message);
                return null;
            }
        }

        /// <summary>房主开关 AI 补位（`MsgRoomSetAi`）。仅房主可设（服务端会拒非房主，`room.go:423-425`）。</summary>
        public async Task<RoomSetAiReply> SetAiFillAsync(bool aiFill)
        {
            var roomId = RequireRoomId("设置 AI 补位");
            if (roomId == null) return null;

            try
            {
                var reply = await Game.Net.Call<RoomSetAiReply>(MsgDef.RoomSetAi,
                    new RoomSetAiReq { room_id = roomId, ai_fill = aiFill });
                if (reply == null || !reply.ok)
                {
                    Fail("设置 AI 补位失败（只有房主可以设置，且对局未开始）");
                    return null;
                }
                Game.Logger?.Info(Tag, $"AI 补位已请求改为 {aiFill}，等 PushRoomState 确认");
                return reply;
            }
            catch (Exception e)
            {
                Fail("设置 AI 补位失败：" + e.Message);
                return null;
            }
        }

        // ═════════════════════════ 推送 ═════════════════════════

        /// <summary>大厅列表变化（`PushRoomList`，Reliable）。</summary>
        private void OnRoomListPush(NetCtx ctx)
        {
            var notify = ctx.Bind<RoomListNotify>();
            if (notify == null)
            {
                // 非预期分支：协议体反序列化失败（`Bind` 失败返回 null 而不抛，容易静默吞掉）。
                Game.Logger?.Error(Tag, "PushRoomList 反序列化失败（Bind 返回 null），房间列表未更新");
                return;
            }

            _rooms = notify.rooms ?? Array.Empty<RoomInfo>();
            Game.Logger?.Info(Tag, $"收到房间列表推送：{_rooms.Length} 间");
            NotifyRooms();
        }

        /// <summary>房间成员 / 准备 / AI 状态（`PushRoomState`，Reliable）。</summary>
        private void OnRoomStatePush(NetCtx ctx)
        {
            var state = ctx.Bind<RoomStateNotify>();
            if (state == null)
            {
                Game.Logger?.Error(Tag, "PushRoomState 反序列化失败（Bind 返回 null），房间状态未更新");
                return;
            }

            if (string.IsNullOrEmpty(_roomId) || state.room_id != _roomId)
            {
                // 预期内的情形：结算后服务端还会给房内真人推一次收尾态，而本客户端在进对局时已清掉本地房间
                //（见 OnStationChanged 的 Battle 分支）。留一条 Info 便于排查"面板没刷新"这类问题。
                Game.Logger?.Info(Tag,
                    $"忽略非当前房间的状态推送 room={state.room_id}（本地当前：{(_roomId ?? "无")}）");
                return;
            }

            ApplyState(state);
        }

        /// <summary>
        /// 开打（`PushBattleStart`，Reliable）。
        ///
        /// <para>
        /// <b>为什么本类也订阅（`AppFlow` 已订过同一条）</b>：任务书 §5.2 要求房间模块订阅它；
        /// 而 `RequestEnterBattle` 是**按 roomId 幂等**的（`AppFlow.cs:160-208`：同一房间重复请求只
        /// 打一条 Info 就返回），并且 `Game.OnMsg` 支持同一消息号多处理器（`Router.cs:14-28`，
        /// 按注册顺序依次调用）⇒ 两条订阅同时存在是安全的，不会重复加载场景。
        /// 本类**不**调 `OffMsg(PushBattleStart)`（那会连带摘掉 `AppFlow` 的注册，引擎的 `OffMsg` 是整表清）。
        /// </para>
        /// </summary>
        private void OnBattleStartPush(NetCtx ctx)
        {
            var start = ctx.Bind<BattleStartNotify>();
            if (start == null)
            {
                Game.Logger?.Error(Tag, "PushBattleStart 反序列化失败（Bind 返回 null），未进入对局");
                return;
            }

            var flow = AppFlow.Instance;
            if (flow == null)
            {
                // 非预期分支：AppFlow 由 Bootstrap 创建，正常早于任何推送。留痕（此时 AppFlow 自己那条订阅
                // 也不存在，所以没人会兜底 —— 必须说清"这局没进去"）。
                Game.Logger?.Error(Tag, $"收到开打推送 room={start.room_id} 但 AppFlow 尚未创建，无法进对局");
                return;
            }

            Game.Logger?.Info(Tag, $"收到开打推送 room={start.room_id}，转交 AppFlow.RequestEnterBattle（按 roomId 幂等）");
            flow.RequestEnterBattle(start.room_id, start);
        }

        // ═════════════════════════ 站点联动 ═════════════════════════

        private void OnStationChanged(string station)
        {
            EnsurePushHandlers();

            if (station == Stations.Room)
            {
                OpenRoomPanel();
                return;
            }

            if (station == Stations.Battle)
            {
                // 进对局后房间的生命周期归对局模块：本地房间态在这里收掉，避免"对局结束回主菜单时
                // 还对着一间可能已被服务端回收的房"。服务端那边的座位由它自己维护（掉线判负等）。
                if (_roomId != null) ClearLocalRoom("已进入对局，本地房间态收尾");
                return;
            }

            if (station == Stations.MainMenu && _roomId != null)
            {
                // 兜底：回到大厅却还留着房间态（异常路径，例如被踢后重登）。静默退房，
                // 否则服务端会留一个"幽灵占座"，别人看到的人数/座位就是错的。
                Game.Logger?.Warn(Tag, $"回到主菜单时仍持有房间 {_roomId}，兜底退房");
                LeaveQuietly();
            }
        }

        /// <summary>打开房间面板并把缓存态补发给它（推送可能早于面板打开）。</summary>
        private void OpenRoomPanel()
        {
            if (Game.UI == null)
            {
                Game.Logger?.Error(Tag, "Game.UI 为空（表现域未挂载），无法打开房间面板");
                return;
            }
            if (string.IsNullOrEmpty(_roomId))
            {
                // 非预期分支：有人直接 Trigger("ToRoom") 切了站点，但本客户端并没有房间。
                // 说清楚（否则现象是"切到房间站点后一片空白"，毫无线索）。
                Game.Logger?.Warn(Tag, "站点进入 Room 但本地没有房间号（未经创建/加入），不打开房间面板");
                return;
            }

            _selfPlayerId = ResolveSelfPlayerId();

            // 面板不许引 CR.Module ⇒ 把"我是谁"经打开参数递进去（它据此判断房主按钮是否置灰）。
            Game.UI.Open<RoomPanel>(new RoomPanel.PanelArgs
            {
                RoomId = _roomId,
                SelfPlayerId = _selfPlayerId,
            });

            if (_state != null) NotifyState();
            else Game.Logger?.Info(Tag, "打开房间面板时还没有服务端房间态，等 PushRoomState");
        }

        // ═════════════════════════ 本地态维护 ═════════════════════════

        /// <summary>建房/加入成功后的本地收尾：记房间号、算自己的角色 ID、广播 `Room.Joined`、请求切站点。</summary>
        private void EnterLocalRoom(string roomId, string roomName)
        {
            _roomId = roomId;
            _state = null;                 // 旧房间的状态不能沿用（成员/准备态完全不同）
            _selfPlayerId = ResolveSelfPlayerId();
            _selfMissingWarned = false;

            if (!string.IsNullOrEmpty(roomName)) Game.Logger?.Info(Tag, $"房间名：{roomName}");
            Game.Logger?.Info(Tag, $"进入房间 {roomId}（createdByMe={_createdByMe}，self={_selfPlayerId ?? "未知"}）");

            Game.Event?.Emit(Events.Room.Joined, roomId);

            // 站点切换的唯一入口是 AppFlow（它还会 CloseAll + 广播 StationChanged），
            // 本类只发"请切到 Room"这条请求 —— 与本项目所有面板的做法一致。
            Game.Event?.Emit(Events.Flow.StationEnterRequest, Stations.Room);
        }

        /// <summary>清掉本地房间态（不碰服务端）。</summary>
        private void ClearLocalRoom(string why)
        {
            Game.Logger?.Info(Tag, $"清掉本地房间态（{why}）：room={_roomId ?? "无"}");
            _roomId = null;
            _state = null;
            _createdByMe = false;
            _selfMissingWarned = false;
        }

        /// <summary>兜底退房（只打日志，绝不 Emit Failed —— 它不是玩家点的操作，弹错误提示只会让人困惑）。</summary>
        private async void LeaveQuietly()
        {
            var roomId = _roomId;
            if (string.IsNullOrEmpty(roomId)) return;
            ClearLocalRoom("兜底退房");

            try
            {
                if (Game.Net == null) return;
                await Game.Net.Call<RoomLeaveReply>(MsgDef.RoomLeave, new RoomLeaveReq { room_id = roomId });
                Game.Logger?.Info(Tag, $"兜底退房成功 room={roomId}");
            }
            catch (Exception e)
            {
                Game.Logger?.Warn(Tag, $"兜底退房失败 room={roomId}：{e.Message}（服务端会在断线清理时回收座位）");
            }
        }

        /// <summary>把推送来的房间态存进缓存并转发给面板。</summary>
        private void ApplyState(RoomStateNotify state)
        {
            _state = state;

            // 协议字段 self_player_id 是**权威**（服务端按推送目标逐个填）：一旦拿到就不必再用账号
            // 反推角色 ID —— 那份副本一旦服务端前缀漂移就会"认不出自己"，表现为"房主按钮永远是灰的"。
            if (!string.IsNullOrEmpty(state.self_player_id))
            {
                _selfPlayerId = state.self_player_id;
                _selfMissingWarned = false;
            }

            var count = state.members != null ? state.members.Length : 0;
            Game.Logger?.Info(Tag,
                $"房间态更新 room={state.room_id} host={state.host} ai_fill={state.ai_fill} " +
                $"started={state.started} members={count}");

            // 「认不出自己」只在真的发生时报一次：这是服务端角色 ID 前缀与本类副本不一致的信号，
            // 必须留痕（否则表现为"我不是房主，永远是灰的"，查起来极贵）。
            if (!string.IsNullOrEmpty(_selfPlayerId) && !_selfMissingWarned)
            {
                var found = false;
                for (var i = 0; i < count; i++)
                {
                    var m = state.members[i];
                    if (m != null && m.player_id == _selfPlayerId) { found = true; break; }
                }
                if (!found)
                {
                    _selfMissingWarned = true;
                    Game.Logger?.Warn(Tag,
                        $"成员列表里找不到自己（self={_selfPlayerId}）：服务端的角色 ID 前缀可能与客户端的副本 " +
                        $"`{PlayerIdPrefix}` 不一致；房主按钮将按「房间是否由我创建」判断");
                }
            }

            Game.Event?.Emit(Events.Room.StateChanged, state);
        }

        private void NotifyState()
        {
            if (_state == null) return;
            Game.Event?.Emit(Events.Room.StateChanged, _state);
        }

        private void NotifyRooms()
        {
            Game.Event?.Emit(Events.Room.ListChanged, _rooms);
        }

        private string RequireRoomId(string what)
        {
            if (!string.IsNullOrEmpty(_roomId)) return _roomId;
            Fail($"你不在任何房间里，无法{what}");
            return null;
        }

        /// <summary>
        /// 反推本机角色 ID（见 <see cref="PlayerIdPrefix"/> 的说明）。
        /// 账号取自 `Game.Net.Session.Account` —— 它就是登录时 `SetupSession` 交上去的那个账号
        /// （引擎源码已核：`Runtime/Core/Contracts.cs:32` 的 `SessionInfo.Account`、
        /// `Runtime/Network/NetworkManager.cs:716-719` 的 `SetupSession`；接口成员见 `Contracts.cs:273`）。
        /// </summary>
        private string ResolveSelfPlayerId()
        {
            // ① 首选协议字段：服务端按推送目标逐个填的 self_player_id（权威，无需复制服务端规则）。
            var fromServer = _state?.self_player_id;
            if (!string.IsNullOrEmpty(fromServer)) return fromServer;

            // ② 退化：服务端尚未带上该字段（或房间态还没到）时，按 playerIDPrefix 反推。
            var account = Game.Net?.Session?.Account;
            if (string.IsNullOrEmpty(account))
            {
                if (!_selfIdWarned)
                {
                    _selfIdWarned = true;
                    Game.Logger?.Warn(Tag,
                        "既没有 self_player_id 也拿不到会话账号（Game.Net.Session.Account 为空）" +
                        "⇒ 认不出自己是哪个成员；" +
                        "房主按钮将按「房间是否由我创建」判断，开打与否仍由服务端裁决");
                }
                return null;
            }
            return PlayerIdPrefix + account;
        }

        /// <summary>失败的唯一出口：打日志 + 广播（面板据此把原因显示给玩家，⛔ 不许只打日志）。</summary>
        private void Fail(string reason)
        {
            Game.Logger?.Warn(Tag, "房间操作失败：" + reason);
            Game.Event?.Emit(Events.Room.Failed, reason);
        }

        /// <summary>服务端 err 为空时的兜底文案（回包体里没有 err 字段的情形）。</summary>
        private static string ErrText(string err)
        {
            return string.IsNullOrEmpty(err) ? "服务端未给出原因（空回包 / 未确认）" : err;
        }
    }
}
