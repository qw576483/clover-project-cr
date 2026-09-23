package core

import (
	"errors"
	"fmt"
	"math/rand"
)

// Battle phases, matching Snapshot.Phase (task doc §4.2).
const (
	PhaseNormal   int32 = 0
	PhaseOvertime int32 = 1
	PhaseEnded    int32 = 2
)

// Sentinel errors returned by PlayCard / NewBattle.
//
// core has no logger (task doc §7): an unexpected branch is expressed as an
// error here and as an Event where the client needs to see it.
var (
	ErrNoTable           = errors.New("core: CardTable is nil")
	ErrBadDeck           = errors.New("core: a deck must hold exactly 8 cards")
	ErrUnknownCard       = errors.New("core: unknown card id")
	ErrMissingUnitDef    = errors.New("core: card's unit key does not resolve in the table")
	ErrMissingTowerDef   = errors.New("core: crown tower def does not resolve in the table")
	ErrBadTeam           = errors.New("core: invalid team")
	ErrBattleEnded       = errors.New("core: battle already ended")
	ErrCardNotInHand     = errors.New("core: card is not in hand")
	ErrNotEnoughElixir   = errors.New("core: not enough elixir")
	ErrOutsideDeployZone = errors.New("core: point is outside the deploy zone")
)

// Config is a battle's setup. Decks hold 8 card ids each (参考规格 §7 S11).
type Config struct {
	Seed  int64
	Table CardTable
	DeckA []int32
	DeckB []int32
}

// Battle is one authoritative match simulation.
type Battle struct {
	cfg   Config
	arena *Arena
	rng   *randSource

	serverMs int32
	tick     int32
	seq      int32

	hands  [2]*hand
	elixir [2]*elixirBar
	crowns [2]int32
	// elixirFullEdge tracks the "elixir full" event edge so it fires once per
	// filling rather than every tick.
	elixirFullEdge [2]bool

	kings  [2]*kingTower
	towers []*entity
	units  []*entity
	byID   map[int32]*entity
	nextID int32
	// roster is every crown tower the match started with, so a fallen tower
	// still contributes its own maximum to the HP-rate tiebreaker and still
	// opens its lane's deploy pocket (参考规格 §2.1 / §3).
	roster []towerEntry

	projectiles []*projectile
	areas       []*areaFx

	// pendingDeaths is this tick's casualties, resolved as a batch after every
	// source of damage has landed.
	pendingDeaths []*entity

	events []Event
	result Result
	ended  bool
	phase  int32
}

// NewBattle validates the configuration and builds the opening position: two
// decks shuffled, both elixir bars at 6, six crown towers on the board.
func NewBattle(cfg Config) (*Battle, error) {
	if cfg.Table == nil {
		return nil, ErrNoTable
	}
	if len(cfg.DeckA) != 8 || len(cfg.DeckB) != 8 {
		return nil, ErrBadDeck
	}
	for _, id := range append(append([]int32{}, cfg.DeckA...), cfg.DeckB...) {
		card, ok := cfg.Table.Card(id)
		if !ok || card == nil {
			return nil, fmt.Errorf("%w: %d", ErrUnknownCard, id)
		}
		if card.Kind == CardTypeSpell {
			if card.Spell == nil {
				return nil, fmt.Errorf("core: card %d is a spell with no SpellDef", id)
			}
			continue
		}
		if card.UnitKey == "" {
			return nil, fmt.Errorf("core: card %d has no unit_key", id)
		}
		if def, ok := cfg.Table.Unit(card.UnitKey); !ok || def == nil {
			return nil, fmt.Errorf("%w: card %d -> %q", ErrMissingUnitDef, id, card.UnitKey)
		}
	}
	princessDef, ok := resolveTowerDef(cfg.Table, KeyPrincessTower)
	if !ok {
		return nil, fmt.Errorf("%w: %v", ErrMissingTowerDef, towerKeyAliases[KeyPrincessTower])
	}
	kingDef, ok := resolveTowerDef(cfg.Table, KeyKingTower)
	if !ok {
		return nil, fmt.Errorf("%w: %v", ErrMissingTowerDef, towerKeyAliases[KeyKingTower])
	}

	b := &Battle{
		cfg:   cfg,
		arena: NewArena(),
		rng:   newRandSource(cfg.Seed),
		byID:  make(map[int32]*entity, 16),
		phase: PhaseNormal,
	}
	b.hands[TeamBlue] = newHand(cfg.DeckA, b.rng)
	b.hands[TeamRed] = newHand(cfg.DeckB, b.rng)
	b.elixir[TeamBlue] = newElixirBar(StartingElixirMilli)
	b.elixir[TeamRed] = newElixirBar(StartingElixirMilli)

	// Towers, in a fixed deterministic order: blue king, blue left, blue
	// right, red king, red left, red right (ids 1..6).
	for _, team := range []Team{TeamBlue, TeamRed} {
		for _, slot := range towerSlotsFor(team) {
			def := princessDef
			if slot.kind == TowerKindKing {
				def = kingDef
			}
			e := b.spawnUnit(team, def, 0, slot.x, slot.y, 0)
			b.roster = append(b.roster, towerEntry{
				team: team, kind: slot.kind, x: slot.x, y: slot.y,
				maxHP: e.maxHP, ent: e,
			})
			if slot.kind == TowerKindKing {
				b.kings[team] = &kingTower{entity: e}
			}
		}
	}
	// The opening board is the starting position, not a spawn burst.
	b.events = nil
	return b, nil
}

