package core

import (
	"math"
	"testing"
)

// 本文件的判据对应差异登记 D142/D147（用户第 3、8 条）：多单位卡的落点必须
// **互不重叠**，且"表里填的半径装不下这个组"时要改铺开方式（pack）而不是照单全收。
//
// 出处（2026-09-23 D134 逐帧实机取证，`.ai-tmp/test/D134-units.tsv`）：
//
//	帧 2820..2822 两只 `chr_minion_out` 坐标**完全相同** `(-5.5000, 2.5000)`
//	  —— 因为 `minions` 卡的 `summon_radius_mt = 0`，而旧 `RingOffsets` 把 `radius<=0`
//	     当成"全部放原点"；
//	帧 2823..2826 靠引擎"互相推开"在 4 帧内散到相距 ~1.0 格（≈7 格/s，稳态 1.5 格/s 的 5 倍）
//	  —— 观感就是"啪一下炸开"，对应判据 A1b 实测 6.58× / 6.63× 稳态；
//	`minion-horde`（n=6，表里 radius=600，单位半径 500）落地时相邻只隔 **0.600 格**，
//	  而身体直径是 **1.000 格** ⇒ 同样是"落地即重叠"。参考实现
//	  `cr_sim/engine/battle.py::_summon_layout` 的第三条分支正是为它准备的：
//	  "A stated radius that cannot physically hold the group still needs packing."

// TestSummonLayoutNoCoincidence —— 判据：`count >= 2` 时**任何两个单位都不许落在同一点**。
//
// **能失败**：把 `PackOffsets` 的 `count<=1 || unitRadius<=0` 早返回改成无条件返回原点即红。
func TestSummonLayoutNoCoincidence(t *testing.T) {
	cases := []struct {
		name       string
		count      int
		radius     int32
		unitRadius int32
	}{
		{"minions n=3 radius=0 (表里的 0 = 没填)", 3, 0, 500},
		{"archers n=2 radius=0", 2, 0, 500},
		{"spear-goblins n=3 radius=0", 3, 0, 500},
		{"skeleton-army n=15 radius=0", 15, 0, 500},
		{"minion-horde n=6 radius=600（装不下 ⇒ 必须改 pack）", 6, 600, 500},
		{"barbarians n=5 radius=700（装不下 ⇒ 必须改 pack）", 5, 700, 500},
		{"goblins n=4 radius=700（装得下 ⇒ 用原半径）", 4, 700, 500},
		{"skeletons n=3 radius=700", 3, 700, 500},
		{"bats n=5 radius=750", 5, 750, 500},
	}
	for _, c := range cases {
		off := SummonLayout(c.count, c.radius, c.unitRadius)
		if len(off) != c.count {
			t.Fatalf("%s: 点数 %d != count %d", c.name, len(off), c.count)
		}
		seen := map[[2]int32]bool{}
		for _, p := range off {
			if seen[p] {
				t.Errorf("%s: 出现重合点 (%d,%d) —— 同牌单位坐标完全重合 ⇒ 部署瞬间'啪一下炸开'", c.name, p[0], p[1])
			}
			seen[p] = true
		}
	}
}

// TestSummonLayoutNoOverlap —— 判据：**任意两点**的间距都 ≥ 单位直径（2R）。
//
// 为什么是强判据："任意两点"同时覆盖了单环的相邻相切与同心环的内外环间距（= ring·spacing）。
//
// 容差 2 milli（= 0.002 格，视觉不可见）是**三角函数取整**的必然产物，不是放宽判据：
// 环上点的坐标是 `int32(round(r·cos θ))`，相邻弦长由两个已取整的点算出，
// 误差可以到 ±1 milli 量级（实测 R=500 n=7 得 999.0，正好差 1）。
// 参考实现 `cr_sim/engine/fixed.py::pack_offsets` 用的是 Python `round()` ⇒ 同样有这个误差。
//
// **能失败**：把 `PackOffsets` 里的 `spacing := 2 * float64(unitRadiusMilli)` 改成
// `spacing := float64(unitRadiusMilli)`（少乘 2）⇒ 全部 case 红。
func TestSummonLayoutNoOverlap(t *testing.T) {
	radii := []int32{400, 500, 600, 750, 1000}
	counts := []int{2, 3, 4, 5, 6, 7, 8, 9, 12, 15, 20}
	for _, n := range counts {
		for _, r := range radii {
			off := SummonLayout(n, 0, r) // radius=0 ⇒ 走 PackOffsets
			need := 2*float64(r) - 2
			for i := 0; i < len(off); i++ {
				for j := i + 1; j < len(off); j++ {
					d := math.Hypot(float64(off[j][0]-off[i][0]), float64(off[j][1]-off[i][1]))
					if d < need {
						t.Fatalf("R=%d n=%d: 点 %d 与 %d 距 %.1f < 直径 %.1f（会重叠 ⇒ 被解算器推开 ⇒ 部署瞬态）",
							r, n, i, j, d, 2*float64(r))
					}
				}
			}
		}
	}
}

