package logic

import (
	"clover-cr/game/core"
	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
	"github.com/qw576483/clover-server-engine/pkg/shared/proto"
	"github.com/qw576483/clover-server-engine/pkg/transport/event"
)

// 人机对战两条入口（用户点名功能）：
//  1. 训练场：MsgAiBattleStart —— 直接建一个「房主 + AI」的房间并开打；
//  2. 房间 AI 补位：MsgRoomSetAi(ai_fill=true) + MsgRoomStart —— 单人房也能开打，1 号座位由 AI 顶。
//
// 两条路径都**复用真人路径的全部校验**（建房 → 进房 → 卡组 → checkStartable → startMatch），
// AI 只是一次占座（memberState.IsAI）+ 在 tick 里调 core.Decide / core.PlayCard，
// ⛔ 没有给 AI 开任何后门。

// buildAIDeck 组装 AI 的卡组：按配表顺序取前 8 张**部队卡**。
//
// 为什么不用"镜像真人卡组"：MsgSaveDeck 的合法性校验只管「8 张 / 不重复 / 存在于卡池」，
// 客户端完全可以交一副 8 张全法术的卡组；镜像过来 AI 会因"法术无处可打"而一张都不出，
// 那就不是「人机对战」了（验收明确要求「AI 至少出过 1 张牌」）。
// 为什么不另立一套强度数值：官方 trainer 卡组数据不在本项目的原版资源里，
// ⛔ 不许编卡表；这里只用本表已有的卡 id，不引入任何新数值。
func buildAIDeck(ct *cardTable) []int32 {
	if ct == nil {
		return nil
	}
	out := make([]int32, 0, deckSize)
	for _, row := range ct.cardRows {
		if int32(row.Type) != core.CardTypeTroop {
			continue
		}
		out = append(out, int32(row.Id))
		if len(out) == deckSize {
			break
		}
	}
	return out
}

// aiDeck 返回装配期定下的 AI 卡组。
func (l *gameLogic) aiDeck() []int32 {
	return l.aiDeckIDs
}

// onAiBattleStart 主菜单「人机对战」：建房 + AI 占座 + 立刻开打。
func (l *gameLogic) onAiBattleStart(c event.Ctx) error {
	var req def.AiBattleStartReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: AiBattleStart 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}

	// 卡组：优先用请求里带的（客户端卡组页的选择），否则用档案里存的。
	deck := req.Deck
	if len(deck) != deckSize {
		deck = p.Deck
	}
	if err := l.checkDeck(deck); err != nil {
		logger.Warnf("logic: 人机对战卡组非法 player=%s deck=%v: %v", pid, deck, err)
		l.g.Reply(c, def.AiBattleStartReply{Err: "卡组非法：" + err.Error()})
		return nil
	}
	if err := l.checkDeck(l.aiDeck()); err != nil {
		logger.Errorf("logic: AI 卡组非法（装配期问题）deck=%v: %v", l.aiDeck(), err)
		l.g.Reply(c, def.AiBattleStartReply{Err: "AI 卡组不可用"})
		return nil
	}

	l.leaveRoomsOf(pid)
	id := l.rooms.nextRoomID()
	if err := l.roomMod.EnsureRoom(id); err != nil {
		logger.Errorf("logic: 人机对战建房失败 room=%s player=%s: %v", id, pid, err)
		l.g.Reply(c, def.AiBattleStartReply{Err: "建房失败：" + err.Error()})
		return nil
	}
	if _, switched, joinErr := l.roomMod.JoinRoom(c.ConnID(), id, pid); joinErr != nil {
		logger.Errorf("logic: 人机对战进房失败 room=%s player=%s: %v", id, pid, joinErr)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: "进房失败：" + joinErr.Error()})
		return nil
	} else if switched {
		logger.Warnf("logic: 人机对战房间被路由到其它节点 room=%s player=%s", id, pid)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: "房间被路由到其它节点"})
		return nil
	}
	if err := l.rooms.configure(id, aiRoomName, pid, p.Nickname); err != nil {
		logger.Errorf("logic: 人机对战元数据失败 room=%s: %v", id, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}
	if err := l.rooms.setDeck(id, pid, deck); err != nil {
		logger.Errorf("logic: 人机对战记录卡组失败 room=%s: %v", id, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}
	// 直接置 ai_fill 并补位（训练场不需要客户端再点一次"设 AI"）。
	if err := l.rooms.setAiFill(id, pid, true); err != nil {
		logger.Errorf("logic: 人机对战开启 AI 补位失败 room=%s: %v", id, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}
	if filled, ferr := l.rooms.fillAI(id, l.aiDeck()); ferr != nil || !filled {
		logger.Errorf("logic: 人机对战 AI 补位失败 room=%s filled=%v: %v", id, filled, ferr)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: "AI 补位失败"})
		return nil
	}
	if err := l.rooms.checkStartable(id); err != nil {
		logger.Errorf("logic: 人机对战开打校验失败 room=%s: %v", id, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}
	if err := l.startMatch(id); err != nil {
		logger.Errorf("logic: 人机对战开打失败 room=%s player=%s: %v", id, pid, err)
		_ = l.roomMod.DestroyRoom(id)
		l.rooms.destroy(id)
		l.g.Reply(c, def.AiBattleStartReply{Err: err.Error()})
		return nil
	}

	logger.Infof("logic: 人机对战开打 room=%s player=%s deck=%v aiDeck=%v", id, pid, deck, l.aiDeck())
	l.g.Reply(c, def.AiBattleStartReply{OK: true, RoomID: id})
	return nil
}
