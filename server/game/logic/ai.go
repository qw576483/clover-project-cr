package logic

import (
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

// aiDeckRefKeys AI 卡组点名的 8 张卡（按 `card.tsv` 的 `key` 列写）。
//
// 出处 = 参考实现 `原版资源/cr-sim/cr_sim/train/run.py:55` 的 `DEFAULT_DECK`
// （逐字：`"Knight", "Musketeer", "Cannon", "Skeletons", "IceSpirits", "Log", "Fireball", "Goblins"`），
// 同一份常量也在 `cr_sim/play/server.py:46`。
// key 的对应关系：我方配表用连字符小写，其中 `IceSpirits` → `ice-spirit`、`Log` → `the-log`
// 是本工程的命名（其余 6 张逐字相同）。⛔ 不新增任何数值 —— 只搬参考实现点名的这 8 张。
//
// ★ 为什么这副卡组同时给了 D148 的答案：参考实现 `cli.py:298` 另外三副卡组
// （`hog_cycle` / `giant_beatdown` / `golem_beatdown`）**每一副都是 6 部队/建筑 + 2 法术**
// ⇒「AI 卡组要带法术」在参考实现里是**每一副都成立**的形状，不是本项目的自定规则。
var aiDeckRefKeys = []string{
	"knight", "musketeer", "cannon", "skeletons",
	"ice-spirit", "the-log", "fireball", "goblins",
}

// buildAIDeck 组装 AI 的卡组：按 `aiDeckRefKeys` 逐张从配表取（6 部队/建筑 + 2 法术）。
//
// ★ D148 修复（用户第 9 条「卡的实现没看到法术」的服务端根因）：
// 原实现是「按配表顺序取前 8 张**部队卡**」—— 它把法术卡**整个排除**在外 ⇒ AI 卡组里 0 张法术
// ⇒ `core/ai.go::decideSpell`（`:308`，规则是"能覆盖 ≥2 个敌方单位 或 法术总伤能补刀塔就放"）
// 是一段**永远走不到**的死代码 ⇒ 整局里**看不到对手放法术**。
// 客户端侧的主链路（`SpellFxTable` / 法术特效帧）上一轮已修好，但服务端从不发法术 ⇒ 依然看不到。
//
// 为什么不用"镜像真人卡组"：`MsgSaveDeck` 的合法性校验只管「8 张 / 不重复 / 存在于卡池」，
// 客户端完全可以交一副 8 张全法术的卡组；镜像过来 AI 会因"法术无处可打"而一张都不出，
// 那就不是「人机对战」了（验收明确要求「AI 至少出过 1 张牌」）。
//
// 降级链（源缺失时**必须留痕**，见 §5 的降级口径）：参考卡组点名的卡若在本表里找不到，
// 就按配表顺序补齐到 8 张（否则 `room.go` 的"座位没有 8 张卡组"会让 AI 房**开不了局**），
// 并打 Warn 说明缺的是哪几张 —— 「配表与参考实现脱节」这件事不许静默。
func buildAIDeck(ct *cardTable) []int32 {
	if ct == nil {
		return nil
	}

	byKey := make(map[string]int32, len(ct.cardRows))
	for _, row := range ct.cardRows {
		if row.Key == "" {
			continue
		}
		if _, dup := byKey[row.Key]; dup {
			continue // 重复 key 取首个（与 loadCardTable 的口径一致）
		}
		byKey[row.Key] = int32(row.Id)
	}

	out := make([]int32, 0, deckSize)
	picked := make(map[int32]bool, deckSize)
	missing := make([]string, 0, len(aiDeckRefKeys))
	for _, key := range aiDeckRefKeys {
		id, ok := byKey[key]
		if !ok {
			missing = append(missing, key)
			continue
		}
		if picked[id] {
			logger.Warnf("logic: AI 参考卡组 key=%s 与前面某张撞了同一个 id=%d（已跳过）", key, id)
			continue
		}
		picked[id] = true
		out = append(out, id)
	}

	if len(missing) > 0 {
		logger.Warnf("logic: AI 参考卡组（出处 cr-sim train/run.py:55 DEFAULT_DECK）有 %d 张在本表里找不到 key=%v ⇒ 按配表顺序补齐到 %d 张",
			len(missing), missing, deckSize)
		for _, row := range ct.cardRows {
			if len(out) == deckSize {
				break
			}
			id := int32(row.Id)
			if picked[id] {
				continue
			}
			picked[id] = true
			out = append(out, id)
		}
		if len(out) != deckSize {
			logger.Warnf("logic: AI 卡组只凑到 %d 张（卡池 %d 行不够？）", len(out), len(ct.cardRows))
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
