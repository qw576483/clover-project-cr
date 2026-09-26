package logic

import (
	"testing"

	"clover-cr/game/core"
	"clover-cr/game/datadef"
)

// 本文件是「主菜单资料页 8 项统计」的离线断言（不依赖起服 / redis / mysql）：
// 判的是"数值到底从哪来"——结算排队 → 补记累加 → 组装回包这三段各自的输入输出。
// 端到端（真的打一局、画面上的数字 +1）由 .ai-tmp 的驱动负责，这里只钉数据口径。

// TestApplyMatchResultAccumulates 补记口径：每局参赛场次 +1；胜/负各自 +1；
// 只有「胜且本方王冠 == 3」才算三冠；本局出牌逐张累进；平局只算场次。
func TestApplyMatchResultAccumulates(t *testing.T) {
	var p datadef.PlayerData

	// 第一局：三冠胜，本局出了 knight×3 + archers×1。
	applyMatchResult(&p, matchResult{Win: true, Crowns: 3, Plays: map[int32]int32{101: 3, 102: 1}})
	// 第二局：两冠胜（不是三冠）。
	applyMatchResult(&p, matchResult{Win: true, Crowns: 2, Plays: map[int32]int32{101: 2}})
	// 第三局：负。
	applyMatchResult(&p, matchResult{Win: false, Crowns: 1})
	// 第四局：平局（不改胜负，但算打过一局）。
	applyMatchResult(&p, matchResult{Draw: true})

	if p.Matches != 4 {
		t.Fatalf("参赛场次应为 4（含平局），实际 %d", p.Matches)
	}
	if p.Wins != 2 || p.Losses != 1 {
		t.Fatalf("胜负应为 2 胜 1 负，实际 %d 胜 %d 负", p.Wins, p.Losses)
	}
	if p.ThreeCrownWins != 1 {
		t.Fatalf("三冠胜场应为 1（只有 Crowns==3 的那局算），实际 %d", p.ThreeCrownWins)
	}
	if got := p.CardPlays[101]; got != 5 {
		t.Fatalf("卡 101 的累计出牌次数应为 3+2=5，实际 %d", got)
	}
	if got := p.CardPlays[102]; got != 1 {
		t.Fatalf("卡 102 的累计出牌次数应为 1，实际 %d", got)
	}
	if len(p.CardPlays) != 2 {
		t.Fatalf("只该有两张卡有计数，实际 %d 张", len(p.CardPlays))
	}
	t.Logf("补记后 matches=%d wins=%d losses=%d threeCrown=%d cardPlays=%v",
		p.Matches, p.Wins, p.Losses, p.ThreeCrownWins, p.CardPlays)
}

// TestFavouriteCardFromRealTable 常用卡牌 = 出牌次数最多的那张卡的真实卡名；
// 同次数时取卡 id 最小者（map 遍历顺序随机 ⇒ 必须有确定的打破平局规则）。
func TestFavouriteCardFromRealTable(t *testing.T) {
	ct := testTable(t)

	// 真表里取两张不同的卡（出处 = server/game/table/tsv/card.tsv）。
	ids := make([]int32, 0, 2)
	for _, row := range ct.cardRows {
		ids = append(ids, int32(row.Id))
		if len(ids) == 2 {
			break
		}
	}
	if len(ids) != 2 {
		t.Fatalf("卡表里取不到两张卡")
	}
	low, high := ids[0], ids[1]
	if low > high {
		low, high = high, low
	}

	// 没有出过牌 ⇒ (0, "")（界面据此显示"真的没有"的占位符）。
	if id, name := favouriteCardOf(&datadef.PlayerData{}, ct); id != 0 || name != "" {
		t.Fatalf("一张牌都没出过时应为 (0,\"\")，实际 (%d,%q)", id, name)
	}

	// 次数持平 ⇒ 取 id 最小者。
	p := &datadef.PlayerData{CardPlays: map[int32]int32{low: 4, high: 4}}
	id, name := favouriteCardOf(p, ct)
	if id != low {
		t.Fatalf("次数持平时应取 id 最小的卡 %d，实际 %d", low, id)
	}
	if name == "" {
		t.Fatalf("卡 %d 应有中文名（来自卡牌表）", low)
	}
	wantName := ""
	if c, ok := ct.Card(low); ok {
		wantName = c.NameCN
	}
	if name != wantName {
		t.Fatalf("卡名应与卡牌表一致：期望 %q，实际 %q", wantName, name)
	}

	// 次数多者胜出。
	p.CardPlays[high] = 5
	if id2, _ := favouriteCardOf(p, ct); id2 != high {
		t.Fatalf("出牌次数多者应胜出：期望 %d，实际 %d", high, id2)
	}
	t.Logf("常用卡牌：持平取 id=%d(%q)，次数多者 id=%d", low, name, high)
}