// ---------------------------------------------------------------------------
// Public accessors (task doc §4.1)
// ---------------------------------------------------------------------------

// Step advances the simulation by exactly one tick (50 ms).
func (b *Battle) Step() {
	if b.ended {
		return
	}
	b.tick++
	b.seq++
	b.serverMs = b.tick * MSecPerTick

	b.stepElixir()
	b.stepDeploy()
	b.stepBuildings()
	b.stepKingActivation()
	b.updateTargets()
	b.stepAttacks()
	b.stepProjectiles()
	b.stepAreas()
	b.stepMovement()
	b.resolveCollisions()
	b.stepDeaths()
	b.compact()
	b.checkVictory()
}

// PlayCard is the single entry point for player input (task doc §4.1: ai.go
// uses it too).
func (b *Battle) PlayCard(team Team, cardID, xMilli, yMilli int32) error {
	if !team.Valid() {
		return ErrBadTeam
	}
	if b.ended {
		return ErrBattleEnded
	}
	card, ok := b.cfg.Table.Card(cardID)
	if !ok || card == nil {
		return fmt.Errorf("%w: %d", ErrUnknownCard, cardID)
	}
	h := b.hands[team]
	if h == nil || !h.Has(cardID) {
		return fmt.Errorf("%w: %d", ErrCardNotInHand, cardID)
	}
	bar := b.elixir[team]
	if !bar.canAfford(card.Elixir) {
		return fmt.Errorf("%w: need %d have %d", ErrNotEnoughElixir, card.Elixir, bar.units())
	}

	// Deployment legality. A spell may be cast anywhere and over water; a
	// troop or building is bound to the deploy zone (参考规格 §2.1).
	anywhere, onWater := false, false
	if card.Kind == CardTypeSpell && card.Spell != nil {
		anywhere = card.Spell.Anywhere
		onWater = card.Spell.OnWater
	}
	if !b.arena.CanDeploy(team, xMilli, yMilli, anywhere, onWater, b.fallenEnemyPrincess(team)) {
		return fmt.Errorf("%w: (%d,%d)", ErrOutsideDeployZone, xMilli, yMilli)
	}

	if !bar.spend(card.Elixir) {
		return ErrNotEnoughElixir
	}
	if !h.play(cardID) {
		return fmt.Errorf("%w: %d", ErrCardNotInHand, cardID)
	}
	b.events = append(b.events, Event{
		Kind: EvPlayCard, CardID: cardID, XMilli: xMilli, YMilli: yMilli, Team: int32(team),
	})

	switch card.Kind {
	case CardTypeSpell:
		b.castSpell(team, card, xMilli, yMilli)
	default:
		def, _ := b.cfg.Table.Unit(card.UnitKey)
		if def == nil {
			return fmt.Errorf("%w: %q", ErrMissingUnitDef, card.UnitKey)
		}
		deployMs := card.DeployDelayMs
		if deployMs <= 0 {
			deployMs = def.DeployMs
		}
		b.spawnGroup(team, def, card.ID, xMilli, yMilli, card.UnitN, card.UnitRadiusMilli, deployMs)
	}
	return nil
}

// Surrender ends the match immediately with the other team as the winner.
func (b *Battle) Surrender(team Team) {
	if b.ended || !team.Valid() {
		return
	}
	b.finish(team.Other(), ReasonSurrender)
}

// Ended reports whether the match is over.
func (b *Battle) Ended() bool { return b.ended }

// Result returns the current result (Ended is false while the match runs).
func (b *Battle) Result() Result { return b.result }

// Elixir returns a team's elixir in 1/1000 units.
func (b *Battle) Elixir(team Team) int32 {
	if !team.Valid() {
		return 0
	}
	return b.elixir[team].amount()
}

