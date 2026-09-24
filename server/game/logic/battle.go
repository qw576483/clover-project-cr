package logic

import (
	"errors"
	"fmt"
	"sync/atomic"
	"time"

	"clover-cr/game/core"
	"clover-cr/game/def"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
	"github.com/qw576483/clover-server-engine/pkg/shared/proto"
	"github.com/qw576483/clover-server-engine/pkg/transport/event"
)

const (
	// battleTickInterval 对局 tick：core 固定 20 TPS（50 ms/帧，见 core/units.go）。
	battleTickInterval = 50 * time.Millisecond
	// snapshotEveryTicks 快照频率：每 2 tick = 100 ms = 10 Hz（项目约定，加严项）。
	snapshotEveryTicks = 2
	// aiDecisionTicks AI 决策频率：每 10 tick = 500 ms（不是每 tick，省算力）。
	aiDecisionTicks = 10
	// battleScopePrefix 对局 tick 的定时器 scope 前缀。
	//
	// ★ 为什么不等于连接 owner：引擎在硬掉线时只清 StopTimerGroup(owner) 这一个 scope，
	// 而对局 tick **不能**随某一方掉线停（对手还要接着打完这一局）。
	// 所以 tick 挂在一个带前缀的独立 scope 上，由结算 / 回收房间时显式 StopTimerGroup 收尾。
	battleScopePrefix = "battle:"
)

// battleScope 房间对应的定时器 scope。
func battleScope(roomID string) string { return battleScopePrefix + roomID }

// seedSeq 对局种子序号（时间戳 + 序号，同一毫秒内连续开打也不会撞种子）。
var seedSeq atomic.Int64

func nextSeed() int64 {
	return time.Now().UnixNano() ^ (seedSeq.Add(1) << 17)
}

// battleTimeline 下发对局时间轴常量（客户端据它画圣水条 / 计时器）。
func battleTimeline() def.BattleTimeline {
	return def.BattleTimeline{
		RegulationMs:    core.RegulationMs,
		OvertimeMs:      core.OvertimeMs,
		StartingElixir:  core.StartingElixirMilli / core.MilliTilePerTile,
		MaxElixir:       core.MaxElixirMilli / core.MilliTilePerTile,
		ElixirMsPerUnit: []int32{core.ElixirMsPerUnit[0], core.ElixirMsPerUnit[1], core.ElixirMsPerUnit[2]},
		ElixirPhaseMs:   []int32{core.ElixirPhaseMs[0], core.ElixirPhaseMs[1], core.ElixirPhaseMs[2]},
	}
}

// ===========================================================================
// 开打
// ===========================================================================

// startMatch 创建对局实例并启动 tick。
// 前置：房间有 2 个座位、每座位都有 8 张卡组（由 roomRegistry.checkStartable 保证）。
func (l *gameLogic) startMatch(roomID string) error {
	if l.cards == nil {
		return errors.New("配表未就绪，无法开打")
	}

	l.rooms.mu.Lock()
	st := l.rooms.rooms[roomID]
	if st == nil {
		l.rooms.mu.Unlock()
		return fmt.Errorf("房间不存在")
	}
	if st.started {
		l.rooms.mu.Unlock()
		return fmt.Errorf("对局已开始")
	}
	if len(st.members) != maxRoomMembers {
		l.rooms.mu.Unlock()
		return fmt.Errorf("需要 %d 个座位才能开打", maxRoomMembers)
	}
	// 座位号 = 队伍号：0 = BLUE，1 = RED。
	deckA := append([]int32(nil), st.decks[st.members[0].PlayerID]...)
	deckB := append([]int32(nil), st.decks[st.members[1].PlayerID]...)
	seed := nextSeed()

	b, err := core.NewBattle(core.Config{Seed: seed, Table: l.cards, DeckA: deckA, DeckB: deckB})
	if err != nil {
		l.rooms.mu.Unlock()
		return fmt.Errorf("对局创建失败：%w", err)
	}

	st.battle = b
	st.lastSnap = core.Snapshot{}
	st.hasLastSnap = false
	st.seed = seed
	st.ticks = 0
	st.ticking = false
	st.started = true
	st.ended = false
	for _, m := range st.members {
		m.Ready = false
	}

	notify := def.BattleStartNotify{
		RoomID:   roomID,
		Seed:     seed,
		ServerMs: 0,
		Timeline: battleTimeline(),
		DeckA:    deckA,
		DeckB:    deckB,
		HandA:    b.Hand(core.TeamBlue),
		NextA:    b.Next(core.TeamBlue),
		HandB:    b.Hand(core.TeamRed),
		NextB:    b.Next(core.TeamRed),
	}
	targets := make([]string, 0, maxRoomMembers)
	seats := make([]int, 0, maxRoomMembers)
	for i, m := range st.members {
		if !m.IsAI {
			targets = append(targets, m.PlayerID)
			seats = append(seats, i)
		}
	}
	l.rooms.mu.Unlock()

	for i, pid := range targets {
		n := notify
		n.MyTeam = int32(seats[i])
		if err := l.g.PushToPlayer(pid, def.PushBattleStart, n); err != nil {
			logger.Warnf("logic: 推送开打失败 room=%s player=%s: %v", roomID, pid, err)
		}
	}
	logger.Infof("logic: 对局开打 room=%s seed=%d seat0=%s seat1=%s deckA=%v deckB=%v",
		roomID, seed, st.members[0].PlayerID, st.members[1].PlayerID, deckA, deckB)

	l.g.Timer.TimerGroup(battleScope(roomID)).Every("tick", battleTickInterval, func() {
		l.battleTick(roomID)
	})
	l.pushRoomState(roomID)
	l.pushRoomList()
	return nil
}

