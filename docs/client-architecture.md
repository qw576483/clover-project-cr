# 客户端架构契约（clover-project-cr）

> **所有客户端 agent 必须先读本文 + `docs/client-api-reference.md`。**
> 本文是主 agent 定下的架构与接口契约；⛔ 不许私自改。发现问题 → 回报主 agent。

## 0. 唯一原则：**面板驱动，不切场景**

```
Main 场景（启动/登录/创角/主菜单/卡组编辑/房间列表/房间）
   │  ① 全部站点都是 UIPanel，切换用 Game.UI.CloseAll() + Game.UI.Open<T>(param)
   │
   └── Game.Scene.Load("Battle01", progress, onDone)   ← 真场景加载，读条用真进度
          │
Battle01 场景（对局：竞技场 + 单位 + HUD + 暂停 + 结算）
   └── 结束 → Game.Scene.Load("Main", …) 回到主菜单
```

**为什么**（代码级理由，不是偏好）：`Game.UI` 的面板树挂在常驻节点 `[UI]/…`（`Runtime/Presentation/UI.cs:60-71`）上，
而 `Game.Scene.Load` 会连带卸载实体/对象池/场景内对象。若把每个界面都做成场景，
站内切换会把常驻 UI 树和池一起清掉。**只有「进对局」这一次是真正的场景切换**，其余全是面板切换。

- ⛔ 不许用 `UnityEngine.SceneManagement.SceneManager` 直接切场景 —— 一律 `Game.Scene.Load`。
- ⛔ 不许自建第二套消息入口 —— 一律 `Game.OnMsg(业务号, ctx => ctx.Bind<T>())`。

## 1. 目录与职责（覆盖 `client/Assets/Scripts/`）

```
Assets/Scripts/
├── Def/          MsgDef.cs / ProtoDef.cs      —— 消息号与协议的唯一定义处
├── Core/         ClientConfig.cs / GameConst.cs / Events.cs / ResPaths.cs / AudioPaths.cs
│                 / PlayerSession.cs / Stations.cs / SettingsSnapshot.cs
│                 —— 常量、事件名常量、资源与音频路径常量、单位换算（tile↔milli↔世界坐标）
├── Module/       Flow / Deck / Room / Battle / Settings
│                 —— 业务逻辑：状态、C2S 门面、快照接收与插值。**不碰 UnityEngine 渲染**
├── UI/Panels/    每个面板一个 .cs（UIPanel 子类）—— ⛔ 不许 using CR.Module，只发/收事件
├── View/         ArenaView / UnitView / BattleViewRoot / EffectsView / BattleAudioView / BgmView 等
│                 —— 渲染与精灵动画（纯表现）
└── App/          Bootstrap.cs —— 唯一组装点，≤200 行
Assets/Editor/    SceneBuilder.cs（生成/保存场景并写入 Build Settings）
Assets/Resources/ 原版素材（从 <根>/原版资源/cr-assets-png 复制进来）
Assets/Configs/config.json
```

**依赖方向（单向，不许反向）**：`App → Module → Core/Def`；`App → UI`；`UI → Core/Events`（只发事件）；
`View → Core`。⛔ `UI` 不许引 `Module`；`Module` 不许引 `View`。

## 2. 关键决策（★ 逐条照做）

