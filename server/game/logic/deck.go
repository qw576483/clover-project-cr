package logic

import (
	"fmt"

	"clover-cr/game/datadef"
	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
	"github.com/qw576483/clover-server-engine/pkg/shared/proto"
	"github.com/qw576483/clover-server-engine/pkg/transport/event"
)

// deckSize 一副卡组的卡数（参考规格 §7 S11：8 张卡组）。
const deckSize = 8

// deckSlots 卡组槽位数 = 界面上的 1..5 号（下标 0..4）。
const deckSlots = 5

// normalizeDecks 把档案里的卡组规范成 `deckSlots` 个槽位 + 合法的当前槽 + `Deck` 镜像。
//
// 三件事，都幂等：
//  1. `Decks` 补齐 / 截到 `deckSlots` 个；
//  2. `Decks` 全空而 `Deck` 有卡 ⇒ 当作「1 号槽」——旧档案（只带 `deck` 字段）走这条；
//  3. `ActiveSlot` 越界或指向空槽 ⇒ 退到第一套满 8 张的槽（都没有则留 0 号空槽）；
//     最后 `Deck` 恒等于 `Decks[ActiveSlot]`，对局 / 房间 / 档案回包读它。
func (l *gameLogic) normalizeDecks(p *datadef.PlayerData) {
	if p == nil {
		return
	}
	if len(p.Decks) != deckSlots {
		grown := make([][]int32, deckSlots)
		copy(grown, p.Decks)
		p.Decks = grown
	}
	if p.ActiveSlot < 0 || p.ActiveSlot >= deckSlots {
		p.ActiveSlot = 0
	}

	// `Deck` 有卡而当前槽不是它 ⇒ 以 `Deck` 为准写回当前槽。
	// 覆盖两种来源：旧档案（只有 `deck` 字段，槽位为空）、以及只写 `Deck` 的写入方。
	if len(p.Deck) > 0 && !sameDeck(p.Deck, p.Decks[p.ActiveSlot]) {
		p.Decks[p.ActiveSlot] = cloneDeck(p.Deck)
	}

	// 当前槽是空的（旧档案里没有这个槽）⇒ 退到第一套满 8 张的槽，别让对局侧拿到空卡组。
	if len(p.Decks[p.ActiveSlot]) != deckSize {
		for i := 0; i < deckSlots; i++ {
			if len(p.Decks[i]) == deckSize {
				p.ActiveSlot = i
				break
			}
		}
	}
	p.Deck = cloneDeck(p.Decks[p.ActiveSlot])
}

// sameDeck 逐张比较（顺序敏感：卡组是有序的 8 张）。
func sameDeck(a, b []int32) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

// cloneDeck 复制一份卡组；nil 进 nil 出。
func cloneDeck(ids []int32) []int32 {
	if len(ids) == 0 {
		return nil
	}
	return append([]int32(nil), ids...)
}

// slotDeck 取某槽的卡组；越界返回 nil（调用方负责先校验槽位）。
func slotDeck(p *datadef.PlayerData, slot int) []int32 {
	if p == nil || slot < 0 || slot >= len(p.Decks) {
		return nil
	}
	return p.Decks[slot]
}

// ensureDefaultDeck 给还没有卡组的角色落一套默认卡组（落在 1 号槽 = 下标 0，并设为当前槽）。
//
// 为什么必须在创角那一刻落：卡组只有 `onSaveDeck` 会写，而开局的两条路
// （`onAiBattleStart` / `room.go::checkStartable`）都要求卡组**已经**是合法的 8 张
// ⇒ 新角色直接点「人机对战」会被 checkDeck 拒掉，玩家看到的就是
// 「必须先去编队里点一次保存」。
//
// 默认卡组 = 参考实现 `原版资源/cr-sim/cr_sim/train/run.py:55` 的 `DEFAULT_DECK`
// （同一份常量已按 key 搬进 `ai.go::aiDeckRefKeys`，出处与 key 对应关系见其注释）。
//
// 幂等：任何槽里已有卡组就原样不动（编队页保存过的玩家不会被这里覆盖）。
func (l *gameLogic) ensureDefaultDeck(p *datadef.PlayerData, pid string) {
	if p == nil {
		return
	}
	l.normalizeDecks(p)
	for i := 0; i < deckSlots; i++ {
		if len(p.Decks[i]) > 0 {
			return
		}
	}
	deck := l.aiDeck()
	if err := l.checkDeck(deck); err != nil {
		// 非预期分支：默认卡组来自装配期（`aiDeckIDs`），非法说明配表没就绪或
		// 参考卡组点名的卡缺失 —— 留痕，但不改档案（宁可不给，也不给一套非法卡组）。
		logger.Errorf("logic: 默认卡组不合法 player=%s deck=%v: %v", pid, deck, err)
		return
	}
	p.Decks[0] = cloneDeck(deck)
	p.ActiveSlot = 0
	p.Deck = cloneDeck(deck)
	logger.Infof("logic: 落默认卡组 player=%s slot=1 cards=%d=%v", pid, len(p.Deck), p.Deck)
}

