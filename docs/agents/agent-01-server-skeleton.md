# agent-01：服务端骨架 + 协议 + 数据层 + 配表打表

## 0. 技能（开工必做）

开工先做两件事：

**(1) 拿到 skill。** 工具集里有 `use_skill` 就用它加载 `clover-engine`；没有就直接读文件（命中即用，不必读完）：
- `<项目根>/tools/ai-skill/SKILL.md` ← **项目级，首选**（本项目约定 / 消息号 / 配表 / 约束）
  —— 同时读 `conventions.md`、`registry.md`、`constraints.md` 三册
- 全局兜底（按序命中即用）：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 服务端范式必读：`patterns/handler.md`、`patterns/datadef.md`、`patterns/table.md`、`patterns/mount.md`、`reference/conventions.md`、`scaffold/new-project.md` §1
- 都找不到 → 回报主 agent 要路径，**不许凭记忆写代码**。

> ⛔ **全局 skill 的规则层（§0~§7）不可被项目级覆盖**；两者冲突以全局为准。

**(2) 按 skill 的「混合模式找依据」查代码**（用户 > 引擎 > 联网/自创），**不许编 API**。

⛔ **红线**：只许读本项目（`<项目根>/…`）、引擎源码 `clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*`（源码 / `tools/ai-skill/` / `策划/` / `docs/` / Editor 生成器 / 素材）**一律不许读、不许 grep、不许照抄**。

⛔ **不许改任何 skill**（项目级 / 仓库源 / 宿主安装副本全算）。发现 skill 有错 ⇒ **写进回报**，由主 agent 改。

## 1. 目标

搭出 `clover-project-cr` 的**服务端骨架**（可 `go build`、可起服、无 panic），并完成**协议冻结**与**配表闭环**，
为后续 agent（对局内核 / 业务 handler / 客户端）提供确切的接口。

## 2. 任务边界

**只做**：
- `server/go.mod`、`server/main.go`、`server/configs/all/server.yaml`
- `server/game/def/`（消息号 + 协议结构体）
- `server/game/datadef/`（玩家档案 schema）
- `策划/数值文档/*.txt`（配表源表）+ `tools/table.ps1`（打表脚本）+ 打表产物
- `tools/hosts/` 下的一次性抽取脚本（放 `.ai-tmp/hosts/`）

**绝不做**：
- ⛔ 不写 `server/game/core/` 的任何文件（那是 agent-02 的地盘）
- ⛔ 不写 `server/game/logic/` 的任何文件（那是 agent-03 的地盘）
- ⛔ 不碰 `client/`（Unity 工程尚未创建）
- ⛔ 不改 `tools/ai-skill/`、`docs/`、`策划/策划案/`、`策划/素材调研.md`、`tools/verify.ps1`
- ⛔ 不写"交接文档"；未完成项只写在**回报消息**里

## 3. 前置依赖（已就绪）

- `<项目根>/策划/策划案/皇室战争参考规格.md` —— ★ **数值/几何/规则的唯一标准答案**（§4.1 塔、§6 卡池 60 张）
- `<项目根>/docs/步骤文档.md` —— ★ **消息号与协议契约（§4.1 / §4.2 / §4.3）**
- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md`
- 引擎源码：`c:\Work\Server\f-v2\clover-server-engine`；引擎 module 名 `clover-server-engine`
- 官方数据：`<项目根>/原版资源/cr-api-data/docs/json/`（Python 3.12 可用来解析；`ConvertFrom-Json` 读不了大文件）
- 已装好并**已启动**的服务端环境（etcd 2379 / nats 4222 / redis 6379 / mysql 3306），
  重启用 `clover-server-tools/windows-env/core/env.exe start`

## 4. 产出物（绝对路径）

### 4.1 `server/go.mod` —— ★ **已由主 agent 预建，不要重写**
内容已是：
```
module clover-cr

go 1.25.0

require clover-server-engine v0.0.0

