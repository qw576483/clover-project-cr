package core

// Ground routing across the river.
//
// Clash Royale's defining spatial constraint is that ground troops can only
// cross at two bridges, and everything about how a push develops follows from
// that one rule (cr-sim engine/pathing.py:1).
//
// Routes are waypoints rather than per-tick directions because movement is
// *derived* from distance travelled along a segment (see
// entity.advanceAlongRoute), which is what keeps positions from accumulating
// rounding drift.
//
// No global A*: the original is lane pathing, not grid search (task doc §5).

// routeWaypoints plans a route per 参考规格 §5 移动路径:
// a ground unit goes start -> nearest bridge centre -> goal, and a same-side
// move is a straight line. Flying units ignore the river entirely.
func routeWaypoints(a *Arena, fromX, fromY, goalX, goalY int32, flying bool) []waypoint {
	if flying {
		return []waypoint{{goalX, goalY}}
	}
	if !crossesRiver(fromY, goalY) {
		return []waypoint{{goalX, goalY}}
	}
	cx := a.NearestBridgeX(fromX)
	goingUp := goalY > fromY
	near, far := int32(RiverTopMilli), int32(RiverBottomMilli)
	if !goingUp {
		near, far = int32(RiverBottomMilli), int32(RiverTopMilli)
	}
	return []waypoint{{cx, near}, {cx, far}, {goalX, goalY}}
}

// crossesRiver reports whether getting from one y to another involves the water.
//
// Note the "already on a bridge" case: a unit whose own y is inside the band
// and whose goal is outside it must still be routed. Asking only whether the
// two *ends* sit on opposite banks excuses it, so it abandons its route, steers
// straight, and walks diagonally off the edge of the bridge into the river
// (cr-sim engine/pathing.py:244 spells that out).
func crossesRiver(fromY, goalY int32) bool {
	fromIn := fromY >= RiverTopMilli && fromY <= RiverBottomMilli
	goalIn := goalY >= RiverTopMilli && goalY <= RiverBottomMilli
	if fromIn && !goalIn {
		return true
	}
	return (fromY < RiverTopMilli && goalY > RiverBottomMilli) ||
		(fromY > RiverBottomMilli && goalY < RiverTopMilli)
}

// marchGoal returns the point a unit with no visible target walks toward.
//
// Clash Royale troops walk their lane toward the enemy towers; the lane is
// decided by the nearest bridge, which is exactly the princess tower's x
// (参考规格 §2 塔与桥对齐). A dead lane tower falls back to the king tower.
func marchGoal(a *Arena, towers []*entity, team Team, xMilli, yMilli int32) (int32, int32, bool) {
	var laneTower, king *entity
	laneX := a.NearestBridgeX(xMilli)
	bestD := int64(-1)
	for _, t := range towers {
		if t.Team == team || !t.alive || t.Kind != KindTower {
			continue
		}
		if t.Def != nil && t.Def.Key != "" && isKingKey(t.Def.Key) {
			king = t
			continue
		}
		if !a.SameLane(t.xMilli, laneX) {
			continue
		}
		if d := DistanceSq(xMilli, yMilli, t.xMilli, t.yMilli); bestD < 0 || d < bestD {
			laneTower, bestD = t, d
		}
	}
	if laneTower != nil {
		return laneTower.xMilli, laneTower.yMilli, true
	}
	if king != nil {
		return king.xMilli, king.yMilli, true
	}
	_ = yMilli
	return 0, 0, false
}
