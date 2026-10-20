package logic

import (
	"encoding/json"
	"strings"
	"testing"
)

// 本文件钉住房间座位 / 卡组校验的边界行为（离线秒级，`go test ./game/logic/ -count=1`）：
//   - 1 个座位不能开打；3 个座位进不来；关掉 AI 补位后单人仍不能开打；
//   - 非房主的准备态翻转直接决定能否开打；
//   - 房主离房后剩下的人接任房主、准备态清零；
//   - 卡组：不足 8 张 / 重复 / 正好 8 张但不在卡池。

// TestRoomOneSeatCannotStart 单人房（未开 AI 补位）不能开打。
func TestRoomOneSeatCannotStart(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	r := newRoomRegistry()
	if err := r.ensure("r-one"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	if err := r.join("r-one", "p_a"); err != nil {
		t.Fatalf("join: %v", err)
	}
	if err := r.configure("r-one", "单人房", "p_a", "小蓝A"); err != nil {
		t.Fatalf("configure: %v", err)
	}
	if err := r.setDeck("r-one", "p_a", deck); err != nil {
		t.Fatalf("setDeck: %v", err)
	}

	err := r.checkStartable("r-one")
	if err == nil || !strings.Contains(err.Error(), "人数不足") {
		t.Fatalf("单人房应因人数不足被拒，实际 %v", err)
	}

	// 开 AI 补位 ⇒ 补齐 2 个座位后可以开打。
	if err := r.setAiFill("r-one", "p_a", true); err != nil {
		t.Fatalf("setAiFill: %v", err)
	}
	if filled, err := r.fillAI("r-one", deck); err != nil || !filled {
		t.Fatalf("AI 补位失败 filled=%v err=%v", filled, err)
	}
	if err := r.checkStartable("r-one"); err != nil {
		t.Fatalf("AI 补位后应可开打，实际被拒: %v", err)
	}

	// 关掉补位 ⇒ AI 座位撤掉，又回到人数不足。
	if err := r.setAiFill("r-one", "p_a", false); err != nil {
		t.Fatalf("关闭 AI 补位: %v", err)
	}
	if err := r.checkStartable("r-one"); err == nil {
		t.Fatal("关闭 AI 补位后单人房不应可开打")
	}
}

// TestRoomRejectsThirdSeat 2 个座位是上限，第 3 个人进不来。
func TestRoomRejectsThirdSeat(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	r := newRoomRegistry()
	if err := r.ensure("r-two"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r-two", "p_a")
	_ = r.configure("r-two", "双人房", "p_a", "小蓝A")
	_ = r.join("r-two", "p_b")
	if err := r.bindMember("r-two", "p_b", "小红B", deck); err != nil {
		t.Fatalf("bindMember: %v", err)
	}
	if err := r.setDeck("r-two", "p_a", deck); err != nil {
		t.Fatalf("setDeck: %v", err)
	}
	if !r.full("r-two") {
		t.Fatal("两个座位后 full 应为 true")
	}
	if err := r.join("r-two", "p_c"); err == nil {
		t.Fatal("第 3 个座位应被拒")
	}
	if got := len(r.players("r-two")); got != maxRoomMembers {
		t.Fatalf("房内真人 = %d，应为 %d", got, maxRoomMembers)
	}
}

// TestRoomReadyToggleGatesStart 非房主的准备态翻转直接决定能否开打。
func TestRoomReadyToggleGatesStart(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	r := newRoomRegistry()
	if err := r.ensure("r-ready"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r-ready", "p_a")
	_ = r.configure("r-ready", "准备房", "p_a", "小蓝A")
	_ = r.join("r-ready", "p_b")
	if err := r.bindMember("r-ready", "p_b", "小红B", deck); err != nil {
		t.Fatalf("bindMember: %v", err)
	}
	if err := r.setDeck("r-ready", "p_a", deck); err != nil {
		t.Fatalf("setDeck: %v", err)
	}

	if err := r.checkStartable("r-ready"); err == nil || !strings.Contains(err.Error(), "还没有准备") {
		t.Fatalf("非房主未准备时应被拒，实际 %v", err)
	}
	if err := r.setReady("r-ready", "p_b", true); err != nil {
		t.Fatalf("setReady(true): %v", err)
	}
	if err := r.checkStartable("r-ready"); err != nil {
		t.Fatalf("非房主已准备时应可开打，实际被拒: %v", err)
	}
	if err := r.setReady("r-ready", "p_b", false); err != nil {
		t.Fatalf("setReady(false): %v", err)
	}
	if err := r.checkStartable("r-ready"); err == nil {
		t.Fatal("取消准备后不应可开打")
	}
}

// TestRoomHostLeavePromotesAndClearsReady 房主离房：剩下的人接任房主，全员准备态清零。
func TestRoomHostLeavePromotesAndClearsReady(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	r := newRoomRegistry()
	if err := r.ensure("r-leave"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	_ = r.join("r-leave", "p_a")
	_ = r.configure("r-leave", "房主离房", "p_a", "小蓝A")
	_ = r.join("r-leave", "p_b")
	_ = r.bindMember("r-leave", "p_b", "小红B", deck)
	_ = r.setDeck("r-leave", "p_a", deck)
	if err := r.setReady("r-leave", "p_b", true); err != nil {
		t.Fatalf("setReady: %v", err)
	}

	if err := r.leave("r-leave", "p_a"); err != nil {
		t.Fatalf("leave: %v", err)
	}
	host, ok := r.hostOf("r-leave")
	if !ok || host != "p_b" {
		t.Fatalf("房主离房后 host = %q (ok=%v)，应为 p_b", host, ok)
	}
	board, _, ok := r.stateNotify("r-leave")
	if !ok || len(board.Members) != 1 {
		t.Fatalf("房主离房后成员 = %+v", board.Members)
	}
	if !board.Members[0].IsHost {
		t.Fatalf("接任者未带房主标记：%+v", board.Members[0])
	}
	if board.Members[0].Ready {
		t.Fatalf("座位号变化后准备态应清零：%+v", board.Members[0])
	}
}

// TestImportStateCapsSeats 导入的房间态最多保留 2 个座位：多出来的座位不会被
// 当成第 3 名玩家（座位号 = 队伍号，超出 2 个没有队伍可对应）。
func TestImportStateCapsSeats(t *testing.T) {
	r := newRoomRegistry()
	state := json.RawMessage(`{"room_id":"r-import","name":"导入房","host":"p_a",` +
		`"members":[{"player_id":"p_a","is_host":true},{"player_id":"p_b"},{"player_id":"p_c"}]}`)
	if err := r.importState("r-import", state); err != nil {
		t.Fatalf("importState: %v", err)
	}
	if got := len(r.players("r-import")); got != maxRoomMembers {
		t.Fatalf("导入后真人座位 = %d，应为 %d", got, maxRoomMembers)
	}
}

// TestCheckDeckRejectsNonPoolAndDuplicates 正好 8 张也可能非法：不在卡池 / 重复。
func TestCheckDeckRejectsNonPoolAndDuplicates(t *testing.T) {
	ct := testTable(t)
	l := &gameLogic{cards: ct}
	deck := buildAIDeck(ct)

	// 战斗单位表里有、但 card 表里没有的实体 id（pekka 的 unit id）。
	const unitOnlyID = int32(26020009)
	nonPool := []int32{unitOnlyID, deck[1], deck[2], deck[3], deck[4], deck[5], deck[6], deck[7]}
	if err := l.checkDeck(nonPool); err == nil || !strings.Contains(err.Error(), "不存在") {
		t.Fatalf("8 张含卡池外 id 应被拒，实际 %v", err)
	}

	dup := []int32{deck[0], deck[0], deck[2], deck[3], deck[4], deck[5], deck[6], deck[7]}
	if err := l.checkDeck(dup); err == nil || !strings.Contains(err.Error(), "重复") {
		t.Fatalf("8 张含重复 id 应被拒，实际 %v", err)
	}
	if err := l.checkDeck(deck[:7]); err == nil || !strings.Contains(err.Error(), "正好 8 张") {
		t.Fatalf("7 张应被拒，实际 %v", err)
	}
	if err := l.checkDeck(deck); err != nil {
		t.Fatalf("合法 8 张应通过，实际 %v", err)
	}
}
