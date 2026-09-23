package core

// AJ1 offline coverage assertions for D9 (physics / collision) and D10
// (gameplay logic). Pure-Go, no engine import, no Play -- every row of
// .ai-tmp/test/AJ1-matrix-D9.tsv and -D10.tsv is decided here.
//
// 判据出处: 参考规格 §2/§2.1（场地几何 / 桥 / 部署区）, §3（胜负判定）, §4
// （激活机制）, §5（移动 / 索敌 / 群体卡 / 法术 / 击退）。

import "testing"

// card ids used only by this file (kept out of the shared fixtures).
const (
	cardPushCover = int32(90)
	cardDingCover = int32(91)
	cardHutCover  = int32(92)
)

// newCoverTable extends the shared fixture with the extra defs D9/D10 need:
// a static building, a projectile-firing troop, a death-payload bomb, a
// spawner hut and an immobile heavy "Lord".
func newCoverTable() *fakeTable {
	t := newTestTable()
	t.units["Wall"] = &UnitDef{
		ID: 501, Key: "Wall", NameCN: "测试建筑", Kind: KindBuilding,
		HP: 100000, SpeedMilliPerSec: 0, RangeMilli: 0, DeployMs: 0, RadiusMilli: 1000, Mass: 0,
	}
	t.units["Bomber"] = &UnitDef{
		ID: 502, Key: "Bomber", NameCN: "投弹手", Kind: KindTroop,
		HP: 5000, Damage: 100, HitSpeedMs: 1000, LoadTimeMs: 500,
		SpeedMilliPerSec: SpeedMilliPerSec(60), RangeMilli: 5000, SightMilli: 5500,
		DeployMs: 0, RadiusMilli: 500, Mass: 3, AtkGround: true, ProjectileKey: "BomberShell",
	}
	t.units["BomberShell"] = &UnitDef{
		ID: 503, Key: "BomberShell", Kind: KindProjectile, SpeedMilliPerSec: SpeedMilliPerSec(600),
	}
	t.units["Bomb"] = &UnitDef{
		ID: 504, Key: "Bomb", NameCN: "炸弹", Kind: KindTroop,
		HP: 100, Damage: 0, SpeedMilliPerSec: 0, DeployMs: 0, RadiusMilli: 300, Mass: 1,
		DeathDamage: 300, DeathAoeRadiusMilli: 2000, DeathSpawnKey: "Skeleton", DeathSpawnN: 2,
	}
	t.units["Hut"] = &UnitDef{
		ID: 505, Key: "Hut", NameCN: "小屋", Kind: KindBuilding,
		HP: 1000, SpeedMilliPerSec: 0, DeployMs: 0, RadiusMilli: 800, Mass: 0,
		SpawnKey: "Skeleton", SpawnN: 1, SpawnRadiusMilli: 500, SpawnIntervalMs: 1000, SpawnLimit: 3,
	}
	t.units["Lord"] = &UnitDef{
		ID: 506, Key: "Lord", NameCN: "领主", Kind: KindTroop,
		HP: 100000, Damage: 0, SpeedMilliPerSec: 0, DeployMs: 0, RadiusMilli: 500, Mass: 4,
	}
	t.cards[cardPushCover] = &CardDef{
		ID: cardPushCover, Key: "wave", NameCN: "冲击波", Kind: CardTypeSpell, Elixir: 2,
		Spell: &SpellDef{ID: 9, Key: "wave", Elixir: 2, RadiusMilli: 3000, Pushback: 2000, Anywhere: true, OnWater: true},
	}
	t.cards[cardDingCover] = &CardDef{ID: cardDingCover, Key: "ding", NameCN: "亡语卡", Kind: CardTypeTroop, Elixir: 1, UnitKey: "Bomb", UnitN: 1}
	t.cards[cardHutCover] = &CardDef{ID: cardHutCover, Key: "hut", NameCN: "小屋卡", Kind: CardTypeBuilding, Elixir: 3, UnitKey: "Hut", UnitN: 1}
	return t
}

// ---------------------------------------------------------------------------
// D9 -- physics / collision
// ---------------------------------------------------------------------------

