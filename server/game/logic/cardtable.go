package logic

import (
	"fmt"

	"clover-cr/game/core"
	"clover-cr/game/def"
	"clover-cr/game/table"
	"github.com/qw576483/clover-server-engine/pkg/foundation/logger"
)

// cardTable 把打表产物适配成 core.CardTable。
//
// ★ 数据源：生成物 `table.Default`（`table.NewTables()` + `LoadAll(dir)`，表文件 =
// `server/game/table/tsv/{card,unit,spell}.tsv`，访问器 = `table.Default.Card/Unit/Spell`）。
// ⛔ 本文件**不自带任何 tsv 解析**。2026-09-20 之前的版本为了绕开两条生成物缺陷
// （① `table.Tables` 的字段名是中文 ⇒ 包外写不出 `table.Default.<中文表名>`；② 生成器 base 层用
// `encoding/csv` + `TrimLeadingSpace` ⇒ 空单元格被吃掉、后续列集体左移）曾自带一个按列名解析器；
// 那两条都已在生成物那一层修掉（表名英文化 `card_cs`/`unit_cs`/`spell_cs` + 上层表手写
// `strings.Split` 解析，见 `server/game/table/tsv.go` 顶部），绕行随之消除。
//
// 这一层薄适配仍然保留，只因为它要干三件生成物不该管的事：
//  1. 行 → core 定义：core 自己定义 CardDef/UnitDef/SpellDef 与 CardTable 接口
//     （core 零引擎依赖、也不 import 生成物），字段搬运必然发生在 core 之外；
//  2. 三处配表语义合并：法术卡的 `Spell` 取同 key 的法术行；群体卡（弓箭手 / 亡灵 / 野蛮人…）
//     在战斗单位表里是一行「落点召唤」，要展开成 `summon_key` + `summon_n` + `summon_radius_mt`；
//  3. 配表顺序：卡池下发（CardInfo）与 AI 卡组都要求顺序确定，而 core.CardDef 里没有
//     name_en / arena / icon。
type cardTable struct {
	cards map[int32]*core.CardDef
	units map[string]*core.UnitDef
	// cardRows 按配表顺序保留原始行（cardPool 与 AI 卡组要用 name_en / arena / icon / type）。
	cardRows []*table.CardRow
}

