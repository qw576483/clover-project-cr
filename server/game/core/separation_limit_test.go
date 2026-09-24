package core

import (
	"fmt"
	"math"
	"testing"
)

// 本文件是用户第 8 条「苍蝇海打人时抽搐 / 人物抖得厉害」在**碰撞分离解算器**上的
// 两条互补判据：
//
//	A. 逐 tick 位移有界 —— 不许出现"一帧冲刺"（峰值速度 = 一帧的位移 / 一帧的时间）。
//	B. 不许振荡 —— 限速之后不能再退化成"来回顶牛"（limit cycle）。
//
// 两者缺一不可：只做 A 会退化成 B（限速把一次性大位移换成了持续小位移来回）；
// 只做 B（不限速）则是原来的"炸开"。取证与根因见 `accumulateSeparation` 的注释。

// TestSeparationMovesAtMostOneWalkStep pins the per-tick displacement bound on
// collision separation -- the root cause of 「苍蝇海打人时抽搐 / 人物抖得厉害」.
//
// 取证（2026-09-24 逐帧实机，`.ai-tmp/test/D134-units.prefix.tsv` id=45）：
// 服务端在 server_ms 45300→45400 这一格把一只亡灵移动了 (0.461, 0.341) 格
// = 0.573 格 = 5.73 格/s，而它**前**一格是 (0.051,-0.147)、**后**一格是 (0.057,-0.118)
// = 1.5 格/s ⇒ 单格 3.8 倍、且 y 方向反了。客户端是 10Hz 快照 + 线性插值，
// 两点直线在快照点处速度不连续 ⇒ 这一格被原样播成一帧冲 0.573 格。
//
// 为什么修在服务端而不是客户端插值：离线仿真
// 用**真实快照点**重放「线性 vs centripetal Catmull-Rom」，
// A1 稳态速度尖峰比 1.923 → 2.185、A1b 部署瞬态 3.09 → 4.05（都变差），
// 只有 A2 改善 —— 任何**穿过快照点**的插值都躲不开这段位移，
// 只会把峰值摊得更高；要平滑就必须引入滞后。⇒ 唯一的无滞后修法，
// 是让源头的**逐 tick 位移**本身有界。
//
// 判据出处：一个单位"被推开的速率"不应超过它**自己走路**的速率
// （`Def.SpeedMilliPerSec / TicksPerSecond`，见 `units.go:SpeedMilliPerSec` 与
// `battle.go stepMovement → e.advanceAlongRoute(e.Def.SpeedMilliPerSec)`）。
func TestSeparationMovesAtMostOneWalkStep(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 7)
	def, _ := b.cfg.Table.Unit("Minion")

	a := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	c := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	// 服务端确实会产生完全同点的一对（多单位同牌 + summon_radius_mt=0）。
	a.setPos(9000, 1000)
	c.setPos(9000, 1000)

	limit := def.SpeedMilliPerSec / TicksPerSecond
	if limit <= 0 {
		t.Fatalf("fixture error: Minion walk step must be > 0, got %d", limit)
	}

	ax0, ay0 := a.xMilli, a.yMilli
	cx0, cy0 := c.xMilli, c.yMilli
	var dA, dC sepDelta
	if !accumulateSeparation(a, c, &dA, &dC) {
		t.Fatal("accumulateSeparation reported nothing for a fully coincident pair")
	}
	applySeparation(b.arena, a, dA, 1)
	applySeparation(b.arena, c, dC, 1)
	movedA := math.Hypot(float64(a.xMilli-ax0), float64(a.yMilli-ay0))
	movedC := math.Hypot(float64(c.xMilli-cx0), float64(c.yMilli-cy0))
	t.Logf("one tick of separation: walk step = %d milli, movedA=%.1f movedC=%.1f", limit, movedA, movedC)
	if movedA > float64(limit) {
		t.Fatalf("separation moved one unit %.1f milli in a single tick, want <= %d (its own walk step)",
			movedA, limit)
	}
	if movedC > float64(limit) {
		t.Fatalf("separation moved the other unit %.1f milli in a single tick, want <= %d", movedC, limit)
	}
}

