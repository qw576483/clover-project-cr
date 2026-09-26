// Package datadef 是服务端的数据 schema 定义处（玩家档案等）。
// 范式见 skill 的 patterns/datadef.md：Load-Modify-Return，改完由引擎自动 Commit。
package datadef

import "github.com/qw576483/clover-server-engine/pkg/domain/data"

// PlayerSchema 玩家档案：按玩家维度存储，仅自己可见。
var PlayerSchema = data.StructSchema{
	Type:       "player",
	OwnerType:  data.OwnerPlayer,
	Visibility: data.ClientSelfOnly,
}

// PlayerData 玩家档案数据体。
//
//	Nickname   昵称（创角时设置）
//	Deck       8 个卡 id（对应配表 卡牌_cs.id）；空 = 未设置。
//	           **恒等于 Decks[ActiveSlot]** —— 对局、房间座位缓存、档案回包都只读它。
//	Decks      5 个卡组槽位（下标 0..4 = 界面上的 1..5 号卡组），nil = 该槽还没存过
//	ActiveSlot 当前使用的槽位下标（0..4）；保存哪个槽，该槽就是当前槽
//	Wins       胜场数
//	Losses     负场数
//	Matches    打过的总局数（含平局）—— 资料页「参赛场次」
//	ThreeCrownWins 以 3 王冠取胜的局数 —— 资料页「三冠胜场」
//	CardPlays  出牌次数计数：卡 id（配表 卡牌_cs.id）→ 累计出牌次数。
//	           资料页「常用卡牌」= 计数最大的那一张；nil / 空 = 一张都没出过。
//
// `Deck` 与 `Decks`/`ActiveSlot` 的一致性由 `logic.normalizeDecks` 维护（读档案时补全、
// 保存卡组时写回）。旧档案只带 `deck` 字段 ⇒ 按「只有 1 号有卡组、其余为空」处理。
//
// `Matches` / `ThreeCrownWins` / `CardPlays` 的写入时机：一局结算时只把结果排进
// `logic.roomRegistry.pending`（结算在定时器 goroutine 里、没有 event.Ctx），
// 由下一次 `logic.loadPlayer` 取出补记（见 `logic/player.go`）。
type PlayerData struct {
	Nickname       string          `json:"nickname"`
	Deck           []int32         `json:"deck"`
	Decks          [][]int32       `json:"decks,omitempty"`
	ActiveSlot     int             `json:"active_slot,omitempty"`
	Wins           int32           `json:"wins"`
	Losses         int32           `json:"losses"`
	Matches        int32           `json:"matches"`
	ThreeCrownWins int32           `json:"three_crown_wins"`
	CardPlays      map[int32]int32 `json:"card_plays,omitempty"`
}

func init() {
	data.RegisterTypeBySchema(PlayerSchema)
}