| # | 决策 | 理由 / 做法 |
|---|---|---|
| **D1** | **UI 用代码构建**（`UIFactory`），不手写 `.prefab` | 引擎自带 `UIFactory.CreatePanel/CreateText/CreateButton/CreateSlider/CreateInputField/...`（见 `docs/client-api-reference.md` §4）。手写 prefab 的 YAML 易错且不可评审。**做法**：面板自身在 `OnOpen` 里用 `UIFactory` 把视觉树建在自己的 `gameObject` 下 |
| **D2** | 面板必须能被 `Game.UI.Open<T>` 打开 | `Game.UI.Open<T>` 默认 `Resources.Load<GameObject>("UI/"+typeof(T).Name)`。**做法**：替换 `CloverPresentation.PanelProvider`（`Runtime/Presentation/CloverPresentation.cs:69`）为「运行时 new GameObject + AddComponent<T>」的工厂（本项目落在 `UI/PanelFactory.cs`），零资产；面板内容仍用 `UIFactory` 自建 |
| **D3** | **所有站点面板都挂在常驻 UI 树上**，站内切换只 `CloseAll`+`Open` | `UILayer`：背景 = `Background`，主界面 = `Normal`，弹窗（暂停/结算/确认）= `Popup`，加载遮罩 = `System` |
| **D4** | 服务端 10 Hz 快照 → 客户端**插值到 60 FPS** 渲染 | `Module/Battle` 保存「上一帧快照 + 当前快照 + 时间戳」，`UnitView` 每帧按 `(now-lastTs)/100ms` 线性插值位置。⛔ 不许直接用离散坐标驱动 Transform（会抖） |
| **D5** | 单位动画 = **逐帧精灵**，帧率由服务端 `anim` 字段选档 | `anim`：0=idle 1=walk 2=attack 3=die。每档一组 `Sprite[]`，用 `Game.Timer` 或 `View` 自更新按固定 FPS 换帧。素材 = `原版资源/cr-assets-png/assets/sc/chr_*/`（逐帧 PNG） |
| **D6** | 血条用引擎的 `WorldHpBar` | `WorldHpBar.Create(unitTransform, …)` + `SetRatio(hp/maxHp)`，见 API 摘要 §4 |
| **D7** | 竞技场用**原版素材**，按自己算的几何摆放 | 底图 = `arena_training_out`（1090×1677，对应 18×32 格）；6 座塔用 `building_tower_out` 帧。世界坐标换算一律走 `Core/GameConst.cs` 的 `TileToWorld/MilliToWorld`，⛔ 不许在 View 里散落魔法数字 |
| **D8** | 素材**复制**进 `Assets/Resources/`，路径收敛到 `Core/ResPaths.cs` | 引擎要求工程内资源；⛔ 不许直接引用 `原版资源/` 路径。**只复制 60 张卡实际用到的目录**（不是 21000 张全拷） |
| **D9** | 拖放出牌 | 按下手牌 → 跟随 `Game.Input.MousePosition` → `View/HandView` 显示落点指示（合法/非法两色）→ 抬起时发 C2S。合法性由**服务端**最终裁决，客户端只做**预校验**（用同一套 `Core/GameConst` 几何规则，避免"看起来能放结果被拒"） |
| **D10** | 一切日志走 `Game.Logger.Info/Warn/Error(tag, msg)` | ⛔ 裸 `Debug.Log` 一律算错（`tools/verify.ps1` 的 `hard-rules` 会报）。非预期分支必须留痕 |
| **D11** | 事件名、资源路径、消息号**各自只有一个定义处** | `Core/Events.cs` / `Core/ResPaths.cs` / `Def/MsgDef.cs`。⛔ 业务脚本里出现裸事件名字符串或裸资源路径一律算错 |

## 3. 服务端 ↔ 客户端契约（已冻结，见 `docs/步骤文档.md` §4.1）

- C2S 16 个 + 推送 6 个，**已逐字落进 `client/Assets/Scripts/Def/{MsgDef.cs,ProtoDef.cs}`**（两端同名同值）。
- ⛔ **回包不占消息号**（回包帧 msgID 恒为 0）—— 用 `await Game.Net.Call<XxxReply>(MsgDef.Xxx, req)`。
- 推送：`Game.OnMsg(MsgDef.PushXxx, ctx => { var n = ctx.Bind<XxxNotify>(); … })`。
- **快照只在 `Game.OnMsg(MsgDef.PushBattleSnapshot, …)` 一条路上进来**；玩家自己的手牌/圣水也在快照里（`hand_a`/`elixir_a`，`my_team` 由 `PushBattleStart` 给出）——所以客户端**不需要**为"我的手牌"再开一条协议。

## 4. 站点（App Flow）与面板清单