go get github.com/qw576483/clover-server-engine@v0.1.0   # 引擎已发布为独立模块，业务侧不再用 replace（见 bug存档 §15.2）
```
你的任务：跑 `go mod tidy` 让它把 `require` / `go.sum` 补全（`replace` **必须保持绝对路径**）。
⛔ 不许改 `module` 名与 `replace` 路径。若 `go mod tidy` 因网络失败，如实回报，不要手编 `go.sum`。

### 4.2 `server/main.go`
照 `scaffold/new-project.md` §1.2 的模板，`import _ "clover-cr/game/logic"` 触发挂载。
（`logic` 包由 agent-03 建；为让本片**先可编译**，你**建一个最小占位** `server/game/logic/logic.go`：
`package logic` + `func init() { app.Mount(app.RoleGame, func(g *app.Game) {}) }`。
⛔ 只许建这一个占位文件，别写 handler —— agent-03 会整体替换它。）

### 4.3 `server/configs/all/server.yaml`
照 `scaffold/new-project.md` §1.3 的最小可用集，平铺结构。**本项目特化**：
- `server_type: "all"`
- `gateway.listen_ws: "127.0.0.1:8001"` / `listen_tcp: "127.0.0.1:8002"` / `listen_udp: "127.0.0.1:8003"` / `enable_wt: true`
- `logic.listen_addr: "127.0.0.1:8011"`、`auth.listen: "127.0.0.1:8051"`、`auth.verify_addr: "http://127.0.0.1:8051"`
- `gateway.reconnect_grace: 30s`、`gateway.disconnect_grace: 10s`（对局断线重连要用）
- `data.tier: "TierRedisMySQL"`（⛔ 不许 TierMemory）、`auto_create_table: true`
- 其余字段照模板写全（**监听地址缺失会静默降级**，必须显式写）

### 4.4 `server/game/def/msg.go`、`push.go`、`types.go`

★ **逐字实现 `docs/步骤文档.md` §4.1 的消息号表**（16 个 C2S + 6 个推送）。
- `msg.go` 放 C2S 常量 + 请求/回包结构体（Go 侧可放一起，或拆 `types.go`，由你定，但**文件名**用 `msg.go` / `push.go` / `types.go`）
- `push.go` 放推送常量 + 推送体
- **⛔ 回包不定义消息号常量**（回包帧 msgID 恒为 0）
- **JSON tag 一律 snake_case**，且与 `docs/步骤文档.md` §4.1 的字段名**逐字一致**
- 常量段位：玩家 1000101+ / 卡池卡组 1000201+ / 房间 1000301+ / 对局 1000401+ / 人机 1000501+；推送 3002001+

**必须定义的结构体（字段名以 §4.1 为准，逐个照抄）**：
`SetNicknameReq/Reply`、`GetProfileReq/Reply`、`GetCardPoolReq/Reply`、`GetDeckReq/Reply`、`SaveDeckReq/Reply`、
`RoomCreateReq/Reply`、`RoomListReq/Reply`、`RoomJoinReq/Reply`、`RoomLeaveReq/Reply`、`RoomReadyReq/Reply`、
`RoomStartReq/Reply`、`RoomSetAiReq/Reply`、`BattlePlayCardReq/Reply`、`BattleSurrenderReq/Reply`、
`BattleSyncReq/Reply`、`AiBattleStartReq/Reply`；
`RoomListNotify`、`RoomStateNotify`、`BattleStartNotify`、`BattleSnapshot`、`BattleEventNotify`、`BattleEndNotify`。

**`BattleSnapshot` 与 `BattleStartNotify` 的字段（★ 与 agent-02 的 `core.Snapshot` 对齐）**：

```go
type TowerState struct {
    ID    int32 `json:"id"`
    Kind  int32 `json:"kind"`   // 0=公主塔 1=国王塔
    HP    int32 `json:"hp"`
    MaxHP int32 `json:"max_hp"`
    Alive bool  `json:"alive"`
}
type EntitySnapshot struct {
    ID       int32 `json:"id"`
    Kind     int32 `json:"kind"`      // 0=部队 1=建筑 2=塔
    CardID   int32 `json:"card_id"`
    Team     int32 `json:"team"`      // 0=BLUE 1=RED
    XMilli   int32 `json:"x_milli"`   // 1/1000 格
    YMilli   int32 `json:"y_milli"`
    HP       int32 `json:"hp"`
    MaxHP    int32 `json:"max_hp"`
    Anim     int32 `json:"anim"`      // 0=idle 1=walk 2=attack 3=die
    Facing   int32 `json:"facing"`    // -1 / 1
    DeployMs int32 `json:"deploy_ms"` // 剩余部署时间
}
type BattleSnapshot struct {
    Seq      int32            `json:"seq"`
    ServerMs int32            `json:"server_ms"`  // 对局已进行毫秒
    Phase    int32            `json:"phase"`      // 0=normal 1=overtime 2=ended
    ElixirA  int32            `json:"elixir_a"`   // 1/1000，0..10000
    ElixirB  int32            `json:"elixir_b"`
    CrownsA  int32            `json:"crowns_a"`
    CrownsB  int32            `json:"crowns_b"`
    TowersA  []TowerState     `json:"towers_a"`
    TowersB  []TowerState     `json:"towers_b"`
    Entities []EntitySnapshot `json:"entities"`
    HandA    []int32          `json:"hand_a"`
    NextA    int32            `json:"next_a"`
    HandB    []int32          `json:"hand_b"`
    NextB    int32            `json:"next_b"`
}

