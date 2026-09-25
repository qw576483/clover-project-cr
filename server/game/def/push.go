package def

// 推送消息号 + 推送体。契约出处：docs/步骤文档.md §4.1。
//
// 段位：3002001 起（房间列表 / 房间状态 / 开打 / 快照 / 事件 / 结算）。
// **推送消息号必须定义常量**（与回包不同：回包不占消息号）。
const (
	PushRoomList       = 3002001 // 推送 Reliable   大厅房间列表变化
	PushRoomState      = 3002002 // 推送 Reliable   房间成员 / 准备 / AI 状态
	PushBattleStart    = 3002003 // 推送 Reliable   开打：对局配置
	PushBattleSnapshot = 3002004 // 推送 BestEffort（UDP） 周期快照（每 100ms = 10 Hz）
	PushBattleEvent    = 3002005 // 推送 Reliable   对局离散事件
	PushBattleEnd      = 3002006 // 推送 Reliable   结算
)

// RoomListNotify 大厅房间列表变化。
type RoomListNotify struct {
	Rooms []RoomInfo `json:"rooms"`
}

// RoomStateNotify 房间内成员 / 准备 / AI 状态。
//
// SelfPlayerID 是**逐接收者**的：同一份房间态推给不同玩家时它的值不同，所以
// roomState.board() 不填它，由 roomRegistry 的推送循环按目标填入（见 room.go 的 pushRoomState）。
// 存在的理由：协议原先没有任何"我自己是谁"的字段，客户端只能按 playerIDPrefix 反推自己，
// 那是**服务端实现细节的副本**（前缀一改客户端就认不出自己，表现为"房主按钮永远是灰的"）。
type RoomStateNotify struct {
	RoomID       string       `json:"room_id"`
	Name         string       `json:"name"`
	Host         string       `json:"host"`
	AiFill       bool         `json:"ai_fill"`
	Started      bool         `json:"started"`
	Members      []RoomMember `json:"members"`
	SelfPlayerID string       `json:"self_player_id"`
}

// BattleStartNotify 开打：对局配置。
//
// 与 docs/步骤文档.md §4.1 的载荷一并对齐：该表列了 `server_ms`，
// 故本结构体保留之（docs/步骤文档.md §4.1 给出的字段全部在内，未删未改名）。
type BattleStartNotify struct {
	RoomID   string         `json:"room_id"`
	Seed     int64          `json:"seed"`
	ServerMs int32          `json:"server_ms"`
	MyTeam   int32          `json:"my_team"`
	Timeline BattleTimeline `json:"timeline"`
	DeckA    []int32        `json:"deck_a"`
	DeckB    []int32        `json:"deck_b"`
	HandA    []int32        `json:"hand_a"`
	NextA    int32          `json:"next_a"`
	HandB    []int32        `json:"hand_b"`
	NextB    int32          `json:"next_b"`
}

// BattleEventNotify 对局离散事件批次。
type BattleEventNotify struct {
	Events []BattleEvent `json:"events"`
}

// BattleEndNotify 结算。
//
// Reason 取值：king_destroyed | time_up_crowns | time_up_hp | surrender | draw。
type BattleEndNotify struct {
	Win     bool   `json:"win"`
	Draw    bool   `json:"draw"`
	CrownsA int32  `json:"crowns_a"`
	CrownsB int32  `json:"crowns_b"`
	Reason  string `json:"reason"`
	HpRateA int32  `json:"hp_rate_a"` // 剩余塔血占总上限的万分比
	HpRateB int32  `json:"hp_rate_b"`
}