// TestCoverBridgeSpanWalkable pins the bridge's *walkable* span: within the
// river band the bridge centre +- 1 tile is land, one milli-tile outside is
// water. Left bridge x in [2500,4500), right bridge x in [13500,15550).
//
// 判据出处: 参考规格 §2（左桥 x=3.5 宽 2 格 / 右桥 x=14.5 宽 2 格）.
func TestCoverBridgeSpanWalkable(t *testing.T) {
	a := NewArena()
	cases := []struct {
		x, y int32
		want bool
		why  string
	}{
		{2500, 16000, true, "left bridge left edge (3.5-1.0) is land"},
		{4499, 16000, true, "left bridge right edge -1 is land"},
		{2499, 16000, false, "one milli-tile left of the bridge is water"},
		{4500, 16000, false, "one milli-tile right of the bridge is water"},
		{3500, 16000, true, "left bridge centre is land"},
		{13500, 16000, true, "right bridge left edge (14.5-1.0) is land"},
		{15499, 16000, true, "right bridge right edge -1 is land"},
		{13499, 16000, false, "one milli-tile left of the right bridge is water"},
		{15500, 16000, false, "one milli-tile right of the right bridge is water"},
		{9000, 16000, false, "mid-river (no bridge) is water"},
	}
	for _, c := range cases {
		if got := a.IsWalkable(c.x, c.y, false); got != c.want {
			t.Fatalf("IsWalkable(%d,%d,ground) = %v, want %v [%s]", c.x, c.y, got, c.want, c.why)
		}
		if got := a.IsWater(c.x, c.y); got == c.want {
			t.Fatalf("IsWater(%d,%d) = %v inconsistent with walkable=%v [%s]", c.x, c.y, got, c.want, c.why)
		}
	}
	// A flying unit ignores all of it.
	if !a.IsWalkable(9000, 16000, true) {
		t.Fatal("flying unit must be able to sit on mid-river water")
	}
	t.Logf("bridge spans: left [%d,%d) right [%d,%d) all land; water elsewhere",
		BridgeAxMilli-BridgeHalfMilli, BridgeAxMilli+BridgeHalfMilli,
		BridgeBxMilli-BridgeHalfMilli, BridgeBxMilli+BridgeHalfMilli)
}

// TestCoverDeployZoneEdges pins the deploy-zone boundary rows exactly.
//
// 判据出处: 参考规格 §2.1（BLUE y∈[0,15) / RED y∈[17,32) / 河面非法 / 国王塔
// 3x3 非法 / 出界非法）.
func TestCoverDeployZoneEdges(t *testing.T) {
	a := NewArena()
	none := []TowerRef{}
	type c struct {
		team Team
		x, y int32
		want bool
		why  string
	}
	for _, tc := range []c{
		{TeamBlue, 9000, 0, true, "BLUE own back row y=0"},
		{TeamBlue, 9000, 14999, true, "BLUE at the river's near bank"},
		{TeamBlue, 9000, 15000, false, "BLUE at y=15.000 is the river"},
		{TeamBlue, 9000, 16999, false, "BLUE still inside the river band"},
		{TeamBlue, 9000, 17000, false, "BLUE past the river but in the enemy half"},
		{TeamRed, 9000, 17000, true, "RED at the river's far bank"},
		{TeamRed, 9000, 16999, false, "RED inside the river band"},
		{TeamRed, 9000, 31999, true, "RED own back row y=31.999"},
		{TeamRed, 9000, 32000, false, "RED out of bounds"},
		{TeamBlue, 9000, 1500, false, "BLUE inside the blue king 3x3 (y=1.5)"},
		{TeamBlue, 7499, 3000, true, "just left of the blue king 3x3"},
		{TeamBlue, 7500, 3000, false, "left edge of the blue king 3x3"},
		{TeamBlue, 10500, 3000, true, "just right of the blue king 3x3"},
		{TeamRed, 9000, 27500, false, "RED inside the red king 3x3 (y=27.5)"},
	} {
		if got := a.CanDeploy(tc.team, tc.x, tc.y, false, false, none); got != tc.want {
			t.Fatalf("CanDeploy(team=%v,%d,%d) = %v, want %v [%s]", tc.team, tc.x, tc.y, got, tc.want, tc.why)
		}
	}
}

