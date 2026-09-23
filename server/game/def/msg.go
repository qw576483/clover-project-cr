// Package def 是本项目**服务端唯一的消息号与协议定义处**（无业务逻辑、无引擎依赖）。
//
// 契约出处：docs/步骤文档.md §4.1（★ 唯一依据，不许私自改）。
// 客户端镜像：client/Assets/Scripts/Def/MsgDef.cs + ProtoDef.cs（两端同名同值、同一批改动）。
//
// 铁律：
//   - 业务消息号必须 >= 10001（引擎占 [1,10000]，启动期统一校验，误用 panic）；
//   - **回包不占消息号**：引擎按 requestID 配对，回包帧 msgID 恒为 0，
//     所以这里**只定义回包结构体**，绝不给 Reply 定义常量；
//   - JSON tag 一律 snake_case，字段名与 docs/步骤文档.md §4.1 逐字一致。
package def

// C2S（客户端 → 服务端）消息号。
//
// 分段（见 tools/ai-skill/conventions.md）：玩家 1000101+ / 卡池卡组 1000201+ /
// 房间 1000301+ / 对局 1000401+ / 人机 1000501+。
const (
	// ---- 玩家 1000101+ ----
	MsgSetNickname = 1000101 // C2S 设置昵称
	MsgGetProfile  = 1000102 // C2S 拉个人档案

	// ---- 卡池与卡组 1000201+ ----
	MsgGetCardPool = 1000201 // C2S 拉 60 张卡池
	MsgGetDeck     = 1000202 // C2S 拉我的卡组
	MsgSaveDeck    = 1000203 // C2S 保存卡组（8 张）

	// ---- 房间 1000301+ ----
	MsgRoomCreate = 1000301 // C2S 创建房间
	MsgRoomList   = 1000302 // C2S 拉房间列表
	MsgRoomJoin   = 1000303 // C2S 加入房间
	MsgRoomLeave  = 1000304 // C2S 离开房间
	MsgRoomReady  = 1000305 // C2S 准备 / 取消准备
	MsgRoomStart  = 1000306 // C2S 房主开打
	MsgRoomSetAi  = 1000307 // C2S 房主设 AI 补位

	// ---- 对局 1000401+ ----
	MsgBattlePlayCard = 1000401 // C2S 出牌
	MsgBattleSurrender = 1000402 // C2S 投降
	MsgBattleSync      = 1000403 // C2S 主动拉一次全量状态（进场 / 重连）

	// ---- 人机 1000501+ ----
	MsgAiBattleStart = 1000501 // C2S 主菜单「人机对战」直接开一局
)

// ---------------------------------------------------------------- 玩家

// SetNicknameReq 设置昵称请求。
type SetNicknameReq struct {
	Nickname string `json:"nickname"`
}

// SetNicknameReply 设置昵称回包（回包不占消息号）。
type SetNicknameReply struct {
	OK       bool   `json:"ok"`
	Nickname string `json:"nickname"`
	Err      string `json:"err,omitempty"`
}

// GetProfileReq 拉个人档案请求。
type GetProfileReq struct{}

// GetProfileReply 个人档案回包。
type GetProfileReply struct {
	Nickname string  `json:"nickname"`
	Wins     int32   `json:"wins"`
	Losses   int32   `json:"losses"`
	Deck     []int32 `json:"deck"`
}

// ------------------------------------------------------------ 卡池与卡组

// GetCardPoolReq 拉卡池请求。
type GetCardPoolReq struct{}

// GetCardPoolReply 卡池回包（60 张）。
type GetCardPoolReply struct {
	Cards []CardInfo `json:"cards"`
}

// GetDeckReq 拉我的卡组请求。
type GetDeckReq struct{}

// GetDeckReply 我的卡组回包（8 个卡 id）。
type GetDeckReply struct {
	CardIDs []int32 `json:"card_ids"`
}

