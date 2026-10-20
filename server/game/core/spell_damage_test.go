package core

import "testing"

// 法术的两条伤害口径：
//
//	① 王冠塔吃 `crown_damage`（官方减伤），部队/建筑吃 `instant_damage`
//	   与 `damage_per_tick`。出处 = 策划/数值文档/spell_cs.txt 的 `crown_damage` 列
//	   （「对王冠塔的实际伤害」：火球 689/207 … 毒药 92/110）；
//	   参考实现同口径 `cr_sim/engine/battle.py:1893-1895`
//	   （area effect 逐目标取 `aspec.damage_to(victim.kind is EntityKind.TOWER)`）。
//	② 作用半径按**命中盒**算（半径 + 目标碰撞半径），不是中心点。
//	   出处 = `cr_sim/engine/battle.py:1862`
//	   `distance(...) > aspec.radius + victim.collision_radius`。
//
// 判据都是"塔少 207 而不是 688"这种能一眼看出因果的数，不是"大致一致"。
func TestSpellHitsCrownTowerWithCrownDamage(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, fullDeck, fullDeck, 401)
	sp := tb.cards[cardFireball].Spell
	if sp.CrownDamage <= 0 || sp.CrownDamage >= sp.InstantDamage {
		t.Fatalf("fixture: 火球对塔伤害应小于对部队伤害（crown=%d instant=%d）", sp.CrownDamage, sp.InstantDamage)
	}

	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	knight, _ := tb.Unit("Knight")
	// 落点旁 1 格：读"部队吃满伤害"那一档。
	troop := b.spawnUnit(TeamRed, knight, cardKnight, tower.xMilli+1000, tower.yMilli, 0)

	towerHP, troopHP := tower.hp, troop.hp
	b.castSpell(TeamBlue, tb.cards[cardFireball], tower.xMilli, tower.yMilli)

	if got := towerHP - tower.hp; got != sp.CrownDamage {
		t.Fatalf("公主塔吃了一发火球掉 %d 血，应为 crown_damage=%d（instant_damage=%d 是打部队的）",
			got, sp.CrownDamage, sp.InstantDamage)
	}
	if got := troopHP - troop.hp; got != sp.InstantDamage {
		t.Fatalf("部队吃了一发火球掉 %d 血，应为 instant_damage=%d", got, sp.InstantDamage)
	}
	t.Logf("火球：塔 -%d（crown_damage） / 部队 -%d（instant_damage）", sp.CrownDamage, sp.InstantDamage)
}

// TestSpellDotHitsCrownTowerPerTick pin 持续伤害法术：每一跳同样分两档
// （毒药的 crown_damage 反而**高于**每跳部队伤害：92 → 110）。
func TestSpellDotHitsCrownTowerPerTick(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, fullDeck, fullDeck, 402)
	poison := &CardDef{
		ID: 99, Key: "poison-test", NameCN: "伤害药水法术", Kind: CardTypeSpell, Elixir: 4,
		Spell: &SpellDef{
			ID: 99, Key: "poison-test", Elixir: 4, RadiusMilli: 3500,
			DurationMs: 1000, TickMs: 250, DamagePerTick: 92, CrownDamage: 110,
			Anywhere: true, OnWater: true,
		},
	}
	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	knight, _ := tb.Unit("Knight")
	troop := b.spawnUnit(TeamRed, knight, cardKnight, tower.xMilli+1000, tower.yMilli, 0)

	towerHP, troopHP := tower.hp, troop.hp
	b.castSpell(TeamBlue, poison, tower.xMilli, tower.yMilli)
	// 1000 ms / 250 ms = 4 跳。
	runSteps(b, 20)

	if got := towerHP - tower.hp; got != 4*110 {
		t.Fatalf("塔吃了 4 跳毒药掉 %d 血，应为 4 x crown_damage(110) = %d", got, 4*110)
	}
	if got := troopHP - troop.hp; got != 4*92 {
		t.Fatalf("部队吃了 4 跳毒药掉 %d 血，应为 4 x damage_per_tick(92) = %d", got, 4*92)
	}
	t.Logf("毒药 4 跳：塔 -%d（110/跳） / 部队 -%d（92/跳）", towerHP-tower.hp, troopHP-troop.hp)
}

// TestSpellAreaCountsHitboxRadius pin 作用半径的口径：中心距 2800 在 2500 半径之外，
// 但骑士半径 500 ⇒ 命中盒（2800 ≤ 2500+500）在圈内，必须吃伤害；
// 中心距 3200 连命中盒都在圈外 ⇒ 一点伤害都不该吃（负控）。
func TestSpellAreaCountsHitboxRadius(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, fullDeck, fullDeck, 403)
	fireball := tb.cards[cardFireball].Spell
	knight, _ := tb.Unit("Knight")
	if knight.RadiusMilli <= 0 {
		t.Fatal("fixture: 骑士必须有碰撞半径，否则这条断言不成立")
	}

	x, y := int32(9000), int32(8000)
	edge := b.spawnUnit(TeamRed, knight, cardKnight, x+fireball.RadiusMilli+300, y, 0)
	out := b.spawnUnit(TeamRed, knight, cardKnight, x+fireball.RadiusMilli+knight.RadiusMilli+200, y, 0)

	b.castSpell(TeamBlue, tb.cards[cardFireball], x, y)

	if edge.hp == edge.maxHP {
		t.Fatalf("中心距 %d（半径 %d + 命中盒 %d）内的单位没吃到法术伤害：作用圈按中心点算了",
			fireball.RadiusMilli+300, fireball.RadiusMilli, knight.RadiusMilli)
	}
	if out.hp != out.maxHP {
		t.Fatalf("中心距 %d 的单位本应在圈外，却掉了 %d 血（半径算多了）",
			fireball.RadiusMilli+knight.RadiusMilli+200, out.maxHP-out.hp)
	}
	t.Logf("作用圈：中心距 %d 命中 / 中心距 %d 不中（半径 %d + 碰撞半径 %d）",
		fireball.RadiusMilli+300, fireball.RadiusMilli+knight.RadiusMilli+200,
		fireball.RadiusMilli, knight.RadiusMilli)
}
