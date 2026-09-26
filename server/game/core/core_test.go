package core

import (
	"errors"
	"testing"
)

// TestArenaGeometry pins the arena's fixed geometry: river band, bridge
// centres, the six tower positions and the king towers' 3x3 blocked footprint.
//
// 判据出处: 参考规格 §2（场地 18x32 / 河 y∈[15,17) / 桥心 x=3.5,14.5 各宽 2 格 /
// 国王塔 3x3 占地 / 公主塔 (3.5,6.5)(14.5,6.5) 与镜像）.
func TestArenaGeometry(t *testing.T) {
	a := NewArena()

	if a.Width() != 18000 || a.Height() != 32000 {
		t.Fatalf("arena size = %dx%d, want 18000x32000 (18x32 tiles)", a.Width(), a.Height())
	}
	top, bottom := a.RiverBand()
	if top != 15000 || bottom != 17000 {
		t.Fatalf("river band = [%d,%d), want [15000,17000)", top, bottom)
	}
	// Bridge centres: 3.5 and 14.5 tiles; a point equidistant picks the left one.
	for _, c := range []struct{ x, want int32 }{{3000, 3500}, {3500, 3500}, {9000, 3500}, {14000, 14500}, {14500, 14500}, {17999, 14500}} {
		if got := a.NearestBridgeX(c.x); got != c.want {
			t.Fatalf("NearestBridgeX(%d) = %d, want %d", c.x, got, c.want)
		}
	}
	// The bridge spans (centre +- 1 tile) are not water; the rest of the band is.
	for _, c := range []struct {
		x, y int32
		want bool
	}{
		{3500, 16000, false}, {2500, 16000, false}, {2499, 16000, true}, {4500, 16000, true},
		{14500, 16000, false}, {13500, 16000, false}, {15500, 16000, true},
		{9000, 16000, true}, {9000, 15000, true}, {9000, 16999, true},
		{9000, 17000, false}, {9000, 14999, false},
	} {
		if got := a.IsWater(c.x, c.y); got != c.want {
			t.Fatalf("IsWater(%d,%d) = %v, want %v", c.x, c.y, got, c.want)
		}
	}
	// Four princess towers + two kings, mirroring y -> 32 - y.
	got := [6][2]int32{BlueKingTowerPos, BluePrincessLeftPos, BluePrincessRightPos, RedKingTowerPos, RedPrincessLeftPos, RedPrincessRightPos}
	want := [6][2]int32{{9000, 3000}, {3500, 6500}, {14500, 6500}, {9000, 29000}, {3500, 25500}, {14500, 25500}}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("tower slot %d = %v, want %v", i, got[i], want[i])
		}
	}
	for i := 0; i < 3; i++ {
		if got[i+3][0] != got[i][0] || got[i+3][1] != ArenaHMilli-got[i][1] {
			t.Fatalf("red tower %d is not the y->32-y mirror of blue tower %d: %v vs %v", i, i, got[i+3], got[i])
		}
	}
	// King tower footprint is a 3x3 blocked square centred on the tower.
	for _, c := range []struct {
		x, y int32
		want bool
	}{
		{9000, 3000, true}, {7500, 1500, true}, {10499, 4499, true},
		{10500, 3000, false}, {9000, 4500, false}, {7400, 3000, false}, {9000, 1400, false},
		{9000, 29000, true}, {9000, 27500, true}, {9000, 30500, false},
		{3500, 6500, false}, // princess towers have no blocked footprint
	} {
		if got := a.IsBlocked(c.x, c.y); got != c.want {
			t.Fatalf("IsBlocked(%d,%d) = %v, want %v", c.x, c.y, got, c.want)
		}
	}
	// Walkability: flying ignores terrain, ground does not.
	if !a.IsWalkable(9000, 16000, true) {
		t.Fatal("flying unit must be able to occupy the river")
	}
	if a.IsWalkable(9000, 16000, false) {
		t.Fatal("ground unit must not be able to occupy the river")
	}
	if a.IsWalkable(9000, 3000, false) {
		t.Fatal("ground unit must not walk into the king tower footprint")
	}
	if a.IsWalkable(18000, 1000, true) || a.IsWalkable(0, 32000, true) {
		t.Fatal("nothing may leave the arena, flying included")
	}
}