| 站点 | Fsm 状态 | 面板 | 层 | 场景 |
|---|---|---|---|---|
| 启动画面 | `Boot` | `BootPanel`（Logo + 版权字 + **底部 `by clover-engine`**） | Normal | **Boot**（停留后切 Main） |
| 登录 / 注册 | `Login` | `LoginPanel` / `RegisterPanel` | Normal | Main |
| 创角（昵称） | `Nickname` | `NicknamePanel` | Normal | Main |
| 主菜单 | `MainMenu` | `MainMenuPanel` | Normal | Main |
| 卡组编辑 | `MainMenu` 子面板 | `DeckEditPanel` | Popup | Main |
| 房间列表 | `MainMenu` 子面板 | `RoomListPanel` | Popup | Main |
| 房间内 | `Room` | `RoomPanel` | Normal | Main |
| 读条 | `Loading` | `LoadingPanel`（或用 `Game.UI.ShowLoading`） | System | 切换中 |
| 对局 | `Battle` | `HudPanel` | Normal | **Battle01** |
| 暂停 | `Pause` | `PausePanel` | Popup | Battle01 |
| 结算 | `Battle` 子面板 | `ResultPanel` | Popup | Battle01 |
| 设置 | 任意站点可开 | `SettingsPanel` | Popup | 任意 |

`by clover-engine` 是**硬要求**：必须出现在**首页画面的底部**（判据 = 实机截图 / 运行时节点树，⛔ 不是 grep 源码）。

## 5. 与全局 skill 的差异（只许"加严"）

| 项 | 写法 | 性质 | 原因 |
|---|---|---|---|
| UI 构建方式 | **代码构建（`UIFactory`）**，面板自身在 `OnOpen` 里建树 | 加严 | 引擎自带该能力；手写 prefab YAML 易错且无法评审 |
| 场景数量 | **固定 2 个**（`Main` / `Battle01`） | 加严 | 站内切换走面板，避免 `Scene.Load` 清掉常驻 UI 与池 |
| 快照渲染 | **必须插值**，不许直接贴坐标 | 加严 | 服务端 10 Hz，直接贴会明显抖 |
| 素材落点 | 只复制**用到的**目录进 `Assets/Resources/` | 加严 | 原版解包有 21000+ 张，全拷会拖垮导入 |

## 6. 接线细则（**以本节为准**）

### 6.1 依赖方向：`UI → View` 是允许的

§1 的「`UI → Core/Events`，⛔ `UI` 不许引 `Module`」之外还有一条：拖放出牌时
`HudPanel`（`CR.UI.Panels`）要问「这个落点合不合法」并让它把圈画出来，而这两件事
（`IsDeployLegal` / `ShowPlacement`）在 `CR.View.BattleViewRoot` 上。

所以 `UI` 允许引 `CR.View` —— `View` 是**纯表现**（不含业务状态与协议），
`UI → View` 不会把协议耦合进面板。**仍然禁止 `UI → CR.Module`**（那才是"面板直接摸业务状态"）。
判据：`grep -r 'using CR\.Module' client/Assets/Scripts/UI` 必须为 0 命中。

### 6.2 坐标插值的归属：**在 `View` 侧**，不是 `Module` 侧

`Events.Battle.Snapshot` 的参数**就是** `BattleSnapshot` 原件，事件表里**没有**"插值后坐标"的事件。
`View` 又不许引 `CR.Module` ⇒ 插值只能由 `BattleViewRoot` 自己做（双帧字典 + `t` 钳到 1，**不外推**，
理由见该类注释：外推会把单位推穿墙，比停一下更糟）。

⛔ **Module 侧不做第二份插值** —— 两处插值会互相打架；`Module/Battle` 只收快照、发事件，坐标以原件下发。

### 6.3 `HudPanel` 拖放与落点指示的接线

1. **`HudPanel` 拖放时既跟手也刷新落点指示**：
   `MoveGhost` 每帧 → `UpdatePlacementPreview(screen)` → `BattleViewRoot.ShowPlacement(tile, radius, isSpell)`；
   `CancelDrag`（正常抬手 / 取消 / 面板关闭）→ `BattleViewRoot.HidePlacement()`（D9 要求的合法/非法落点圈就靠这条链）。