// SaveDeckReq 保存卡组请求（8 张）。
type SaveDeckReq struct {
	CardIDs []int32 `json:"card_ids"`
}

// SaveDeckReply 保存卡组回包。
type SaveDeckReply struct {
	OK  bool   `json:"ok"`
	Err string `json:"err,omitempty"`
}

// ---------------------------------------------------------------- 房间

// RoomCreateReq 创建房间请求。
type RoomCreateReq struct {
	Name string `json:"name"`
}

// RoomCreateReply 创建房间回包。
type RoomCreateReply struct {
	OK     bool   `json:"ok"`
	RoomID string `json:"room_id"`
	Err    string `json:"err,omitempty"`
}

// RoomListReq 拉房间列表请求。
type RoomListReq struct{}

// RoomListReply 房间列表回包。
type RoomListReply struct {
	Rooms []RoomInfo `json:"rooms"`
}

// RoomJoinReq 加入房间请求。
type RoomJoinReq struct {
	RoomID string `json:"room_id"`
}

// RoomJoinReply 加入房间回包。
type RoomJoinReply struct {
	OK     bool   `json:"ok"`
	RoomID string `json:"room_id"`
	Err    string `json:"err,omitempty"`
	AiFill bool   `json:"ai_fill"`
}

// RoomLeaveReq 离开房间请求。
type RoomLeaveReq struct {
	RoomID string `json:"room_id"`
}

// RoomLeaveReply 离开房间回包。
type RoomLeaveReply struct {
	OK bool `json:"ok"`
}

// RoomReadyReq 准备 / 取消准备请求。
type RoomReadyReq struct {
	RoomID string `json:"room_id"`
	Ready  bool   `json:"ready"`
}

// RoomReadyReply 准备 / 取消准备回包。
type RoomReadyReply struct {
	OK bool `json:"ok"`
}

// RoomStartReq 房主开打请求。
type RoomStartReq struct {
	RoomID string `json:"room_id"`
}

// RoomStartReply 房主开打回包。
type RoomStartReply struct {
	OK  bool   `json:"ok"`
	Err string `json:"err,omitempty"`
}

// RoomSetAiReq 房主设 AI 补位请求。
type RoomSetAiReq struct {
	RoomID string `json:"room_id"`
	AiFill bool   `json:"ai_fill"`
}

// RoomSetAiReply 房主设 AI 补位回包。
type RoomSetAiReply struct {
	OK bool `json:"ok"`
}

// ---------------------------------------------------------------- 对局

// BattlePlayCardReq 出牌请求（落点单位为 milli-tile = 1/1000 格）。
type BattlePlayCardReq struct {
	RoomID string `json:"room_id"`
	CardID int32  `json:"card_id"`
	XMilli int32  `json:"x_milli"`
	YMilli int32  `json:"y_milli"`
}

// BattlePlayCardReply 出牌回包。
type BattlePlayCardReply struct {
	OK  bool   `json:"ok"`
	Err string `json:"err,omitempty"`
}

// BattleSurrenderReq 投降请求。
type BattleSurrenderReq struct {
	RoomID string `json:"room_id"`
}

// BattleSurrenderReply 投降回包。
type BattleSurrenderReply struct {
	OK bool `json:"ok"`
}

// BattleSyncReq 主动拉一次全量状态请求（进场 / 重连）。
type BattleSyncReq struct {
	RoomID string `json:"room_id"`
}

// BattleSyncReply 全量状态回包。
type BattleSyncReply struct {
	Snapshot BattleSnapshot `json:"snapshot"`
}

// ---------------------------------------------------------------- 人机

// AiBattleStartReq 主菜单「人机对战」开一局请求（带自己的卡组）。
type AiBattleStartReq struct {
	Deck []int32 `json:"deck"`
}

// AiBattleStartReply 人机对战开打回包。
type AiBattleStartReply struct {
	OK     bool   `json:"ok"`
	RoomID string `json:"room_id"`
	Err    string `json:"err,omitempty"`
}