// loadCardTable 走生成物装配入口（NewTables + LoadAll）并把生成物适配成 core.CardTable，
// 返回逐表行数（形如 "card=60"），供调用方向引擎广播 table.Loaded。
//
// 任一张卡适配不出来 ⇒ 直接返回错误：缺一张卡会让含它的卡组整局开不了，
// 这种错误必须 fail-fast，不能"跳过后静默少一张"。
func loadCardTable(dir string) (*cardTable, []string, error) {
	tbl := table.NewTables() // 顺带把全局单例 table.Default 指向本实例
	names := make([]string, 0, 3)
	table.OnLoadedOne = func(name string, count int) {
		names = append(names, fmt.Sprintf("%s=%d", name, count))
	}
	if err := tbl.LoadAll(dir); err != nil {
		return nil, names, fmt.Errorf("生成物配表加载失败（目录 %s）: %w", dir, err)
	}
	if tbl.Card.Len() == 0 || tbl.Unit.Len() == 0 {
		return nil, names, fmt.Errorf("配表为空（card=%d unit=%d spell=%d，目录 %s）",
			tbl.Card.Len(), tbl.Unit.Len(), tbl.Spell.Len(), dir)
	}

	ct := &cardTable{
		cards:    make(map[int32]*core.CardDef, tbl.Card.Len()),
		units:    make(map[string]*core.UnitDef, tbl.Unit.Len()),
		cardRows: tbl.Card.Rows(),
	}

	// 战斗单位表：按实体 key 建 core.UnitDef 索引（core.CardTable.Unit 是按 key 取的）。
	seenUnit := make(map[string]bool, tbl.Unit.Len())
	for _, row := range tbl.Unit.Rows() {
		if row.Key == "" {
			logger.Warnf("logic: unit 表有一行没有 key（id=%d，已跳过：core 只能按 key 取实体）", row.Id)
			continue
		}
		if seenUnit[row.Key] {
			logger.Warnf("logic: unit 表重复 key=%s（取首个出现的行）", row.Key)
			continue
		}
		seenUnit[row.Key] = true
		ct.units[row.Key] = unitDefOf(row)
	}

	// 法术表：key = 卡牌 key。
	spellDefs := make(map[string]*core.SpellDef, tbl.Spell.Len())
	for _, row := range tbl.Spell.Rows() {
		if row.Key == "" {
			logger.Warnf("logic: spell 表有一行没有 key（id=%d，已跳过：法术卡要按 key 找它）", row.Id)
			continue
		}
		if _, dup := spellDefs[row.Key]; dup {
			logger.Warnf("logic: spell 表重复 key=%s（取首个出现的行）", row.Key)
			continue
		}
		spellDefs[row.Key] = spellDefOf(row)
	}

	// 卡牌表：行 → core.CardDef（法术卡顺带挂上 SpellDef）。
	seenCard := make(map[int32]bool, len(ct.cardRows))
	for _, row := range ct.cardRows {
		if row.Key == "" {
			logger.Warnf("logic: card 表有一行没有 key（id=%d，已跳过）", row.Id)
			continue
		}
		if seenCard[int32(row.Id)] {
			logger.Warnf("logic: card 表重复 id=%d（取首个出现的行）", row.Id)
			continue
		}
		cd, cerr := cardDefOf(row, tbl.Unit, spellDefs)
		if cerr != nil {
			return nil, names, cerr
		}
		seenCard[cd.ID] = true
		ct.cards[cd.ID] = cd
	}
	return ct, names, nil
}

// cardDefOf 把一行卡牌（生成物 card 表）配成 core.CardDef。
//
// 群体卡（弓箭手 / 亡灵 / 野蛮人 …）在战斗单位表里是一行「落点召唤」行：
// summon_key 指向真正出兵的本体行（Archer / Minion / Barbarian），summon_n 是数量，
// summon_radius_mt 是环形铺开半径。单卡（骑士 / 巨人 …）则直接用自己的 key。
func cardDefOf(row *table.CardRow, unitRows *table.UnitTable, spells map[string]*core.SpellDef) (*core.CardDef, error) {
	id := int32(row.Id)
	cd := &core.CardDef{
		ID:     id,
		Key:    row.Key,
		NameCN: row.NameCn,
		Kind:   int32(row.Type), // 0=部队 1=法术 2=建筑（core.CardDef.Kind 同口径）
		Rarity: int32(row.Rarity),
		Elixir: int32(row.Elixir),
	}
	if cd.Kind == core.CardTypeSpell {
		sp, ok := spells[row.Key]
		if !ok {
			return nil, fmt.Errorf("卡 %d(%s) 是法术，但法术表里没有同 key 的行", id, row.Key)
		}
		cd.Spell = sp
		return cd, nil
	}

	u := unitRows.GetByKey(row.Key)
	if u == nil {
		return nil, fmt.Errorf("卡 %d(%s) 在战斗单位表里没有同 key 的行", id, row.Key)
	}
	cd.DeployDelayMs = int32(u.DeployMs)
	cd.UnitN = 1
	if u.SummonKey != "" && u.SummonN > 0 {
		if unitRows.GetByKey(u.SummonKey) == nil {
			return nil, fmt.Errorf("卡 %d(%s) 的 summon_key=%s 在战斗单位表里找不到", id, row.Key, u.SummonKey)
		}
		cd.UnitKey = u.SummonKey
		cd.UnitN = int32(u.SummonN)
		cd.UnitRadiusMilli = int32(u.SummonRadiusMt)
		// ★ 2026-09-23 修（差异登记 D142/D147）：`summon_deploy_delay` 的语义是
		// **逐个错开的间隔**，⛔ 不是"这一组单位的绝对部署时间"。参考实现
		// `cr_sim/engine/battle.py:659` 写的是
		// `deploy_ticks = spec.deploy_ticks + index * self.clock.ticks(summon_deploy_delay)`
		// ⇒ 第 i 只在"单位自身 deploy_time + i × 间隔"落地。
		// 旧实现把它当绝对 deploy 覆写（`cd.DeployDelayMs = 100/200`），于是整组**同时**出现
		// 而不是排队出现，观感就是"啪一下全冒出来"。
		cd.UnitStaggerMs = int32(u.SummonDeployDelayMs)
		return cd, nil
	}
	cd.UnitKey = row.Key
	return cd, nil
}

