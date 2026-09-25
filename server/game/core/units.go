// Package core is the authoritative battle simulation for clover-project-cr.
//
// It is PURE Go on purpose: it must never import a clover-server-engine
// package, so that every gameplay rule can be asserted offline in milliseconds
// by `go test ./game/core/...` (project skill rule 6). Gameplay rules live
// here and nowhere else; game/logic/ may only take parameters, call core and
// send the reply.
//
// Everything positional is an integer in **milli-tiles** (1/1000 tile), the
// same scale the official data files use (reference spec section 1). Floats are
// never used to store a position or a hitpoint: the only place trigonometry is
// allowed is unit ring layout, and it is rounded back to integers immediately.
package core

import "math"

// Team identifies which side of the arena an entity fights for.
//
// Reference: 参考规格 §2 (y 小 = BLUE 后方，y 大 = RED 后方).
type Team int32

const (
	// TeamBlue defends the low-y half (y in [0, 15) tiles).
	TeamBlue Team = 0
	// TeamRed defends the high-y half (y in [17, 32) tiles).
	TeamRed Team = 1
)

// Other returns the opposing team.
func (t Team) Other() Team {
	if t == TeamBlue {
		return TeamRed
	}
	return TeamBlue
}

// Valid reports whether the value is one of the two teams.
func (t Team) Valid() bool { return t == TeamBlue || t == TeamRed }

// String is for diagnostics only.
func (t Team) String() string {
	switch t {
	case TeamBlue:
		return "blue"
	case TeamRed:
		return "red"
	default:
		return "unknown"
	}
}

// Arena / tick / elixir constants.
//
// These are the only numbers allowed to be written in code: arena size, river
// and bridge geometry, the elixir timeline and the king-tower activation delay
// Every unit, building and tower stat comes from the
// CardTable instead.
const (
	// MilliTilePerTile is the fixed-point unit for positions and ranges:
	// 1/1000 tile, matching the official data scale (anchors.json
	// `milli_tiles_per_tile: 1000`).
	MilliTilePerTile = 1000
	// TicksPerSecond is the server tick rate (50 ms per tick). Every official
	// duration is a multiple of 50 ms, so 20 TPS represents the game's own
	// timing grid (参考规格 §1).
	TicksPerSecond = 20
	// MSecPerTick is the wall-clock length of one tick.
	MSecPerTick = 50

	// ArenaWMilli / ArenaHMilli: 18 x 32 tiles (参考规格 §2).
	ArenaWMilli = 18000
	ArenaHMilli = 32000

	// River occupies y in [15000, 17000): 2 tiles tall (参考规格 §2).
	RiverTopMilli    = 15000
	RiverBottomMilli = 17000

	// Bridges: centre x = 3.5 / 14.5 tiles, half-width 1 tile (total 2 tiles).
	BridgeAxMilli   = 3500
	BridgeBxMilli   = 14500
	BridgeHalfMilli = 1000

	// Elixir: start 6, cap 10, held in 1/1000 units.
	MaxElixirMilli      = 10000
	StartingElixirMilli = 6000

	// Regulation 180 s, overtime 120 s (参考规格 §3).
	RegulationMs = 180000
	OvertimeMs   = 120000

	// KingActivationMs is how long a provoked king tower waits before it
	// starts attacking. Source: cr-sim engine/battle.py:140
	// `KING_ACTIVATION_MS = 3300`, read out of BUILDING.KingTower's action
	// graph (参考规格 §4.1).
	KingActivationMs = 3300
)

// ElixirMsPerUnit is the milliseconds needed per elixir point in each of the
// three match segments.
//
// Source: 参考规格 §3 / anchors.json `elixir_ms_per_unit: [2800,1400,930]`
// (official battle_timelines.json Default.elixir_full_bar_ms / 10).
var ElixirMsPerUnit = [3]int32{2800, 1400, 930}

// ElixirPhaseMs is the length of each elixir segment. It runs across regulation
// AND overtime without resetting, which is why the total is 300 s rather than
// 180 s: the 2x segment starts one minute before regulation ends.
//
// Source: 参考规格 §3 / anchors.json `elixir_phase_seconds: [120,120,60]`.
var ElixirPhaseMs = [3]int32{120000, 120000, 60000}

