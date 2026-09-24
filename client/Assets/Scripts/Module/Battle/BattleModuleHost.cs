using CloverEngine;
using UnityEngine;

namespace CR.Module.Battle
{
    /// <summary>
    /// 对局模块的装载点：把 <see cref="BattleManager"/> 挂到引擎的**启动钩子**上。
    /// 写法与 <c>Module/Room/RoomModuleHost.cs</c> 逐条同源（⛔ 不在 `App/Bootstrap.cs` 里加一行）。
    ///
    /// <para>
    /// <b>为什么用引擎的启动钩子</b>（`Game.RegisterLaunchHook`，`Game.cs:661-671`，
    /// 注释写明"可安全配合 `RuntimeInitializeOnLoadMethod` 使用"，并在 `Launch` 内 `RunLaunchHooks()`，
    /// `Game.cs:401-405`）：① 它早于第一个面板打开，模块一定就绪；② 比"在 `Update` 里轮询
    /// `Game.IsRunning`"确定（不每帧白跑）。⛔ 不改 `Bootstrap` 是有意的。
    /// </para>
    /// <para>
    /// <b>时序</b>：`RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 在进入 Play 时就运行
    /// （早于首个场景里 `Bootstrap.Start` 的 `Game.Launch`），此处只是**登记回调**；
    /// `Game.Launch` 完成核心子系统（含 `Game.Event`）后由 `RunLaunchHooks()` 调用本回调。
    /// ⚠️ 但**推送处理器**那时还挂不上：`Game.OnMsg` 要求 router 已挂，而 router 由 `CloverNet.Init`
    /// 创建（在 `Game.Launch` **之后**）⇒ 由 `BattleManager` 在站点切换 / 上行操作时延后再注册
    /// （那里有详细说明）。
    /// </para>
    /// <para>
    /// <b>重复登记是安全的</b>：`RegisterLaunchHook` 按 key 覆盖（`Game.cs:669-671`），
    /// 且 `BattleManager.Install` 以「事件总线的对象标识」判幂等。
    /// </para>
    /// </summary>
    public static class BattleModuleHost
    {
        /// <summary>启动钩子的注册键（同 key 重复注册会覆盖，因此可安全重复登记）。</summary>
        private const string LaunchHookKey = "CR.Module.Battle";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, () => BattleManager.EnsureCreated());
        }
    }
}