// stopBattleTimer 停掉某房间的对局 tick（结算 / 房间回收 / 起服退出）。
func (l *gameLogic) stopBattleTimer(roomID string) {
	l.g.Timer.StopTimerGroup(battleScope(roomID))
}

// ===========================================================================
// tick 驱动
// ===========================================================================

// battleTick 推进一帧：Step → 事件 → 快照（每 2 tick）→ AI（每 10 tick）→ 结算。
//
// 引擎的定时任务是在独立 goroutine 里跑的，所以：
//   - 全部对局状态改动都在 registry 锁内完成；
//   - 用 st.ticking 做重入保护：上一帧还没跑完就跳过本帧，避免两个 goroutine
//     同时 Step 同一个 Battle（core 的 Battle 不是并发安全的）。
//
// 推送（网络 I/O）一律在锁外做。
func (l *gameLogic) battleTick(roomID string) {
	l.rooms.mu.Lock()
	st := l.rooms.rooms[roomID]
	if st == nil || st.battle == nil {
		l.rooms.mu.Unlock()
		l.stopBattleTimer(roomID) // 房间已回收 ⇒ 这个 tick 不该再跑
		return
	}
	if st.ticking {
		l.rooms.mu.Unlock()
		logger.Warnf("logic: 对局 tick 重入（上一帧未跑完），跳过 room=%s tick=%d", roomID, st.ticks)
		return
	}
	st.ticking = true

	b := st.battle
	b.Step()
	st.ticks++

	// AI 决策：每 500 ms 一次，落点仍然走 core.PlayCard（唯一入口，与真人同一条校验路径）。
	if seat := st.aiSeatLocked(); seat >= 0 && st.ticks%aiDecisionTicks == 0 {
		team := core.Team(seat)
		if cardID, x, y, ok := core.Decide(b, team); ok {
			if err := b.PlayCard(team, cardID, x, y); err != nil {
				logger.Warnf("logic: AI 出牌被拒 room=%s team=%d card=%d (%d,%d): %v",
					roomID, seat, cardID, x, y, err)
			} else {
				logger.Infof("logic: AI 出牌成功 room=%s team=%d card=%d (%d,%d) elixir=%d tick=%d",
					roomID, seat, cardID, x, y, b.Elixir(team), st.ticks)
			}
		}
	}

	events := b.DrainEvents()
	var snap *core.Snapshot
	if st.ticks%snapshotEveryTicks == 0 {
		s := b.Snapshot()
		st.lastSnap = s
		st.hasLastSnap = true
		snap = &s
	}
	ended := b.Ended()
	var result core.Result
	var targets []string
	seats := make([]int, 0, maxRoomMembers)
	if ended {
		result = b.Result()
		st.ended = true
		st.started = false
		// 清理对局实例：本局结束，房间回到未开打态（能再来一局）。
		st.battle = nil
	}
	targets = append(targets, st.humanIDs()...)
	for i, m := range st.members {
		if !m.IsAI {
			seats = append(seats, i)
		}
	}
	st.ticking = false
	l.rooms.mu.Unlock()

	if len(events) > 0 {
		payload := def.BattleEventNotify{Events: eventsToDef(events)}
		for _, pid := range targets {
			if err := l.g.PushToPlayer(pid, def.PushBattleEvent, payload); err != nil {
				logger.Warnf("logic: 推送对局事件失败 room=%s player=%s: %v", roomID, pid, err)
			}
		}
	}
	if snap != nil {
		payload := snapshotPayload(roomID, *snap)
		for _, pid := range targets {
			// 快照走高丢包容忍的 BestEffort（引擎在纯 TCP 且未绑 UDP 时自动降级为可靠发送）。
			if err := l.g.PushToPlayer(pid, def.PushBattleSnapshot, payload, proto.DeliveryModeBestEffort); err != nil {
				logger.Warnf("logic: 推送快照失败 room=%s player=%s: %v", roomID, pid, err)
			}
		}
	}
	if ended {
		l.stopBattleTimer(roomID)
		l.finishMatch(roomID, result, targets, seats)
		return
	}
}