// TestSeparationStillResolvesOverlap pins that the bound does not break the
// feature: a bounded separation must still end up fully separated, just over
// several ticks instead of one.
func TestSeparationStillResolvesOverlap(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 11)
	def, _ := b.cfg.Table.Unit("Minion")

	a := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	c := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	a.setPos(9000, 1000)
	c.setPos(9000, 1000)

	limit := def.SpeedMilliPerSec / TicksPerSecond
	// 容差与 `touchToleranceMilli` 一致：分离到"只剩触摸容差"即算完成。
	want := a.radiusMilli() + c.radiusMilli() - touchToleranceMilli

	ticks := 0
	for ticks < 40 {
		ticks++
		b.resolveCollisions() // 一个 tick 一轮
		gap := int32(math.Hypot(float64(c.xMilli-a.xMilli), float64(c.yMilli-a.yMilli)))
		if gap >= want {
			t.Logf("fully separated after %d tick(s) (gap=%d, want>=%d, walk step=%d)", ticks, gap, want, limit)
			return
		}
	}
	gap := int32(math.Hypot(float64(c.xMilli-a.xMilli), float64(c.yMilli-a.yMilli)))
	t.Fatalf("still overlapping after 40 ticks (gap=%d, want>=%d): the bound broke separation", gap, want)
}

// TestSeparationBoundDoesNotSlowSparseCase guards the common case: a pair that
// overlaps only slightly must still be separated inside a single tick
// (one walk step is enough for it), so the bound costs nothing in normal play.
func TestSeparationBoundDoesNotSlowSparseCase(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 13)
	def, _ := b.cfg.Table.Unit("Minion")

	a := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	c := b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1000, 0)
	limit := def.SpeedMilliPerSec / TicksPerSecond
	// 重叠一个"行走步"以内 ⇒ 必须在一次 tick 内解决。
	overlap := limit
	radius := a.radiusMilli()
	gap := radius + c.radiusMilli() - overlap
	a.setPos(9000, 1000)
	c.setPos(9000+gap, 1000)

	var dA, dC sepDelta
	accumulateSeparation(a, c, &dA, &dC)
	applySeparation(b.arena, a, dA, 1)
	applySeparation(b.arena, c, dC, 1)
	got := int32(math.Hypot(float64(c.xMilli-a.xMilli), float64(c.yMilli-a.yMilli)))
	want := a.radiusMilli() + c.radiusMilli() - touchToleranceMilli
	if got < want {
		t.Fatalf("small overlap not resolved in one tick: gap=%d want>=%d (overlap was only %d)",
			got, want, overlap)
	}
}

// TestCrowdWalkDisplacementIsBounded is the end-to-end form of bound A: it runs a
// real battle with a six-minion squad stacked on one point (the shape the server
// actually produces for a multi-unit card) and watches every unit's **per-tick**
// displacement.
//
// 一个 tick 内一个单位能走的距离 = 它自己的行走步长（主动移动）
// + 同样多的分离位移（`separationStepLimitMilli`，按**向量模长**收缩一次）。
// 旧写法（逐对立即施加 + 标量额度）实测 6 只同点时单只一 tick 被推 5 次、
// 峰值 887 milli = 12 倍名义步长。
func TestCrowdWalkDisplacementIsBounded(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 23)
	def, _ := b.cfg.Table.Unit("Minion")
	walk := def.SpeedMilliPerSec / TicksPerSecond
	if walk <= 0 {
		t.Fatalf("fixture error: Minion walk step must be > 0")
	}
	// 界 = 行走(≤walk) + 分离额度(≤walk) + 取整/转角余量。
	// 实测（修复后）worst = 155.3；旧缺陷实测 887.0 ⇒ 取 200 既挡住原缺陷、
	// 又不产生假红。
	bound := 200.0

	const n = 6
	es := make([]*entity, 0, n)
	for i := 0; i < n; i++ {
		es = append(es, b.spawnUnit(TeamBlue, def, cardMinion, 9000, 1500, 0))
	}

	px := make([]int32, n)
	py := make([]int32, n)
	for i, e := range es {
		px[i], py[i] = e.xMilli, e.yMilli
	}

	worst := 0.0
	worstTick := -1
	var worstID int32
	var worstDX, worstDY float64
	worstWhy := ""
	for tick := 0; tick < 120; tick++ {
		b.Step()
		for i, e := range es {
			if e == nil {
				continue
			}
			dx := float64(e.xMilli - px[i])
			dy := float64(e.yMilli - py[i])
			d := math.Hypot(dx, dy)
			if d > worst {
				worst, worstTick, worstID = d, tick, e.ID
				worstDX, worstDY = dx, dy
				worstWhy = fmt.Sprintf("navIdx=%d/%d anim=%d moving=%v", e.nav.idx, len(e.nav.waypoints), e.anim, e.moving)
			}
			px[i], py[i] = e.xMilli, e.yMilli
		}
	}
	t.Logf("worst per-tick displacement = %.1f milli (unit id=%d, tick=%d) d=(%.1f,%.1f) [%s], bound %.0f",
		worst, worstID, worstTick, worstDX, worstDY, worstWhy, bound)
	if worst > bound {
		t.Fatalf("a unit moved %.1f milli in one tick, want <= %.0f (= its own walk step plus one "+
			"bounded separation): the server is still teleporting units, and the client will "+
			"play that as a one-frame lunge", worst, bound)
	}
}

