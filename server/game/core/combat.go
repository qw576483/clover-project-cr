package core

// The attack cycle, damage, projectiles, spells, death payloads and collision.
//
// An attack is not "deal damage every HitSpeed ticks". It is a small state
// machine whose details decide interactions (cr-sim engine/combat.py:1):
// LoadTimeMs is the windup before the first hit after engaging, HitSpeedMs is
// the interval between later hits, and switching target restarts the windup.
//
// Deciding a hit and applying it are deliberately separated. If damage landed
// inline, the outcome of a fight would depend on the order entities happen to
// sit in the list, and two identical units placed symmetrically would produce a
// winner. Collecting the tick's hits and applying them together makes a tick
// simultaneous, so a mirror match trades evenly.

const (
	// touchToleranceMilli is how much overlap is ignored before separation is
	// forced; a small tolerance stops jitter between units that are merely
	// touching. cr-sim _TOUCH_TOLERANCE is 60 subtiles = 3.33 milli-tiles.
	touchToleranceMilli = 3
	// immovableMass is the effective mass of anything that cannot be pushed:
	// buildings and towers are static, and the reference treats a unit flagged
	// ignore-pushback the same way.
	immovableMass = 1 << 20
	// collisionPasses bounds the separation relaxation. In a dense crowd
	// separating one pair creates another overlap, so iterating to convergence
	// would make a tick's cost depend on how crowded the board is -- exactly
	// what a simulator cannot afford (cr-sim engine/movement.py:134).
	collisionPasses = 3
	// maxProjectileFlightMs is a hard cap so a mistake in the homing maths can
	// never strand a shot in the air forever (cr-sim engine/projectiles.py:208).
	maxProjectileFlightMs = 30000
)

// ---------------------------------------------------------------------------
// Attack cycle
// ---------------------------------------------------------------------------

// pendingHit is a hit that has been *decided* this tick but not yet applied.
type pendingHit struct {
	attacker *entity
	target   *entity
	damage   int32
}

// advanceAttack advances one entity's attack cycle by one tick and returns the
// hit it decided, if any.
func advanceAttack(e *entity, target *entity) *pendingHit {
	loadTimeMs := e.Def.LoadTimeMs
	hitSpeedMs := e.Def.HitSpeedMs
	if hitSpeedMs < MSecPerTick {
		// A zero hit speed would otherwise swing every tick; the reference
		// clamps this too (`max(1, ticks)`).
		hitSpeedMs = MSecPerTick
	}

	// Switching target restarts the windup: that is what makes distraction
	// powerful.
	e.st.engage(target.ID, loadTimeMs)

	if e.st.cooldownMs > 0 {
		e.st.cooldownMs -= MSecPerTick
		if e.st.cooldownMs > 0 {
			return nil
		}
	}

	// The windup finished on this tick, so this entity swings.
	e.st.loaded = true
	e.st.cooldownMs = hitSpeedMs
	e.swings++
	e.setAnim(AnimAttack)
	return &pendingHit{attacker: e, target: target, damage: e.Def.Damage}
}

// applyHit lands a decided hit. Its target may already have died this tick.
func (b *Battle) applyHit(h *pendingHit) {
	if h == nil || h.target == nil || !h.target.alive {
		return
	}
	b.damageEntity(h.target, h.damage, h.attacker)
}

// damageEntity applies damage, which is the single funnel for every source of
// harm in the match (attacks, projectiles, spells, death payloads).
//
// A target that reaches zero hitpoints is marked dead here and queued for the
// death phase; the payload is not resolved inline so that this tick's other
// hits still land (a simultaneous tick).
func (b *Battle) damageEntity(target *entity, amount int32, src *entity) {
	if target == nil || !target.alive || amount <= 0 {
		return
	}
	target.hp -= amount
	if target.hp <= 0 {
		target.hp = 0
		target.alive = false
		target.setAnim(AnimDie)
		b.pendingDeaths = append(b.pendingDeaths, target)
	}
	// Being attacked provokes the king tower (参考规格 §4.1 激活机制).
	if target.Kind == KindTower && isKingDef(target.Def) {
		if k := b.kings[target.Team]; k != nil {
			k.arm(b.serverMs)
		}
	}
	_ = src
}

