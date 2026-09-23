# agent-07：客户端对局表现（竞技场 / 单位 / 帧动画 / 拖放出牌 / HUD）

## 0. 技能（开工必做）

- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
- 全局兜底：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 客户端范式：`patterns/client/app-flow.md`、`patterns/client/ui.md`、`patterns/client/network.md`
- 有 `use_skill` 就先加载 `clover-engine`
- ⛔ 不许改任何 skill（发现问题写进回报）

## 1. ★★ 必读（写第一行代码之前）

| 文档 | 为什么 |
|---|---|
| `<项目根>/docs/client-architecture.md` | 架构契约：**D4 快照必须插值**、**D5 帧动画**、**D6 血条用 `WorldHpBar`**、**D7 几何**、**D9 拖放出牌**、D10 `Game.Logger`、D11 唯一定义处 |
| `<项目根>/docs/client-api-reference.md` | 引擎 API 逐字摘要（含 `文件:行`）+ §0 不存在清单 + §1 四个实测纠正。⛔ 每个 `Game.xxx` 调用前先查到它 |
| `<项目根>/docs/步骤文档.md` §4.1 | 协议契约（已冻结） |
| `<项目根>/策划/策划案/皇室战争参考规格.md` §1/§2 | **几何与塔位的权威数值**（逐条带出处） |

**已交付、你要依赖的（⛔ 不许改）**：
- `client/Assets/Scripts/Core/{GameConst.cs,Events.cs,Stations.cs,ResPaths.cs}` —— `GameConst` 是**几何/单位的唯一换算处**
- `client/Assets/Scripts/Module/Flow/AppFlow.cs` —— `AppFlow.Instance.RequestEnterBattle(roomId, start)` / `RequestReturnToMainMenu()`
- `client/Assets/Scripts/UI/{PanelFactory.cs,CrUiStyle.cs}` —— 面板基建与共用样式（**照它写 `HudPanel`**）
- `client/Assets/Scripts/Module/Deck/DeckManager.cs` —— 卡池数据（`CardInfo`）与卡组
- `client/Assets/Scripts/Module/Room/RoomManager.cs` —— 房间态（你要用它的 `CurrentRoomId`）

## 2. ⚠️ 本片特殊前提：**编辑器还没开**

- ⛔ **绝对不许**跑 `unity run` / `unity test` / `Unity.exe -batchmode` / `unity command editor_play` / 启动编辑器
- ✅ 有**可用的离线编译自检**（复用，不要重写）：
  `powershell -NoProfile -ExecutionPolicy Bypass -File <根>/.ai-tmp/hosts/compile-check.ps1`
  先跑基线、改完再跑，**目标 = `errors in client/Assets = 0`**，两次输出都贴进回报

## 3. 目标（用户点名的核心）

> 用户原话：「**实现皇室战争！只实现核心逻辑**……**要有人机 pk**，**要能创建房间，和人一起玩**」

本片交付**对局画面**：竞技场 + 6 座塔 + 双方单位（逐帧动画 + 血条）+ 4 张手牌 + 拖放出牌 + 圣水条 + 计时 + 冠数。

## 4. 任务边界（⛔ 严格）

**只做**：
- `client/Assets/Scripts/Module/Battle/**`
- `client/Assets/Scripts/View/**`
- `client/Assets/Scripts/UI/Panels/HudPanel.cs`

**绝不做**：
- ⛔ 不写也不改 `UI/Panels/{PausePanel,ResultPanel}.cs`（agent-08 的地盘）
- ⛔ 不改 agent-05/06 的任何文件（`App/`、`Core/`、`Module/{Flow,Settings,Deck,Room}/`、
  `UI/{PanelFactory,CrUiStyle}.cs`、`UI/Panels/` 里已有的 10 个面板、`Assets/Editor/`、`Assets/Resources/`）
- ⛔ 不改 `client/Assets/Scripts/Def/{MsgDef.cs,ProtoDef.cs}`、`Core/ClientConfig.cs`（冻结）
- ⛔ 不改 `client/Packages/manifest.json`、`CR.asmdef`
- ⛔ 不改 `server/**`、`tools/**`、`docs/**`、`策划/**`、`tools/ai-skill/**`
- ⛔ 不许写 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物**只许放** `<根>/.ai-tmp/`
- 只许读本项目、`clover-client-unity-engine`、`clover-server-engine`、`clover-doc`、skill；
  工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**

## 5. 详细要求