2. **落点半径是表现近似**：`CardInfo` 里**没有**碰撞半径字段（协议未下发），
   所以 `HudPanel` 按类型给固定值（部队 1 格 / 法术 3 格）。**只影响圈画多大，不影响任何裁决。**

### 6.4 `ArenaView` 底图：**分段定标**，不是整图缩放

`arena_training_out` 的 23 帧**不是**一张完整底图，是同一画布（1090×1677）上的**分层小图元**，
且**帧间不共位**（f006 河心 py=835 vs f022 河心 py=572，差 263 px ⇒ 叠起来是错的）。
原版靠逐物体锚点表解决，而**该表在本项目不存在**。
故只采用 **f006**（唯一与 18×32 格网自洽的一帧：通路中心间距 502 px ÷ 11 格 = 45.6 px/格，
反推出格 0 在 px 208.4、格 18 在 1029，**另外三处独立特征也落在该映射说的地方**），
并按「后沿 0 / 广场 6.5 / 河心 16 / 内容顶 22.3」四点做**两段定标**（原版美术带透视，
纵向 px/格 近处≈58、河附近≈22，**不存在**单一线性映射能同时让河与广场落对格）。
RED 半场 = 同一帧 `flipY` 镜像。**未纳入合成的 22 帧**（不共位）与
**未认出的塔摧毁态下标**见 `tools/ai-skill/registry.md` / `constraints.md` 的已知缺口条目。

### 6.5 对局 HUD 的**图元来源**与**坐标来源**（只记结构）

对局 HUD（`UI/Panels/HudPanel.cs`）的每一个可见件，现在都满足两条结构约定：

| 项 | 结构 | 落点 |
|---|---|---|
| 图元来源 | 战斗 HUD 的图元**不在**独立的 `.sc` 文件里，而在 **`ui.sc` 的 `HUD_*` export 内部的 `0c` 具名子元件**里（`HUD_player`(1091) → `slots`(1070) / `elixir_bar`(1080)；`printScore_*`(1026/1031)；`HUD_topRight`(1017)；`replay_HUD_left`(962)） | 出处文档 `策划/战斗HUD素材索引.md`（§1 结论表 / §3 逐项明细 / §4 落地表）；键集中在 `Core/ResPaths.cs` 的 `UiRoot` 段 |
| 圣水条三件 | `ElixirBarTrack` = `ui_out` **155**（原版 `elixir_bar/bar_bg`）、`ElixirBarFrame` = **158**（`bar_end`）、`ElixirBarFill` = **157**（`bar_body`/`ghost`） | `Core/ResPaths.cs`（三个键）；对照行 `策划/对照表.md` C60–C62（差值 0） |
| HUD 新增键（按用途） | `Slots/ui_out`：`HudHandSlot`(200) ×4 +「下一张」同件复用、`HudHandSlotDraft`(201)；`Icons/ui_out`：`HudStarPlayer`(187)/`HudStarEnemy`(188)/`HudStarPlayerAlt`(197)/`HudStarEnemyAlt`(198)、`HudClockIcon`(042)、`HudPauseIconPlay`(170)/`HudPauseIconPause`(171)、`IconElixirBarLeft`(159)、`IconQuitCross`(164)；`Panels/ui_out`：`HudScoreNamePlate`(196)、`HudTopRightPlate`(193)；`Buttons/ui_out`：`HudPauseButtonPlate`(163)；`Bars/ui_out`：`ElixirBarTrackAlt`(156)、`ElixirBarTick`(160)、`ElixirRequirementTrack`(161)、`ElixirRequirementEnd`(162) | `Core/ResPaths.cs`；清单同步在 `tools/probes/copy-ui-assets.py`（⛔ 业务代码不写路径字面量） |
| 坐标来源 | HUD 的 21 个几何常量**逐条带出处**：底部 HUD 全部取 `策划/参考图/几何量取.md` §1.3（基线 `18_对局HUD_1080x1920.jpg`，与本项目画布**同尺寸 ⇒ k=1.0**）；量不到的项（冠数 / 暂停 / 阶段 / 时钟图标摆放尺寸 / 手牌字号 / 新帧九宫格切边）**保持现状并在注释里写明未量到**，⛔ 不编绝对值 | `UI/Panels/HudPanel.cs` 的常量区（每条注释带 `D1..D17` 或 `§2 C4/C4c` 出处）；对照行 `策划/对照表.md` A38–A53 |
| 顺序纠正（结构级） | ① **圣水条在手牌下方**（原版条 y1797..1841、卡 y1614..1785）；② **「下一张」在左下**（x 33..97 / y 1634..1716）；③ 手牌整排**不再"居中推算"**，改用 `TextAnchor.LowerLeft` 直接落在量到的 x=144 | 同上；出处 = `几何量取.md` §1.3 D1/D2/D12/D14/D15/D9 |
| 三向一致的把关 | `ResPaths` 键 ↔ `Sprites/Ui/**` 磁盘文件 ↔ `copy-ui-assets.py` 清单条目，由 `tools/probes/check-ui-keys.ps1` 机械对账 | `tools/probes/check-ui-keys.ps1`、`tools/probes/copy-ui-assets.py` |

