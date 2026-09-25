package logic

import (
	"testing"

	"clover-cr/game/datadef"
)

// 判据出处: 参考规格 §7 S7（卡组编辑 = 8 张）；默认卡组内容 =
// 参考实现 `原版资源/cr-sim/cr_sim/train/run.py:55` 的 `DEFAULT_DECK`
// （同一份常量在 ai.go::aiDeckRefKeys，逐 key 对应关系见其注释）。
//
// D171 场景：新角色档案里没有卡组 ⇒ 点「人机对战」时 `checkDeck` 直接拒
// （玩家看到的是"必须先去编队里点一次保存"）。创角那一刻落一套默认卡组 ⇒ 直接能开。
//
// 两侧断言：① 空档案 ⇒ 创角后卡组正好 8 张且过 checkDeck；
// ② 已有卡组 ⇒ 不被默认卡组覆盖（幂等）。
func TestNewCharacterGetsDefaultDeck(t *testing.T) {
	ct := testTable(t)
	l := &gameLogic{cards: ct, aiDeckIDs: buildAIDeck(ct)}

	var p datadef.PlayerData
	if len(p.Deck) != 0 {
		t.Fatalf("fixture：新角色档案不该带卡组，实际 %v", p.Deck)
	}
	if err := l.checkDeck(p.Deck); err == nil {
		t.Fatalf("fixture：空卡组本该被 checkDeck 拒（这正是缺陷的入口）")
	}

	l.ensureDefaultDeck(&p, "p_cr_new")
	if len(p.Deck) != deckSize {
		t.Fatalf("创角后卡组应为 %d 张，实际 %d 张（%v）", deckSize, len(p.Deck), p.Deck)
	}
	if err := l.checkDeck(p.Deck); err != nil {
		t.Fatalf("创角后直接开局应通过卡组校验，实际被拒: %v", err)
	}
	defaultDeck := append([]int32(nil), p.Deck...)

	// 已有卡组不被覆盖（反序即一副不同的合法卡组）。
	own := append([]int32(nil), p.Deck...)
	for i, j := 0, len(own)-1; i < j; i, j = i+1, j-1 {
		own[i], own[j] = own[j], own[i]
	}
	p.Deck = own
	l.ensureDefaultDeck(&p, "p_cr_new")
	for i := range own {
		if p.Deck[i] != own[i] {
			t.Fatalf("已有卡组被默认卡组覆盖: %v -> %v", own, p.Deck)
		}
	}
	t.Logf("新角色默认卡组 = %v（%d 张，过 checkDeck）；已有卡组不被覆盖", defaultDeck, len(defaultDeck))
}
