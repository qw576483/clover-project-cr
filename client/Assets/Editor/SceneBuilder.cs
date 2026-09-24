using System.IO;
using CloverEngine;
using CR;
using CR.App;
using CR.Module.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CR.EditorTools
{
    /// <summary>
    /// 场景生成器：产出本项目**固定三个**场景（架构契约 §0：站内切换走面板、不切场景，
    /// 只有"启动画面→主场景 / 进对局 / 回主菜单"三次真加载），并把它们写进 Build Settings。
    ///
    /// <para>
    /// <b>为什么场景要由脚本生成而不是手写 `.unity` YAML</b>：引擎包**不含任何 `.unity` / `.prefab`**
    /// （见项目约束册），手写 YAML 要自己编 fileID / GUID，且编辑器在下一次导入时可能改写它。
    /// 用 `EditorSceneManager` 建场景是唯一"编辑器认可"的写法。
    /// </para>
    /// <para>
    /// <b>为什么必须写 `EditorBuildSettings.scenes`</b>：`Game.Scene.Load` 最终走
    /// `SceneManager.LoadSceneAsync(sceneName)`（`Runtime/Presentation/Scene.cs:30`），
    /// **不在 Build Settings 里的场景加载不到**：引擎会打一条 `Scene not found` 的 Error 并直接回调
    /// `onDone`，表现为"读条一闪而过、界面没换、也没有任何异常"。这是本项目最容易踩的静默失败之一。
    /// </para>
    /// <para>
    /// <b>怎么跑</b>：菜单 `Tools/CR/1. 生成场景与写入 Build Settings`，
    /// 或无头执行 `unity run <client> -- -executeMethod CR.EditorTools.SceneBuilder.BuildAll`。
    /// 幂等：重复执行只会用当前代码重新生成同样三个场景。
    /// </para>
    /// </summary>
    public static class SceneBuilder
    {
        private const string Tag = "SceneBuilder";

        // 本文件原先用裸 `Debug.Log*`（7 处）。引擎规矩 = 客户端日志一律走 `Game.Logger`
        // （`ILogger`，⛔ 裸 `Debug.Log`）。此前担心的"Editor 期 `Game.Logger` 可能未就绪"**已核实不成立**：
        // `Runtime/Core/Game.cs:149` `public static ILogger Logger { get; private set; } = ConsoleLogger.Instance;`
        // 且 `Runtime/Core/ConsoleLogger.cs` 的类注释逐字写着「现在 `Game.Logger` **永不为 null**：
        // 未 Launch 时指向本类（写 Console，测试里直接可见），Launch 后同样回到本类」
        // ⇒ Editor 编辑模式（未 Play / 未 Launch）下调 `Game.Logger?.Xxx(tag, msg)` 一样进 Unity Console，
        // 且输出格式 `[时间] [级别] [tag] 消息` 与运行时文件日志同格式、可按时间逐行对齐。
        // ⛔ 不发明 Logger 包装类（引擎已有唯一入口，起第二套 = 违规）。

        /// <summary>场景目录（相对工程根）。</summary>
        public const string ScenesDir = "Assets/Scenes";

        /// <summary>
        /// 启动场景路径。场景名取自 `AppFlow` 的常量 —— ⛔ 不在这里写第二份字面量。
        /// 它是 Build Settings 的 index 0（构建产物先加载它）。
        /// </summary>
        public static string BootScenePath => ScenesDir + "/" + AppFlow.BootSceneName + ".unity";

        /// <summary>主场景路径。场景名取自 `AppFlow` 的常量 —— ⛔ 不在这里写第二份字面量。</summary>
        public static string MainScenePath => ScenesDir + "/" + AppFlow.MainSceneName + ".unity";

        /// <summary>对局场景路径。同上，名字的唯一出处是 `AppFlow`。</summary>
        public static string BattleScenePath => ScenesDir + "/" + AppFlow.BattleSceneName + ".unity";

        /// <summary>主相机的 z 距离：2D 工程沿用 Unity 默认的 -10（正交相机下 z 只决定裁剪范围）。</summary>
        private const float CameraZ = -10f;

        /// <summary>一次性生成三个场景 + 写入 Build Settings。</summary>
        [MenuItem("Tools/CR/1. 生成场景并写入 Build Settings")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
            {
                // 非预期分支：Play 模式下改场景会被 Unity 丢弃。留痕，别静默什么都不做。
                Game.Logger?.Error(Tag, "正处于 Play 模式，请先退出 Play 再生成场景");
                return;
            }

            if (!Directory.Exists(ScenesDir)) Directory.CreateDirectory(ScenesDir);

            BuildBootScene();
            BuildMainScene();
            BuildBattleScene();
            WriteBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Game.Logger?.Info(Tag, $"完成：{BootScenePath} + {MainScenePath} + {BattleScenePath} 已生成并写入 Build Settings");
        }

        /// <summary>只读校验：三个场景文件在不在、Build Settings 里有没有（供验收取证用）。</summary>
        [MenuItem("Tools/CR/2. 校验场景与 Build Settings")]
        public static void Verify()
        {
            var bootOk = File.Exists(BootScenePath);
            var mainOk = File.Exists(MainScenePath);
            var battleOk = File.Exists(BattleScenePath);
            var inBuild = CountInBuildSettings(BootScenePath) > 0
                          && CountInBuildSettings(MainScenePath) > 0
                          && CountInBuildSettings(BattleScenePath) > 0;

            Game.Logger?.Info(Tag, $"校验：{BootScenePath}={(bootOk ? "存在" : "缺失")} " +
                                   $"{MainScenePath}={(mainOk ? "存在" : "缺失")} " +
                                   $"{BattleScenePath}={(battleOk ? "存在" : "缺失")} BuildSettings={(inBuild ? "已写入" : "缺失")}");
        }

        // ───────────────────────── 场景内容 ─────────────────────────

        /// <summary>
        /// `Boot`：启动场景（Build Settings index 0）—— 空场景 + `Bootstrap` 节点 + 相机。
        /// <para>
        /// 与 `Main` **同样手法**（同一种空场景 + `Bootstrap` + <see cref="AddCamera"/>）：
        /// 引擎的首次拉起（`Bootstrap.LaunchEngine`）在这里发生，`AppFlow` 进 `Boot` 站点打开
        /// 启动画面（`BootPanel`，含底部 `by clover-engine` 署名），停留
        /// `GameConst.BootSplashSeconds` 后 `AppFlow.LoadMainSceneFromBoot` 切到 `Main`；
        /// `Main` 里的 `Bootstrap` 走的是"引擎已在运行"的重入路径。
        /// </para>
        /// <para>
        /// 相机同样不能省：清屏色与 `Main` 一致 ⇒ 启动画面出现前的**第一帧**不会闪白屏；
        /// 且 `AudioListener` 必须在场（否则 Unity 每帧报 "There are no audio listeners in the scene"）。
        /// </para>
        /// </summary>
        private static void BuildBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrap = new GameObject("Bootstrap");
            bootstrap.AddComponent<Bootstrap>();

            AddCamera();

            var saved = EditorSceneManager.SaveScene(scene, BootScenePath);
            Game.Logger?.Info(Tag, $"{BootScenePath} 保存{(saved ? "成功" : "**失败**")}（含 Bootstrap 组件）");
        }

        /// <summary>
        /// `Main`：`Bootstrap` 挂在根节点上，外加一台相机 + AudioListener。
        /// <para>
        /// <b>`Bootstrap` 在 `Main` 里走的是"引擎已在运行"的重入路径</b>（`Game.IsRunning` 分支：
        /// 只 `PanelFactory.Install` + `AppFlow.EnsureCreated`，不重复 Launch）；引擎的**首次**拉起
        /// 发生在 `Boot` 场景（见 <see cref="BuildBootScene"/>）。
        /// </para>
        /// <para>
        /// 为什么还要相机：`Game.UI` 的 Canvas 是 `ScreenSpaceOverlay`（不需要相机），但
        /// <list type="bullet">
        /// <item>`Camera.main` 是引擎 `UIFactory.UICamera()`（世界坐标 → 屏幕坐标，飘字用）的取值来源，
        /// 场景里没有主相机时飘字直接不显示；</item>
        /// <item>没有 AudioListener 时 Unity 每帧报 "There are no audio listeners in the scene"，
        /// 且 `Game.Sound` 播放无声。</item>
        /// </list>
        /// </para>
        /// </summary>
        private static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bootstrap = new GameObject("Bootstrap");
            bootstrap.AddComponent<Bootstrap>();

            AddCamera();

            var saved = EditorSceneManager.SaveScene(scene, MainScenePath);
            Game.Logger?.Info(Tag, $"{MainScenePath} 保存{(saved ? "成功" : "**失败**")}（含 Bootstrap 组件）");
        }

        /// <summary>
        /// `Battle01`：空场景 + 一台正交相机。竞技场 / 单位 / HUD 由 agent-07 的 `View` + `HudPanel`
        /// 在进图后创建，本片**故意**不往里放东西 —— 放进去的占位物都要被删掉，不如不放。
        /// <para>
        /// 相机正交尺寸取 <see cref="GameConst.ArenaTilesH"/> 的一半（32 格高 ⇒ 16）：
        /// 世界坐标以"格"为单位、竞技场中心在原点（见 `Core/GameConst.cs`），
        /// 因此这一个数值就让整座竞技场正好铺满视野高度，不出现"只能看到一半场地"。
        /// </para>
        /// </summary>
        private static void BuildBattleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = AddCamera();
            cam.orthographic = true;
            cam.orthographicSize = GameConst.ArenaTilesH * 0.5f;

            var saved = EditorSceneManager.SaveScene(scene, BattleScenePath);
            Game.Logger?.Info(Tag, $"{BattleScenePath} 保存{(saved ? "成功" : "**失败**")}" +
                                   $"（空场景 + 正交相机 size={cam.orthographicSize}）");
        }

        /// <summary>建一台 2D 用相机（正交、纯色清屏、带 AudioListener）。</summary>
        private static Camera AddCamera()
        {
            var go = new GameObject("Main Camera");
            // Camera.main 按 tag 查找；不带 tag 的话 UICamera() 会退回"任一相机"，
            // 多相机场景下就会取错。
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = GameConst.ArenaTilesH * 0.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 清屏色取与 UI 底色同一族（`CrUiStyle.ScreenBg`），避免加载间隙闪白屏。
            cam.backgroundColor = new Color(0.043f, 0.063f, 0.106f, 1f);
            go.transform.position = new Vector3(0f, 0f, CameraZ);

            go.AddComponent<AudioListener>();
            return cam;
        }

        // ───────────────────────── Build Settings ─────────────────────────

        /// <summary>
        /// 用这三个场景**整体替换** Build Settings 列表。
        /// 顺序 = <c>Boot → Main → Battle01</c>：`Boot` 必须是 index 0，构建产物启动后先加载启动场景，
        /// 再由 `AppFlow.LoadMainSceneFromBoot` 切到 `Main`。
        /// 原来的模板场景（`SampleScene`）不再出现在列表里，但**不删除文件**（不在本片职权内）。
        /// </summary>
        private static void WriteBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),   // index 0：构建后的启动场景
                new EditorBuildSettingsScene(MainScenePath, true),
                new EditorBuildSettingsScene(BattleScenePath, true),
            };
            EditorBuildSettings.scenes = scenes;

            Game.Logger?.Info(Tag, $"EditorBuildSettings.scenes 已写入 {scenes.Length} 条：" +
                                   $"[0] {BootScenePath} [1] {MainScenePath} [2] {BattleScenePath}");
        }

        private static int CountInBuildSettings(string path)
        {
            var count = 0;
            var scenes = EditorBuildSettings.scenes;
            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null && scenes[i].path == path && scenes[i].enabled) count++;
            }
            return count;
        }
    }
}
