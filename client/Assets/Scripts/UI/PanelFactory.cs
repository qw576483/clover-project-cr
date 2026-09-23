using System;
using System.Collections.Generic;
using System.Reflection;
using CloverEngine;
using UnityEngine;

namespace CR.UI
{
    /// <summary>
    /// 面板供给者（架构契约 **D2 方案 ①**）：替换 <see cref="CloverPresentation.PanelProvider"/>，
    /// 让 <c>Game.UI.Open&lt;T&gt;</c> 不再依赖 `Resources/UI/{类名}.prefab` 资产。
    ///
    /// <para>
    /// <b>为什么可行（已核对引擎源码，不是猜的）</b>：`UIManager.Open&lt;T&gt;` 的面板获取链路是
    /// 「拿一个 GameObject（`CloverPresentation.PanelProvider(typeName)`，默认实现是
    /// `Resources` 加载 `"UI/" + typeName`）→ `Instantiate` → `GetComponent&lt;T&gt;()`」
    /// （`Runtime/Presentation/UI.cs:119-137`），而 `PanelProvider` 的类型正是
    /// `public static Func&lt;string, GameObject&gt; PanelProvider { get; set; }`
    /// （`Runtime/Presentation/CloverPresentation.cs:69`）——
    /// 一个**公开可写**的函数槽。因此「运行时 new GameObject + AddComponent&lt;T&gt;」这条路径成立，
    /// 且不需要任何 `.prefab` 资产（D2 里 ①「零资产」的收益）。
    /// </para>
    /// <para>
    /// <b>为什么按 <c>typeName</c> 反射找类型</b>：函数槽只给得到字符串。若把「类名 → Type」写成
    /// `switch`，agent-06/07/08 每加一个面板都要回来改本文件（本文件属 agent-05 的地盘）——
    /// 反射让**加面板 = 只加自己的文件**，接口不用动。
    /// </para>
    /// <para>
    /// <b>为什么模板要延后一帧销毁</b>：`Instantiate` 由引擎在拿到返回值后**同步**调用，
    /// 所以模板必须活到那一行之后。`Game.Timer.After(0f, …)` 的回调在**下一个** Tick 才触发
    /// （`Timer.Tick` 的判据是 `e.Elapsed &lt; e.Delay`，Delay=0 时首次 Tick 才满足），
    /// 因此那一刻克隆已经完成，场景里不留残留物。⛔ 不要改成 `Object.Destroy` 当场调用：
    /// 那会让对象在 `Instantiate` 之前就变成 Unity 的「假 null」并抛异常。
    /// </para>
    /// <para>
    /// <b>面板实现约定（本片 D1）</b>：面板**在 <c>OnOpen</c> 里**用 `UIFactory` 自建视觉树，
    /// ⛔ 不要在 `Awake`/`Start` 里建 —— 模板对象也会走一遍 `Awake`（`AddComponent` 当场触发），
    /// 在 `Awake` 里建树会让模板也建一份（白做一遍、还多一次资源加载）。
    /// </para>
    /// </summary>
    public static class PanelFactory
    {
        private const string Tag = "PanelFactory";

        /// <summary>类名 → 面板类型；惰性构建一次。</summary>
        private static Dictionary<string, Type> _panelTypes;

        /// <summary>已交出但还没被延后销毁的模板（正常是空的；仅在 Timer 不可用时兜底，见 <see cref="Dispose"/>）。</summary>
        private static readonly List<GameObject> _pending = new();

        /// <summary>
        /// 装 <c>PanelProvider</c> 时那一轮的引擎总线 —— 也就是"**本轮引擎**"的标识。
        /// <para>
        /// <b>为什么不能用裸 <c>bool _installed</c>（根因）</b>：`CloverPresentation.PanelProvider` 是**静态**
        /// 函数槽，而本工程**关闭了域重载** ⇒ 裸 bool 会跨轮存活：新一轮 Play 里它仍是 true、
        /// `Install()` 直接早退，而 `CloverPresentation` 侧的槽可能已随引擎重建被清掉 ⇒
        /// 之后每次 `Game.UI.Open` 都去找 `Resources/UI/{类名}.prefab` 并失败（表现为"面板打不开 / 点了没反应"）；
        /// 这正是 AR1 实测的**僵尸会话**（托管侧被整体复位、静态残留、按钮运行时监听者全为 0）的一个成因面。
        /// </para>
        /// <para>
        /// 口径与工程其余 5 处**逐字一致**（`RoomManager` / `DeckManager` / `BattleManager` /
        /// `BattleUiHost` / `BgmView`）：按**当前总线对象**标识本轮（`Game.Launch` 每轮新建 `EventBus`，
        /// 引擎 `Game.cs:381`）。
        /// </para>
        /// </summary>
        private static IEventBus _installedBus;

