using CloverEngine;
using CR.Module.Flow;
using CR.Module.Settings;
using CR.UI;
using UnityEngine;

namespace CR.App
{
    /// <summary>
    /// 唯一的组装点：挂 `Main` 场景里的根节点上，负责按契约次序拉起引擎并把流程交给 <see cref="AppFlow"/>。
    ///
    /// <para>
    /// <b>初始化次序是硬契约</b>（`docs/client-api-reference.md` §1）：`Game.Launch` 不联网、
    /// 也不挂资源与表现域，漏一步的后果都是**静默**的 ——
    /// 漏 `CloverInput.Init()` ⇒ 面板能开但按钮全点不动（没有 EventSystem）；
    /// 漏 `CloverRes.Init` ⇒ `Game.Res` 恒 null，图永远加载不出来。
    /// 因此这里逐条按序、且每条都在下面写明"漏了会怎样"。
    /// </para>
    /// <para>
    /// <b>⛔ 本组件会在场景切换时被销毁并重建</b>：`Main` → `Battle01` 会卸载 `Main`，
    /// 所以 `OnDestroy` 里**绝不能**调 `Game.Shutdown()`（那会在进对局的一瞬间把网络、
    /// 日志、UI 全拆掉）。跨场景要活下来的东西（流程状态、事件订阅）都放在
    /// <see cref="AppFlow"/> 里 —— 它是纯 C# 类 + 静态单例，不随场景销毁。
    /// </para>
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private const string Tag = "Bootstrap";

        private SettingsManager _settings;

        /// <summary>
        /// **本轮引擎**的事件总线（= `Game.Launch` 建出来的那一条）。
        /// <para>
        /// <b>为什么需要它（根因）</b>：`Start` 的重入判定原先只看 `Game.IsRunning` 这个**裸静态 bool** ——
        /// 它只说明"某个时刻引擎被拉起过"，**不标识是哪一轮引擎**。引擎每轮 `Game.Launch` 都会
        /// **新建** `EventBus`（引擎 `Game.cs:381`），而本工程**关闭了域重载**（Enter Play Mode Options）
        /// ⇒ 静态字段会跨轮存活 ⇒ `Game.IsRunning` 可能在"本轮其实没被拉起 / 托管侧被整体复位"时仍为
        /// true —— 那就是 AR1 实测出的**僵尸会话**（托管侧复位 `ThreadAbort` + TCP send loop aborted、
        /// Unity 对象留存 ⇒ `Bootstrap.Start` 不再跑、引擎不再拉起、5 颗按钮的运行时监听者全部归零，
        /// 症状 = "UI 在屏、点什么都没反应"，且没有一条报错）。
        /// 这种状态下走重入分支会把 `Game.Launch` / `CloverInput.Init` / `runInBackground` /
        /// `CloverRes.Init` / `CloverNet.Init` / `PanelFactory.Install` **全部跳过**。
        /// </para>
        /// <para>
        /// 判定口径与全工程已合格的 5 处**逐字一致**（`RoomManager` / `DeckManager` / `BattleManager` /
        /// `BattleUiHost` / `BgmView` 都是 `ReferenceEquals(bus, _bus)`）：**按当前总线对象标识本轮引擎**。
        /// </para>
        /// </summary>
        private static IEventBus _bus;

        private void Start()
        {
            // ⚠️ 只有"引擎在跑 **且** 跑的还是我们这轮记下的那条总线"才算合法的重入；
            //    任一不成立（总线为空 = 引擎被拆 / 总线换了对象 = 新的一轮）都必须走完整拉起路径。
            if (Game.IsRunning && ReferenceEquals(Game.Event, _bus))
            {
                // 第二次进 Main（从 Battle01 回主菜单）：同一个引擎、同一条总线还活着，
                // 所以**不许**再 Launch / Init 一遍（引擎对重复初始化会 Warn 并忽略，
                // 但那是"绕过正确路径"，不该依赖它）。这里只需保证面板供给者与流程实例在 —— 两者都幂等。
                PanelFactory.Install();
                AppFlow.EnsureCreated(_settings);
                Game.Logger?.Info(Tag, "引擎已在运行且总线未变：仅确保流程存在（从对局返回主菜单的重入路径）");
                return;
            }

            LaunchEngine();
            // 记下"本轮引擎"的标识（必须在 LaunchEngine 之后：Game.Launch 在这一步才建出总线）。
            _bus = Game.Event;

            _settings = new SettingsManager();
            _settings.Init();

            AppFlow.EnsureCreated(_settings);

            Game.Logger?.Info(Tag, $"启动完成：addr={Cfg.Server.addr} tls={Cfg.Server.tls} auth={Cfg.Server.auth_addr}");
        }

