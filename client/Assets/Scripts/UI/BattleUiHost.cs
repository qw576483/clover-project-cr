using System;
using CloverEngine;
using CR.Def;
using CR.UI.Panels;
using UnityEngine;   // RuntimeInitializeOnLoadMethod / RuntimeInitializeLoadType（启动钩子的登记时机）

namespace CR.UI
{
    /// <summary>
    /// 对局期 UI 的装载点：把「暂停菜单」与「结算页」挂到引擎的启动钩子上（照
    /// `Module/Room/RoomModuleHost` 的范式写）。
    ///
    /// <para>
    /// <b>为什么需要它</b>：`PausePanel` / `ResultPanel` 都⛔不许 `using CR.Module`（契约 §1），
    /// 而"什么时候开这两个面板"需要一个知道对局状态的角色：
    /// ① 进 `Pause` 站点 ⇒ 开 `PausePanel`（`AppFlow.EnterPause` 只打日志，
    ///    `BattleManager.OnStationChanged` 也只认 `Battle` / `MainMenu`）；
    /// ② `Events.Battle.Ended` ⇒ 开 `ResultPanel`（`BattleManager.OnBattleEndPush` 只**广播**结算体，
    ///    开关面板由本类负责）。
    /// </para>
    /// <para>
    /// <b>为什么用启动钩子</b>（与 `RoomModuleHost` 同一理由）：`App/Bootstrap.cs` 不承载对局 UI 的开关；
    /// 引擎提供 `Game.RegisterLaunchHook`（`Game.cs:660-671`，按 key 覆盖 ⇒ 可安全重复登记），
    /// 在 `Game.Launch` 建好核心子系统（含 `Game.Event`）后回调，因此本类一定早于任何面板打开就绪。
    /// ⚠️ 但 `Game.UI` 由 `CloverPresentation` 的钩子挂载，两个钩子同相位、**顺序不保证**
    /// （`RunLaunchHooks` 遍历字典）⇒ 所以这里只在钩子里挂**事件订阅**，
    /// `Game.UI` 一律在"真的要开面板"那一刻才用，并在为空时留 Error（⛔ 不静默）。
    /// </para>
    /// <para>
    /// <b>本类<u>不</u>做的事（避免重复处理者）</b>：
    /// ⛔ 不订阅 `Events.Battle.SurrenderRequest`（处理者已是 `BattleManager`，再挂一个会发两条投降请求）；
    /// ⛔ 不订阅 `Events.Battle.AiBattleRequest`（处理者已是 `AppFlow.StartAiBattleAsync`，
    /// 再挂一个会开两局）；⛔ 不发任何 C2S（那是 Module 的地盘）。
    /// 本类只做"请求/推送 → 开关面板"这一件事。
    /// </para>
    /// </summary>
    public static class BattleUiHost
    {
        private const string Tag = "BattleUiHost";

        /// <summary>启动钩子的注册键（同 key 重复注册会覆盖，因此可安全重复登记）。</summary>
        private const string LaunchHookKey = "CR.UI.BattleUi";

        /// <summary>本机队伍（0=BLUE 1=RED）；-1 = 还没收到 `Events.Battle.Started`。
        /// ⛔ 不许默认成 0（那等于"假设自己永远是 A 方"，红方玩家会看到反的冠数）。</summary>
        private const int TeamUnknown = -1;

        /// <summary>已装订阅的那条事件总线（判据：对象标识 —— 每次 `Game.Launch` 都新建 `EventBus`）。</summary>
        private static IEventBus _bus;

        // ── 最近一次开打的信息（`Events.Battle.Started`）──
        private static int _myTeam = TeamUnknown;
        private static string _roomId = string.Empty;

        /// <summary>
        /// 本局是不是人机对局（决定「再来一局」是"再开一局人机"还是"回主菜单请重新开房"）。
        /// ⚠️ 协议里**没有**这个字段（`BattleStartNotify` / `BattleEndNotify` 都没有"是否人机"），
        /// 所以只能由"最近一次开打是从哪条路来的"推：见 <see cref="OnAiBattleRequest"/> /
        /// <see cref="OnRoomJoined"/>。这是本项目客户端侧的**推论**，不是协议事实 ——
        /// 默认 false（房间），因为默认值走"回主菜单"这条**安全**分支（⛔ 绝不误开一局人机）。
        /// </summary>
        private static bool _aiBattle;