// Hand returns a copy of a team's four visible cards.
func (b *Battle) Hand(team Team) []int32 {
	if !team.Valid() || b.hands[team] == nil {
		return nil
	}
	return b.hands[team].Hand()
}

// Next returns a team's next card.
func (b *Battle) Next(team Team) int32 {
	if !team.Valid() || b.hands[team] == nil {
		return 0
	}
	return b.hands[team].Next()
}

// Crowns returns how many crowns a team has scored.
func (b *Battle) Crowns(team Team) int32 {
	if !team.Valid() {
		return 0
	}
	return b.crowns[team]
}

// ServerMs returns the match clock.
func (b *Battle) ServerMs() int32 { return b.serverMs }

// Phase returns 0 = normal, 1 = overtime, 2 = ended.
func (b *Battle) Phase() int32 { return b.phase }

// ---------------------------------------------------------------------------
// Tick phases
// ---------------------------------------------------------------------------

// stepElixir regenerates both bars on the shared timeline and raises the
// "elixir full" event on the rising edge.
func (b *Battle) stepElixir() {
	for _, team := range []Team{TeamBlue, TeamRed} {
		bar := b.elixir[team]
		bar.regenerate(b.serverMs)
		full := bar.full()
		if full && !b.elixirFullEdge[team] {
			b.events = append(b.events, Event{Kind: EvElixirFull, Team: int32(team)})
		}
		b.elixirFullEdge[team] = full
	}
}

// stepDeploy burns down deploy delays. A unit that has just finished deploying
// emits nothing extra -- its spawn event was raised when it was placed.
func (b *Battle) stepDeploy() {
	for _, e := range b.allEntities() {
		if !e.alive || e.deployMsLeft <= 0 {
			continue
		}
		e.deployMsLeft -= MSecPerTick
		if e.deployMsLeft < 0 {
			e.deployMsLeft = 0
		}
	}
}

// stepBuildings ages buildings and runs their periodic spawners.
func (b *Battle) stepBuildings() {
	for _, e := range b.allEntities() {
		if !e.alive || e.Kind == KindTower || e.Def == nil {
			continue
		}
		if e.Def.LifeMs > 0 && e.lifeMsLeft > 0 {
			e.lifeMsLeft -= MSecPerTick
			if e.lifeMsLeft <= 0 {
				// A building that runs out of life disappears, and its death
				// payload still fires (that is how a Tombstone works).
				e.lifeMsLeft = 0
				e.hp = 0
				e.alive = false
				e.setAnim(AnimDie)
				b.pendingDeaths = append(b.pendingDeaths, e)
				continue
			}
		}
		if e.Def.SpawnKey == "" || e.Def.SpawnIntervalMs <= 0 {
			continue
		}
		if e.Def.SpawnLimit > 0 && e.spawnedCount >= e.Def.SpawnLimit {
			continue
		}
		e.spawnTimerMs -= MSecPerTick
		if e.spawnTimerMs > 0 {
			continue
		}
		e.spawnTimerMs += e.Def.SpawnIntervalMs
		def, ok := b.cfg.Table.Unit(e.Def.SpawnKey)
		if !ok || def == nil {
			continue
		}
		n := e.Def.SpawnN
		if n < 1 {
			n = 1
		}
		if e.Def.SpawnLimit > 0 && e.spawnedCount+n > e.Def.SpawnLimit {
			n = e.Def.SpawnLimit - e.spawnedCount
		}
		if n <= 0 {
			continue
		}
		b.spawnGroup(e.Team, def, e.CardID, e.xMilli, e.yMilli, n, e.Def.SpawnRadiusMilli, def.DeployMs)
		e.spawnedCount += n
	}
}

// stepKingActivation advances the lazy king towers' countdowns. It runs before
// the attack phase so a tower that finishes activating on tick N can fight on
// tick N -- "provoked at T, active exactly 3300 ms later".
func (b *Battle) stepKingActivation() {
	for _, team := range []Team{TeamBlue, TeamRed} {
		k := b.kings[team]
		if k == nil {
			continue
		}
		if k.tickActivation() {
			k.activeAtMs = b.serverMs
			b.events = append(b.events, Event{
				Kind: EvTowerActivated, EntityID: k.ID,
				XMilli: k.xMilli, YMilli: k.yMilli, Team: int32(team),
			})
		}
	}
}

