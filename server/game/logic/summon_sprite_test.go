package logic

import (
	"testing"

	"clover-cr/game/core"
)

// TestWitchSummonsSkeletonsWithOwnSpriteDir 钉死两件事：
//
//  1. 女巫确实每 7 s 召唤一波小骷髅（不是再召唤一只女巫）；
//  2. 召唤物的快照 `sprite_dir` 是**它自己**的（`chr_skeleton_out`），而 card_id 仍是父卡（女巫）。
//     客户端只按 card_id 画会把骷髅画成女巫，所以这个字段是协议契约。
func TestWitchSummonsSkeletonsWithOwnSpriteDir(t *testing.T) {
	const (
		witchID = int32(26010010)
		blue    = core.TeamBlue
	)
	deck := []int32{
		witchID, 26010011, 26010012, 26010003,
		26010004, 26010005, 26010007, 26010001,
	}
	ct := testTable(t)

	for seed := int64(1); seed <= 40; seed++ {
		b, err := core.NewBattle(core.Config{Seed: seed, Table: ct, DeckA: deck, DeckB: deck})
		if err != nil {
			t.Fatalf("NewBattle 失败: %v", err)
		}
		if err := b.PlayCard(blue, witchID, 9000, 10000); err != nil {
			continue // 这张卡不在起手 4 张里：换种子
		}

		// 落位 1 s + 首波间隔 7 s：跑到 8.1 s，首波刚落地（再晚会被塔打死，数不到）。
		for i := 0; i < 810; i++ {
			b.Step()
		}
		snap := b.Snapshot()
		var skeletons, witches int
		for _, e := range snap.Entities {
			if e.Team != int32(blue) {
				continue
			}
			switch e.SpriteDir {
			case "chr_skeleton_out":
				skeletons++
			case "chr_witch_out":
				witches++
			}
		}
		if skeletons == 0 {
			t.Fatalf("seed=%d：女巫 9 s 后没有召唤出小骷髅（场上有 %d 只女巫）", seed, witches)
		}
		if witches > 1 {
			t.Fatalf("seed=%d：女巫召唤出了额外的女巫（场上女巫=%d）", seed, witches)
		}
		for _, e := range snap.Entities {
			if e.SpriteDir == "chr_skeleton_out" && e.CardID != witchID {
				t.Fatalf("seed=%d：骷髅的 card_id=%d，期望仍是父卡 %d", seed, e.CardID, witchID)
			}
		}
		return
	}
	t.Fatalf("40 个种子里女巫都没进起手 4 张，测试无法开始")
}
