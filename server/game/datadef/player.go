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
//	Nickname 昵称（创角时设置）
//	Deck     8 个卡 id（对应配表 卡牌_cs.id）；空 = 未设置
//	Wins     胜场数
//	Losses   负场数
type PlayerData struct {
	Nickname string  `json:"nickname"`
	Deck     []int32 `json:"deck"`
	Wins     int32   `json:"wins"`
	Losses   int32   `json:"losses"`
}

func init() {
	data.RegisterTypeBySchema(PlayerSchema)
}
