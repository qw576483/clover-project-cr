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
//
// 除下面标了「恒 0」的三项外，全部字段都来自**真实发生过的事件**（一局结算时累加并落库），
// 没有一项是"看起来合理"的常量。三标「恒 0」的是本工程没有对应机制的系统
// （奖杯/段位、部落捐赠、锦标赛/挑战卡牌奖励）—— 原版新号这几项同样是 0。
type GetProfileReply struct {
	Nickname string  `json:"nickname"`
	Wins     int32   `json:"wins"`
	Losses   int32   `json:"losses"`
	Deck     []int32 `json:"deck"`

	// Matches 参赛场次：打过的总局数（含平局）。
	Matches int32 `json:"matches"`
	// ThreeCrownWins 三冠胜场：以 3 王冠取胜的局数。
	ThreeCrownWins int32 `json:"three_crown_wins"`
	// CardsFound 已收集卡牌 = 卡牌表里可用的卡数（本工程卡池即全集，没有"未解锁"概念）。
	CardsFound int32 `json:"cards_found"`
	// FavouriteCard 常用卡牌 = 出牌次数最多的那张卡的 id；0 = 一张都没出过。
	// FavouriteCardName = 它的中文名（服务端从卡牌表取，客户端直接显示）。
	FavouriteCard     int32  `json:"favourite_card"`
	FavouriteCardName string `json:"favourite_card_name"`

	// HighestTrophies 最高奖杯：本工程无奖杯 / 段位机制 ⇒ 恒 0。
	HighestTrophies int32 `json:"highest_trophies"`
	// CardsDonated 累计捐赠：本工程无部落 / 捐赠系统 ⇒ 恒 0。
	CardsDonated int32 `json:"cards_donated"`
	// CardsWon 赢得卡牌：本工程无锦标赛 / 挑战 / 卡牌奖励系统 ⇒ 恒 0。
	CardsWon int32 `json:"cards_won"`
}

// ------------------------------------------------------------ 卡池与卡组

// GetCardPoolReq 拉卡池请求。
type GetCardPoolReq struct{}

// GetCardPoolReply 卡池回包（60 张）。
type GetCardPoolReply struct {
	Cards []CardInfo `json:"cards"`
}

// GetDeckReq 拉卡组请求。
//
// Slot = 槽位下标 0..4（界面上的 1..5 号卡组）；**-1 = 当前使用的那一个**（`PlayerData.ActiveSlot`）。
// 缺字段（旧客户端）= 0 = 1 号；越界由服务端兜到当前槽并留痕。
type GetDeckReq struct {
	Slot int `json:"slot"`
}

// GetDeckReply 卡组回包。
//
// CardIDs = `Slot` 那一套卡组的 8 个卡 id；Slot = 实际取到的槽位下标（-1 请求时回真实槽位）；
// ActiveSlot = 当前使用的槽位下标。客户端据此决定打开卡组页停在哪个号上。
type GetDeckReply struct {
	CardIDs    []int32 `json:"card_ids"`
	Slot       int     `json:"slot"`
	ActiveSlot int     `json:"active_slot"`
}

// SaveDeckReq 保存卡组请求（8 张）。
//
// Slot = 槽位下标 0..4；缺字段（旧客户端）= 0 = 1 号。保存成功即把该槽设为当前槽
// （对局 / 房间座位缓存用的就是当前槽那一套）。
type SaveDeckReq struct {
	CardIDs []int32 `json:"card_ids"`
	Slot    int     `json:"slot"`
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