        private static Action<BattleStartNotify> _onStarted;
        private static Action<BattleEndNotify> _onEnded;
        private static Action<string> _onStationChanged;
        private static Action<string> _onStationEnterRequest;
        private static Action _onAiBattleRequest;
        private static Action<string> _onRoomJoined;
        private static Action _onRoomStartRequest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, Install);
        }

        /// <summary>
        /// 装上事件订阅（幂等：同一条事件总线只装一次）。
        /// 判据是 `Game.Event` 的对象标识 —— 上一次 Launch 的订阅随旧 `EventBus` 消失，必须重装；
        /// 同一次 Launch 里重复调用直接返回。
        /// </summary>
        public static void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：启动钩子早于 Launch 的核心初始化。留痕，否则表现为"暂停/结算永远不出现"。
                Game.Logger?.Error(Tag, "Game.Event 为空（启动钩子时机异常），暂停 / 结算面板不会有任何响应");
                return;
            }

            if (ReferenceEquals(bus, _bus)) return; // 同一条总线：已装过

            if (_bus != null)
            {
                Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），重装订阅并复位对局信息");
                _myTeam = TeamUnknown;
                _roomId = string.Empty;
                _aiBattle = false;
            }

            _bus = bus;

            _onStarted = OnStarted;
            _onEnded = OnEnded;
            _onStationChanged = OnStationChanged;
            _onStationEnterRequest = OnStationEnterRequest;
            _onAiBattleRequest = OnAiBattleRequest;
            _onRoomJoined = OnRoomJoined;
            _onRoomStartRequest = OnRoomStartRequest;

            bus.On(Events.Battle.Started, _onStarted);
            bus.On(Events.Battle.Ended, _onEnded);
            bus.On<string>(Events.Flow.StationChanged, _onStationChanged);
            bus.On<string>(Events.Flow.StationEnterRequest, _onStationEnterRequest);
            // ⚠️ 下面两条**只用于记"这一局是不是人机"**（协议没有这个字段）：它们本身已有别的处理者
            //    （`AppFlow.StartAiBattleAsync` / `RoomManager`），本类只是乘客，⛔ 不代它们执行任何动作。
            bus.On(Events.Battle.AiBattleRequest, _onAiBattleRequest);
            bus.On<string>(Events.Room.Joined, _onRoomJoined);
            bus.On(Events.Room.StartRequest, _onRoomStartRequest);

            Game.Logger?.Info(Tag,
                "对局 UI 装载完成（Battle.Started/Ended + Flow.StationChanged/StationEnterRequest + 对局模式推断 3 条）");
        }

        // ═════════════════════════ 对局信息 ═════════════════════════

        private static void OnStarted(BattleStartNotify start)
        {
            if (start == null)
            {
                // 非预期分支：`Emit` 走 `DynamicInvoke`，参数类型不对会在这里显形。留痕。
                Game.Logger?.Warn(Tag, "收到 null 的开打配置（发送方参数有误？），结算页的队伍换算将不可用");
                return;
            }

            _myTeam = start.my_team;
            _roomId = start.room_id ?? string.Empty;
            Game.Logger?.Info(Tag, $"记录开打信息 room={_roomId} my_team={_myTeam}（0=蓝 1=红）");
        }

        private static void OnStationChanged(string station)
        {
            if (station == Stations.Battle)
            {
                // 回到对局站点（含"继续"从暂停返回 / 退到暂停后又进 Battle）：收掉暂停菜单。
                // ⚠️ 不在这里开 HUD：那是 `BattleManager.EnterBattleStation()` 的职责（它会补发
                //    开打配置 / 快照 / 卡池，顺序要求很严），⛔ 本类不重复插手。
                ClosePauseIfOpen("站点已回到 Battle");
                return;
            }

            if (station == Stations.Pause)
            {
                EnsurePausePanel("站点切到 Pause");
            }
        }

        /// <summary>
        /// 站点切换请求（`AppFlow.OnStationEnterRequest` 也会收这条事件并把 FSM 切过去）。
        /// ⚠️ **必须监听"请求"而不只是"站点变化"**：`AppFlow.GoTo` 对"已经是当前站点"的请求会早退
        /// （`AppFlow.cs:357`）⇒ 如果暂停菜单被别的 Popup 顶掉（例如打开设置时 `UIManager` 的
        /// 同层互斥关闭，`UI.cs:143-147`）而站点仍是 `Pause`，那么再点一次「暂停」只会发出请求、
        /// **不会有 `StationChanged`** —— 只监听站点变化的实现到那时就永远打不开暂停菜单了。
        /// </summary>
        private static void OnStationEnterRequest(string station)
        {
            if (station == Stations.Pause) EnsurePausePanel("收到进 Pause 站点的请求");
        }

        private static void OnEnded(BattleEndNotify result)
        {
            if (result == null)
            {
                // 非预期分支：`Bind` 失败 / 发送方参数有误。留痕 —— 否则表现为"打完什么都不显示"。
                Game.Logger?.Warn(Tag, "收到 null 的结算（发送方参数有误？），结算面板不打开");
                return;
            }

            var ui = Game.UI;
            if (ui == null)
            {
                // 非预期分支：表现域未挂载（CloverPresentation.Init 没跑）。留痕。
                Game.Logger?.Error(Tag, "Game.UI 为空（表现域未挂载），结算面板无法打开");
                return;
            }

            var args = new ResultPanel.PanelArgs
            {
                Result = result,
                MyTeam = _myTeam,
                AiBattle = _aiBattle,
                RoomId = _roomId,
            };

            Game.Logger?.Info(Tag,
                $"打开结算面板（win={result.win} draw={result.draw} crowns={result.crowns_a}:{result.crowns_b} " +
                $"reason={result.reason} ai={_aiBattle}）");

            // `ResultPanel` 是 Popup 层：`UIManager.Open` 会互斥关闭同层的暂停菜单，所以不需要先手动关它。
            ui.Open<ResultPanel>(args);
        }

        // ═════════════════════════ 对局模式推断（协议无此字段） ═════════════════════════

        private static void OnAiBattleRequest()
        {
            // 玩家点了主菜单的「人机对战」（`MainMenuPanel.OnAiBattleClicked`）⇒ 接下来的这一局是人机。
            _aiBattle = true;
            Game.Logger?.Info(Tag, "推断本局为人机对局（收到 Battle.AiBattleRequest）");
        }

        private static void OnRoomJoined(string roomId)
        {
            // 进了房间 ⇒ 接下来的对局是房间对局（⛔ 不能沿用上一局的"人机"标记）。
            _aiBattle = false;
            Game.Logger?.Info(Tag, $"推断本局为房间对局（收到 Room.Joined room={roomId}）");
        }

        private static void OnRoomStartRequest()
        {
            // 房主点了「开始对战」（`RoomPanel.OnStartClicked`）⇒ 同上。
            _aiBattle = false;
            Game.Logger?.Info(Tag, "推断本局为房间对局（收到 Room.StartRequest）");
        }

        // ═════════════════════════ 面板开关 ═════════════════════════

        /// <summary>打开暂停菜单（已打开则不动 —— 重复 `OnOpen` 会把玩家的操作状态刷掉）。</summary>
        private static void EnsurePausePanel(string why)
        {
            var ui = Game.UI;
            if (ui == null)
            {
                // 非预期分支：表现域未挂载。留痕。
                Game.Logger?.Error(Tag, $"Game.UI 为空（表现域未挂载），暂停菜单无法打开（{why}）");
                return;
            }

            if (ui.IsOpen<PausePanel>())
            {
                Game.Logger?.Info(Tag, $"暂停菜单已打开，不重复打开（{why}）");
                return;
            }

            Game.Logger?.Info(Tag, $"打开暂停菜单（{why}）");
            ui.Open<PausePanel>();
        }

        private static void ClosePauseIfOpen(string why)
        {
            var ui = Game.UI;
            if (ui == null) return;
            if (!ui.IsOpen<PausePanel>()) return;

            Game.Logger?.Info(Tag, $"关闭暂停菜单（{why}）");
            ui.Close<PausePanel>();
        }
    }
}
