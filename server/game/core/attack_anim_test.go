package core

import "testing"

// 攻击档的两条硬约束（离线判据）：
//
//	① 一次挥砍必须在客户端 10 Hz 的快照流里**被采到一次**。快照周期是项目约定
//	   （game/logic `snapshotEveryTicks = 2` × 50 ms = 100 ms；客户端
//	   `GameConst.SnapshotIntervalMs` 同值）。core 是纯 Go、⛔ 不许 import game/logic，
//	   所以约定在这里重申一次，断言直接打在 `attackAnimHoldTicks` 的取值上，
//	   ⛔ 不为某个单位写死。
//	② 相邻两次挥砍必须给客户端一个**新的上升沿**（`View/UnitView.Apply` 在 AnimAttack
//	   的上升沿重播攻击档）⇒ 攻击档窗口必须**严格短于该单位的 hit_speed_ms**。
const snapshotPeriodTicks = 2

// TestAttackAnimWindowPerSwing 驱动一个持续攻击的单位（骑士站进红方公主塔的攻击
// 距离内原地挥砍），逐 tick 记录 anim 与挥砍次数，断言：
//
//   - 前摇开始（= 距离出刀 load_time_ms 的那一 tick）起 anim == AnimAttack；
//   - 每次挥砍后 anim == AnimAttack 至少保持 attackAnimHoldTicks 个 tick；
//   - 任意 100 ms 快照窗口里都有这次挥砍被采到（窗口内存在快照 tick）；
//   - 窗口之后至少一个 tick 不是 AnimAttack（= 相邻两刀不被并成一段）；
//   - 每个 AnimAttack 上升沿后面都对应一次挥砍（唯一例外是收尾那次**还没走到出刀**
//     的前摇 —— 跑完时它还没落刀，见下面的 edges 断言）；
//   - 窗口 < 该单位的 hit_speed_ms，且 >= 2 个快照周期。
func TestAttackAnimWindowPerSwing(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 7)
	knight, ok := b.cfg.Table.Unit("Knight")
	if !ok {
		t.Fatal("fixture: no Knight row")
	}
	tower := redPrincessTower(t, b)

	// reach = range 1200 + 骑士半径 500 + 塔半径 1000 + 1 = 2701 milli ⇒ 间距 2000
	// 就在射程内，骑士原地持续挥砍（位移不会介入这次读数）。
	const standOffMilli = 2000
	k := b.spawnUnit(TeamBlue, knight, cardKnight, tower.xMilli, tower.yMilli-standOffMilli, 0)
	if !canTarget(k, tower) || !inAttackRange(k, tower) {
		t.Fatalf("fixture: the knight is not in range of the tower (d=%d)",
			Distance(k.xMilli, k.yMilli, tower.xMilli, tower.yMilli))
	}

	const ticks = 180
	animAt := make([]int32, ticks+1) // animAt[t] = 第 t 个 tick 走完后的 anim；下标 0 保持 AnimIdle
	var swingAt []int
	for i := 1; i <= ticks; i++ {
		before := k.swings
		b.Step()
		animAt[i] = k.anim
		if k.swings > before {
			swingAt = append(swingAt, i)
		}
	}
	// 读数原文：挥砍 tick 与每 tick 的 anim（`S` = 这一 tick 落下一刀）。
	var tl string
	for i := 1; i <= 44; i++ {
		if i == swingAt[0] || (len(swingAt) > 1 && i == swingAt[1]) {
			tl += " S"
		}
		tl += " " + string(rune('0'+animAt[i]))
	}
	t.Logf("swings=%v | tick 1..44 anim (S = swing):%s", swingAt[:min(3, len(swingAt))], tl)
	if !k.alive {
		t.Fatalf("fixture: the knight died inside %d ticks -- shorten the run", ticks)
	}
	if len(swingAt) < 5 {
		t.Fatalf("fixture: only %d swings in %d ticks, want >= 5", len(swingAt), ticks)
	}

	if hold := attackAnimHoldTicks * MSecPerTick; hold >= knight.HitSpeedMs {
		t.Fatalf("attack window %d ms >= hit_speed_ms %d: consecutive swings would merge into a single run",
			hold, knight.HitSpeedMs)
	}
	if attackAnimHoldTicks < 2*snapshotPeriodTicks {
		t.Fatalf("attack window %d ticks < 2 snapshot periods (%d ticks): the snapshot grid could miss a swing",
			attackAnimHoldTicks, 2*snapshotPeriodTicks)
	}

	// 前摇：到出刀那一刻为止的 load_time 里，attack 档必须是抬着的 —— 客户端就是靠
	// 这个上升沿起手（起手后再等 load_time 才是这一刀的落点）。
	loadTicks := TicksForMs(knight.LoadTimeMs)
	if loadTicks < 2 {
		t.Fatalf("fixture: load_time %d ms = %d ticks is too short to observe a windup", knight.LoadTimeMs, loadTicks)
	}
	hold := k.attackAnimHold()

	for _, sw := range swingAt {
		// 前摇：从 `sw - loadTicks + 1` 起（含出刀那一 tick 共 loadTicks 个 tick）都是攻击档。
		for d := int(loadTicks) - 1; d >= 0; d-- {
			tick := sw - d
			if tick < 1 {
				continue
			}
			if animAt[tick] != AnimAttack {
				t.Fatalf("swing at tick %d: anim=%d at tick %d (windup), want AnimAttack(%d)",
					sw, animAt[tick], tick, AnimAttack)
			}
		}
		// 前摇之前必须是释放的（否则客户端看不到"抬手"的上升沿）。
		if tick := sw - int(loadTicks); tick >= 1 && animAt[tick] == AnimAttack && animAt[tick-1] == AnimAttack {
			t.Fatalf("swing at tick %d: no release before the windup (tick %d is already attack)", sw, tick)
		}
		// 窗口：挥砍那一 tick 起连续 attackAnimHoldTicks 个 tick 都必须是攻击档。
		for d := 0; d < int(hold); d++ {
			tick := sw + d
			if tick > ticks {
				t.Fatalf("run of %d ticks is too short to observe the window of the swing at tick %d", ticks, sw)
			}
			if animAt[tick] != AnimAttack {
				t.Fatalf("swing at tick %d: anim=%d at tick %d, want AnimAttack(%d)",
					sw, animAt[tick], tick, AnimAttack)
			}
		}
		// 100 ms 快照网格：窗口内必须存在一个快照 tick（否则客户端整刀看不见）。
		sampled := false
		for d := 0; d < int(hold); d++ {
			if (sw+d)%snapshotPeriodTicks == 0 {
				sampled = true
				break
			}
		}
		if !sampled {
			t.Fatalf("swing at tick %d: no snapshot tick inside the %d-tick window", sw, hold)
		}
		// 释放：窗口之后至少一个 tick 不是攻击档 —— 下一刀的上升沿靠它。
		if after := sw + int(hold); after <= ticks && animAt[after] == AnimAttack {
			t.Fatalf("swing at tick %d: the attack tier is still set at tick %d -- two swings merged, the client would play one attack",
				sw, after)
		}
	}

	edges := 0
	prev := int32(AnimIdle)
	for i := 1; i <= ticks; i++ {
		if animAt[i] == AnimAttack && prev != AnimAttack {
			edges++
		}
		prev = animAt[i]
	}
	// 上升沿 == 挥砍数，除非跑完那一 tick 还停在一段**尚未出刀**的前摇里（最后那次
	// 上升沿的出刀落在 ticks 之后）。
	extra := 0
	if edges > len(swingAt) {
		lastEdge := 0
		prev = AnimIdle
		for i := 1; i <= ticks; i++ {
			if animAt[i] == AnimAttack && prev != AnimAttack {
				lastEdge = i
			}
			prev = animAt[i]
		}
		if lastEdge+int(loadTicks)-1 > ticks {
			extra = 1
		}
	}
	if edges != len(swingAt)+extra {
		t.Fatalf("AnimAttack rising edges=%d but swings=%d (+%d unfinished windup): every swing must produce exactly one fresh attack",
			edges, len(swingAt), extra)
	}
}