// TestSummonLayoutPacksWhenRadiusTooSmall —— 判据：**给定半径装不下就改 pack**，
// 这是参考实现 `_summon_layout` 的第三条分支，也是旧实现整条漏掉的那一条。
//
// `minion-horde`：n=6、单位半径 500、表里 radius=600。
// 环周长 2π·600 = 3769.9 < 需要 6×2×500 = 6000 ⇒ 必须 pack；
// pack 出来的单环半径 = max(1000, 1000/(2·sin(30°))) = 1000 ⇒ 相邻弦长 = 2·1000·sin(30°) = 1000 = 直径。
//
// **能失败**：删掉 `SummonLayout` 里 `circumference < need` 那一段即红（相邻会掉到 600）。
func TestSummonLayoutPacksWhenRadiusTooSmall(t *testing.T) {
	off := SummonLayout(6, 600, 500)
	minGap := math.Inf(1)
	for i := 0; i < len(off); i++ {
		j := (i + 1) % len(off)
		d := math.Hypot(float64(off[j][0]-off[i][0]), float64(off[j][1]-off[i][1]))
		if d < minGap {
			minGap = d
		}
	}
	if minGap < 999 {
		t.Fatalf("minion-horde（n=6 radius=600 单位半径 500）相邻间距 %.1f < 1000 —— "+
			"没有走 pack 分支 ⇒ 落地即重叠（用户看到的'苍蝇海抽搐'）", minGap)
	}
	// 反面对照：半径真的装得下的组必须**原样**用给的半径（不许被 pack 掉）。
	off2 := SummonLayout(4, 700, 500)
	maxD := 0.0
	for _, p := range off2 {
		if d := math.Hypot(float64(p[0]), float64(p[1])); d > maxD {
			maxD = d
		}
	}
	if math.Abs(maxD-700) > 1 {
		t.Fatalf("goblins（n=4 radius=700 单位半径 500）显式半径被改写：最远点 %.1f，应 ≈700", maxD)
	}
}

// TestSummonLayoutSingleAndBoundaries —— 单体落原点；`radius<=0 && unitRadius<=0`
// 时退回原点（没有可用的几何信息，⛔ 不许凭空造一个半径）。
func TestSummonLayoutSingleAndBoundaries(t *testing.T) {
	if off := SummonLayout(1, 0, 500); len(off) != 1 || off[0] != [2]int32{0, 0} {
		t.Fatalf("单体卡必须落在原点，实际 %v", off)
	}
	if off := SummonLayout(0, 0, 500); len(off) != 1 || off[0] != [2]int32{0, 0} {
		t.Fatalf("count=0 必须退化成 1 个原点，实际 %v", off)
	}
	if off := SummonLayout(3, 0, 0); len(off) != 3 {
		t.Fatalf("count 不符：%v", off)
	}
	// pack 分支的 n<=8 与 n>8 两条路都要有点数正确
	if off := PackOffsets(8, 500); len(off) != 8 {
		t.Fatalf("PackOffsets(8) 点数 %d", len(off))
	}
	if off := PackOffsets(15, 500); len(off) != 15 {
		t.Fatalf("PackOffsets(15) 点数 %d", len(off))
	}
	// 第一个点固定落在原点（参考实现 `out: list = [(0, 0)]`），n>8 时才有。
	if off := PackOffsets(15, 500); off[0] != [2]int32{0, 0} {
		t.Fatalf("n>8 的 pack 首点应为原点，实际 %v", off[0])
	}
}

// TestRingOffsetsExplicitRadiusKept —— 显式半径必须原样生效，且 `count<=1` / `radius<=0`
// 落原点（参考实现 `ring_offsets` 的早返回）。
func TestRingOffsetsExplicitRadiusKept(t *testing.T) {
	if off := RingOffsets(1, 700, 0); len(off) != 1 || off[0] != [2]int32{0, 0} {
		t.Fatalf("count=1 必须落原点，实际 %v", off)
	}
	if off := RingOffsets(4, 0, 0); len(off) != 4 {
		t.Fatalf("radius=0 时点数应仍为 4，实际 %v", off)
	}
	off := RingOffsets(4, 800, 0)
	for _, p := range off {
		if d := math.Hypot(float64(p[0]), float64(p[1])); math.Abs(d-800) > 1 {
			t.Fatalf("显式半径 800 被改写：点 %v 距原点 %.1f", p, d)
		}
	}
}