// ---------------------------------------------------------------------------
// Projectiles
// ---------------------------------------------------------------------------

// projectile is a shot in flight.
//
// A non-homing shot stores the point it was aimed at and commits to it; a
// homing one re-reads its target's position every tick. Speed is the
// projectile's own tiles-per-minute converted the same way a unit's is
// (参考规格 §5 投射物).
type projectile struct {
	id       int32
	ownerID  int32
	team     Team
	damage   int32
	x, y     int32
	srcX     int32
	srcY     int32
	destX    int32
	destY    int32
	targetID int32
	homing   bool
	// speedMilliPerSec is in milli-tiles per second.
	speedMilliPerSec int32
	// travelledFine counts milli-tiles x TicksPerSecond, so a 600-speed shot's
	// 500 milli-tiles per tick is exact.
	travelledFine int32
	totalFine     int32
	ageMs         int32
}

// fireProjectile spawns a shot from src toward target.
//
// If the projectile key does not resolve in the table, the shot resolves in
// place instead of being silently dropped: core has no logger (task doc §7),
// and losing the damage would be a much worse failure than losing the travel
// time. Every projectile key in the 60-card pool resolves, because
// 战斗单位_cs carries the projectile rows (步骤文档 §4.2).
func (b *Battle) fireProjectile(src *entity, target *entity, damage int32) {
	if src == nil || target == nil || !target.alive {
		return
	}
	var pdef *UnitDef
	if src.Def != nil && src.Def.ProjectileKey != "" {
		if d, ok := b.cfg.Table.Unit(src.Def.ProjectileKey); ok {
			pdef = d
		}
	}
	if pdef == nil || pdef.SpeedMilliPerSec <= 0 {
		// Resolve inline (documented degradation).
		b.damageEntity(target, damage, src)
		return
	}
	p := &projectile{
		id:               b.nextID,
		ownerID:          src.ID,
		team:             src.Team,
		damage:           damage,
		x:                src.xMilli,
		y:                src.yMilli,
		srcX:             src.xMilli,
		srcY:             src.yMilli,
		destX:            target.xMilli,
		destY:            target.yMilli,
		targetID:         target.ID,
		homing:           true,
		speedMilliPerSec: pdef.SpeedMilliPerSec,
	}
	b.nextID++
	p.totalFine = Distance(p.x, p.y, p.destX, p.destY) * TicksPerSecond
	if p.totalFine <= 0 {
		b.damageEntity(target, damage, src)
		return
	}
	b.projectiles = append(b.projectiles, p)
}

// advance flies one tick and reports whether the shot arrived.
//
// A homing shot re-aims from **where it currently is**, not from where it was
// fired: re-measuring from the launch point lets a retreating target outrun the
// remaining distance forever and the shot never lands
// (cr-sim engine/projectiles.py:239). If the target dies mid-flight the shot
// keeps going to where it last was and is spent there.
func (b *Battle) advanceProjectile(p *projectile) bool {
	p.ageMs += MSecPerTick
	if p.ageMs > maxProjectileFlightMs {
		return true
	}
	if p.homing {
		if t := b.entityByID(p.targetID); t != nil && t.alive {
			p.srcX, p.srcY = p.x, p.y
			p.destX, p.destY = t.xMilli, t.yMilli
			p.travelledFine = 0
			p.totalFine = Distance(p.x, p.y, p.destX, p.destY) * TicksPerSecond
		}
	}
	if p.totalFine <= 0 {
		return true
	}
	p.travelledFine += p.speedMilliPerSec
	if p.travelledFine >= p.totalFine {
		p.x, p.y = p.destX, p.destY
		return true
	}
	p.x, p.y = PointAlong(p.srcX, p.srcY, p.destX, p.destY, p.travelledFine, p.totalFine)
	return false
}

// ---------------------------------------------------------------------------
// Spell area effects
// ---------------------------------------------------------------------------

