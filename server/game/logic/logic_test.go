package logic

import (
	"encoding/json"
	"testing"

	"clover-cr/game/core"
	"clover-cr/game/table"
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
	// flying 列必须接进 core.UnitDef.Flying：
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

// TestAIDeckMatchesReferenceDefaultDeck AI 卡组必须**逐张等于**参考实现的 `DEFAULT_DECK`。
//
// ★ D148：这条断言取代了旧的 `TestAIDeckIsTroopDeck`（旧断言要求"8 张全是部队卡"）。
// 旧口径正是缺陷本身：它把法术卡整个排除在 AI 卡组外 ⇒ `core/ai.go::decideSpell` 永不触发
// ⇒ 玩家整局看不到对手放法术（用户第 9 条「卡的实现没看到法术」）。
//
// 出处 = `原版资源/cr-sim/cr_sim/train/run.py:55` 的 `DEFAULT_DECK`
// （也在 `cr_sim/play/server.py:46`）：Knight, Musketeer, Cannon, Skeletons,
// IceSpirits, Log, Fireball, Goblins —— 6 部队/建筑 + **2 法术**。
//
// 为什么"含法术"这条断言自己也能失败（防"恒绿"）：它逐个核 `core.CardDef.Kind`，
// 数出法术张数必须 == 2；若谁把 `aiDeckRefKeys` 改回全部队卡，这条立刻红。
func TestAIDeckMatchesReferenceDefaultDeck(t *testing.T) {
	const wantSpells = 2 // cr-sim train/run.py:55 DEFAULT_DECK 里的法术张数（Log + Fireball）

	ct := testTable(t)
	deck := buildAIDeck(ct)
	if len(deck) != deckSize {
		t.Fatalf("AI 卡组应为 %d 张，实际 %d 张", deckSize, len(deck))
	}

	// ① 逐张对上参考卡组的 key（顺序也要对 —— `DEFAULT_DECK` 是有序元组）。
	wantKeys := []string{"knight", "musketeer", "cannon", "skeletons", "ice-spirit", "the-log", "fireball", "goblins"}
	gotKeys := make([]string, 0, len(deck))
	for _, id := range deck {
		cd, ok := ct.Card(id)
		if !ok || cd == nil {
			t.Fatalf("AI 卡组里的卡 %d 解析不到", id)
		}
		gotKeys = append(gotKeys, cd.Key)
	}
	for i := range wantKeys {
		if gotKeys[i] != wantKeys[i] {
			t.Fatalf("AI 卡组第 %d 张应为 %s（参考 DEFAULT_DECK），实际 %s（全组 %v）",
				i, wantKeys[i], gotKeys[i], gotKeys)
		}
	}

	// ② 法术张数 == 2，且每张法术都真的带上了 SpellDef（否则 decideSpell 仍然打不出去）。
	spells := 0
	for _, id := range deck {
		cd, _ := ct.Card(id)
		if cd.Kind != core.CardTypeSpell {
			continue
		}
		spells++
		if cd.Spell == nil {
			t.Fatalf("AI 卡组的法术卡 %d(%s) 没有 SpellDef ⇒ decideSpell 照样打不出去", id, cd.Key)
		}
		if cd.Spell.RadiusMilli <= 0 {
			t.Fatalf("AI 卡组的法术卡 %d(%s) 半径 <= 0 ⇒ decideSpell 会 `continue` 跳过它", id, cd.Key)
		}
	}
	if spells != wantSpells {
		t.Fatalf("AI 卡组应含 %d 张法术（参考 DEFAULT_DECK），实际 %d 张（全组 %v）", wantSpells, spells, gotKeys)
	}

	// ③ 6 张非法术（部队/建筑）——与参考卡组同构。
	if troops := len(deck) - spells; troops != deckSize-wantSpells {
		t.Fatalf("AI 卡组应含 %d 张部队/建筑，实际 %d 张", deckSize-wantSpells, troops)
	}

	t.Logf("AI 卡组 = %v（法术 %d 张）", gotKeys, spells)
}

// TestBuildAIDeckDegradesLoudlyWhenRefCardMissing 负控：参考卡组点名的卡在本表里缺失时，
// `buildAIDeck` 必须**仍然凑满 8 张**（否则 `room.go` 的"座位没有 8 张卡组"会让 AI 房开不了局），
// 而且要打到 Warn（"配表与参考实现脱节"不许静默）。
//
// 做法：拿真表**复制一份**、从 `cardRows` 里摘掉 `fireball` 那一行 ⇒ 只影响这一条断言，
// ⛔ 不动盘上的 tsv。
func TestBuildAIDeckDegradesLoudlyWhenRefCardMissing(t *testing.T) {
	ct := testTable(t)

	trimmed := &cardTable{
		cards:    ct.cards,
		units:    ct.units,
		cardRows: make([]*table.CardRow, 0, len(ct.cardRows)),
	}
	removed := false
	for _, row := range ct.cardRows {
		if row.Key == "fireball" {
			removed = true
			continue
		}
		trimmed.cardRows = append(trimmed.cardRows, row)
	}
	if !removed {
		t.Fatalf("负控自身失效：card 表里找不到 key=fireball，无法构造缺卡场景")
	}

	deck := buildAIDeck(trimmed)
	if len(deck) != deckSize {
		t.Fatalf("缺参考卡时应降级补齐到 %d 张，实际 %d 张", deckSize, len(deck))
	}
	seen := make(map[int32]bool, len(deck))
	for _, id := range deck {
		if seen[id] {
			t.Fatalf("降级补齐后出现重复 id=%d（全组 %v）", id, deck)
		}
		seen[id] = true
		if _, ok := ct.Card(id); !ok {
			t.Fatalf("降级补齐塞进了卡池外的 id=%d", id)
		}
	}
	// 缺的是 fireball ⇒ 降级结果里必然没有它（补进来的是配表顺序里的下一张）。
	if seen[fireballID(t, ct)] {
		t.Fatalf("fireball 已被摘掉，降级结果里不该出现它（全组 %v）", deck)
	}
	t.Logf("缺 fireball 时的降级卡组 = %v（%d 张，无重复）", deck, len(deck))
}

// fireballID 取 `fireball` 的卡 id（负控断言要用；找不到就 Fatal）。
func fireballID(t *testing.T, ct *cardTable) int32 {
	t.Helper()
	for _, row := range ct.cardRows {
		if row.Key == "fireball" {
			return int32(row.Id)
		}
	}
	t.Fatalf("card 表里找不到 key=fireball")
	return 0
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
// ⚠️ **旧结论已失效（2026-09-24 复核）**：本注释原先逐字写着「本引擎在**连接关闭**时
// gwcore.cleanup 会 panic（`index out of range [-1]`，session.go 的 "清尾" 写在 append
// 截断之后），而派发断线事件的代码在它之后 ⇒ `OnDisconnect` 永远不会触发」。**引擎已修**：
// `clover-server-engine/internal/transport/gateway/gwcore/session.go` 的会话摘除已改成
// 「先在**原长度**上清尾（`sessions[n-1] = nil`）再 `sessions = sessions[:n-1]` 截断」，
// 同处注释逐字记着原缺陷的因果（append 截断之后 len 已 -1 ⇒ 清的是最后一条**存活**会话，
// 且本会话是唯一一条时对 `sessions[-1]` 赋值越界 panic ⇒ 尾部
// fireHardDisconnect → OnDisconnect 永不执行）⇒ **断线事件派发现在可达**。
//
// 本测试**仍然离线**（不改回 end-to-end）是有意的：它判的是「断线后**注册表**该发生什么」——
// 那是业务侧 `roomRegistry.detach` 的簿记语义，与服务端连接层是否 panic 无关；
// 钉在注册表层比端到端更稳（端到端还要起 gateway + 真连接，判据会变脆）。
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

// TestAISpellCastFromReferenceDeck 端到端留痕判据：用**参考实现的 AI 卡组**
// 跑一整局纯 AI 对局，AI（红方）必须**真的放出过法术**。
//
// ★ 为什么这条是 D148 用户第 9 条「卡的实现没看到法术」的直接判据：
// 上一轮客户端已经补好了法术特效链路（`SpellFxTable` / 落点环 / 施法帧），
// 但服务端 `buildAIDeck` 只取部队卡 ⇒ AI 卡组 0 张法术 ⇒
// `core/ai.go::decideSpell` **永远走不到** ⇒ 整局看不到对手放法术。
// 只测「卡组里有法术」还不够 —— 那只证明数据对；这条测的是
// **决策真的选中了法术并把 EvPlayCard 发了出来**。
//
// 白盒口径：直接订阅 `core.Event`（`EvPlayCard` 带 `CardID`），
// 用 `cardTable.Card(id).Kind == core.CardTypeSpell` 判是不是法术。
// 两个 AI 用的是同一副 `buildAIDeck`，所以只数**红方**（对手位）的出牌。
func TestAISpellCastFromReferenceDeck(t *testing.T) {
	b := newTestBattle(t, 20240924)
	ct := testTable(t)

	const totalTicks = 6000 // 300 s：足够走完"开局铺场 → 中路会战 → 法术清场"
	spellPlays := 0
	perKey := map[string]int{}
	playedTotal := 0

	for tick := 0; tick < totalTicks; tick++ {
		if tick%aiDecisionTicks == 0 {
			for _, team := range []core.Team{core.TeamBlue, core.TeamRed} {
				if cardID, x, y, ok := core.Decide(b, team); ok {
					if err := b.PlayCard(team, cardID, x, y); err != nil {
						t.Fatalf("AI 出牌被拒 team=%d card=%d (%d,%d): %v", team, cardID, x, y, err)
					}
				}
			}
		}
		b.Step()

		for _, ev := range b.DrainEvents() {
			if ev.Kind != core.EvPlayCard {
				continue
			}
			if core.Team(ev.Team) != core.TeamRed {
				continue // 只数对手位（AI）
			}
			cd, ok := ct.Card(ev.CardID)
			if !ok || cd == nil {
				t.Fatalf("AI 打出的卡 %d 在配表里解析不到", ev.CardID)
			}
			playedTotal++
			if cd.Kind == core.CardTypeSpell {
				spellPlays++
				perKey[cd.Key]++
			}
		}
	}

	if spellPlays == 0 {
		t.Fatalf("AI 在 %d tick（%d s）里一张法术都没放（共出牌 %d 张）—— decideSpell 仍是死代码",
			totalTicks, totalTicks/20, playedTotal)
	}
	t.Logf("AI（红方）出牌 %d 张，其中法术 %d 张 %v —— decideSpell 已被走到并成功下发 EvPlayCard",
		playedTotal, spellPlays, perKey)
}
