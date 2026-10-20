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
// No global A*: the original is lane pathing, not grid search.

// routeWaypoints plans a route per 参考规格 §5 移动路径:
// a ground unit goes start -> nearest bridge centre -> goal, and a same-side
// move is a straight line. Flying units ignore the river entirely.
//
// Every waypoint must move the unit closer to the goal. That is why a unit
// that is already inside the river band leaves it by the bank the goal lies
// beyond -- walking to the bank behind it would send the unit back across the
// bridge, and re-planning from inside the band (a moving target does that every
// tick) would pace it back and forth instead of crossing.
func routeWaypoints(a *Arena, fromX, fromY, goalX, goalY int32, flying bool) []waypoint {
	if flying {
		return []waypoint{{goalX, goalY}}
	}
	fromIn := atRiverBand(fromY)
	goalIn := atRiverBand(goalY)
	if !fromIn && !goalIn {
		if !crossesRiver(fromY, goalY) {
			return []waypoint{{goalX, goalY}}
		}
		cx := a.NearestBridgeX(fromX)
		return []waypoint{
			{cx, riverEntryEdge(fromY)},
			{cx, riverExitEdge(fromY, goalY)},
			{goalX, goalY},
		}
	}
	if fromIn && goalIn {
		// 两端的 y 都在河道带内：同一座桥的桥面是直线可达的，不需要绕河岸。
		return []waypoint{{goalX, goalY}}
	}
	if fromIn {
		return []waypoint{{a.NearestBridgeX(fromX), riverExitEdge(fromY, goalY)}, {goalX, goalY}}
	}
	// 目标在河道带内（站在桥上）：从目标那座桥、与我方同侧的河沿进场，再沿桥接近。
	return []waypoint{{a.NearestBridgeX(goalX), riverEntryEdge(fromY)}, {goalX, goalY}}
}

// atRiverBand reports whether y lies inside the river band with both bank edges
// included. It is the routing question, not the water question: a unit standing
// exactly on a bank edge still has the whole band between itself and the far
// bank. (Water geometry uses inRiverBand, whose band is half-open.)
func atRiverBand(y int32) bool {
	return y >= RiverTopMilli && y <= RiverBottomMilli
}

// riverEntryEdge is the bank edge a unit coming from outside the band steps
// onto the bridge from.
func riverEntryEdge(fromY int32) int32 {
	if fromY < RiverTopMilli {
		return RiverTopMilli
	}
	return RiverBottomMilli
}

// riverExitEdge is the bank edge a unit leaves the band by on its way to goalY:
// the edge on the goal's side of fromY, never the one behind it.
func riverExitEdge(fromY, goalY int32) int32 {
	if goalY < fromY {
		return RiverTopMilli
	}
	return RiverBottomMilli
}

// crossesRiver reports whether getting from one y to another involves the water.
//
// Note the "already on a bridge" case: a unit whose own y is inside the band
// and whose goal is outside it must still be routed. Asking only whether the
// two *ends* sit on opposite banks excuses it, so it abandons its route, steers
// straight, and walks diagonally off the edge of the bridge into the river
// (cr-sim engine/pathing.py:244 spells that out).
func crossesRiver(fromY, goalY int32) bool {
	fromIn := atRiverBand(fromY)
	goalIn := atRiverBand(goalY)
	if fromIn && !goalIn {
		return true
	}
	return (fromY < RiverTopMilli && goalY > RiverBottomMilli) ||
		(fromY > RiverBottomMilli && goalY < RiverTopMilli)
}

// marchObjective picks the enemy crown tower a unit with no visible target
// walks at.
//
// The objective is the **princess tower covering the unit's side** of the
// board; the king tower takes that lane over once the princess tower in it is
// gone. The side is the princess tower whose x is nearest to the unit's, with
// the lower id as the tiebreak -- the same lane the bridges define, because
// the princess towers stand in line with the bridges (参考规格 §2 塔与桥对齐).
//
// 判据出处: cr-sim engine/battle.py:2776-2793（`_pick_objective` 的文档字符串：
// "The King Tower only becomes the objective once the Princess Tower covering
// that side is gone."）。
//
// The returned tower is the unit's **target**: a unit that walks at a tower
// without holding it never swings at it.
func marchObjective(roster []towerEntry, team Team, xMilli int32) *entity {
	var side *towerEntry
	for i := range roster {
		t := &roster[i]
		if t.team == team || t.kind != TowerKindPrincess {
			continue
		}
		if side == nil || nearerTower(t, side, xMilli) {
			side = t
		}
	}
	if side != nil && side.ent != nil && side.ent.alive {
		return side.ent
	}
	// 该侧公主塔已倒 ⇒ 国王塔接管这条车道。
	for i := range roster {
		t := &roster[i]
		if t.team == team || t.kind != TowerKindKing {
			continue
		}
		if t.ent != nil && t.ent.alive {
			return t.ent
		}
	}
	// 兜底：还活着的敌方塔里按 |x| 最近的一座。
	var best *entity
	bestD := int32(0)
	for i := range roster {
		t := &roster[i]
		if t.team == team || t.ent == nil || !t.ent.alive {
			continue
		}
		d := absI32(t.x - xMilli)
		if best == nil || d < bestD || (d == bestD && t.ent.ID < best.ID) {
			best, bestD = t.ent, d
		}
	}
	return best
}

// nearerTower reports whether tower a is the nearer choice than tower b for a
// unit standing at xMilli. Distance is measured on x, and the lower id breaks
// a tie so two units never disagree.
func nearerTower(a, b *towerEntry, xMilli int32) bool {
	da := absI32(a.x - xMilli)
	db := absI32(b.x - xMilli)
	if da != db {
		return da < db
	}
	if a.ent == nil || b.ent == nil {
		return a.ent != nil
	}
	return a.ent.ID < b.ent.ID
}

// standoffGoal is the point a unit walks to when it is heading for a target:
// the spot at `range + both radii` from the target's centre, measured on the
// unit's side of the target.
//
// Walking to the target's centre instead would put the unit inside the target
// -- and, for a crown tower, inside the king tower's blocked square, which no
// ground unit may stand in. The distance is the attack range measured the way
// this engine measures ranges: the gap between the two hitboxes (see
// inAttackRange), so the unit that arrives there can swing.
func standoffGoal(e *entity, t *entity) (int32, int32) {
	if e == nil || t == nil || e.Def == nil {
		return 0, 0
	}
	reach := int64(e.Def.RangeMilli) + int64(e.radiusMilli()) + int64(t.radiusMilli())
	if reach <= 0 {
		return t.xMilli, t.yMilli
	}
	dx := int64(e.xMilli) - int64(t.xMilli)
	dy := int64(e.yMilli) - int64(t.yMilli)
	dist := isqrt64(dx*dx + dy*dy)
	if dist <= 0 {
		return t.xMilli, t.yMilli
	}
	if dist <= reach {
		// 已经在站位圈内 ⇒ 目标就在射程里，单位原地不动。
		return e.xMilli, e.yMilli
	}
	px := int64(t.xMilli) + dx*reach/dist
	py := int64(t.yMilli) + dy*reach/dist
	// 整数截断可能把落点留在圈外一步，而射程判定在边界上是精确的
	// （见 inAttackRange 的注释），所以这时把落点再拉近一格。
	if isqrt64((px-int64(t.xMilli))*(px-int64(t.xMilli))+(py-int64(t.yMilli))*(py-int64(t.yMilli))) > reach {
		px = int64(t.xMilli) + dx*(reach-1)/dist
		py = int64(t.yMilli) + dy*(reach-1)/dist
	}
	return int32(px), int32(py)
}
