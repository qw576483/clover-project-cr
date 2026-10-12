package core

import "testing"

// TestNextCardCooldown 断言「下一张」的揭晓延迟（参考规格 §3 隐藏手牌（下一张）冷却）：
// 出牌后槽位是空的、队头不动，冷却到点才把队头换进槽位；出的那张牌立刻回到队尾
// （8 卡循环本身不变）。
//
// 判据出处: 官方 battle_timelines.json Default.next_spell_cooldown_ms = [1000,500,350]；
// 冷却长度 = core.NextSpellCooldownMs。
func TestNextCardCooldown(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 20261001)
	hand0 := b.Hand(TeamBlue)
	next0 := b.Next(TeamBlue)
	if next0 == 0 {
		t.Fatal("fixture: 开局没有「下一张」")
	}

	const slot = 1
	played := hand0[slot]
	setElixir(b, TeamBlue, MaxElixirMilli)
	if err := b.PlayCard(TeamBlue, played, 9000, 9000); err != nil {
		t.Fatalf("出牌（卡 %d）: %v", played, err)
	}

	// 出牌当拍：槽位隐藏、队头不变、出的那张牌已在队尾。
	if got := b.Hand(TeamBlue)[slot]; got != 0 {
		t.Fatalf("出牌后槽位 = %d，要 0（冷却期间是隐藏手牌）", got)
	}
	if n := b.Next(TeamBlue); n != next0 {
		t.Fatalf("冷却期间「下一张」= %d，要 %d（到点才抽）", n, next0)
	}
	if q := b.hands[TeamBlue].queue; q[len(q)-1] != played {
		t.Fatalf("出的牌 %d 没进队尾（队尾 = %d）", played, q[len(q)-1])
	}

	cdTicks := int(NextSpellCooldownMs[0] / MSecPerTick)
	if cdTicks <= 0 {
		t.Fatalf("fixture: 一段冷却 %d ms 不足一个 tick", NextSpellCooldownMs[0])
	}
	for i := 1; i < cdTicks; i++ {
		b.Step()
		if got := b.Hand(TeamBlue)[slot]; got != 0 {
			t.Fatalf("serverMs=%d（冷却 %d ms 未到）槽位已揭晓成 %d", b.ServerMs(), NextSpellCooldownMs[0], got)
		}
		if n := b.Next(TeamBlue); n != next0 {
			t.Fatalf("serverMs=%d（冷却未到）「下一张」变成 %d，要仍是 %d", b.ServerMs(), n, next0)
		}
	}
	b.Step() // serverMs == 1000：到点
	if got := b.Hand(TeamBlue)[slot]; got != next0 {
		t.Fatalf("冷却到点（serverMs=%d）槽位 = %d，要换成队头 %d", b.ServerMs(), got, next0)
	}
	if n := b.Next(TeamBlue); n == next0 {
		t.Fatalf("到点后「下一张」应换新，仍是 %d", n)
	}
	hand := b.Hand(TeamBlue)
	for _, c := range hand {
		if c == 0 {
			t.Fatalf("到点后手牌仍有空槽（%v）", hand)
		}
	}
	t.Logf("冷却=%d ms：出牌后槽位=%d（隐藏）/「下一张」=%d；到点后槽位=%d/「下一张」=%d，手牌=%v",
		NextSpellCooldownMs[0], 0, next0, hand[slot], b.Next(TeamBlue), hand)
}

// TestNextCardCooldownFollowsElixirSegment 断言冷却长度跟**当前圣水段**走：
// 2 倍段（120 s 起）是 500 ms，而不是 1 倍段的 1000 ms。
func TestNextCardCooldownFollowsElixirSegment(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 20261002)
	// 把对局钟拨到 2 倍段（120 s <= t < 240 s），不动别的状态。
	b.tick = 130000 / MSecPerTick
	b.serverMs = b.tick * MSecPerTick
	if phase := ElixirPhaseAt(b.ServerMs()); phase != 1 {
		t.Fatalf("fixture: serverMs=%d 落在圣水段 %d，要 1（2 倍段）", b.ServerMs(), phase)
	}
	cd := NextSpellCooldownMsAt(b.ServerMs())
	if cd != NextSpellCooldownMs[1] {
		t.Fatalf("2 倍段冷却 = %d ms，要 %d", cd, NextSpellCooldownMs[1])
	}

	const slot = 0
	setElixir(b, TeamBlue, MaxElixirMilli)
	card := b.Hand(TeamBlue)[slot]
	next0 := b.Next(TeamBlue)
	if err := b.PlayCard(TeamBlue, card, 9000, 9000); err != nil {
		t.Fatalf("出牌（卡 %d）: %v", card, err)
	}
	cdTicks := int(cd / MSecPerTick)
	for i := 1; i < cdTicks; i++ {
		b.Step()
		if got := b.Hand(TeamBlue)[slot]; got != 0 {
			t.Fatalf("2 倍段 serverMs=%d 槽位提前揭晓成 %d", b.ServerMs(), got)
		}
	}
	b.Step()
	if got := b.Hand(TeamBlue)[slot]; got != next0 {
		t.Fatalf("2 倍段冷却到点（serverMs=%d）槽位 = %d，要 %d", b.ServerMs(), got, next0)
	}
	t.Logf("圣水段 %d：冷却 %d ms = %d tick，到点槽位 %d -> %d",
		ElixirPhaseAt(b.ServerMs()), cd, cdTicks, 0, next0)
}
