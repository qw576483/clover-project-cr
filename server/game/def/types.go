package def

// 本文件放**跨请求 / 推送共享**的协议类型（快照、时间线、房间信息等）。
// 字段名与 docs/步骤文档.md §4.1 逐字一致；`BattleSnapshot` 与 `BattleStartNotify`
// 的字段与 `core.Snapshot` 对齐。

// TowerState 一座塔的状态。
type TowerState struct {
	ID    int32 `json:"id"`
	Kind  int32 `json:"kind"` // 0=公主塔 1=国王塔
	HP    int32 `json:"hp"`
	MaxHP int32 `json:"max_hp"`
	Alive bool  `json:"alive"`
}

// EntitySnapshot 一个战场实体（部队 / 建筑 / 塔）的快照。
type EntitySnapshot struct {
	ID       int32 `json:"id"`
	Kind     int32 `json:"kind"` // 0=部队 1=建筑 2=塔
	CardID   int32 `json:"card_id"`
	Team     int32 `json:"team"`    // 0=BLUE 1=RED
	XMilli   int32 `json:"x_milli"` // 1/1000 格
	YMilli   int32 `json:"y_milli"` // 1/1000 格
	HP       int32 `json:"hp"`
	MaxHP    int32 `json:"max_hp"`
	Anim     int32 `json:"anim"`      // 0=idle 1=walk 2=attack 3=die
	Facing   int32 `json:"facing"`    // -1 / 1
	DeployMs int32 `json:"deploy_ms"` // 剩余部署时间
}