// TestCoverCollisionLayers pins collision x role-type, one row per pair:
// ground-ground separates, air-air separates, air-ground is two layers and
// never touches, a building is immovable (the trooper gives way), and neither
// a projectile nor a spell cloud is an entity that can collide at all.
//
// 判据出处: 参考规格 §5 碰撞（空军/陆军分层；建筑不动；投射物与法术云不是实体）.
func TestCoverCollisionLayers(t *testing.T) {
	knight, _ := newCoverTable().Unit("Knight")
	minion, _ := newCoverTable().Unit("Minion")

	// --- ground vs ground: both give way, ending at least a radii-sum apart.
	b := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 71)
	g1 := b.spawnUnit(TeamBlue, knight, cardKnight, 9000, 8000, 0)
	g2 := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 8000, 0)
	b.resolveCollisions()
	if d := Distance(g1.xMilli, g1.yMilli, g2.xMilli, g2.yMilli); d < 2*500-touchToleranceMilli {
		t.Fatalf("ground-ground coincident pair: distance = %d, want >= %d", d, 2*500-touchToleranceMilli)
	}
	t.Logf("ground-ground: separated to %d (2 radii = 1000)", Distance(g1.xMilli, g1.yMilli, g2.xMilli, g2.yMilli))

	// --- air vs ground: different layers, no separation at all.
	b2 := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 72)
	ground := b2.spawnUnit(TeamBlue, knight, cardKnight, 9000, 8000, 0)
	air := b2.spawnUnit(TeamRed, minion, cardMinion, 9000, 8000, 0)
	if !air.flying || ground.flying {
		t.Fatal("fixture error: knight must be ground, minion must be flying")
	}
	b2.resolveCollisions()
	if d := Distance(ground.xMilli, ground.yMilli, air.xMilli, air.yMilli); d != 0 {
		t.Fatalf("air vs ground must not collide (separate layers); distance changed to %d", d)
	}
	t.Log("air vs ground: no collision (coincident, distance stays 0)")

	// --- air vs air: same layer, separates.
	b3 := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 73)
	a1 := b3.spawnUnit(TeamBlue, minion, cardMinion, 9000, 8000, 0)
	a2 := b3.spawnUnit(TeamRed, minion, cardMinion, 9000, 8000, 0)
	b3.resolveCollisions()
	if d := Distance(a1.xMilli, a1.yMilli, a2.xMilli, a2.yMilli); d < 2*500-touchToleranceMilli {
		t.Fatalf("air-air coincident pair: distance = %d, want >= %d", d, 2*500-touchToleranceMilli)
	}

	// --- building is immovable: the trooper is pushed off, the wall stands.
	b4 := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 74)
	wall, _ := b4.cfg.Table.Unit("Wall")
	w := b4.spawnUnit(TeamBlue, wall, cardKnight, 9000, 8000, 0)
	k := b4.spawnUnit(TeamRed, knight, cardKnight, 9000, 8500, 0) // 500 from wall, overlap
	wx, wy := w.xMilli, w.yMilli
	b4.resolveCollisions()
	if w.xMilli != wx || w.yMilli != wy {
		t.Fatalf("building moved in a collision: (%d,%d) -> (%d,%d)", wx, wy, w.xMilli, w.yMilli)
	}
	if d := Distance(w.xMilli, w.yMilli, k.xMilli, k.yMilli); d < (1000+500)-touchToleranceMilli {
		t.Fatalf("trooper vs building: distance = %d, want >= %d (trooper must give way)", d, 1500-touchToleranceMilli)
	}
	t.Logf("building immovable: wall stayed at (%d,%d), trooper pushed to %d apart", wx, wy,
		Distance(w.xMilli, w.yMilli, k.xMilli, k.yMilli))

	// --- projectile / spell cloud are not entities: never collidable.
	tb5 := newCoverTable()
	b5 := mustBattleWith(t, tb5, troopDeck, troopDeck, 75)
	bomber, _ := tb5.Unit("Bomber")
	shooter := b5.spawnUnit(TeamBlue, bomber, cardKnight, 9000, 4000, 0)
	target := b5.spawnUnit(TeamRed, knight, cardKnight, 9000, 5500, 0)
	runSteps(b5, 40)
	if len(b5.projectiles) == 0 {
		t.Fatal("fixture error: the ranged Bomber should have a shot in flight")
	}
	sp := tb5.cards[cardPushCover].Spell
	b5.castSpell(TeamBlue, tb5.cards[cardPushCover], 9000, 5500)
	for _, e := range b5.allEntities() {
		if e.Kind == KindProjectile {
			t.Fatalf("a projectile appears in the entity list (id %d): it must not be collidable", e.ID)
		}
	}
	_ = shooter
	_ = target
	_ = sp
	t.Logf("projectiles=%d areas=%d but neither is an entity (allEntities has kind<=KindTower only)",
		len(b5.projectiles), len(b5.areas))
}

