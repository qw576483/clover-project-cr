package core

import (
	"errors"
	"fmt"
	"math/rand"
)

// Battle phases, matching Snapshot.Phase.
const (
	PhaseNormal   int32 = 0
	PhaseOvertime int32 = 1
	PhaseEnded    int32 = 2
)

// Sentinel errors returned by PlayCard / NewBattle.
//
// core has no logger: an unexpected branch is expressed as an
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
// Public accessors
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

// PlayCard is the single entry point for player input (ai.go
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
		b.spawnGroup(team, def, card.ID, xMilli, yMilli, card.UnitN, card.UnitRadiusMilli, deployMs, card.UnitStaggerMs)
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
		// ★ 2026-09-23 重写（差异登记 D142/D147）：三个字段的语义按官方数据 +
		// 参考实现 `cr_sim/engine/battle.py::_phase_run_spawners` 的文档字符串逐字对齐 ——
		//   `SpawnPauseTime`（本表的 SpawnIntervalMs）= **波间隔**（哥布林小屋 10000 /
		//        野蛮人小屋 14000 / 骷髅墓碑 3500 / 女巫 7000）；
		//   `SpawnNumber`（SpawnN）                    = **每波个数**；
		//   `SpawnInterval`（SpawnStaggerMs）          = **波内错开**的 ms（小屋 500）。
		// 旧实现把 `SpawnInterval`（500 ms）当成波周期，于是哥布林小屋在 29 s 生命期里
		// 吐了 **174 只**（原版约 6 只，29×）。
		//
		// 参考里还有两条硬规则，这里一并落地：
		//   · 没落地（还在 deploy 中）的建筑不产兵；
		//   · **没有 `SpawnPauseTime` 就只是一波**，不是"无限快"（参考原文：
		//     "No SpawnPauseTime means one wave, not an infinitely fast one."）
		//     ⇒ 用 spawnDone 标记一次性，而不是把周期退回 1 tick。
		if e.Def.SpawnKey == "" || e.spawnDone {
			continue
		}
		if e.deploying() {
			continue
		}
		e.spawnTimerMs -= MSecPerTick
		if e.spawnTimerMs > 0 {
			continue
		}
		def, ok := b.cfg.Table.Unit(e.Def.SpawnKey)
		if !ok || def == nil {
			continue
		}
		n := e.Def.SpawnN
		if n < 1 {
			n = 1
		}
		// spawn_limit 卡的是**场上存活子代数**，⛔ 不是累计生成数：
		// 参考 `room = spawn_limit - len(living)`。0 = 不卡（三个小屋的官方值都是 0）。
		if e.Def.SpawnLimit > 0 {
			room := int32(e.Def.SpawnLimit) - int32(b.livingSpawnChildren(e))
			if room <= 0 {
				b.rescheduleSpawn(e)
				continue
			}
			if room < n {
				n = room
			}
		}
		born := b.spawnGroup(e.Team, def, e.CardID, e.xMilli, e.yMilli, n,
			e.Def.SpawnRadiusMilli, def.DeployMs, e.Def.SpawnStaggerMs)
		for _, u := range born {
			e.spawnChildren = append(e.spawnChildren, u.ID)
		}
		e.spawnedCount += n
		b.rescheduleSpawn(e)
	}
}

// livingSpawnChildren counts how many of a building's children are still on
// the board (参考 `_spawn_children` 的存活过滤).
func (b *Battle) livingSpawnChildren(e *entity) int {
	if len(e.spawnChildren) == 0 {
		return 0
	}
	kept := e.spawnChildren[:0]
	for _, id := range e.spawnChildren {
		c := b.byID[id]
		if c != nil && c.alive {
			kept = append(kept, id)
		}
	}
	e.spawnChildren = kept
	return len(kept)
}

