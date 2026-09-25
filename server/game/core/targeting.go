package core

// Targeting is where most of Clash Royale's texture lives. Three rules matter
// more than the rest and are easy to get subtly wrong
// (cr-sim engine/targeting.py:1):
//
//  1. Range is measured to a hitbox, not to a point -- so a Giant with a 0.75
//     tile radius can be hit from further away than a Skeleton with 0.5.
//  2. Targets are sticky: a unit does not re-choose every tick, it keeps its
//     target until that target dies or leaves sight range.
//  3. Building-targeting troops are not "preferring" buildings, they cannot
//     see troops at all.

// canTarget is a hard filter, not a preference. A unit that fails it does not
// see the target and will walk right past it (参考规格 §5 索敌).
func canTarget(attacker *entity, target *entity) bool {
	if attacker == nil || target == nil || attacker.Def == nil || target.Def == nil {
		return false
	}
	if !target.alive || target.Team == attacker.Team {
		return false
	}
	// A projectile or an area cloud is not a thing you can attack. In core both
	// live outside the entity list entirely, so this is belt and braces.
	if target.Kind == KindProjectile {
		return false
	}
	// Still deploying => not yet selectable (参考规格 §5 部署延迟).
	if !target.acquirable() {
		return false
	}
	// target.flying is cached at spawn: this runs for every candidate of every
	// unit every tick, and re-deriving flight from the key would put a string
	// operation (and an allocation) in the hottest loop of the match.
	if target.flying {
		if !attacker.Def.AtkAir {
			return false
		}
	} else if !attacker.Def.AtkGround {
		return false
	}
	if attacker.Def.OnlyBuildings && !target.Def.IsStructure() {
		return false
	}
	if attacker.Def.OnlyTowers && target.Kind != KindTower {
		return false
	}
	if attacker.Def.OnlyTroops && target.Kind != KindTroop {
		return false
	}
	return true
}

// withinSight reports whether the target's hitbox gap is inside the attacker's
// sight range. Sight is also measured hitbox-to-hitbox.
//
// 边界口径与 inAttackRange 同一套（含那个 `+1`，见其注释）。
func withinSight(attacker *entity, target *entity, extra int32) bool {
	reach := attacker.Def.SightMilli + extra
	limit := int64(reach) + int64(attacker.radiusMilli()) + int64(target.radiusMilli()) + 1
	return DistanceSq(attacker.xMilli, attacker.yMilli, target.xMilli, target.yMilli) < limit*limit
}

// inAttackRange reports whether the target's hitbox gap is inside the
// attacker's attack range.
//
// 整数边界与参考实现**逐字对齐**。
// 参考 `cr-sim/cr_sim/engine/targeting.py::within_gap` 的原文推导是
//
//	gap = isqrt(d²) − ra − rt  ≤  reach
//	⟺ isqrt(d²) ≤ reach + ra + rt      记 k = reach + ra + rt
//	⟺ d² < (k + 1)²                    （对整数恒等：floor(√d²) ≤ k ⟺ d² < (k+1)²）
//
// ⛔ 不许写成 `d² < k²`：少了那个 `+1`，在"中心距**正好等于** k"这一格上比参考更严
// 1 milli-tile（1/1000 格）。方向上是"更不容易命中"，不会造成"老远就打到"，
// 但那是一处无谓的差异，不必制造。
//
// ⛔ 不许再退化成"中心距 ≤ reach"（漏掉双方半径）或"半径加两遍"：
// `range` 描述的是两个 hitbox 之间的空隙（参考 `gap_between` 的文档字符串：
// "ranges in Clash Royale describe the space between units rather than between
// their centres"）。口径与数值见 `range_pin_test.go`（含负控）。
func inAttackRange(attacker *entity, target *entity) bool {
	reach := attacker.Def.RangeMilli
	limit := int64(reach) + int64(attacker.radiusMilli()) + int64(target.radiusMilli()) + 1
	return DistanceSq(attacker.xMilli, attacker.yMilli, target.xMilli, target.yMilli) < limit*limit
}

// acquireTarget picks the nearest legal target within sight.
//
// Nearest first by hitbox gap, with entity id as a tiebreak so two units in
// identical positions never disagree: an arbitrary but *stable* choice, which
// is what determinism requires (cr-sim engine/targeting.py:147). The candidate
// order is therefore irrelevant to the result.
func acquireTarget(attacker *entity, candidates []*entity) *entity {
	var best *entity
	bestGap := int32(0)
	for _, c := range candidates {
		if !canTarget(attacker, c) {
			continue
		}
		if !withinSight(attacker, c, 0) {
			continue
		}
		gap := GapBetween(attacker.xMilli, attacker.yMilli, attacker.radiusMilli(),
			c.xMilli, c.yMilli, c.radiusMilli())
		if best == nil || gap < bestGap || (gap == bestGap && c.ID < best.ID) {
			best, bestGap = c, gap
		}
	}
	return best
}

// keepTarget reports whether the attacker holds its current target for another
// tick (参考规格 §5: 锁定后保持，目标死亡/越界才重锁定).
func keepTarget(attacker *entity, target *entity) bool {
	if target == nil || !canTarget(attacker, target) {
		return false
	}
	return withinSight(attacker, target, 0)
}
