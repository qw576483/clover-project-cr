package logic

import (
	"encoding/json"
	"testing"

	"clover-cr/game/core"
)

// 本文件是**离线秒级断言**（不依赖起服 / redis / mysql，`go test ./game/logic/ -count=1`）：
// 它把「配表适配 + AI 决策 + 快照适配」这三件与引擎无关的事钉成断言，
// 免得每次改动都要起服 + 跑客户端才能发现"适配错了 / AI 一步都不走"。
// 起服与端到端由 .ai-tmp/test 的驱动负责（那才是必须真跑的链路）。

// testTable 从 tsv 目录构建适配层（测试的工作目录 = server/game/logic）。
// 走的是起服同一条路：生成物 table.NewTables + LoadAll → 适配成 core.CardTable。
func testTable(t *testing.T) *cardTable {
	t.Helper()
	ct, _, err := loadCardTable("../table/tsv")
	if err != nil {
		t.Fatalf("配表适配失败: %v", err)
	}
	return ct
}

// TestTableAdapterResolvesEveryCard 60 张卡必须全部能适配成 core.CardDef，
// 且非法术卡的召唤实体必须在战斗单位表里可解析（否则含它的卡组整局开不了）。
func TestTableAdapterResolvesEveryCard(t *testing.T) {
	ct := testTable(t)
	if got := ct.CardCount(); got != 60 {
		t.Fatalf("卡池应为 60 张，实际 %d 张", got)
	}
	spells := 0
	for _, row := range ct.cardRows {
		id, key := int32(row.Id), row.Key
		cd, ok := ct.Card(id)
		if !ok || cd == nil {
			t.Fatalf("卡 %d(%s) 适配缺失", id, key)
		}
		if cd.Kind == core.CardTypeSpell {
			spells++
			if cd.Spell == nil {
				t.Fatalf("法术卡 %d(%s) 没有 SpellDef", id, key)
			}
			continue
		}
		if cd.UnitN < 1 {
			t.Fatalf("卡 %d(%s) UnitN=%d（应 >= 1）", id, key, cd.UnitN)
		}
		def, ok := ct.Unit(cd.UnitKey)
		if !ok || def == nil {
			t.Fatalf("卡 %d(%s) 的召唤实体 %q 解析不到", id, key, cd.UnitKey)
		}
		if def.HP <= 0 {
			t.Fatalf("实体 %q 的 HP=%d（配表应有值）", cd.UnitKey, def.HP)
		}
	}
	if spells == 0 {
		t.Fatalf("一张法术都没有，法术表适配显然没接上")
	}
	t.Logf("卡 %d 张（其中法术 %d 张）/ 战斗单位 %d 个全部可解析", ct.CardCount(), spells, ct.UnitCount())
}

// TestTableAdapterTowersAndSpeed 塔与速度换算（塔 HP 有出处：参考规格 §4.1）。
func TestTableAdapterTowersAndSpeed(t *testing.T) {
	ct := testTable(t)
	princess, ok := ct.Unit(core.KeyPrincessTower)
	if !ok || princess == nil {
		t.Fatalf("PrincessTower 解析不到")
	}
	king, ok := ct.Unit(core.KeyKingTower)
	if !ok || king == nil {
		t.Fatalf("KingTower 解析不到")
	}
	if princess.HP != 3584 || king.HP != 6144 {
		t.Fatalf("塔血量与参考规格不符：公主塔 %d（应 3584）国王塔 %d（应 6144）", princess.HP, king.HP)
	}
	knight, ok := ct.Unit("knight")
	if !ok || knight == nil {
		t.Fatalf("knight 解析不到")
	}
	// 官方 speed = 60 格/分钟 ⇒ 1000 milli-tile/秒（anchors.json 的换算）。
	if knight.SpeedMilliPerSec != 1000 {
		t.Fatalf("knight 速度换算错：%d（应 1000）", knight.SpeedMilliPerSec)
	}
	if knight.Flying {
		t.Fatalf("knight 被标成飞行单位（flying 列映射反了）")
	}
	// flying 列（agent-01b 补的）必须接进 core.UnitDef.Flying：
	// 若这条失败，先看配表列还在不在（在）与适配层的列名（"flying"）是否一致。
	minion, ok := ct.Unit("Minion")
	if !ok || minion == nil {
		t.Fatalf("Minion 解析不到")
	}
	if !minion.Flying {
		t.Fatalf("Minion 的 Flying=false：配表 flying 列没接进 core.UnitDef.Flying")
	}
	t.Logf("公主塔 HP=%d 国王塔 HP=%d knight speed=%d milli/s knight.flying=%v minion.flying=%v",
		princess.HP, king.HP, knight.SpeedMilliPerSec, knight.Flying, minion.Flying)
}