// finishMatch 结算：推 BattleEnd、排队战绩、清理掉线座位、房间回到未开打态。
func (l *gameLogic) finishMatch(roomID string, res core.Result, targets []string, seats []int) {
	for i, pid := range targets {
		if i >= len(seats) {
			break
		}
		win := res.Ended && !res.Draw && int(res.Winner) == seats[i]
		payload := def.BattleEndNotify{
			Win:     win,
			Draw:    res.Draw,
			CrownsA: res.CrownsA,
			CrownsB: res.CrownsB,
			Reason:  res.Reason,
			HpRateA: res.HpRateA,
			HpRateB: res.HpRateB,
		}
		if err := l.g.PushToPlayer(pid, def.PushBattleEnd, payload); err != nil {
			logger.Warnf("logic: 推送结算失败 room=%s player=%s: %v", roomID, pid, err)
		}
		if res.Draw {
			// 平局不改胜负场（PlayerData 只有 Wins/Losses 两个计数，没有平局位）。
			continue
		}
		l.rooms.pushResult(pid, win)
	}
	logger.Infof("logic: 对局结束 room=%s reason=%s draw=%v winner=%d crownsA=%d crownsB=%d hpRateA=%d hpRateB=%d",
		roomID, res.Reason, res.Draw, res.Winner, res.CrownsA, res.CrownsB, res.HpRateA, res.HpRateB)

	l.rooms.dropOffline(roomID)
	l.tidyRoom(roomID)
	l.pushRoomState(roomID)
	l.pushRoomList()
}

// ===========================================================================
// handlers
// ===========================================================================

// onBattlePlayCard 出牌。这是唯一的出牌入口：真人走它，AI 走 core.PlayCard（同一函数）。
func (l *gameLogic) onBattlePlayCard(c event.Ctx) error {
	var req def.BattlePlayCardReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: BattlePlayCard 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.BattlePlayCardReply{Err: err.Error()})
		return nil
	}
	if err := l.rooms.playCard(req.RoomID, pid, req.CardID, req.XMilli, req.YMilli); err != nil {
		// 失败必须带原因（客户端要能提示"圣水不足 / 位置非法 / 不在手上"）。
		logger.Warnf("logic: 出牌被拒 room=%s player=%s card=%d (%d,%d): %v",
			req.RoomID, pid, req.CardID, req.XMilli, req.YMilli, err)
		l.g.Reply(c, def.BattlePlayCardReply{OK: false, Err: err.Error()})
		return nil
	}
	logger.Infof("logic: 出牌成功 room=%s player=%s card=%d (%d,%d)",
		req.RoomID, pid, req.CardID, req.XMilli, req.YMilli)
	l.g.Reply(c, def.BattlePlayCardReply{OK: true})
	return nil
}

// onBattleSurrender 投降（结算由 tick 在 50 ms 内推出）。
func (l *gameLogic) onBattleSurrender(c event.Ctx) error {
	var req def.BattleSurrenderReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: BattleSurrender 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.BattleSurrenderReply{OK: false})
		return nil
	}
	ok, serr := l.rooms.surrender(req.RoomID, pid)
	if !ok {
		logger.Warnf("logic: 投降失败 room=%s player=%s: %v", req.RoomID, pid, serr)
		l.g.Reply(c, def.BattleSurrenderReply{OK: false})
		return nil
	}
	logger.Infof("logic: 投降 room=%s player=%s", req.RoomID, pid)
	l.g.Reply(c, def.BattleSurrenderReply{OK: true})
	return nil
}