type BattleTimeline struct {
    RegulationMs   int32   `json:"regulation_ms"`    // 180000
    OvertimeMs     int32   `json:"overtime_ms"`      // 120000
    StartingElixir int32   `json:"starting_elixir"`  // 6
    MaxElixir      int32   `json:"max_elixir"`       // 10
    ElixirMsPerUnit []int32 `json:"elixir_ms_per_unit"` // [2800,1400,930]
    ElixirPhaseMs   []int32 `json:"elixir_phase_ms"`    // [120000,120000,60000]
}
type BattleStartNotify struct {
    RoomID   string         `json:"room_id"`
    Seed     int64          `json:"seed"`
    MyTeam   int32          `json:"my_team"`
    Timeline BattleTimeline `json:"timeline"`
    DeckA    []int32        `json:"deck_a"`
    DeckB    []int32        `json:"deck_b"`
    HandA    []int32        `json:"hand_a"`
    NextA    int32          `json:"next_a"`
    HandB    []int32        `json:"hand_b"`
    NextB    int32          `json:"next_b"`
}
type BattleEvent struct {
    Kind     int32  `json:"kind"`      // 0=出牌 1=生成 2=死亡 3=塔毁 4=圣水满 5=塔激活
    CardID   int32  `json:"card_id"`
    XMilli   int32  `json:"x_milli"`
    YMilli   int32  `json:"y_milli"`
    EntityID int32  `json:"entity_id"`
    Team     int32  `json:"team"`
    Text     string `json:"text"`
}
type BattleEventNotify struct { Events []BattleEvent `json:"events"` }