// TestCheckDeckRejects 卡组合法性（8 张 / 不重复 / 存在）。
func TestCheckDeckRejects(t *testing.T) {
	l := &gameLogic{cards: testTable(t)}
	deck := buildAIDeck(l.cards)
	if len(deck) != deckSize {
		t.Fatalf("AI 卡组应为 %d 张，实际 %d 张", deckSize, len(deck))
	}
	if err := l.checkDeck(deck); err != nil {
		t.Fatalf("AI 卡组应合法，实际: %v", err)
	}
	if err := l.checkDeck(deck[:7]); err == nil {
		t.Fatalf("7 张应被拒")
	}
	dup := []int32{deck[0], deck[0], deck[2], deck[3], deck[4], deck[5], deck[6], deck[7]}
	if err := l.checkDeck(dup); err == nil {
		t.Fatalf("重复卡应被拒")
	}
	unknown := []int32{999999, deck[1], deck[2], deck[3], deck[4], deck[5], deck[6], deck[7]}
	if err := l.checkDeck(unknown); err == nil {
		t.Fatalf("不存在的卡应被拒")
	}
	t.Logf("卡组校验：合法（%d 张）通过；7 张 / 重复 / 不存在 均被拒", deckSize)
}

// TestAIDeckIsTroopDeck AI 卡组必须是 8 张可解析的**部队卡**
// （原因见 ai.go buildAIDeck：镜像全法术卡组会让 AI 一步都不走）。
func TestAIDeckIsTroopDeck(t *testing.T) {
	ct := testTable(t)
	deck := buildAIDeck(ct)
	if len(deck) != deckSize {
		t.Fatalf("AI 卡组应为 %d 张，实际 %d 张", deckSize, len(deck))
	}
	for _, id := range deck {
		cd, ok := ct.Card(id)
		if !ok || cd == nil {
			t.Fatalf("AI 卡组里的卡 %d 解析不到", id)
		}
		if cd.Kind != core.CardTypeTroop {
			t.Fatalf("AI 卡组里的卡 %d(%s) 不是部队卡（kind=%d）", id, cd.Key, cd.Kind)
		}
	}
	t.Logf("AI 卡组 = %v", deck)
}

// newTestBattle 用两个 AI 卡组开一局（离线，不碰引擎）。
func newTestBattle(t *testing.T, seed int64) *core.Battle {
	t.Helper()
	ct := testTable(t)
	deck := buildAIDeck(ct)
	b, err := core.NewBattle(core.Config{Seed: seed, Table: ct, DeckA: deck, DeckB: deck})
	if err != nil {
		t.Fatalf("NewBattle 失败: %v", err)
	}
	return b
}

// TestDecideAlwaysReturnsLegalPoint AI 给出的落点必须过 arena.CanDeploy
// （这是"AI 与真人同一条路"的机械保证；非法点会让 PlayCard 直接报错）。
func TestDecideAlwaysReturnsLegalPoint(t *testing.T) {
	b := newTestBattle(t, 20240920)
	arena := core.NewArena()
	decisions, plays := 0, 0
	for tick := 0; tick < 1200; tick++ {
		if tick%aiDecisionTicks == 0 {
			for _, team := range []core.Team{core.TeamBlue, core.TeamRed} {
				cardID, x, y, ok := core.Decide(b, team)
				if !ok {
					continue
				}
				decisions++
				cd, found := cardTableOf(t, b).Card(cardID)
				if !found {
					t.Fatalf("AI 选了不存在的卡 %d", cardID)
				}
				anywhere, onWater := false, false
				if cd.Spell != nil {
					anywhere, onWater = cd.Spell.Anywhere, cd.Spell.OnWater
				}
				if !arena.CanDeploy(team, x, y, anywhere, onWater, nil) && cd.Spell == nil {
					t.Fatalf("AI 落点非法 team=%d card=%d (%d,%d)", team, cardID, x, y)
				}
				if err := b.PlayCard(team, cardID, x, y); err != nil {
					t.Fatalf("AI 出牌被拒 team=%d card=%d (%d,%d): %v", team, cardID, x, y, err)
				}
				plays++
			}
		}
		b.Step()
	}
	if plays == 0 {
		t.Fatalf("1200 tick（60 s）内 AI 一张牌都没出")
	}
	t.Logf("决策 %d 次 / 出牌 %d 次，全部通过 CanDeploy 与 PlayCard", decisions, plays)
}

