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
	// ⚠️ 旧常量 `collisionPasses = 3`（tick 内逐对立即施加、固定 N 轮）已于
	// 2026-09-24 移除：逐对立即施加 + 标量额度会产生永久 limit cycle
	// （见 `accumulateSeparation` 的复现记录）。现在是"每 tick 一轮向量累加 +
	// **取同伴平均** + 统一落地"，收敛交给逐 tick 的松弛，tick 内不再迭代。
	//
	// 为什么取**平均**而不是求和（欠松弛，2026-09-24，用户第 8 条「苍蝇海打人时
	// 抽搐」的第二个面）：设一只单位有 n 个"贴住"的同伴、沿某轴位置为 x，它这一
	// tick 拿到的分离位移 ≈ Σ(overlap_i/2)，而 overlap_i = limit − |x − x_i|
	// ⇒ ∂(位移)/∂x = −n/2。离散迭代的误差增益 = 1 − n/2 ⇒ n ≥ 4 时 |增益| ≥ 1，
	// 不是收敛而是**等幅/发散振荡**（实机与离线复现都命中）。
	// 除以 n（取**平均**）后增益恒为 1 − 1/2 = 0.5，与 n 无关 ⇒ 恒收敛；
	// 且 n=1（最常见的轻微重叠）时仍是**完整修正、一个 tick 解决**，不为阻尼付代价。
	// 深重叠时这一项不起作用（原始修正量很大，真正生效的是
	// `separationStepLimitMilli` 的额度），所以它只压"接近平衡时的抖动"，
	// 不拖慢"炸开后的散开"。
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
	// aoeRadius is the splash radius applied where the shot lands, taken from
	// the **projectile row** (`cards_stats_projectile.json:radius`). 0 = single
	// target. See splashDamage for the field's provenance.
	aoeRadius int32
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
			b.spawnGroup(team, def, card.ID, x, y, sp.SpawnN, sp.SpawnRadiusMilli, def.DeployMs, 0)
		}
	}
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
			// 旧实现传 `def.RadiusMilli*2` 是**自造**的半径。
			b.spawnGroup(e.Team, def, e.CardID, e.xMilli, e.yMilli, e.Def.DeathSpawnN, 0, def.DeployMs, 0)
		}
	}
}

// splashDamage deals `damage` to every enemy whose hitbox is inside
// `radiusMilli` of (cx, cy), skipping `skip` (the hit's primary target, which
// has already been damaged).
//
// 出处（用户第 3 条「法师不是 aoe 攻击吗？」）：
//   - 官方把溅射半径放在两个不同的字段里：角色表 `area_damage_radius`
//     （女武神 2000 / 黑暗王子 1100 / 超级骑士 1300）与**投射物表 `radius`**
//     （法师 `chr_wizardProjectile` 1500 / 屠夫 `AxeManProjectile` 1000 /
//     滚石 `BowlerProjectile` 1800 / 炸弹兵 1500 / 公主 2000 / 火精灵 2300 …）。
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
// ⚠ 差异登记 D142：旧实现**根本没有**这条路 —— `IsWalkable` 只看 `flying`，
// 官方 `jump_enabled` 的野猪骑士在河边被水域挡停（用户第 3 条"野猪骑士为什么不能穿河？"）。
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
// ⚠️ 为什么要"先累加、后落地"（2026-09-24 修，用户第 8 条「苍蝇海打人时抽搐」
// 的**第二个面**）：旧写法在**每个重叠对上立即**调用 `shiftEntity`。当一只单位被
// 夹在两只同伴中间时，它会先被一侧推 +d、再被另一侧推 −d —— 逐次施加时这两次
// 位移**不会互相抵消**，只会把位置来回挪；再叠加"每 tick 位移上限"（旧写法按
// **标量**消耗额度，第二次反向推力被裁成 0）后，净位移永远偏向先施加的那一侧
// ⇒ 一个真正的 **limit cycle**。
//
// 离线受控复现（`tmp_seponly_test.go`，与实机同源）：3 只半径 500 milli 的单位
// 放在间距仅 5~15 milli 的同一格，**不施加任何行走**、只跑 `resolveCollisions`，
// 旧写法下 id=7 在 y=1130↔1145 每 tick 来回 15 milli、**永久振荡**；
// 实机同形态见 `.ai-tmp/test/D134-units.tsv` id=46/47/48（fr 2957..2978，
// wx 恒为 −5.5 的一条竖直车道，前排 id=45 wy=−8.0550 恒定不动、anim=2→3 交火）。
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
// 根因（2026-09-24 逐帧实机取证，`.ai-tmp/test/D134-units.tsv` id=45）：
// 旧写法把重叠量在**一个 tick 内**全部分完 —— 实测服务端在 server_ms 45300→45400
// 这一格把一只亡灵移动了 (0.461, 0.341) 格 = 0.573 格 = 5.73 格/s，而它**前**一格
// 是 (0.051,-0.147)、**后**一格是 (0.057,-0.118) = 1.5 格/s ⇒ 单格 3.8 倍、方向还反了。
// 客户端按 10Hz 快照做线性插值，两点直线在快照点处速度不连续 ⇒ 这一格被原样播成
// "一帧冲 0.573 格"（= 用户报的「苍蝇海打人时抽搐 / 人物抖得厉害」）。
//
// 为什么修在这里而不是客户端插值：离线仿真（`tools/probes/d134-spline-sim.py`）用
// **真实快照点**重放「线性 vs centripetal Catmull-Rom」，A1 稳态速度尖峰比 1.923 → 2.185、
// A1b 部署瞬态 3.09 → 4.05（都变差），只有 A2 改善 —— 任何**穿过快照点**的插值都躲不开
// 这段位移，只会把峰值摊得更高；要平滑就必须引入滞后。⇒ 唯一无滞后的修法是让源头的
// 逐 tick 位移有界。
//
// 上限取值的出处：该单位**自己走路**一个 tick 的距离 —— `Def.SpeedMilliPerSec` 是
// milli-tiles/second（`units.go:SpeedMilliPerSec`），而每 tick 沿路线前进的正是
// `Def.SpeedMilliPerSec / TicksPerSecond` 个 milli-tile（`battle.go` 每 tick 调
// `advanceAlongRoute(e.Def.SpeedMilliPerSec)`，而 fine 单位 = milli × TicksPerSecond）。
// 物理上自洽：被推开的速率不超过主动移动的速率。
// ⚠️ 这是**本项目新增**的约束（参考实现 `cr-sim engine/movement.py:68` 只规定"最终要分开"，
// 不含任何速率约束）⇒ 已登记在 `策划/验收表.md` 的「允许的差异」。
//
// ⚠️ 额度按**整个 tick 的位移向量模长**计（`applySeparation` 对累加后的
// `sepDelta` 收缩一次），⛔ 不是"每对推力各裁一次"：后者在"被夹在两只同伴中间"
// 时会把反向的那一份裁掉，实测产生永久的 ±15 milli / tick 振荡（见
// `accumulateSeparation` 的复现记录）。
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
