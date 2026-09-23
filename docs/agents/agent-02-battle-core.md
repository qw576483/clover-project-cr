# agent-02：对局内核 `server/game/core/`（纯 Go，零引擎依赖）

## 0. 技能（开工必做）

**(1) 拿到 skill。**
- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
- 全局兜底（按序命中即用）：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 有 `use_skill` 工具就先加载 `clover-engine`
- 找不到 → 回报主 agent 要路径，**不许凭记忆写代码**

**(2) 按 skill 的「混合模式找依据」查代码**（用户 > 引擎 > 联网/自创），**不许编 API**。

⛔ **红线**：只许读本项目、`clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*` **一律不许读、不许 grep、不许照抄**。

⛔ **不许改任何 skill**。发现 skill 有错 ⇒ 写进回报。

## 1. 目标

实现《皇室战争》**对局内核**：一个 20 TPS 的**服务端权威**模拟器，放在 `server/game/core/`，
**纯 Go、零 `clover-server-engine` 依赖**，可 `go test ./game/core/...` **离线秒级断言**。

这是整个项目**唯一**实现玩法规则的地方；`game/logic/` 只做取参 → 调 core → 回包。

## 2. 任务边界

**只做**：`server/game/core/` 下的 `.go` 文件（含 `*_test.go`）。

**绝不做**：
- ⛔ 不许写 / 改 `server/game/logic/`、`server/game/def/`、`server/game/datadef/`、`server/game/table/`、
  `server/main.go`、`server/configs/`（这些是 agent-01 / agent-03 的地盘；你只**只读**它们）
- ⛔ 不许碰 `client/`
- ⛔ 不许改 `tools/`、`docs/`、`策划/`、`tools/ai-skill/`
- ⛔ **`core/` 里出现任何 `import "clover-server-engine/..."` 一律算错**
- ⛔ 不许写"交接文档"；未完成项只写在**回报消息**里

## 3. 前置依赖

- ★ `<项目根>/策划/策划案/皇室战争参考规格.md` —— **唯一标准答案**：§1 单位系统、§2 竞技场几何、
  §3 对局规则、§4 塔数值、§5 战斗规则
- ★ `<项目根>/docs/步骤文档.md` §4.1（`BattleSnapshot` / `BattleStartNotify` 字段）、§4.2 / §4.3（配表列名）、§4.4（常量）
- `<项目根>/tools/ai-skill/constraints.md`（引擎与平台约束）
- 参考物实现（**只读，只当公式/规则出处，⛔ 不许抄代码**）：
  `原版资源/cr-sim/cr_sim/engine/{arena,constants,fixed,pathing,targeting,combat,movement,elixir,battle,spells,projectiles}.py`
  与 `原版资源/cr-sim/reference/anchors.json`
- **可见性**：agent-01 正在并行产出 `server/game/def/` 与配表。你**不要等它** ——
  为避免耦合，`core/` **自己定义** `UnitDef` / `SpellDef` / `CardDef` / `CardTable` 接口（见 §4.1），
  agent-03 负责把打表产物适配成 `CardTable` 传进来。**你只用接口，不 import 任何生成代码。**

## 4. 产出物（绝对路径，全部在 `c:\Work\Server\f-v2\clover-project-cr\server\game\core\`）

| 文件 | 职责 |
|---|---|
| `units.go` | 单位系统常量 + 定点换算 + `Team` + 距离/几何工具 |
| `arena.go` | 竞技场几何 + `CanDeploy` |
| `card.go` | `UnitDef` / `SpellDef` / `CardDef` / `CardTable` / `CardKind` 类型定义 |
| `entity.go` | 实体（部队 / 建筑 / 塔 / 投射物）+ 状态机 |
| `hand.go` | 8 卡组洗牌 + 4 手牌轮转 + 下一张 |
| `elixir.go` | 圣水三段回复 |
| `pathing.go` | 车道 → 桥 → 目标寻路 |
| `targeting.go` | 索敌 / 重锁定 |
| `combat.go` | 攻击 / 投射物 / 法术 / 亡语 / 击退 / 推挤分离 |
| `tower.go` | 塔（含国王塔惰性激活 3300 ms） |
| `rules.go` | 胜负判定 |
| `battle.go` | 对局主体 + `Step()` + `PlayCard()` |
| `snapshot.go` | 快照（`Snapshot()`）与事件（`DrainEvents()`） |
| `ai.go` | ⛔ **不归你**（agent-03 负责），但你要在 `battle.go` 里留出 `PlayCard` 这一唯一入口 |
| `*_test.go` | ★ 离线断言（见 §6） |

