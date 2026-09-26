package core

// Tower kinds used by TowerRef and TowerSnap (0=公主塔 1=国王塔).
const (
	TowerKindPrincess int32 = 0
	TowerKindKing     int32 = 1
)

// Tower geometry. All of it is arena geometry (参考规格 §2 / anchors.json),
// not balance data, so it is fixed here rather than read from a table.
const (
	// PrincessTowerRadiusMilli: PrincessTower.collision_radius = 1000
	// (参考规格 §4.1: 碰撞半径 1000 = 1 格).
	PrincessTowerRadiusMilli = 1000
	// KingTowerRadiusMilli: KingTower.collision_radius = 1400 (参考规格 §4.1).
	KingTowerRadiusMilli = 1400
	// KingFootprintHalfMilli: the king tower's blocked square is 3x3 tiles
	// centred on the tower, i.e. a half-extent of 1.5 tiles. Source: the
	// BLOCKED region at tilemap cells x15-20,y3-8, which is tiles x[7.5,10.5)
	// y[1.5,4.5) for the BLUE king (参考规格 §2).
	KingFootprintHalfMilli = 1500
)

// Tower slot positions in milli-tiles (参考规格 §2).
var (
	BlueKingTowerPos     = [2]int32{9000, 3000}   // (9.0, 3.0)
	BluePrincessLeftPos  = [2]int32{3500, 6500}   // (3.5, 6.5)
	BluePrincessRightPos = [2]int32{14500, 6500}  // (14.5, 6.5)
	RedKingTowerPos      = [2]int32{9000, 29000}  // (9.0, 29.0) = mirror of 3.0
	RedPrincessLeftPos   = [2]int32{3500, 25500}  // (3.5, 25.5)
	RedPrincessRightPos  = [2]int32{14500, 25500} // (14.5, 25.5)
)

// TowerRef identifies a fallen tower for the deploy-pocket rule.
type TowerRef struct {
	ID     int32
	Kind   int32 // 0=公主塔 1=国王塔
	XMilli int32
	YMilli int32
}

// Arena is the battlefield geometry: 18x32 tiles, a river, two bridges and the
// king towers' blocked footprints. It is immutable and shared by every battle
// (only one is ever needed), so it holds no match state -- the list of fallen
// towers is passed into CanDeploy by the caller (cr-sim engine/arena.py:293
// explains why).
type Arena struct {
	bridges [2]int32 // bridge centre x, ascending
}

// NewArena builds the standard arena.
func NewArena() *Arena {
	return &Arena{bridges: [2]int32{BridgeAxMilli, BridgeBxMilli}}
}

// Width / Height in milli-tiles.
func (a *Arena) Width() int32  { return ArenaWMilli }
func (a *Arena) Height() int32 { return ArenaHMilli }

// InBounds reports whether a point is inside the arena.
func (a *Arena) InBounds(xMilli, yMilli int32) bool {
	return xMilli >= 0 && xMilli < ArenaWMilli && yMilli >= 0 && yMilli < ArenaHMilli
}

// inRiverBand reports whether y sits inside the river's y-range.
func inRiverBand(yMilli int32) bool {
	return yMilli >= RiverTopMilli && yMilli < RiverBottomMilli
}

// onBridge reports whether x sits inside one of the two bridge spans.
//
// A bridge is the run of the river band that is *not* water; the data does not
// mark bridges positively, it marks the river and leaves the gaps
// (cr-sim engine/arena.py:247).
func onBridge(xMilli int32) bool {
	if xMilli < 0 {
		return false
	}
	for _, cx := range [2]int32{BridgeAxMilli, BridgeBxMilli} {
		if xMilli >= cx-BridgeHalfMilli && xMilli < cx+BridgeHalfMilli {
			return true
		}
	}
	return false
}

// IsWater reports whether a point is river water (bridges are not water).
func (a *Arena) IsWater(xMilli, yMilli int32) bool {
	return inRiverBand(yMilli) && !onBridge(xMilli)
}

// IsBlocked reports whether a point is inside a king tower's 3x3 footprint.
func (a *Arena) IsBlocked(xMilli, yMilli int32) bool {
	for _, pos := range [2][2]int32{BlueKingTowerPos, RedKingTowerPos} {
		if xMilli >= pos[0]-KingFootprintHalfMilli && xMilli < pos[0]+KingFootprintHalfMilli &&
			yMilli >= pos[1]-KingFootprintHalfMilli && yMilli < pos[1]+KingFootprintHalfMilli {
			return true
		}
	}
	return false
}

