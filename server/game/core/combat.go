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
	// maxProjectileFlightMs is how long a projectile may stay in the air
	// before it is treated as lost.
	maxProjectileFlightMs = 30000
)

// ---------------------------------------------------------------------------
// Attack cycle
// ---------------------------------------------------------------------------

// attackAnimHoldTicks is how long the AnimAttack tier stays set after the tick whose
// swing lands, counted in ticks.
//
// A swing is decided on exactly one tick, but the client only ever observes the
// simulation through the snapshot stream, which runs at 10 Hz -- a project convention
// (game/logic `snapshotEveryTicks = 2` at TicksPerSecond = 20, i.e. one snapshot every
// 100 ms, pinned by GameConst.SnapshotIntervalMs on the client). A one-tick tier is
// therefore invisible whenever the swing tick is not a snapshot tick, and the client
// renders a unit that attacks forever without ever playing an attack animation.
//
// Two bounds fix the value:
//
//   - At least two snapshot periods (>= 4 ticks = 200 ms). Four consecutive ticks hold
//     two even and two odd ticks, so on a 2-tick snapshot grid at least one snapshot of
//     every swing falls inside the window regardless of phase.
//   - Strictly below the unit's own hit_speed_ms. A window as long as the attack interval
//     would merge consecutive swings into one unbroken AnimAttack run; the client restarts
//     its attack clip on the rising edge of the tier (View/UnitView.Apply), so merged
//     swings would each be played as a single attack. holdAttackAnim lowers the window
//     for any unit whose hit speed is shorter than this constant.
const attackAnimHoldTicks int32 = 4

// pendingHit is a hit that has been *decided* this tick but not yet applied.
type pendingHit struct {
	attacker *entity
	target   *entity
	damage   int32
}

// advanceAttack advances one entity's attack cycle by one tick and returns the
// hit it decided, if any. `tick` is the battle tick this call belongs to (it fixes the
// attack animation window, see holdWindupAnim / holdAttackAnim).
func advanceAttack(e *entity, target *entity, tick int32) *pendingHit {
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
		// The windup is the last LoadTimeMs before the swing, and the tier is
		// raised for all of it: that window is the only signal the snapshot
		// stream carries for "this unit is winding up", and the client starts
		// the attack clip (and its shot) off its rising edge. The damage is not
		// moved forward with it -- the hit is still decided below, on the tick
		// the cooldown reaches zero (cr-sim engine/combat.py:6-18).
		if e.st.cooldownMs <= loadTimeMs {
			e.holdWindupAnim(tick, ticksUntilSwing(e.st.cooldownMs))
		}
		e.st.cooldownMs -= MSecPerTick
		if e.st.cooldownMs > 0 {
			return nil
		}
	}

	// The windup finished on this tick, so this entity swings.
	e.st.loaded = true
	e.st.cooldownMs = hitSpeedMs
	e.swings++
	e.holdAttackAnim(tick)
	return &pendingHit{attacker: e, target: target, damage: e.Def.Damage}
}

// ticksUntilSwing is how many ticks a cooldown of cooldownMs still needs before
// its swing: the swing lands on tick `tick + ticksUntilSwing`, i.e. on the tick
// whose decrement takes the countdown to zero or below.
func ticksUntilSwing(cooldownMs int32) int32 {
	if cooldownMs <= 0 {
		return 0
	}
	return (cooldownMs - 1) / MSecPerTick
}

// holdWindupAnim raises the attack tier for a windup whose swing lands
// `ticksToSwing` ticks from now: the tier stays set through the swing and its hold
// window, so the window is `[windup start, swing + hold)`.
//
// A window that is still open is left alone: the client tells a fresh swing apart
// from an ongoing one by "the tier was released, then AnimAttack again"
// (View/UnitView.Apply holds a finished attack clip and replays it on that edge),
// so a windup at least as long as the attack interval -- Inferno Tower's beam, a
// hit speed shorter than its load -- must wait for the previous window to close
// instead of extending it. On such a unit the tier therefore stays up until the
// window ends and the next windup re-raises it one tick later.
func (e *entity) holdWindupAnim(tick, ticksToSwing int32) {
	if e.animHoldUntilTick >= tick {
		return
	}
	e.anim = AnimAttack
	e.animHoldUntilTick = tick + ticksToSwing + e.attackAnimHold()
}