// TestAttackWindupKeepsDamageOnTheSwingTick 断言把攻击档提前到前摇**不移动伤害**：
// 塔掉血只发生在出刀那一 tick，前摇期间一 tick 都不掉（伤害落在前摇结束，见
// cr-sim engine/combat.py:6-18）。
func TestAttackWindupKeepsDamageOnTheSwingTick(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 17)
	knight, _ := b.cfg.Table.Unit("Knight")
	tower := redPrincessTower(t, b)
	k := b.spawnUnit(TeamBlue, knight, cardKnight, tower.xMilli, tower.yMilli-2000, 0)

	loadTicks := TicksForMs(knight.LoadTimeMs)
	hpStart := tower.hp
	b.Step()
	// 前摇第一 tick 就已是攻击档……
	if k.anim != AnimAttack {
		t.Fatalf("the windup's first tick reads anim=%d, want AnimAttack(%d)", k.anim, AnimAttack)
	}
	if tower.hp != hpStart {
		t.Fatalf("damage landed on the windup's first tick: hp %d -> %d", hpStart, tower.hp)
	}
	// ……而伤害还要等 load_time 才落。
	for i := 1; i < int(loadTicks)-1; i++ {
		b.Step()
		if tower.hp != hpStart {
			t.Fatalf("damage landed %d ticks into the windup (load_time = %d ticks): hp %d -> %d",
				i+1, loadTicks, hpStart, tower.hp)
		}
		if k.anim != AnimAttack {
			t.Fatalf("the attack tier dropped during the windup at tick %d (anim=%d)", i+1, k.anim)
		}
	}
	b.Step() // 第 loadTicks 个 tick：出刀
	if k.swings != 1 {
		t.Fatalf("swings after load_time = %d, want 1", k.swings)
	}
	if tower.hp >= hpStart {
		t.Fatalf("the hit did not land on the swing tick (hp %d -> %d)", hpStart, tower.hp)
	}
	t.Logf("windup: attack tier from tick 1 (load_time %d ticks), first damage exactly on tick %d, tower hp %d -> %d",
		loadTicks, loadTicks, hpStart, tower.hp)
}