        /// <summary>按 `docs/client-api-reference.md` §1 的次序初始化引擎与各子系统。</summary>
        private static void LaunchEngine()
        {
            // ★★ 朝向治理（T1）：**必须是本方法的第一件事** —— `UIManager` 在 `Game.Launch` 的挂载钩子里
            //    构造（`CloverPresentation.Init` → `Game.AttachUI(new UIManager())`），而它在构造时
            //    **只读一次** CanvasScaler 参数 ⇒ 写在 Launch 之后等于没写（且不报错）。
            //
            //    出处：A =《皇室战争》是**竖版(portrait)** 游戏（官方截图宽 < 高）。本项目原先是横版
            //    （`ProjectSettings.asset:11` `defaultScreenOrientation: 4` = LandscapeRight），
            //    用户 2026-09-20 原话：「竖版游戏，你用横板ui，真有你的」。
            //
            //    ① 参考分辨率 = 1080×1920（竖版设计画布；与 `CrUiStyle.DesignW/DesignH` 同一口径）。
            //    ② match = 0（匹配宽度）：**画布宽恒为 1080**。竖屏下 match=0.5 会按宽高比插值出
            //       ≈1.10xx 的**小数**缩放 ⇒ UI 像素不是整数倍、文字与 1px 描边发糊；
            //       取 0 后不同竖屏分辨率都是整数倍缩放、UI 像素对齐。
            //    ③ `Screen.orientation`：运行时（含编辑器 Play）的屏幕方向；真机默认方向另有工程设置
            //       （`PlayerSettings.defaultInterfaceOrientation`，见 ProjectSettings 的 player 设置）。
            CloverPresentation.ReferenceResolution = new Vector2(1080f, 1920f);
            CloverPresentation.MatchWidthOrHeight = 0f;
            Screen.orientation = ScreenOrientation.Portrait;

            // ① 引擎（不联网）。UseTls 必须与服务端 `gateway.tcp_tls_disabled` **相反**
            //    （本项目服务端 tcp_tls_disabled=true ⇒ 客户端 tls=false），配反的症状是"连上就断"。
            Game.Launch(new GameConfig
            {
                ServerAddr = Cfg.Server.addr,
                WsAddr = null, // 原生客户端走裸 TCP；WsAddr 只对 WebGL 有意义
                UseTls = Cfg.Server.tls,
                LogDir = "logs",
                DataDir = null,
                // ⛔ 这里**刻意不设** `ResourceRoot`：资源根不是 GameConfig 决定的，
                //    而是 CloverRes.Init(...) 的参数（见下面第 ④ 步），且引擎不读 config.ResourceRoot
                //    （全引擎只有 `Game.cs:87` 的字段声明，没有任何消费点）。
                //    写一个"看着像资源根"的字面量只会误导下一个人去改错地方。
                CallTimeoutSeconds = Cfg.Server.call_timeout,
                MaxReconnectCount = Cfg.Server.max_reconnect_count,
            });

            // ② 输入 + EventSystem —— ★ 必须在建 UI 之前，晚了按钮全点不动且没有报错。
            CloverInput.Init();

            // ②' 失焦继续跑。★ 这是**联机游戏的正确行为**，不是测试取巧：
            //     服务端 tick 不会因为你切窗口而停，客户端若停摆，切回来时已经错过整局。
            //     实测（本轮踩到，烧掉一小时）：默认 false 时窗口不在前台 ⇒ 玩家循环**完全冻结** ——
            //     `Time.frameCount` 恒为 1、`Time.time` 恒为 0、`Fsm.Current` 永远停在 `Boot`，
            //     而且**一条报错都没有**（`consoleErrors=0`），是最难查的一类现象。
            //     命令行驱动 Play 时编辑器必然可能不在前台，所以这里必须显式打开。
            Application.runInBackground = true;

            // ③ 网络（显式；Launch 不联网）。网关 **TCP** 口 8002，不是 8001（8001 是 WS 口，
            //    配错的现象是"连上 → 秒断 → 重连耗尽被踢 → 之后所有 Call 超时"）。
            CloverNet.Init(Cfg.Server.addr, string.IsNullOrEmpty(Cfg.Server.udp_addr) ? null : Cfg.Server.udp_addr);

            // ④ 资源。★ 参数是 **Resources 下的子目录前缀**（`CloverRes.cs:32` 与
            //    `ResourceBackend.cs:227` 的拼法是 `root + "/" + path`），空串 = 直接以 `Assets/Resources` 为根。
            //    ⛔ 不要照抄 `docs/client-api-reference.md` §1 的 `CloverRes.Init("Assets/Resources")`：
            //    那会让所有加载去找 `Resources/Assets/Resources/...`，一个资源都命中不了（已回报主 agent）。
            CloverRes.Init(string.Empty);

            // ⑤ 配表：**本项目客户端不落地 tsv**，因此这一步是"显式声明不接入"而不是一次调用。
            //    理由：60 张卡的名称/费用/稀有度/图集键随 `GetCardPoolReply` 从服务端下发，
            //    数值的权威在服务端 `game/table`；客户端再存一份必然漂移。
            //    ⛔ 也不要写 `CloverData.InitDataTable(CloverTable.Dir)`：`CloverTable.Dir` 在
            //    `CloverTable.LoadAll` 成功之前恒为 null，传进去只会让引擎打一条 Error（已回报主 agent）。
            Game.Logger?.Info(Tag, "配表未接入：客户端不落地 tsv，卡池/卡组数据一律走服务端协议");

            // ⑥ 账号服地址 —— 不设的话 LoginAsync/SignupAsync 直接抛 InvalidOperationException。
            CloverAuth.AuthAddr = Cfg.Server.auth_addr;

            // ⑦ 面板供给者：**必须在第一次 Game.UI.Open 之前**装好，否则那一次会去
            //    `Resources/UI/{类名}` 找预制体并报 "Panel prefab not found"。
            PanelFactory.Install();

            // ⑧ 帧节奏（引擎 `FramePacingPolicy`）：帧率上限恒定 + 垂直同步与显示器刷新率对齐。
            //    ★ 为什么必须在进 Boot 站点之前钉：本工程是"每帧按 dt 插值推进"的表现链
            //    （`View/BattleViewRoot.cs` 10Hz 权威快照 → 60FPS 插值），**帧间隔不匀会直接变成
            //    画面推进不匀**。引擎 `Runtime/Presentation/FramePacing.cs` 的类注释记着真机 A/B：
            //    vSync 关 + 硬性 60fps 在 100 Hz 面板上 ⇒ dt 8.5~62.6 ms（sd 5.0）、
            //    相机每帧推进量 sd = 2.97 px、corr(纵向偏差, dt−均值) = 0.923 ⇒ 不匀就是帧时间造成的。
            //    ⛔ 只用引擎这三个静态入口（Recommend / Pin / Describe）：本工程**不写第二份**
            //    `Application.targetFrameRate =` 或 `QualitySettings.vSyncCount =`。
            //    ⚠️ `vSyncCount > 0` 时平台忽略 `targetFrameRate` 属**预期**，⛔ 别当成"没生效"。
            var refreshReadable = FramePacingPolicy.Recommend(out var pacingFps, out var pacingVSync, out var pacingHz);
            var pacingPinned = FramePacingPolicy.Pin(pacingFps, pacingVSync,
                out var readBackFps, out var readBackVSync, out var pacingError);
            Game.Logger?.Info(Tag, FramePacingPolicy.Describe(pacingFps, pacingVSync, "启动", readBackFps, readBackVSync));
            if (!refreshReadable)
                Game.Logger?.Warn(Tag, $"刷新率读不到（无头 / 平台不提供，readHz={pacingHz:0}）⇒ 帧节奏走兜底口径");
            if (!pacingPinned)
                Game.Logger?.Warn(Tag,
                    $"帧节奏 Pin 未完全生效（{pacingError ?? "读回值与目标不一致"}）：" +
                    $"目标 targetFrameRate={pacingFps} vSyncCount={pacingVSync} ⇒ " +
                    $"读回 targetFrameRate={readBackFps} vSyncCount={readBackVSync}");
        }

        private void OnApplicationQuit()
        {
            // 退出应用（编辑器里是停 Play）时的收尾。注意这里**不是**场景卸载路径：
            // 场景卸载（进对局）不会走到这里，因此引擎不会在进对局时被拆掉。
            AppFlow.Instance?.Shutdown();
            PanelFactory.Dispose();
            Game.Shutdown();
        }
    }
}