// TestCanDeployBase pins the base deploy rules: own half only, river never, the
// king's footprint never, and spells exempt.
//
// 判据出处: 参考规格 §2.1（BLUE y∈[0,15) / RED y∈[17,32) / 河面仅法术 / BLOCKED 格非法）.
func TestCanDeployBase(t *testing.T) {
	a := NewArena()
	none := []TowerRef{}

	if !a.CanDeploy(TeamBlue, 9000, 5000, false, false, none) {
		t.Fatal("BLUE at (9,5) must be legal")
	}
	if a.CanDeploy(TeamBlue, 9000, 20000, false, false, none) {
		t.Fatal("BLUE at (9,20) (enemy half) must be illegal")
	}
	if a.CanDeploy(TeamBlue, 9000, 16000, false, false, none) {
		t.Fatal("BLUE on the river must be illegal")
	}
	if a.CanDeploy(TeamBlue, 9000, 3000, false, false, none) {
		t.Fatal("BLUE inside the king tower footprint must be illegal")
	}
	if !a.CanDeploy(TeamBlue, 9000, 14999, false, false, none) {
		t.Fatal("BLUE at the river's near bank (y=14999) must be legal")
	}
	if a.CanDeploy(TeamBlue, 9000, 15000, false, false, none) {
		t.Fatal("BLUE at y=15000 is in the river and must be illegal")
	}
	if a.CanDeploy(TeamBlue, 3500, 16000, false, false, none) {
		t.Fatal("BLUE standing on the bridge is still in the river band and must be illegal")
	}
	if !a.CanDeploy(TeamRed, 9000, 20000, false, false, none) {
		t.Fatal("RED at (9,20) must be legal")
	}
	if a.CanDeploy(TeamRed, 9000, 5000, false, false, none) {
		t.Fatal("RED at (9,5) must be illegal")
	}
	if !a.CanDeploy(TeamRed, 9000, 17000, false, false, none) {
		t.Fatal("RED at the river's far bank (y=17000) must be legal")
	}
	// Out of bounds.
	if a.CanDeploy(TeamBlue, 18000, 5000, false, false, none) || a.CanDeploy(TeamBlue, -1, 5000, false, false, none) {
		t.Fatal("out-of-bounds points must be illegal")
	}
	// Spells: anywhere on the enemy half, and over the river.
	if !a.CanDeploy(TeamBlue, 9000, 20000, true, false, none) {
		t.Fatal("a spell must be castable on the enemy half")
	}
	if !a.CanDeploy(TeamBlue, 9000, 16000, true, true, none) {
		t.Fatal("a spell must be castable on the river")
	}
	if !a.CanDeploy(TeamBlue, 3500, 25500, true, true, none) {
		t.Fatal("a spell must be castable on an enemy tower's tile")
	}
	// 国王塔的阻塞格（IsBlocked 覆盖的 3x3）对法术同样开放：法术点是"场内任意点"，
	// 只有 InBounds 在它前面。参考实现把 _BLOCKED 判在 anywhere 之前（arena.py:322-331），
	// 本工程有意偏离（见 CanDeploy 的注释）。
	if !a.CanDeploy(TeamBlue, 9000, 29000, true, false, none) {
		t.Fatal("a spell must be castable on the enemy king tower's own tile")
	}
	if !a.CanDeploy(TeamRed, 9000, 3000, true, false, none) {
		t.Fatal("a spell must be castable on the enemy king tower's own tile (RED side)")
	}
	if a.CanDeploy(TeamBlue, 18000, 29000, true, false, none) {
		t.Fatal("out of bounds stays illegal for spells too")
	}
}