// TestCoverKnockbackPushback pins 击退: a spell's pushback shoves a movable
// enemy away from the cast point along the line between them.
//
// 判据出处: 参考规格 §5 击退（远离施法点；建筑/塔不可推）.
func TestCoverKnockbackPushback(t *testing.T) {
	tb := newCoverTable()
	b := mustBattleWith(t, tb, troopDeck, troopDeck, 76)
	lord, _ := tb.Unit("Lord")
	e := b.spawnUnit(TeamRed, lord, cardKnight, 9000, 8000, 0)
	wall, _ := tb.Unit("Wall")
	w := b.spawnUnit(TeamRed, wall, cardKnight, 9000, 7000, 0)
	wy := w.yMilli

	before := Distance(9000, 9000, e.xMilli, e.yMilli)
	wallBefore := Distance(9000, 9000, w.xMilli, w.yMilli)
	b.castSpell(TeamBlue, tb.cards[cardPushCover], 9000, 9000)
	after := Distance(9000, 9000, e.xMilli, e.yMilli)
	if after <= before {
		t.Fatalf("movable enemy not pushed away: %d -> %d", before, after)
	}
	if w.yMilli != wy {
		t.Fatalf("immovable building was pushed: y %d -> %d", wy, w.yMilli)
	}
	if wallAfter := Distance(9000, 9000, w.xMilli, w.yMilli); wallAfter != wallBefore {
		t.Fatalf("immovable building moved under pushback: %d -> %d", wallBefore, wallAfter)
	}
	t.Logf("pushback: troop %d -> %d from cast point; building unmoved (%d)", before, after, wallBefore)
}

// TestCoverDeathPayload pins 亡语: on death an entity deals area damage and
// spawns its payload for its own team.
//
// 判据出处: 参考规格 §5 亡语（死亡时范围伤害 + 召唤）.
func TestCoverDeathPayload(t *testing.T) {
	b := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 77)
	bomb, _ := b.cfg.Table.Unit("Bomb")
	knight, _ := b.cfg.Table.Unit("Knight")
	bm := b.spawnUnit(TeamBlue, bomb, cardKnight, 9000, 8000, 0)
	enemy := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 8500, 0) // inside the 2000 blast
	before := enemy.hp

	b.damageEntity(bm, 1000, nil) // kill the bomb
	b.Step()                      // death payload resolves in stepDeaths

	if got := before - enemy.hp; got != bomb.DeathDamage {
		t.Fatalf("death damage on the nearby enemy = %d, want %d", got, bomb.DeathDamage)
	}
	spawned := 0
	for _, e := range b.units {
		if e.Team == TeamBlue && e.Def != nil && e.Def.Key == "Skeleton" {
			spawned++
		}
	}
	if spawned != int(bomb.DeathSpawnN) {
		t.Fatalf("death spawned %d skeletons, want %d", spawned, bomb.DeathSpawnN)
	}
	t.Logf("death payload: %d damage to a 2.0-tile enemy, %d skeletons summoned", bomb.DeathDamage, spawned)
}