## 4.1 公开接口（★ 冻结，agent-03 依赖它）

```go
package core

// ---------- units.go ----------
type Team int32
const (
    TeamBlue Team = 0
    TeamRed  Team = 1
)

const (
    MilliTilePerTile = 1000   // 定点单位：1/1000 格；与官方数据同尺度
    TicksPerSecond   = 20
    MSecPerTick      = 50

    ArenaWMilli      = 18000
    ArenaHMilli      = 32000
    RiverTopMilli    = 15000  // 河：y ∈ [15000, 17000)
    RiverBottomMilli = 17000
    BridgeAxMilli    = 3500   // 桥中心 x = 3.5 格
    BridgeBxMilli    = 14500  // 桥中心 x = 14.5 格
    BridgeHalfMilli  = 1000   // 桥半宽 1 格

    MaxElixirMilli      = 10000
    StartingElixirMilli = 6000
    RegulationMs        = 180000
    OvertimeMs          = 120000
    KingActivationMs    = 3300
)

// 圣水三段（毫秒/整点），与 battle_timelines.json 的 Default 一致
var ElixirMsPerUnit = [3]int32{2800, 1400, 930}
var ElixirPhaseMs   = [3]int32{120000, 120000, 60000}

// ---------- card.go ----------
type CardKind int32
const (
    KindTroop      CardKind = 0
    KindBuilding   CardKind = 1
    KindTower      CardKind = 2
    KindProjectile CardKind = 3
)

type UnitDef struct {
    ID, Key, NameCN            string  // ID 用 int32，见下
    // …（字段见下表）
}
type CardDef struct {
    ID       int32
    Key      string
    NameCN   string
    Kind     int32 // 卡类型：0=部队 1=法术 2=建筑
    Rarity   int32 // 0=普通 1=稀有 2=史诗 3=传说
    Elixir   int32
    UnitKey  string // 部队/建筑：召唤的单位 key
    UnitN    int32  // 召唤数量（1 = 单卡）
    UnitRadiusMilli int32 // 环形铺开半径
    DeployDelayMs   int32
    Spell    *SpellDef // 仅法术卡
}
type UnitDef struct {
    ID   int32
    Key  string
    NameCN string
    Kind CardKind
    HP, Damage, HitSpeedMs, LoadTimeMs int32
    SpeedMilliPerSec int32 // 格/分钟 → 换算见 §5
    RangeMilli, SightMilli int32
    DeployMs int32
    RadiusMilli, Mass int32
    AtkAir, AtkGround bool
    OnlyBuildings, OnlyTowers, OnlyTroops bool
    ProjectileKey string
    DeathSpawnKey string
    DeathSpawnN int32
    DeathDamage int32
    DeathAoeRadiusMilli int32
    LifeMs int32
    SpawnKey string
    SpawnN int32
    SpawnRadiusMilli int32
    SpawnIntervalMs int32
    SpawnLimit int32
    SpriteDir string
}
type SpellDef struct {
    ID int32
    Key, NameCN string
    Elixir int32
    RadiusMilli int32
    InstantDamage int32
    DurationMs int32
    TickMs int32
    DamagePerTick int32
    HealPerSecond int32
    SpawnKey string
    SpawnN int32
    SpawnRadiusMilli int32
    Pushback int32
    Anywhere, OnWater bool
}
type CardTable interface {
    Card(id int32) (*CardDef, bool)
    Unit(key string) (*UnitDef, bool)
}

// ---------- arena.go ----------
type Arena struct{ /* 只读，构造一次全局复用 */ }
func NewArena() *Arena
func (a *Arena) InBounds(xMilli, yMilli int32) bool
func (a *Arena) IsWater(xMilli, yMilli int32) bool
func (a *Arena) IsBlocked(xMilli, yMilli int32) bool      // 国王塔 3x3 占地
func (a *Arena) IsWalkable(xMilli, yMilli int32, flying bool) bool
func (a *Arena) NearestBridgeX(xMilli int32) int32
// canDeploy: 摧毁的敌方公主塔会扩展该车道的部署区（见参考规格 §2.1）
func (a *Arena) CanDeploy(team Team, xMilli, yMilli int32, anywhere, onWater bool, fallenEnemyPrincess []TowerRef) bool

// ---------- battle.go ----------
type Config struct {
    Seed  int64
    Table CardTable
    DeckA []int32 // 8 个卡 id
    DeckB []int32
}
type Battle struct{ /* … */ }
func NewBattle(cfg Config) (*Battle, error)
func (b *Battle) Step()                                    // 推进 1 tick（50 ms）
func (b *Battle) PlayCard(team Team, cardID int32, xMilli, yMilli int32) error
func (b *Battle) Surrender(team Team)
func (b *Battle) Ended() bool
func (b *Battle) Result() Result
func (b *Battle) Snapshot() Snapshot                       // 见 §4.2
func (b *Battle) DrainEvents() []Event
func (b *Battle) Elixir(team Team) int32                   // 1/1000 圣水
func (b *Battle) Hand(team Team) []int32
func (b *Battle) Next(team Team) int32
func (b *Battle) Crowns(team Team) int32

type Result struct {
    Ended  bool
    Winner Team   // 仅 Ended && !Draw 时有效
    Draw   bool
    CrownsA, CrownsB int32
    Reason string  // king_destroyed | time_up_crowns | time_up_hp | surrender | draw
    HpRateA, HpRateB int32 // 剩余塔血占总上限的万分比
}
```