### 5.1 `Module/Battle/BattleManager.cs` + `BattleModuleHost.cs`
- **订阅 `PushBattleStart`**（`BattleStartNotify`）：记 `my_team` / `seed` / `timeline` / 双卡组 / 手牌。
  ⚠️ `AppFlow` 也订阅它（引擎 `Router` 支持同一 msgID 多处理器，`Runtime/Network/Router.cs:14-28`）——
  你**只消费**，不要重复发 `MsgAiBattleStart`。
- **订阅 `PushBattleSnapshot`**（10 Hz，`BestEffort`；服务端无 UDP 端点时引擎自动降级为可靠 TCP，**不会丢**）：
  双帧缓冲 —— 保留「上一帧 + 当前帧 + 当前帧到达时间」，对外给 `Sample(nowMs)` 做**插值**（D4）。
  ⛔ 不许把快照坐标直接贴到 Transform（10 Hz 会明显抖）。
  - 服务端坐标是 **1/1000 格**（`x_milli`/`y_milli`），换算**只许**走 `Core/GameConst.cs`。
  - `RED` 侧的镜像规则必须与服务端 `core/arena.go` 一致（`GameConst` 里已有 `IsMirroredForTeam`）。
- **订阅 `PushBattleEvent`**：出牌/生成/死亡/塔毁/圣水满/塔激活 → 广播事件给 `View` 播表现。
  ⚠️ 离散事件与快照**都会到**（快照 10 Hz、事件可靠）—— 去重规则写清楚（例如塔毁只播一次）。
- **订阅 `PushBattleEnd`**：交给 agent-08 的 `ResultPanel`（你只 `Emit`，不建面板）。
- **上行**：`BattlePlayCard`（`room_id`/`card_id`/`x_milli`/`y_milli`）、`BattleSurrender`、`BattleSync`。
  出牌请求被服务端拒（`ok=false`）时，必须**把原因显示给玩家**（`Emit` 出去，⛔ 不许只打日志），
  并**回滚本地乐观表现**（如果做了乐观表现）。
- **进场**：`RequestEnterBattle` 之后先 `BattleSync` 拉一次全量，再等周期快照。

### 5.2 `View/ArenaView.cs` —— 竞技场与塔
- ⚠️ **`arena_training_out` 的 23 帧不是一张完整底图**，是**同一画布（1090×1677）上的分层小图元**
  （agent-05 抽查确认）。⇒ 你需要**按层合成**出竞技场：先目视认出各帧是什么（地面/河/桥/边界/装饰），
  再决定叠加顺序。**把"哪一帧是什么"的结论写在代码注释里**（这是你唯一的判断依据）。
  ⛔ 认不出来就**如实报缺**，用同尺寸的纯色/渐变底图兜底并**在回报里登记**，不许编。
- 6 座塔：`building_tower_out`（214 帧）与 `ResPaths.TowerFrame`。塔位**只许**取自 `GameConst`
  （BLUE 国王塔 (9,3) / 公主塔 (3.5,6.5) 与 (14.5,6.5)，RED 按 `y → 32 - y` 镜像）。
- 相机：竞技场 18×32 格，竖屏构图（参考原版）。用 `Game.Camera` 或直接摆 `Camera`，写清选择理由。

### 5.3 `View/UnitView.cs`（+ 池化）—— 单位
- 每个实体（`EntitySnapshot`）一个 `UnitView`：逐帧精灵（`Game.Res.LoadAsset<Sprite>`）+ `anim` 档位
  （0=idle 1=walk 2=attack 3=die，D5）+ `WorldHpBar`（D6，`SetRatio(hp/max_hp)`）。
- 精灵目录：`ResPaths.UnitFrame(cardKey, frameIndex)`（帧名已规范化为 `frame_NNN.png`）。
  ⚠️ 单位表里 `sprite_dir` 指向的角色目录已拷进工程（38 个 `chr_*`）。
- 单位复用走 `Game.Pool`/`ReferencePool`；⛔ 不许每次 `Instantiate` 新对象。
- 朝向：`facing`（-1/1）→ `localScale.x` 符号或 `SpriteRenderer.flipX`，写清选择理由。
- **部署期**（`deploy_ms > 0`）：原版表现是半透明/未激活 —— 按此实现，不要显示为正常状态。