// TestBuildProfileReplyFillsAllEightStats 组装回包：8 项一个不缺，且三项"本工程没有该系统"
// 的字段确实是 0（不是"忘了填"，是刻意的空值语义）；已收集卡牌 = 卡牌表可用卡数。
func TestBuildProfileReplyFillsAllEightStats(t *testing.T) {
	ct := testTable(t)
	p := &datadef.PlayerData{
		Nickname:       "测试员",
		Deck:           []int32{1, 2, 3},
		Wins:           7,
		Losses:         5,
		Matches:        13,
		ThreeCrownWins: 2,
		CardPlays:      map[int32]int32{int32(ct.cardRows[0].Id): 9},
	}
	r := buildProfileReply(p, ct)

	if r.CardsFound != int32(ct.CardCount()) {
		t.Fatalf("已收集卡牌应等于卡表可用卡数 %d，实际 %d", ct.CardCount(), r.CardsFound)
	}
	if r.CardsFound != 60 {
		t.Fatalf("本工程卡池应为 60 张（card.tsv），实际 %d", r.CardsFound)
	}
	if r.Matches != 13 || r.Wins != 7 || r.Losses != 5 || r.ThreeCrownWins != 2 {
		t.Fatalf("计数搬运有误：matches=%d wins=%d losses=%d threeCrown=%d",
			r.Matches, r.Wins, r.Losses, r.ThreeCrownWins)
	}
	if r.FavouriteCard != int32(ct.cardRows[0].Id) || r.FavouriteCardName == "" {
		t.Fatalf("常用卡牌应给出 id=%d 及其卡名，实际 id=%d name=%q",
			ct.cardRows[0].Id, r.FavouriteCard, r.FavouriteCardName)
	}
	// 本工程没有奖杯/段位、部落捐赠、锦标赛卡牌奖励这三套系统 ⇒ 三项恒 0。
	if r.HighestTrophies != 0 || r.CardsDonated != 0 || r.CardsWon != 0 {
		t.Fatalf("无对应机制的三项应恒 0，实际 最高奖杯=%d 累计捐赠=%d 赢得卡牌=%d",
			r.HighestTrophies, r.CardsDonated, r.CardsWon)
	}
	if r.Nickname != "测试员" || len(r.Deck) != 3 {
		t.Fatalf("昵称 / 卡组搬运有误：nickname=%q deck=%v", r.Nickname, r.Deck)
	}
	t.Logf("回包 8 项：胜场=%d 常用卡牌=%d(%q) 三冠=%d 已收集=%d 最高奖杯=%d 捐赠=%d 场次=%d 赢得卡牌=%d",
		r.Wins, r.FavouriteCard, r.FavouriteCardName, r.ThreeCrownWins, r.CardsFound,
		r.HighestTrophies, r.CardsDonated, r.Matches, r.CardsWon)
}

// TestBuildProfileReplyMatchesFloorForLegacyData 老档案（早于局数统计，只有胜负场）的
// 参赛场次必须退到 **wins+losses** 这个下限，⛔ 不许显示成比胜负之和还小的数。
func TestBuildProfileReplyMatchesFloorForLegacyData(t *testing.T) {
	ct := testTable(t)

	// 迁移前的老档案：只记了 5 胜 7 负，局数 / 三冠 / 出牌表都还没有。
	legacy := &datadef.PlayerData{Nickname: "老号", Wins: 5, Losses: 7}
	r := buildProfileReply(legacy, ct)
	if r.Matches != 12 {
		t.Fatalf("老档案参赛场次应退到 wins+losses=12，实际 %d", r.Matches)
	}
	if r.ThreeCrownWins != 0 || r.FavouriteCard != 0 || r.FavouriteCardName != "" {
		t.Fatalf("老档案没有的项应如实为 0/空：三冠=%d 常用卡牌=%d(%q)",
			r.ThreeCrownWins, r.FavouriteCard, r.FavouriteCardName)
	}

	// 新档案：真实局数 >= 胜负之和时，下发真实局数（平局也算一局，故可以更大）。
	fresh := &datadef.PlayerData{Wins: 3, Losses: 2, Matches: 6}
	if r2 := buildProfileReply(fresh, ct); r2.Matches != 6 {
		t.Fatalf("真实局数 6 >= 胜负之和 5 时应下发 6，实际 %d", r2.Matches)
	}
	t.Logf("老档案下发 matches=%d（下限），新档案下发 matches=%d（真实值）",
		r.Matches, buildProfileReply(fresh, ct).Matches)
}

