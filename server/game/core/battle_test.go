package core

import (
	"reflect"
	"testing"
	"time"
)

// TestUnitWalksToBridgeThenAcross pins ground pathing: a ground unit may only
// cross the river at a bridge, so it reaches the bridge centre's x before its y
// passes the far bank.
//
// 判据出处: 参考规格 §5 移动（地面单位沿车道 → 最近桥 → 目标点）.
func TestUnitWalksToBridgeThenAcross(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 3)
	def, _ := b.cfg.Table.Unit("Knight")
	e := b.spawnUnit(TeamBlue, def, cardKnight, 9000, 1000, 0)

	crossedX := int32(-1)
	for i := 0; i < 800 && crossedX < 0; i++ {
		b.Step()
		if e.yMilli >= RiverTopMilli && e.yMilli < RiverBottomMilli && b.arena.IsWater(e.xMilli, e.yMilli) {
			t.Fatalf("tick %d: ground unit stood on water at (%d,%d)", i, e.xMilli, e.yMilli)
		}
		if e.yMilli >= RiverBottomMilli {
			crossedX = e.xMilli
		}
	}
	if crossedX < 0 {
		t.Fatalf("unit never crossed the river (pos %d,%d)", e.xMilli, e.yMilli)
	}
	t.Logf("crossed y=17.0 at x=%d (bridge A centre = %d)", crossedX, BridgeAxMilli)
	if absI32(crossedX-BridgeAxMilli) > BridgeHalfMilli {
		t.Fatalf("crossed at x=%d, want within 1 tile of the nearest bridge centre (%d)", crossedX, BridgeAxMilli)
	}
}

// TestFlyingUnitIgnoresRiver pins flight: a flying unit takes the straight line
// and crosses the water rather than funnelling through a bridge.
//
// 判据出处: 参考规格 §5 移动（飞行单位无视地形，可直接过河）.
func TestFlyingUnitIgnoresRiver(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 3)
	def, _ := b.cfg.Table.Unit("Minion")
	if !isFlyingDef(def) {
		t.Fatal("fixture error: Minion must be a flying unit")
	}
	e := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)

	sawWater := false
	crossedX := int32(-1)
	for i := 0; i < 400; i++ {
		b.Step()
		if e.yMilli >= RiverTopMilli && e.yMilli < RiverBottomMilli && b.arena.IsWater(e.xMilli, e.yMilli) {
			sawWater = true
		}
		if e.yMilli >= RiverBottomMilli && crossedX < 0 {
			crossedX = e.xMilli
		}
	}
	if crossedX < 0 {
		t.Fatalf("flying unit never crossed (pos %d,%d)", e.xMilli, e.yMilli)
	}
	if !sawWater {
		t.Fatal("flying unit never stood over open water: it must ignore the river")
	}
	t.Logf("flew across y=17.0 at x=%d (bridge centres are %d / %d)", crossedX, BridgeAxMilli, BridgeBxMilli)
	if absI32(crossedX-BridgeAxMilli) <= BridgeHalfMilli || absI32(crossedX-BridgeBxMilli) <= BridgeHalfMilli {
		t.Fatalf("flying unit crossed at x=%d, which is on a bridge: it should fly the straight line", crossedX)
	}
}