### 5.4 `View/HandView.cs` + `UI/Panels/HudPanel.cs` —— 手牌与 HUD
- 手牌 4 张 + 下一张预览（`hand_a[]` / `next_a`，或按 `my_team` 取 `hand_b`/`next_b`——**取哪个由 `my_team` 决定**）。
- 圣水条：`elixir_a`/`elixir_b`（1/1000，0..10000，取自己那侧）+ `timeline.max_elixir`。
  用 `UIFactory.SetBarWidth(rt, progress01)` 或滑块，**不要**用无 sprite 的 `Image.fillAmount` 画进度条。
- 计时 + 阶段（`phase` 0=normal 1=overtime 2=ended；`server_ms`）+ 冠数（`crowns_a`/`crowns_b`）。
- **拖放出牌（D9）**：
  - 按下手牌 → 跟随 `Game.Input.MousePosition` → 显示落点指示（合法/非法两色）→ 抬起发 C2S。
  - 合法性**客户端预校验**必须用**同一套 `GameConst` 几何**（半场 + 河/桥 + 摧毁公主塔后的口袋区），
    ⛔ 不许在面板里另写一套判定。最终裁决永远在服务端。
  - ⛔ 不许直接用 `UnityEngine.Input`（用 `Game.Input`）。
- **卡面图不可得**（原版卡面是匿名编号帧、无 key→序号映射）⇒ 手牌与卡格用
  「**圣水数 + 中文名 + 类型色**」呈现（`DeckEditPanel` 已是这个做法，**保持一致**）。
  ⛔ 不许编映射表、不许拿色块假冒卡面图标。若你在素材里**确实**认出了卡面对应关系，
  可以登记到 `ResPaths` 的注释里并在回报中说明依据（目视认图），**但不要编**。

### 5.5 依赖方向（硬约束）
- `View/**` 与 `HudPanel` ⛔ **不许** `using CR.Module` —— 只 `On` 事件（`Events.Battle.*`）。
- `HudPanel` 的订阅在 `OnOpen` 注册、`OnClose` 注销（成对，不许泄漏）。

## 6. 验收标准

- [ ] 文件存在且**无越界**（`Assets/Scripts/` 下只有你负责的新文件）
- [ ] **离线编译自检**：`compile-check.ps1` 报 `errors in client/Assets = 0`（贴改前/改后两次）
- [ ] ⛔ grep 不到裸 `Debug.Log` / 裸消息号字面量 / 裸事件名字符串 / 裸资源路径字符串
- [ ] ⛔ grep 不到 `UnityEngine.Input` / `PlayerPrefs` / `GameObject.Find` / `Resources.Load` / `SceneManager.`
- [ ] `View/**` 与 `HudPanel.cs` 里**没有** `using CR.Module`
- [ ] **插值确实存在**：贴出双帧缓冲 + `Sample(nowMs)` 的代码，并说明"为什么直接贴坐标会抖"
- [ ] **几何只来自 `GameConst`**：贴出 `ArenaView` 里塔位与坐标换算的代码，证明没有散落魔法数字
- [ ] 面板订阅/注销成对：贴 `OnOpen`/`OnClose` 代码
- [ ] 竞技场底图**合成依据**：逐帧说明"第 N 帧是什么、为什么这个叠加顺序"（认不出的如实报缺）
- [ ] `docs/client-api-reference.md` 里查不到的 API **一个都没用**（逐条核对并说明）
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出（如实贴）
- [ ] ⛔ 项目里没有 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物只在 `.ai-tmp/`

> ⚠️ 本片**无法用编辑器验证**（编辑器未开）。表现类判据（竞技场观感、动画是否对、拖放是否顺手）
> 由主 agent 在编辑器打开后采一次**联络图**（`reference/visual-loop.md` 第八节：一张多格联络图，⛔ 不是逐项截图）。
> 你只需保证**代码按契约写对 + 离线自检通过**。

## 7. 约束

- 改动四拍：① 只读取证 + 改动清单 → ② 批量写（⛔ 不编译）→ ③ **一次**离线编译自检 → ④ 集中出证据
- ⛔ 不许改契约；不许再派生任何子 agent
- 注释写清**为什么**（尤其"为什么这么摆/为什么用这个 API/为什么这个叠加顺序"）

## 8. 回报格式

```
产出物：<绝对路径清单 + 行数>
对面板/模块的 public 接口：<逐条签名>
竞技场底图合成：<逐帧结论 + 叠加顺序 + 依据；认不出的如实报缺>
离线编译自检：<改前 / 改后 两次的 total errors 与 errors in client/Assets>
静态自检：<每条 grep 命令 + 结果>
查不到的 API / 拿不到的素材：<逐条列，不许编>
未决：无 / <具体条目>
```