// ElixirTotalMs is the full elixir timeline length (300 s).
const ElixirTotalMs int32 = 120000 + 120000 + 60000

// ElixirPhaseAt returns the segment index (0..2) in force at elapsed ms.
//
// The timeline is cumulative and never resets, so a match that runs into
// overtime simply keeps advancing through segment 1 into segment 2.
func ElixirPhaseAt(elapsedMs int32) int {
	if elapsedMs < 0 {
		return 0
	}
	at := int32(0)
	for i := 0; i < len(ElixirPhaseMs); i++ {
		at += ElixirPhaseMs[i]
		if elapsedMs < at {
			return i
		}
	}
	return len(ElixirPhaseMs) - 1
}

// ElixirMsPerUnitAt returns ms-per-elixir in force at elapsed ms.
func ElixirMsPerUnitAt(elapsedMs int32) int32 {
	return ElixirMsPerUnit[ElixirPhaseAt(elapsedMs)]
}

// ---------------------------------------------------------------------------
// Fixed-point geometry helpers
// ---------------------------------------------------------------------------

// MilliToTile converts a milli-tile value to tiles. Display and tests only --
// never used for engine logic (cr-sim engine/fixed.py:69 says the same).
func MilliToTile(milli int32) float64 { return float64(milli) / float64(MilliTilePerTile) }

// TileToMilli converts tiles to milli-tiles, rounded to the nearest unit.
func TileToMilli(tiles float64) int32 {
	return int32(math.Round(tiles * float64(MilliTilePerTile)))
}

// DistanceSq returns the squared distance between two points, in int64 so the
// 18x32 arena (max ~6.5e8 squared) can never overflow.
func DistanceSq(ax, ay, bx, by int32) int64 {
	dx := int64(ax) - int64(bx)
	dy := int64(ay) - int64(by)
	return dx*dx + dy*dy
}

// Distance returns the Euclidean distance truncated to a whole milli-tile.
//
// Exact integer square root, no float: two runs of the same seed must agree
// bit for bit (cr-sim engine/fixed.py:80).
func Distance(ax, ay, bx, by int32) int32 {
	return int32(isqrt64(DistanceSq(ax, ay, bx, by)))
}

// WithinRange reports whether B is within reach milli-tiles of A.
// Compared squared, so no square root is taken for a yes/no answer.
func WithinRange(ax, ay, bx, by, reach int32) bool {
	if reach < 0 {
		return false
	}
	r := int64(reach)
	return DistanceSq(ax, ay, bx, by) <= r*r
}

// GapBetween returns the distance between two circles' edges (never negative).
//
// Clash Royale ranges describe the space *between* units, not between their
// centres, so both radii come off (cr-sim engine/targeting.py:55).
func GapBetween(ax, ay, ra, bx, by, rb int32) int32 {
	g := Distance(ax, ay, bx, by) - ra - rb
	if g < 0 {
		return 0
	}
	return g
}

// CirclesOverlap reports whether two circles overlap at all.
func CirclesOverlap(ax, ay, ra, bx, by, rb int32) bool {
	total := int64(ra) + int64(rb)
	return DistanceSq(ax, ay, bx, by) < total*total
}

// PointAlong returns the point `travelled` milli-tiles from A toward B.
//
// Movement is derived from a running distance rather than accumulated per-tick
// deltas on purpose: a truncated step added every tick would let the truncation
// error accumulate into a visible drift (cr-sim engine/fixed.py:108 explains
// the measurement). Clamps at both endpoints.
func PointAlong(ax, ay, bx, by, travelled, segmentLength int32) (int32, int32) {
	if segmentLength <= 0 || travelled >= segmentLength {
		return bx, by
	}
	if travelled <= 0 {
		return ax, ay
	}
	// The products are computed in 64-bit: `travelled` is carried in
	// milli-tiles x TicksPerSecond (so a route of a few tiles reaches ~10^5) and
	// `bx - ax` can be 18000, which overflows int32 and silently bends the
	// heading the wrong way.
	tx := int64(travelled)
	l := int64(segmentLength)
	return int32(int64(ax) + int64(bx-ax)*tx/l),
		int32(int64(ay) + int64(by-ay)*tx/l)
}

