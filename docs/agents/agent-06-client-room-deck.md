# agent-06：客户端卡组编辑 + 房间（列表 / 创建 / 加入 / 房内准备 / AI 补位）

## 0. 技能（开工必做）

- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
- 全局兜底：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 客户端范式：`patterns/client/app-flow.md`、`patterns/client/ui.md`、`patterns/client/network.md`
- 有 `use_skill` 就先加载 `clover-engine`
- ⛔ 不许改任何 skill（发现问题写进回报）

## 1. ★★ 三份必读（写第一行代码之前）

| 文档 | 为什么 |
|---|---|
| `<项目根>/docs/client-architecture.md` | 架构契约：面板驱动不切场景、D1–D11、目录与依赖方向（⛔ UI 不许 `using CR.Module`，只发事件） |
| `<项目根>/docs/client-api-reference.md` | 引擎 API 逐字摘要（含 `文件:行`）+ §0「已确认不存在的东西」+ §1 的四个实测纠正。⛔ 每个 `Game.xxx` 调用前先在这里查到它 |
| `<项目根>/docs/步骤文档.md` §4.1 | 协议契约（已冻结，与 `client/Assets/Scripts/Def/` 逐字对应） |

**还要读 agent-05 的产出**（你直接依赖它，⛔ 不要改它的文件）：
- `client/Assets/Scripts/Module/Flow/AppFlow.cs` —— 站点编排 + 它对外暴露的 12 个 public 方法
- `client/Assets/Scripts/Core/Events.cs` —— **事件名唯一定义处**（`Events.Room.*` 13 个 / `Events.Deck.*` 4 个，
  每条注释里写了参数类型）
- `client/Assets/Scripts/Core/Stations.cs` / `ResPaths.cs` / `GameConst.cs`
- `client/Assets/Scripts/UI/{PanelFactory.cs,CrUiStyle.cs}` —— 面板基建与共用样式（**照它的写法建面板**）

## 2. ⚠️ 本片的特殊前提：**编辑器还没开，你无法编译**

Unity 工程已建好、manifest 已冻结；**编辑器由用户稍后打开**（半成品就打开 ⇒ Safe Mode ⇒ 主 agent 失去驱动能力）。
- ⛔ **绝对不许**跑 `unity run` / `unity test` / `Unity.exe -batchmode` / `unity command editor_play` / 启动编辑器
- ✅ 按契约把代码一次写对；**每个引擎 API 调用都要在 `docs/client-api-reference.md` 里查得到**；
  宁可用笨一点但确定存在的 API，也不要用"看起来对"的 API
- ✅ 有一条**可用的离线编译自检**（agent-05 做的，复用不要重写）：
  `powershell -NoProfile -ExecutionPolicy Bypass -File <根>/.ai-tmp/hosts/compile-check.ps1`
  （用 Unity 自带 Roslyn + 真 UnityEngine/UnityEditor DLL 编译 `client/Assets`；**目标 = `errors in client/Assets = 0`**）
  先跑一次基线，改完再跑一次，把两次的 `total errors / errors in client/Assets` 都贴进回报。

## 3. 目标（用户点名功能）

> 用户原话：「**要能创建房间，和人一起玩**」「房间列表加入」「人机 PK = 训练场入口 + 房间 AI 补位」

本片交付：**卡组编辑**（60 张卡池选 8 张）+ **房间列表**（浏览/创建/加入）+ **房间内**（成员/准备/设 AI 补位/开打）。

## 4. 任务边界（⛔ 严格）

**只做**：
- `client/Assets/Scripts/Module/Deck/**`
- `client/Assets/Scripts/Module/Room/**`
- `client/Assets/Scripts/UI/Panels/{DeckEditPanel.cs,RoomListPanel.cs,RoomPanel.cs}`

**绝不做**：
- ⛔ 不改 agent-05 的任何文件（`App/Bootstrap.cs`、`Module/Flow/**`、`Module/Settings/**`、`Core/**`、
  `UI/{PanelFactory,CrUiStyle}.cs`、`UI/Panels/{BootPanel,LoginPanel,RegisterPanel,NicknamePanel,MainMenuPanel,SettingsPanel,LoadingPanel}.cs`、
  `Assets/Editor/**`、`Assets/Resources/**`）
- ⛔ 不改 `client/Assets/Scripts/Def/{MsgDef.cs,ProtoDef.cs}`、`Core/ClientConfig.cs`（冻结）
- ⛔ 不写 `Module/Battle/**`、`View/**`、`UI/Panels/{HudPanel,PausePanel,ResultPanel}.cs`（agent-07/08 的地盘）
- ⛔ 不改 `client/Packages/manifest.json`、`CR.asmdef`
- ⛔ 不改 `server/**`、`tools/**`（含 `verify.ps1`）、`docs/**`、`策划/**`、`tools/ai-skill/**`
- ⛔ 不许写 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物**只许放** `<根>/.ai-tmp/`
- 只许读本项目、`clover-client-unity-engine`、`clover-server-engine`、`clover-doc`、skill；
  工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**

## 5. 详细要求