// holdAttackAnim raises the attack tier and opens its hold window (see
// attackAnimHoldTicks): the tier stays set on ticks `[tick, tick+hold)`.
//
// The window is always strictly shorter than the entity's own hit speed, so two
// consecutive swings are separated by at least one tick of walk/idle -- that gap is what
// lets the client see each swing as its own attack.
func (e *entity) holdAttackAnim(tick int32) {
	e.anim = AnimAttack
	e.animHoldUntilTick = tick + e.attackAnimHold()
}

// attackAnimHold is the hold window length in ticks for this entity's own hit
// speed: attackAnimHoldTicks, lowered to `hit speed - 1` for a unit whose hit
// speed is shorter than that constant (see attackAnimHoldTicks), and never below
// one tick.
func (e *entity) attackAnimHold() int32 {
	hold := attackAnimHoldTicks
	if ticks := TicksForMs(e.Def.HitSpeedMs); ticks > 0 && hold >= ticks {
		hold = ticks - 1
	}
	if hold < 1 {
		hold = 1
	}
	return hold
}

// applyHit lands a decided hit. Its target may already have died this tick.
//
// A melee hit with a splash radius (`area_damage_radius`: Valkyrie 2000 /
// Dark Prince 1100 / Mega Knight 1300) also damages everything around the
// target -- the reference does this at the moment of impact, not at swing time
// (cr-sim engine/battle.py:1301-1310).
func (b *Battle) applyHit(h *pendingHit) {
	if h == nil || h.target == nil || !h.target.alive {
		return
	}
	tx, ty := h.target.xMilli, h.target.yMilli
	b.damageEntity(h.target, h.damage, h.attacker)
	if h.attacker != nil && h.attacker.Def != nil && h.attacker.Def.AoeRadiusMilli > 0 {
		b.splashDamage(h.attacker, tx, ty, h.attacker.Def.AoeRadiusMilli, h.damage, h.target)
	}
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
// homing one re-reads its target's position every tick. Which of the two a shot
// is comes from its own row's `homing` (UnitDef.Homing, 官方
// cards_stats_projectile.json). Speed is the projectile's own tiles-per-minute
// converted the same way a unit's is (参考规格 §5 投射物).
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
	// aoeRadius is the splash radius applied where the shot lands, taken from
	// the **projectile row** (`cards_stats_projectile.json:radius`). 0 = single
	// target. See splashDamage for the field's provenance.
	aoeRadius int32
}

// fireProjectile spawns a shot from src toward target.
//
// If the projectile key does not resolve in the table, the shot resolves in
// place instead of being silently dropped: core has no logger,
// and losing the damage would be a much worse failure than losing the travel
// time. Every projectile key in the 60-card pool resolves, because
// 战斗单位_cs carries the projectile rows.
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
		homing:           pdef.Homing,
		speedMilliPerSec: pdef.SpeedMilliPerSec,
		aoeRadius:        pdef.AoeRadiusMilli,
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
	team        Team
	casterID    int32
	x, y        int32
	radiusMilli int32
	remainingMs int32
	tickMs      int32
	accMs       int32
	// damagePerTick 是每跳伤害；crownDamagePerTick 是同一跳打在王冠塔上的伤害
	// （见 SpellDef.CrownDamage）。0 = 该法术不区分塔。
	damagePerTick      int32
	crownDamagePerTick int32
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
		if !areaCovers(fx.x, fx.y, fx.radiusMilli, e) {
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
			b.damageEntity(e, spellDamageOn(fx.damagePerTick, fx.crownDamagePerTick, e), nil)
		}
	}
}

// ---------------------------------------------------------------------------
// Spells
// ---------------------------------------------------------------------------