## 4.2 `Snapshot` / `Event`（★ 必须与 agent-01 在 `def` 里定义的结构体**字段逐字一致**）

```go
type TowerSnap struct {
    ID    int32
    Kind  int32 // 0=公主塔 1=国王塔
    HP    int32
    MaxHP int32
    Alive bool
}
type EntitySnap struct {
    ID       int32
    Kind     int32 // 0=部队 1=建筑 2=塔
    CardID   int32
    Team     int32
    XMilli   int32
    YMilli   int32
    HP       int32
    MaxHP    int32
    Anim     int32 // 0=idle 1=walk 2=attack 3=die
    Facing   int32 // -1 / 1
    DeployMs int32 // 剩余部署时间
}
type Snapshot struct {
    Seq      int32
    ServerMs int32
    Phase    int32   // 0=normal 1=overtime 2=ended
    ElixirA, ElixirB int32
    CrownsA, CrownsB int32
    TowersA, TowersB []TowerSnap
    Entities []EntitySnap
    HandA, HandB []int32
    NextA, NextB int32
}
type Event struct {
    Kind     int32 // 0=出牌 1=生成 2=死亡 3=塔毁 4=圣水满 5=塔激活
    CardID   int32
    XMilli   int32
    YMilli   int32
    EntityID int32
    Team     int32
    Text     string
}
```

## 5. 规则要点（★ 全部有出处，见 `策划/策划案/皇室战争参考规格.md`）