### 5.1 `Module/Deck/DeckManager.cs`
- `MsgDef.GetCardPool` → 60 张卡（`CardInfo[]`，含 `id/key/name_cn/type/rarity/elixir/icon`）。
  首次进主菜单/卡组面板时拉一次并缓存。
- `MsgDef.GetDeck` / `MsgDef.SaveDeck`：保存必须**客户端先校验**再发（8 张、不重复、都是卡池里的 id），
  失败把原因显示在面板上（⛔ 不许只打日志）。
- 卡面图：`CardInfo.icon` 是**服务端给的图名**，客户端拼 `ResPaths`。⚠️ agent-05 已登记：
  原版卡面素材是**匿名编号帧**、解包素材里**没有 key→序号映射表** ⇒ 若拼不出真实卡面，
  **在回报里如实登记**，面板先用「圣水数 + 中文名 + 类型色块」呈现（⛔ 不许拿色块假冒图标、不许编映射表）。

### 5.2 `Module/Room/RoomManager.cs`
C2S 门面，逐条对应已冻结协议：`RoomCreate` / `RoomList` / `RoomJoin` / `RoomLeave` / `RoomReady` /
`RoomStart` / `RoomSetAi`；订阅推送 `PushRoomList` / `PushRoomState` / `PushBattleStart`。
- ⛔ **不要**自己实现「人机对战」的发送：`AppFlow.RequestStartAiBattle()` **已经实现**了
  `MsgAiBattleStart` 的发送与 `PushBattleStart` 的收尾。你只消费事件。
- `PushBattleStart` 到了 ⇒ 调 `AppFlow.Instance.RequestEnterBattle(roomId, start)`
  （**它是按 roomId 幂等的**，重复调安全）。
- 房间列表要**能刷新**；房主才能 `RoomStart`/`RoomSetAi`（非房主按钮置灰并说明原因）。

### 5.3 三个面板（按 `CrUiStyle` 的写法，用 `UIFactory` 在 `OnOpen` 里建树）
- `DeckEditPanel`：60 张卡格（可滚动或分页）+ 已选 8 格 + 保存/取消。显示圣水消耗与中文名。
- `RoomListPanel`：房间列表（房号/房名/人数/是否 AI 补位/是否已开打）+ 创建房间（输入房名）+ 刷新 + 加入。
- `RoomPanel`：成员列表（昵称/是否房主/是否 AI/准备态）+ 准备/取消 + 房主「AI 补位」开关 + 房主「开始对战」+ 离开。

### 5.4 依赖方向（硬约束）
- ⛔ 三个面板里**不许** `using CR.Module` —— 只 `Emit` `Events.Room.*` / `Events.Deck.*`，
  由 `RoomManager` / `DeckManager` 订阅并处理。`RoomManager`/`DeckManager` 处理完再 `Emit` 回面板要的数据。
- 面板注册/注销订阅必须在 `OnOpen` / `OnClose` 成对出现，⛔ 不许泄漏（面板会被反复开关）。

## 6. 验收标准

- [ ] 三个 `.cs` + 两个 Manager 存在；`Assets/Scripts/` 下**没有**越界文件
- [ ] **离线编译自检**：`compile-check.ps1` 报 `errors in client/Assets = 0`（贴改前/改后两次输出）
- [ ] ⛔ grep 不到裸 `Debug.Log`（一律 `Game.Logger.*`）、裸消息号字面量、裸事件名字符串、裸资源路径字符串
- [ ] ⛔ grep 不到 `UnityEngine.Input` / `PlayerPrefs` / `GameObject.Find` / `Resources.Load` / `SceneManager.`
- [ ] `UI/Panels/*.cs` 里**没有** `using CR.Module`；面板的 `Emit`/`On` 在 `OnOpen`/`OnClose` 成对
- [ ] 面板订阅/注销成对：贴出 `OnOpen` 与 `OnClose` 的代码
- [ ] `docs/client-api-reference.md` 里查不到的 API **一个都没用**（逐条核对并在回报里说明）
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出（如实贴）
- [ ] ⛔ 项目里没有 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物只在 `.ai-tmp/`

> ⚠️ 本片**无法用编辑器验证**（编辑器未开）。表现类（面板观感、按钮是否好点）由主 agent 在编辑器打开后采一次联络图；
> 服务端交互由主 agent 跑端到端。你只需保证**代码按契约写对 + 离线自检通过**。

## 7. 约束

- 改动四拍：① 只读取证 + 改动清单 → ② 批量写（⛔ 不编译）→ ③ **一次**离线编译自检 → ④ 集中出证据
- ⛔ 不许改契约；不许再派生任何子 agent
- 注释写清**为什么**（尤其"为什么这么摆/为什么用这个 API"）

## 8. 回报格式

```
产出物：<绝对路径清单 + 行数>
对面板/模块的 public 接口：<逐条签名>
离线编译自检：<改前 / 改后 两次的 total errors 与 errors in client/Assets>
静态自检：<每条 grep 命令 + 结果>
查不到的 API / 拿不到的素材：<逐条列，不许编>
未决：无 / <具体条目>
```