> ⚠️ **已知缺口（结构级）**：
> ① `check-ui-keys.ps1` 只扫 `ResPaths` 的**常量键**，扫不到 `CrUiStyle` 里经 `ResPaths.UiFrame(...)` 拼出的
> **动态路径**（`CrUiStyle.LogoOfficial` / `LoadingBarFill`）⇒ 这两个引用对"未引用"探针**不可见**
> （历史后果与恢复记录见 `策划/验收表.md` 的 **D75**）。
> ② `HudPauseIconPlay`(170) 已登记键但 HUD 不使用（该按钮恒定进暂停菜单，没有播放态）；
> ③ 圣水刻度 / 需求条（160/161/162）已登记键但 HUD 未绘制。

## 7. 竖版画布约定（★ 朝向 = portrait）

> 出处：A =《皇室战争》是**竖版(portrait)** 游戏（官方截图宽 < 高）。
> 全局 skill 闸门 0：朝向只判一次、全程照做。

| 项 | 值 | 落点 |
|---|---|---|
| 画布参考分辨率 | **1080×1920**（竖版设计画布） | `App/Bootstrap.cs` 的 `LaunchEngine()` **第一行**：`CloverPresentation.ReferenceResolution = new Vector2(1080f, 1920f)` |
| CanvasScaler match | **0**（匹配宽度 ⇒ 画布宽恒为 1080） | 同上：`CloverPresentation.MatchWidthOrHeight = 0f` |
| 屏幕方向 | portrait | 运行时 `Screen.orientation = ScreenOrientation.Portrait`；真机默认方向 = 工程设置 `PlayerSettings.defaultInterfaceOrientation`（`defaultScreenWidth/Height = 1080/1920`） |
| 设计画布常量 | `CrUiStyle.DesignW = 1080f` / `DesignH = 1920f` | `UI/CrUiStyle.cs`（★ 全项目唯一的画布尺寸出处） |

**为什么 match 取 0 而不是 0.5**：`0.5` 在竖屏上会按宽高比插值出**小数**倍缩放
（1080 宽屏上 ≈1.10xx）⇒ UI 像素不是整数倍、文字与 1px 描边发糊；取 `0` 后画布宽恒为 1080，
任意竖屏分辨率下都是整数倍缩放。**⛔ 这两个属性必须在 `Game.Launch` 之前设置**
（`UIManager` 在 Launch 的挂载钩子里构造，构造时只读一次）。

**⛔ 面板禁止按 1920 宽布局**（新写的面板必须遵守）：

- 竖版画布宽只有 **1080** ⇒ 任何 `BoxW ≥ 1200` 的横排布局、或"标签–输入框–按钮"一行并排，**必然错**；
- 一律**上下排布 + 角锚点**（`UIFactory.AnchoredTopLeft` / `AnchoredBottom` / `Stretch`）；
  ⛔ 不许在面板里写 1920 / 1080 字面量 —— 引用 `CrUiStyle.DesignW` / `DesignH`，或按锚点自适应；
