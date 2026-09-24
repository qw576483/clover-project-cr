using CloverEngine;

namespace CR.UI
{
    /// <summary>
    /// 面板供给者（架构契约 **D2 方案 ①**）：替换 <see cref="CloverPresentation.PanelProvider"/>，
    /// 让 <c>Game.UI.Open&lt;T&gt;</c> 不再依赖 `Resources/UI/{类名}.prefab` 资产。
    ///
    /// <para>
    /// <b>本类转调引擎件 <see cref="RuntimePanelProvider"/></b>
    /// （`clover-client-unity-engine/Runtime/Presentation/RuntimePanelProvider.cs`；出处 =
    /// `client/Assets/Scripts/UI/PanelFactory.cs:41-216`）：<see cref="Install"/> / <see cref="Dispose"/>
    /// 是项目侧的调用口径（另有 <see cref="IsInstalled"/> / <see cref="InstalledBus"/> 两个只读诊断属性），
    /// 内部持一个引擎实例（扫描范围 = 本类所在程序集，即 CR 业务程序集）。
    /// 下列口径全部由引擎件执行，本工程照用 ——
    /// 「按 `typeName` 反射建「简单类名 → 类型」表」「模板延后一帧销毁（`Timer.After(0f, …)`）」
    /// 「按**当前总线对象**做幂等安装」「空类名 / 找不到类型 ⇒ 留 Error 并返回 null（面板不打开、不崩）」。
    /// </para>
    ///
    /// <para>
    /// <b>为什么可行（依据引擎源码）</b>：`UIManager.Open&lt;T&gt;` 的面板获取链路是
    /// 「拿一个 GameObject（`CloverPresentation.PanelProvider(typeName)`，默认实现是
    /// `Resources` 加载 `"UI/" + typeName`）→ `Instantiate` → `GetComponent&lt;T&gt;()`」
    /// （`Runtime/Presentation/UI.cs:119-137`），而 `PanelProvider` 的类型正是
    /// `public static Func&lt;string, GameObject&gt; PanelProvider { get; set; }`
    /// （`Runtime/Presentation/CloverPresentation.cs:69`）——
    /// 一个**公开可写**的函数槽。因此「运行时 new GameObject + AddComponent&lt;T&gt;」这条路径成立，
    /// 且不需要任何 `.prefab` 资产（D2 里 ①「零资产」的收益）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么按 <c>typeName</c> 反射找类型</b>：函数槽只给得到字符串。若把「类名 → Type」写成
    /// `switch`，每加一个面板都要回来改本文件 —— 反射让**加面板 = 只加自己的文件**，接口不用动。
    /// </para>
    ///
    /// <para>
    /// <b>为什么模板要延后一帧销毁</b>：`Instantiate` 由引擎在拿到返回值后**同步**调用，
    /// 所以模板必须活到那一行之后。`Game.Timer.After(0f, …)` 的回调在**下一个** Tick 才触发
    /// （`Timer.Tick` 的判据是 `e.Elapsed &lt; e.Delay`，Delay=0 时首次 Tick 才满足），
    /// 因此那一刻克隆已经完成，场景里不留残留物。⛔ 不要改成 `Object.Destroy` 当场调用：
    /// 那会让对象在 `Instantiate` 之前就变成 Unity 的「假 null」并抛异常。
    /// </para>
    ///
    /// <para>
    /// <b>面板实现约定</b>：面板**在 <c>OnOpen</c> 里**用 `UIFactory` 自建视觉树，
    /// ⛔ 不要在 `Awake`/`Start` 里建 —— 模板对象也会走一遍 `Awake`（`AddComponent` 当场触发），
    /// 在 `Awake` 里建树会让模板也建一份（白做一遍、还多一次资源加载）。
    /// </para>
    ///
    /// <para>
    /// <b>IL2CPP（发布前必读）</b>：反射扫程序集会被代码裁剪打掉 ⇒ 面板类型"没有任何静态引用"
    /// 就会被裁掉、`GetTypes()` 里没有它们、面板**静默开不出来**。发布版必须用
    /// `Assets/link.xml` 保留面板类型（或至少保留 `CR` 程序集）。
    /// </para>
    /// </summary>
    public static class PanelFactory
    {
        /// <summary>
        /// 引擎件实例（惰性建一次）；扫描范围 = 本类所在程序集。
        /// 定时器不给 ⇒ 引擎件回落 <c>Game.Timer</c>。
        /// </summary>
        private static RuntimePanelProvider _provider;

        private static RuntimePanelProvider Provider
        {
            get { return _provider ??= new RuntimePanelProvider(typeof(PanelFactory).Assembly); }
        }

        /// <summary>
        /// 装上工厂（幂等）。必须在 <c>Game.Launch</c> 之后调用（此前 `CloverPresentation` 还没挂载），
        /// 且必须在**第一次 `Game.UI.Open`** 之前 —— 晚装只会影响之后打开的面板，
        /// 早先那次会因为找不到 `Resources/UI/{类名}` 而报「Panel prefab not found」。
        /// </summary>
        public static void Install()
        {
            Provider.Install(Game.Event);
        }

        /// <summary>还原函数槽并清掉可能的残留模板（关闭时调；只影响之后打开的面板）。</summary>
        public static void Dispose()
        {
            Provider.Uninstall();
        }

        /// <summary>
        /// 当前是否装着本工程的供给者（且引擎仍是安装时那一轮）—— 只读诊断用，转发引擎件同名的公开属性。
        /// </summary>
        public static bool IsInstalled
        {
            get { return Provider.IsInstalled; }
        }

        /// <summary>
        /// 安装时那一轮的引擎总线（未安装为 <c>null</c>）—— 只读诊断用，转发引擎件同名的公开属性。
        /// </summary>
        public static IEventBus InstalledBus
        {
            get { return Provider.InstalledBus; }
        }
    }
}
