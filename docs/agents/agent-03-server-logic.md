# agent-03：服务端业务层（房间 + 对局 handler + AI）

## 0. 技能（开工必做）

**(1) 拿到 skill。**
- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
- 全局兜底（按序命中即用）：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 有 `use_skill` 就先加载 `clover-engine`
- 服务端范式必读：`patterns/handler.md`、`patterns/datadef.md`、`patterns/mount.md`、
  `patterns/signup-login.md`、`reference/conventions.md`
- **房间必读**：`clover-doc/server/examples/room.md`（引擎房间范例）+ `clover-doc/server/concepts/push.md`
- 找不到 → 回报主 agent 要路径，**不许凭记忆写代码**

**(2) 按 skill 的「混合模式找依据」查代码**（用户 > 引擎 > 联网/自创），**不许编 API**。

⛔ **红线**：只许读本项目、`clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**。⛔ **不许改任何 skill**。

## 1. 目标

让服务端**整条链路真跑通**：登录 → 设昵称 → 拉卡池 / 存卡组 → 建房 / 拉列表 / 加入 / 准备 / 开打 →
出牌 → 快照推送 → 结算 → 回大厅；外加**人机对战**与**房间 AI 补位**。

## 2. 任务边界

**只做**：
- `server/game/logic/*.go`（**整体替换** agent-01 留下的占位 `logic.go`）
- `server/game/core/ai.go`（★ AI 决策；`core/` 其余文件 **只读**）

**绝不做**：
- ⛔ 不改 `server/game/core/` 的**其它任何文件**（`units/arena/card/entity/hand/elixir/pathing/targeting/combat/tower/rules/battle/snapshot.go` 全部只读）
- ⛔ 不改 `server/game/def/`、`server/game/datadef/`、`server/game/table/`（只读；契约已冻结）
- ⛔ 不改 `server/main.go`、`server/configs/`、`server/go.mod`
- ⛔ 不碰 `client/`、`tools/`（含 `verify.ps1`）、`docs/`、`策划/`
- ⛔ 不许写"交接文档"；未完成项只写在**回报消息**里

## 3. 前置依赖（已就绪，全是只读）

### 3.1 协议（`server/game/def/`，已冻结）
22 个消息号与结构体，逐字见 `docs/步骤文档.md` §4.1。要点：
- 取请求 `c.BindMsg(&req)`；回包 `l.g.Reply(c, def.XxxReply{...})`
- 推送 `l.g.PushToPlayer(playerID, def.PushXxx, payload, proto.DeliveryModeReliable)`；
  快照用 `proto.DeliveryModeBestEffort`（走 UDP）
- ⛔ **回包不定义消息号**（按 requestID 配对）
- 已有结构体：见 `server/game/def/{msg,push,types}.go`（含 `CardInfo`、`RoomInfo`、
  `RoomStateNotify`、`BattleStartNotify`、`BattleSnapshot`、`BattleEventNotify`、`BattleEndNotify`）

### 3.2 数据层（`server/game/datadef/player.go`）
`PlayerSchema`（`Type:"player"`, `OwnerPlayer`, `ClientSelfOnly`）+ `PlayerData{Nickname, Deck []int32, Wins, Losses}`。
用法：`l.g.LoadStruct(c, datadef.PlayerSchema, c.PlayerID(), &v)` → 改字段 → handler 返回自动 Commit。

### 3.3 配表（`server/game/table/`，agent-01 生成，⛔ 不许手改）
- `table.Default.卡牌.Get(id)` / `table.Default.战斗单位.Get(id)` / `table.Default.法术.Get(id)`
- 加载：`tbl := table.NewTables(); tbl.LoadAll(dir)`，`dir` 指向 `server/game/table/tsv`
  （`卡牌.tsv` / `战斗单位.tsv` / `法术.tsv`）
- 行类型在 `server/game/table/base/`（`base_卡牌.go` / `base_战斗单位.go` / `base_法术.go`），
  **字段名以那三个文件为准**（先读它们，不要猜列名）

### 3.4 对局内核（`server/game/core/`，agent-02 已交付，⛔ 除 `ai.go` 外只读）
公开接口（逐字见 `docs/agents/agent-02-battle-core.md` §4.1）：

```go
a := core.NewArena()
b, err := core.NewBattle(core.Config{Seed: seed, Table: cardTable, DeckA: deckA, DeckB: deckB})
b.Step()                                                  // 推进 1 tick = 50 ms
b.PlayCard(core.TeamBlue, cardID, xMilli, yMilli) error   // 出牌（AI 也走这一个入口）
b.Surrender(core.TeamRed)
b.Ended() bool
b.Result() core.Result
b.Snapshot() core.Snapshot
b.DrainEvents() []core.Event
b.Elixir(team) int32 / b.Hand(team) []int32 / b.Next(team) int32 / b.Crowns(team) int32
a.CanDeploy(team, xMilli, yMilli, anywhere, onWater, fallenEnemyPrincess []core.TowerRef) bool
```

> ⚠️ `core.Snapshot` 的字段与 `def.BattleSnapshot` **逐字对应**（agent-02 已按契约实现）。
> 你需要写一个**适配函数**把 `core.Snapshot` → `def.BattleSnapshot`（`logic/` 里做，别改 core）。
> 塔**不在** `core.Snapshot.Entities` 里（由 `TowersA/TowersB` 报告）。

## 4. 产出物（绝对路径）

| 文件 | 内容 |
|---|---|
| `server/game/logic/logic.go` | **替换占位**：`app.Mount(app.RoleGame, ...)` 内做全部装配（房间模块、配表、handler 注册、断线回调） |
| `server/game/logic/cardtable.go` | 把 `table.Default.*` 适配成 `core.CardTable`（`Card(id)` / `Unit(key)`） |
| `server/game/logic/player.go` | `onSetNickname` / `onGetProfile` |
| `server/game/logic/deck.go` | `onGetCardPool` / `onGetDeck` / `onSaveDeck`（8 张校验） |
| `server/game/logic/room.go` | `onRoomCreate` / `onRoomList` / `onRoomJoin` / `onRoomLeave` / `onRoomReady` / `onRoomStart` / `onRoomSetAi` + 房间注册表 + 房间状态广播 |
| `server/game/logic/battle.go` | `onBattlePlayCard` / `onBattleSurrender` / `onBattleSync` + 对局 tick 驱动 + 快照/事件/结算推送 |
| `server/game/logic/ai.go` | `onAiBattleStart`（主菜单人机对战）+ AI 驱动的接线 |
| `server/game/core/ai.go` | ★ AI 决策（纯 Go，零引擎依赖；只依赖 `core` 自己的类型） |

## 5. 关键契约（★ 必须逐字按此实现）

### 5.1 房间：引擎 `room.Module` 做外壳 + 业务实现 `room.Kernel`

按 `clover-doc/server/examples/room.md` 的骨架：

```go
roomMod := room.NewModule(room.Config{
    MasterCaller: g,                                              // *app.Game 已实现
    Pusher:       func(pid string, msgID uint32, v any) error { return g.PushToPlayer(pid, msgID, v) },
    NodeAddr:     g.Addr(),
    Kernel:       myKernel,                                       // ★ 业务实现
})
```

`room.Kernel` 的 8 个方法（真身 `pkg/domain/room/kernel.go:29`）：
`EnsureRoom(roomID) error` / `Join(roomID, playerID) error` / `Leave(roomID, playerID) error` /
`Destroy(roomID) error` / `ExportState(roomID) (ExportPack, error)` / `ImportState(roomID, json.RawMessage) error` /
`Players(roomID) []string` / `Close()`。

- **业务侧维护房间元数据**（房间名 / 房主 / 成员昵称与准备态 / `ai_fill` / 是否已开打 / 卡组快照）
  与**对局实例**（`*core.Battle`）——放在 `logic/room.go` 的进程内注册表里，用 `sync.RWMutex` 保护。
- ⛔ **不要** `SetInputApplier`、⛔ **不要**依赖引擎帧推（`PushMessageID` 默认 0 = 静默不推）。
  快照由**业务自己** `g.PushToPlayer(..., proto.DeliveryModeBestEffort)` 推。
- `EnsureRoom` 必须**幂等**（重复建房同 ID 不报错）。

### 5.2 对局驱动
- 开打时：建房成功 → 双方都 `Ready`（或房主 `ai_fill` 直接开）→ 生成 `seed` →
  `core.NewBattle(...)` → 双方各推一次 `PushBattleStart`。
- **tick 循环**：用 `g.Timer.Every(name, 50*time.Millisecond, task)`，`name` 带房间号（如 `"battle:"+roomID`）；
  每 tick 调 `b.Step()`；**每 2 tick**（= 100 ms = 10 Hz）推一次 `PushBattleSnapshot`；
  tick 后立刻 `DrainEvents()`，非空则推 `PushBattleEvent`（Reliable）。
- 结算：`b.Ended()` ⇒ 推 `PushBattleEnd`（Reliable）给双方，更新 `PlayerData.Wins/Losses`（**用 `LoadStruct` 改再让引擎 Commit**），
  `g.Timer.StopTimer(name)`，清理对局实例，房间回到未开打态（**能再来一局**）。
- `MsgBattlePlayCard`：校验 `room_id` 匹配 + 玩家在房内 + 对局进行中 → 调 `b.PlayCard(...)` →
  成功 `Reply{OK:true}`，失败 `Reply{OK:false, Err:...}`（**失败必须带原因**）。
  ★ 出牌是**唯一**入口，AI 也走它 —— 不许给 AI 开后门。
- `MsgBattleSync`：返回 `def.BattleSyncReply{Snapshot: adapterOf(b.Snapshot())}`。

### 5.3 定时器 scope（★ 引擎约束）
- 随掉线自动清理的任务 ⇒ `scope` **必须等于连接级 owner**（`c.Account()` / 登录回执的 owner）。
- 对局 tick **不随掉线停**（对方还要继续打）⇒ 用**带前缀**的独立 scope（如 `"battle:"+roomID`），
  显式 `StopTimerGroup`。

### 5.4 AI（用户点名功能，两条入口）
1. **训练场**（`MsgAiBattleStart`）：直接建一个"房主 + AI"的房间并开打（复用真人路径的所有校验）。
2. **房间 AI 补位**（`MsgRoomSetAi` + `MsgRoomStart`）：房主开 `ai_fill` 后即使只有 1 个真人也开打，
   对手位置由 AI 顶。

**AI 决策（`core/ai.go`）**：`func Decide(b *Battle, team Team) (cardID int32, xMilli, yMilli int32, ok bool)`
- 每 **500 ms** 决策一次（不是每 tick，省算力）；由 `logic` 侧按 tick 计数调用。
- 策略（够"像人"即可，不必最强）：
  ① 若对手地面部队已越过桥、进入己方半场 ⇒ **防守**：优先用能打到它的 cheapest 手牌，
     落点选**己方塔前或桥头附近**（离威胁单位 3~5 格）；
  ② 否则圣水 ≥ 7（或已到双倍/三倍段且 ≥ 6）⇒ **进攻**：从手牌挑圣水消耗最大的**部队卡**，
     落在**己方桥头**（蓝方 y≈13.5 格 / 红方 y≈18.5 格）靠近威胁较小的那一路；
  ③ 法术卡只在能覆盖 ≥ 2 个敌方单位（或能补刀塔）时使用；
  ④ 圣水不足 / 无可选卡 ⇒ `ok=false`，什么都不做。
- **所有落点必须过 `arena.CanDeploy`** —— 不许绕过校验（这是"AI 与真人同一条路"的机械保证）。

### 5.5 日志（★ 硬约束）
服务端日志是**包级函数**：`logger.Infof/Warnf/Errorf`（`clover-server-engine/pkg/foundation/logger`）。
⛔ `Game` 上**没有** `Logger` 方法；⛔ 不许 `log.Printf` / `fmt.Print`。
**每条非预期分支都要打日志**（含 `switch` 的 default、房间不存在、卡组非法、出牌被拒的原因）。

## 6. 验收标准

- [ ] `cd server && go build ./... && go vet ./...` 全绿
- [ ] `go test ./game/core/... -count=1` 仍全绿（**你没弄坏 core**）
- [ ] `go run . -config configs/all` 起服无 panic/error；日志里能看到配表加载 + handler 注册
- [ ] **端到端真跑**（用 `clover-server-tools/msg-client`，没有 `signup` 子命令就先
      `go build -o ./msg-client.exe ./client` 自己编一个；
      ⛔ 不许把它留在 `clover-tools/`）：
      ① 注册两个账号 → ② 各设昵称 → ③ 各存 8 张卡组 → ④ A 建房 → ⑤ B 拉列表看到该房 →
      ⑥ B 加入 → ⑦ 双方 Ready → ⑧ A 开打 → ⑨ 双方都收到 `PushBattleStart` →
      ⑩ A 出一张牌（成功）+ 出一张越界牌（应被拒并带原因）→ ⑪ 收到 ≥5 条 `PushBattleSnapshot` →
      ⑫ 收到 `PushBattleEvent` → ⑬ A 投降 → 双方收到 `PushBattleEnd` → ⑭ 房间可再来一局
      **把每一步的原始报文/日志贴进回报**
- [ ] **人机对战**：`MsgAiBattleStart` → 收到 `PushBattleStart` + 快照里能看到 AI 出的牌
      （AI 至少出过 1 张牌；把日志贴出来）
- [ ] **房间 AI 补位**：单人建房 + `ai_fill=true` → 开打成功
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出（如实贴）
- [ ] ⛔ 项目里**没有** `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物**只在** `.ai-tmp/`

## 7. 约束

- **改动四拍**：① 只读取证 + 改动清单（⛔ 不写代码）→ ② 批量改（⛔ 不编译）→ ③ **一次**编译 + 预演 →
  ④ 集中出证据。⛔ 不许"改一处编译一次"。
- ⛔ 不许改契约（消息号 / 协议字段 / `core` 的公开签名）；发现问题先回报。
- ⛔ 不许再派生任何子 agent。
- ⛔ 玩法规则只能在 `core/`；`logic/` 里出现伤害/速度/射程/胜负分支一律算错。
  （`logic/` 允许做的判断只有：房间/成员/回合状态、`CanDeploy` 的调用、配表适配、协议编解码。）

## 8. 回报格式

```
产出物：<绝对路径清单>
自检：<命令 + 原始输出：go build / go vet / go test / 起服日志 / 端到端 14 步的原始报文 / AI 出牌日志>
未决：无 / <具体条目>
```