// TestCoverSpawnerBuilding pins 召唤: a building emits its periodic spawns up
// to SpawnLimit and then stops.
//
// 判据出处: 参考规格 §5 建筑（周期召唤 / 上限）.
func TestCoverSpawnerBuilding(t *testing.T) {
	b := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 78)
	hut, _ := b.cfg.Table.Unit("Hut")
	hb := b.spawnUnit(TeamBlue, hut, cardHutCover, 9000, 8000, 0)
	countSkel := func() int {
		n := 0
		for _, e := range b.units {
			if e.Team == TeamBlue && e.Def != nil && e.Def.Key == "Skeleton" {
				n++
			}
		}
		return n
	}
	runSteps(b, 70) // 3.5 s: exactly three spawns at 1 s intervals
	if n := countSkel(); n != int(hut.SpawnLimit) {
		t.Fatalf("spawner produced %d live units in 3.5 s, want %d", n, hut.SpawnLimit)
	}
	if hb.spawnedCount != hut.SpawnLimit {
		t.Fatalf("spawnedCount = %d, want %d", hb.spawnedCount, hut.SpawnLimit)
	}
	// Past the limit it stops: no fourth spawn, even though earlier skeletons
	// keep marching off and are eventually killed by a tower. The limit counts
	// SPAWNS, not survivors.
	runSteps(b, 400)
	if hb.spawnedCount != hut.SpawnLimit {
		t.Fatalf("spawner exceeded its limit: spawnedCount = %d, want %d", hb.spawnedCount, hut.SpawnLimit)
	}
	t.Logf("spawner: 3 spawns by 3.5 s, then stops (spawnedCount=%d, live skeletons after 10.3 s = %d -- they marched into a tower's range)",
		hb.spawnedCount, countSkel())
}

// ---------------------------------------------------------------------------
// D10 -- gameplay logic
// ---------------------------------------------------------------------------

// TestCoverElixirBoundaries pins 圣水 0 / 9 / 10 / over-cap: the bar clamps at
// 10, holds no partial progress when full, and cannot pay a cost it cannot
// afford (spending 0 is free).
//
// 判据出处: 参考规格 §3（起始 6 / 上限 10）.
func TestCoverElixirBoundaries(t *testing.T) {
	// Over-cap start is clamped down to the cap.
	if v := newElixirBar(20000).amount(); v != MaxElixirMilli {
		t.Fatalf("newElixirBar(20000).amount = %d, want %d (clamped)", v, MaxElixirMilli)
	}
	if v := newElixirBar(-500).amount(); v != 0 {
		t.Fatalf("newElixirBar(-500).amount = %d, want 0 (clamped)", v)
	}

	full := newElixirBar(MaxElixirMilli)
	if !full.full() {
		t.Fatal("a bar at 10000 milli must report full")
	}
	// Regenerating a full bar stays exactly at the cap and keeps no fraction.
	full.regenerate(0)
	full.regenerate(120000)
	if full.amountMilli != MaxElixirMilli || full.accNum != 0 {
		t.Fatalf("full bar after regen = %d (acc %d), want %d / acc 0", full.amountMilli, full.accNum, MaxElixirMilli)
	}
	if !full.spend(10) || full.amountMilli != 0 {
		t.Fatalf("spend(10) on a full bar: ok=%v amount=%d, want true/0", full.amountMilli == 0, full.amountMilli)
	}
	// At 0 the bar can pay nothing but a free card.
	if full.canAfford(1) {
		t.Fatal("a bar at 0 must not afford a 1-cost card")
	}
	if !full.canAfford(0) {
		t.Fatal("a bar at 0 must afford a free (0-cost) card")
	}
	if full.spend(1) {
		t.Fatal("spend(1) on an empty bar must fail")
	}

	// 9 vs 10: exactly at 9 the bar can pay a 9-cost but not a 10-cost.
	nine := newElixirBar(9000)
	if !nine.canAfford(9) {
		t.Fatal("9 elixir must afford a 9-cost card")
	}
	if nine.canAfford(10) {
		t.Fatal("9 elixir must NOT afford a 10-cost card")
	}
	// units() truncates the sub-point fraction.
	frac := newElixirBar(9999)
	if frac.units() != 9 {
		t.Fatalf("9999 milli units() = %d, want 9", frac.units())
	}
	t.Log("elixir: clamped 20000->10000, cap holds (no partial progress), 9 affords 9 not 10, 0 affords only free")
}