// TestMeleeKillsInExpectedHits pins the attack cycle arithmetic against the
// verified Knight numbers (1766 HP / 202 damage, anchors.json).
//
// Case 1: two Knights placed symmetrical trade evenly and both die on the 9th
// swing -- ceil(1766/202) = 9, and 8 swings (1616) is not enough.
// Case 2: the same Knight attacking a Princess Tower needs 9 swings and cannot
// kill it (9 * 202 = 1818 < 3584), while the tower's 14 swings kill it
// (ceil(1766/128) = 14).
func TestMeleeKillsInExpectedHits(t *testing.T) {
	knight, _ := newTestTable().Unit("Knight")
	wantHits := (knight.HP + knight.Damage - 1) / knight.Damage
	if wantHits != 9 {
		t.Fatalf("fixture changed: ceil(1766/202) = %d, want 9", wantHits)
	}

	// --- Case 1: a mirror melee fight, resolved simultaneously.
	//
	// The crown towers are inert in this scenario (newPerfTable(false) zeroes
	// their damage/range) so the only damage in the fight is the two Knights'
	// own. With the real towers live, both Knights sit inside the enemy
	// princess towers' sight and their fire contaminates the arithmetic.
	b := mustBattleWith(t, newPerfTable(false), troopDeck, troopDeck, 4)
	blue := b.spawnUnit(TeamBlue, knight, cardKnight, 9000, 10000, 0)
	red := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 12000, 0)
	runSteps(b, 300)

	if blue.alive || red.alive {
		t.Fatalf("mirror fight must end with both dead (blue %d hp / %d swings, red %d hp / %d swings)",
			blue.hp, blue.swings, red.hp, red.swings)
	}
	if blue.swings != wantHits || red.swings != wantHits {
		t.Fatalf("swings = %d / %d, want %d each", blue.swings, red.swings, wantHits)
	}
	t.Logf("mirror Knight fight: %d swings each, both dead; %d damage kills %d HP, %d does not",
		wantHits, wantHits*knight.Damage, knight.HP, (wantHits-1)*knight.Damage)

	// --- Case 2: the same Knight into a Princess Tower.
	b2 := mustBattle(t, troopDeck, troopDeck, 4)
	tower := towerOf(t, b2, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	k := b2.spawnUnit(TeamBlue, knight, cardKnight, RedPrincessLeftPos[0], RedPrincessLeftPos[1]-1000, 0)
	for i := 0; i < 500 && k.alive; i++ {
		b2.Step()
	}
	towerDef, _ := newTestTable().Unit("PrincessTower")
	wantTowerHits := (knight.HP + towerDef.Damage - 1) / towerDef.Damage
	if k.alive {
		t.Fatalf("the princess tower failed to kill the knight in 500 ticks (%d hp left)", k.hp)
	}
	if tower.swings != wantTowerHits {
		t.Fatalf("tower swings = %d, want %d (= ceil(%d/%d))", tower.swings, wantTowerHits, knight.HP, towerDef.Damage)
	}
	if !tower.alive {
		t.Fatal("the tower must survive: the knight only lands 9 hits for 1818 of its 3584 HP")
	}
	if k.swings != wantHits {
		t.Fatalf("knight landed %d swings before dying, want %d", k.swings, wantHits)
	}
	t.Logf("knight vs princess tower: knight landed %d swings (%d damage of %d HP), tower landed %d swings and killed it",
		k.swings, k.swings*knight.Damage, towerDef.HP, tower.swings)
}

// TestTowerShellsNearestTarget pins tower targeting and cadence: the tower
// shoots the **nearest** target only, on its HitSpeedMs grid, and its damage
// arrives with the projectile's flight time.
//
// 判据出处: 参考规格 §5（塔只攻击进入其 range 的最近敌方单位）+ §4.1
// （公主塔 hit_speed 800 ms, damage 128, projectile speed 600 = 10 tiles/s）.
func TestTowerShellsNearestTarget(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 8)
	knight, _ := b.cfg.Table.Unit("Knight")
	tower := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])
	near := b.spawnUnit(TeamRed, knight, cardKnight, BluePrincessLeftPos[0], BluePrincessLeftPos[1]+2500, 0)
	far := b.spawnUnit(TeamRed, knight, cardKnight, BluePrincessLeftPos[0], BluePrincessLeftPos[1]+5500, 0)

	runSteps(b, 50)

	if tower.targetID != near.ID {
		t.Fatalf("tower target = %d, want the nearer enemy %d", tower.targetID, near.ID)
	}
	// LoadTime 0 => first swing on tick 1, then every 800 ms = 16 ticks:
	// ticks 1, 17, 33, 49 = 4 swings within 50 ticks.
	if tower.swings != 4 {
		t.Fatalf("tower swings after 50 ticks = %d, want 4 (800 ms grid from tick 1)", tower.swings)
	}
	// The projectile travels 10 tiles/s, so a 2.5-tile shot lands 5 ticks after
	// it is fired: swings at 1/17/33 land at 6/22/38, and the 4th at 54.
	wantHP := near.maxHP - 3*128
	if near.hp != wantHP {
		t.Fatalf("near enemy HP = %d, want %d (3 of 4 shots landed)", near.hp, wantHP)
	}
	if far.hp != far.maxHP {
		t.Fatalf("far enemy HP = %d, want %d untouched: the tower must shoot only the nearest", far.hp, far.maxHP)
	}
	t.Logf("tower: %d swings in 50 ticks, near enemy %d/%d hp (3 shots landed), far enemy untouched",
		tower.swings, near.hp, near.maxHP)
}