// TestCanDeployPocketAfterPrincessFalls pins the deploy pocket: destroying an
// enemy princess tower expands the deploy zone in **that lane only**, up to but
// not including the row the tower stood on.
//
// 判据出处: 参考规格 §2.1（摧毁敌方公主塔 ⇒ 该车道扩展至该塔所在行，不含该行）.
func TestCanDeployPocketAfterPrincessFalls(t *testing.T) {
	a := NewArena()

	// BLUE's view: the RED left princess tower (x = 3.5, y = 25.5) has fallen.
	fallenLeft := []TowerRef{{Kind: TowerKindPrincess, XMilli: 3500, YMilli: 25500}}
	if !a.CanDeploy(TeamBlue, 3500, 25000, false, false, fallenLeft) {
		t.Fatal("left lane must open up to (but not including) the fallen tower's row")
	}
	if a.CanDeploy(TeamBlue, 3500, 25500, false, false, fallenLeft) {
		t.Fatal("the fallen tower's own row must stay excluded")
	}
	if !a.CanDeploy(TeamBlue, 3500, 16000, false, false, fallenLeft) {
		t.Fatal("the bridge is now inside the expanded left lane and must be legal")
	}
	if a.CanDeploy(TeamBlue, 14500, 25000, false, false, fallenLeft) {
		t.Fatal("the right lane must be unaffected by the left tower's fall")
	}
	// A point whose *nearest* bridge is the left one belongs to the left lane,
	// so it opens with it even though it sits between the two towers.
	if !a.CanDeploy(TeamBlue, 9000, 25000, false, false, fallenLeft) {
		t.Fatal("the left lane is bounded by the nearest bridge, so x=9.0 must open too")
	}
	if a.CanDeploy(TeamBlue, 3500, 25000, false, false, nil) {
		t.Fatal("without a fallen tower the enemy half must stay closed")
	}

	// RED's view: the BLUE right princess tower (x = 14.5, y = 6.5) has fallen.
	fallenRight := []TowerRef{{Kind: TowerKindPrincess, XMilli: 14500, YMilli: 6500}}
	if !a.CanDeploy(TeamRed, 14500, 7000, false, false, fallenRight) {
		t.Fatal("red must be able to deploy just past the fallen blue right tower")
	}
	if a.CanDeploy(TeamRed, 14500, 6500, false, false, fallenRight) {
		t.Fatal("red must not be able to deploy on the fallen tower's own row")
	}
	if a.CanDeploy(TeamRed, 3500, 7000, false, false, fallenRight) {
		t.Fatal("red's left lane must be unaffected")
	}
	// A king tower never opens a pocket: its fall ends the match.
	fallenKing := []TowerRef{{Kind: TowerKindKing, XMilli: 3500, YMilli: 25500}}
	if a.CanDeploy(TeamBlue, 3500, 25000, false, false, fallenKing) {
		t.Fatal("a fallen king tower must not open a deploy pocket")
	}
}

// TestElixirThreePhases drives the elixir bar across the full 300 s timeline.
//
// The bar is drained every tick so it never reaches the 10-elixir cap, which is
// what makes the *rate* observable. The three cumulative totals are exact
// integer consequences of the timeline: 2400 ticks at 2800 ms/point, 2400 at
// 1400, 1200 at 930.
//
// 判据出处: 参考规格 §3（0–120s 2800ms / 120–240s 1400ms / 240–300s 930ms）.
func TestElixirThreePhases(t *testing.T) {
	for _, c := range []struct {
		ms   int32
		want int
	}{{0, 0}, {119950, 0}, {120000, 1}, {239950, 1}, {240000, 2}, {299950, 2}} {
		if got := ElixirPhaseAt(c.ms); got != c.want {
			t.Fatalf("ElixirPhaseAt(%d) = %d, want %d", c.ms, got, c.want)
		}
	}
	if ElixirMsPerUnitAt(119950) != 2800 || ElixirMsPerUnitAt(120000) != 1400 || ElixirMsPerUnitAt(240000) != 930 {
		t.Fatalf("ms per unit = %d/%d/%d in the three phases, want 2800/1400/930",
			ElixirMsPerUnitAt(119950), ElixirMsPerUnitAt(120000), ElixirMsPerUnitAt(240000))
	}

	bar := newElixirBar(0)
	var drained int32
	var at120, at240, at300 int32
	for i := int32(0); i < 6000; i++ {
		bar.regenerate(i * MSecPerTick)
		for bar.amountMilli >= MilliTilePerTile {
			bar.amountMilli -= MilliTilePerTile
			drained += MilliTilePerTile
		}
		switch i {
		case 2399:
			at120 = drained + bar.amountMilli
		case 4799:
			at240 = drained + bar.amountMilli
		case 5999:
			at300 = drained + bar.amountMilli
		}
	}
	t.Logf("elixir generated (milli): at 120s=%d (%d ms/point), at 240s=%d, at 300s=%d",
		at120, msPerPoint(120000, at120), at240, at300)
	if at120 != 42857 {
		t.Fatalf("elixir after 120 s = %d, want 42857 (2400 ticks at 2800 ms/point)", at120)
	}
	if at240 != 128571 {
		t.Fatalf("elixir after 240 s = %d, want 128571 (42857 + 2400 ticks at 1400 ms/point)", at240)
	}
	if at300 != 193087 {
		t.Fatalf("elixir after 300 s = %d, want 193087 (128571 + 1200 ticks at 930 ms/point)", at300)
	}
	if got := msPerPoint(120000, at120); got != 2800 {
		t.Fatalf("derived first-phase rate = %d ms/point, want 2800", got)
	}
	if got := msPerPoint(120000, at240-at120); got != 1400 {
		t.Fatalf("derived second-phase rate = %d ms/point, want 1400", got)
	}
	if got := msPerPoint(60000, at300-at240); got != 930 {
		t.Fatalf("derived third-phase rate = %d ms/point, want 930", got)
	}
}

