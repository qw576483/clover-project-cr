package core

import "testing"

// 判据出处: cr-sim engine/targeting.py:206-226 `should_keep_target`（目标存活 +
// 可被选 + 仍在 `sight_range` 内 ⇒ 保留）与 engine/battle.py:1078-1131
// `_phase_acquire_targets`（原文注释 "Targets are sticky"）。
//
// 场景：红骑士沿左车道南下打蓝方左塔（arena.go `BluePrincessLeftPos` = 3.5,6.5）。
// 骑士先锁塔；蓝巨人随后从骑士正面进入视野且**比塔更近**，但塔仍在骑士视野内
// ⇒ 骑士必须保留塔，不转火巨人。
//
// 反例（本条会抓到）：索敌若按「每 tick 重取视野内最近者」执行，骑士会立刻改锁
// 更近的巨人，目标 id 变掉 ⇒ 断言失败。
func TestTargetIsStickyWhileInSight(t *testing.T) {
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

	// 巨人在骑士正前方、比塔近得多；塔没有被击退，也没有离开视野。
	g := b.spawnUnit(TeamBlue, giant, cardGiant, BluePrincessLeftPos[0], 11500, 0)
	runSteps(b, 40)

	if k.targetID != tower.ID {
		t.Fatalf("knight dropped a target still inside sight: target=%d (tower %d, giant %d)",
			k.targetID, tower.ID, g.ID)
	}
	if g.hp != giant.HP {
		t.Fatalf("the knight turned onto the nearer giant: giant hp %d/%d", g.hp, giant.HP)
	}
	if tower.hp != towerHP {
		t.Fatalf("tower took damage in a fixture where the knight cannot reach it: %d -> %d",
			towerHP, tower.hp)
	}
	t.Logf("骑士保留视野内的原目标：target=%d (tower %d) 未随更近的巨人 %d 切换",
		k.targetID, tower.ID, g.ID)
}

// 判据出处: 同 `TestTargetIsStickyWhileInSight` 的 `should_keep_target` —— 「目标
// 死亡 / 离开视野」是仅有的两个放开条件。
//
// 场景：红骑士贴着蓝方左塔，前方一只蓝骷髅更近 ⇒ 先锁骷髅；骷髅 81 HP 被骑士
// 一击（伤害 202）打死 ⇒ 下一 tick 必须立刻重选视野内的次近合法目标（蓝方左塔）。
func TestRetargetsWhenTargetDies(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 22)
	knight, _ := b.cfg.Table.Unit("Knight")
	skelDef, _ := b.cfg.Table.Unit("Skeleton")
	tower := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])

	k := b.spawnUnit(TeamRed, knight, cardKnight, 3500, 9000, 0)
	skel := b.spawnUnit(TeamBlue, skelDef, cardSkeleton, 3500, 7900, 0)
	b.Step()
	if k.targetID != skel.ID {
		t.Fatalf("fixture: knight target=%d, want the nearer skeleton %d", k.targetID, skel.ID)
	}

	runSteps(b, 20) // 骑士 load_time 700 ms ⇒ 第 14 tick 落下那一刀
	if skel.alive {
		t.Fatalf("fixture: the knight did not kill the skeleton (hp %d)", skel.hp)
	}
	if k.targetID != tower.ID {
		t.Fatalf("knight did not re-acquire after its target died: target=%d, want the tower %d",
			k.targetID, tower.ID)
	}
	t.Logf("目标死亡后立即重选：skeleton 已死，knight target=%d (tower)", k.targetID)
}