// areaCovers reports whether an area of `radiusMilli` centred on (cx, cy)
// touches e.
//
// 口径是**命中盒到命中盒**：目标自己的碰撞半径要算进去，和攻击距离
// （inAttackRange）以及溅射（splashDamage）同一套。只比中心点会让每个法术的
// 实际作用圈比官方小一个目标半径（部队 0.5 格 / 公主塔 1 格），站在圈边上的
// 单位白白免伤。参考实现同口径：`cr_sim/engine/battle.py:1862`
// `distance(...) > aspec.radius + victim.collision_radius`。
func areaCovers(cx, cy, radiusMilli int32, e *entity) bool {
	if e == nil {
		return false
	}
	reach := int64(radiusMilli) + int64(e.radiusMilli())
	return DistanceSq(cx, cy, e.xMilli, e.yMilli) <= reach*reach
}

// spellDamageOn 是一发法术打在 e 上的伤害：王冠塔吃 CrownDamage（官方减伤，
// 火球 207 而不是 689），其余吃 base。
//
// crown <= 0 时退回 base —— 表里没有对塔伤害的法术（无伤害的法术）本来就不会
// 走到这里，而"有伤害但没填对塔伤害"的旧表也不该把塔打成绝对免疫。
func spellDamageOn(base, crown int32, e *entity) int32 {
	if base <= 0 {
		return 0
	}
	if e != nil && e.Kind == KindTower && crown > 0 {
		return crown
	}
	return base
}

// pendingSpellImpact is one spell whose payload has not landed yet.
//
// A spell with `flight_ms` resolves when its projectile arrives, not when the
// card is played: the client draws the projectile flying for that same duration
// (`View/SpellFxTable.Entry.FlightSeconds`), so applying the damage on the cast
// tick is seen as a hit that lands before the projectile gets there.
//
// The landing point and radius are frozen at cast time: a spell locks onto the
// point it was aimed at (no spell homes on a unit), so whoever stands inside
// the circle when the projectile arrives is hit -- walking out dodges it,
// walking in eats it.
type pendingSpellImpact struct {
	team  Team
	sp    *SpellDef
	x, y  int32
	// dueTick is the first tick on which the projectile has arrived; set from
	// `FlightMs` at cast time, so it never depends on how many ticks pass.
	dueTick int32
}

// castSpell puts a spell card on the board at a point.
//
// `flight_ms = 0` (no projectile segment) applies the payload immediately, in
// the same tick as the cast. A positive `flight_ms` queues the payload for the
// tick the projectile arrives (see pendingSpellImpact).
//
// 伤害按目标分两档：王冠塔吃 `CrownDamage`（官方减伤，火球 207 而不是 689），
// 其余单位/建筑吃 `InstantDamage`。参考实现同一口径：
// `cr_sim/engine/specs.py::damage_to` 与 `cr_sim/engine/battle.py:1893-1894`
// （area effect 逐目标取 `aspec.damage_to(victim.kind is EntityKind.TOWER)`）。
func (b *Battle) castSpell(team Team, card *CardDef, x, y int32) {
	sp := card.Spell
	if sp == nil {
		return
	}
	if sp.FlightMs > 0 {
		b.pendingSpells = append(b.pendingSpells, &pendingSpellImpact{
			team:    team,
			sp:      sp,
			x:       x,
			y:       y,
			dueTick: b.tick + TicksForMs(sp.FlightMs),
		})
	} else {
		b.applySpellImpact(team, sp, x, y)
	}
	// 4) deploy whatever the spell delivers (a summoned group, per the spell row).
	// Not part of the deferred payload: the group is placed by the spell card
	// itself (哥布林飞桶), which has no projectile segment.
	if sp.SpawnKey != "" {
		if def, ok := b.cfg.Table.Unit(sp.SpawnKey); ok && def != nil {
			b.spawnGroup(team, def, card.ID, x, y, sp.SpawnN, sp.SpawnRadiusMilli, def.DeployMs, 0)
		}
	}
}

