# agent-05：客户端框架 + 启动链路（启动/登录/创角/主菜单/设置）+ 面板基建 + 素材管线

> 这是客户端**第一片**，其它客户端片都建在它之上。**接口一旦定下就是契约**，不许私改。

## 0. 技能（开工必做）

**(1) 拿到 skill。**
- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
- 全局兜底（按序命中即用）：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 客户端范式必读：`patterns/client/app-flow.md`、`patterns/client/ui.md`、`patterns/client/network.md`、
  `patterns/client/config.md`、`reference/visual-loop.md`
- 有 `use_skill` 就先加载 `clover-engine` 与 `unity-cli`
- 找不到 → 回报主 agent 要路径，**不许凭记忆写代码**

**(2) 按 skill 的「混合模式找依据」查代码**（用户 > 引擎 > 联网/自创），**不许编 API**。

⛔ **红线**：只许读本项目、`clover-client-unity-engine`（引擎源码）、`clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**。⛔ **不许改任何 skill**。

## 1. ★★ 三个必读（写第一行代码之前）

| 文档 | 为什么 |
|---|---|
| `<项目根>/docs/client-architecture.md` | **架构契约**：面板驱动不切场景、11 条关键决策（D1–D11）、目录与依赖方向、站点表 |
| `<项目根>/docs/client-api-reference.md` | **引擎 API 逐字摘要（含 `文件:行`）**，含 §0「已确认不存在的东西」清单。⛔ 每写一个 `Game.xxx` 调用前先在这里查到它 |
| `<项目根>/docs/步骤文档.md` §4.1 | 服务端↔客户端协议契约（已冻结） |

另外：`<引擎>/Samples~/LoginFlow/LoginFlow.cs` 是引擎自带的最完整接入示例（`Game.Launch` → `CloverAuth` →
`CloverNet.Init` → 订阅 `Game.Event`/`Game.Sync`）—— **先读它**。

## 2. ⚠️ 本片的特殊前提：**编辑器还没开，你无法编译**

- Unity 工程已建好（`<项目根>/client`，6000.6.0f1，Universal-2D 模板），manifest 已含
  `com.clover.unity-engine`（本地路径）+ `com.unity.pipeline` + `testables`。
- **编辑器由用户稍后打开**（如果提前打开而代码是半成品，Unity 会进 Safe Mode ⇒ 主 agent 失去驱动能力）。
- 所以：**⛔ 不要试图编译**（`unity run` / `-batchmode` 一律不许，skill §2.1.2 明令禁止）。
  你要做的是**按契约把代码写对**：每一个引擎 API 调用都必须在 `docs/client-api-reference.md` 里查得到；
  查不到就**不要写**，改用查得到的写法，并在回报里说明。
- 主 agent 会在编辑器打开后驱动它编译、把编译错误逐条修掉。

## 3. 目标（本片交付物）

让**启动链路**真正跑通：启动画面 → 登录/注册 → 创角（昵称）→ 主菜单 → 设置面板，
并把后面所有片要用的**基础设施**一次性定下来。

## 4. 任务边界（⛔ 严格）

**只做**：
- `client/Assets/Scripts/Core/{GameConst.cs,Events.cs,ResPaths.cs}`
- `client/Assets/Scripts/App/Bootstrap.cs`
- `client/Assets/Scripts/Module/Flow/AppFlow.cs`
- `client/Assets/Scripts/Module/Settings/SettingsManager.cs`
- `client/Assets/Scripts/UI/Panels/{BootPanel,LoginPanel,RegisterPanel,NicknamePanel,MainMenuPanel,SettingsPanel}.cs`
- `client/Assets/Scripts/UI/PanelFactory.cs`（D2 的 PanelProvider 替换，落点由你按引擎实际 API 定）
- `client/Assets/Editor/{SceneBuilder.cs,PrefabBuilder.cs}`（生成场景/面板预制体，若 D2 需要）
- **素材管线**：把 60 张卡用到的原版素材**复制**进 `client/Assets/Resources/`
- `client/Assets/Resources/**`（仅素材）

**绝不做**：
- ⛔ 不改 `client/Assets/Scripts/Def/{MsgDef.cs,ProtoDef.cs}`（**已冻结**，两端同名同值）
- ⛔ 不改 `client/Assets/Scripts/Core/ClientConfig.cs`（**已冻结**）
- ⛔ 不写 `Module/Room/**`、`Module/Battle/**`、`Module/Deck/**`、`View/**`、
  `UI/Panels/{DeckEditPanel,RoomListPanel,RoomPanel,HudPanel,PausePanel,ResultPanel}.cs`（那是 agent-06/07/08 的地盘）
- ⛔ 不改 `server/**`、`tools/**`、`docs/**`、`策划/**`、`tools/ai-skill/**`
- ⛔ 不许写交接/进度类 md；未完成项只写在**回报消息**里
- 一次性产物**只许放** `<项目根>/.ai-tmp/`

## 5. 详细要求

### 5.1 `Core/GameConst.cs` — 几何与单位（**唯一换算处**）
必须与 `策划/策划案/皇室战争参考规格.md` §1/§2 **一致**（逐条带出处）：
```csharp
public const int MilliTilePerTile = 1000;   // 服务端坐标单位 = 1/1000 格
public const float ArenaTilesW = 18f;
public const float ArenaTilesH = 32f;
public const float RiverTopTile = 15f;      // y ∈ [15,17)
public const float RiverBottomTile = 17f;
public const float BridgeCxATile = 3.5f;    // 桥中心 x
public const float BridgeCxBTile = 14.5f;
public const float BridgeHalfTile = 1f;     // 桥半宽（总宽 2 格）
public const float KingTowerTileX = 9f;     // BLUE 国王塔 (9,3)，RED 镜像 y=32-3
public const float KingTowerTileY = 3f;
public const float PrincessTowerTileY = 6.5f; // BLUE 公主塔 (3.5/14.5, 6.5)
public const int MaxElixirMilli = 10000;    // 圣水 1/1000
// 世界坐标换算：竞技场以「格」为单位、中心在原点
public static Vector2 MilliToWorld(int xMilli, int yMilli);
public static Vector2 TileToWorld(float xTile, float yTile);
public static Vector2 MilliToTile(int xMilli, int yMilli);
public static bool IsMirroredForTeam(int team);   // RED 侧 = y → 32 - y 镜像
```
> `y` 轴方向、镜像规则必须与服务端 `core/arena.go` **一致**（服务端：y 小 = BLUE 后方）。

### 5.2 `Core/Events.cs` — 事件名常量（**唯一定义处**）
按模块分段命名，形如 `Events.Flow.LoginSuccess` / `Events.Room.ListChanged` / `Events.Battle.Snapshot` 等。
用 `public const string`。⛔ 业务脚本里出现裸事件名字符串一律算错。

### 5.3 `Core/ResPaths.cs` — 资源路径常量（**唯一定义处**）
至少覆盖：卡面图目录、角色精灵目录（按 `key` 拼）、塔、竞技场底图、UI 图集。形如
`public static string CharacterFrames(string key)`（返回 `"Sprites/chr_" + key` 之类）。
⛔ 业务脚本里出现裸资源路径字符串一律算错。

### 5.4 素材管线（★ 只拷用到的，不要全拷）
- 源：`<项目根>/原版资源/cr-assets-png/assets/sc/`（**只读**）。
- 目标：`<项目根>/client/Assets/Resources/`。
- **需要拷什么**：60 张卡实际用到的角色目录（`chr_*_out`）+ `building_tower_out` + 1 个竞技场底图
  （`arena_training_out`）+ 建筑目录（`building_*_out`）+ 卡面图（`ui_spells_out` / 卡面所在目录）+ 少量 UI 图。
  目录名取自 `策划/策划案/皇室战争参考规格.md` §6 的 `素材目录` 列与 `策划/数值文档/unit_cs.txt` 的 `sprite_dir` 列。
- 写一个一次性脚本放 `<项目根>/.ai-tmp/hosts/copy_assets.py`（⛔ 不许放别处），**可重复执行（幂等）**。
- ⛔ 不许拷整棵 `sc/`（21000+ 张，会拖垮导入）。拷完在回报里给出**文件数 + 总字节数**。
- 卡面图/UI 图若在 `sc/` 下找不到明确来源，**如实报缺**（写进回报，不要编）。

### 5.5 `App/Bootstrap.cs`（≤200 行，唯一组装点）
按 `docs/client-api-reference.md` §1 的**严格次序**初始化；然后：
`Game.Fsm` 注册站点状态（Boot/Login/Nickname/MainMenu/Room/Battle/Pause）与转移；
挂 `PanelFactory`；进 `Boot` 状态开 `BootPanel`。

### 5.6 `Module/Flow/AppFlow.cs`
站点状态机 + 面板/场景编排。**面板只发事件，Flow 收事件决定下一步**（⛔ UI 不许 using `CR.Module`）。
包含：
- **启动画面**：`BootPanel` 显示 Logo + 版权字 + **底部 `by clover-engine`**；停留后自动进 `Login`。
- **登录/注册**：`Game` 上输入的账号密码 → `CloverAuth.SignupAsync` / `LoginAsync` 拿 token →
  `Game.Net.SetupSession(account, token, line)` → 等 `EMsg.Login` 成功事件 → 进 `Nickname` 或 `MainMenu`。
  失败必须**把原因显示在面板上**（不许只打日志）。
- **创角**：昵称 → `MsgDef.SetNickname` → 成功后进 `MainMenu`。
- **主菜单**：四个入口 —— 卡组编辑 / 房间列表 / **人机对战** / 设置，外加退出。
  ⚠️ 卡组编辑、房间列表、人机对战、对局这四个动作的**实现**在 agent-06/07 的片里；
  你只负责**按钮存在 + 发出对应事件 + 调用 `AppFlow` 暴露的接口方法**。
  **接口按这个签名暴露**（这就是契约，agent-06/07 会依赖它）：
  ```csharp
  public void RequestOpenDeckEdit();
  public void RequestOpenRoomList();
  public void RequestStartAiBattle();     // 主菜单「人机对战」
  public void RequestEnterBattle(string roomId, CR.Def.BattleStartNotify start);  // 由 Room/Battle 模块在开打时调用
  public void RequestReturnToMainMenu();  // 结算/暂停用
  ```
- **读条进图**：`Game.Scene.Load("Battle01", progress, onDone)`，**把真进度显示在面板上**。

### 5.7 `Module/Settings/SettingsManager.cs` + `SettingsPanel`
音量（BGM/SFX/Voice 三档，走 `Game.Sound.SetVolume` + `Game.Setting`）、画质（`Game.Quality.SetLevel`，
三档 `QualityTier`）、全屏（`Screen.fullScreen`）。**真改真存**：`Game.Setting.Save()`，重进仍在。

### 5.8 面板基建（`UI/PanelFactory.cs`）+ 场景/预制体生成器
按 `docs/client-architecture.md` **D2** 的二选一（**优先 ①**）：
① 替换 `CloverPresentation.PanelProvider`（`Runtime/Presentation/CloverPresentation.cs:69`）——
   **先读那个字段/属性的真实类型与赋值方式**，再决定怎么替换。
② 若 ① 不可行：写 `Assets/Editor/PrefabBuilder.cs` 生成**只有 RectTransform + 面板组件**的空 prefab
   到 `Assets/Resources/UI/{类名}.prefab`（面板内容仍由 `UIFactory` 在运行时构建）。

`Assets/Editor/SceneBuilder.cs`：用 `UnityEditor.SceneManagement.EditorSceneManager` 生成并保存
`Assets/Scenes/Main.unity` 与 `Assets/Scenes/Battle01.unity`（`Main` 挂 `Bootstrap`），
**并写入 `EditorBuildSettings.scenes`**（不进 Build Settings 就 `Scene.Load` 不到）。

### 5.9 面板视觉（★ 必须用原版素材，不许色块代替）
`BootPanel`（Logo/版权/`by clover-engine`）、`LoginPanel`、`RegisterPanel`、`NicknamePanel`、
`MainMenuPanel`（卡组/房间/人机/设置/退出）、`SettingsPanel`。
- 布局按 `patterns/client/ui.md` 与 `reference/visual-loop.md`；参考物截图放 `策划/基线图/`（若拿不到原版 UI 截图，用原版 UI 素材拼，并在回报里说明）。
- 字体：原版字体文件不在解包素材内 ⇒ 用 `UIFactory.DefaultFont()`，**并在回报里登记为已知差异**。
- ⛔ 不许用 `Image` 色块假装按钮图标；按钮必须有可读文字。

## 6. 验收标准

- [ ] 上述文件全部存在；`Assets/Scripts/` 下**只有**你负责的文件（别越界）
- [ ] ⛔ 全工程 grep 不到裸 `Debug.Log`（用 `Game.Logger.*`）
- [ ] ⛔ 全工程 grep 不到裸消息号字面量（`Game.Net.*(1000xxx`）、裸事件名字符串、裸资源路径字符串
- [ ] ⛔ 全工程 grep 不到 `UnityEngine.Input` / `PlayerPrefs` / `GameObject.Find` / `Resources.Load`（面板走 `Game.UI`，资源走 `Game.Res`）
- [ ] `App/Bootstrap.cs` ≤ 200 行（贴实际行数）
- [ ] `UI/Panels/*.cs` 里**没有** `using CR.Module`
- [ ] `by clover-engine` 确实出现在 `BootPanel` 的视觉树里（贴代码行 + 说明它渲染在哪一层）
- [ ] 素材已复制：贴**文件数 / 总字节数 / 目录清单**
- [ ] `docs/client-api-reference.md` 里查不到的 API **一个都没用**（自己逐条核对并在回报里说明）
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出（如实贴）
- [ ] ⛔ 项目里**没有** `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物**只在** `.ai-tmp/`

> ⚠️ 本片**无法编译验证**（编辑器未开）。验收以上面的「静态检查 + grep」为准；
> 编译错误由主 agent 在编辑器打开后统一修。**所以代码必须一次写对**：
> 宁可用笨一点但确定存在的 API，也不要用"看起来对"的 API。

## 7. 约束

- 改动四拍：① 只读取证 + 改动清单 → ② 批量写（⛔ 不编译）→ ③ **静态自检**（grep 清单）→ ④ 集中出证据
- ⛔ 不许改契约（`docs/client-architecture.md` / `docs/client-api-reference.md` / `Def/` / `ClientConfig.cs`）
- ⛔ 不许再派生任何子 agent
- 注释里写清**为什么**（尤其是"为什么这么摆/为什么用这个 API"），不要写"做了什么"

## 8. 回报格式

```
产出物：<绝对路径清单 + 行数>
契约对外暴露的接口：AppFlow 的 public 方法签名（逐条）
素材：<目录清单 + 文件数 + 字节数>
静态自检：<每条 grep 命令 + 结果>
查不到的 API / 拿不到的素材：<逐条列，不许编>
未决：无 / <具体条目>
```