// updateTargets keeps or re-acquires each entity's target.
//
// Targets are sticky: a unit does not re-choose every tick, it keeps its target
// until that target dies or leaves sight range (参考规格 §5 索敌).
func (b *Battle) updateTargets() {
	candidates := b.allEntities()
	for _, e := range candidates {
		if !e.alive || e.deploying() || e.Def == nil {
			continue
		}
		if e.Kind == KindTower {
			k := b.kings[e.Team]
			if isKingDef(e.Def) && (k == nil || !k.canFight()) {
				// A lazy king tower sees nothing until it is awake.
				e.targetID = 0
				e.st.disengage()
				continue
			}
		}
		if !e.Def.Attackable() {
			continue
		}
		cur := b.entityByID(e.targetID)
		if !keepTarget(e, cur) {
			cur = acquireTarget(e, candidates)
			if cur == nil {
				e.targetID = 0
				e.st.disengage()
				continue
			}
			e.targetID = cur.ID
		}
	}
}

// stepAttacks decides every hit for this tick and then applies them together,
// so the tick is simultaneous and results never depend on iteration order.
func (b *Battle) stepAttacks() {
	var hits []*pendingHit
	for _, e := range b.allEntities() {
		if !e.alive || e.deploying() || e.Def == nil || !e.Def.Attackable() {
			continue
		}
		if e.Kind == KindTower && isKingDef(e.Def) {
			k := b.kings[e.Team]
			if k == nil || !k.canFight() {
				continue
			}
		}
		target := b.entityByID(e.targetID)
		if target == nil || !target.acquirable() {
			continue
		}
		if !inAttackRange(e, target) {
			continue
		}
		if h := advanceAttack(e, target); h != nil {
			hits = append(hits, h)
		}
	}
	for _, h := range hits {
		if h.attacker == nil || h.attacker.Def == nil {
			continue
		}
		// A ranged attacker fires a projectile; a melee one lands the blow.
		if h.attacker.Def.ProjectileKey != "" {
			b.fireProjectile(h.attacker, h.target, h.damage)
			continue
		}
		b.applyHit(h)
	}
}

// stepProjectiles flies every shot and resolves arrivals.
func (b *Battle) stepProjectiles() {
	if len(b.projectiles) == 0 {
		return
	}
	kept := b.projectiles[:0]
	for _, p := range b.projectiles {
		if b.advanceProjectile(p) {
			if t := b.entityByID(p.targetID); t != nil && t.alive {
				src := b.entityByID(p.ownerID)
				b.damageEntity(t, p.damage, src)
			}
			continue
		}
		kept = append(kept, p)
	}
	b.projectiles = kept
}

// stepAreas ticks lingering spell clouds and drops the expired ones.
func (b *Battle) stepAreas() {
	if len(b.areas) == 0 {
		return
	}
	kept := b.areas[:0]
	for _, fx := range b.areas {
		b.tickAreaFx(fx)
		if fx.remainingMs > 0 {
			kept = append(kept, fx)
		}
	}
	b.areas = kept
}

// stepMovement walks everything that is not busy attacking.
func (b *Battle) stepMovement() {
	for _, e := range b.allEntities() {
		if !e.alive || e.deploying() || e.Kind == KindTower {
			continue
		}
		if e.Def == nil || e.Def.SpeedMilliPerSec <= 0 {
			// A building never moves.
			continue
		}
		target := b.entityByID(e.targetID)
		if target != nil && target.acquirable() && inAttackRange(e, target) {
			// In range: hold position and keep hitting.
			e.moving = false
			e.faceToward(target.xMilli)
			continue
		}
		var goalX, goalY int32
		have := false
		if target != nil && target.acquirable() {
			goalX, goalY, have = target.xMilli, target.yMilli, true
		} else if gx, gy, ok := marchGoal(b.arena, b.towers, e.Team, e.xMilli, e.yMilli); ok {
			goalX, goalY, have = gx, gy, true
		}
		if !have {
			e.moving = false
			e.setAnim(AnimIdle)
			continue
		}
		if e.routeGoalMoved(goalX, goalY) {
			e.routeTo(b.arena, goalX, goalY)
		}
		if e.nav.idx >= len(e.nav.waypoints) {
			e.moving = false
			e.setAnim(AnimIdle)
			continue
		}
		e.faceToward(goalX)
		e.advanceAlongRoute(e.Def.SpeedMilliPerSec)
		e.moving = true
		e.setAnim(AnimWalk)
	}
}

// resolveCollisions separates every overlapping pair, a fixed number of
// relaxation passes per tick.
func (b *Battle) resolveCollisions() {
	all := b.allEntities()
	for pass := 0; pass < collisionPasses; pass++ {
		moved := 0
		for i := 0; i < len(all); i++ {
			a := all[i]
			if !a.alive || a.deploying() {
				continue
			}
			for j := i + 1; j < len(all); j++ {
				c := all[j]
				if !c.alive || c.deploying() {
					continue
				}
				// Air and ground are separate layers.
				if a.flying != c.flying {
					continue
				}
				if separate(b.arena, a, c) {
					moved++
				}
			}
		}
		if moved == 0 {
			break
		}
	}
}

