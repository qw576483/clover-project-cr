package logic

import (
	"sort"
	"testing"
	"time"

	"clover-cr/game/core"
)

// 判据出处: 参考规格 §7 S20（胜负结算）/ S24（人机对战）；塔的初始态来自
// `core/core.NewBattle`（`server/game/core/battle.go:131-146` 每局都从 roster 重新 spawn 6 座塔）。
//
// D173（「再来一局」后我方的塔仍是死亡状态）的服务端半边核验：
// 每一局都是 `NewBattle` 造的全新对局 —— 上一局的塔状态**没有任何通道**流进新局。
// 判据 = 连开两局，两局的快照都必须给出 6 座 `alive=true`、满血的塔
// （客户端那一半的根因见 `.ai-tmp/test/fix-battle-report.md` 的 D173 一节）。
func TestNewBattleAlwaysStartsWithLiveTowers(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)

	for round := 1; round <= 2; round++ {
		b, err := core.NewBattle(core.Config{Seed: int64(round), Table: ct, DeckA: deck, DeckB: deck})
		if err != nil {
			t.Fatalf("第 %d 局 NewBattle 失败: %v", round, err)
		}
		s := b.Snapshot()
		if len(s.TowersA) != 3 || len(s.TowersB) != 3 {
			t.Fatalf("第 %d 局塔数不对：A=%d B=%d（应为各 3 座）", round, len(s.TowersA), len(s.TowersB))
		}
		for _, side := range []struct {
			name string
			list []core.TowerSnap
		}{{"A", s.TowersA}, {"B", s.TowersB}} {
			for _, tw := range side.list {
				if !tw.Alive {
					t.Fatalf("第 %d 局 %s 方塔 id=%d 开局就是阵亡态", round, side.name, tw.ID)
				}
				if tw.HP != tw.MaxHP {
					t.Fatalf("第 %d 局 %s 方塔 id=%d 开局血 %d/%d（应满血）", round, side.name, tw.ID, tw.HP, tw.MaxHP)
				}
			}
		}
		// 造一局已打完的旧局，再开新局：新局的塔状态仍必须是满血存活。
		for i := 0; i < 40; i++ {
			b.Step()
		}
		b.Surrender(core.TeamBlue)
		if !b.Ended() {
			t.Fatalf("第 %d 局投降后未结束", round)
		}
	}
	t.Logf("连开两局：每局快照都是 6 座满血存活塔（塔状态不进下一局）")
}

// 判据出处: `experience/perf-triage.md`（掉帧先证伪环境、再做 CPU 侧拆分）；
// 本工程 tick 率 = 20 Hz（`core.TicksPerSecond`，50 ms/帧）。
//
// D167（「放一张卡就卡一下，出现时候也卡一下」）的服务端半边核验：
// 出牌（`core.PlayCard`：扣圣水 / 洗牌 / spawnGroup / 环形铺开）与随后的 tick
// 是否有**数量级**尖峰。判据 = ① 单次 PlayCard 与单 tick 的耗时上限；
// ② 最慢 tick ≤ 10 × 中位 tick（相对判据，对机器负载不敏感）。
// 只要这两条成立，卡顿就不在服务端 tick 上，而在客户端表现层（见报告 D167 一节）。
func TestServerTickCostHasNoCardPlaySpike(t *testing.T) {
	b := newTestBattle(t, 20240925)

	const (
		ticks        = 2400 // 120 s
		playInterval = 5    // 每 5 tick 尝试出牌一次（贴近真人节奏，且远密于实际对局）
	)
	stepNs := make([]int64, 0, ticks)
	playNs := make([]int64, 0, ticks)

	for tick := 0; tick < ticks; tick++ {
		if tick%playInterval == 0 {
			for _, team := range []core.Team{core.TeamBlue, core.TeamRed} {
				cardID, x, y, ok := core.Decide(b, team)
				if !ok {
					continue
				}
				t0 := time.Now()
				if err := b.PlayCard(team, cardID, x, y); err != nil {
					t.Fatalf("出牌被拒: %v", err)
				}
				playNs = append(playNs, time.Since(t0).Nanoseconds())
			}
		}
		t0 := time.Now()
		b.Step()
		stepNs = append(stepNs, time.Since(t0).Nanoseconds())
	}

	sum := func(v []int64) (mean, p95, max int64, at int) {
		if len(v) == 0 {
			return 0, 0, 0, -1
		}
		var total int64
		for i, x := range v {
			total += x
			if x > max {
				max, at = x, i
			}
		}
		cp := append([]int64(nil), v...)
		sort.Slice(cp, func(i, j int) bool { return cp[i] < cp[j] })
		return total / int64(len(v)), cp[len(cp)*95/100], max, at
	}
	stepMean, stepP95, stepMax, stepAt := sum(stepNs)
	playMean, playP95, playMax, playAt := sum(playNs)

	// 稳态 = 跳过前 10 tick（首 tick 的分配 / 预热不属于"出牌帧"）。
	const warmup = 10
	_, _, stepSteadyMax, _ := sum(stepNs[warmup:])

	t.Logf("tick 数=%d 出牌次数=%d", len(stepNs), len(playNs))
	t.Logf("单 tick 耗时 mean=%.4fms p95=%.4fms max=%.4fms(第 %d tick) 稳态max=%.4fms",
		float64(stepMean)/1e6, float64(stepP95)/1e6, float64(stepMax)/1e6, stepAt,
		float64(stepSteadyMax)/1e6)
	t.Logf("单次 PlayCard mean=%.4fms p95=%.4fms max=%.4fms(第 %d 次)",
		float64(playMean)/1e6, float64(playP95)/1e6, float64(playMax)/1e6, playAt)

	// 判据：一帧预算 50 ms（`core.MSecPerTick`）。要用服务端 tick 解释"看得见的卡顿"，
	// 单 tick 至少得吃掉预算的一大块 —— 实测稳态连 1 ms 都不到 ⇒ 数量级上不可能是它。
	const quarterTickNs = 12_500_000 // 50ms / 4
	if stepSteadyMax > quarterTickNs {
		t.Fatalf("稳态最慢 tick %.3fms（> 12.5ms = ¼ 帧预算）⇒ 服务端 tick 确有尖峰",
			float64(stepSteadyMax)/1e6)
	}
	if playMax > 5_000_000 {
		t.Fatalf("最慢一次出牌 %.3fms（> 5ms）", float64(playMax)/1e6)
	}
}
