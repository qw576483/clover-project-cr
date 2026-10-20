package core

import "testing"

// 火球的飞行段时长（ms）= 客户端 `View/SpellFxTable` 的 `91f / 60f` s
// （原版 `fireball_projectile1` 的时间轴，出处 `策划/单位动画分组表.md:5276`）。
const fireballFlightMs = 1517

// TestSpellFlightDelaysImpactUntilLanding pin 带飞行段的法术**在弹体到达那一 tick**
// 才结算：出牌当 tick 与飞行途中目标一滴血都不掉，`flight_ms` 到期才掉。
//
// 因果链：客户端按 `SpellFxTable.Entry.FlightSeconds`（= `flight_ms / 1000`）把弹体从
// 施法者画到落点（`View/BattleViewRoot.PlaySpellFx`）⇒ 服务端提前结算就会看到
// "火球还没飞到，伤害已经打出去"。
func TestSpellFlightDelaysImpactUntilLanding(t *testing.T) {
	tb := newTestTable()
	spellDeck := []int32{cardFireball, cardFireball, cardFireball, cardFireball,
		cardFireball, cardFireball, cardFireball, cardFireball}
	b := mustBattleWith(t, tb, spellDeck, fullDeck, 511)
	setElixir(b, TeamBlue, MaxElixirMilli)

	sp := tb.cards[cardFireball].Spell
	sp.FlightMs = fireballFlightMs
	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	hp0 := tower.hp

	if err := b.PlayCard(TeamBlue, cardFireball, tower.xMilli, tower.yMilli); err != nil {
		t.Fatalf("PlayCard: %v", err)
	}
	if tower.hp != hp0 {
		t.Fatalf("出牌当 tick 塔就掉了 %d 血（应为 0：弹体还在飞）", hp0-tower.hp)
	}

	dueTicks := int(TicksForMs(sp.FlightMs))
	b.Step()
	if tower.hp != hp0 {
		t.Fatalf("第 1 tick（server_ms=%d）塔就掉了 %d 血，应为 0", b.ServerMs(), hp0-tower.hp)
	}
	for int(b.tick) < dueTicks-1 {
		b.Step()
	}
	if tower.hp != hp0 {
		t.Fatalf("飞行段最后一 tick 之前（server_ms=%d / 到期 %d ms）塔掉了 %d 血",
			b.ServerMs(), dueTicks*MSecPerTick, hp0-tower.hp)
	}

	b.Step()
	if got := hp0 - tower.hp; got != sp.CrownDamage {
		t.Fatalf("到达那一 tick（server_ms=%d）塔掉了 %d 血，应为 crown_damage=%d",
			b.ServerMs(), got, sp.CrownDamage)
	}
	b.Step()
	if got := hp0 - tower.hp; got != sp.CrownDamage {
		t.Fatalf("到达后又掉了一次：塔共掉 %d 血，应为 %d（一发只结算一次）", got, sp.CrownDamage)
	}
	t.Logf("flight_ms=%d（%d tick）：出牌 tick 掉 0 → %d ms 掉 %d",
		sp.FlightMs, dueTicks, b.ServerMs()-MSecPerTick, sp.CrownDamage)
}

// TestSpellWithoutFlightLandsImmediately pin `flight_ms = 0` 的法术行为不变：
// 出牌当 tick 就结算（没有弹体可等）。
func TestSpellWithoutFlightLandsImmediately(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, fullDeck, fullDeck, 512)
	sp := tb.cards[cardFireball].Spell
	if sp.FlightMs != 0 {
		t.Fatalf("fixture: 飞行段应为 0，实际 %d", sp.FlightMs)
	}
	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	hp0 := tower.hp
	b.castSpell(TeamBlue, tb.cards[cardFireball], tower.xMilli, tower.yMilli)
	if got := hp0 - tower.hp; got != sp.CrownDamage {
		t.Fatalf("flight_ms=0 的法术出牌当 tick 应掉 %d 血，实际 %d", sp.CrownDamage, got)
	}
}

// TestSpellFlightLocksLandingPoint pin 落点与半径取**出牌时**的值：
// 到期结算按那时的站位逐个判定 —— 走出圈的躲掉、走进圈的照样吃（原版法术锁定落点，
// ⛔ 不追人）。
func TestSpellFlightLocksLandingPoint(t *testing.T) {
	tb := newTestTable()
	b := mustBattleWith(t, tb, fullDeck, fullDeck, 513)

	// 木桩：不动、不还手、血厚，且放在全场塔都打不到的中路河口
	//（最近的塔也超过 9.6 格，塔的攻击距离 7.5 格）。
	dummy := &UnitDef{
		ID: 901, Key: "Dummy", NameCN: "木桩", Kind: KindTroop,
		HP: 100000, Damage: 0, SpeedMilliPerSec: 0, RadiusMilli: 500, Mass: 4,
	}
	tb.units["Dummy"] = dummy
	const cx, cy = int32(9000), int32(16000)
	stay := b.spawnUnit(TeamRed, dummy, 0, cx, cy-600, 0)
	leave := b.spawnUnit(TeamRed, dummy, 0, cx-700, cy, 0)

	card := &CardDef{ID: 77, Key: "fireball-flight", Kind: CardTypeSpell, Elixir: 4,
		Spell: &SpellDef{ID: 77, Key: "fireball-flight", RadiusMilli: 2500,
			InstantDamage: 688, CrownDamage: 207, FlightMs: fireballFlightMs,
			Anywhere: true, OnWater: true}}
	b.castSpell(TeamBlue, card, cx, cy)

	// 出牌之后：一只走出圈（3500 > 半径 2500 + 命中盒 500），一只走进圈（圈心）。
	leave.setPos(cx+3500, cy)
	walkIn := b.spawnUnit(TeamRed, dummy, 0, cx, cy, 0)

	runSteps(b, int(TicksForMs(fireballFlightMs)))

	if got := stay.maxHP - stay.hp; got != card.Spell.InstantDamage {
		t.Fatalf("留在圈里的木桩掉了 %d 血，应为 instant_damage=%d", got, card.Spell.InstantDamage)
	}
	if walkIn.hp != walkIn.maxHP-card.Spell.InstantDamage {
		t.Fatalf("到达时走进圈的木桩掉了 %d 血，应为 %d（结算读的是到期时的站位）",
			walkIn.maxHP-walkIn.hp, card.Spell.InstantDamage)
	}
	if leave.hp != leave.maxHP {
		t.Fatalf("走出圈的木桩掉了 %d 血：落点/半径没锁在出牌时", leave.maxHP-leave.hp)
	}
	t.Logf("到期结算：圈内 -%d / 走进圈 -%d / 走出圈 -%d",
		stay.maxHP-stay.hp, walkIn.maxHP-walkIn.hp, leave.maxHP-leave.hp)
}