// applySpellImpact lands one spell's payload at (x, y): area damage, knockback
// and the lingering cloud. Called on the cast tick for a spell with no
// projectile, or on the arrival tick for one with `flight_ms`.
func (b *Battle) applySpellImpact(team Team, sp *SpellDef, x, y int32) {
	if sp == nil {
		return
	}
	// 1) instant area damage
	if sp.InstantDamage > 0 && sp.RadiusMilli > 0 {
		for _, e := range b.allEntities() {
			if !e.alive || e.Team == team || e.deploying() {
				continue
			}
			if !areaCovers(x, y, sp.RadiusMilli, e) {
				continue
			}
			b.damageEntity(e, spellDamageOn(sp.InstantDamage, sp.CrownDamage, e), nil)
		}
	}
	// 2) knockback: away from the cast point (参考规格 §5 击退)
	if sp.Pushback > 0 {
		for _, e := range b.allEntities() {
			if !e.alive || e.Team == team || e.deploying() || e.immovable() {
				continue
			}
			if !areaCovers(x, y, sp.RadiusMilli, e) {
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
			team:               team,
			x:                  x,
			y:                  y,
			radiusMilli:        sp.RadiusMilli,
			remainingMs:        sp.DurationMs,
			tickMs:             tickMs,
			damagePerTick:      sp.DamagePerTick,
			crownDamagePerTick: sp.CrownDamage,
			healPerSecond:      sp.HealPerSecond,
		})
	}
}

// stepSpellImpacts lands every queued spell whose projectile has arrived.
func (b *Battle) stepSpellImpacts() {
	if len(b.pendingSpells) == 0 {
		return
	}
	kept := b.pendingSpells[:0]
	for _, p := range b.pendingSpells {
		if b.tick < p.dueTick {
			kept = append(kept, p)
			continue
		}
		b.applySpellImpact(p.team, p.sp, p.x, p.y)
	}
	b.pendingSpells = kept
}

// ---------------------------------------------------------------------------
// Spawning
// ---------------------------------------------------------------------------

// spawnGroup places n units around a point, laid out by SummonLayout.
//
// A group card does not drop its units on one point: `summon_radius` spaces
// them out, and when the stated radius cannot physically hold the group the
// reference **packs** them instead of stacking them (参考规格 §5 群体卡；
// `cr_sim/engine/battle.py::_summon_layout` 三分支 —— 见 SummonLayout)。
//
// staggerMs is the per-unit deploy delay: the reference computes
// `deploy_ticks + index * summon_deploy_delay` (`battle.py:659`), which is what
// makes a swarm materialise in sequence rather than all at once. A hut's wave
// uses the same mechanism with its own `spawn_interval` (see stepSpawners).
func (b *Battle) spawnGroup(team Team, def *UnitDef, cardID int32, x, y int32, n int32, radiusMilli int32, deployMs int32, staggerMs int32) []*entity {
	if n < 1 {
		n = 1
	}
	offsets := SummonLayout(int(n), radiusMilli, int32(def.RadiusMilli))
	out := make([]*entity, 0, n)
	for i, off := range offsets {
		ux := ClampI32(x+off[0], 0, ArenaWMilli-1)
		uy := ClampI32(y+off[1], 0, ArenaHMilli-1)
		d := deployMs
		if staggerMs > 0 {
			d += int32(i) * staggerMs
		}
		e := b.spawnUnit(team, def, cardID, ux, uy, d)
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
		jumps:        isJumpingDef(def),
	}
	if def.LifeMs > 0 {
		e.lifeMsLeft = def.LifeMs
	}
	if def.SpawnKey != "" {
		// 首次到期的等待 = 波间隔（本表没有 spawn_start_time 列，参考实现里缺它时
		// 也是"等一整个周期"）。波间隔为 0 ⇒ 一次性生成器 ⇒ 立刻吐一波，然后
		// stepBuildings 会把它标成 spawnDone。
		if def.SpawnIntervalMs > 0 {
			e.spawnTimerMs = def.SpawnIntervalMs
		}
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
			// 半径传 0 ⇒ SummonLayout 走 PackOffsets（参考实现 `_spawn_units` 的
			// `radius` 默认值就是 0 ⇒ `pack_offsets(count, collision_radius)`）。
			b.spawnGroup(e.Team, def, e.CardID, e.xMilli, e.yMilli, e.Def.DeathSpawnN, 0, def.DeployMs, 0)
		}
	}
}