// PushAway shoves point further from origin by amount, along the line between
// them. Used by every knockback (cr-sim engine/fixed.py:132).
//
// Deliberately not implemented via PointAlong, which clamps at the far endpoint
// and would therefore leave the point exactly where it was.
func PushAway(ox, oy, px, py, amount int32) (int32, int32) {
	if amount == 0 {
		return px, py
	}
	span := Distance(ox, oy, px, py)
	if span <= 0 {
		return px, py // no direction to push in; stay put (deterministic)
	}
	newSpan := int64(span) + int64(amount)
	if newSpan < 0 {
		newSpan = 0
	}
	s := int64(span)
	return int32(int64(ox) + int64(px-ox)*newSpan/s),
		int32(int64(oy) + int64(py-oy)*newSpan/s)
}

// ClampI32 bounds v to [lo, hi].
func ClampI32(v, lo, hi int32) int32 {
	if v < lo {
		return lo
	}
	if v > hi {
		return hi
	}
	return v
}

// PackOffsets lays `count` units out in concentric rings so none overlap.
// 逐字搬运参考实现 `原版资源/cr-sim/cr_sim/engine/fixed.py::pack_offsets`。
//
// Some multi-unit cards ship no `SummonRadius` at all -- Skeleton Army
// (fifteen units), Minions, Archers. A ring of one radius cannot hold fifteen
// skeletons without overlap, and stacking them is not a state the board can
// represent, so the layout is derived from how much room the units need:
// rings spaced two radii apart, each holding as many as its circumference
// allows. This is a derived default, not a value from the data.
//
// 为什么必须忠实搬运：`summon_radius_mt<=0` 不能当"全放原点"（`archers` n=2 /
// `spear-goblins` n=3 / `minions` n=3 / `skeleton-army` n=15 四张卡都是 0）——
// 那样同牌几只落在**完全相同的坐标**上，随后靠引擎的"互相推开"解算散开：
// `minions` 卡两只坐标逐字相同 `(-5.5000, 2.5000)`，在 4 帧内被推到相距 ~1.0 格
//（≈7 格/s，稳态 1.5 格/s 的 5 倍）⇒ 观感就是"啪一下炸开"（抽搐）。
//
// 单环分支的半径是 `max(spacing, spacing / (2·sin(π/n)))`：`spacing/(2 sin)` 是"相邻恰好
// 相切"的外接半径，`max` 兜住 n=2（两个单位并肩而不是背对背）。
func PackOffsets(count int, unitRadiusMilli int32) [][2]int32 {
	if count <= 1 || unitRadiusMilli <= 0 {
		n := count
		if n < 1 {
			n = 1
		}
		out := make([][2]int32, n)
		return out // 全为 (0,0)
	}
	spacing := 2 * float64(unitRadiusMilli)
	if count <= 8 {
		// A small group reads as a formation, not a blob: everyone on one ring
		// sized so neighbours just touch.
		radius := spacing
		if r := spacing / (2 * math.Sin(math.Pi/float64(count))); r > radius {
			radius = r
		}
		step := 2 * math.Pi / float64(count)
		out := make([][2]int32, 0, count)
		for i := 0; i < count; i++ {
			out = append(out, [2]int32{
				int32(math.Round(radius * math.Cos(step*float64(i)))),
				int32(math.Round(radius * math.Sin(step*float64(i)))),
			})
		}
		return out
	}
	out := make([][2]int32, 0, count)
	out = append(out, [2]int32{0, 0})
	for ring := 1; len(out) < count; ring++ {
		radius := float64(ring) * spacing
		capacity := int(2 * math.Pi * radius / spacing)
		if capacity < 1 {
			capacity = 1
		}
		take := capacity
		if rest := count - len(out); rest < take {
			take = rest
		}
		step := 2 * math.Pi / float64(take)
		// Offset alternate rings so units do not line up spoke-on-spoke.
		phase := 0.0
		if ring%2 == 1 {
			phase = step / 2
		}
		for i := 0; i < take; i++ {
			out = append(out, [2]int32{
				int32(math.Round(radius * math.Cos(phase+step*float64(i)))),
				int32(math.Round(radius * math.Sin(phase+step*float64(i)))),
			})
		}
	}
	return out[:count]
}

