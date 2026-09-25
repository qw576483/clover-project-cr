package core

import "testing"

// 判据出处: 参考规格 §5 索敌 = 在 `sight_range` 内取**距离最近**的合法目标
// （官方字段 `sight_range`）。
//
// 场景：红骑士沿左车道南下打蓝方左塔（arena.go `BluePrincessLeftPos` = 3.5,6.5），
// 蓝巨人从同一车道北上。骑士先锁塔（塔在 5500 视野内），巨人随后从骑士正面进入
// 视野且**比塔更近** ⇒ 骑士必须转火打巨人，而不是无视它直冲塔。
//
// 两侧断言互为负控：若有人把「旧目标还在视野内就永不换目标」的旧口径放回来，
// 骑士会一路走到塔前、巨人一点血也不掉 ⇒ 本条立刻失败。
func TestTargetSwitchToNearerUnitInTransit(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 21)
	knight, _ := b.cfg.Table.Unit("Knight")
	giant, _ := b.cfg.Table.Unit("Giant")
	tower := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])

	k := b.spawnUnit(TeamRed, knight, cardKnight, BluePrincessLeftPos[0], 13000, 0)
	b.Step() // 这一 tick 里骑士视野内只有蓝方左塔
	if k.targetID != tower.ID {
		t.Fatalf("fixture: knight target=%d, want the princess tower %d", k.targetID, tower.ID)
	}
	towerHP := tower.hp

	g := b.spawnUnit(TeamBlue, giant, cardGiant, BluePrincessLeftPos[0], 11500, 0)
	runSteps(b, 40) // 2 s：骑士 load_time 700 ms ⇒ 至少命中两次 202

	if g.hp >= giant.HP {
		t.Fatalf("the knight dealt no damage to the giant crossing its lane (giant hp %d/%d, knight target=%d)",
			g.hp, giant.HP, k.targetID)
	}
	if tower.hp != towerHP {
		t.Fatalf("the knight hit the tower instead of the nearer giant: tower hp %d -> %d", towerHP, tower.hp)
	}
	t.Logf("骑士转火打更近的巨人：giant hp %d -> %d, tower hp %d 未变 (knight target=%d, giant id=%d)",
		giant.HP, g.hp, tower.hp, k.targetID, g.ID)
}