// splashDamage deals `damage` to every enemy whose hitbox is inside
// `radiusMilli` of (cx, cy), skipping `skip` (the hit's primary target, which
// has already been damaged).
//
// 出处（官方数据 + 参考实现）：
//   - 官方把溅射半径放在两个不同的字段里：角色表 `area_damage_radius`
//     （女武神 2000）与**投射物表 `radius`**
//     （法师 `chr_wizardProjectile` 1500 / 炸弹兵 `BombSkeletonProjectile` 1500 /
//     飞龙宝宝 `BabyDragonProjectile` 1200 …）。
//   - 参考实现 `cr_sim/engine/battle.py:1301-1310` 的溅射判定是
//     `distance <= reach + e.collision_radius + attacker.collision_radius`
//     （与攻击距离同一口径：算到命中盒，不算到中心点），本函数照此实现。
func (b *Battle) splashDamage(attacker *entity, cx, cy int32, radiusMilli int32, damage int32, skip *entity) int {
	if attacker == nil || radiusMilli <= 0 || damage <= 0 {
		return 0
	}
	n := 0
	for _, t := range b.allEntities() {
		if t == nil || t == skip || t == attacker {
			continue
		}
		if !canTarget(attacker, t) {
			continue // 含 atk_air/atk_ground/only_* 与"部署中不可选"三条硬过滤
		}
		reach := int64(radiusMilli) + int64(t.radiusMilli())
		if DistanceSq(cx, cy, t.xMilli, t.yMilli) > reach*reach {
			continue
		}
		b.damageEntity(t, damage, attacker)
		n++
	}
	return n
}

// ---------------------------------------------------------------------------
// Movement helpers and collision
// ---------------------------------------------------------------------------

// canCrossWater reports whether this entity may stand on river water.
//
//   - A flying unit always may -- flight ignoring the river is the whole point
//     of it (参考规格 §5 移动 / 飞行).
//   - A **jumping** unit may, because the official `jump_height` (4000) covers
//     the river's full width. The band is fixed geometry
//     (`RiverTopMilli` 15000 → `RiverBottomMilli` 17000 = 2000, 2 tiles) and
//     all four official jumpers read 4000, so the comparison below is a real
//     check rather than a formality: a unit whose table row said e.g. 1000
//     would still be stopped by the water.
//
// 跳河单位仍是地面目标：水域不再挡它，但国王塔的阻塞格照样挡它。
func (e *entity) canCrossWater() bool {
	if e == nil {
		return false
	}
	if e.flying {
		return true
	}
	if !e.jumps || e.Def == nil {
		return false
	}
	return e.Def.JumpHeightMilli >= RiverBottomMilli-RiverTopMilli
}