// unitDefOf 把一行战斗单位（生成物 unit 表）配成 core.UnitDef。所有数值逐字段搬运，不做换算；
// 唯一的换算是速度（官方「格/分钟」→ core 的「milli-tile/秒」），
// 走的是 core 里那个有出处的官方换算函数 core.SpeedMilliPerSec。
func unitDefOf(row *table.UnitRow) *core.UnitDef {
	return &core.UnitDef{
		ID:     int32(row.Id),
		Key:    row.Key,
		NameCN: row.NameCn,
		Kind:   core.CardKind(row.Kind), // 0=部队 1=建筑 2=塔 3=投射物（与 core 同口径）

		HP:         int32(row.Hp),
		Damage:     int32(row.Damage),
		HitSpeedMs: int32(row.HitSpeedMs),
		LoadTimeMs: int32(row.LoadTimeMs),

		SpeedMilliPerSec: core.SpeedMilliPerSec(int32(row.Speed)),
		// 原始官方速度（格/分钟）也保留：整数换算对非 3 的倍数的速度有损
		// （550 → 9166 → 549），而协议下发的 proj_speed 要逐字一致。
		SpeedTilesPerMinute: int32(row.Speed),
		RangeMilli:          int32(row.RangeMt),
		SightMilli:          int32(row.SightMt),
		DeployMs:            int32(row.DeployMs),
		RadiusMilli:         int32(row.RadiusMt),
		Mass:                int32(row.Mass),

		AtkAir:        row.AtkAir != 0,
		AtkGround:     row.AtkGround != 0,
		OnlyBuildings: row.OnlyBuildings != 0,
		OnlyTowers:    row.OnlyTowers != 0,
		OnlyTroops:    row.OnlyTroops != 0,

		ProjectileKey: row.ProjectileKey,

		DeathSpawnKey:       row.DeathSpawnKey,
		DeathSpawnN:         int32(row.DeathSpawnN),
		DeathDamage:         int32(row.DeathDamage),
		DeathAoeRadiusMilli: int32(row.DeathAoeRadiusMt),

		LifeMs:           int32(row.LifeMs),
		SpawnKey:         row.SpawnKey,
		SpawnN:           int32(row.SpawnN),
		SpawnRadiusMilli: int32(row.SpawnRadiusMt),
		SpawnIntervalMs:  int32(row.SpawnIntervalMs),
		SpawnStaggerMs:   int32(row.SpawnStaggerMs),
		SpawnLimit:       int32(row.SpawnLimit),

		// ★ 2026-09-23 增（差异登记 D142）：普攻溅射半径。旧实现的 `aoe_radius_mt`
		// 只映射了角色表的 `area_damage_radius`，投射物行的溅射半径（官方投射物表的
		// `radius`）被错列进了 `radius_mt`（= 弹体半径）⇒ 法师/屠夫/滚石/炸弹兵/公主/
		// 火精灵的溅射一发不剩，且 core 侧**根本没有**这个字段。
		AoeRadiusMilli: int32(row.AoeRadiusMt),

		// ★ 2026-09-23 增（差异登记 D142）：跳河。官方 `jump_enabled`/`jump_height`/
		// `jump_speed`（野猪骑士 / 王子 / 黑暗王子 / 野蛮人攻城槌 四张卡是 true + 4000 + 160）。
		JumpHeightMilli:      int32(row.JumpHeightMt),
		JumpSpeedTilesPerMin: int32(row.JumpSpeed),

		SpriteDir: row.SpriteDir,

		// 飞行：unit 表的 flying 列（出处官方 cards_stats_characters.json 的 flying_height > 0）。
		// core 的 isFlyingDef 另有官方兜底表（core/card.go 的 officialFlyerKeys），
		// 所以"列在 / 列不在"两种状态 core 都能跑；列缺失时这里恒为 false。
		Flying: row.Flying != 0,
	}
}

