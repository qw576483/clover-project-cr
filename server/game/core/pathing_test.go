package core

import (
	"reflect"
	"testing"
)

// assertMonotoneToward fails when a route contains a waypoint that moves away
// from the goal on the y axis. Crossing the river is the only reason y ever
// changes inside a leg, so this is exactly the "no pacing back and forth"
// invariant.
func assertMonotoneToward(t *testing.T, fromY, goalY int32, wps []waypoint) {
	t.Helper()
	prev := fromY
	for i, wp := range wps {
		if goalY > fromY && wp.y < prev {
			t.Fatalf("waypoint %d y=%d walks back from y=%d while the goal is at y=%d", i, wp.y, prev, goalY)
		}
		if goalY < fromY && wp.y > prev {
			t.Fatalf("waypoint %d y=%d walks back from y=%d while the goal is at y=%d", i, wp.y, prev, goalY)
		}
		prev = wp.y
	}
}

// TestRouteFromInsideRiverLeavesByGoalSide pins the river-crossing route for a
// unit that is already inside the band: it leaves by the bank its goal lies
// beyond and never walks to the bank behind it.
//
// 判据出处: 参考规格 §5 移动（地面单位沿车道 → 最近桥 → 目标点）。
func TestRouteFromInsideRiverLeavesByGoalSide(t *testing.T) {
	a := NewArena()
	cases := []struct {
		name     string
		fromX    int32
		fromY    int32
		goalX    int32
		goalY    int32
		want     []waypoint
		behindY  int32
		bridgeX  int32
		goalSide string
	}{
		{
			name: "带内 → y 大的一侧（RED 方向）", fromX: 9000, fromY: 15600, goalX: 9000, goalY: 20000,
			want:    []waypoint{{3500, RiverBottomMilli}, {9000, 20000}},
			behindY: RiverTopMilli, bridgeX: 3500, goalSide: "y 大（RED）侧",
		},
		{
			name: "带内 → y 小的一侧（BLUE 方向）", fromX: 14000, fromY: 16400, goalX: 9000, goalY: 10000,
			want:    []waypoint{{14500, RiverTopMilli}, {9000, 10000}},
			behindY: RiverBottomMilli, bridgeX: 14500, goalSide: "y 小（BLUE）侧",
		},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			got := routeWaypoints(a, c.fromX, c.fromY, c.goalX, c.goalY, false)
			if len(got) != 2 {
				t.Fatalf("route = %v, want exactly 2 waypoints (exit bank + goal)", got)
			}
			if !reflect.DeepEqual(got, c.want) {
				t.Fatalf("route = %v, want %v", got, c.want)
			}
			// 出口必须是目标侧河沿。
			if got[0].y != c.want[0].y {
				t.Fatalf("exit bank y = %d, want the %s bank %d", got[0].y, c.goalSide, c.want[0].y)
			}
			if got[0].x != c.bridgeX {
				t.Fatalf("exit bank x = %d, want the nearest bridge %d", got[0].x, c.bridgeX)
			}
			// 身后那条河沿一个航点都不许出现。
			for _, wp := range got {
				if wp.y == c.behindY {
					t.Fatalf("route %v contains the bank behind the unit (y=%d)", got, c.behindY)
				}
			}
			assertMonotoneToward(t, c.fromY, c.goalY, got)
		})
	}
}

// TestRouteOutsideBand pins the two branches whose endpoints are outside the
// water band: a crossing goes entry bank -> exit bank -> goal, and a goal that
// sits inside the band is approached over the goal's own bridge.
func TestRouteOutsideBand(t *testing.T) {
	a := NewArena()

	// 从 BLUE 半场过河到 RED 半场：先到本方河沿，再到对岸河沿，最后是目标。
	got := routeWaypoints(a, 9000, 14000, 9000, 20000, false)
	want := []waypoint{{3500, RiverTopMilli}, {3500, RiverBottomMilli}, {9000, 20000}}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("crossing route = %v, want %v", got, want)
	}
	assertMonotoneToward(t, 14000, 20000, got)

	// 目标站在右侧桥上（带内）：必须从目标那座桥、我方同侧的河沿进场。
	got = routeWaypoints(a, 9000, 12000, 14500, 16000, false)
	want = []waypoint{{14500, RiverTopMilli}, {14500, 16000}}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("route to a target in the band = %v, want %v", got, want)
	}
	assertMonotoneToward(t, 12000, 16000, got)

	// 从 RED 半场向 BLUE 半场过河：进场河沿在下、出口河沿在上。
	got = routeWaypoints(a, 9000, 26000, 9000, 8000, false)
	want = []waypoint{{3500, RiverBottomMilli}, {3500, RiverTopMilli}, {9000, 8000}}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("reverse crossing route = %v, want %v", got, want)
	}
	assertMonotoneToward(t, 26000, 8000, got)
}