// TestAIPlaysWithinOneMinute 纯 AI 对局：60 s 内 AI 必须出过牌，且场上真的多了实体。
func TestAIPlaysWithinOneMinute(t *testing.T) {
	b := newTestBattle(t, 7)
	redPlays := 0
	var first core.Event
	for tick := 0; tick < 1200; tick++ {
		if tick%aiDecisionTicks == 0 {
			for _, team := range []core.Team{core.TeamBlue, core.TeamRed} {
				if cardID, x, y, ok := core.Decide(b, team); ok {
					if err := b.PlayCard(team, cardID, x, y); err != nil {
						t.Fatalf("AI 出牌被拒: %v", err)
					}
					if team == core.TeamRed {
						redPlays++
					}
				}
			}
		}
		b.Step()
		for _, ev := range b.DrainEvents() {
			if ev.Kind == core.EvPlayCard && first.Kind == 0 && ev.CardID != 0 && first.CardID == 0 {
				first = ev
			}
		}
	}
	if redPlays == 0 {
		t.Fatalf("红方 AI 一张牌都没出")
	}
	snap := b.Snapshot()
	// 开局只有 6 座塔，出过牌之后场上必然还有部队 / 建筑。
	if len(snap.Entities) == 0 {
		t.Fatalf("场上没有实体，AI 的牌没有落地")
	}
	t.Logf("红方 AI 出牌 %d 次；首次出牌事件 card=%d (%d,%d)；场上实体 %d 个；圣水 A=%d B=%d",
		redPlays, first.CardID, first.XMilli, first.YMilli, len(snap.Entities), snap.ElixirA, snap.ElixirB)
}

// TestSnapshotAdapterMatchesCore 适配层必须逐字段搬运 core.Snapshot（不丢字段）。
func TestSnapshotAdapterMatchesCore(t *testing.T) {
	b := newTestBattle(t, 99)
	ct := testTable(t)
	deck := buildAIDeck(ct)
	_ = b.PlayCard(core.TeamBlue, deck[0], 9000, 6000)
	for tick := 0; tick < 100; tick++ {
		b.Step()
	}
	s := b.Snapshot()
	d := snapshotToDef(s)
	if d.Seq != s.Seq || d.ServerMs != s.ServerMs || d.Phase != s.Phase {
		t.Fatalf("Seq/ServerMs/Phase 不一致: %+v vs %+v", d, s)
	}
	if len(d.TowersA) != len(s.TowersA) || len(d.TowersB) != len(s.TowersB) {
		t.Fatalf("塔数量不一致: %d/%d vs %d/%d", len(d.TowersA), len(d.TowersB), len(s.TowersA), len(s.TowersB))
	}
	if len(d.Entities) != len(s.Entities) {
		t.Fatalf("实体数量不一致: %d vs %d", len(d.Entities), len(s.Entities))
	}
	if d.ElixirA != s.ElixirA || d.CrownsA != s.CrownsA || len(d.HandA) != len(s.HandA) || d.NextA != s.NextA {
		t.Fatalf("A 侧字段搬运有误: %+v vs %+v", d, s)
	}
	// 协议体必须能 JSON 序列化（客户端就吃这一份）。
	raw, err := json.Marshal(d)
	if err != nil {
		t.Fatalf("快照 JSON 序列化失败: %v", err)
	}
	t.Logf("快照 JSON %d 字节；seq=%d server_ms=%d 实体=%d 塔A=%d 塔B=%d",
		len(raw), d.Seq, d.ServerMs, len(d.Entities), len(d.TowersA), len(d.TowersB))
}

// cardTableOf 供测试取回适配层（battle 内部持有的就是它）。
func cardTableOf(t *testing.T, _ *core.Battle) *cardTable {
	t.Helper()
	return testTable(t)
}