// IsWalkable reports whether a unit may occupy this point.
//
// Flying units ignore terrain entirely (except for leaving the arena) -- that
// is the whole point of flight in this game, and it is why the river is a real
// boundary only for ground troops (参考规格 §5 移动).
func (a *Arena) IsWalkable(xMilli, yMilli int32, flying bool) bool {
	if !a.InBounds(xMilli, yMilli) {
		return false
	}
	if flying {
		return true
	}
	return !a.IsWater(xMilli, yMilli) && !a.IsBlocked(xMilli, yMilli)
}

// NearestBridgeX returns the centre x of the bridge closest to xMilli.
// Ties resolve to the left bridge, matching cr-sim's `min()` over an
// ascending bridge list.
func (a *Arena) NearestBridgeX(xMilli int32) int32 {
	best := a.bridges[0]
	bestD := absI32(xMilli - best)
	for _, cx := range a.bridges[1:] {
		if d := absI32(xMilli - cx); d < bestD {
			best, bestD = cx, d
		}
	}
	return best
}

// SameLane reports whether two x-positions are in the same lane.
//
// Bridges sit in line with the princess towers, so "nearest bridge" is exactly
// "which lane" (cr-sim engine/arena.py:352).
func (a *Arena) SameLane(xMilli, otherXMilli int32) bool {
	return a.NearestBridgeX(xMilli) == a.NearestBridgeX(otherXMilli)
}

// RiverBand returns the river's (top, bottom) y edges.
func (a *Arena) RiverBand() (int32, int32) { return RiverTopMilli, RiverBottomMilli }

// OwnHalf returns the (low, high) y-range a team may deploy in before any
// enemy princess tower has fallen.
//
// Deployment stops at the near bank of the river, not at the midline -- the
// river itself is never a legal placement (参考规格 §2.1).
func (a *Arena) OwnHalf(team Team) (int32, int32) {
	if team == TeamBlue {
		return 0, RiverTopMilli
	}
	return RiverBottomMilli, ArenaHMilli
}

// CanDeploy reports whether `team` may place a card at this point.
//
// `anywhere` covers cards flagged CanDeployOnEnemySide (every spell);
// `onWater` covers spells, which may be cast over the river where troops may
// not stand.
//
// fallenEnemyPrincess is the one piece of battle state this otherwise-static
// check needs: destroying an enemy Princess Tower expands the deploy zone into
// **that lane only** of the enemy's half, up to (not including) the row the
// destroyed tower stood on -- it does not open the whole half, and it never
// reaches the king tower (参考规格 §2.1 / cr-sim engine/arena.py:293-350).
// 法术点（`anywhere`）的合法范围 = **竞技场内任意点**，含两座国王塔的阻塞格
// （`IsBlocked` 覆盖的 3x3）与河面 —— 所以 `anywhere` 的短路排在 `IsBlocked` **之前**，
// 只有 `InBounds` 在它前面（场外仍然非法）。
//
// 参考实现 `原版资源/cr-sim/cr_sim/engine/arena.py:322-331` 的顺序是
// `in_bounds → _BLOCKED → _WATER → anywhere`（`_BLOCKED` 在 `anywhere` 之前）⇒ 那条路径下
// 法术发不进国王塔格。本工程**有意偏离该顺序**：法术必须能落在塔格上（判据见 `core_test.go`
// 的「spell on a king tower's tile」一条）。
func (a *Arena) CanDeploy(team Team, xMilli, yMilli int32, anywhere, onWater bool, fallenEnemyPrincess []TowerRef) bool {
	if !a.InBounds(xMilli, yMilli) {
		return false
	}
	if anywhere {
		return true
	}
	if a.IsBlocked(xMilli, yMilli) {
		return false
	}
	if a.IsWater(xMilli, yMilli) && !onWater {
		return false
	}

	low, high := a.OwnHalf(team)
	for _, t := range fallenEnemyPrincess {
		if t.Kind == TowerKindKing {
			continue // the king's fall ends the match; it never opens a pocket
		}
		if !a.SameLane(xMilli, t.XMilli) {
			continue
		}
		if team == TeamBlue {
			// BLUE's enemy-ward bound is `high`, already exclusive, so the
			// fallen tower's own row comes out excluded for free.
			if t.YMilli > high {
				high = t.YMilli
			}
		} else {
			// RED's enemy-ward bound is `low`, which is inclusive in the
			// unexpanded case (the river's far edge is real land). The +1
			// keeps "short of the tower's row" meaning the same on both sides.
			if t.YMilli+1 < low {
				low = t.YMilli + 1
			}
		}
	}
	return yMilli >= low && yMilli < high
}

// absI32 returns the absolute value of an int32.
func absI32(v int32) int32 {
	if v < 0 {
		return -v
	}
	return v
}