// TestKingTowerInertUntilProvoked pins the lazy king tower: it does not attack
// while unprovoked even with an enemy in range, and after one of its own
// princess towers falls it starts fighting **exactly** KingActivationMs (3300)
// later.
//
// 判据出处: 参考规格 §4.1（国王塔惰性：己方任一公主塔被摧毁或自身被攻击 ⇒ 3300 ms
// 后开始攻击）.
func TestKingTowerInertUntilProvoked(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 6)
	// A Giant is the intruder because it survives the princess towers' fire
	// (every point inside the blue king's 7000 sight is also inside a blue
	// princess tower's 7500 sight, so the intruder *will* be shot at).
	knight, _ := b.cfg.Table.Unit("Giant")
	king := b.kings[TeamBlue]
	princess := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])
	intruder := b.spawnUnit(TeamRed, knight, cardKnight, 9000, 9000, 0)

	runSteps(b, 100)
	if king.active {
		t.Fatal("an unprovoked king tower must not be active")
	}
	if king.swings != 0 {
		t.Fatalf("an unprovoked king tower swung %d times", king.swings)
	}
	// There IS a legal enemy inside the king's sight, so "it did not attack" is
	// a real refusal rather than an absence of targets.
	if acquireTarget(king.entity, b.allEntities()) == nil {
		t.Fatal("fixture error: the intruder should be inside the king tower's sight")
	}
	// And it has fired nothing: no projectile on the board belongs to the king.
	for _, p := range b.projectiles {
		if p.ownerID == king.ID {
			t.Fatalf("an unprovoked king tower fired a projectile (owner %d)", p.ownerID)
		}
	}
	// The intruder may well have been shot by a princess tower -- those are
	// always active -- but the total damage on it must be exactly what those
	// towers landed. If the inert king had fired even once, the arithmetic
	// would not close.
	princessDef, _ := b.cfg.Table.Unit("PrincessTower")
	princessSwings, inFlight := int32(0), int32(0)
	for _, e := range b.towers {
		if e.Team != TeamBlue || e.Kind != KindTower || isKingDef(e.Def) {
			continue
		}
		princessSwings += e.swings
		for _, p := range b.projectiles {
			if p.ownerID == e.ID {
				inFlight++
			}
		}
	}
	if princessSwings == 0 {
		t.Fatal("fixture error: a blue princess tower should have been shooting the intruder")
	}
	landed := princessSwings - inFlight
	if got := intruder.maxHP - intruder.hp; got != landed*princessDef.Damage {
		t.Fatalf("intruder lost %d HP; the princess towers landed %d shots x %d = %d",
			got, landed, princessDef.Damage, landed*princessDef.Damage)
	}
	if !intruder.alive {
		t.Fatal("the intruder should have survived: the princess towers alone cannot kill it in 5 s")
	}
	t.Logf("before provocation: king swings=0, %d princess swings landed (%d HP on the intruder, %d rounds in flight)",
		princessSwings, intruder.maxHP-intruder.hp, inFlight)

	// Destroy one of the king's own princess towers.
	b.damageEntity(princess, 1_000_000, nil)
	wantArmed := b.serverMs + MSecPerTick
	b.Step()
	if !king.provoked || king.armedAtMs != wantArmed {
		t.Fatalf("king armed at %d, want %d (the tick its princess tower fell)", king.armedAtMs, wantArmed)
	}
	// 3250 ms later it must still not have swung.
	for king.armedAtMs+KingActivationMs-MSecPerTick > b.serverMs {
		b.Step()
	}
	if king.active || king.swings != 0 {
		t.Fatalf("king became active/swung %d before 3300 ms elapsed", king.swings)
	}
	for !king.active && b.tick < 1000 {
		b.Step()
	}
	if !king.active {
		t.Fatal("king never activated")
	}
	if got := king.activeAtMs - king.armedAtMs; got != KingActivationMs {
		t.Fatalf("king activated %d ms after being provoked, want exactly %d", got, KingActivationMs)
	}
	runSteps(b, 40)
	if king.swings == 0 {
		t.Fatal("an active king tower must attack a target in range")
	}
	t.Logf("king tower: armed at %d ms, active at %d ms (delta %d, want %d), %d swings after activation",
		king.armedAtMs, king.activeAtMs, king.activeAtMs-king.armedAtMs, KingActivationMs, king.swings)
}

