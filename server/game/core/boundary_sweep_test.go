package core

import (
	"errors"
	"testing"
)

// 本文件钉住出牌与快照的边界行为（离线秒级，`go test ./game/core/ -count=1`）：
//   - 部署区四边：x = 0 / 17999 / 18000，y 取双方半场内的 8000 / 24000 与场外 32000；
//   - 圣水刚好等于费用；
//   - 同一 tick 双人出牌；
//   - 空场快照；
//   - 同 tick 双单位死亡 / 双塔被毁。
//
// 场地口径（core/units.go / arena.go）：18x32 格 = x∈[0,18000)、y∈[0,32000)、
// 河 y∈[15000,17000)，双方可部署区 = 各自半场（BLUE y<15000 / RED y>=17000）。

// TestPlayCardDeployZoneBoundary 出牌落点的四边与双方半场边界。
func TestPlayCardDeployZoneBoundary(t *testing.T) {
	cases := []struct {
		name    string
		team    Team
		x, y    int32
		wantErr error
	}{
		{"BLUE 原点 (0,0)", TeamBlue, 0, 0, nil},
		{"BLUE x 右边界前一点 17999", TeamBlue, ArenaWMilli - 1, 0, nil},
		{"BLUE x=18000 场外", TeamBlue, ArenaWMilli, 0, ErrOutsideDeployZone},
		{"BLUE x=-1 场外", TeamBlue, -1, 0, ErrOutsideDeployZone},
		{"BLUE 己方半场 y=8000", TeamBlue, 9000, 8000, nil},
		{"BLUE 敌方半场 y=24000", TeamBlue, 9000, 24000, ErrOutsideDeployZone},
		{"BLUE y=32000 场外", TeamBlue, 9000, ArenaHMilli, ErrOutsideDeployZone},
		{"RED 己方半场 y=24000", TeamRed, 9000, 24000, nil},
		{"RED 敌方半场 y=8000", TeamRed, 9000, 8000, ErrOutsideDeployZone},
		{"RED y=17000 近岸", TeamRed, 9000, RiverBottomMilli, nil},
		{"RED y=16999 河面", TeamRed, 9000, RiverBottomMilli - 1, ErrOutsideDeployZone},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			b := mustBattle(t, troopDeck, troopDeck, 101)
			cardID := b.Hand(c.team)[0]
			card, ok := b.cfg.Table.Card(cardID)
			if !ok || card.Kind != CardTypeTroop {
				t.Fatalf("fixture 错误：手牌 %d 不是部队卡", cardID)
			}
			setElixir(b, c.team, MaxElixirMilli)

			err := b.PlayCard(c.team, cardID, c.x, c.y)
			if c.wantErr == nil {
				if err != nil {
					t.Fatalf("落点 (%d,%d) 应合法，实际被拒: %v", c.x, c.y, err)
				}
				if len(b.units) != 1 {
					t.Fatalf("合法出牌后场上单位 = %d，应为 1", len(b.units))
				}
				if u := b.units[0]; u.xMilli != c.x || u.yMilli != c.y {
					t.Fatalf("单位落点 (%d,%d)，应等于请求点 (%d,%d)", u.xMilli, u.yMilli, c.x, c.y)
				}
				return
			}
			if !errors.Is(err, c.wantErr) {
				t.Fatalf("落点 (%d,%d) 应返回 %v，实际 %v", c.x, c.y, c.wantErr, err)
			}
			if len(b.units) != 0 {
				t.Fatalf("被拒的出牌仍生成了 %d 个单位", len(b.units))
			}
			if got := b.Elixir(c.team); got != MaxElixirMilli {
				t.Fatalf("被拒的出牌扣了圣水：%d", got)
			}
		})
	}
}