| 项 | 规则 |
|---|---|
| 定点 | 位置 = **1/1000 格**（`MilliTilePerTile = 1000`）。**⛔ 不许用 float 存位置/血量**（血量/伤害用 int32；只有三角函数类的铺开坐标可临时用 float 再取整） |
| 速度换算 | 官方 `speed` = 格/分钟。每 tick 位移 = `speed * 1000 * (1000/TicksPerSecond) / 60000` milli —— **先乘后除，且把余数累计到「已行进距离」里**（不要每 tick 截断后累加位置，否则会漂移；见 `cr-sim/cr_sim/engine/fixed.py:108` 的 `point_along`） |
| 竞技场 | 18×32 格；河 y∈[15,17)；桥心 x=3.5 / 14.5，各宽 2 格；国王塔 (9,3)/(9,29) 占 3×3 格；公主塔 (3.5,6.5)/(14.5,6.5)/(3.5,25.5)/(14.5,25.5) 半径 1 格 |
| 部署 | BLUE y∈[0,15)；RED y∈[17,32)；河面仅法术可落；国王塔 3×3 与 BLUE 格不可落；摧毁敌方公主塔 ⇒ **该车道**部署区扩展到该塔行（不含该行） |
| 圣水 | 起始 6；上限 10；回复率按 `ElixirPhaseMs` 分三段切换 `ElixirMsPerUnit`（2800/1400/930 毫秒每整点） |
| 手牌 | 8 张卡组洗牌 → 手牌 4 + 下一张 1；出牌后该卡进"已出队列"，从队列取下一张补位（经典轮转：出了的卡排在队尾） |
| 部署延迟 | 落点成功后 `DeployMs`（官方普遍 1000 ms）内不可行动、不可被选中 |
| 索敌 | 在 `SightMilli` 内取**距离最近**的合法目标；`OnlyBuildings`/`OnlyTowers`/`OnlyTroops` 与 `AtkAir`/`AtkGround` 过滤；**锁定后保持**，目标死亡/越界才重锁定 |
| 攻击 | 目标进入 `RangeMilli` ⇒ 先等 `LoadTimeMs`（首次前摇）⇒ 之后每 `HitSpeedMs` 造成一次 `Damage` |
| 投射物 | `ProjectileKey` 非空 ⇒ 生成投射物实体，速度取投射物的 `SpeedMilliPerSec`，命中结算（简化：追踪至目标点，不做弹道抛物线） |
| 飞行 | `AtkAir` 相关的飞行单位**无视地形与水**（直接过河）；`IsWalkable(flying=true)` 恒真（除了出界） |
| 移动路径 | 地面单位：起点 → **最近桥中心**（若需过河）→ 目标点；同侧则直线。⛔ 不做全局 A*（原版是车道寻路） |
| 推挤 | 每 tick 做一次圆-圆分离（`RadiusMilli`），质量大的推得少（`Mass`） |
| 击退 | 沿 origin→point 方向外推 `Pushback` |
| 亡语 | 死亡时按 `DeathSpawnKey/DeathSpawnN` 生成单位、按 `DeathDamage/DeathAoeRadiusMilli` 造成范围伤害 |
| 建筑 | 有 `LifeMs` 的建筑到时间自然消失；`SpawnKey/SpawnN/SpawnIntervalMs/SpawnLimit` 周期性产兵 |
| 塔 | 公主塔始终激活；**国王塔惰性**：己方任一公主塔被摧毁 **或** 自身被攻击 ⇒ 记起点火，**3300 ms 后**开始攻击。射程/攻速/伤害见参考规格 §4.1 |
| 胜负 | ① 国王塔被毁 ⇒ 立即结束；② `ServerMs` 到常规 180 s：冠数不同 ⇒ 判定；冠数相同 ⇒ **进入加时（`Phase=1`，圣水按同一时间轴继续，`ElixirPhaseMs` 累积到 240 s 起进入 3 倍段）**；③ 到 300 s ⇒ 比冠数；④ 冠数相同比**剩余塔血占总上限的万分比**；⑤ 仍相同 = 平局 |
| 冠数 | 摧毁一座公主塔 = 1 冠；摧毁国王塔 = 直接 3 冠结算 |
| 投降 | `Surrender(team)` ⇒ 立即结束，对方胜，`Reason="surrender"` |

## 6. 验收标准（★ `go test` 是本体，必须离线秒级跑完）