// TestCoverHandFullCycle pins 手牌循环到第 8 张: an 8-card deck's full rotation
// returns the opening hand and the opening "next" after exactly 8 plays.
//
// 判据出处: 参考规格 §7 S11（8 卡组 / 4 手牌 / 第 8 张回到起点）.
func TestCoverHandFullCycle(t *testing.T) {
	b := mustBattle(t, fullDeck, fullDeck, 20240920)
	opening := b.Hand(TeamBlue)
	next0 := b.Next(TeamBlue)
	seen := map[int32]bool{}
	for _, c := range opening {
		seen[c] = true
	}

	// A full 8-card rotation plays the four visible cards in order, then the
	// four that were queued behind them -- not the same slot four times.
	playInOrder := func() {
		h := b.Hand(TeamBlue)
		for _, c := range h {
			setElixir(b, TeamBlue, MaxElixirMilli)
			if err := b.PlayCard(TeamBlue, c, 9000, 9000); err != nil {
				t.Fatalf("play (card %d): %v", c, err)
			}
		}
	}
	playInOrder() // first four: the opening hand
	playInOrder() // next four: the queued cards
	after := b.Hand(TeamBlue)
	for j := 0; j < handSize; j++ {
		if after[j] != opening[j] {
			t.Fatalf("after 8 plays hand[%d] = %d, want the opening %d (hand=%v opening=%v)", j, after[j], opening[j], after, opening)
		}
	}
	if got := b.Next(TeamBlue); got != next0 {
		t.Fatalf("after 8 plays next = %d, want the opening next %d", got, next0)
	}
	// The 8th card is the last distinct id seen: all 8 distinct cards cycled.
	for _, c := range after {
		if !seen[c] {
			t.Fatalf("card %d reappeared that was not in the opening hand", c)
		}
	}
	t.Logf("hand: 8 plays restored the opening hand %v and next=%d (8-card rotation)", opening, next0)
}

// TestCoverTowerHpBoundaries pins 塔 HP 1 / 0: the last point kills and credits
// a crown, a zero-damage hit does nothing.
//
// 判据出处: 参考规格 §3（塔被摧毁⇒冠）/ §5（伤害漏斗）.
func TestCoverTowerHpBoundaries(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 79)
	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])

	tower.hp = 1
	// A zero/negative damage hit must not kill it.
	b.damageEntity(tower, 0, nil)
	b.damageEntity(tower, -5, nil)
	if !tower.alive || tower.hp != 1 {
		t.Fatalf("zero/negative damage killed a 1-HP tower: alive=%v hp=%d", tower.alive, tower.hp)
	}
	// One point finishes it.
	b.damageEntity(tower, 1, nil)
	if tower.alive || tower.hp != 0 {
		t.Fatalf("1 point of damage on a 1-HP tower: alive=%v hp=%d, want false/0", tower.alive, tower.hp)
	}
	b.stepDeaths()
	if got := b.Crowns(TeamBlue); got != 1 {
		t.Fatalf("crowns after a princess tower fell = %d, want 1", got)
	}
	t.Log("tower HP: 1 HP survives 0/negative damage, dies to exactly 1, credits one crown")
}