// areaFx is a spell cloud that keeps touching whatever is inside it
// (参考规格 §5 法术: duration / damage per tick / heal per second).
type areaFx struct {
	team          Team
	casterID      int32
	x, y          int32
	radiusMilli   int32
	remainingMs   int32
	tickMs        int32
	accMs         int32
	damagePerTick int32
	// healPerSecond is accumulated exactly in healAccNum (milli-HP per 1000 ms)
	// so no fraction is lost to integer division.
	healPerSecond int32
	healAccNum    int32
}

// tick advances the cloud by one tick and returns the damage/heal events it
// produced.
func (b *Battle) tickAreaFx(fx *areaFx) {
	fx.remainingMs -= MSecPerTick
	if fx.tickMs <= 0 {
		return
	}
	fx.accMs += MSecPerTick
	if fx.accMs < fx.tickMs {
		return
	}
	fx.accMs -= fx.tickMs
	for _, e := range b.allEntities() {
		if !e.alive || e.deploying() {
			continue
		}
		if DistanceSq(fx.x, fx.y, e.xMilli, e.yMilli) > int64(fx.radiusMilli)*int64(fx.radiusMilli) {
			continue
		}
		if e.Team == fx.team {
			if fx.healPerSecond > 0 && e.Kind != KindTower && e.hp < e.maxHP {
				fx.healAccNum += fx.healPerSecond * fx.tickMs
				heal := fx.healAccNum / 1000
				fx.healAccNum %= 1000
				e.hp = ClampI32(e.hp+heal, 0, e.maxHP)
			}
			continue
		}
		if fx.damagePerTick > 0 {
			b.damageEntity(e, fx.damagePerTick, nil)
		}
	}
}

// ---------------------------------------------------------------------------
// Spells
// ---------------------------------------------------------------------------

// castSpell applies a spell card at a point.
func (b *Battle) castSpell(team Team, card *CardDef, x, y int32) {
	sp := card.Spell
	if sp == nil {
		return
	}
	// 1) instant area damage
	if sp.InstantDamage > 0 && sp.RadiusMilli > 0 {
		for _, e := range b.allEntities() {
			if !e.alive || e.Team == team || e.deploying() {
				continue
			}
			if DistanceSq(x, y, e.xMilli, e.yMilli) > int64(sp.RadiusMilli)*int64(sp.RadiusMilli) {
				continue
			}
			b.damageEntity(e, sp.InstantDamage, nil)
		}
	}
	// 2) knockback: away from the cast point (参考规格 §5 击退)
	if sp.Pushback > 0 {
		for _, e := range b.allEntities() {
			if !e.alive || e.Team == team || e.deploying() || e.immovable() {
				continue
			}
			if DistanceSq(x, y, e.xMilli, e.yMilli) > int64(sp.RadiusMilli)*int64(sp.RadiusMilli) {
				continue
			}
			nx, ny := PushAway(x, y, e.xMilli, e.yMilli, sp.Pushback)
			b.moveEntity(e, nx, ny)
		}
	}
	// 3) lingering cloud
	if sp.DurationMs > 0 && (sp.DamagePerTick > 0 || sp.HealPerSecond > 0) {
		tickMs := sp.TickMs
		if tickMs <= 0 {
			tickMs = MSecPerTick
		}
		b.areas = append(b.areas, &areaFx{
			team:          team,
			x:             x,
			y:             y,
			radiusMilli:   sp.RadiusMilli,
			remainingMs:   sp.DurationMs,
			tickMs:        tickMs,
			damagePerTick: sp.DamagePerTick,
			healPerSecond: sp.HealPerSecond,
		})
	}
	// 4) deploy whatever the spell delivers (Goblin Barrel, Rage's bottle...)
	if sp.SpawnKey != "" {
		if def, ok := b.cfg.Table.Unit(sp.SpawnKey); ok && def != nil {
			b.spawnGroup(team, def, card.ID, x, y, sp.SpawnN, sp.SpawnRadiusMilli, def.DeployMs)
		}
	}
}

// ---------------------------------------------------------------------------
// Spawning
// ---------------------------------------------------------------------------