// BattleSnapshot 周期全量快照（10 Hz，BestEffort）。
//
// RoomID 与客户端 `ProtoDef.BattleSnapshot.room_id` 同名同值：
// 客户端**必须**按它丢弃"不是本局房间"的帧 —— 否则旧房间的 tick 若还在推快照，
// 它更大的 seq 会让客户端把新房的帧全判"倒退"丢掉（跨房间快照污染）。
// 服务端先发字段即可：`JsonUtility` 忽略未知字段，旧客户端不会因此报错。
type BattleSnapshot struct {
	RoomID   string           `json:"room_id"`
	Seq      int32            `json:"seq"`
	ServerMs int32            `json:"server_ms"` // 对局已进行毫秒
	Phase    int32            `json:"phase"`     // 0=normal 1=overtime 2=ended
	ElixirA  int32            `json:"elixir_a"`  // 1/1000，0..10000
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

// BattleTimeline 对局时间轴常量（出处：策划/策划案/皇室战争参考规格.md §3）。
type BattleTimeline struct {
	RegulationMs    int32   `json:"regulation_ms"`      // 180000
	OvertimeMs      int32   `json:"overtime_ms"`        // 120000
	StartingElixir  int32   `json:"starting_elixir"`    // 6
	MaxElixir       int32   `json:"max_elixir"`         // 10
	ElixirMsPerUnit []int32 `json:"elixir_ms_per_unit"` // [2800,1400,930]
	ElixirPhaseMs   []int32 `json:"elixir_phase_ms"`    // [120000,120000,60000]
}

// BattleEvent 对局中的一个离散事件。
type BattleEvent struct {
	Kind     int32  `json:"kind"` // 0=出牌 1=生成 2=死亡 3=塔毁 4=圣水满 5=塔激活 6=塔开火
	CardID   int32  `json:"card_id"`
	XMilli   int32  `json:"x_milli"`
	YMilli   int32  `json:"y_milli"`
	EntityID int32  `json:"entity_id"`
	Team     int32  `json:"team"`
	Text     string `json:"text"`

	// ProjSpeed 只在 kind==6（EvTowerShoot）上有值：该塔投射物的速度，单位 = 格/分钟
	// （与 CardInfo.proj_speed 同口径）。塔不是卡 ⇒ 客户端查不到，必须由服务端下发；
	// 0 = 该塔无投射物（近战塔），客户端只播枪口闪光。
	// ⛔ 加在**末尾**且 omitempty：老客户端不认识该字段，反序列化时忽略即可（JSON 前向兼容）。
	ProjSpeed int32 `json:"proj_speed,omitempty"`
}

// RoomInfo 房间列表里的一行。
type RoomInfo struct {
	RoomID  string `json:"room_id"`
	Name    string `json:"name"`
	Host    string `json:"host"`
	Cur     int32  `json:"cur"`
	Max     int32  `json:"max"` // 固定 2
	AiFill  bool   `json:"ai_fill"`
	Started bool   `json:"started"`
}

// RoomMember 房间内的一个成员（含 AI 顶位）。
type RoomMember struct {
	PlayerID string `json:"player_id"`
	Nickname string `json:"nickname"`
	Ready    bool   `json:"ready"`
	IsHost   bool   `json:"is_host"`
	IsAI     bool   `json:"is_ai"`
}

// CardInfo 卡池里的一张卡（GetCardPoolReply.cards 的元素）。
//
// 字段取自配表 `卡牌_cs`（server/game/table），服务端下发后客户端可直接渲染卡池。
// 出处：docs/步骤文档.md §4.1 的 `GetCardPoolReply{cards[]}`（元素结构草案未在该表给出，
// 本文件按下发给客户端所需的最小集确定）。
type CardInfo struct {
	ID     int32  `json:"id"`
	Key    string `json:"key"`
	NameCn string `json:"name_cn"`
	NameEn string `json:"name_en"`
	Type   int32  `json:"type"`   // 0=部队 1=法术 2=建筑
	Rarity int32  `json:"rarity"` // 0=普通 1=稀有 2=史诗 3=传说
	Elixir int32  `json:"elixir"`
	Arena  int32  `json:"arena"`
	Icon   string `json:"icon"`

	// ProjectileKey 是这张卡战斗本体的**攻击投射物实体 key**（战斗单位_cs 的
	// `projectile_key` 列；出处 = 官方 cards_stats_*.json 的 `projectile` 字段）。
	// 空字符串 = 非远程（近战部队 / 法术）。客户端据此判定"这张卡是不是远程"。
	//
	// 战斗本体：群体卡（弓箭手 / 亡灵…）取 `summon_key` 指向的本体行，与
	// logic.cardDefOf 的 CardDef.UnitKey 同口径；投射物行本身不参与。
	ProjectileKey string `json:"projectile_key"`

	// ProjSpeed 是弹道速度，单位 = **格/分钟**，与官方
	// `cards_stats_projectile.json` 的 `speed` 同口径
	// （出处：策划/策划案/皇室战争参考规格.md §4「投射物：`projectile` 非空 ⇒ 远程，
	//   弹道速度 = `projectile.speed`」）。
	// 取值来源 = 战斗单位_cs 里该投射物行（kind=3）的 `speed` 列（表内列注释
	// 「投射物行为弹道速度」），⛔ 不在 Go 里硬编码。ProjectileKey 为空时为 0。
	ProjSpeed int32 `json:"proj_speed"`

	// AoeRadiusMilli 是**法术卡**的作用半径，单位 = milli-tile（1 格 = 1000）。
	// 非法术卡恒为 0。
	//
	// 为什么要下发它：客户端拖出法术时要在落点画一个**半径圈**，圈多大必须等于真实
	// 作用范围（`spell.tsv` 的 `radius_mt` 列，10 张法术各不相同：万箭齐发 1400 …
	// 毒药/雷电 3500）。旧实现是客户端写死 `PlacementRadiusTilesSpell = 3.0f`
	// 一刀切 ⇒ 万箭齐发的圈画大了 2 倍多、雷电/毒药又画小了，玩家照着圈放会打空。
	// 数值权威在服务端配表 ⇒ 由服务端算好下发，⛔ 客户端不许自造半径。
	AoeRadiusMilli int32 `json:"aoe_radius_milli,omitempty"`
}