// moveEntity moves an entity, refusing to put it somewhere it cannot stand.
//
// On failure it tries each axis alone so an entity slides along an obstacle
// rather than sticking to it -- otherwise crowds jam solid against the river
// bank (cr-sim engine/movement.py:113).
func (b *Battle) moveEntity(e *entity, x, y int32) bool {
	if x == e.xMilli && y == e.yMilli {
		return false
	}
	flying := e.canCrossWater()
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

// sepDelta is one entity's accumulated separation displacement inside a tick.
type sepDelta struct {
	x, y int32
}

// accumulateSeparation adds the displacement that would push e1 and e2 apart
// into d1 / d2. It never moves anything: `resolveCollisions` applies the
// accumulated vectors once per tick, after clamping their magnitude.
//
// 分离解算是"每 tick 一轮**向量累加** + **取同伴平均** + 统一落地"，收敛交给
// 逐 tick 的松弛，tick 内不再迭代。
//
// 为什么取**平均**而不是求和（欠松弛）：设一只单位有 n 个"贴住"的同伴、沿某轴
// 位置为 x，它这一 tick 拿到的分离位移 ≈ Σ(overlap_i/2)，而
// overlap_i = limit − |x − x_i| ⇒ ∂(位移)/∂x = −n/2。离散迭代的误差增益
// = 1 − n/2 ⇒ n ≥ 4 时 |增益| ≥ 1，不是收敛而是**等幅/发散振荡**。
// 除以 n（取**平均**）后增益恒为 1 − 1/2 = 0.5，与 n 无关 ⇒ 恒收敛；
// 且 n=1（最常见的轻微重叠）时仍是**完整修正、一个 tick 解决**，不为阻尼付代价。
// 深重叠时这一项不起作用（原始修正量很大，真正生效的是
// `separationStepLimitMilli` 的额度），所以它只压"接近平衡时的抖动"，
// 不拖慢"炸开后的散开"。
//
// 逐对立即施加时，被夹在两只同伴中间的单位会先被一侧推 +d、再被另一侧推 −d ——
// 逐次施加时这两次位移**不会互相抵消**，只会把位置来回挪；再叠加"每 tick 位移
// 上限"（按**标量**消耗额度时，第二次反向推力被裁成 0）后，净位移永远偏向先施加
// 的那一侧 ⇒ 一个真正的 **limit cycle**。按向量累加再取平均之后，反向推力自然
// 抵消、耦合增益恒为 0.5。
func accumulateSeparation(e1, e2 *entity, d1, d2 *sepDelta) bool {
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
	if m1 < immovableMass && share1 != 0 {
		d1.x -= dx * share1 / gap
		d1.y -= dy * share1 / gap
	}
	if m2 < immovableMass && share2 != 0 {
		d2.x += dx * share2 / gap
		d2.y += dy * share2 / gap
	}
	return true
}

// applySeparation takes the **mean** of e's accumulated displacement over the
// `neighbours` pairs it came from (欠松弛, 推导见文件头的 `collisionPasses` 注释),
// clamps it to e's per-tick budget (`separationStepLimitMilli`, taken as the
// **magnitude** of the whole vector) and performs it, refusing to end up inside
// terrain.
//
// ⚠️ 额度按**向量模长**计，⛔ 不是"把每一对推力各自裁一次"：后者在被夹住时
// 会把反向的那一份裁掉，正是上面 `accumulateSeparation` 记录的振荡来源。
func applySeparation(a *Arena, e *entity, d sepDelta, neighbours int) bool {
	if neighbours > 1 {
		d.x /= int32(neighbours)
		d.y /= int32(neighbours)
	}
	if d.x == 0 && d.y == 0 {
		return false
	}
	if lim := separationStepLimitMilli(e); lim > 0 {
		mag := isqrt64(int64(d.x)*int64(d.x) + int64(d.y)*int64(d.y))
		if mag > int64(lim) {
			d.x = int32(int64(d.x) * int64(lim) / mag)
			d.y = int32(int64(d.y) * int64(lim) / mag)
		}
	}
	return shiftEntity(a, e, d.x, d.y)
}

// separationStepLimitMilli is a single entity's **whole-tick** separation
// budget: the furthest it may be pushed apart inside one tick, in milli-tiles.
// Returns 0 = unbounded.
//
// 为什么必须给上限：把重叠量在**一个 tick 内**全部分完时，服务端会在
// server_ms 45300→45400 这一格把一只亡灵移动 (0.461, 0.341) 格 = 0.573 格 = 5.73 格/s，
// 而它**前**一格是 (0.051,-0.147)、**后**一格是 (0.057,-0.118) = 1.5 格/s
// ⇒ 单格 3.8 倍、方向还反了。客户端按 10Hz 快照做线性插值，两点直线在快照点处
// 速度不连续 ⇒ 这一格被原样播成 "一帧冲 0.573 格"（= 观感"苍蝇海打人时抽搐 / 人物抖得厉害"）。
//
// 为什么在这里限而不是在客户端插值：离线仿真用
// **真实快照点**重放「线性 vs centripetal Catmull-Rom」，A1 稳态速度尖峰比 1.923 → 2.185、
// A1b 部署瞬态 3.09 → 4.05（都变差），只有 A2 改善 —— 任何**穿过快照点**的插值都躲不开
// 这段位移，只会把峰值摊得更高；要平滑就必须引入滞后。⇒ 唯一无滞后的做法是让源头的
// 逐 tick 位移有界。
//
// 上限取值的出处：该单位**自己走路**一个 tick 的距离 —— `Def.SpeedMilliPerSec` 是
// milli-tiles/second（`units.go:SpeedMilliPerSec`），而每 tick 沿路线前进的正是
// `Def.SpeedMilliPerSec / TicksPerSecond` 个 milli-tile（`battle.go` 每 tick 调
// `advanceAlongRoute(e.Def.SpeedMilliPerSec)`，而 fine 单位 = milli × TicksPerSecond）。
// 物理上自洽：被推开的速率不超过主动移动的速率。
// ⚠️ 这是**本项目新增**的约束（参考实现 `cr-sim engine/movement.py:68` 只规定"最终要分开"，
// 不含任何速率约束）⇒ 已登记在 `策划/对照表.md` §D.3。
//
// ⚠️ 额度按**整个 tick 的位移向量模长**计（`applySeparation` 对累加后的
// `sepDelta` 收缩一次），⛔ 不是"每对推力各裁一次"：后者在"被夹在两只同伴中间"
// 时会把反向的那一份裁掉，产生永久的 ±15 milli / tick 振荡。
func separationStepLimitMilli(e *entity) int32 {
	if e == nil || e.Def == nil || e.Def.SpeedMilliPerSec <= 0 {
		return 0 // 无速度定义（建筑 / 塔）⇒ 不限制，仍即时分开
	}
	lim := e.Def.SpeedMilliPerSec / TicksPerSecond
	if lim <= 0 {
		lim = 1
	}
	return lim
}

// shiftEntity moves an entity by a delta, refusing to put it in terrain.
func shiftEntity(a *Arena, e *entity, dx, dy int32) bool {
	if dx == 0 && dy == 0 {
		return false
	}
	cross := e.canCrossWater()
	x := e.xMilli + dx
	y := e.yMilli + dy
	if a.IsWalkable(x, y, cross) {
		e.setPos(x, y)
		return true
	}
	if a.IsWalkable(e.xMilli+dx, e.yMilli, cross) {
		e.setPos(e.xMilli+dx, e.yMilli)
		return true
	}
	if a.IsWalkable(e.xMilli, e.yMilli+dy, cross) {
		e.setPos(e.xMilli, e.yMilli+dy)
		return true
	}
	return escapeBlocked(a, e)
}

// escapeBlocked walks an entity out of the king towers' blocked squares.
//
// The three candidates of shiftEntity all stay inside the square when the
// entity already stands in it, so a unit that ended up there could never be
// pushed off again -- it stayed welded to the tower for the rest of the match.
// The eight directions are tried in a fixed order and the first walkable step
// that brings the entity closer to the edge of the square is taken.
//
// The step is the whole distance to the nearest edge of the square: every
// position in between is inside the square and therefore unstandable, so the
// only way out is one step across the edge.
func escapeBlocked(a *Arena, e *entity) bool {
	if !a.IsBlocked(e.xMilli, e.yMilli) {
		return false
	}
	before := a.BlockedExitDistance(e.xMilli, e.yMilli)
	step := before + 1
	for _, d := range [8][2]int32{
		{0, -1}, {1, -1}, {1, 0}, {1, 1}, {0, 1}, {-1, 1}, {-1, 0}, {-1, -1},
	} {
		nx, ny := e.xMilli+d[0]*step, e.yMilli+d[1]*step
		if !a.IsWalkable(nx, ny, e.canCrossWater()) {
			continue
		}
		if a.BlockedExitDistance(nx, ny) >= before {
			continue
		}
		e.setPos(nx, ny)
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