// msPerPoint derives ms-per-elixir from a window's elapsed ms and milli-elixir.
func msPerPoint(elapsedMs, milli int32) int32 {
	if milli <= 0 {
		return 0
	}
	return elapsedMs * 1000 / milli
}

// TestHandCycle pins the 8-card rotation: 4 in hand, 1 next, the played card
// goes to the back of the queue.
//
// 判据出处: 参考规格 §7 S11（8 卡组 / 4 手牌 / 下一张 / 循环）.
func TestHandCycle(t *testing.T) {
	b := mustBattle(t, fullDeck, fullDeck, 20240920)

	hand0 := b.Hand(TeamBlue)
	if len(hand0) != 4 {
		t.Fatalf("opening hand has %d cards, want 4", len(hand0))
	}
	next0 := b.Next(TeamBlue)
	if next0 == 0 {
		t.Fatal("there must be a next card")
	}
	seen := map[int32]int{}
	for _, c := range hand0 {
		seen[c]++
		if seen[c] > 1 {
			t.Fatalf("card %d appears twice in the opening hand", c)
		}
		if c == next0 {
			t.Fatalf("the next card %d must not already be in hand", next0)
		}
	}

	// q holds the draw order as it is revealed: q[0] is the opening "next".
	q := []int32{next0}
	for i := 0; i < 4; i++ {
		setElixir(b, TeamBlue, MaxElixirMilli)
		if err := b.PlayCard(TeamBlue, hand0[i], 9000, 9000); err != nil {
			t.Fatalf("play #%d (card %d): %v", i, hand0[i], err)
		}
		got := b.Hand(TeamBlue)
		for j := 0; j <= i; j++ {
			if got[j] != q[j] {
				t.Fatalf("after play #%d, hand[%d] = %d, want %d (hand=%v)", i, j, got[j], q[j], got)
			}
		}
		for j := i + 1; j < 4; j++ {
			if got[j] != hand0[j] {
				t.Fatalf("after play #%d, untouched slot %d changed: %d, want %d", i, j, got[j], hand0[j])
			}
		}
		if i < 3 {
			q = append(q, b.Next(TeamBlue))
		} else if n := b.Next(TeamBlue); n != hand0[0] {
			t.Fatalf("after a full hand cycle the next card = %d, want the first card played (%d)", n, hand0[0])
		}
	}

	// Full cycle: the hand is now exactly the four cards that were queued.
	cycled := b.Hand(TeamBlue)
	for j := 0; j < 4; j++ {
		if cycled[j] != q[j] {
			t.Fatalf("after a full cycle hand[%d] = %d, want %d", j, cycled[j], q[j])
		}
	}
	// And four more plays bring the original hand back in the original order.
	for i := 0; i < 4; i++ {
		setElixir(b, TeamBlue, MaxElixirMilli)
		if err := b.PlayCard(TeamBlue, cycled[i], 9000, 9000); err != nil {
			t.Fatalf("second-cycle play #%d (card %d): %v", i, cycled[i], err)
		}
	}
	back := b.Hand(TeamBlue)
	for j := 0; j < 4; j++ {
		if back[j] != hand0[j] {
			t.Fatalf("after two cycles hand[%d] = %d, want the original %d", j, back[j], hand0[j])
		}
	}
	if n := b.Next(TeamBlue); n != q[0] {
		t.Fatalf("after two cycles next = %d, want %d", n, q[0])
	}
}