- 背景图 `CrUiStyle.SpriteBackground` 现在是**铺满**（已去掉 `preserveAspect`）——
  竖构图原图配竖画布；⛔ 不许再靠"按原比例居中 + 两侧留底色"去兜朝向错误。

**引擎侧**：`CloverPresentation.ReferenceResolution` / `.MatchWidthOrHeight` 是引擎的两个公开静态属性
（默认 `1920×1080` / `0.5`，本项目在 `Game.Launch` 前覆盖 ⇒ 其它项目零影响），
出处 = 引擎 `Runtime/Presentation/CloverPresentation.cs:85` / `:101`。

**判据日志行**（每次 `Game.Launch` 打一条，出处 = 引擎 `UI.cs` 的 `UIManager` 构造末尾）：

```
[UI] 画布适配：参考分辨率=1080×1920 / match=0 / Screen=<宽>×<高>
```

看到 `参考分辨率=1920×1080` 就说明那三行写在了 `Game.Launch` **之后**（改晚了、不生效，且不报错）。

**编辑器里怎么取"竖版"截图**（判据本质是画面时必须知道）：编辑器按 **Game View 的尺寸**渲染，
**与 `PlayerSettings.defaultScreenWidth/Height` 无关** ⇒ 截图前要把 Game View 切成竖版：
Game View 右上角 Aspect 下拉 → `+` → Fixed Resolution `1080 × 1920`。
自定义项存**编辑器侧**、不落在工程目录内 ⇒ 换机器 / 清编辑器配置后需重加。

**竖版画布下的结构约定（只记结构）**：面板不采用"一行并排标签-输入框-按钮"，而是**上下排布 + 角锚点**的
**纵向单列**（建房 = 三行、房间内四个动作 = 四行、设置音量 = 每档两行、暂停四个动作 = 单列四行）；
五面板（主菜单 / 房间列表 / 房间内 / 设置 / 暂停）全部只引用 `CrUiStyle` 的常量与
`ContentPanel` / `TitleBar` / `ActionButton` / `NineSlice` / `AspectImage`，
⇒ 版式常量集中在 `UI/CrUiStyle.cs` **一处**，⛔ 面板里不写画布尺寸字面量。

## 8. 启动场景（Boot）与特效层的结构

### 8.1 `Boot` 场景（Build Settings index 0）

启动链路由「单场景 + 面板切换」改为**两段场景**：

| 项 | 结构 | 落点 |
|---|---|---|
| 场景清单与顺序 | `Boot`（index 0）→ `Main` → `Battle01` | `client/ProjectSettings/EditorBuildSettings.asset`，由 `Assets/Editor/SceneBuilder.BuildAll()` **生成**（⛔ 不手写场景 YAML） |
| `Boot` 场景内容 | 一个 `Bootstrap` 宿主（`m_EditorClassIdentifier: CR::CR.App.Bootstrap`）+ `Main Camera` | `Assets/Scenes/Boot.unity` |
| 启动页所在场景 | `BootPanel` 跑在 **`Boot` 场景里**（不是 Main） | `AppFlow` 站点序 `Launching → Boot → Login`；Boot 停留后 `Scene.Load("Main")` |
| 第二次进入 Main | Main 场景的 `Bootstrap` 走**重入路径**「引擎已在运行 ⇒ 仅确保流程存在」 | 与「从对局返回主菜单」**同一条**重入路径，⛔ 不是第二套初始化 |
| 与 `View` 的关系 | 场景切换会卸载旧的 View 树 ⇒ `BattleViewRoot.Build()` 必须**自愈**（`BattleContent` 被销毁时重建），否则整场对局渲染成空场地 | `View/BattleViewRoot.cs` |

### 8.2 特效层（`View/EffectsView.cs`）

- **唯一入口**：`EffectsView.Create(Transform)` 建层；`EffectsView.Play(pos, use, firstFrame, count, worldSize)` 定点播、
  `PlayFlight(from, to, use, firstFrame, count, worldSize)` 飞行播。
  帧**不新建**：全部经 `SpriteBank.LoadDir(ResPaths.EffectDir(use), UnifiedCanvasAnchor)` 从磁盘上的**原版帧**加载
  （目录 = `Sprites/Effects/<用途>/`，用途键集中在 `Core/ResPaths.cs` 特效区段），⛔ 代码内无 `Sprite.Create` 自绘、无纯色方块。