// spawnGroup places n units on a ring of radiusMilli around a point.
//
// A group card does not drop its units on one point: `summon_radius` spaces
// them out (参考规格 §5 群体卡). Offsets are computed with the same ring layout
// the reference uses.
func (b *Battle) spawnGroup(team Team, def *UnitDef, cardID int32, x, y int32, n int32, radiusMilli int32, deployMs int32) []*entity {
	if n < 1 {
		n = 1
	}
	offsets := RingOffsets(int(n), radiusMilli)
	out := make([]*entity, 0, n)
	for _, off := range offsets {
		ux := ClampI32(x+off[0], 0, ArenaWMilli-1)
		uy := ClampI32(y+off[1], 0, ArenaHMilli-1)
		e := b.spawnUnit(team, def, cardID, ux, uy, deployMs)
		out = append(out, e)
	}
	return out
}

// spawnUnit creates one battle entity and registers it.
func (b *Battle) spawnUnit(team Team, def *UnitDef, cardID int32, x, y int32, deployMs int32) *entity {
	e := &entity{
		ID:           b.nextID,
		Kind:         def.Kind,
		CardID:       cardID,
		Team:         team,
		Def:          def,
		xMilli:       x,
		yMilli:       y,
		hp:           def.HP,
		maxHP:        def.HP,
		alive:        true,
		deployMsLeft: deployMs,
		facing:       facingFor(team),
		anim:         AnimIdle,
		flying:       isFlyingDef(def),
	}
	if def.LifeMs > 0 {
		e.lifeMsLeft = def.LifeMs
	}
	if def.SpawnKey != "" && def.SpawnIntervalMs > 0 {
		e.spawnTimerMs = def.SpawnIntervalMs
	}
	b.nextID++
	b.byID[e.ID] = e
	if def.Kind == KindTower {
		b.towers = append(b.towers, e)
	} else {
		b.units = append(b.units, e)
	}
	b.events = append(b.events, Event{
		Kind: EvSpawn, CardID: cardID, XMilli: x, YMilli: y, EntityID: e.ID, Team: int32(team),
	})
	return e
}

// facingFor is the initial facing of a freshly deployed unit: it looks toward
// the enemy half.
func facingFor(team Team) int32 {
	if team == TeamBlue {
		return 1
	}
	return -1
}

// ---------------------------------------------------------------------------
// Death
// ---------------------------------------------------------------------------

// resolveDeath handles one dead entity: crown credit, king provocation and the
// death payload (亡语 / 分裂).
func (b *Battle) resolveDeath(e *entity) {
	other := e.Team.Other()
	if e.Kind == KindTower {
		if isKingDef(e.Def) {
			// Destroying the king is an instant three-crown win (参考规格 §3).
			b.crowns[other] = 3
		} else {
			b.crowns[other]++
			// One of its own princess towers falling provokes the king tower.
			if k := b.kings[e.Team]; k != nil {
				k.arm(b.serverMs)
			}
		}
		b.events = append(b.events, Event{
			Kind: EvTowerDestroyed, XMilli: e.xMilli, YMilli: e.yMilli,
			EntityID: e.ID, Team: int32(e.Team),
		})
	} else {
		b.events = append(b.events, Event{
			Kind: EvDeath, XMilli: e.xMilli, YMilli: e.yMilli,
			EntityID: e.ID, Team: int32(e.Team),
		})
	}

	if e.Def == nil {
		return
	}
	// Area damage on death (骷髅巨人的爆炸).
	if e.Def.DeathDamage > 0 && e.Def.DeathAoeRadiusMilli > 0 {
		r2 := int64(e.Def.DeathAoeRadiusMilli) * int64(e.Def.DeathAoeRadiusMilli)
		for _, t := range b.allEntities() {
			if !t.alive || t.Team == e.Team || t.deploying() {
				continue
			}
			if DistanceSq(e.xMilli, e.yMilli, t.xMilli, t.yMilli) > r2 {
				continue
			}
			b.damageEntity(t, e.Def.DeathDamage, e)
		}
	}
	// Spawn on death (天狗分裂、气球兵掉落).
	if e.Def.DeathSpawnKey != "" {
		if def, ok := b.cfg.Table.Unit(e.Def.DeathSpawnKey); ok && def != nil {
			b.spawnGroup(e.Team, def, e.CardID, e.xMilli, e.yMilli, e.Def.DeathSpawnN, def.RadiusMilli*2, def.DeployMs)
		}
	}
}