// TestVictoryByKingDestroyed pins the instant win: a destroyed king tower ends
// the match with three crowns.
func TestVictoryByKingDestroyed(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 9)
	b.damageEntity(b.kings[TeamRed].entity, 1_000_000, nil)
	b.Step()

	r := b.Result()
	if !r.Ended || r.Draw {
		t.Fatalf("result = %+v, want an ended non-draw match", r)
	}
	if r.Winner != TeamBlue {
		t.Fatalf("winner = %v, want blue", r.Winner)
	}
	if r.Reason != ReasonKingDestroyed {
		t.Fatalf("reason = %q, want %q", r.Reason, ReasonKingDestroyed)
	}
	if r.CrownsA != 3 || r.CrownsB != 0 {
		t.Fatalf("crowns = %d/%d, want 3/0", r.CrownsA, r.CrownsB)
	}
	if b.Phase() != PhaseEnded {
		t.Fatalf("phase = %d, want %d", b.Phase(), PhaseEnded)
	}
	if !b.Ended() {
		t.Fatal("Ended() must report true")
	}
}

// TestVictoryByCrownsAt180s pins the regulation-time check: different crowns end
// the match immediately, without overtime.
//
// 判据出处: 参考规格 §3（180 s 冠数不同 ⇒ 判定；相同 ⇒ 加时）.
func TestVictoryByCrownsAt180s(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 10)
	b.damageEntity(towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0]), 1_000_000, nil)
	runSteps(b, 3)
	if b.Crowns(TeamBlue) != 1 {
		t.Fatalf("crowns = %d, want 1 after a princess tower falls", b.Crowns(TeamBlue))
	}
	if b.Ended() {
		t.Fatal("one crown must not end the match")
	}

	// Jump to the last tick of regulation.
	b.tick = RegulationMs/MSecPerTick - 1
	b.serverMs = b.tick * MSecPerTick
	b.Step()
	if b.serverMs != RegulationMs {
		t.Fatalf("serverMs = %d, want %d", b.serverMs, RegulationMs)
	}
	r := b.Result()
	if !r.Ended {
		t.Fatalf("the match must end at 180 s with unequal crowns (result %+v)", r)
	}
	if r.Reason != ReasonTimeUpCrowns {
		t.Fatalf("reason = %q, want %q", r.Reason, ReasonTimeUpCrowns)
	}
	if r.Winner != TeamBlue || r.CrownsA != 1 || r.CrownsB != 0 {
		t.Fatalf("result = %+v, want blue 1-0", r)
	}
	if b.Phase() != PhaseEnded {
		t.Fatalf("phase = %d: unequal crowns must not enter overtime", b.Phase())
	}
}