// TestTwoTeamsPlayOnSameTick 双人出牌落在同一 tick（两次 PlayCard 之间不 Step）：
// 两侧各自扣费、各自入手牌、各自生成单位，事件都记在这一帧。
func TestTwoTeamsPlayOnSameTick(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 202)
	blueCard := b.Hand(TeamBlue)[0]
	redCard := b.Hand(TeamRed)[0]
	blueDef, _ := b.cfg.Table.Card(blueCard)
	redDef, _ := b.cfg.Table.Card(redCard)
	setElixir(b, TeamBlue, MaxElixirMilli)
	setElixir(b, TeamRed, MaxElixirMilli)
	b.DrainEvents()

	if err := b.PlayCard(TeamBlue, blueCard, 9000, 8000); err != nil {
		t.Fatalf("蓝方出牌: %v", err)
	}
	if err := b.PlayCard(TeamRed, redCard, 9000, 24000); err != nil {
		t.Fatalf("红方同帧出牌: %v", err)
	}
	if len(b.units) != 2 {
		t.Fatalf("同帧双方出牌后场上单位 = %d，应为 2", len(b.units))
	}
	if got, want := b.Elixir(TeamBlue), MaxElixirMilli-blueDef.Elixir*MilliTilePerTile; got != want {
		t.Fatalf("蓝方圣水 = %d，应为 %d", got, want)
	}
	if got, want := b.Elixir(TeamRed), MaxElixirMilli-redDef.Elixir*MilliTilePerTile; got != want {
		t.Fatalf("红方圣水 = %d，应为 %d", got, want)
	}

	evs := b.DrainEvents()
	play, spawn := 0, 0
	teams := map[int32]bool{}
	for _, e := range evs {
		switch e.Kind {
		case EvPlayCard:
			play++
			teams[e.Team] = true
		case EvSpawn:
			spawn++
		}
	}
	if play != 2 || spawn != 2 {
		t.Fatalf("同帧出牌事件 EvPlayCard=%d EvSpawn=%d，应各 2 条", play, spawn)
	}
	if !teams[int32(TeamBlue)] || !teams[int32(TeamRed)] {
		t.Fatalf("EvPlayCard 缺少一方：team 集合 %v", teams)
	}

	// 下一帧两侧的落位计时同时走一格。
	b.Step()
	for _, u := range b.units {
		if u.deployMsLeft != 1000-MSecPerTick {
			t.Fatalf("单位 %d 落位剩余 %d，应为 %d", u.ID, u.deployMsLeft, 1000-MSecPerTick)
		}
	}
}

// TestSnapshotEmptyBoard 开局空场快照：实体为空、双方各 3 座塔、各 4 张手牌，
// 且取快照不推进时钟（seq 不变）。
func TestSnapshotEmptyBoard(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 303)
	s := b.Snapshot()
	if len(s.Entities) != 0 {
		t.Fatalf("开局实体 = %d，应为 0", len(s.Entities))
	}
	if len(s.TowersA) != 3 || len(s.TowersB) != 3 {
		t.Fatalf("塔数 A/B = %d/%d，应为 3/3", len(s.TowersA), len(s.TowersB))
	}
	if len(s.HandA) != 4 || len(s.HandB) != 4 {
		t.Fatalf("手牌数 A/B = %d/%d，应为 4/4", len(s.HandA), len(s.HandB))
	}
	if s.ElixirA != StartingElixirMilli || s.ElixirB != StartingElixirMilli {
		t.Fatalf("开局圣水 A/B = %d/%d，应为 %d", s.ElixirA, s.ElixirB, StartingElixirMilli)
	}
	if s.Phase != PhaseNormal || s.ServerMs != 0 {
		t.Fatalf("开局 phase=%d server_ms=%d，应为 0/0", s.Phase, s.ServerMs)
	}
	if again := b.Snapshot(); again.Seq != s.Seq {
		t.Fatalf("连续取快照改变了 seq：%d -> %d", s.Seq, again.Seq)
	}
	for _, tw := range append(append([]TowerSnap{}, s.TowersA...), s.TowersB...) {
		if !tw.Alive || tw.HP != tw.MaxHP {
			t.Fatalf("开局塔 %d 应为满血存活：%+v", tw.ID, tw)
		}
	}
}