// RingOffsets lays `count` units evenly on a circle of `radius` milli-tiles.
// 逐字搬运参考实现 `cr_sim/engine/fixed.py::ring_offsets`。
//
// Swarm cards do not drop their units on one point: `SummonRadius` spaces them
// out (参考规格 §5 群体卡). A single unit lands dead centre, and `radius<=0` is
// also the centre (the caller is expected to have already decided packing --
// see SummonLayout). The trigonometry runs once here and is rounded to whole
// milli-tiles immediately, so no float reaches the tick loop.
func RingOffsets(count int, radius int32, startEighth int) [][2]int32 {
	if count <= 1 || radius <= 0 {
		n := count
		if n < 1 {
			n = 1
		}
		out := make([][2]int32, n)
		return out // 全为 (0,0)
	}
	step := 2 * math.Pi / float64(count)
	phase := float64(startEighth) * math.Pi / 4
	out := make([][2]int32, 0, count)
	for i := 0; i < count; i++ {
		a := phase + step*float64(i)
		out = append(out, [2]int32{
			int32(math.Round(float64(radius) * math.Cos(a))),
			int32(math.Round(float64(radius) * math.Sin(a))),
		})
	}
	return out
}

// SummonLayout is where a multi-unit card's units land relative to the drop
// point. 逐字搬运参考实现 `cr_sim/engine/battle.py::_summon_layout` 的**三分支**：
//
//  1. `total <= 1` ⇒ 原点。
//  2. `radius <= 0`（卡没有 SummonRadius）⇒ `PackOffsets`。
//  3. **给定半径装不下这个组**（`2πr < n·2R`）⇒ 仍然 `PackOffsets`。
//     否则 `RingOffsets`。
//
// 第 3 条是旧实现漏掉的那一条（也是用户看到的"苍蝇海抽搐"的主根因）：`minion-horde` 的
// `summon_radius_mt=600`（自定列 D22）配 n=6 只、身体半径 500 ⇒ 环周长 2π·600 = 3769
// milli 装不下 6 只各自 1000 milli 的直径（6000 milli）⇒ 相邻只隔 **0.600 格**，而身体直径
// 是 **1.000 格** ⇒ 落地即重叠、4 帧内被推开。参考实现对此的处置是"改 pack"而不是
// "照单全收"。
func SummonLayout(count int, radius, unitRadiusMilli int32) [][2]int32 {
	if count <= 1 {
		n := count
		if n < 1 {
			n = 1
		}
		out := make([][2]int32, n)
		return out
	}
	if radius <= 0 {
		return PackOffsets(count, unitRadiusMilli)
	}
	if unitRadiusMilli > 0 {
		circumference := 2 * math.Pi * float64(radius)
		need := float64(count) * 2 * float64(unitRadiusMilli)
		if circumference < need {
			return PackOffsets(count, unitRadiusMilli)
		}
	}
	return RingOffsets(count, radius, 0)
}

// SpeedMilliPerSec converts an official `speed` (tiles per minute) into
// milli-tiles per second.
//
// Exact for every speed the official data ships (45/60/90/120 -> 750/1000/1500
// /2000): anchors.json `tiles_per_minute_per_speed_unit: 1`.
func SpeedMilliPerSec(speedTilesPerMinute int32) int32 {
	return speedTilesPerMinute * MilliTilePerTile / 60
}

// TicksForMs converts milliseconds to whole ticks, rounding half up.
//
// Rounding rather than truncating matters: 350 ms is 7 ticks rounded and 6
// truncated, and a whole tick of windup decides close interactions
// (cr-sim engine/constants.py:48).
func TicksForMs(ms int32) int32 {
	if ms <= 0 {
		return 0
	}
	return (ms*TicksPerSecond + 500) / 1000
}

// isqrt64 is an exact integer square root.
//
// It seeds from an IEEE-754 square root and then corrects by at most one step.
// The seed is deterministic (IEEE 754 requires sqrt to be correctly rounded, so
// every machine produces the same bits) and the correction loop makes the
// result exact regardless. Overflow-free for the whole arena's range.
func isqrt64(v int64) int64 {
	if v <= 0 {
		return 0
	}
	r := int64(math.Sqrt(float64(v)))
	for r > 0 && r*r > v {
		r--
	}
	for r < 3037000499 && (r+1)*(r+1) <= v {
		r++
	}
	return r
}