// TestPlayCardCountsOnlyAcceptedPlays 出牌计数只统计**真的出成功**的牌：
// 真人出牌走 roomRegistry.playCard（AI 不走这条路径）；被拒的牌不计入。
func TestPlayCardCountsOnlyAcceptedPlays(t *testing.T) {
	ct := testTable(t)
	r := newRoomRegistry()
	if err := r.ensure("r1"); err != nil {
		t.Fatalf("ensure: %v", err)
	}
	if err := r.join("r1", "p_a"); err != nil {
		t.Fatalf("join p_a: %v", err)
	}
	if err := r.join("r1", "p_b"); err != nil {
		t.Fatalf("join p_b: %v", err)
	}

	deck := buildAIDeck(ct)
	b, err := core.NewBattle(core.Config{Seed: 424242, Table: ct, DeckA: deck, DeckB: deck})
	if err != nil {
		t.Fatalf("NewBattle: %v", err)
	}
	r.rooms["r1"].battle = b

	// 出一张真的在手上、落点合法的牌（落点由 core.Decide 给出，与 AI 同一条校验路径）。
	// 开局（tick 0，圣水 6）不一定有可出且轮次到位的牌 ⇒ 先推进几帧再决策。
	var cardID, x, y int32
	var ok bool
	for tick := 0; tick < 60 && !ok; tick++ {
		cardID, x, y, ok = core.Decide(b, core.TeamBlue)
		if !ok {
			b.Step()
		}
	}
	if !ok {
		t.Fatalf("推进 60 tick 后 AI 决策仍给不出一张可出的牌")
	}
	if err := r.playCard("r1", "p_a", cardID, x, y); err != nil {
		t.Fatalf("合法出牌应成功: %v", err)
	}
	if got := r.rooms["r1"].plays["p_a"][cardID]; got != 1 {
		t.Fatalf("成功出牌后该卡计数应为 1，实际 %d", got)
	}

	// 出一张**不在手上**的牌 ⇒ 必被拒，且不得计入统计。
	notInHand := int32(0)
	for _, row := range ct.cardRows {
		held := false
		for _, h := range b.Hand(core.TeamBlue) {
			if h == int32(row.Id) {
				held = true
				break
			}
		}
		if !held {
			notInHand = int32(row.Id)
			break
		}
	}
	if notInHand == 0 {
		t.Fatalf("找不到一张不在手上的卡，断言无法成立")
	}
	if err := r.playCard("r1", "p_a", notInHand, x, y); err == nil {
		t.Fatalf("出不在手上的牌应被拒")
	}
	if _, counted := r.rooms["r1"].plays["p_a"][notInHand]; counted {
		t.Fatalf("被拒的牌不得计入出牌统计")
	}
	if got := r.rooms["r1"].plays["p_a"][cardID]; got != 1 {
		t.Fatalf("被拒一次后原有计数不该变，实际 %d", got)
	}
	// AI 不在这张表里（它走 core 的 PlayCard，不经 roomRegistry.playCard）。
	if _, ai := r.rooms["r1"].plays[aiPlayerPrefix+aiNickname]; ai {
		t.Fatalf("AI 不该出现在真人出牌计数里")
	}
	t.Logf("出牌统计：p_a 的 card=%d 计数=%d；被拒的 card=%d 未计入；表内玩家数=%d",
		cardID, r.rooms["r1"].plays["p_a"][cardID], notInHand, len(r.rooms["r1"].plays))
}