// TestRouteSameSideAndFlyingAreStraight pins the two cases that never touch a
// bridge: a move within one bank, and a flying unit over the water.
func TestRouteSameSideAndFlyingAreStraight(t *testing.T) {
	a := NewArena()

	if got, want := routeWaypoints(a, 9000, 14000, 3500, 9000, false), []waypoint{{3500, 9000}}; !reflect.DeepEqual(got, want) {
		t.Fatalf("same-side route = %v, want the straight line %v", got, want)
	}
	if got, want := routeWaypoints(a, 3500, 15600, 3500, 15500, false), []waypoint{{3500, 15500}}; !reflect.DeepEqual(got, want) {
		t.Fatalf("route inside the band = %v, want the straight line %v", got, want)
	}
	if got, want := routeWaypoints(a, 9000, 14000, 9000, 20000, true), []waypoint{{9000, 20000}}; !reflect.DeepEqual(got, want) {
		t.Fatalf("flying route = %v, want the straight line %v", got, want)
	}
}

// TestKnightCrossingDoesNotPace simulates a ground knight re-planning its route
// every tick while it crosses: a moving target rebuilds the route that often,
// and a rebuild inside the band must leave by the goal-side bank, otherwise the
// knight is sent back to the bank behind it and paces back and forth.
//
// The run is 30 s rather than 5 s because the 5.5-tile diagonal approach alone
// takes 5.6 s at the Knight's 1 tile/s, so 5 s never reaches the water.
func TestKnightCrossingDoesNotPace(t *testing.T) {
	b := mustBattleWith(t, newPerfTable(false), troopDeck, troopDeck, 9)
	def, ok := b.cfg.Table.Unit("Knight")
	if !ok {
		t.Fatal("fixture error: no Knight in the table")
	}
	e := b.spawnUnit(TeamBlue, def, cardKnight, 9000, 14000, 0)

	const goalX, goalY = int32(9000), int32(20000)
	const tol = int32(50)
	const ticks = 30 * TicksPerSecond

	prevY, peakY := e.yMilli, e.yMilli
	lastDir := 0
	var reversals []int
	reachedFarBank := false
	for i := 1; i <= ticks; i++ {
		e.routeTo(b.arena, goalX, goalY)
		e.advanceAlongRoute(b.arena, def.SpeedMilliPerSec)
		y := e.yMilli

		// 严格口径：朝目标的这条轴上一步都不许退（+-50 的容差会把"每 tick 退 50"
		// 的锯齿正好吞掉，所以那一条单独判）。
		if y < peakY {
			t.Fatalf("tick %d: y stepped back from %d to %d: the unit walks back from its goal", i, peakY, y)
		}
		peakY = y
		if y < prevY-tol {
			t.Fatalf("tick %d: y fell from %d to %d: the unit walks back from its goal", i, prevY, y)
		}
		dir := 0
		if y > prevY+tol {
			dir = 1
		} else if y < prevY-tol {
			dir = -1
		}
		if dir != 0 && lastDir != 0 && dir != lastDir {
			reversals = append(reversals, i)
			if n := len(reversals); n >= 3 && reversals[n-1]-reversals[n-3] < 10 {
				t.Fatalf("y direction reversed 3 times within %d ticks (at ticks %v)", reversals[n-1]-reversals[n-3], reversals[n-3:])
			}
			lastDir = dir
		} else if dir != 0 {
			lastDir = dir
		}
		if y >= RiverTopMilli && y <= RiverBottomMilli && !onBridge(e.xMilli) {
			t.Fatalf("tick %d: ground unit left the bridge at (%d,%d)", i, e.xMilli, y)
		}
		if y >= RiverBottomMilli {
			reachedFarBank = true
		}
		prevY = y
	}
	if !reachedFarBank {
		t.Fatalf("unit never left the band (pos %d,%d): it paced instead of crossing", e.xMilli, e.yMilli)
	}
	if e.yMilli != goalY || e.xMilli != goalX {
		t.Fatalf("unit ended at (%d,%d), want the goal (%d,%d)", e.xMilli, e.yMilli, goalX, goalY)
	}
	t.Logf("crossed the band in %d ticks, %d y-direction reversals, final pos (%d,%d)", ticks, len(reversals), e.xMilli, e.yMilli)
}
