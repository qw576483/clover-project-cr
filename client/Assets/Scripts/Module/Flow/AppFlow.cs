using System;
using System.Threading.Tasks;
using CloverEngine;
using CR.Def;
using CR.Module.Settings;
using CR.UI;
using CR.UI.Panels;
using UnityEngine;

namespace CR.Module.Flow
{
    /// <summary>
    /// 站点状态机 + 面板/场景编排（本项目客户端流程的**唯一**编排处）。
    ///
    /// <para>
    /// <b>唯一原则（架构契约 §0）：面板驱动，不切场景</b>。登录 / 创角 / 主菜单 / 房间 / 暂停
    /// 全是常驻 UI 树上的面板切换（`CloseAll` + `Open&lt;T&gt;`），走真场景加载（`Game.Scene.Load`）
    /// 的**只有三处**：① 启动画面结束时的 `Boot` → `Main`（见 <see cref="LoadMainSceneFromBoot"/>）、
    /// ② `Main` → `Battle01`（进对局）、③ `Battle01` → `Main`（回主菜单）。理由见契约 §0：`Game.UI` 的面板树挂在常驻节点 `[UI]/…` 上，
    /// 而 `Game.Scene.Load` 会连带卸载实体 / 对象池 / 场景内对象；若每个界面都做成场景，
    /// 站内切换会把常驻 UI 树与池一起清掉。
    /// </para>
    /// <para>
    /// <b>事件 vs 直接调用</b>：面板⛔不许 `using CR.Module`（契约 §1），所以面板只能
    /// `Emit(Events.Flow.*)`；本类订阅这些事件后执行动作。**面板发事件、Flow 决定下一步**
    /// 就是本类的全部职责。⚠️ 凡本类既订阅又对外暴露的方法（`RequestStartAiBattle` /
    /// `RequestReturnToMainMenu`），**订阅处理器只调内部实现、绝不重发同一个事件** ——
    /// 否则会自激成死循环（发出的那一刻又被自己收到）。
    /// </para>
    /// <para>
    /// <b>为什么不挂 MonoBehaviour</b>：<see cref="AppFlow"/> 是纯 C# 类、由 `Bootstrap` 持有，
    /// 且通过 <see cref="EnsureCreated"/> 保成**跨场景存活**的单例。这不只是风格：
    /// `Boot` / `Main` / `Battle01` 场景互切时，场景里的 `Bootstrap` 会被销毁并重建，
    /// 而本类持有登录链、订阅与会话态（nickname），做成 MonoBehaviour 会随场景一起消失、
    /// 表现为"从对局回主菜单后流程断了"。
    /// </para>
    /// <para>
    /// <b>登录链（两步，逐字对齐引擎示例 `Samples~/LoginFlow/LoginFlow.cs`）</b>：
    /// ① 账号服 HTTP 换 token（`CloverAuth.LoginAsync`）→ ② 游戏网关 `EMsg.Login{token}`
    /// 用 `Call&lt;ELoginReply&gt;` 拿回包判定成败。⛔ 没有"`EMsg.Login` 成功事件"这种东西 ——
    /// 它是 C2S 请求，成败就在回包里。
    /// </para>
    /// </summary>
    public sealed class AppFlow
    {
        private const string Tag = "AppFlow";

        // ───────────────────────── 场景名（唯一定义处） ─────────────────────────
        //
        // ⛔ 这三个名字必须与 `Assets/Editor/SceneBuilder.cs` 保存的场景文件名、
        //    以及它写进 EditorBuildSettings 的顺序（Boot → Main → Battle01，Boot 是 index 0）一致 ——
        //    不进 Build Settings 的场景 `SceneManager.LoadSceneAsync` 找不到，
        //    `Game.Scene.Load` 会打 Error 并直接回调 onDone。

        /// <summary>
        /// 启动场景（唯一挂着 `Bootstrap` 的入口场景 + `BootPanel` 启动画面）。
        /// Build Settings 的 **index 0**：构建产物启动后先加载它。
        /// </summary>
        public const string BootSceneName = "Boot";

        /// <summary>主场景（登录/创角/主菜单/房间；`Bootstrap` 挂在这里 —— 走出引擎后的重入路径）。</summary>
        public const string MainSceneName = "Main";

        /// <summary>对局场景（竞技场 + HUD + 暂停 + 结算）。</summary>
        public const string BattleSceneName = "Battle01";

        /// <summary>等待网关连接的最长秒数。本项目自定：连不上就早失败并**把原因显示给玩家**，不无限等。</summary>
        private const float ConnectWaitSeconds = 15f;

        /// <summary>连接轮询间隔（秒）。与引擎登录流程同一手法（轮询 + 总超时）。</summary>
        private const float ConnectPollSeconds = 0.1f;

        /// <summary>当前实例（跨场景存活，见类注释）。</summary>
        public static AppFlow Instance { get; private set; }

        private readonly SettingsManager _settings;

        // 会话昵称**不再放在本类字段里**：它是"服务端权威数据"，必须能被**面板**读到，
        // 而面板不许 `using CR.Module`（契约 §1）⇒ 数据落在 `CR.PlayerSession`（服务端回包写入，
        // 面板读取）。本类保留的只是"写入 + 广播"职责。见 `Core/PlayerSession.cs` 的类注释（根因）。
        private bool _nicknameRefreshBusy;

        /// <summary>
        /// 待显示给玩家的登录失败原因（一次性）。为什么需要它：失败（含被踢）会**切回登录站点**，
        /// 而 `LoginFailed` 事件在面板还没打开时无人接收 ⇒ 玩家只会看到一个干净的登录框、
        /// 完全不知道刚才发生了什么。所以失败原因要在切站点时**随 param 带进新面板**。
        /// </summary>
        private string _pendingLoginError;

        private bool _started;

        /// <summary>
        /// **本轮引擎**的事件总线（= 本流程当前订阅所在的那一条）。用途见 <see cref="EnsureCreated"/>：
        /// 引擎每轮 `Game.Launch` 都会**新建** `EventBus`（引擎 `Game.cs:381`），而本工程**关闭了域重载**
        /// ⇒ 静态单例 <see cref="Instance"/> 会跨轮存活。若不按总线重绑，新一轮里
        /// `Instance != null` 会直接早退 ⇒ **新总线上一个订阅都没有** ⇒ 面板发的事件没人接、
        /// 玩家点了没反应（AR1 实测的**僵尸会话**）。口径与工程其余 5 处逐字一致
        /// （`RoomManager` / `DeckManager` / `BattleManager` / `BattleUiHost` / `BgmView`）。
        /// </summary>
        private IEventBus _bus;

        private bool _busy;                 // 登录链在途（防重入：按钮连点 / 注册后自动登录）
        private bool _enteringBattle;       // 读条进图在途
        private string _enteringRoomId;     // 在途进图的房间号（`RequestEnterBattle` 的幂等判据）
        private bool _returningToMainMenu;  // 回主菜单在途