// spellDefOf 把一行法术（生成物 spell 表）配成 core.SpellDef。
func spellDefOf(row *table.SpellRow) *core.SpellDef {
	return &core.SpellDef{
		ID:     int32(row.Id),
		Key:    row.Key,
		NameCN: row.NameCn,
		Elixir: int32(row.Elixir),

		RadiusMilli:      int32(row.RadiusMt),
		InstantDamage:    int32(row.InstantDamage),
		DurationMs:       int32(row.DurationMs),
		TickMs:           int32(row.TickMs),
		DamagePerTick:    int32(row.DamagePerTick),
		HealPerSecond:    int32(row.HealPerSecond),
		SpawnKey:         row.SpawnKey,
		SpawnN:           int32(row.SpawnN),
		SpawnRadiusMilli: int32(row.SpawnRadiusMt),
		Pushback:         int32(row.Pushback),
		Anywhere:         row.Anywhere != 0,
		OnWater:          row.OnWater != 0,
	}
}

// Card 实现 core.CardTable。
func (t *cardTable) Card(id int32) (*core.CardDef, bool) {
	c, ok := t.cards[id]
	return c, ok
}

// Unit 实现 core.CardTable。
func (t *cardTable) Unit(key string) (*core.UnitDef, bool) {
	u, ok := t.units[key]
	return u, ok
}

// CardCount / UnitCount 供装配日志使用。
func (t *cardTable) CardCount() int { return len(t.cards) }
func (t *cardTable) UnitCount() int { return len(t.units) }

// cardPool 把 60 张卡池下发成协议体（GetCardPoolReply.cards）。
//
// 每张卡额外带 `projectile_key` + `proj_speed`（远程判定 + 弹道速度）：这两条是
// 玩法规则（参考规格 §4 投射物），所以走 core.ProjectileOf，⛔ 不在本层写判定分支。
// 法术卡再带 `aoe_radius_milli`（作用半径）：客户端拖出法术时要在落点画半径圈，
// 半径必须等于真实作用范围（`spell.tsv` 的 `radius_mt`）—— 数值权威在配表，故由本层下发。
func (t *cardTable) cardPool() []def.CardInfo {
	out := make([]def.CardInfo, 0, len(t.cardRows))
	for _, row := range t.cardRows {
		card := t.cards[int32(row.Id)]
		projKey, projSpeed := core.ProjectileOf(t, card)
		// 法术半径：只对法术卡有意义（`card.Spell` 为 nil 时留 0）。
		var aoeRadius int32
		if card != nil && card.Spell != nil {
			aoeRadius = card.Spell.RadiusMilli
		}
		out = append(out, def.CardInfo{
			ID:             int32(row.Id),
			Key:            row.Key,
			NameCn:         row.NameCn,
			NameEn:         row.NameEn,
			Type:           int32(row.Type),
			Rarity:         int32(row.Rarity),
			Elixir:         int32(row.Elixir),
			Arena:          int32(row.Arena),
			Icon:           row.Icon,
			ProjectileKey:  projKey,
			ProjSpeed:      projSpeed,
			AoeRadiusMilli: aoeRadius,
		})
	}
	return out
}