// TestAttackAnimWindowEndsOnDeath 断言死亡档不被窗口的关闭覆盖：窗口内死亡的单位
// 必须一直停在 AnimDie（否则尸体会"站回"待机 / 走路）。
func TestAttackAnimWindowEndsOnDeath(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 11)
	knight, _ := b.cfg.Table.Unit("Knight")
	tower := redPrincessTower(t, b)
	k := b.spawnUnit(TeamBlue, knight, cardKnight, tower.xMilli, tower.yMilli-2000, 0)

	// 让这一次调用直接挥砍：目标不变（不重开前摇）、冷却为 0（风门已满）。
	k.st.targetID = tower.ID
	k.st.cooldownMs = 0
	if h := advanceAttack(k, tower, b.tick); h == nil {
		t.Fatal("fixture: advanceAttack decided no hit")
	}
	if k.anim != AnimAttack || k.animHoldUntilTick <= b.tick {
		t.Fatalf("after the swing: anim=%d until=%d tick=%d, want anim=%d with an open window",
			k.anim, k.animHoldUntilTick, b.tick, AnimAttack)
	}

	b.damageEntity(k, k.hp, nil)
	if k.alive || k.anim != AnimDie {
		t.Fatalf("after the killing blow: alive=%v anim=%d, want dead with anim=%d", k.alive, k.anim, AnimDie)
	}
	if k.animHoldUntilTick != 0 {
		t.Fatalf("the death frame left the attack window open: until=%d", k.animHoldUntilTick)
	}
	b.stepAnimHold()
	if k.anim != AnimDie {
		t.Fatalf("the attack window overwrote the death frame: anim=%d, want %d", k.anim, AnimDie)
	}
}

// TestAttackAnimHoldKeepsMovesAlive 断言攻击窗口只扣住**档位**、不拦移动本身：
// 窗口内写入移动档无效，窗口一关就落回当时该有的档（射程内原地 ⇒ idle）。
func TestAttackAnimHoldKeepsMovesAlive(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 13)
	knight, _ := b.cfg.Table.Unit("Knight")
	tower := redPrincessTower(t, b)
	k := b.spawnUnit(TeamBlue, knight, cardKnight, tower.xMilli, tower.yMilli-2000, 0)

	for i := 0; i < 40 && k.swings == 0; i++ {
		b.Step()
	}
	if k.swings == 0 {
		t.Fatal("fixture: the knight never swung")
	}
	if k.animHoldUntilTick <= b.tick {
		t.Fatalf("fixture: no open window after the swing (until=%d tick=%d)", k.animHoldUntilTick, b.tick)
	}

	// 窗口内：移动档写入被扣住。
	k.moving = true
	k.setMoveAnim(AnimWalk)
	if k.anim != AnimAttack {
		t.Fatalf("setMoveAnim overwrote the held attack tier: anim=%d, want %d", k.anim, AnimAttack)
	}

	// 窗口关闭：落回当时该有的档。
	for i := 0; i <= int(attackAnimHoldTicks) && k.anim == AnimAttack; i++ {
		b.Step()
	}
	if k.anim == AnimAttack {
		t.Fatalf("the attack tier never came back down (until=%d tick=%d)", k.animHoldUntilTick, b.tick)
	}
	if k.anim != AnimIdle {
		t.Fatalf("after the window: anim=%d, want AnimIdle(%d) (in range, not moving)", k.anim, AnimIdle)
	}
}

// redPrincessTower returns Red's left princess tower (the roster is ordered
// king / left / right per team, blue first).
func redPrincessTower(t *testing.T, b *Battle) *entity {
	t.Helper()
	for _, e := range b.roster {
		if e.team == TeamRed && e.kind == TowerKindPrincess {
			return e.ent
		}
	}
	t.Fatal("no red princess tower on the board")
	return nil
}