        /// <summary>
        /// 人机对战在途（防重入）。**根因（CR-F2 审计实测）**：本类此前只有 `_busy`（登录链）
        /// / `_enteringBattle` / `_returningToMainMenu` 三道闸，`StartAiBattleAsync` **一道都没有** ——
        /// 主菜单「人机对战」按钮连点 N 次就会发出 N 条 `MsgAiBattleStart`，服务端每次都**真建一个房**
        /// 并各推一条 `PushBattleStart`（`server/game/logic/ai.go:onAiBattleStart` 无去重），
        /// 客户端随后被多条推送反复要求进图。
        /// <para>
        /// 闸的持有区间 = **从发请求到服务端 `PushBattleStart` 接手**（不是"到回包返回"）：
        /// 「回包已到、推送未到」之间仍留在主菜单站点、按钮仍可点，这段时间放开闸等于把连点漏洞留着。
        /// 推送一接手就转由 `_enteringBattle` 管（进图自有幂等），两条闸首尾相接、不重叠。
        /// </para>
        /// </summary>
        private bool _aiBattleInFlight;

        private Action<string, string> _onLoginRequest;
        private Action<string, string> _onRegisterRequest;
        private Action _onOpenRegisterRequest;
        private Action _onBackToLoginRequest;
        private Action<string> _onNicknameSubmit;
        private Action _onOpenSettingsRequest;
        private Action _onQuitRequest;
        private Action<string> _onStationEnterRequest;
        private Action _onAiBattleRequest;
        private Action _onReturnToMainMenuRequest;

        private Action _onNetConnected;
        private Action _onNetDisconnected;
        private Action _onNetKicked;
        private MsgHandler _onBattleStartPush;