type BattleEndNotify struct {
    Win      bool   `json:"win"`
    Draw     bool   `json:"draw"`
    CrownsA  int32  `json:"crowns_a"`
    CrownsB  int32  `json:"crowns_b"`
    Reason   string `json:"reason"` // king_destroyed | time_up_crowns | time_up_hp | surrender | draw
    HpRateA  int32  `json:"hp_rate_a"` // 剩余塔血占总上限的万分比
    HpRateB  int32  `json:"hp_rate_b"`
}
```

`RoomListReply` / `RoomListNotify` 的 `rooms` 元素类型 `RoomInfo`：
```go
type RoomInfo struct {
    RoomID   string `json:"room_id"`
    Name     string `json:"name"`
    Host     string `json:"host"`
    Cur      int32  `json:"cur"`
    Max      int32  `json:"max"`      // 固定 2
    AiFill   bool   `json:"ai_fill"`
    Started  bool   `json:"started"`
}
```
`RoomStateNotify`：
```go
type RoomMember struct {
    PlayerID string `json:"player_id"`
    Nickname string `json:"nickname"`
    Ready    bool   `json:"ready"`
    IsHost   bool   `json:"is_host"`
    IsAI     bool   `json:"is_ai"`
}
type RoomStateNotify struct {
    RoomID  string       `json:"room_id"`
    Name    string       `json:"name"`
    Host    string       `json:"host"`
    AiFill  bool         `json:"ai_fill"`
    Started bool         `json:"started"`
    Members []RoomMember `json:"members"`
}
```

### 4.5 `server/game/datadef/player.go`
```go
var PlayerSchema = data.StructSchema{
    Type:       "player",
    OwnerType:  data.OwnerPlayer,
    Visibility: data.ClientSelfOnly,
}
type PlayerData struct {
    Nickname string  `json:"nickname"`
    Deck     []int32 `json:"deck"`   // 8 个卡 id；空 = 未设置
    Wins     int32   `json:"wins"`
    Losses   int32   `json:"losses"`
}
func init() { data.RegisterTypeBySchema(PlayerSchema) }
```

### 4.6 配表（★ 走 `patterns/table.md` 闭环）

**三张源表**放 `<项目根>/策划/数值文档/`，**tab 分隔 txt，页签名 = 表名含 `_cs` 后缀**：

#### `卡牌_cs.txt` —— 60 行
列：`id` / `key` / `name_cn` / `name_en` / `type` / `rarity` / `elixir` / `arena` / `icon` / `sprite_dir` / `deploy_fx`
- `id`：本项目自定，`26010001` 起顺序编号
- `key`：与官方 `cards.json` 的 `key` 字段**逐字一致**
- `name_cn`：从 `cards_i18n.json` 取（拿不到就按`策划/策划案/皇室战争参考规格.md` §6 的中文名）
- `type`：`0`=部队 `1`=法术 `2`=建筑
- `rarity`：`0`=普通 `1`=稀有 `2`=史诗 `3`=传说
- `elixir` / `arena`：官方 `cards.json`
- `icon`：客户端用的卡面图名（如 `card_knight`），由 agent-04 决定；本片先按 `card_<key>` 约定填
- `sprite_dir`：素材目录名（如 `chr_knight_out`），取自参考规格 §6 的表
- **必须正好 60 行**，清单逐字照 `策划/策划案/皇室战争参考规格.md` §6

#### `战斗单位_cs.txt` —— 每个「战斗实体」一行
需要覆盖：60 张卡里所有会被召唤出来的单位 + 10 张建筑 + **公主塔 / 国王塔**。
列（★ 单位见 `docs/步骤文档.md` §4.2）：
`id` / `key` / `name_cn` / `kind` / `hp` / `damage` / `hit_speed_ms` / `load_time_ms` / `speed` /
`range_mt` / `sight_mt` / `deploy_ms` / `radius_mt` / `mass` / `atk_air` / `atk_ground` /
`only_buildings` / `only_towers` / `only_troops` / `projectile_key` /
`death_spawn_key` / `death_spawn_n` / `death_damage` / `death_aoe_radius_mt` /
`life_ms` / `spawn_key` / `spawn_n` / `spawn_radius_mt` / `spawn_interval_ms` / `spawn_limit` / `sprite_dir`

**取值规则（★ 铁律，不许编）**：
1. 一律取 **11 级 = 逐级数组索引 10**。口径已交叉验证：Knight `hitpoints_per_level[10]=1766`、
   `damage_per_level[10]=202`，与 `原版资源/cr-sim/reference/anchors.json` 的手工实测值**逐字一致**。
2. 卡片主单位：`cards_stats_characters.json`（按 `name_en` 关联 `cards_stats_troop.json` 的 `summon_character`）
   → `hitpoints_per_level[10]` / `damage_per_level[10]` / `hit_speed` / `load_time` / `speed` /
   `range` / `sight_range` / `deploy_time` / `collision_radius` / `mass` / `attacks_air` / `attacks_ground` /
   `target_only_buildings` / `target_only_towers` / `target_only_troops` / `projectile`
3. 群体卡（骷髅军团 / 亡灵大军 / 哥布林帮 等）：从 `cards_stats_troop.json` 的
   `summon_character` / `summon_number` / `summon_radius` / `summon_deploy_delay` 取；
   被召唤的**单个单位**在 `战斗单位_cs` 里单独占一行
4. 建筑：`cards_stats_building.json`（`hitpoints_per_level[10]` 等；`life_time` 用 `life_time` 字段）
5. 塔：`PrincessTower` / `KingTower`，`hp = hitpoints_per_level[10]` = **3584 / 6144**；
   `damage` 取 `cards_stats_projectile.json` 的 `TowerPrincessProjectile.damage_per_level[10]` /
   `KingProjectile.damage_per_level[10]` = **128 / 128**；`hit_speed_ms` = 800 / 1000；
   `range_mt` = 7500 / 7000；`load_time_ms` = 0 / 500；`radius_mt` = 1000 / 1400；
   `atk_air`/`atk_ground` = 1/1
6. **投射物**：`cards_stats_projectile.json` 的 `speed` / `radius` 填进对应单位的
   `projectile_key`（把投射物也作为 `战斗单位_cs` 的一类行，`kind` 用 `3`=投射物）
7. 取不到的字段填空 / `0`，**并在回报里逐条列出**（不许猜）

#### `法术_cs.txt` —— 10 行
列：`id` / `key` / `name_cn` / `elixir` / `rarity` / `radius_mt` / `instant_damage` / `crown_damage` /
`duration_ms` / `tick_ms` / `damage_per_tick` / `heal_per_second` / `spawn_key` / `spawn_n` /
`spawn_radius_mt` / `pushback` / `anywhere` / `on_water` / `sprite_dir`
取值：`cards_stats_spell.json`（逐级数组取索引 10）+ `cards_stats_projectile.json`；
`anywhere` / `on_water` 由 `can_deploy_on_enemy_side` / `can_place_on_water` 决定；
**对照 `原版资源/cr-sim/reference/anchors.json` 的 `Fireball` / `Rocket` / `Arrows` / `Zap` / `Lightning` / `Log` / `Poison` 逐条核对**并写进回报。

#### `tools/table.ps1`
一键闭环脚本（三件事）：① 源表 `-pack` 反出 `策划/数值文档/<表名>-pack.xlsx` 交策划用 Excel 改；
② 打表出 tsv + 强类型代码；③ 把产物拷到服务端与客户端约定位置。
**工具用法先查 `patterns/table.md` 与 `clover-tools/table`**（⛔ 不许自己编命令行参数）。
产物落点：服务端 `server/game/table/`；客户端位置在 `docs/步骤文档.md` §4 里定（客户端工程尚未建，本片只需保证
打表脚本能产出并落到服务端目录，客户端一侧留好路径参数）。

**抽取脚本**放 `<项目根>/.ai-tmp/hosts/`（⛔ 不许放 `tools/`、`策划/`、项目根）。
输出确定性：同输入两次跑出**逐字节相同**的源表（列表按 id 排序、无随机）。

## 5. 验收标准

- [ ] `cd server && go mod tidy && go build ./... && go vet ./...` 全绿
- [ ] `go run . -config configs/all` 起服，日志无 panic / 无 error；`env.exe info` 四组件就绪
- [ ] `server/game/def/` 的消息号与结构体与 `docs/步骤文档.md` §4.1 **逐条一致**（自己 diff 一遍）
- [ ] `策划/数值文档/{卡牌_cs,战斗单位_cs,法术_cs}.txt` 存在；`卡牌_cs.txt` **正好 60 行数据**
- [ ] `tools/table.ps1` 能一键跑通，产出 tsv + 强类型代码，服务端 `go build` 仍通过
- [ ] **抽查对账**：Knight / P.E.K.K.A / Rocket / Arrows 四个值与你表里的值 + `anchors.json` 三方一致（把对比贴进回报）
- [ ] 起服后用 `clover-server-tools/msg-client` 能成功登录（HTTP 换 token → `EMsg.Login`）—— 做不到就说明卡在哪
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 的原始输出（此刻可能有 FAIL，如实贴）
- [ ] ⛔ 项目里**没有** `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`
- [ ] 一次性产物**只在** `.ai-tmp/`

## 6. 约束

- 每条非预期分支都要打日志：服务端用**包级** `logger.Infof/Warnf/Errorf`（`pkg/foundation/logger`），
  ⛔ 不许 `log.Printf` / `fmt.Print`
- 改动按 **四拍**走：① 只读取证 + 改动清单 → ② 批量改（不编译）→ ③ **一次**编译 + 预演 → ④ 集中出证据
- ⛔ 不许改契约（消息号 / 协议字段名以本任务书 + `docs/步骤文档.md` §4 为准）
- ⛔ 不许再派生任何子 agent

## 7. 回报格式（做完一次性回报，中途不播报）

```
产出物：<绝对路径清单>
自检：<实际执行的命令 + 原始输出（go build / go vet / 起服日志 / 打表输出 / 三角对账表）>
未决：无 / <具体条目：哪个字段取不到、哪个数值有歧义>
```
