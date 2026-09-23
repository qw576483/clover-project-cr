package core

// Animation states, matching EntitySnap.Anim (task doc §4.2).
const (
	AnimIdle   int32 = 0
	AnimWalk   int32 = 1
	AnimAttack int32 = 2
	AnimDie    int32 = 3
)

// attackState is one entity's place in its attack cycle.
//
// An attack is not "deal damage every HitSpeed". It is a small state machine
// whose details decide interactions (cr-sim engine/combat.py:1):
//
//   - LoadTimeMs is the windup before the **first** hit after engaging. A
//     Knight waits 700 ms before its first swing but only 1200 ms between later
//     ones. This is why a unit that is repeatedly forced to re-engage never
//     deals any damage.
//   - HitSpeedMs is the interval between later hits.
//   - Switching target restarts the windup, which is what makes distraction
//     powerful.
//
// Damage lands at the END of a windup, so a unit killed during its load deals
// nothing.
type attackState struct {
	// cooldownMs is time until the next hit lands; zero means "ready now".
	cooldownMs int32
	// loaded is true once a hit has landed on the current target, so later
	// hits use HitSpeedMs rather than the longer LoadTimeMs.
	loaded bool
	// targetID is the target the current load belongs to.
	targetID int32
}

// engage begins (or restarts) a windup against a target.
func (s *attackState) engage(targetID int32, loadTimeMs int32) {
	if targetID == s.targetID {
		return
	}
	s.targetID = targetID
	s.loaded = false
	s.cooldownMs = loadTimeMs
}

// disengage drops the current target and the load with it.
func (s *attackState) disengage() {
	s.targetID = 0
	s.loaded = false
	s.cooldownMs = 0
}

// waypoint is one node of a movement route.
type waypoint struct{ x, y int32 }

// nav is an entity's current route plus how far along it the entity has walked.
//
// Positions are *derived* from the distance travelled, never accumulated as
// per-tick deltas: at roughly one milli-tile lost per tick a unit would drift a
// fifth of a tile over a minute (cr-sim engine/fixed.py:108). `travelledFine`
// counts in milli-tiles x TicksPerSecond so a 45 tiles/min unit's 37.5
// milli-tiles per tick is exact rather than truncated.
type nav struct {
	waypoints []waypoint
	idx       int
	fromX     int32
	fromY     int32
	// segLen is the current segment's length in `travelledFine` units.
	segLen        int32
	travelledFine int32
	// goalX/goalY remember what the route was built for, so it is only rebuilt
	// when the goal actually moves.
	goalX, goalY int32
}

// entity is one live battle entity: a troop, a building or a crown tower.
//
// Projectiles and spell area effects are deliberately *not* entities: they are
// never targeted and never collide, and EntitySnap.Kind only has room for
// troop/building/tower (task doc §4.2).
type entity struct {
	ID     int32
	Kind   CardKind
	CardID int32
	Team   Team
	Def    *UnitDef

	xMilli, yMilli int32
	hp, maxHP      int32
	facing         int32
	anim           int32
	alive          bool

	// deathReported is set by Snapshot once this casualty's death frame has
	// actually been sent to the client (see compact): a dead entity stays in
	// b.units until then, so the client receives one snapshot carrying
	// anim=AnimDie + hp=0 and can play the death animation instead of the unit
	// simply vanishing. Set before nextID++ in spawnUnit is unnecessary: Go
	// zero value is false.
	deathReported bool

	// deployMsLeft is the remaining deploy time; a deploying unit cannot act
	// and cannot be targeted (参考规格 §5 部署延迟).
	deployMsLeft int32

	st       attackState
	swings   int32
	targetID int32
	moving   bool

	nav nav

	// Building lifetime and periodic spawner.
	lifeMsLeft   int32
	spawnTimerMs int32
	spawnedCount int32

	// flying mirrors Def' air/ground layer for collision and pathing.
	flying bool
}

// pos returns the entity's position in milli-tiles.
func (e *entity) pos() (int32, int32) { return e.xMilli, e.yMilli }

// setPos moves the entity.
func (e *entity) setPos(x, y int32) { e.xMilli, e.yMilli = x, y }

// radiusMilli is the entity's collision radius.
func (e *entity) radiusMilli() int32 {
	if e.Def == nil {
		return 0
	}
	return e.Def.RadiusMilli
}