// TestSeparationDoesNotOscillate is bound B: the rate limit above must not turn
// a one-tick lunge into a permanent ping-pong.
//
// 离线受控复现（用户第 8 条「苍蝇海打人时抽搐」的第二个面）：
// 6 只半径 500 milli 的单位放在**间距仅 70 milli** 的一条竖线上，
// **不施加任何行走**、只跑 `resolveCollisions()`。
//
//	· 旧写法（逐对立即施加 + 标量额度）：单只在 y 上每 tick 来回 15 milli，
//	  **跑满 300 tick 仍在振**（rev≈290/300）；
//	· 现写法（向量累加 + 欠松弛）：越过起始松弛瞬态后反向 0 次，单调散开。
//
// 起始瞬态单独说明（⛔ 不是隐藏证据）：前 `sepOscSkipTicks` 个 tick 是"最深重叠"
// 的松弛起步，个别单位会有 1~2 次方向修正；与 A1 的 `TH_A1_SPAWN_SKIP_FRAMES`
// 同口径，只跳过**起点**、不跳过过程中的任何一帧。
//
// 实机同形态：`.ai-tmp/test/D134-units.tsv` id=46/47/48（fr 2957..2978，
// wx 恒为 −5.5 的一条竖直车道，前排 id=45 wy=−8.0550 恒定不动、anim=2→3 交火）。
func TestSeparationDoesNotOscillate(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 23)
	def, _ := b.cfg.Table.Unit("Skeleton")

	ys := []int32{1000, 1070, 1140, 1210, 1280, 1350}
	es := make([]*entity, 0, len(ys))
	for _, y := range ys {
		es = append(es, b.spawnUnit(TeamBlue, def, cardSkeleton, 9000, y, 0))
	}

	// 起始松弛瞬态长度。实测：前 16 个 tick 内最深重叠被摊开一个数量级，
	// 之后进入单调收敛；取 16 = 实测瞬态长度，且不遮挡过程中任何一帧。
	const sepOscSkipTicks = 16

	const ticks = 300
	hist := make([][]int32, len(es))
	for tick := 0; tick < ticks; tick++ {
		b.tick++
		b.serverMs = b.tick * MSecPerTick
		b.resolveCollisions() // 只有分离，没有任何行走
		for i, e := range es {
			hist[i] = append(hist[i], e.yMilli)
		}
	}

	const eps = 1.0
	worstRev := 0
	var worstID int32
	for i, e := range es {
		h := hist[i]
		rev := 0
		for k := sepOscSkipTicks; k+1 < len(h); k++ {
			d1 := float64(h[k] - h[k-1])
			d2 := float64(h[k+1] - h[k])
			if math.Abs(d1) < eps || math.Abs(d2) < eps {
				continue
			}
			if d1*d2 < 0 {
				rev++
			}
		}
		t.Logf("id=%-4d rev=%-4d y[%d]=%-6d y[%d]=%-6d", e.ID, rev, sepOscSkipTicks, h[sepOscSkipTicks], len(h)-1, h[len(h)-1])
		if rev > worstRev {
			worstRev, worstID = rev, e.ID
		}
	}
	if worstRev > 0 {
		t.Fatalf("separation oscillates: unit id=%d reversed direction %d times after the start-up "+
			"transient (%d ticks) with no walking at all (want 0): the per-tick correction is not "+
			"cancelling / not under-relaxed, so a unit squeezed between neighbours ping-pongs forever",
			worstID, worstRev, sepOscSkipTicks)
	}
}
