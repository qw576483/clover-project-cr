using CloverEngine;
using UnityEngine;

namespace CR.Module.Deck
{
    /// <summary>
    /// 卡组模块的装载点：把 <see cref="DeckManager"/> 挂到引擎的**启动钩子**上。
    ///
    /// <para>
    /// <b>为什么要这么绕</b>：`Bootstrap`（唯一组装点）与 `AppFlow` 都不经手本模块的创建，
    /// 而本项目的管理器又必须有人创建。可选路径有三条：
    /// ① 改 `Bootstrap` 加一行 —— 会把组装点与模块耦合起来；
    /// ② 在 `Update` 里轮询 `Game.IsRunning` 再初始化 —— 能用，但每帧白跑、且"什么时候装好"不确定；
    /// ③ **引擎自己的启动钩子**（<c>Game.RegisterLaunchHook</c>，`Game.cs:661-671`，注释写明
    /// 「供上层模块把自己挂接到门面」「可安全配合 RuntimeInitializeOnLoadMethod 使用」）—— 选它。
    /// </para>
    /// <para>
    /// <b>时序为什么成立</b>：`RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 在进入 Play 时
    /// 就运行（早于首个场景里 `Bootstrap.Start` 的 `Game.Launch`），此处只是"登记回调"；
    /// 真正的 `EnsureCreated()` 在 `Game.Launch` 完成核心子系统（含 `Game.Event`）之后、
    /// 由 `RunLaunchHooks()` 调用（`Game.cs:401-405`）。因此模块一定早于第一个面板打开就绪。
    /// </para>
    /// <para>
    /// <b>为什么钩子里的 lambda 每次 Launch 都会重新装订阅</b>：`Game.Event` 是 Launch 新建的对象，
    /// 先前的订阅随它消失；`DeckManager.Install()` 内部用「先 Off 再 On」保证不叠加。
    /// </para>
    /// </summary>
    public static class DeckModuleHost
    {
        /// <summary>启动钩子的注册键（重复注册会覆盖，因此可安全重复登记）。</summary>
        private const string LaunchHookKey = "CR.Module.Deck";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            Game.RegisterLaunchHook(LaunchHookKey, () => DeckManager.EnsureCreated());
        }
    }
}