        private AppFlow(SettingsManager settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// 取（或首次创建）流程实例。第二次进入 `Main` 场景时 `Bootstrap` 是新建的，
        /// 但引擎还活着 ⇒ 这里必须返回**同一个**实例，否则新实例会重新进 `Boot` 站点、
        /// 把已经登录的玩家弹回启动画面。
        /// </summary>
        public static AppFlow EnsureCreated(SettingsManager settings)
        {
            if (Instance != null)
            {
                // ⛔ 不要盲目返回：静态单例可能属于**上一轮**引擎（见 `_bus` 注释）。
                // 总线变了就把订阅重挂到当前总线上（只重挂，不重跑 Start、不回 Boot 站点）。
                Instance.RebindIfBusChanged();
                return Instance;
            }

            Instance = new AppFlow(settings);
            Instance.Start();
            return Instance;
        }

        /// <summary>
        /// 总线换了（= 引擎重新 Launch）⇒ 把"必须跟着当前引擎走"的东西补挂到**当前**总线上。
        /// <para>
        /// 为什么只做这三件（订阅 / 消息处理器 / 站点注册）而**不**调 <see cref="Start"/>：
        /// `Start` 末尾会 `GoTo(Stations.Boot)` ⇒ 把已经登录的玩家弹回启动画面；
        /// 而换总线这件事在"从对局回主菜单 / 编辑器重新 Play"都要能安全发生。
        /// </para>
        /// <para>同一条总线时本方法**什么都不做**（`ReferenceEquals` 早退）。</para>
        /// </summary>
        private void RebindIfBusChanged()
        {
            var bus = Game.Event;
            if (bus == null || ReferenceEquals(bus, _bus)) return;

            Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），流程重装订阅与站点注册");
            RegisterStations();
            Subscribe();

            // ★★ 换总线 = 引擎重新 Launch = **新会话**：站点状态机也是全新的，此刻停在引擎的初始态
            //     （实测 `Game.Fsm.Current == "Launching"`）—— 也就是"没有任何站点在屏上"。
            //     只重挂订阅就返回的话，流程**永远不会再动**：实测 2026-09-23 17:34 那次 Play，
            //     17:34:20 打完 "网络已连接" 之后一行日志都没有，`[UI]` 的 7 个层
            //     （Background/Normal/Popup/Top/Toasts/FloatTexts/System）全空 = 纯深色空屏，
            //     而且 `consoleErrors=0` —— 正是最难查的那类"静默"。所以这里必须补一次 `GoTo(Boot)`。
            //
            //   ⛔ 判据是"当前站点**在不在本流程的站点集里**"，不是"有没有换过总线"：
            //     "从对局回主菜单"走的是**同一个引擎、同一条总线**（`Bootstrap.OnDestroy` 刻意
            //     不调 `Game.Shutdown()`，见其注释），根本进不到本方法 ⇒ 不会把已登录的玩家弹回启动画面。
            //     旧注释把"编辑器重新 Play"也算进"不能回启动画面"的场景，那是错的：重新 Play 时
            //     引擎是新的、玩家根本没登录，回 Boot 站点才是对的（本行就是那条缺掉的推进）。
            var cur = Game.Fsm?.Current;
            var inOwnStation = cur == Stations.Boot || cur == Stations.Login || cur == Stations.Nickname
                || cur == Stations.MainMenu || cur == Stations.Room || cur == Stations.Battle
                || cur == Stations.Pause;
            if (!inOwnStation)
            {
                Game.Logger?.Warn(Tag,
                    $"换总线后 FSM 停在 '{cur}'（不在本流程的站点集内）⇒ 重新进 {Stations.Boot} 站点，避免永远空白屏");
                GoTo(Stations.Boot);
            }
        }

        // ═════════════════════════ 对外契约（agent-06/07/08 依赖这几个签名，⛔ 不许改） ═════════════════════════

        /// <summary>
        /// 请求打开卡组编辑。
        /// 实现落点在 agent-08 的 `Module/Deck` + `DeckEditPanel`（本片范围内没有它们）：
        /// 因此这里只**广播** `Events.Deck.OpenRequest`，由那边的模块订阅后拉卡池/卡组并开面板。
        /// </summary>
        public void RequestOpenDeckEdit()
        {
            Game.Logger?.Info(Tag, "请求打开卡组编辑（广播 Events.Deck.OpenRequest）");
            Game.Event?.Emit(Events.Deck.OpenRequest);
        }

        /// <summary>
        /// 请求打开房间列表。同上：面板与 `MsgRoomList` 属 agent-06 的 `Module/Room`，
        /// 这里只广播 `Events.Room.OpenListRequest`。
        /// </summary>
        public void RequestOpenRoomList()
        {
            Game.Logger?.Info(Tag, "请求打开房间列表（广播 Events.Room.OpenListRequest）");
            Game.Event?.Emit(Events.Room.OpenListRequest);
        }

        /// <summary>
        /// 主菜单「人机对战」：发 `MsgAiBattleStart`（服务端一条消息完成"建房 + AI 占座 + 立刻开打"）。
        /// 成功后服务端推 `PushBattleStart`，本类收到后走 <see cref="RequestEnterBattle"/> 读条进图。
        /// </summary>
        public void RequestStartAiBattle()
        {
            StartAiBattleAsync();
        }

        /// <summary>
        /// 进对局：读条（真进度）→ 加载 `Battle01` → 切到 `Battle` 站点。
        /// <para>
        /// <b>由谁调</b>：agent-06 的 `Module/Room`（收到 `PushBattleStart` 后）或 agent-07 的对局模块。
        /// ⚠️ 本类**自己也**订阅了 `PushBattleStart`（这样单独跑本片时人机对战也能走完），
        /// 所以本方法按 `roomId` **幂等**：同一个房间的重复请求只执行一次，不会加载两遍场景。
        /// </para>
        /// </summary>
        public void RequestEnterBattle(string roomId, BattleStartNotify start)
        {
            if (start == null)
            {
                Game.Logger?.Error(Tag, "RequestEnterBattle 收到 null 的开打配置（服务端 PushBattleStart 反序列化失败？），不切场景");
                return;
            }
            var effectiveRoomId = string.IsNullOrEmpty(roomId) ? start.room_id : roomId;

            if (_enteringBattle)
            {
                if (_enteringRoomId == effectiveRoomId)
                {
                    Game.Logger?.Info(Tag, $"房间 {effectiveRoomId} 已在进图流程中，忽略重复的 RequestEnterBattle");
                }
                else
                {
                    Game.Logger?.Warn(Tag,
                        $"上一个房间 {_enteringRoomId} 还在进图，忽略新房间 {effectiveRoomId} 的进图请求");
                }
                return;
            }

            // 先在**旧场景**里广播开打配置：agent-07 的对局模块据此建立状态、
            // 并在随后的场景切换中把竞技场建起来（场景资源在 Load 完成后才属于新场景）。
            Game.Event?.Emit(Events.Battle.Started, start);

            if (Game.Scene == null)
            {
                Game.Logger?.Error(Tag, "Game.Scene 为空（表现域未挂载），无法进入对局");
                return;
            }
            if (Game.Scene.CurrentScene == BattleSceneName)
            {
                // 已经在 Battle01（例如重连后服务端重推开打）：只切站点，不重复加载场景
                //（重复加载会把正在打的竞技场与单位一起清掉）。
                Game.Logger?.Info(Tag, "已在 Battle01 场景，仅切换站点到 Battle");
                GoTo(Stations.Battle);
                return;
            }

            _enteringBattle = true;
            _enteringRoomId = effectiveRoomId;
            EnterSceneAsync(BattleSceneName, "正在进入对局…", Stations.Battle, () =>
            {
                _enteringBattle = false;
                _enteringRoomId = null;
            });
        }

        /// <summary>
        /// 回主菜单（结算 / 暂停面板调用）：读条（真进度）→ 加载 `Main` → 切到 `MainMenu` 站点。
        /// 幂等：在途时重复调用被忽略。
        /// </summary>
        public void RequestReturnToMainMenu()
        {
            if (_returningToMainMenu)
            {
                Game.Logger?.Info(Tag, "回主菜单已在途，忽略重复请求");
                return;
            }

            // 已在 Main 场景（含"从没切过场景"的启动态）：不加载场景，只切站点 ——
            // ⛔ 绝不能在这里 `Load(Main)`：那会把 Bootstrap 所在的当前场景整个重载。
            if (Game.Scene == null || Game.Scene.CurrentScene != BattleSceneName)
            {
                Game.Logger?.Info(Tag,
                    $"当前不在 {BattleSceneName}（CurrentScene='{Game.Scene?.CurrentScene}'），只切站点到 MainMenu");
                GoTo(Stations.MainMenu);
                return;
            }

            _returningToMainMenu = true;
            EnterSceneAsync(MainSceneName, "正在返回主菜单…", Stations.MainMenu, () => _returningToMainMenu = false);
        }

        // ═════════════════════════ 启动 / 停止 ═════════════════════════

        private void Start()
        {
            if (_started) return;
            _started = true;

            RegisterStations();
            Subscribe();

            Game.Logger?.Info(Tag, "流程启动 → 进 Boot 站点");
            GoTo(Stations.Boot);
        }

        /// <summary>
        /// 注册 7 个站点状态 + 转移表。
        /// ⚠️ `Battle` 与引擎自注册的**占位**状态同名（`Game.cs:855` 的 `Fsm.RegisterState("Battle")`，
        /// 三个回调全为 null）—— 引擎会在这次注册时打一条 Warn"state 'Battle' re-registered"，
        /// 那是**预期内**的：占位态没有任何回调，被替换不损失行为。站点名按契约 §4 逐字使用，不改名。
        /// </summary>
        private void RegisterStations()
        {
            if (Game.Fsm == null)
            {
                Game.Logger?.Error(Tag, "Game.Fsm 为空（Game.Launch 未执行？），站点状态机无法注册");
                return;
            }

            Game.Fsm.RegisterState(Stations.Boot, EnterBoot);
            Game.Fsm.RegisterState(Stations.Login, EnterLogin);
            Game.Fsm.RegisterState(Stations.Nickname, EnterNickname);
            Game.Fsm.RegisterState(Stations.MainMenu, EnterMainMenu);
            Game.Fsm.RegisterState(Stations.Room, EnterRoom);
            Game.Fsm.RegisterState(Stations.Battle, EnterBattle);
            Game.Fsm.RegisterState(Stations.Pause, EnterPause);

            // 转移表：站点切换的**唯一入口**是 `GoTo`（内部用 `Transition`），下面这些触发器
            // 是给"不能引 CR.Module 的面板/模块"准备的等价入口（例如 agent-07 的暂停面板可
            // `Game.Fsm.Trigger("ToPause")`）。两条路径最终都落到同一批 OnEnter 回调。
            Game.Fsm.AddTransition("BootDone", Stations.Login);
            Game.Fsm.AddTransition("ToLogin", Stations.Login);
            Game.Fsm.AddTransition("NeedNickname", Stations.Nickname);
            Game.Fsm.AddTransition("ToMainMenu", Stations.MainMenu);
            Game.Fsm.AddTransition("ToRoom", Stations.Room);
            Game.Fsm.AddTransition("ToBattle", Stations.Battle);
            Game.Fsm.AddTransition("ToPause", Stations.Pause);
        }

        private void Subscribe()
        {
            _onLoginRequest = (account, password) => LoginAsync(account, password);
            _onRegisterRequest = (account, password) => RegisterAsync(account, password);
            _onOpenRegisterRequest = OnOpenRegister;
            _onBackToLoginRequest = () => GoTo(Stations.Login);
            _onNicknameSubmit = SubmitNicknameAsync;
            _onOpenSettingsRequest = OpenSettings;
            _onQuitRequest = Quit;
            _onStationEnterRequest = OnStationEnterRequest;
            // ⚠️ `Events.Battle.AiBattleRequest` 的处理者只允许是"不重发该事件的内部实现"：
            //    若挂成 `RequestStartAiBattle`（它自己会 Emit 同一条事件），就会自激成死循环。
            _onAiBattleRequest = StartAiBattleAsync;
            _onReturnToMainMenuRequest = RequestReturnToMainMenu;
            _onNetConnected = () => Game.Logger?.Info(Tag, "网络已连接");
            _onNetDisconnected = () => Game.Logger?.Warn(Tag, "网络已断开（引擎会自动重连 / 恢复会话）");
            _onNetKicked = OnKicked;
            _onBattleStartPush = OnBattleStartPush;

            Game.Event?.On(Events.Flow.LoginRequest, _onLoginRequest);
            Game.Event?.On(Events.Flow.RegisterRequest, _onRegisterRequest);
            Game.Event?.On(Events.Flow.OpenRegisterRequest, _onOpenRegisterRequest);
            Game.Event?.On(Events.Flow.BackToLoginRequest, _onBackToLoginRequest);
            Game.Event?.On(Events.Flow.NicknameSubmit, _onNicknameSubmit);
            Game.Event?.On(Events.Flow.OpenSettingsRequest, _onOpenSettingsRequest);
            Game.Event?.On(Events.Flow.QuitRequest, _onQuitRequest);
            Game.Event?.On(Events.Flow.StationEnterRequest, _onStationEnterRequest);
            // ⛔ 本类**不订阅** `Events.Deck.OpenRequest` / `Events.Room.OpenListRequest`：
            //    它们的响应者分别是 agent-08 的 `Module/Deck` 与 agent-06 的 `Module/Room`；
            //    而 `RequestOpenDeckEdit` / `RequestOpenRoomList` 就是"重发这两条事件"，
            //    挂上来会立刻自激成死循环。
            Game.Event?.On(Events.Battle.AiBattleRequest, _onAiBattleRequest);
            Game.Event?.On(Events.Battle.ReturnToMainMenuRequest, _onReturnToMainMenuRequest);

            Game.Event?.On(CloverEvents.Net.OnConnected, _onNetConnected);
            Game.Event?.On(CloverEvents.Net.OnDisconnected, _onNetDisconnected);
            Game.Event?.On(CloverEvents.Net.OnKicked, _onNetKicked);

            // 开打推送：`Game.OnMsg` 是业务消息的唯一定阅入口（`Game.Net` 上没有 OnMsg）。
            // ⛔ 必须是 `PushBattleStart`（Reliable），不是快照（BestEffort，10 Hz）。
            Game.OnMsg(MsgDef.PushBattleStart, _onBattleStartPush);

            // 记下"本轮引擎"的标识（`RebindIfBusChanged` 的判据）。必须在所有 On/OnMsg 之后记，
            // 否则中途换总线时这里会先被写上、判据就永远为真 = 白加。
            _bus = Game.Event;
        }

        /// <summary>退订 + 注销消息（正常只在退出应用时走一次）。</summary>
        public void Shutdown()
        {
            if (!_started) return;
            _started = false;

            Game.Event?.Off(Events.Flow.LoginRequest, _onLoginRequest);
            Game.Event?.Off(Events.Flow.RegisterRequest, _onRegisterRequest);
            Game.Event?.Off(Events.Flow.OpenRegisterRequest, _onOpenRegisterRequest);
            Game.Event?.Off(Events.Flow.BackToLoginRequest, _onBackToLoginRequest);
            Game.Event?.Off(Events.Flow.NicknameSubmit, _onNicknameSubmit);
            Game.Event?.Off(Events.Flow.OpenSettingsRequest, _onOpenSettingsRequest);
            Game.Event?.Off(Events.Flow.QuitRequest, _onQuitRequest);
            Game.Event?.Off(Events.Flow.StationEnterRequest, _onStationEnterRequest);
            Game.Event?.Off(Events.Battle.AiBattleRequest, _onAiBattleRequest);
            Game.Event?.Off(Events.Battle.ReturnToMainMenuRequest, _onReturnToMainMenuRequest);
            Game.Event?.Off(CloverEvents.Net.OnConnected, _onNetConnected);
            Game.Event?.Off(CloverEvents.Net.OnDisconnected, _onNetDisconnected);
            Game.Event?.Off(CloverEvents.Net.OnKicked, _onNetKicked);
            Game.OffMsg(MsgDef.PushBattleStart);

            _bus = null;      // 本轮归属已失效：下一轮必须重绑（见 `_bus` 注释）
            Instance = null;
        }

        // ═════════════════════════ 站点切换 ═════════════════════════

        /// <summary>切站点的**唯一实现**（站点名必须是 <see cref="Stations"/> 的常量）。</summary>
        private void GoTo(string station)
        {
            if (Game.Fsm == null) return;
            if (Game.Fsm.Current == station) return;

            Game.Logger?.Info(Tag, $"站点 {Game.Fsm.Current} → {station}");
            Game.Fsm.Transition(station);
            // 站点变化对外广播：订阅方（如 agent-07 的对局模块）据此开关自己的面板。
            Game.Event?.Emit(Events.Flow.StationChanged, station);
        }

        /// <summary>打开注册面板：仍留在 `Login` 站点（注册是登录站点的第二个面孔，不是新站点）。</summary>
        private void OnOpenRegister()
        {
            Game.UI?.CloseAll();
            Game.UI?.Open<RegisterPanel>();
        }

        private void OnStationEnterRequest(string station)
        {
            if (string.IsNullOrEmpty(station))
            {
                Game.Logger?.Warn(Tag, "收到空的站点切换请求，已忽略");
                return;
            }
            // 只认已注册的站点名（引擎对未注册状态会打 Error 并**不切换** —— 这里提前拦一道，
            // 把"谁传了错的名字"记清楚）。
            if (station != Stations.Boot && station != Stations.Login && station != Stations.Nickname
                && station != Stations.MainMenu && station != Stations.Room && station != Stations.Battle
                && station != Stations.Pause)
            {
                Game.Logger?.Warn(Tag, $"未知站点名 '{station}'，已忽略（取值必须是 Core/Stations.cs 的常量）");
                return;
            }
            GoTo(station);
        }

        // ───────────────────────── 各站点的 OnEnter ─────────────────────────

        /// <summary>
        /// `Boot` 站点：打开启动画面（`BootPanel`，含底部 `by clover-engine` 署名），
        /// 停留 <see cref="GameConst.BootSplashSeconds"/> 秒后切到 `Main` 场景。
        ///
        /// <para>
        /// <b>为什么启动画面跑在 `Boot` 场景里</b>：引擎的首次拉起（`Bootstrap.LaunchEngine`）与
        /// 启动画面同处一个场景，玩家先看到一个完整的启动画面、再进入承载 UI 的主场景；
        /// `Main` 场景里的 `Bootstrap` 走的是"引擎已在运行"的重入路径（见 `Bootstrap.Start`）。
        /// </para>
        /// </summary>
        private void EnterBoot()
        {
            Game.UI?.CloseAll();
            Game.UI?.Open<BootPanel>();
            Game.Logger?.Info(Tag,
                $"{BootSceneName} 场景启动画面已打开（停留 {GameConst.BootSplashSeconds:0}s 后切 {MainSceneName} 场景）");

            // 停留后切主场景。⛔ 用 `Game.Timer.After`（引擎没有 `Timer.Once`）。
            Game.Timer?.After(GameConst.BootSplashSeconds, () =>
            {
                // 回调可能在玩家已经走了别的站点之后才到（例如被踢回登录）：只在仍处 Boot 时推进。
                if (Game.Fsm == null || Game.Fsm.Current != Stations.Boot)
                {
                    Game.Logger?.Info(Tag,
                        $"启动画面停留结束时站点已是 '{Game.Fsm?.Current}'，不再走切 {MainSceneName} 场景的启动流程");
                    return;
                }
                LoadMainSceneFromBoot();
            });
        }

        /// <summary>
        /// 启动画面 → 主场景的**唯一**切换点：`Game.Scene.Load(MainSceneName)`，
        /// 加载完成后进 `Login` 站点。
        ///
        /// <para>
        /// <b>为什么切场景而不是直接进 `Login`</b>：`Boot` 场景只承载启动画面与引擎的首次拉起，
        /// 登录之后的一切界面（登录 / 创角 / 主菜单 / 房间）都挂在 `Main` 场景里；
        /// 切过去之后由 `Main` 的 `Bootstrap` 以重入路径（`Game.IsRunning` 分支）继续，
        /// 流程实例 <see cref="Instance"/> 是纯 C# 单例、不随场景销毁，所以登录态/订阅都不断。
        /// </para>
        /// <para>
        /// ⛔ 启动画面（`BootPanel`）在加载期间**保持显示**，不换读条面板：
        /// 这就是"启动画面 → 主场景"的连续观感；`EnterLogin` 的 `CloseAll` 才把它收掉。
        /// </para>
        /// </summary>
        private void LoadMainSceneFromBoot()
        {
            if (Game.Scene == null)
            {
                // 非预期分支：表现域未挂载（`Bootstrap` 漏了 LaunchEngine，或装配次序被改坏）。
                // 不留痕的话现象是"永久停在启动画面上、一句报错都没有"。
                Game.Logger?.Error(Tag,
                    $"Game.Scene 为空（表现域未挂载），无法切到 {MainSceneName} 场景；直接进 Login 站点");
                GoTo(Stations.Login);
                return;
            }

            // 开发期直接把 `Main` 设为开场景跑 Play：当前活动场景已经是 Main，⛔ 不重载 ——
            // 重载会把 `Bootstrap` 所在的当前场景整个拆掉再建一遍，没有必要（不是启动链的语义）。
            // 出处：`SceneManager` 与 `Scene.name` 均来自 UnityEngine.SceneManagement
            //（引擎同一处也在用：Runtime/Presentation/Scene.cs:30 `SceneManager.LoadSceneAsync`；
            // `Game.Scene.CurrentScene` 只记录**经引擎加载过**的场景名，启动场景名它不知道）。
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (activeScene == MainSceneName)
            {
                Game.Logger?.Info(Tag,
                    $"当前活动场景已是 {MainSceneName}（开发期直接跑 Main），跳过场景切换，直接进 Login 站点");
                GoTo(Stations.Login);
                return;
            }

            Game.Logger?.Info(Tag,
                $"Boot 场景启动画面结束 → 切 {MainSceneName} 场景（由那边的 Bootstrap 走重入路径继续）");
            var loggedStep = -1;
            Game.Scene.Load(MainSceneName, progress =>
            {
                // 只按 25% 一档打日志：progress 回调是 0.05s 一跳，逐条打会刷屏。
                var step = (int)(progress * 4f);
                if (step == loggedStep) return;
                loggedStep = step;
                Game.Logger?.Info(Tag, $"{MainSceneName} 场景加载进度 {progress * 100f:0}%");
            }, () =>
            {
                // 加载失败时引擎也会回调这里（`Scene not found` 走 Error 分支后直接 onDone），
                // 所以推进站点是安全的：最坏情况是画面停在启动画面上但流程不再挂死。
                Game.Logger?.Info(Tag, $"{MainSceneName} 场景加载完成 → 进 Login 站点");
                GoTo(Stations.Login);
            });
        }

        private void EnterLogin()
        {
            Game.UI?.CloseAll();
            // 带上"待显示的失败原因"（见 `_pendingLoginError` 的注释）：一次性，用掉即清。
            Game.UI?.Open<LoginPanel>(_pendingLoginError);
            _pendingLoginError = null;
        }

        private void EnterNickname()
        {
            Game.UI?.CloseAll();
            Game.UI?.Open<NicknamePanel>();
        }

        private void EnterMainMenu()
        {
            Game.UI?.CloseAll();
            Game.UI?.Open<MainMenuPanel>(PlayerSession.Nickname);

            if (PlayerSession.HasNickname)
            {
                // 推一次"权威昵称已就绪"：面板可能在此之前就被别的路径打开过（`GoTo` 对
                // 同一站点早退 ⇒ 本方法不跑），它现在能就地刷新成服务端那个名字。
                Game.Event?.Emit(Events.Flow.NicknameKnown, PlayerSession.Nickname);
                return;
            }

            // 非预期分支：进主菜单时还没拿到服务端昵称（档案为空 / 登录链未走完）。
            // ⛔ 不能静默 —— 面板此时只能显示兜底名，必须留痕（Warn 对**每个会话的这一次**各一条，
            //    由 `_nicknameRefreshBusy` 去重，不会每次进菜单都刷屏），并主动去服务端再拉一次。
            Game.Logger?.Warn(Tag,
                "进入主菜单时尚未拿到服务端昵称（档案为空或登录链未走完）；面板先显示兜底名，已重新拉取档案");
            RefreshNicknameAsync();
        }

        /// <summary>
        /// 向服务端重拉一次档案里的昵称，成功则写入 <see cref="PlayerSession"/> 并广播
        /// <see cref="Events.Flow.NicknameKnown"/>（已打开的主菜单面板据此就地刷新）。
        /// <para>
        /// 为什么要有这条"重拉"：昵称是**迟到**数据。`EnterMainMenu` 可能在任何一条路径上先跑
        ///（回主菜单 / 房间退出 / 被踢重登），那时档案请求可能还没回来 —— 兜底名能立刻显示，
        /// 但一旦档案到位就必须换成服务端那个名字（判据：主菜单显示服务端权威昵称，不是本地默认值）。
        /// </para>
        /// </summary>
        private async void RefreshNicknameAsync()
        {
            if (_nicknameRefreshBusy) return;
            if (Game.Net == null)
            {
                Game.Logger?.Warn(Tag, "无法重拉昵称：网络模块未挂载（主菜单保持兜底名）");
                return;
            }
            _nicknameRefreshBusy = true;
            try
            {
                var profile = await Game.Net.Call<GetProfileReply>(MsgDef.GetProfile, new GetProfileReq());
                var nick = profile != null ? profile.nickname : null;
                if (string.IsNullOrEmpty(nick))
                {
                    Game.Logger?.Warn(Tag, "重拉档案后昵称仍为空（服务端该玩家未创角）；主菜单保持兜底名");
                    return;
                }
                PlayerSession.SetNickname(nick, "RefreshNicknameAsync");
                Game.Event?.Emit(Events.Flow.NicknameKnown, PlayerSession.Nickname);
            }
            catch (Exception e)
            {
                Game.Logger?.Warn(Tag, "重拉档案失败（主菜单保持兜底名）：" + e.Message);
            }
            finally
            {
                _nicknameRefreshBusy = false;
            }
        }

        private void EnterRoom()
        {
            // 房间站点（`RoomPanel`）属 agent-06 的 `Module/Room`：本片不引它的类型，
            // 只把站点切好并广播 —— 那边的模块订阅 `Events.Flow.StationChanged` 后自己开面板。
            Game.UI?.CloseAll();
            Game.Logger?.Info(Tag, "已进入 Room 站点（RoomPanel 由 agent-06 的 Module/Room 负责打开）");
        }

        private void EnterBattle()
        {
            // 读条面板在场景加载完成时由 EnterSceneAsync 收掉，这里只收尾 + 广播。
            Game.UI?.CloseAll();
            Game.Logger?.Info(Tag,
                "已进入 Battle 站点（竞技场/HUD 由 agent-07 的 Module/Battle + View + HudPanel 负责）");
        }

        private void EnterPause()
        {
            // 对战不真暂停（参考规格 §7 S22：暂停只覆盖菜单 + 投降），所以这里**不动 timeScale**，
            // 也不关下层 HUD —— PausePanel 是 Popup 层，UIManager 会自动压遮罩。
            Game.Logger?.Info(Tag, "已进入 Pause 站点（PausePanel 由 agent-07 负责打开）");
        }

        // ═════════════════════════ 登录 / 注册 / 创角 ═════════════════════════

        /// <summary>
        /// 登录链：账号服换 token → `EMsg.Login` → `SetupSession` → 拉档案决定进创角还是主菜单。
        /// `async void` 只用于事件回调的顶层（异常已全捕获，不会逃逸到 Unity 的未处理异常通道）。
        /// </summary>
        private async void LoginAsync(string account, string password)
        {
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "登录链已在途，忽略重复的登录请求（按钮连点？）");
                return;
            }
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                Fail("账号或密码为空");
                return;
            }

