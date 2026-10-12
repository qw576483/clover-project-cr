package core

import "testing"

// TestProjectileHomingFromTable 断言制导口径**取自投射物行**（官方
// cards_stats_projectile.json 的 `homing`，经配表进 core.UnitDef.Homing）：
//
//   - homing=false（迫击炮 / 炸弹塔 / 炸弹兵 / 公主 / 屠夫 …）：落点固定在**发射
//     时刻的目标位置**，目标走开不改落点，飞到落点按半径结算 ⇒ 走位能躲掉；
//   - homing=true（箭 / 火枪 / 塔弹 …）：每 tick 重瞄当前位置。
func TestProjectileHomingFromTable(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, troopDeck, troopDeck, 71)
	mortar, ok := tb.Unit("Mortar")
	if !ok {
		t.Fatal("fixture: 没有 Mortar 行")
	}
	knight, _ := tb.Unit("Knight")

	shooter := b.spawnUnit(TeamBlue, mortar, 0, 5000, 4000, 0)
	tgt := b.spawnUnit(TeamRed, knight, cardKnight, 5000, 9000, 0)
	b.fireProjectile(shooter, tgt, 100)
	if len(b.projectiles) != 1 {
		t.Fatalf("fixture: 迫击炮弹没起飞（projectiles=%d）", len(b.projectiles))
	}
	shell := b.projectiles[0]
	if shell.homing {
		t.Fatal("MortarProjectile 官方 homing=false，弹道却是制导的（会追着目标飞）")
	}
	destX, destY := shell.destX, shell.destY
	if destX != tgt.xMilli || destY != tgt.yMilli {
		t.Fatalf("落点 (%d,%d) 不等于发射时刻的目标位置 (%d,%d)", destX, destY, tgt.xMilli, tgt.yMilli)
	}

	// 目标走开 5 格：非制导弹道不许改落点。
	tgt.setPos(tgt.xMilli+5000, tgt.yMilli)
	for i := 0; i < 10; i++ {
		b.advanceProjectile(shell)
	}
	if shell.destX != destX || shell.destY != destY {
		t.Fatalf("非制导弹道中途改了落点：(%d,%d) -> (%d,%d)", destX, destY, shell.destX, shell.destY)
	}

	// 飞完：伤害按**落点**结算（官方 `_impact` 的溅射分支），走开的目标不挨打。
	hp0 := tgt.hp
	for i := 0; i < 2000 && len(b.projectiles) > 0; i++ {
		b.stepProjectiles()
	}
	if tgt.hp != hp0 {
		t.Fatalf("目标走开后仍被非制导弹道命中：hp %d -> %d（落点是发射时刻的旧位置）", hp0, tgt.hp)
	}

	// 对照（负控）：制导的箭每 tick 重瞄目标当前位置。
	archer, _ := tb.Unit("Archer")
	a := b.spawnUnit(TeamBlue, archer, cardArcher, 5000, 4000, 0)
	tgt2 := b.spawnUnit(TeamRed, knight, cardKnight, 5000, 9000, 0)
	b.fireProjectile(a, tgt2, 10)
	arrow := b.projectiles[len(b.projectiles)-1]
	if !arrow.homing {
		t.Fatal("ArcherArrow 官方 homing=true，弹道却不是制导的")
	}
	tgt2.setPos(tgt2.xMilli+3000, tgt2.yMilli)
	b.advanceProjectile(arrow)
	if arrow.destX != tgt2.xMilli || arrow.destY != tgt2.yMilli {
		t.Fatalf("制导弹道没有重瞄：落点 (%d,%d)，目标 (%d,%d)",
			arrow.destX, arrow.destY, tgt2.xMilli, tgt2.yMilli)
	}
	t.Logf("homing：迫击炮落点锁定 (%d,%d)（目标走到 %d 仍不改）且不命中；箭重瞄到 (%d,%d)",
		destX, destY, tgt.xMilli, arrow.destX, arrow.destY)
}