// TestOvertimeThenHpRate pins the overtime path: level crowns at 180 s go to
// overtime, and level crowns at 300 s fall to the remaining tower HP fraction,
// in ten-thousandths.
//
// 判据出处: 参考规格 §3 胜负判定 ②③④.
func TestOvertimeThenHpRate(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 13)

	// Level crowns at the end of regulation => overtime, not the end.
	b.tick = RegulationMs/MSecPerTick - 1
	b.serverMs = b.tick * MSecPerTick
	b.Step()
	if b.Ended() {
		t.Fatalf("level crowns at 180 s must not end the match (result %+v)", b.Result())
	}
	if b.Phase() != PhaseOvertime {
		t.Fatalf("phase = %d, want %d (overtime)", b.Phase(), PhaseOvertime)
	}

	// Damage one red princess tower to exactly half.
	redPrincess := towerOf(t, b, TeamRed, TowerKindPrincess, RedPrincessLeftPos[0])
	redPrincess.hp = redPrincess.maxHP / 2

	// Level crowns at the end of overtime => HP-rate tiebreaker.
	b.tick = (RegulationMs+OvertimeMs)/MSecPerTick - 1
	b.serverMs = b.tick * MSecPerTick
	b.Step()

	// Blue keeps all three towers: 3584+3584+6144 = 13312 => 10000.
	// Red loses half of one 3584 tower: 1792+3584+6144 = 11520 => 11520*10000/13312 = 8653.
	r := b.Result()
	if !r.Ended || r.Draw {
		t.Fatalf("result = %+v, want an ended non-draw match", r)
	}
	if r.Reason != ReasonTimeUpHp {
		t.Fatalf("reason = %q, want %q", r.Reason, ReasonTimeUpHp)
	}
	if r.HpRateA != 10000 {
		t.Fatalf("HpRateA = %d, want 10000", r.HpRateA)
	}
	if r.HpRateB != 8653 {
		t.Fatalf("HpRateB = %d, want 8653 (11520*10000/13312)", r.HpRateB)
	}
	if r.Winner != TeamBlue {
		t.Fatalf("winner = %v, want blue (it kept more of its towers)", r.Winner)
	}
	if r.CrownsA != 0 || r.CrownsB != 0 {
		t.Fatalf("crowns = %d/%d, want 0/0", r.CrownsA, r.CrownsB)
	}
	t.Logf("300 s tiebreak: hp rates %d vs %d (万分比), winner blue, reason %q", r.HpRateA, r.HpRateB, r.Reason)
}

// TestSurrender pins the surrender shortcut.
func TestSurrender(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 14)
	b.Surrender(TeamRed)
	r := b.Result()
	if !r.Ended || r.Winner != TeamBlue || r.Reason != ReasonSurrender {
		t.Fatalf("result = %+v, want blue winning by surrender", r)
	}
	if b.Surrender(TeamBlue); b.Result().Winner != TeamBlue {
		t.Fatal("a second surrender must not change an ended match")
	}
}

// TestDeterminism pins reproducibility: the same seed and the same operation
// sequence must produce a field-identical snapshot.
func TestDeterminism(t *testing.T) {
	script := func(b *Battle) {
		for i := 0; i < 900; i++ {
			if i%60 == 0 {
				setElixir(b, TeamBlue, MaxElixirMilli)
				if err := b.PlayCard(TeamBlue, b.Hand(TeamBlue)[0], 9000, 9000); err != nil {
					t.Fatalf("blue play at tick %d: %v", i, err)
				}
				setElixir(b, TeamRed, MaxElixirMilli)
				if err := b.PlayCard(TeamRed, b.Hand(TeamRed)[0], 9000, 22000); err != nil {
					t.Fatalf("red play at tick %d: %v", i, err)
				}
			}
			b.Step()
		}
	}
	b1 := mustBattle(t, fullDeck, troopDeck, 987654321)
	b2 := mustBattle(t, fullDeck, troopDeck, 987654321)
	script(b1)
	script(b2)

	s1, s2 := b1.Snapshot(), b2.Snapshot()
	if s1.Seq != s2.Seq || s1.ServerMs != s2.ServerMs || s1.Phase != s2.Phase {
		t.Fatalf("clock diverged: %d/%d/%d vs %d/%d/%d", s1.Seq, s1.ServerMs, s1.Phase, s2.Seq, s2.ServerMs, s2.Phase)
	}
	if s1.ElixirA != s2.ElixirA || s1.ElixirB != s2.ElixirB || s1.CrownsA != s2.CrownsA || s1.CrownsB != s2.CrownsB {
		t.Fatalf("economy/crowns diverged: %+v vs %+v", s1, s2)
	}
	if !reflect.DeepEqual(s1.TowersA, s2.TowersA) || !reflect.DeepEqual(s1.TowersB, s2.TowersB) {
		t.Fatalf("towers diverged:\n%v\n%v", s1.TowersA, s2.TowersA)
	}
	if !reflect.DeepEqual(s1.HandA, s2.HandA) || !reflect.DeepEqual(s1.NextA, s2.NextA) {
		t.Fatalf("hand diverged: %v/%d vs %v/%d", s1.HandA, s1.NextA, s2.HandA, s2.NextA)
	}
	if len(s1.Entities) != len(s2.Entities) {
		t.Fatalf("entity count diverged: %d vs %d", len(s1.Entities), len(s2.Entities))
	}
	for i := range s1.Entities {
		if s1.Entities[i] != s2.Entities[i] {
			t.Fatalf("entity %d diverged: %+v vs %+v", i, s1.Entities[i], s2.Entities[i])
		}
	}
	if !reflect.DeepEqual(s1, s2) {
		t.Fatalf("snapshots differ:\n%+v\n%+v", s1, s2)
	}
	if !reflect.DeepEqual(b1.Result(), b2.Result()) {
		t.Fatalf("results differ: %+v vs %+v", b1.Result(), b2.Result())
	}
	t.Logf("determinism: %d ticks, %d entities, snapshot identical (seq=%d serverMs=%d)",
		b1.tick, len(s1.Entities), s1.Seq, s1.ServerMs)
}