// stepDeaths resolves this tick's casualties as a batch.
func (b *Battle) stepDeaths() {
	if len(b.pendingDeaths) == 0 {
		return
	}
	deaths := b.pendingDeaths
	b.pendingDeaths = nil
	for _, e := range deaths {
		if e == nil {
			continue
		}
		b.resolveDeath(e)
		// Damage caused by a death payload may have killed more entities; those
		// join the next tick's batch (a bounded cascade).
	}
}

// compact removes dead entities from the live lists.
//
// Root cause it encodes (AV1): a casualty must NOT leave the list before a
// snapshot has carried it, or the client never sees anim=AnimDie and the unit
// teleports out of existence. Snapshot sets Entity.deathReported while it
// renders the death frame; only an already-reported casualty is reaped here.
// Waiting for the snapshot is also what makes the frame reliable: ticks are
// 20 Hz while snapshots are 10 Hz (logic.snapshotEveryTicks), so "reap at the
// end of the tick it died" would lose the death frame on every odd tick.
//
// A finished match takes no further snapshot, so everything dead goes at once
// (otherwise an un-reportable corpse would sit in the list for ever).
func (b *Battle) compact() {
	force := b.ended
	if len(b.units) > 0 {
		kept := b.units[:0]
		for _, e := range b.units {
			if e.alive || (!force && !e.deathReported) {
				kept = append(kept, e)
				continue
			}
			delete(b.byID, e.ID)
		}
		b.units = kept
	}
	if len(b.towers) > 0 {
		kept := b.towers[:0]
		for _, e := range b.towers {
			if e.alive || (!force && !e.deathReported) {
				kept = append(kept, e)
				continue
			}
			delete(b.byID, e.ID)
		}
		b.towers = kept
	}
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// allEntities returns towers then troops/buildings, in id order within each
// group. The order is fixed so iteration never depends on a map.
func (b *Battle) allEntities() []*entity {
	out := make([]*entity, 0, len(b.towers)+len(b.units))
	out = append(out, b.towers...)
	out = append(out, b.units...)
	return out
}

// entityByID finds a live or dead entity by id.
func (b *Battle) entityByID(id int32) *entity {
	if id == 0 {
		return nil
	}
	return b.byID[id]
}

// fallenEnemyPrincess lists the enemy princess towers that are already down,
// which is what widens the deploy zone (参考规格 §2.1).
//
// It reads the roster rather than the live tower list: a fallen tower has left
// the live list, and its pocket must stay open for the rest of the match.
func (b *Battle) fallenEnemyPrincess(team Team) []TowerRef {
	var out []TowerRef
	for _, t := range b.roster {
		if t.team == team || t.kind != TowerKindPrincess || t.ent == nil || t.ent.alive {
			continue
		}
		out = append(out, TowerRef{ID: t.ent.ID, Kind: TowerKindPrincess, XMilli: t.x, YMilli: t.y})
	}
	return out
}

// hpRate returns a team's remaining tower HP as a fraction of its own maximum,
// in ten-thousandths (参考规格 §3 胜负判定 ③).
//
// A destroyed tower is a perfectly valid answer: zero out of its own maximum.
func (b *Battle) hpRate(team Team) int32 {
	var hp, max int64
	for _, t := range b.roster {
		if t.team != team {
			continue
		}
		max += int64(t.maxHP)
		if t.ent != nil && t.ent.alive {
			hp += int64(t.ent.hp)
		}
	}
	if max <= 0 {
		return 0
	}
	return int32(hp * 10000 / max)
}

// towerEntry is one crown tower's identity, kept for the whole match.
type towerEntry struct {
	team  Team
	kind  int32
	x, y  int32
	maxHP int32
	ent   *entity
}

// randSource is a deterministic random source: the same seed must deal the same
// opening hand, which is what TestDeterminism pins.
type randSource struct{ r *rand.Rand }

// newRandSource builds a source from a seed.
func newRandSource(seed int64) *randSource {
	return &randSource{r: rand.New(rand.NewSource(seed))}
}

// shuffleInt32 shuffles a slice in place, deterministically for a given seed.
func (s *randSource) shuffleInt32(v []int32) {
	s.r.Shuffle(len(v), func(i, j int) { v[i], v[j] = v[j], v[i] })
}