        /// <summary>
        /// 装上工厂（幂等）。必须在 <c>Game.Launch</c> 之后调用（此前 `CloverPresentation` 还没挂载），
        /// 且必须在**第一次 `Game.UI.Open`** 之前 —— 晚装只会影响之后打开的面板，
        /// 早先那次会因为找不到 `Resources/UI/{类名}` 而报「Panel prefab not found」。
        /// </summary>
        public static void Install()
        {
            var bus = Game.Event;
            if (bus == null)
            {
                // 非预期分支：Install 早于 Game.Launch（本方法的契约就是"必须在其之后"）。
                // 留痕，别静默 —— 否则表现为"所有面板都去找 prefab 且找不到"。
                Game.Logger?.Error(Tag, "Game.Event 为空（Install 早于 Game.Launch？），面板供给者未装上");
                return;
            }

            if (ReferenceEquals(bus, _installedBus)) return; // 同一条总线：已装过

            if (_installedBus != null)
            {
                // 换总线 = 引擎重新 Launch（编辑器里每次 Play 都会发生）。只在这一刻报一次，不刷屏。
                Game.Logger?.Info(Tag, "检测到新的事件总线（引擎重新 Launch），重装面板供给者");
            }

            _installedBus = bus;
            CloverPresentation.PanelProvider = Provide;
            Game.Logger?.Info(Tag, "面板供给者已替换为运行时构建（D2 ①：无需 Resources/UI/*.prefab 资产）");
        }

        /// <summary>还原函数槽并清掉可能的残留模板（关闭时调；只影响之后打开的面板）。</summary>
        public static void Dispose()
        {
            if (_installedBus != null)
            {
                CloverPresentation.PanelProvider = null;
                _installedBus = null;
            }

            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i] != null) UnityEngine.Object.Destroy(_pending[i]);
            }
            _pending.Clear();
        }

        /// <summary>
        /// 引擎回调：按类名造一个"只带 RectTransform + 面板组件"的空 GameObject 交给 `UIManager` 克隆。
        /// 找不到类型时返回 null —— 引擎会打它自己的 Error 并**不打开面板**（不静默、不崩）。
        /// </summary>
        private static GameObject Provide(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                Game.Logger?.Error(Tag, "PanelProvider 收到空类名，无法造面板");
                return null;
            }

            var type = ResolvePanelType(typeName);
            if (type == null)
            {
                Game.Logger?.Error(Tag,
                    $"找不到面板类型 `{typeName}`（须是 CR 程序集里 UIPanel/MonoBehaviour 的子类）；面板不会打开");
                return null;
            }

            // 带 RectTransform：面板根要被挂进 Canvas 的层级节点下，且其内部的 Image/Text 依赖
            // RectTransform 父链做布局。只 new GameObject 的话根节点是普通 Transform，整棵 UI 树会错位。
            var template = new GameObject(typeName, typeof(RectTransform));
            template.AddComponent(type);

            if (Game.Timer != null)
            {
                GameObject captured = template;
                // 延后一帧销毁（见类注释：必须活过引擎随后的 Instantiate 那一行）。
                Game.Timer.After(0f, () =>
                {
                    if (captured != null) UnityEngine.Object.Destroy(captured);
                });
            }
            else
            {
                // 非预期分支：Timer 缺失（理论上只在 Launch 之前）。留痕并把它记进兜底清单，
                // 由 Dispose 收尾 —— 绝不静默留下一个永远不销毁的对象。
                Game.Logger?.Warn(Tag, "Game.Timer 为空，模板无法延后销毁，改由 Dispose 兜底回收");
                _pending.Add(template);
            }

            return template;
        }

        /// <summary>按类名（先精确匹配简单名，再匹配完整名）在 CR 程序集里找面板类型。</summary>
        private static Type ResolvePanelType(string typeName)
        {
            var map = _panelTypes ??= BuildPanelTypeMap();

            if (map.TryGetValue(typeName, out var type)) return type;

            // 兜底：调用方传了完整名（`CR.UI.Panels.BootPanel`）时也认。
            foreach (var kv in map)
            {
                if (kv.Value.FullName == typeName) return kv.Value;
            }
            return null;
        }

        /// <summary>
        /// 扫描 CR 程序集，建立「简单类名 → 类型」。只收能当面板用的类型
        /// （MonoBehaviour + 实现 `IUIPanel` + 非抽象）。
        /// </summary>
        private static Dictionary<string, Type> BuildPanelTypeMap()
        {
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type[] types;
            try
            {
                types = typeof(PanelFactory).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 有类型加载失败时仍尽量把能用的收进来（GetTypes 会整体抛，Types 里保留成功的那部分）。
                types = ex.Types;
                Game.Logger?.Warn(Tag, $"扫描面板类型时部分类型加载失败（{ex.Message}），按已加载的部分继续");
            }

            if (types == null) return map;

            for (var i = 0; i < types.Length; i++)
            {
                var t = types[i];
                if (t == null || !t.IsClass || t.IsAbstract) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                if (!typeof(IUIPanel).IsAssignableFrom(t)) continue;

                if (map.ContainsKey(t.Name))
                {
                    // 同名不同命名空间：Provider 只给得到简单名 ⇒ 后到者不可达，必须留痕
                    //（否则表现为"某个面板永远打不开"，且毫无线索）。
                    Game.Logger?.Error(Tag,
                        $"面板类名重复 `{t.Name}`（{map[t.Name].FullName} / {t.FullName}）：按简单名的 Provider 只能取到前者");
                    continue;
                }
                map[t.Name] = t;
            }

            Game.Logger?.Info(Tag, $"已登记 {map.Count} 个面板类型（供 Game.UI.Open<T> 按类名取）");
            return map;
        }
    }
}
