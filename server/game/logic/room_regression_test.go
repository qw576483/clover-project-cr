package logic

import (
	"testing"

	"clover-cr/game/core"
)

// 结算的座位口径：`core.Result.Winner` 是 Team（= 开局时的座位号），
// 而 `members` 的下标会因中途有人离房前移 ⇒ 结算必须按开局座位表算人。
func TestResultForSeatMapsWinnerAfterSeatShift(t *testing.T) {
	// 蓝方（座位 0）投降 ⇒ 红方（座位 1）胜。
	surrender := core.Result{Ended: true, Winner: core.TeamRed, Reason: core.ReasonSurrender}

	if got := resultForSeat(1, surrender, nil); !got.Win {
		t.Fatalf("座位 1（红方）应记胜，实际 Win=%v", got.Win)
	}
	// 负控：拿"离房后前移的下标 0"去算，就会把胜方记成负方。
	if got := resultForSeat(0, surrender, nil); got.Win {
		t.Fatalf("座位 0 是认输的一方，不该记胜")
	}

	// 王冠数按座位取：2:1 时座位 1 应拿到 1 冠。
	crowns := core.Result{Ended: true, Winner: core.TeamBlue, CrownsA: 2, CrownsB: 1}
	if got := resultForSeat(0, crowns, nil).Crowns; got != 2 {
		t.Fatalf("座位 0 的王冠 = %d，应为 CrownsA=2", got)
	}
	if got := resultForSeat(1, crowns, nil).Crowns; got != 1 {
		t.Fatalf("座位 1 的王冠 = %d，应为 CrownsB=1", got)
	}

	// 平局：不判胜负，但参赛场次要算（Draw=true）。
	if got := resultForSeat(0, core.Result{Ended: true, Draw: true}, nil); got.Win || !got.Draw {
		t.Fatalf("平局结果 Win=%v Draw=%v，应为 false/true", got.Win, got.Draw)
	}
}

// 开局座位表在一局内不变：有人离房（成员下标前移）之后，
// `matchSeat` 仍返回他开局时的座位号。
func TestMatchSeatStableWhenMemberLeaves(t *testing.T) {
	r := newRoomRegistry()
	if err := r.ensure("r1"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r1", "p_a")
	_ = r.configure("r1", "测试房", "p_a", "小蓝A")
	_ = r.join("r1", "p_b")

	r.mu.Lock()
	st := r.rooms["r1"]
	st.seats = map[string]int{"p_a": 0, "p_b": 1}
	st.started = true
	r.mu.Unlock()

	// 座位 0（房主）在对局中离房。
	if err := r.leave("r1", "p_a"); err != nil {
		t.Fatalf("leave: %v", err)
	}

	r.mu.Lock()
	defer r.mu.Unlock()
	if got := st.seatOf("p_b"); got != 0 {
		t.Fatalf("fixture: 离房后 p_b 的下标应前移到 0，实际 %d", got)
	}
	if got := st.matchSeat("p_b"); got != 1 {
		t.Fatalf("p_b 的开局座位应保持 1（红方），实际 %d —— 结算会把他算成蓝方", got)
	}
	if got := st.matchSeat("p_a"); got != 0 {
		t.Fatalf("离房的 p_a 开局座位仍是 0，实际 %d", got)
	}
	if got := st.matchSeat("p_x"); got != -1 {
		t.Fatalf("不在本局名单里的玩家应返回 -1，实际 %d", got)
	}
}

// 对局结算后清理离线座位：房主自己掉线时，剩下的真人必须接任房主，
// 否则那个房间既开不了下一局（房主校验按 ID 比），也改不了 AI 补位。
func TestDropOfflinePromotesSurvivingHost(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	r := newRoomRegistry()
	if err := r.ensure("r1"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r1", "p_a")
	_ = r.configure("r1", "测试房", "p_a", "小蓝A")
	_ = r.join("r1", "p_b")

	b, err := core.NewBattle(core.Config{Seed: 5, Table: ct, DeckA: deck, DeckB: deck})
	if err != nil {
		t.Fatalf("NewBattle: %v", err)
	}
	r.mu.Lock()
	st := r.rooms["r1"]
	st.battle = b
	st.started = true
	st.seats = map[string]int{"p_a": 0, "p_b": 1}
	r.mu.Unlock()

	// 房主在对局中硬掉线：只标离线（座位保留到结算）。
	if _, seat, running, ok := r.detach("p_a"); !ok || !running || seat != 0 {
		t.Fatalf("房主掉线应保留座位 0，实际 seat=%d running=%v ok=%v", seat, running, ok)
	}

	empty, humans := r.dropOffline("r1")
	if empty || len(humans) != 1 || humans[0] != "p_b" {
		t.Fatalf("结算后应只剩 p_b，实际 empty=%v humans=%v", empty, humans)
	}
	host, _ := r.hostOf("r1")
	if host != "p_b" {
		t.Fatalf("房主掉线后剩下的真人应接任房主，实际 host=%q", host)
	}
	r.mu.Lock()
	isHost := len(st.members) == 1 && st.members[0].IsHost
	r.mu.Unlock()
	if !isHost {
		t.Fatalf("接任房主的人 IsHost 标记也要置上（members=%v）", humans)
	}
}

// 关掉 AI 补位必须把 AI 占的座位一起撤掉：否则房间在列表里永远满员，
// 真人进不来，而房主以为补位已经关了。
func TestSetAiFillOffReleasesAiSeat(t *testing.T) {
	ct := testTable(t)
	r := newRoomRegistry()
	if err := r.ensure("r1"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r1", "p_a")
	_ = r.configure("r1", "单人房", "p_a", "小蓝A")
	if err := r.setAiFill("r1", "p_a", true); err != nil {
		t.Fatalf("开启 AI 补位: %v", err)
	}
	filled, err := r.fillAI("r1", buildAIDeck(ct))
	if err != nil || !filled {
		t.Fatalf("AI 补位失败 filled=%v err=%v", filled, err)
	}
	if notify, _, ok := r.stateNotify("r1"); !ok || len(notify.Members) != maxRoomMembers {
		t.Fatalf("补位后应有 %d 个座位，实际 %d", maxRoomMembers, len(notify.Members))
	}

	if err := r.setAiFill("r1", "p_a", false); err != nil {
		t.Fatalf("关闭 AI 补位: %v", err)
	}
	notify, _, ok := r.stateNotify("r1")
	if !ok {
		t.Fatal("房间不见了")
	}
	if len(notify.Members) != 1 {
		t.Fatalf("关闭补位后应只剩 1 个座位，实际 %d（%v）", len(notify.Members), notify.Members)
	}
	for _, m := range notify.Members {
		if m.IsAI {
			t.Fatalf("关闭补位后仍有 AI 占座：%+v", m)
		}
	}
	if info := r.rooms["r1"].info(); info.Cur != 1 {
		t.Fatalf("房间列表里的人数应回到 1，实际 %d", info.Cur)
	}
	// 座位空出来后真人可以进。
	if err := r.join("r1", "p_b"); err != nil {
		t.Fatalf("关闭补位后真人应能进房: %v", err)
	}
}