// onBattleSync 主动拉一次全量状态（进场 / 重连）。
func (l *gameLogic) onBattleSync(c event.Ctx) error {
	var req def.BattleSyncReq
	if err := c.BindMsg(&req); err != nil {
		logger.Warnf("logic: BattleSync 解析请求失败 account=%q: %v", c.Account(), err)
		_ = l.g.Alert(c, &proto.EAlertNotify{Title: "错误", Content: "参数错误", Level: "error"})
		return nil
	}
	_, pid, err := l.profile(c)
	if err != nil {
		l.warnNotLoggedIn(c, err, "登录状态异常，请重新登录")
		l.g.Reply(c, def.BattleSyncReply{})
		return nil
	}
	snap, ok := l.rooms.snapshotFor(req.RoomID, pid)
	if !ok {
		logger.Warnf("logic: 同步请求无效（不在房内或对局未开始）room=%s player=%s", req.RoomID, pid)
		l.g.Reply(c, def.BattleSyncReply{})
		return nil
	}
	logger.Infof("logic: 主动同步 room=%s player=%s seq=%d server_ms=%d",
		req.RoomID, pid, snap.Seq, snap.ServerMs)
	l.g.Reply(c, def.BattleSyncReply{Snapshot: snapshotPayload(req.RoomID, snap)})
	return nil
}

// ===========================================================================
// 适配：core.Snapshot → def.BattleSnapshot（逐字段搬运，不改 core）
// ===========================================================================

// snapshotPayload core.Snapshot → def.BattleSnapshot，并补上**房间号**。
//
// ★ 为什么在这里补：`core.Snapshot` 是纯玩法状态，不含"房间"概念（也不该含），
// 但客户端必须能分辨"这一帧属于哪个房间" —— 否则旧房间的 tick 若还在推快照，
// 它的 seq 更大，客户端会把新房的帧全判"倒退"丢掉（跨房间快照污染）。
// 所以 room_id 只在下发这一层贴上去，两条下发路径（周期推送 / BattleSync）共用本函数。
func snapshotPayload(roomID string, s core.Snapshot) def.BattleSnapshot {
	out := snapshotToDef(s)
	out.RoomID = roomID
	return out
}

func snapshotToDef(s core.Snapshot) def.BattleSnapshot {
	out := def.BattleSnapshot{
		Seq:      s.Seq,
		ServerMs: s.ServerMs,
		Phase:    s.Phase,
		ElixirA:  s.ElixirA,
		ElixirB:  s.ElixirB,
		CrownsA:  s.CrownsA,
		CrownsB:  s.CrownsB,
		HandA:    s.HandA,
		NextA:    s.NextA,
		HandB:    s.HandB,
		NextB:    s.NextB,
	}
	out.TowersA = towersToDef(s.TowersA)
	out.TowersB = towersToDef(s.TowersB)
	out.Entities = make([]def.EntitySnapshot, 0, len(s.Entities))
	for _, e := range s.Entities {
		out.Entities = append(out.Entities, def.EntitySnapshot{
			ID:       e.ID,
			Kind:     e.Kind,
			CardID:   e.CardID,
			Team:     e.Team,
			XMilli:   e.XMilli,
			YMilli:   e.YMilli,
			HP:       e.HP,
			MaxHP:    e.MaxHP,
			Anim:     e.Anim,
			Facing:   e.Facing,
			DeployMs: e.DeployMs,
		})
	}
	return out
}

func towersToDef(in []core.TowerSnap) []def.TowerState {
	out := make([]def.TowerState, 0, len(in))
	for _, t := range in {
		out = append(out, def.TowerState{ID: t.ID, Kind: t.Kind, HP: t.HP, MaxHP: t.MaxHP, Alive: t.Alive})
	}
	return out
}

func eventsToDef(in []core.Event) []def.BattleEvent {
	out := make([]def.BattleEvent, 0, len(in))
	for _, e := range in {
		out = append(out, def.BattleEvent{
			Kind:      e.Kind,
			CardID:    e.CardID,
			XMilli:    e.XMilli,
			YMilli:    e.YMilli,
			EntityID:  e.EntityID,
			Team:      e.Team,
			Text:      e.Text,
			ProjSpeed: e.ProjSpeed,
		})
	}
	return out
}