// ---------------------------------------------------------------------------
// Movement helpers and collision
// ---------------------------------------------------------------------------

// moveEntity moves an entity, refusing to put it somewhere it cannot stand.
//
// On failure it tries each axis alone so an entity slides along an obstacle
// rather than sticking to it -- otherwise crowds jam solid against the river
// bank (cr-sim engine/movement.py:113).
func (b *Battle) moveEntity(e *entity, x, y int32) bool {
	if x == e.xMilli && y == e.yMilli {
		return false
	}
	flying := e.flying
	if b.arena.IsWalkable(x, y, flying) {
		e.setPos(x, y)
		return true
	}
	if b.arena.IsWalkable(x, e.yMilli, flying) {
		e.setPos(x, e.yMilli)
		return true
	}
	if b.arena.IsWalkable(e.xMilli, y, flying) {
		e.setPos(e.xMilli, y)
		return true
	}
	// Slide: try a unit step along each axis so a unit pressed against a corner
	// still makes progress.
	stepX := signI32(x - e.xMilli)
	stepY := signI32(y - e.yMilli)
	if stepX != 0 && b.arena.IsWalkable(e.xMilli+stepX, e.yMilli, flying) {
		e.setPos(e.xMilli+stepX, e.yMilli)
		return true
	}
	if stepY != 0 && b.arena.IsWalkable(e.xMilli, e.yMilli+stepY, flying) {
		e.setPos(e.xMilli, e.yMilli+stepY)
		return true
	}
	return false
}

// effectiveMass is how strongly an entity resists being displaced.
func effectiveMass(e *entity) int32 {
	if e.immovable() {
		return immovableMass
	}
	return e.mass()
}

// separate pushes two overlapping entities apart. It reports whether anything
// moved. Displacement splits by inverse mass, so the lighter unit gives way.
func separate(a *Arena, e1, e2 *entity) bool {
	limit := e1.radiusMilli() + e2.radiusMilli()
	if limit <= 0 {
		return false
	}
	dx := e2.xMilli - e1.xMilli
	dy := e2.yMilli - e1.yMilli
	gap := int32(isqrt64(DistanceSq(e1.xMilli, e1.yMilli, e2.xMilli, e2.yMilli)))
	overlap := limit - gap
	if overlap <= touchToleranceMilli {
		return false
	}
	if gap == 0 {
		// Coincident: no direction to separate along, so pick one by entity id.
		// Arbitrary, but deterministic, which is what matters.
		if e1.ID < e2.ID {
			dx, dy, gap = limit, 0, limit
		} else {
			dx, dy, gap = -limit, 0, limit
		}
	}
	m1 := effectiveMass(e1)
	m2 := effectiveMass(e2)
	total := m1 + m2
	if total >= 2*immovableMass {
		return false // two immovables; nothing to do
	}
	share1 := overlap * m2 / total
	share2 := overlap - share1

	moved := false
	if m1 < immovableMass && share1 != 0 {
		moved = shiftEntity(a, e1, -dx*share1/gap, -dy*share1/gap) || moved
	}
	if m2 < immovableMass && share2 != 0 {
		moved = shiftEntity(a, e2, dx*share2/gap, dy*share2/gap) || moved
	}
	return moved
}

// shiftEntity moves an entity by a delta, refusing to put it in terrain.
func shiftEntity(a *Arena, e *entity, dx, dy int32) bool {
	if dx == 0 && dy == 0 {
		return false
	}
	x := e.xMilli + dx
	y := e.yMilli + dy
	if a.IsWalkable(x, y, e.flying) {
		e.setPos(x, y)
		return true
	}
	if a.IsWalkable(e.xMilli+dx, e.yMilli, e.flying) {
		e.setPos(e.xMilli+dx, e.yMilli)
		return true
	}
	if a.IsWalkable(e.xMilli, e.yMilli+dy, e.flying) {
		e.setPos(e.xMilli, e.yMilli+dy)
		return true
	}
	return false
}

// signI32 returns -1, 0 or 1.
func signI32(v int32) int32 {
	if v < 0 {
		return -1
	}
	if v > 0 {
		return 1
	}
	return 0
}