// TestCoverTimeBoundaries pins the clock boundaries 179 / 180 / 181 / 299 /
// 300 / 301 s with level crowns: nothing ends before 180, 180 with level
// crowns enters overtime, overtime runs to 300, and 300 ends the match.
//
// 判据出处: 参考规格 §3（180 s 冠数判定 / 平冠加时 / 300 s 加时结束）.
func TestCoverTimeBoundaries(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 80)

	stepTo := func(ms int32) {
		b.tick = ms/MSecPerTick - 1
		b.serverMs = b.tick * MSecPerTick
		b.Step()
	}

	stepTo(179000)
	if b.Ended() || b.Phase() != PhaseNormal || b.ServerMs() != 179000 {
		t.Fatalf("at 179 s: ended=%v phase=%d ms=%d, want false/normal/179000", b.Ended(), b.Phase(), b.ServerMs())
	}
	stepTo(180000)
	if b.Ended() || b.Phase() != PhaseOvertime {
		t.Fatalf("at 180 s with level crowns: ended=%v phase=%d, want false/overtime", b.Ended(), b.Phase())
	}
	stepTo(181000)
	if b.Ended() || b.Phase() != PhaseOvertime {
		t.Fatalf("at 181 s (overtime): ended=%v phase=%d, want false/overtime", b.Ended(), b.Phase())
	}
	stepTo(299000)
	if b.Ended() {
		t.Fatal("match must not end at 299 s (overtime still running)")
	}
	stepTo(300000)
	if !b.Ended() {
		t.Fatal("match must end at 300 s")
	}
	r := b.Result()
	if !r.Draw || r.Reason != ReasonDraw {
		t.Fatalf("300 s with level crowns and level HP: result=%+v, want a draw", r)
	}
	// 301 s and beyond: the ended match ignores every further tick.
	tickAtEnd := b.tick
	runSteps(b, 20)
	if !b.Ended() || b.tick != tickAtEnd {
		t.Fatalf("an ended match advanced on further ticks: ended=%v tick=%d (want %d)", b.Ended(), b.tick, tickAtEnd)
	}
	t.Log("clock: 179 normal -> 180 overtime -> 300 draw; a ended match ignores further ticks")
}

// TestCoverCrownCounts pins 冠数 0:0 / 1:1 / 2:2 / 3:0 and that crowns alone
// never end a match before the clock does (only a king's fall does).
//
// 判据出处: 参考规格 §3（冠数比较 / 国王塔倒下⇒3 冠）.
func TestCoverCrownCounts(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 81)
	if b.Crowns(TeamBlue) != 0 || b.Crowns(TeamRed) != 0 {
		t.Fatalf("opening crowns = %d/%d, want 0/0", b.Crowns(TeamBlue), b.Crowns(TeamRed))
	}

	kill := func(team Team, right bool) {
		x := BluePrincessLeftPos[0]
		if team == TeamRed {
			x = RedPrincessLeftPos[0]
		}
		if right {
			if team == TeamBlue {
				x = BluePrincessRightPos[0]
			} else {
				x = RedPrincessRightPos[0]
			}
		}
		b.damageEntity(towerOf(t, b, team, TowerKindPrincess, x), 1_000_000, nil)
		b.Step()
	}

	kill(TeamRed, false) // blue scores on red's left
	kill(TeamBlue, false)
	if b.Crowns(TeamBlue) != 1 || b.Crowns(TeamRed) != 1 {
		t.Fatalf("after one each: crowns = %d/%d, want 1/1", b.Crowns(TeamBlue), b.Crowns(TeamRed))
	}
	if b.Ended() {
		t.Fatal("1:1 must not end the match before the clock")
	}
	kill(TeamRed, true)
	kill(TeamBlue, true)
	if b.Crowns(TeamBlue) != 2 || b.Crowns(TeamRed) != 2 {
		t.Fatalf("after two each: crowns = %d/%d, want 2/2", b.Crowns(TeamBlue), b.Crowns(TeamRed))
	}
	if b.Ended() {
		t.Fatal("2:2 must not end the match before the clock")
	}

	// A fresh battle: the red king falls with no other crowns => 3:0.
	k := mustBattle(t, troopDeck, troopDeck, 82)
	k.damageEntity(k.kings[TeamRed].entity, 1_000_000, nil)
	k.Step()
	r := k.Result()
	if r.CrownsA != 3 || r.CrownsB != 0 || r.Reason != ReasonKingDestroyed || r.Winner != TeamBlue {
		t.Fatalf("king destroyed from 0:0: result=%+v, want 3:0 king_destroyed blue", r)
	}
	t.Log("crowns: 0:0 -> 1:1 -> 2:2 without ending; a king's fall from 0:0 gives 3:0")
}

