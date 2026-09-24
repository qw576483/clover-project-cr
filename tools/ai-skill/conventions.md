# 本项目约定（clover-project-cr）

## 模块名与命名空间

| 端 | 标识 | 说明 |
|---|---|---|
| Go module | `clover-cr` | `server/go.mod` |
| C# 根命名空间 | `CR` | 业务程序集 `Assets/Scripts/CR.asmdef`（`name`/`rootNamespace` 均为 `CR`） |
| 客户端消息号 | `CR.Def.MsgDef` | 唯一处 |
| 客户端协议体 | `CR.Def.ProtoDef` | 唯一处 |

## 消息号段分配

| 段 | 范围 | 用途 |
|---|---|---|
| 引擎 | 1 – 10000 | 引擎占用，业务不许用（硬约束：业务 C2S 必须 > 10000，启动期校验，误用 panic） |
| **C2S** | **1000101 起** | 玩家 1000101+ / 卡池卡组 1000201+ / 房间 1000301+ / 对局 1000401+ / 人机 1000501+ |
| 回包 | **不占消息号** | 引擎按 requestID 配对，回包帧 msgID 恒为 0；**只定义回复体结构，不定义常量** |
| **推送** | **3002001 起** | 房间列表 3002001 / 房间状态 3002002 / 开打 3002003 / 快照 3002004 / 事件 3002005 / 结算 3002006 |

> 权威清单见 `registry.md`；改名/改值必须**两端同一批改动**。

## 命名

- 服务端：消息常量 `MsgXxx` / `PushXxx`（`def` 包）；handler `onXxx`（`logic` 包方法）；
  schema 变量 `XxxSchema`；数据体 `XxxData`。
- 客户端：`MsgDef.Xxx` / `XxxReq` / `XxxReply` / `XxxNotify`；面板 `XxxPanel`；
  管理器 `XxxManager`；事件名 `Events.Xxx`（`Core/Events.cs`，**禁止裸字符串**）。
- **协议字段名一律 snake_case**（与服务端 `json` tag 对齐）。Go 侧用 `json:"x_milli"`，C# 侧字段名逐字对应。
- 对局坐标字段统一带单位后缀：`x_milli` / `y_milli`（**1/1000 格**）或 `x_tile` / `y_tile`（格，浮点）。
  ⛔ 不许出现无单位的 `x` / `y`。

## 目录边界

```
server/game/def/       消息号 + 协议结构体      —— 服务端唯一处，无逻辑
server/game/datadef/   数据 schema              —— 玩家档案
server/game/table/     打表产物                 —— ⛔ 自动生成，不许手改
server/game/core/      对局内核（纯 Go）        —— ⛔ 零引擎依赖；玩法规则唯一实现处
server/game/logic/     引擎 handler             —— 只做取参/调 core/回包推送
client/Assets/Scripts/Def/       消息号 + 协议  —— 客户端唯一处
client/Assets/Scripts/Core/      配置 + 事件名 + 常量
client/Assets/Scripts/Module/    业务模块（Flow/Room/Battle/Deck/Settings）
client/Assets/Scripts/UI/Panels/ 面板（UIPanel 子类，⛔ 不许 using CR.Module）
client/Assets/Scripts/View/      战斗表现（竞技场/单位/血条/特效）
client/Assets/Scripts/App/       唯一组装点 Bootstrap（≤200 行）
策划/数值文档/                  配表源表 + -pack.xlsx
策划/策划案/                    参考规格 + 策划案
docs/                           过程文档（步骤文档 + agent 任务书），任务结束即冻结
原版资源/                       下载/解包的 A 素材（含素材调研.md 里的清单）
.ai-tmp/test|hosts|drivers/     一次性产物（⛔ 不许散落到别处）
```

## 数据 / 配表约定

- **配表取值下标**：`值 = <字段>_per_level[ rarities[稀有度].tournament_level_index ]`。
  官方 `rarities.json` 给的是 **Common 10 / Rare 8 / Epic 5 / Legendary 2 / Champion 0**。
  ⛔ 不许"一律取索引 10"（只对 Common 成立，对 Epic 会错 60%）。
- **配表访问器**：`table.Default.卡牌.Get(id)` / `table.Default.战斗单位.Get(id)` / `table.Default.法术.Get(id)`
  （表名中文 ⇒ 标识符中文，Go 合法；`server/game/table/registry.go` 是生成物）。
- **非卡牌实体的 `key`** = **官方实体名逐字**（`Skeleton` / `PrincessTower` / `TowerPrincessProjectile`）。
- **打表**：源表改动后必须 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1 -Force`
  （不带 `-Force` 不会覆盖已存在的 `*-pack.xlsx`，tsv 会保留旧值）。

## 对局内核（`server/game/core/`）约定

- ⛔ **零引擎依赖**（不许 import 任何 `clover-server-engine/...`）—— 要查时 `grep -r "clover-server-engine" server/` 一次即可（`tools/verify.ps1` 只是可选脚本，⛔ 不必跑）。
- 位置/血量/伤害一律**整数定点**；位置单位 = **1/1000 格**。
- 手牌 / 圣水 / 实体 / 塔全部由 `Battle` 持有；对外只有 `Step()` / `PlayCard()` / `Surrender()` /
  `Snapshot()` / `DrainEvents()` / `Result()` 几个口。
- **塔不在 `Snapshot.Entities` 里**（由 `TowersA/TowersB` 报告）。
- AI 只通过 `PlayCard()` 这一个入口出手（与真人共用同一校验路径）。

## 与全局 skill 的差异（**只许记"加严"，不许记"放宽"**）

| 项 | 全局 skill 写法 | 本项目写法 | 性质 | 原因 |
| --- | --- | --- | --- | --- |
| 服务端 tick 率 | 未规定 | `core/` 固定 **20 TPS（50 ms/帧）**，常量集中在 `core/units.go` | 加严 | 官方所有时长均为 50 ms 倍数，20 TPS 可整除；出处见参考规格 §1 |
| 位置精度 | 未规定 | 一律 **整数定点 1/1000 格（milli-tile）**，⛔ 不许用 float 存位置 | 加严 | 与官方数据同尺度，避免浮点漂移导致两端不一致 |
| 玩法规则落点 | 未规定 | 只许在 `server/game/core/`，`logic/` 不许有规则分支 | 加严 | 保证玩法可离线秒级断言，不依赖起服 |
| 对局快照频率 | 未规定 | 固定 **每 100 ms 一次（10 Hz）**，走 `DeliveryModeBestEffort` | 加严 | 20 TPS 全量推太重；10 Hz 够客户端插值 |
| 数值来源 | 未规定 | 只认 `策划/策划案/皇室战争参考规格.md` 里带出处的值 | 加严 | 满足全局 §0 铁律 3 |
| 本项目 git 仓库 | 跟随工作区 | **独立 git 仓库**，提交不触发任何 hook（⛔ 别把闸门挂在 `pre-commit` 上） | 加严 | 强制层作用域只在本项目，不干扰父仓库 |