- [ ] `cd server && go build ./game/core/... && go vet ./game/core/...` 全绿
- [ ] `go test ./game/core/... -count=1` **全绿**，且**不依赖任何服务**（不起服、不连 redis/mysql）
- [ ] **必须有的断言（每条对应一个规则，命名即判据）**：
  - `TestArenaGeometry`：`RiverTop/Bottom`、两桥中心、四塔坐标、国王塔 3×3 blocked 区
  - `TestCanDeployBase`：BLUE 在 (9,5) 合法 / 在 (9,20) 非法；河面非法；国王塔格非法
  - `TestCanDeployPocketAfterPrincessFalls`：摧毁 BLUE 视角的敌方**左**公主塔 ⇒ 左车道 y 上限扩展到该塔行（不含），**右车道不受影响**
  - `TestElixirThreePhases`：从 0 起，模拟 300 s，断言在 120 s 前 2800 ms/点、120–240 s 为 1400、240 s 后为 930（用整数断言，给出实际值）
  - `TestHandCycle`：8 张卡组，连续出牌断言手牌循环与 `Next` 正确（4 手牌 + 1 下一张，出牌后补位）
  - `TestPlayCardRejectedWhenElixirShort`
  - `TestPlayCardRejectedOutsideDeployZone`
  - `TestUnitWalksToBridgeThenAcross`：一个地面单位从本侧后场出发，断言它经过桥中心 x（±1 格）后才越过 y=17
  - `TestFlyingUnitIgnoresRiver`：飞行单位直线过河
  - `TestMeleeKillsInExpectedHits`：**Knight(L11, HP 1766 / dmg 202) 打 TowerPrincess 类目标** 或两个已核验单位互殴，断言出手次数（把算法与实际值贴进回报）
  - `TestTowerShellsNearestTarget`：塔只打最近目标、按 `HitSpeedMs` 出手
  - `TestKingTowerInertUntilProvoked`：国王塔在未点火时不出手；公主塔被毁后 **恰好 3300 ms** 开始出手
  - `TestVictoryByKingDestroyed`：国王塔血 0 ⇒ `Ended()` 且 `Winner` 正确、`Reason="king_destroyed"`
  - `TestVictoryByCrownsAt180s`：180 s 时冠数不同 ⇒ 立即结束（**不进加时**）
  - `TestOvertimeThenHpRate`：180 s 平冠 ⇒ 进加时；300 s 仍平冠 ⇒ 比血量万分比，数值断言
  - `TestDeterminism`：同一 `Seed` + 同一操作序列跑两遍 ⇒ `Snapshot()` 逐字段一致
- [ ] **性能**：一局 300 s × 20 TPS = 6000 tick，场上 60 个单位时，`go test -run TestPerfFullMatch` 单局 < 2 s（贴实测）
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出（如实贴，可能有 FAIL）
- [ ] ⛔ `core/` 里 **0 个** `clover-server-engine` import（自己 grep 验证并把结果贴出来）
- [ ] ⛔ 项目里**没有** `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；一次性产物**只在** `.ai-tmp/`

## 7. 约束

- 改动按**四拍**走：① 只读取证 + 改动清单 → ② 批量改（不编译） → ③ **一次**编译 + 预演 → ④ 集中出证据
- 每条非预期分支都要打日志 —— 但 `core/` 是纯逻辑层，**用返回 error / 事件表达**，不要引 logger；
  需要留痕的诊断点用 `Event{Kind:..., Text:...}` 输出
- ⛔ 不许改契约（§4.1 的签名与 §4.2 的字段名）；发现问题先回报
- ⛔ 不许再派生任何子 agent
- **数值⛔不许硬编码**：所有 HP/伤害/速度/射程/时长**只能**来自 `CardTable`（配表）；
  只有 §4.1 `units.go` 里那批**全局常量**（场地尺寸/河桥/圣水时间线/国王塔激活延迟）可以直接写在代码里

## 8. 回报格式

```
产出物：<绝对路径清单>
自检：<命令 + 原始输出：go build / go vet / go test -v 的关键行 / perf 实测 / grep 引擎依赖结果>
未决：无 / <具体条目：哪条规则拿不准、哪个数值缺出处、哪个断言没过>
```
