package core

import (
	"testing"
)

// TestAttackRangeGapSemantics pins the *gap* semantics of the attack range:
// a range describes the space between two units' hitboxes, not between their
// centres (参考实现 `cr-sim/cr_sim/engine/targeting.py::gap_between` /
// `within_gap`).
//
// 为什么单独钉这条（差异登记 D141）
// ---------------------------------
// 用户第 2 条报「部分卡攻击范围不对（大皮卡 可老远就打到我了）」。
// 复核结论：**数据侧没有缺陷** ——
//   · `server/game/table/tsv/unit.tsv` 的 `range_mt` / `radius_mt` 与官方
//     `原版资源/cr-api-data/docs/json/*.json` 的 `range` / `collision_radius`
//     **逐行相等**（91 个数据行，0 处不等）。PEKKA 官方 `range=1200`、
//     `collision_radius=750`，本表同值；公主塔官方 `collision_radius=1000`，
//     本表同值（⛔ 不是某些记录里写的 1500 —— 1500 是
//     `KingFootprintHalfMilli` 这个**部署区**常量，不是碰撞半径）。
//   · `inAttackRange` 的算式与参考实现同构，**并且本轮把那个 `+1` 补齐**
//     （参考实现原文：`floor(sqrt(d2)) <= reach+ra+rt` ⟺ `d2 < (reach+ra+rt+1)^2`
//     对整数恒等）。补齐前我们恰好在"整数距离正好等于上限"这一格上比参考
//     更严 1 subtile。
//
// 本测试的作用 = 把这条口径钉死并留证：命中判定的中心距上限 =
// range + 攻击者半径 + 目标半径，**含上界**；越界 1 milli 必须为假（负控）。
// 任何"把半径加两遍 / 把射程当净射程 / 把塔半径从 1000 改成 1500"的改动都会红。
//
// ⚠️ 残余（登记，未证实）：用户看到"老远"的另一可能来源是**视觉**——
// 贴图尺寸与碰撞体尺寸并不相等（原版单位贴图比它的碰撞圆大），这条要另立片
// 量两侧 art 的像素宽才能判定，本片不动。
func TestAttackRangeGapSemantics(t *testing.T) {
	// 官方数据（出处 = cr-api-data cards_stats_characters.json / cards_stats_building.json）
	pekka := &UnitDef{Key: "pekka", Kind: KindTroop, RangeMilli: 1200, RadiusMilli: 750,
		AtkGround: true, HitSpeedMs: 1800}
	knight := &UnitDef{Key: "knight", Kind: KindTroop, RangeMilli: 1200, RadiusMilli: 500,
		AtkGround: true, HitSpeedMs: 1200}
	princessTower := &UnitDef{Key: KeyPrincessTower, Kind: KindTower, RangeMilli: 7500, RadiusMilli: 1000}

	mk := func(def *UnitDef, x, y int32) *entity {
		return &entity{ID: 1, Def: def, xMilli: x, yMilli: y, hp: 1000, alive: true}
	}

	cases := []struct {
		name   string
		atk    *UnitDef
		tgt    *UnitDef
		dist   int32
		want   bool
		why    string
	}{
		// PEKKA(750) vs Knight(500): 上限 = 1200+750+500 = 2450。
		{"pekka vs knight @2449", pekka, knight, 2449, true,
			"上限内 1 milli ⇒ 命中"},
		{"pekka vs knight @2450 上界", pekka, knight, 2450, true,
			"正好等于上限 ⇒ **含上界**（参考实现 `d2 < (limit+1)^2`）"},
		{"pekka vs knight @2451", pekka, knight, 2451, false,
			"越界 1 milli ⇒ 必须为假（负控）"},
		// PEKKA(750) vs 公主塔(1000): 上限 = 1200+750+1000 = 2950。
		{"pekka vs princess tower @2949", pekka, princessTower, 2949, true,
			"塔半径必须用官方的 1000，上限才是 2950"},
		{"pekka vs princess tower @2951", pekka, princessTower, 2951, false,
			"若有人把塔半径写成 1500（KingFootprintHalfMilli 的误用），这里会翻成真 ⇒ 本行就是那条错法的负控"},
		{"pekka vs tower @3450（旧登记算错的数）", pekka, princessTower, 3450, false,
			"3.45 格 = 旧 D141 记录里「塔半径当 1500」算出来的数，必须为假"},
		// 公主塔(1000) 打 PEKKA：上限 = 7500+1000+750 = 9250。
		{"princess tower vs pekka @9249", princessTower, pekka, 9249, true, "塔射程 7.5 格 + 双方半径"},
		{"princess tower vs pekka @9251", princessTower, pekka, 9251, false, "越界 1 milli ⇒ 假（负控）"},
	}

	for _, c := range cases {
		atk := mk(c.atk, 9000, 9000)
		tgt := mk(c.tgt, 9000+c.dist, 9000)
		if got := inAttackRange(atk, tgt); got != c.want {
			t.Errorf("%s: inAttackRange = %v, want %v（%s）", c.name, got, c.want, c.why)
		}
		// 同一对实体，距离换成纵向也必须一致（判定不许只对 x 轴成立）。
		atkV := mk(c.atk, 9000, 9000)
		tgtV := mk(c.tgt, 9000, 9000+c.dist)
		if got := inAttackRange(atkV, tgtV); got != c.want {
			t.Errorf("%s（纵向）: inAttackRange = %v, want %v", c.name, got, c.want)
		}
	}

	// 口径自证：命中上限必须**同时**含双方半径，缺一不可。
	//
	// ⛔ 这里不许写成"在 1700 处必须判假" —— 1700 = range+rt 只是**错口径**的
	// 上限，真口径在 1700 处本来就该判真（1700 ≪ 2450）。那种写法没有鉴别力，
	// 还会把我们自己的正确实现判红。
	//
	// 有鉴别力的写法：在"错上限"之外、真上限之内各取 1 个点，断言**必须判真**。
	// 漏掉哪个半径，对应的那一点就会翻成假。
	atk := mk(pekka, 9000, 9000)
	if !inAttackRange(atk, mk(knight, 9000+1200+750+500, 9000)) {
		t.Fatalf("上限算式不自洽：range+ra+rt = %d 处必须命中", 1200+750+500)
	}
	// 漏攻击者半径的错口径 ⇒ 上限 1200+500 = 1700；1701 必须判真。
	if !inAttackRange(atk, mk(knight, 9000+1200+500+1, 9000)) {
		t.Fatalf("漏掉攻击者半径的错口径在 %d 处会判假，真口径必须判真 ⇒ 我们漏了攻击者半径",
			1200+500+1)
	}
	// 漏目标半径的错口径 ⇒ 上限 1200+750 = 1950；1951 必须判真。
	if !inAttackRange(atk, mk(knight, 9000+1200+750+1, 9000)) {
		t.Fatalf("漏掉目标半径的错口径在 %d 处会判假，真口径必须判真 ⇒ 我们漏了目标半径",
			1200+750+1)
	}
	t.Logf("range 口径钉住：centre <= range + attacker.r + target.r "+
		"(PEKKA range=%d vs Knight r=%d ⇒ %d；vs PrincessTower r=%d ⇒ %d)",
		pekka.RangeMilli, knight.RadiusMilli, 1200+750+500,
		princessTower.RadiusMilli, 1200+750+1000)
}
