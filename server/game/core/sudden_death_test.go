package core

import "testing"

// enterOvertime 把对局推到加时段：常规 180 s 到时双方冠数相同 ⇒ 进加时。
func enterOvertime(t *testing.T, b *Battle) {
	t.Helper()
	b.tick = RegulationMs/MSecPerTick - 1
	b.serverMs = b.tick * MSecPerTick
	b.Step()
	if b.Ended() || b.Phase() != PhaseOvertime {
		t.Fatalf("fixture: 180 s 平冠没进加时（ended=%v phase=%d）", b.Ended(), b.Phase())
	}
}

// TestSuddenDeathInOvertime 断言加时里的**突然死亡**：某一 tick 恰有一方增冠 ⇒
// 立即判其胜（不等 300 s），口径出处 cr-sim cr_sim/engine/battle.py:2722-2733
// `_decide("sudden death")`。
func TestSuddenDeathInOvertime(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 61)
	enterOvertime(t, b)
	if b.ServerMs() < RegulationMs {
		t.Fatalf("fixture: serverMs=%d 还没进加时段", b.ServerMs())
	}

	red := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	red.hp = 1
	b.damageEntity(red, 1, nil)
	b.Step()

	r := b.Result()
	if !r.Ended || r.Draw {
		t.Fatalf("加时破塔应立即结束，结果 = %+v", r)
	}
	if r.Winner != TeamBlue {
		t.Fatalf("winner = %v，要 blue（是蓝方破的塔）", r.Winner)
	}
	if r.Reason != ReasonSuddenDeath {
		t.Fatalf("reason = %q，要 %q", r.Reason, ReasonSuddenDeath)
	}
	if r.CrownsA != 1 || r.CrownsB != 0 {
		t.Fatalf("crowns = %d:%d，要 1:0", r.CrownsA, r.CrownsB)
	}
	if b.Phase() != PhaseEnded {
		t.Fatalf("phase = %d，要 %d", b.Phase(), PhaseEnded)
	}
	t.Logf("加时 sudden death：serverMs=%d（< %d 就结束）winner=%v reason=%q crowns=%d:%d",
		b.ServerMs(), RegulationMs+OvertimeMs, r.Winner, r.Reason, r.CrownsA, r.CrownsB)
}

// TestSuddenDeathNeedsASingleScorer 断言突然死亡的边界：同一 tick **双方**各破一塔
// 不算突然死亡（继续打到 300 s），而加时**之前**已经掉过的塔也不触发（比的是本 tick
// 的增冠，不是"谁领先"）。
func TestSuddenDeathNeedsASingleScorer(t *testing.T) {
	// ① 同一 tick 双方各破一塔 ⇒ 不是突然死亡。
	b := mustBattle(t, troopDeck, troopDeck, 62)
	enterOvertime(t, b)
	b.damageEntity(towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0]), 1_000_000, nil)
	b.damageEntity(towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0]), 1_000_000, nil)
	b.Step()
	if b.Ended() {
		t.Fatalf("同一 tick 双方各破一塔不是突然死亡，结果 = %+v", b.Result())
	}
	if got := b.Crowns(TeamBlue); got != 1 {
		t.Fatalf("fixture: 蓝冠 = %d，要 1", got)
	}
	if got := b.Crowns(TeamRed); got != 1 {
		t.Fatalf("fixture: 红冠 = %d，要 1", got)
	}
	runSteps(b, 20)
	if b.Ended() {
		t.Fatalf("双方同 tick 得分后不得提前结束，结果 = %+v", b.Result())
	}

	// ② 加时前掉过的塔不算：常规期双方各破一塔（1:1）⇒ 180 s 平冠进加时，
	//    加时里没有新的破塔 ⇒ 不结束。
	c := mustBattle(t, troopDeck, troopDeck, 63)
	c.damageEntity(towerOf(t, c, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0]), 1_000_000, nil)
	c.damageEntity(towerOf(t, c, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0]), 1_000_000, nil)
	runSteps(c, 3)
	if c.Crowns(TeamBlue) != 1 || c.Crowns(TeamRed) != 1 {
		t.Fatalf("fixture: 常规期冠数 = %d:%d，要 1:1", c.Crowns(TeamBlue), c.Crowns(TeamRed))
	}
	enterOvertime(t, c)
	runSteps(c, 20)
	if c.Ended() {
		t.Fatalf("加时前掉过的塔不该触发突然死亡，结果 = %+v", c.Result())
	}
	t.Log("sudden death 边界：同 tick 双方得分不结束；加时前已掉的塔不触发（比的是本 tick 增冠）")
}