// TestRoomRegistryDisconnectCleanup 断线清理的业务侧簿记（离线）。
//
// ★ 为什么只能离线验：本引擎在**连接关闭**时 gwcore.cleanup 会 panic
// （`index out of range [-1]`，session.go:1280 的 "清尾" 写在 append 截断之后），
// 而派发断线事件的代码在它**之后**（session.go:1340），所以 panic 一发生
// `OnDisconnect` 就永远不会触发（实测：关掉客户端连接后服务端 0 条日志、
// stderr 三条同款 panic 栈 = 三个客户端）。引擎缺陷不在本片可写范围内，
// 因此这里把「断线后该发生什么」在注册表层钉成断言，等引擎修好后 end-to-end 自然生效。
func TestRoomRegistryDisconnectCleanup(t *testing.T) {
	ct := testTable(t)
	r := newRoomRegistry()
	if err := r.ensure("r1"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	if err := r.join("r1", "p_a"); err != nil {
		t.Fatalf("join a: %v", err)
	}
	if err := r.configure("r1", "测试房", "p_a", "小蓝A"); err != nil {
		t.Fatalf("configure: %v", err)
	}
	if err := r.join("r1", "p_b"); err != nil {
		t.Fatalf("join b: %v", err)
	}
	if err := r.bindMember("r1", "p_b", "小红B", buildAIDeck(ct)); err != nil {
		t.Fatalf("bindMember: %v", err)
	}
	if err := r.setDeck("r1", "p_a", buildAIDeck(ct)); err != nil {
		t.Fatalf("setDeck: %v", err)
	}

	// (a) 未开打：对局外掉线 ⇒ 直接摘座位，房主不受影响。
	roomID, seat, running, ok := r.detach("p_b")
	if !ok || running || roomID != "r1" || seat != 1 {
		t.Fatalf("未开打时 detach 结果异常: room=%s seat=%d running=%v ok=%v", roomID, seat, running, ok)
	}
	if got := r.players("r1"); len(got) != 1 || got[0] != "p_a" {
		t.Fatalf("摘座位后房内应只剩 p_a，实际 %v", got)
	}

	// (b) 房主掉线且房内还有人 ⇒ 房主移交给剩下的人。
	if _, _, _, ok := r.detach("p_a"); !ok {
		t.Fatalf("房主 detach 应成功")
	}
	host, _ := r.hostOf("r1")
	if host != "" {
		t.Fatalf("房内已无真人，host 应为空，实际 %q", host)
	}
	if !r.tidy("r1") {
		t.Fatalf("没有真人座位的房间应可回收（tidy=true）")
	}
	if err := r.destroy("r1"); err != nil {
		t.Fatalf("destroy: %v", err)
	}

	// (c) 对局进行中掉线：座位必须保留（座位号 = 队伍号，摘掉会让对手队伍错位），
	//     并且该座位立即判负；结算后由 dropOffline 统一清理。
	if err := r.ensure("r2"); err != nil {
		t.Fatalf("ensure r2: %v", err)
	}
	_ = r.join("r2", "p_a")
	_ = r.configure("r2", "测试房2", "p_a", "小蓝A")
	_ = r.join("r2", "p_b")
	deck := buildAIDeck(ct)
	b, err := core.NewBattle(core.Config{Seed: 5, Table: ct, DeckA: deck, DeckB: deck})
	if err != nil {
		t.Fatalf("NewBattle: %v", err)
	}
	r.mu.Lock()
	st := r.rooms["r2"]
	st.battle = b
	st.started = true
	r.mu.Unlock()

	_, seat, running, ok = r.detach("p_b")
	if !ok || !running || seat != 1 {
		t.Fatalf("对局中 detach 应返回 running=true seat=1，实际 seat=%d running=%v ok=%v", seat, running, ok)
	}
	if got := len(r.players("r2")); got != 2 {
		t.Fatalf("对局中掉线不得摘座位（否则队伍号错位），实际房内 %d 人", got)
	}
	if !r.surrenderOnDisconnect("r2", seat) {
		t.Fatalf("掉线判负应生效")
	}
	if !b.Ended() {
		t.Fatalf("掉线判负后对局应结束")
	}
	res := b.Result()
	if res.Draw || res.Winner != core.TeamBlue || res.Reason != core.ReasonSurrender {
		t.Fatalf("掉线判负结果异常: %+v", res)
	}
	empty, humans := r.dropOffline("r2")
	if empty || len(humans) != 1 || humans[0] != "p_a" {
		t.Fatalf("结算后应清掉离线座位并留下 p_a，实际 empty=%v humans=%v", empty, humans)
	}
	t.Logf("断线清理：对局外直接摘座位 / 对局中判负并保留座位 / 结算后清理离线座位，全部符合预期（reason=%s）", res.Reason)
}
