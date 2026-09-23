using CloverEngine;
using UnityEngine;

namespace CR.Module.Room
{
    /// <summary>
    /// 房间模块的装载点：把 <see cref="RoomManager"/> 挂到引擎的**启动钩子**上。
    ///
    /// <para>
    /// <b>为什么要这么绕</b>：`Bootstrap`（唯一组装点，`App/Bootstrap.cs`）与 `AppFlow` 都是
    /// agent-05 的冻结产出，本片⛔不许改；而本项目的管理器必须有人创建。可选路径：
    /// ① 改 `Bootstrap` 加一行 —— 越界；
    /// ② 在 `Update` 里轮询 `Game.IsRunning` 再初始化 —— 能用，但每帧白跑、且"什么时候装好"不确定；
    /// ③ **引擎自己的启动钩子**（`Game.RegisterLaunchHook`，`Game.cs:661-671`）—— 选它。
    /// </para>
    /// <para>
    /// <b>时序</b>：`RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 在进入 Play 时就运行
    /// （早于首个场景里 `Bootstrap.Start` 的 `Game.Launch`），此处只是**登记回调**；
    /// `Game.Launch` 完成核心子系统（含 `Game.Event`）后由 `RunLaunchHooks()` 调用
    /// （`Game.cs:401-405`），因此模块一定早于第一个面板打开就绪。
    /// ⚠️ 但**推送处理器**那时还挂不上：`Game.OnMsg` 要求 router 已挂，而 router 由
    /// `CloverNet.Init` 创建（在 `Game.Launch` **之后**）⇒ 由 `RoomManager.EnsurePushHandlers` 延后到
    /// 首次房间操作 / 站点切换时再注册（那里有详细说明）。
    /// </para>
    /// <para>
    /// <b>重复登记是安全的</b>：`RegisterLaunchHook` 按 key 覆盖（`Game.cs:669-671`），
    /// 且 `RoomManager.Install` 以「事件总线的对象标识」判幂等。
    /// </para>
    /// </summary>
    public static class RoomModuleHost
    {
        /// <summary>启动钩子的注册键（同 key 重复注册会覆盖，因此可安全重复登记）。</summary>
        private const string LaunchHookKey = "CR.Module.Room";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, () => RoomManager.EnsureCreated());
        }
    }
}