// TestUnitsDieOnSameTick 同一 tick 内两只单位死亡：死亡事件各一条，
// 同一帧快照里都带 anim=die + HP 0，且上报后的下一 tick 才被回收。
func TestUnitsDieOnSameTick(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 404)
	def, ok := b.cfg.Table.Unit("Skeleton")
	if !ok || def == nil {
		t.Fatal("fixture 缺少 Skeleton 行")
	}
	blue := b.spawnUnit(TeamBlue, def, cardSkeleton, 9000, 14000, 0)
	red := b.spawnUnit(TeamRed, def, cardSkeleton, 9000, 18000, 0)
	b.DrainEvents()

	b.damageEntity(blue, 99999, nil)
	b.damageEntity(red, 99999, nil)
	b.Step()

	deaths := 0
	ids := map[int32]bool{}
	for _, e := range b.DrainEvents() {
		if e.Kind == EvDeath {
			deaths++
			ids[e.EntityID] = true
		}
	}
	if deaths != 2 {
		t.Fatalf("同 tick 死亡事件 = %d，应为 2", deaths)
	}
	if !ids[blue.ID] || !ids[red.ID] {
		t.Fatalf("死亡事件实体 id 不齐：%v（应为 %d / %d）", ids, blue.ID, red.ID)
	}

	s := b.Snapshot()
	found := map[int32]bool{}
	for _, e := range s.Entities {
		if e.ID != blue.ID && e.ID != red.ID {
			continue
		}
		found[e.ID] = true
		if e.HP != 0 || e.Anim != AnimDie {
			t.Fatalf("死亡帧快照 id=%d HP=%d anim=%d，应为 0/%d", e.ID, e.HP, e.Anim, AnimDie)
		}
	}
	if !found[blue.ID] || !found[red.ID] {
		t.Fatalf("快照没有同时带上两只尸体：%v", found)
	}

	// 快照上报过之后才回收（否则客户端永远看不到死亡帧）。
	b.Step()
	for _, u := range b.units {
		if u.ID == blue.ID || u.ID == red.ID {
			t.Fatalf("已上报死亡帧的单位 %d 仍留在场上", u.ID)
		}
	}
}

// TestTowersDestroyedOnSameTick 同一 tick 内两座公主塔被毁：
// 两条塔毁事件、进攻方 +2 冠、快照里两塔都 Alive=false 且仍在塔列表里。
func TestTowersDestroyedOnSameTick(t *testing.T) {
	b := mustBattle(t, troopDeck, troopDeck, 505)
	left := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessLeftPos[0])
	right := towerOf(t, b, TeamBlue, TowerKindPrincess, BluePrincessRightPos[0])
	b.DrainEvents()

	b.damageEntity(left, 99999, nil)
	b.damageEntity(right, 99999, nil)
	b.Step()

	destroyed := map[int32]bool{}
	for _, e := range b.DrainEvents() {
		if e.Kind == EvTowerDestroyed {
			destroyed[e.EntityID] = true
		}
	}
	if len(destroyed) != 2 || !destroyed[left.ID] || !destroyed[right.ID] {
		t.Fatalf("同 tick 塔毁事件集合 %v，应为 {%d,%d}", destroyed, left.ID, right.ID)
	}
	if got := b.Crowns(TeamRed); got != 2 {
		t.Fatalf("红方王冠 = %d，应连得 2 冠", got)
	}

	s := b.Snapshot()
	if len(s.TowersA) != 3 {
		t.Fatalf("塔毁后 A 侧塔列表 = %d 条，应仍为 3 条（含已毁）", len(s.TowersA))
	}
	down := 0
	for _, tw := range s.TowersA {
		if !tw.Alive {
			down++
			if tw.HP != 0 {
				t.Fatalf("已毁塔 %d 的 hp = %d，应为 0", tw.ID, tw.HP)
			}
		}
	}
	if down != 2 {
		t.Fatalf("快照里已毁塔 = %d，应为 2", down)
	}
	if b.Phase() != PhaseNormal {
		// 只毁两座公主塔不结束对局（国王塔还活着）。
		t.Fatalf("只毁公主塔不应结束对局，phase=%d", b.Phase())
	}
}
