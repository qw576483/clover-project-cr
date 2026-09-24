package core

import "testing"

// faceToward 只区分左右（facing ∈ {-1, +1}）。目标在**正上 / 正下**（x 与自身相等）时，
// 旧实现在该分支里什么都不做 ⇒ 保留上一次朝向 ——「明明朝自己半场，却对着正上方的目标保持背身」。
// 补正口径（entity.go 的 faceToward 注释）= 回退到「面向敌方半场」，与出生朝向同一基准
// facingFor（combat.go:395-401：蓝=+1 / 红=-1）。
//
// ⛔ 本用例是**可失败**的：把 faceToward 的 else 分支改回「不赋值」，下面两条 x 相等断言立即判红。
func TestFaceToward(t *testing.T) {
	blue := &entity{Team: TeamBlue, xMilli: 9000}
	red := &entity{Team: TeamRed, xMilli: 9000}

	// ① 目标在右 / 左：按 x 大小定左右（原语义，未改）。
	blue.faceToward(9500)
	if blue.facing != 1 {
		t.Fatalf("目标在右侧：facing=%d want 1", blue.facing)
	}
	blue.faceToward(8500)
	if blue.facing != -1 {
		t.Fatalf("目标在左侧：facing=%d want -1", blue.facing)
	}

	// ② 目标在正上 / 正下（x 相等）：必须回退到「面向敌方半场」，不得保留旧的背身朝向。
	blue.facing = -1 // 人为造成"背身"
	blue.faceToward(blue.xMilli)
	if blue.facing != 1 {
		t.Fatalf("蓝方正上方目标：facing=%d want 1（facingFor(TeamBlue)，面向敌方半场）", blue.facing)
	}

	red.facing = 1 // 人为造成"背身"
	red.faceToward(red.xMilli)
	if red.facing != -1 {
		t.Fatalf("红方正下方目标：facing=%d want -1（facingFor(TeamRed)）", red.facing)
	}
}
