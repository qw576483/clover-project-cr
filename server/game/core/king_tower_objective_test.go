package core

import "testing"

// TestGroundUnitTargetsKingTowerAfterPrincessFalls pins the two halves of the
// march objective: the tower a ground unit walks at is also the tower it
// holds as a target, and a unit whose lane's princess tower has fallen walks
// at the king tower and hits it.
//
// 判据出处: cr-sim engine/battle.py:2776-2793（`_pick_objective`：公主塔倒了以后
// 国王塔接管这条车道，目标与行军目标是同一次决策里的同一座塔）。
func TestGroundUnitTargetsKingTowerAfterPrincessFalls(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 41)
	knightDef, _ := b.cfg.Table.Unit("Knight")
	king := b.kings[TeamBlue]
	princess := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])

	// 红方骑士站在蓝方左车道（离左公主塔 2.5 格）。
	u := b.spawnUnit(TeamRed, knightDef, cardKnight, BluePrincessLeftPos[0], 9000, 0)

	// 该车道的公主塔被摧毁。
	b.damageEntity(princess, 1_000_000, nil)
	runSteps(b, 2)
	if princess.alive {
		t.Fatal("fixture error: the princess tower should be down")
	}
	if u.targetID != king.ID {
		t.Fatalf("knight target = %d, want the king tower %d", u.targetID, king.ID)
	}

	kingHP := king.hp
	runSteps(b, 200)
	if u.targetID != king.ID {
		t.Fatalf("knight dropped the king tower: target = %d, want %d", u.targetID, king.ID)
	}
	if u.swings == 0 {
		t.Fatal("knight never swung at the king tower")
	}
	if king.hp >= kingHP {
		t.Fatalf("king tower took no damage: hp %d -> %d", kingHP, king.hp)
	}
	t.Logf("公主塔倒下后骑士改打国王塔：target=%d (king %d)，knight swings=%d，king hp %d -> %d",
		u.targetID, king.ID, u.swings, kingHP, king.hp)
}

// TestNoGroundUnitStandsInKingFootprint pins the terrain rule: no ground unit
// may stand inside a king tower's blocked square at any tick, and none may sit
// on a tower's centre.
//
// 判据出处: 参考规格 §2（国王塔的 3x3 不可通行方块）+ cr-sim engine/battle.py:
// 2095-2117（`_place`：每一步落点都过地形检查）。
func TestNoGroundUnitStandsInKingFootprint(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 42)
	knightDef, _ := b.cfg.Table.Unit("Knight")
	giantDef, _ := b.cfg.Table.Unit("Giant")

	princess := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])
	// 两条车道各一支地面单位，直扑国王塔脚下。
	b.spawnUnit(TeamRed, knightDef, cardKnight, BluePrincessLeftPos[0], 9000, 0)
	b.spawnUnit(TeamRed, giantDef, cardGiant, BluePrincessRightPos[0], 9000, 0)
	// 该车道的公主塔被摧毁 ⇒ 目标变成国王塔。
	b.damageEntity(princess, 1_000_000, nil)

	for i := 0; i < 600; i++ {
		b.Step()
		for _, e := range b.units {
			if e.flying || !e.alive {
				continue
			}
			if b.arena.IsBlocked(e.xMilli, e.yMilli) {
				t.Fatalf("tick %d: ground unit %d stands inside the king tower's blocked square at (%d,%d)",
					b.tick, e.ID, e.xMilli, e.yMilli)
			}
			for _, tw := range b.towers {
				if tw.Kind != KindTower || !tw.alive {
					continue
				}
				if e.xMilli == tw.xMilli && e.yMilli == tw.yMilli {
					t.Fatalf("tick %d: ground unit %d stands on tower %d's centre (%d,%d)",
						b.tick, e.ID, tw.ID, e.xMilli, e.yMilli)
				}
			}
		}
	}
	t.Logf("600 tick 内没有地面单位进过国王塔的阻塞格，也没有与塔心重合")
}

// TestGroundUnitEscapesKingFootprint pins the escape: a unit that somehow
// stands inside the blocked square (a shove, a spawn on top of it) walks out
// of it instead of staying welded to the tower.
func TestGroundUnitEscapesKingFootprint(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 43)
	knightDef, _ := b.cfg.Table.Unit("Knight")
	king := b.kings[TeamBlue]

	u := b.spawnUnit(TeamRed, knightDef, cardKnight, king.xMilli, king.yMilli, 0)
	if !b.arena.IsBlocked(u.xMilli, u.yMilli) {
		t.Fatalf("fixture error: the unit must start inside the blocked square (%d,%d)", u.xMilli, u.yMilli)
	}
	for i := 0; i < 400; i++ {
		b.Step()
		if !b.arena.IsBlocked(u.xMilli, u.yMilli) {
			t.Logf("unit %d left the king tower's blocked square after %d ticks at (%d,%d)",
				u.ID, b.tick, u.xMilli, u.yMilli)
			return
		}
	}
	t.Fatalf("unit %d is still inside the king tower's blocked square at (%d,%d)",
		u.ID, u.xMilli, u.yMilli)
}