// mass is the entity's collision mass (never below 1, so inverse-mass splits
// cannot divide by zero).
func (e *entity) mass() int32 {
	if e.Def == nil || e.Def.Mass < 1 {
		return 1
	}
	return e.Def.Mass
}

// deploying reports whether the entity is still inside its deploy delay.
func (e *entity) deploying() bool { return e.deployMsLeft > 0 }

// acquirable reports whether the entity may be selected as a target.
func (e *entity) acquirable() bool { return e.alive && !e.deploying() }

// immovable reports whether the entity can be displaced by collisions and
// knockback. Buildings and towers are static; troops are not.
func (e *entity) immovable() bool {
	return e.Kind == KindBuilding || e.Kind == KindTower
}

// setAnim records the animation state for the snapshot.
func (e *entity) setAnim(a int32) { e.anim = a }

// faceToward points the entity at a point.
func (e *entity) faceToward(x int32) {
	if x < e.xMilli {
		e.facing = -1
	} else if x > e.xMilli {
		e.facing = 1
	}
}

// clearRoute drops the current route plan.
func (e *entity) clearRoute() {
	e.nav.waypoints = nil
	e.nav.idx = 0
	e.nav.segLen = 0
	e.nav.travelledFine = 0
	e.nav.goalX, e.nav.goalY = 0, 0
}

// routeTo builds and installs a route to a goal point.
func (e *entity) routeTo(a *Arena, goalX, goalY int32) {
	e.nav.waypoints = routeWaypoints(a, e.xMilli, e.yMilli, goalX, goalY, e.flying)
	e.nav.idx = 0
	e.nav.fromX, e.nav.fromY = e.xMilli, e.yMilli
	e.nav.travelledFine = 0
	e.nav.goalX, e.nav.goalY = goalX, goalY
	if len(e.nav.waypoints) > 0 {
		wp := e.nav.waypoints[0]
		e.nav.segLen = Distance(e.xMilli, e.yMilli, wp.x, wp.y) * TicksPerSecond
	}
}

// routeGoalMoved reports whether the installed route still targets (x, y).
func (e *entity) routeGoalMoved(x, y int32) bool {
	return len(e.nav.waypoints) == 0 || e.nav.goalX != x || e.nav.goalY != y
}

// advanceAlongRoute walks `stepFine` (milli-tiles x TicksPerSecond) along the
// route, carrying leftovers into the next segment so a unit rounding a corner
// does not lose a fraction of a tick's movement (cr-sim engine/pathing.py:65).
func (e *entity) advanceAlongRoute(stepFine int32) {
	if stepFine <= 0 || e.nav.idx >= len(e.nav.waypoints) {
		return
	}
	remaining := stepFine
	for remaining > 0 && e.nav.idx < len(e.nav.waypoints) {
		wp := e.nav.waypoints[e.nav.idx]
		if e.nav.segLen <= 0 {
			e.nav.fromX, e.nav.fromY = e.xMilli, e.yMilli
			e.nav.segLen = Distance(e.xMilli, e.yMilli, wp.x, wp.y) * TicksPerSecond
			e.nav.travelledFine = 0
		}
		if e.nav.segLen <= 0 {
			// Degenerate segment (already on the waypoint).
			e.xMilli, e.yMilli = wp.x, wp.y
			e.nav.idx++
			e.nav.travelledFine = 0
			e.nav.fromX, e.nav.fromY = e.xMilli, e.yMilli
			continue
		}
		left := e.nav.segLen - e.nav.travelledFine
		if remaining < left {
			e.nav.travelledFine += remaining
			remaining = 0
			nx, ny := PointAlong(e.nav.fromX, e.nav.fromY, wp.x, wp.y, e.nav.travelledFine, e.nav.segLen)
			e.xMilli, e.yMilli = nx, ny
		} else {
			remaining -= left
			e.xMilli, e.yMilli = wp.x, wp.y
			e.nav.idx++
			e.nav.segLen = 0
			e.nav.travelledFine = 0
			e.nav.fromX, e.nav.fromY = e.xMilli, e.yMilli
		}
	}
}

// (Route planning -- routeWaypoints / crossesRiver / marchGoal -- lives in
// pathing.go, next to the lane and bridge rules it encodes.)