// TestPlayCardRejectedWhenElixirShort pins the affordability gate: nothing is
// spent and nothing is deployed when the elixir is short.
func TestPlayCardRejectedWhenElixirShort(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 5)
	cardID := b.Hand(TeamBlue)[0]
	card, _ := b.cfg.Table.Card(cardID)

	setElixir(b, TeamBlue, (card.Elixir-1)*MilliTilePerTile)
	err := b.PlayCard(TeamBlue, cardID, 9000, 9000)
	if !errors.Is(err, ErrNotEnoughElixir) {
		t.Fatalf("err = %v, want ErrNotEnoughElixir", err)
	}
	if got := b.Elixir(TeamBlue); got != (card.Elixir-1)*MilliTilePerTile {
		t.Fatalf("elixir changed on a rejected play: %d", got)
	}
	if len(b.units) != 0 {
		t.Fatalf("a rejected play deployed %d units", len(b.units))
	}
	if !b.hands[TeamBlue].Has(cardID) {
		t.Fatal("a rejected play consumed the card")
	}

	// With exactly enough elixir the same play goes through.
	setElixir(b, TeamBlue, card.Elixir*MilliTilePerTile)
	if err := b.PlayCard(TeamBlue, cardID, 9000, 9000); err != nil {
		t.Fatalf("play with exact elixir: %v", err)
	}
	if got := b.Elixir(TeamBlue); got != 0 {
		t.Fatalf("elixir after playing a %d-cost card = %d, want 0", card.Elixir, got)
	}
	if len(b.units) != 1 {
		t.Fatalf("units deployed = %d, want 1", len(b.units))
	}
}

// TestPlayCardRejectedOutsideDeployZone pins the placement gate for troops and
// the exemption for spells.
//
// 判据出处: 参考规格 §2.1.
func TestPlayCardRejectedOutsideDeployZone(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 11)
	cardID := b.Hand(TeamBlue)[0]
	card, _ := b.cfg.Table.Card(cardID)
	if card.Kind != CardTypeTroop {
		t.Fatalf("fixture error: hand card %d is not a troop", cardID)
	}
	setElixir(b, TeamBlue, MaxElixirMilli)

	for _, c := range []struct {
		x, y int32
		why  string
	}{
		{9000, 20000, "enemy half"},
		{9000, 16000, "river"},
		{9000, 3000, "king tower footprint"},
		{9000, 32000, "out of bounds"},
	} {
		err := b.PlayCard(TeamBlue, cardID, c.x, c.y)
		if !errors.Is(err, ErrOutsideDeployZone) {
			t.Fatalf("deploy at (%d,%d) [%s]: err = %v, want ErrOutsideDeployZone", c.x, c.y, c.why, err)
		}
	}
	if got := b.Elixir(TeamBlue); got != MaxElixirMilli {
		t.Fatalf("elixir changed on rejected deploys: %d", got)
	}
	if len(b.units) != 0 {
		t.Fatalf("rejected deploys spawned %d units", len(b.units))
	}
	if err := b.PlayCard(TeamBlue, cardID, 9000, 5000); err != nil {
		t.Fatalf("legal deploy at (9,5): %v", err)
	}
	if len(b.units) != 1 {
		t.Fatalf("units after a legal deploy = %d, want 1", len(b.units))
	}

	// A spell is exempt: it may be cast on the enemy half and over the river.
	spellDeck := []int32{cardFireball, cardFireball, cardFireball, cardFireball, cardFireball, cardFireball, cardFireball, cardFireball}
	sb := mustBattle(t, spellDeck, spellDeck, 12)
	setElixir(sb, TeamBlue, MaxElixirMilli)
	if err := sb.PlayCard(TeamBlue, cardFireball, 9000, 20000); err != nil {
		t.Fatalf("spell on the enemy half: %v", err)
	}
	setElixir(sb, TeamBlue, MaxElixirMilli)
	if err := sb.PlayCard(TeamBlue, cardFireball, 9000, 16000); err != nil {
		t.Fatalf("spell on the river: %v", err)
	}
}