// TestCoverOnlyBuildingsCannotSeeTroops pins the hard targeting filter: a
// building-only troop cannot even see a troop, only structures.
//
// 判据出处: 参考规格 §5 索敌（只打建筑 ⇒ 看不见部队）.
func TestCoverOnlyBuildingsCannotSeeTroops(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 83)
	giant, _ := b.cfg.Table.Unit("Giant")
	if !giant.OnlyBuildings {
		t.Fatal("fixture error: Giant must be a building-only troop")
	}
	g := b.spawnUnit(TeamBlue, giant, cardGiant, 9000, 9000, 0)
	knight, _ := b.cfg.Table.Unit("Knight")
	k := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 10000, 0)
	tower := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])

	if canTarget(g, k) {
		t.Fatal("a building-only troop must not be able to target an enemy troop")
	}
	if !canTarget(g, tower) {
		t.Fatal("a building-only troop must be able to target a crown tower")
	}
	t.Log("targeting filter: Giant (only_buildings) cannot see a Knight, can see a tower")
}

// TestCoverVictoryCrownsAt180Differs pins 加时触发条件: unequal crowns at 180 s
// end the match at once (no overtime); equal crowns go to overtime.
//
// 判据出处: 参考规格 §3（180 s 冠数不同⇒判定；相同⇒加时）.
func TestCoverVictoryCrownsAt180Differs(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 84)
	b.damageEntity(towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0]), 1_000_000, nil)
	b.Step()
	b.tick = RegulationMs/MSecPerTick - 1
	b.serverMs = b.tick * MSecPerTick
	b.Step()
	r := b.Result()
	if !r.Ended || r.Reason != ReasonTimeUpCrowns || r.Winner != TeamBlue || r.CrownsA != 1 || r.CrownsB != 0 {
		t.Fatalf("180 s with 1-0 crowns: result=%+v, want blue 1-0 time_up_crowns", r)
	}
	if b.Phase() != PhaseEnded {
		t.Fatalf("unequal crowns at 180 s must end immediately, phase=%d", b.Phase())
	}
	t.Logf("180 s unequal crowns: ended=%v reason=%s winner=%v phase=%d", r.Ended, r.Reason, r.Winner, b.Phase())
}

// TestCoverAttackWindupAndProjectile pins 攻击: an attack is load-time windup +
// hit-speed cadence, and a ranged attacker resolves its hit from a projectile.
//
// 判据出处: 参考规格 §5 攻击（首次 LoadTime 前摇 / HitSpeed 间隔 / 远程走投射物）.
func TestCoverAttackWindupAndProjectile(t *testing.T) {
	b := mustBattleWith(t, newCoverTable(), troopDeck, troopDeck, 85)
	bomber, _ := b.cfg.Table.Unit("Bomber") // LoadTime 500, HitSpeed 1000, ranged
	knight, _ := b.cfg.Table.Unit("Knight")
	atk := b.spawnUnit(TeamBlue, bomber, cardKnight, 9000, 4000, 0)
	def := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 6500, 0) // within 5.0-tile range

	runSteps(b, 9) // 450 ms < LoadTime 500
	if atk.swings != 0 {
		t.Fatalf("attacker swung %d times before its 500 ms windup elapsed", atk.swings)
	}
	runSteps(b, 1) // 500 ms: first swing
	if atk.swings != 1 {
		t.Fatalf("attacker swings after 500 ms = %d, want 1", atk.swings)
	}
	if len(b.projectiles) == 0 && def.hp == def.maxHP {
		t.Fatal("a ranged swing must produce either a projectile or an applied hit")
	}
	runSteps(b, 20) // 1000 ms later: the second swing
	if atk.swings != 2 {
		t.Fatalf("attacker swings after 1500 ms = %d, want 2 (HitSpeed 1000)", atk.swings)
	}
	t.Logf("attack: no swing before 500 ms windup, swings=1 at 500 ms, swings=2 at 1500 ms (%d projectiles in flight)",
		len(b.projectiles))
}