            _busy = true;
            _pendingLoginError = null; // 新的一次尝试：清掉上一次的失败原因（避免"回到登录页看到旧错误"）
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return;
                }
                if (!CloverAuth.Enabled)
                {
                    Fail("账号服地址未配置（CloverAuth.AuthAddr 为空）");
                    return;
                }

                // 等网关连接就绪：账号服与网关是两个独立服务，先连上哪个都不一定。
                if (!await WaitConnectedAsync())
                {
                    Fail($"等待网关连接超时（{ConnectWaitSeconds:0}s）—— 检查服务端是否已启动、地址是否 {Cfg.Server.addr}");
                    return;
                }

                // ① 账号服 HTTP 换 token（失败抛异常，消息就是服务端 err 文案）。
                var token = await CloverAuth.LoginAsync(account, password);
                if (string.IsNullOrEmpty(token))
                {
                    Fail("账号服未返回 token");
                    return;
                }

                // ② 游戏服登录。回包直接给出成败 —— 没有"EMsg.Login 成功事件"这种东西。
                //    ⛔ `ELoginRequest.encrypt` 不要手填：`NetworkManager.Call` 会按平台能力自动置位。
                var reply = await Game.Net.Call<ELoginReply>(EMsg.Login, new ELoginRequest { token = token });
                if (reply == null || !reply.success)
                {
                    Fail("游戏服登录失败：" + (reply != null && !string.IsNullOrEmpty(reply.err) ? reply.err : "空回包"));
                    return;
                }

                // ③ 建立会话。★ 恢复凭证必须传 **null**：登录回包的 session_key 是通道加密密钥
                //    （AES-256），当断线恢复凭证交上去会让恢复会话 token mismatch 被踢
                //    （`Contracts.cs:304-314` 与 `LoginFlow.cs:206` 都写明了这一点）。
                //    恢复凭证由服务端随全量同步推送的 session_token 覆盖。
                Game.Net.SetupSession(account, null, Cfg.Account.line);
                Game.Logger?.Info(Tag, $"已登录 owner={reply.owner} line={Cfg.Account.line}");

                // ④ 拉一次档案：昵称为空 ⇒ 还没创角，先走 Nickname 站点。
                //    用 GetProfile 而不是等 Game.Sync：档案里就有 nickname，一步到位且不依赖推送时序。
                var profile = await Game.Net.Call<GetProfileReply>(MsgDef.GetProfile, new GetProfileReq());
                PlayerSession.SetNickname(profile != null ? profile.nickname : null, "GetProfile");

                Game.Event?.Emit(Events.Flow.LoginSucceeded, PlayerSession.Nickname);
                GoTo(PlayerSession.HasNickname ? Stations.MainMenu : Stations.Nickname);
            }
            catch (Exception e)
            {
                // 超时 / 账号服 4xx / 网关拒登 都在这里落地（`CloverAuth` 与 `Call` 都以异常结束）。
                Fail(e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>注册（账号服注册成功即签发 token）→ 复用同一条登录链。</summary>
        private async void RegisterAsync(string account, string password)
        {
            if (_busy)
            {
                Game.Logger?.Warn(Tag, "登录链已在途，忽略重复的注册请求");
                return;
            }
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password))
            {
                Fail("账号或密码为空");
                return;
            }

            _busy = true;
            _pendingLoginError = null; // 同 LoginAsync：新的一次尝试清掉旧原因
            try
            {
                if (Game.Net == null)
                {
                    Fail("网络模块未挂载（漏了 CloverNet.Init？）");
                    return;
                }
                if (!CloverAuth.Enabled)
                {
                    Fail("账号服地址未配置（CloverAuth.AuthAddr 为空）");
                    return;
                }
                if (!await WaitConnectedAsync())
                {
                    Fail($"等待网关连接超时（{ConnectWaitSeconds:0}s）—— 检查服务端是否已启动");
                    return;
                }

                // 注册失败（最常见是"账号已存在"）抛异常，原样显示给玩家。
                await CloverAuth.SignupAsync(account, password);
                Game.Logger?.Info(Tag, $"账号服注册成功 account={account}，接着走登录链");
            }
            catch (Exception e)
            {
                Fail("注册失败：" + e.Message);
                return;
            }
            finally
            {
                _busy = false;
            }

            // 注册成功 = 已有 token，但登录链本身要再走一遍（拿游戏服会话 + 档案），
            // 复用 LoginAsync 保证只有一处会话建立逻辑。
            LoginAsync(account, password);
        }

        /// <summary>创角：`MsgSetNickname` → 成功进主菜单。</summary>
        private async void SubmitNicknameAsync(string nickname)
        {
            try
            {
                if (Game.Net == null)
                {
                    EmitNicknameFailed("网络模块未挂载");
                    return;
                }
                var reply = await Game.Net.Call<SetNicknameReply>(MsgDef.SetNickname,
                    new SetNicknameReq { nickname = nickname });
                if (reply == null || !reply.ok)
                {
                    EmitNicknameFailed(reply != null && !string.IsNullOrEmpty(reply.err) ? reply.err : "空回包");
                    return;
                }

                PlayerSession.SetNickname(string.IsNullOrEmpty(reply.nickname) ? nickname : reply.nickname, "SetNickname");
                Game.Logger?.Info(Tag, $"创角成功 nickname={PlayerSession.Nickname}");
                // 创角回包就是"服务端权威昵称"到达的时刻 ⇒ 推给可能已经打开的主菜单面板。
                Game.Event?.Emit(Events.Flow.NicknameKnown, PlayerSession.Nickname);
                GoTo(Stations.MainMenu);
            }
            catch (Exception e)
            {
                EmitNicknameFailed("设置昵称失败：" + e.Message);
            }
        }

        /// <summary>登录链失败的**唯一**出口：打日志 + 广播（面板据此把原因显示给玩家）。</summary>
        private void Fail(string reason)
        {
            _busy = false;
            _pendingLoginError = reason; // 若随后切回登录站点，让新面板把原因显示出来
            Game.Logger?.Error(Tag, "登录链失败：" + reason);
            Game.Event?.Emit(Events.Flow.LoginFailed, reason);
        }

        private void EmitNicknameFailed(string reason)
        {
            Game.Logger?.Warn(Tag, "创角失败：" + reason);
            Game.Event?.Emit(Events.Flow.NicknameFailed, reason);
        }

        private void OnKicked()
        {
            // 被踢 = 登录态终结（引擎 `EngineLoginFlow` 同一口径）：必须回登录页重新登录，
            // 否则玩家会停在一个"上行全被拒"的界面上，而且没有任何提示。
            Game.Logger?.Error(Tag, "已被踢下线，回登录站点", null);
            _pendingLoginError = "已被踢下线（会话已失效，请重新登录）";
            Game.Event?.Emit(Events.Flow.LoginFailed, _pendingLoginError);
            GoTo(Stations.Login); // 进 Login 站点时会把上面这条原因带进面板显示
        }

        /// <summary>
        /// 等网关连接就绪（轮询 + 总超时，不无限等）。
        ///
        /// <para>
        /// <b>超时前会做一次主动恢复</b>（实测缺陷）：引擎的断线自动重连有**次数上限**
        /// （`GameConfig.MaxReconnectCount`，本项目 5 次）。服务端重启一次，这些尝试在客户端
        /// 察觉之前就用光了，此后 `IsConnected` 永远为 false、引擎**不会再自己重试** ——
        /// 而这里如果只是干等，表现就是「**登录按钮永久失效，必须重启客户端**」，且日志只有一句
        /// "等待网关连接超时"，看不出是重连预算耗尽还是服务端没起。
        /// 实测：服务端重启后 A 掉线 → 点登录 → 15s 超时；同一时刻另一个客户端在线正常。
        /// </para>
        /// <para>
        /// 恢复用的是引擎的**手动恢复入口** `Game.Net.Reconnect()`：它清退避计数、从主链重新开始，
        /// 并且**保留会话**（清理会话是 `Disconnect()` 的语义，会丢掉恢复凭证）。等第二次仍不成就
        /// 返回失败，由调用方把原因显示到面板上 —— 用户再点一次登录即可，不需要重启客户端。
        /// </para>
        /// </summary>
        private static async Task<bool> WaitConnectedAsync()
        {
            var waited = 0f;
            while (waited < ConnectWaitSeconds)
            {
                if (Game.Net != null && Game.Net.IsConnected) return true;
                await Task.Delay((int)(ConnectPollSeconds * 1000f));
                waited += ConnectPollSeconds;
            }
            if (Game.Net != null && Game.Net.IsConnected) return true;

            Game.Logger?.Warn(Tag,
                $"等待网关连接超时（{ConnectWaitSeconds:0}s）—— 尝试主动重连一次" +
                "（服务端刚重启时引擎的断线重连预算可能已经耗尽，此后它不会再自己重试）");
            try
            {
                Game.Net?.Reconnect();
            }
            catch (Exception e)
            {
                // 非预期分支：Reconnect 只在未记录地址/无线路计划时告警返回，不该抛。留痕后走失败路径。
                Game.Logger?.Error(Tag, $"主动重连抛异常：{e.GetType().Name}: {e.Message}", e);
                return false;
            }

            waited = 0f;
            while (waited < ConnectWaitSeconds)
            {
                if (Game.Net != null && Game.Net.IsConnected) return true;
                await Task.Delay((int)(ConnectPollSeconds * 1000f));
                waited += ConnectPollSeconds;
            }
            return Game.Net != null && Game.Net.IsConnected;
        }

        // ═════════════════════════ 人机对战 / 读条进图 ═════════════════════════

        private async void StartAiBattleAsync()
        {
            // ── 防重入闸（CR-F2）────────────────────────────────────────────
            // 两道都要拦：① 本方法自己在途（本次请求还没被 PushBattleStart 接手）
            //              ② 已在读条进图（上一局的开打推送已接手，场景正在加载）
            // 被拦下的点击**一条 MsgAiBattleStart 都不发**，并留痕（非预期分支必须可查）。
            if (_aiBattleInFlight)
            {
                Game.Logger?.Info(Tag,
                    "[AiBattle] DUP-DROP(gate) 人机对战已在途（等 PushBattleStart 接手），忽略重复点击 —— 未再发 MsgAiBattleStart");
                return;
            }
            if (_enteringBattle)
            {
                Game.Logger?.Info(Tag,
                    $"[AiBattle] DUP-DROP(entering) 正在读条进图（room={_enteringRoomId}），忽略请求 —— 未再发 MsgAiBattleStart");
                return;
            }

            SetAiBattleInFlight(true, "start-request");
            var handedOff = false;
            try
            {
                if (Game.Net == null)
                {
                    BattleStartFailed("网络模块未挂载");
                    return;
                }
                // ⛔ 不带 deck：服务端在 `len(deck) != 8` 时回落到**档案里存的卡组**
                //    （`server/game/logic/ai.go` 的 `onAiBattleStart`）。客户端复制一份"默认卡组"
                //    会立刻与配表/服务端漂移，而且卡组的权威在服务端。
                //
                // ⚠️ 这条日志是**防重入的运行时判据**（数值类 L3）：连点 N 次后它必须只出现 1 次。
                //    它落在 `Call` **之前** ⇒ 判的是"到底发了几条请求"（过程），不是"最后有没有开成局"（结果）。
                Game.Logger?.Info(Tag,
                    $"[AiBattle] SEND MsgAiBattleStart(id={MsgDef.AiBattleStart}) 请服务端建房 + AI 占座 + 立刻开打");
                var reply = await Game.Net.Call<AiBattleStartReply>(MsgDef.AiBattleStart, new AiBattleStartReq());
                if (reply == null || !reply.ok)
                {
                    BattleStartFailed(reply != null && !string.IsNullOrEmpty(reply.err) ? reply.err : "空回包");
                    return;
                }

                Game.Logger?.Info(Tag, $"[AiBattle] reply-ok room={reply.room_id} 等服务端推 PushBattleStart");
                // 收尾在 `OnBattleStartPush`：开打配置（自己的队伍 / 双方卡组 / 手牌 / 时间线）
                // 只有 `PushBattleStart` 才有，与真人开打走的是同一条路径。
                // ⇒ 闸保持到那条推送为止（见 `OnBattleStartPush`），此时 `handedOff` 不置位。
                handedOff = true;
            }
            catch (Exception e)
            {
                BattleStartFailed("人机对战启动失败：" + e.Message);
            }
            finally
            {
                // 失败（含超时 / 服务端拒绝）⇒ 立刻放闸，让玩家能重试；
                // 成功 ⇒ 闸交给 `OnBattleStartPush` 放（"回包已到、推送未到"那一瞬连点仍被拦）。
                if (!handedOff) SetAiBattleInFlight(false, "请求未成功，允许重试");
            }
        }

        /// <summary>
        /// 人机对战在途闸的**唯一写入点**（置位 / 放闸都打一行，便于从日志复原"谁在什么时候放闸"）。
        /// 值没变时不重复打，避免连点把日志刷满。
        /// </summary>
        private void SetAiBattleInFlight(bool inFlight, string reason)
        {
            if (_aiBattleInFlight == inFlight) return;
            _aiBattleInFlight = inFlight;
            // 标记用 ASCII（HOLD / RELEASE）⇒ 运行时日志可按"过程"计数（数值类判据，不看截图）。
            Game.Logger?.Info(Tag, $"[AiBattle] gate={(inFlight ? "HOLD" : "RELEASE")} reason={reason}");
        }

        private void BattleStartFailed(string reason)
        {
            Game.Logger?.Error(Tag, "开一局失败：" + reason, null);
            Game.Event?.Emit(Events.Battle.StartFailed, reason);
        }

        /// <summary>服务端开打推送（`PushBattleStart`，Reliable）。</summary>
        private void OnBattleStartPush(NetCtx ctx)
        {
            var start = ctx.Bind<BattleStartNotify>();
            if (start == null)
            {
                // 非预期分支：协议体反序列化失败。`Bind` 失败返回 null 而不抛，容易被静默吞掉。
                Game.Logger?.Error(Tag, "PushBattleStart 反序列化失败（Bind 返回 null），未进入对局");
                // 开打在途闸必须放：否则玩家会卡在"按钮点不动"（这条推送是收官信号，收不到就没人放闸）。
                SetAiBattleInFlight(false, "开打推送反序列化失败");
                return;
            }
            Game.Logger?.Info(Tag,
                $"收到开打推送 room={start.room_id} my_team={start.my_team} " +
                $"hand={start.hand_a?.Length ?? 0} 张 deck_a={start.deck_a?.Length ?? 0} 张");
            RequestEnterBattle(start.room_id, start);
            // 交接点：进图（读条 + 场景加载）自有 `_enteringBattle` 幂等闸，人机对战闸到此为止。
            SetAiBattleInFlight(false, "PushBattleStart 已接手进图");
        }

        /// <summary>
        /// 读条 + 场景加载的**唯一实现**：开 LoadingPanel → `Game.Scene.Load`（真进度写进面板）→
        /// 关面板 → 切站点。`onFinished` 用于清掉调用方的"在途"标记。
        /// </summary>
        private void EnterSceneAsync(string sceneName, string hint, string targetStation, Action onFinished)
        {
            if (Game.UI == null || Game.Scene == null)
            {
                Game.Logger?.Error(Tag, $"表现域未挂载（Game.UI / Game.Scene 为空），无法加载场景 {sceneName}");
                onFinished?.Invoke();
                return;
            }

            Game.UI.CloseAll();
            Game.UI.Open<LoadingPanel>(hint);

            Game.Scene.Load(sceneName, progress =>
            {
                // 真进度：`Game.Scene.Load` 的 progress 回调是 0~1（引擎在 progress ≥ 0.9 时放行激活，
                // 所以最后 10% 会在激活那一刻直接跳到完成 —— 面板显示的百分比如实反映这一点）。
                var panel = Game.UI.Get<LoadingPanel>();
                if (panel != null) panel.SetProgress(progress);
            }, () =>
            {
                onFinished?.Invoke();
                Game.Logger?.Info(Tag, $"场景 {sceneName} 加载完成 → 站点 {targetStation}");
                Game.UI.Close<LoadingPanel>();
                GoTo(targetStation);
            });
        }

        // ═════════════════════════ 设置 / 退出 ═════════════════════════

        private void OpenSettings()
        {
            if (Game.UI == null) return;
            // 传一份**现读**的快照进去：面板不能自己读设置（那要引 CR.Module）。
            var snapshot = _settings != null ? _settings.Snapshot() : null;
            Game.UI.Open<SettingsPanel>(snapshot);
        }

        private void Quit()
        {
            Game.Logger?.Info(Tag, "退出游戏");
#if UNITY_EDITOR
            // 编辑器里 `Application.Quit()` 不生效（Play 模式不会退出）—— 停掉 Play 才是编辑器的"退出"。
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