// rescheduleSpawn sets the timer for the next wave, or marks a one-shot
// spawner as spent.
func (b *Battle) rescheduleSpawn(e *entity) {
	if e.Def.SpawnIntervalMs > 0 {
		e.spawnTimerMs += e.Def.SpawnIntervalMs
		return
	}
	e.spawnDone = true
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
		// ★ D145：塔开火 —— 发一条 EvTowerShoot（塔不在快照 entities 里，客户端
		// 除此之外没有"这一帧这座塔开火了"的信息源）。
		// 近战塔（无投射物行）也发：客户端只播枪口闪光、不飞弹道（由 ProjSpeed==0 表达）。
		if h.attacker.Kind == KindTower {
			b.emitTowerShoot(h.attacker)
		}
		// A ranged attacker fires a projectile; a melee one lands the blow.
		if h.attacker.Def.ProjectileKey != "" {
			b.fireProjectile(h.attacker, h.target, h.damage)
			continue
		}
		b.applyHit(h)
	}
}

// emitTowerShoot appends the EvTowerShoot event for one tower shot (D145).
//
// ProjSpeed 取该塔投射物行的 `SpeedTilesPerMinute`（格/分钟）；投射物行缺失 /
// 该塔本来是近战（无投射物）时为 0，客户端据此只播枪口闪光、不飞弹道。
func (b *Battle) emitTowerShoot(t *entity) {
	speed := int32(0)
	if t.Def.ProjectileKey != "" {
		if p, ok := b.cfg.Table.Unit(t.Def.ProjectileKey); ok && p != nil {
			speed = p.SpeedTilesPerMinute
		}
	}
	b.events = append(b.events, Event{
		Kind:      EvTowerShoot,
		EntityID:  t.ID,
		XMilli:    t.xMilli,
		YMilli:    t.yMilli,
		Team:      int32(t.Team),
		ProjSpeed: speed,
	})
}

// stepProjectiles flies every shot and resolves arrivals.
func (b *Battle) stepProjectiles() {
	if len(b.projectiles) == 0 {
		return
	}
	kept := b.projectiles[:0]
	for _, p := range b.projectiles {
		if b.advanceProjectile(p) {
			src := b.entityByID(p.ownerID)
			t := b.entityByID(p.targetID)
			// ★ 2026-09-23 增：**落点溅射**（差异登记 D142）。官方把溅射半径放在
			// 投射物行的 `radius` 上（法师 1500 / 屠夫 1000 / 滚石 1800 / 炸弹兵 1500 /
			// 公主 2000 / 火精灵 2300 …），旧实现的 `aoe_radius_mt` 列全 0 ⇒ 这些卡的
			// 溅射一发不剩。中心取**到达点** (p.x,p.y)，⛔ 不是开火点：目标中途死亡时
			// 落点就是它最后的位置，这是 advanceProjectile 的既有语义。
			if p.aoeRadius > 0 {
				var skip *entity
				if t != nil && t.alive {
					b.damageEntity(t, p.damage, src)
					skip = t
				}
				b.splashDamage(src, p.x, p.y, p.aoeRadius, p.damage, skip)
			} else if t != nil && t.alive {
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

// resolveCollisions separates every overlapping pair.
//
// 每 tick 先把所有重叠对的分离位移按**向量**累加到各实体头上
// （`accumulateSeparation`），再统一按"每 tick 位移上限"收缩后落地
// （`applySeparation`，额度见 `separationStepLimitMilli`；落地前还会按
// **同伴数取平均**做欠松弛，推导见 combat.go 头部 `collisionPasses` 注释）。
//
// ⚠️ 为什么不再是"固定 N 轮、每轮逐对立即施加"（2026-09-24 改）：
// 逐对立即施加时，被夹在两只同伴中间的单位会先被一侧推 +d、再被另一侧推 −d，
// 两次位移不抵消（还被标量额度裁掉反向那份）⇒ 永久 limit cycle。
// 改成"向量累加 + 取平均"后反向推力自然抵消、耦合增益恒为 0.5；
// 剩余的收敛交给**逐 tick** 的松弛（每 tick 一轮），不再需要 tick 内多轮。
func (b *Battle) resolveCollisions() {
	all := b.allEntities()
	d := make([]sepDelta, len(all))
	nbr := make([]int32, len(all))
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
			if accumulateSeparation(a, c, &d[i], &d[j]) {
				nbr[i]++
				nbr[j]++
			}
		}
	}
	for i, e := range all {
		if !e.alive || e.deploying() {
			continue
		}
		applySeparation(b.arena, e, d[i], int(nbr[i]))
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