// TestPerfFullMatch measures a full 300 s match (6000 ticks) twice: once with
// 60 inert bodies on the board for the whole match, once with 30v30 Knights
// actually fighting. Both must finish well under 2 s.
func TestPerfFullMatch(t *testing.T) {
	const ticks = (RegulationMs + OvertimeMs) / MSecPerTick // 6000

	// Scenario A: 60 bodies that never die (towers deal no damage), so the
	// per-tick cost of pathing + collision + separation is measured at a
	// constant board size.
	tb := newPerfTable(false)
	b := mustBattleWith(t, tb, troopDeck, troopDeck, 21)
	dummy, _ := tb.Unit("Dummy")
	for i := 0; i < 30; i++ {
		bx := int32(2000 + (i%6)*2800)
		by := int32(4000 + (i/6)*2000)
		b.spawnUnit(TeamBlue, dummy, cardSkeleton, bx, by, 0)
		b.spawnUnit(TeamRed, dummy, cardSkeleton, bx, ArenaHMilli-by, 0)
	}
	if len(b.units) != 60 {
		t.Fatalf("scenario A board = %d units, want 60", len(b.units))
	}
	start := time.Now()
	for i := 0; i < ticks; i++ {
		b.Step()
	}
	elapsedA := time.Since(start)
	if b.serverMs != RegulationMs+OvertimeMs {
		t.Fatalf("scenario A ran to %d ms, want %d", b.serverMs, RegulationMs+OvertimeMs)
	}
	t.Logf("perf A: %d ticks with 60 inert bodies always on board: %v (%d alive at end)",
		ticks, elapsedA, len(b.units))
	if elapsedA > 2*time.Second {
		t.Fatalf("scenario A took %v, want < 2s", elapsedA)
	}

	// Scenario B: 30v30 fighting bodies, again held on the board for the whole
	// match. The "Brawler" fixture has enough HP to survive 300 s of its own
	// fire (30 attackers x 60 damage/s = 1800 DPS against 5,000,000 HP), and
	// the crown towers (10,000,000 HP) are live, so this measures pathing +
	// collision + targeting + the full attack/projectile cycle at 60 units.
	tb2 := newPerfTable(true)
	b2 := mustBattleWith(t, tb2, troopDeck, troopDeck, 22)
	brawler, _ := tb2.Unit("Brawler")
	for i := 0; i < 30; i++ {
		bx := int32(2000 + (i%6)*2800)
		by := int32(4000 + (i/6)*2000)
		b2.spawnUnit(TeamBlue, brawler, cardSkeleton, bx, by, 0)
		b2.spawnUnit(TeamRed, brawler, cardSkeleton, bx, ArenaHMilli-by, 0)
	}
	if len(b2.units) != 60 {
		t.Fatalf("scenario B board = %d units, want 60", len(b2.units))
	}
	start = time.Now()
	for i := 0; i < ticks; i++ {
		b2.Step()
	}
	elapsedB := time.Since(start)
	if b2.serverMs != RegulationMs+OvertimeMs {
		t.Fatalf("scenario B ran to %d ms, want %d", b2.serverMs, RegulationMs+OvertimeMs)
	}
	t.Logf("perf B: %d ticks with 60 fighting bodies always on board: %v (%d alive at end, %d swings)",
		ticks, elapsedB, len(b2.units), b2.countSwings())
	if elapsedB > 2*time.Second {
		t.Fatalf("scenario B took %v, want < 2s", elapsedB)
	}
}

// countSwings totals the swings landed by every live entity, so the perf log
// can prove combat actually ran in the measured window.
func (b *Battle) countSwings() int32 {
	var n int32
	for _, e := range b.allEntities() {
		n += e.swings
	}
	return n
}