// checkDeck 校验卡组：正好 8 张、不重复、每张都在卡池里。
// 这是**协议层**的合法性校验（不是玩法规则：不判圣水、不判强度），
// 与 core.NewBattle 的 8 张断言同口径。
func (l *gameLogic) checkDeck(ids []int32) error {
	if len(ids) != deckSize {
		return fmt.Errorf("卡组必须正好 %d 张（当前 %d 张）", deckSize, len(ids))
	}
	if l.cards == nil {
		return fmt.Errorf("配表未就绪")
	}
	seen := make(map[int32]bool, deckSize)
	for _, id := range ids {
		if seen[id] {
			return fmt.Errorf("卡 %d 重复", id)
		}
		seen[id] = true
		if _, ok := l.cards.Card(id); !ok {
			return fmt.Errorf("卡 %d 不存在", id)
		}
	}
	return nil
}

// onGetCardPool 拉 60 张卡池（客户端卡组编辑页用）。
func (l *gameLogic) onGetCardPool(c event.Ctx) error {
	var req def.GetCardPoolReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: GetCardPool 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	if l.cards == nil {
		logger.Errorf("logic: 卡池请求但配表未就绪")
		l.g.Reply(c, def.GetCardPoolReply{})
		return nil
	}
	cards := l.cards.cardPool()
	logger.Infof("logic: 下发卡池 %d 张", len(cards))
	l.g.Reply(c, def.GetCardPoolReply{Cards: cards})
	return nil
}

// onGetDeck 拉我的卡组。
func (l *gameLogic) onGetDeck(c event.Ctx) error {
	var req def.GetDeckReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: GetDeck 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.GetDeckReply{})
		return nil
	}
	l.normalizeDecks(p)

	slot := req.Slot
	if slot < 0 {
		slot = p.ActiveSlot
	}
	if slot >= deckSlots {
		// 非预期分支：客户端槽位越界（消息号 / 下标口径不一致）。留痕，并按当前槽回包。
		logger.Warnf("logic: GetDeck 槽位越界 player=%s slot=%d，按当前槽 %d 回包", pid, req.Slot, p.ActiveSlot)
		slot = p.ActiveSlot
	}

	cards := slotDeck(p, slot)
	logger.Infof("logic: 下发卡组 player=%s slot=%d active=%d cards=%v",
		pid, slot+1, p.ActiveSlot+1, cards)
	l.g.Reply(c, def.GetDeckReply{CardIDs: cards, Slot: slot, ActiveSlot: p.ActiveSlot})
	return nil
}

// onSaveDeck 保存卡组（8 张）。
func (l *gameLogic) onSaveDeck(c event.Ctx) error {
	var req def.SaveDeckReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: SaveDeck 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	p, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.SaveDeckReply{Err: err.Error()})
		return nil
	}
	l.normalizeDecks(p)
	if req.Slot < 0 || req.Slot >= deckSlots {
		logger.Warnf("logic: SaveDeck 槽位越界 player=%s slot=%d", pid, req.Slot)
		l.g.Reply(c, def.SaveDeckReply{OK: false, Err: fmt.Sprintf("卡组号必须在 1..%d 之间", deckSlots)})
		return nil
	}
	if err := l.checkDeck(req.CardIDs); err != nil {
		logger.Warnf("logic: 卡组非法 player=%s slot=%d cards=%v: %v", pid, req.Slot+1, req.CardIDs, err)
		l.g.Reply(c, def.SaveDeckReply{OK: false, Err: err.Error()})
		return nil
	}

	// 保存哪个槽，那个槽就成为当前槽（进对局 / 房间座位缓存读的都是当前槽）。
	p.Decks[req.Slot] = cloneDeck(req.CardIDs)
	p.ActiveSlot = req.Slot
	p.Deck = cloneDeck(req.CardIDs)

	// ★ 若此刻在房间里，必须把新卡组同步进房间的座位缓存。
	//
	// 房间的卡组缓存 `roomState.decks` 只在**加入那一刻**由 bindMember 写一次，而
	// `checkStartable` 校验的正是这份缓存 ⇒ 不在这里同步，"先加入、后编辑卡组"这条
	// 最常见的路径会让房主开打时**永远**被拒，且原因看着莫名其妙：
	//
	//	实测（2026-09-20，两个真人同房）：
	//	  15:03:05 加入房间成功 room=r…-3 player=p_cr_b2 deck=0
	//	  15:03:28 保存卡组成功 player=p_cr_b2 cards=[26010001 … 26010008]
	//	  15:03:36 开打前置校验失败 room=r…-3 player=p_cr_cli: 座位 1（）还没有 8 张卡组
	//
	// 同步放在保存侧（而不是开打侧去逐个 loadPlayer）的理由：① 不额外加载别的玩家档案，
	// 不碰引擎"同一 handler 对同一 key 只许载一次"的约束；② 房间缓存从此**始终**等于档案，
	// 任何读它的地方都对；③ 改动只有这一处。
	if roomID, inRoom := l.rooms.roomIDOfPlayer(pid); inRoom {
		if err := l.rooms.setDeck(roomID, pid, p.Deck); err != nil {
			// 非预期分支：能查到房间却写不进缓存（并发离房）。留痕，但不影响保存本身的结果。
			logger.Warnf("logic: 保存卡组后同步房间缓存失败 room=%s player=%s: %v", roomID, pid, err)
		} else {
			logger.Infof("logic: 已把新卡组同步进房间缓存 room=%s player=%s cards=%d", roomID, pid, len(p.Deck))
		}
	}

	logger.Infof("logic: 保存卡组成功 player=%s slot=%d active=%d cards=%v",
		pid, req.Slot+1, p.ActiveSlot+1, p.Deck)
	l.g.Reply(c, def.SaveDeckReply{OK: true})
	return nil
}