- **缩放口径**：`WorldSize = 5.37f`（原版特效画布 474×537 @ PPU 100），`Play` 按 `size / WorldSize` 定缩放。
- **接线方向**：服务端对局事件（`Event.Kind`）由 `BattleViewRoot.OnBattleEventNotify` 翻译成特效播放；
  位置**取事件自带**的 `x_milli / y_milli`（服务端在 `combat.go` 里对 `EvDeath` / `EvTowerDestroyed` 都填了实体自身坐标）
  ⇒ 客户端⛔ 不另算位置。特效节点独立于单位与塔，排序层在二者之上（`order=3000`）。
- **远程弹道**：`PlayFlight` 带 `durationSeconds` 的重载（既有签名保留）；`BattleViewRoot` 订阅 `Deck.PoolLoaded`
  建 `card_id → CardInfo` 索引，`case EventKindPlayCard` 里 `CardInfo.projectile_key` 非空 ⇒ 播飞行弹道，
  时长 = 距离(格) × 60 / `proj_speed`（`proj_speed` 单位 = **格/分钟**）。
  施法者位置**降级**为出牌方国王塔中心（事件载荷不带施法者）。

## 9. 后续片接入的结构（只记结构，不记进度）

> 本节记的是**部件之间的接线形状**（谁持有谁、入口在哪），进度见 `策划/验收表.md`。

### 9.1 单位动画：帧段表 → `UnitView.AnimRanges`

- `UnitView` 新增两张**纯数据表**：`AnimRanges[dir]`（四元组 `(start, count)`，`start` 是**帧号**不是下标）
  与 `AnimFpsPerDir[dir]`（逐档帧率；idle = 0 ⇒ 停在区间首帧）。
- 帧号 → 扁平下标的换算在 `SpriteBank.FrameNumberMap(path, mode)` 一处（内部打印**恒等断言**：
  单位目录 1 PNG = 1 Sprite ⇒ 帧号恒等于下标；多子图目录如 `chr_giant_out` 会打印「非恒等」并给出映射样例，
  避免静默取错帧）。
- `UnitView.TryClipRange` 经它把帧号换算成下标；未收录目录回落整目录循环 + `WarnIfUnmapped` 每目录报一次。
- ⛔ 塔 / 竞技场多子图目录**不走这条**（`ArenaView` 未接 `AnimRanges`，其底图仍按帧号取）。

### 9.2 音效层：`Core/AudioPaths` + `View/BattleAudioView`

- **素材键**集中在 `Core/AudioPaths.cs`（SFX 按用途分目录 + BGM 三键，⛔ 业务代码不写路径字面量）；物理目录 =
  `client/Assets/Resources/Sound/SFX/<用途>/<源文件名>.ogg`（引擎 `PlaySFX` 硬编码 `Sound/SFX/{clipName}`）。
- **对局音效唯一播放入口** = `View/BattleAudioView.cs`：`BattleViewRoot.Build()` 挂一个 `BattleAudioView`（唯一挂点），
  订阅对局事件后调 `Game.Sound.PlaySFX(clipName)`；BGM 由 `View/BgmView.cs` 负责。
  ⛔ 不 `AddComponent<AudioSource>`、不建池、不散点 `Resources.Load`。
- 音量走 `SoundGroup.SFX` 分组，**读**既有 `Module/Settings/SettingsManager` 的对外契约（不写死值）。

### 9.3 设置订阅：`Events.Settings.*Request` → `SettingsManager.Init()`

- `SettingsManager.Init()` 订阅 `Events.Settings.{Bgm,Sfx,Voice}VolumeRequest / QualityRequest / FullscreenRequest`，
  **同帧**转调 `Game.Sound.SetVolume` / `Game.Quality.SetLevel` / `Screen.fullScreen` 并 `Game.Setting.Save()` 落盘。
- 面板侧（`SettingsPanel`）只负责 `Emit(Events.Settings.*Request, …)`，⛔ 不直接持有引擎对象。