// newPerfTable builds the perf fixture: crown towers with 10,000,000 HP (so a
// match can never end early) plus an inert "Dummy" mover. With combat=false the
// towers also stop attacking, which keeps the board at a constant size.
func newPerfTable(combat bool) *fakeTable {
	t := newTestTable()
	for _, key := range []string{"PrincessTower", "KingTower"} {
		d := t.units[key]
		d.HP = 10_000_000
		if !combat {
			d.Damage = 0
			d.RangeMilli = 0
			d.SightMilli = 0
		}
	}
	t.units["Dummy"] = &UnitDef{
		ID: 401, Key: "Dummy", NameCN: "测试体", Kind: KindTroop,
		HP: 1000, Damage: 0, HitSpeedMs: 1000, LoadTimeMs: 0,
		SpeedMilliPerSec: SpeedMilliPerSec(45), RangeMilli: 0, SightMilli: 0,
		DeployMs: 0, RadiusMilli: 500, Mass: 4, AtkGround: true,
	}
	// Brawler is the combat scenario's body: a normal attack cycle and a speed
	// that makes it walk, with enough HP to still be alive after 300 s of
	// 30-vs-30 fighting.
	t.units["Brawler"] = &UnitDef{
		ID: 402, Key: "Brawler", NameCN: "测试斗士", Kind: KindTroop,
		HP: 5_000_000, Damage: 60, HitSpeedMs: 1000, LoadTimeMs: 0,
		SpeedMilliPerSec: SpeedMilliPerSec(45), RangeMilli: 1200, SightMilli: 5500,
		DeployMs: 0, RadiusMilli: 500, Mass: 6, AtkGround: true,
	}
	const cardDummy = int32(101)
	t.cards[cardDummy] = &CardDef{ID: cardDummy, Key: "dummy", NameCN: "测试体", Kind: CardTypeTroop, Elixir: 1, UnitKey: "Dummy", UnitN: 1}
	return t
}

// TestSnapshotCarriesDeathFrame pins the AV1 defect: a casualty used to be
// reaped by compact() at the end of the tick it died, and Snapshot() only
// iterates b.units, so anim=AnimDie could never reach the client -- the unit
// simply vanished (no death animation at all). The death frame has to survive
// into a snapshot, and it must not be re-sent for ever afterwards.
func TestSnapshotCarriesDeathFrame(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 31)
	knight, _ := b.cfg.Table.Unit("Knight")
	k := b.spawnUnit(TeamBlue, knight, cardKnight, 9000, 9000, 0)

	b.damageEntity(k, k.hp, nil) // lethal: queued for this tick's death batch
	b.Step()

	// The tick it died: the snapshot must carry the death frame, once.
	found := false
	for _, e := range b.Snapshot().Entities {
		if e.ID != k.ID {
			continue
		}
		found = true
		if e.Anim != AnimDie || e.HP != 0 {
			t.Fatalf("death frame = anim=%d hp=%d, want anim=%d hp=0", e.Anim, e.HP, AnimDie)
		}
	}
	if !found {
		t.Fatal("the casualty is missing from the snapshot of the tick it died")
	}

	// ...and it is not re-sent after that: the following tick's snapshot has it gone.
	b.Step()
	for _, e := range b.Snapshot().Entities {
		if e.ID == k.ID {
			t.Fatalf("the casualty is still in the next tick's snapshot (anim=%d) -- the death frame must be sent exactly once", e.Anim)
		}
	}

	// A finished match takes no further snapshot, so nothing dead may be left
	// behind for ever.
	if len(b.units) != 0 {
		t.Fatalf("%d casualties left on the board after the match ended", len(b.units))
	}
	t.Logf("death frame: one snapshot carried anim=%d hp=0 for entity %d, then it was reaped", AnimDie, k.ID)
}

// mustBattleWith builds a battle against an explicit table.
func mustBattleWith(t *testing.T, table CardTable, deckA, deckB []int32, seed int64) *Battle {
	t.Helper()
	b, err := NewBattle(Config{Seed: seed, Table: table, DeckA: deckA, DeckB: deckB})
	if err != nil {
		t.Fatalf("NewBattle: %v", err)
	}
	return b
}
