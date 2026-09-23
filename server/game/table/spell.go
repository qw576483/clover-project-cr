// ★ 手写文件（表 spell_cs）：生成器只在文件不存在时写一份模板，此后**永不覆盖**
// （gen.GoGenerate 对上层文件固定 WriteFile(..., false)）——
// 那份模板会嵌入 base 层的 `encoding/csv` 解析器，而它会把空单元格吃掉、整行左移（见 tsv.go 顶部）。
// 所以本文件刻意**不嵌入 base**：自己实现 Load/Get/Rows/Len/Clear，数据仍是同一份 tsv。
// 额外提供 `GetByKey`：法术是按**卡牌 key** 与卡牌表关联的
// （core.CardTable.Card(id).Spell 靠 key 找 SpellDef），生成器只按第一列 id 建索引。
package table

import "fmt"

// SpellRow 表 "spell"（源表 策划/数值文档/spell_cs.txt → spell_cs-pack.xlsx 的 sheet spell_cs）的一行。
type SpellRow struct {
	Id            int    // 本表主键（26030001 起）
	Key           string // 卡牌 key（与 card.key 一致）
	NameCn        string // 中文名
	Elixir        int    // 圣水消耗
	Rarity        int    // 0=普通 1=稀有 2=史诗 3=传说
	RadiusMt      int    // 作用半径 milli-tile
	InstantDamage int    // 立即伤害（11 级）；持续伤害型为 0
	CrownDamage   int    // 对王冠塔的实际伤害
	DurationMs    int    // 作用区存活时长 ms
	TickMs        int    // 持续伤害结算间隔 ms
	DamagePerTick int    // 每跳伤害（11 级）
	HealPerSecond int    // 每秒治疗量
	SpawnKey      string // 法术召唤的实体 key
	SpawnN        int    // 法术召唤数量
	SpawnRadiusMt int    // 法术召唤铺设半径 milli-tile
	Pushback      int    // 击退力（milli-tile）
	Anywhere      int    // 能否落敌方半场 0/1
	OnWater       int    // 能否落河面 0/1
	SpriteDir     string // 卡面素材目录名
}

// SpellTable 表 "spell" 的只读容器（按主键 id + 按卡牌 key 双索引）。
type SpellTable struct {
	rows  []*SpellRow
	index map[int]*SpellRow
	byKey map[string]*SpellRow
}

// NewSpellTable 创建空表。
func NewSpellTable() *SpellTable {
	return &SpellTable{index: make(map[int]*SpellRow), byKey: make(map[string]*SpellRow)}
}

// Get 按主键取行（不存在返回 nil，与生成器 base 层的 Get 同语义）。
func (t *SpellTable) Get(id int) *SpellRow { return t.index[id] }

// GetByKey 按卡牌 key 取行（不存在返回 nil）。key 为空的行不入索引。
// 重复 key 取**首个**出现的行（重复由业务侧 Warnf 报出，见 logic/cardtable.go）。
func (t *SpellTable) GetByKey(key string) *SpellRow { return t.byKey[key] }

// Rows 返回全部行（配表顺序）。
func (t *SpellTable) Rows() []*SpellRow { return t.rows }

// Len 当前行数。
func (t *SpellTable) Len() int { return len(t.rows) }

// Clear 清空全部数据（tsv 缺失时由 registry 调用）。
func (t *SpellTable) Clear() {
	t.rows = nil
	t.index = make(map[int]*SpellRow)
	t.byKey = make(map[string]*SpellRow)
}

// Load 从 tsv 文本加载（首行列名表头，'\t' 分隔），整体替换旧数据。
// 表头缺列 / 行宽与表头不一致 / 整型列非整数 ⇒ 返回错误，不做任何静默降级。
func (t *SpellTable) Load(content string) error {
	header, recs, err := splitTSV(content)
	if err != nil {
		return fmt.Errorf("spell: %w", err)
	}
	c, err := newCols("spell", header)
	if err != nil {
		return err
	}
	if err := c.require("id", "key", "name_cn", "elixir", "rarity", "radius_mt", "instant_damage",
		"crown_damage", "duration_ms", "tick_ms", "damage_per_tick", "heal_per_second",
		"spawn_key", "spawn_n", "spawn_radius_mt", "pushback", "anywhere", "on_water",
		"sprite_dir"); err != nil {
		return err
	}

	rows := make([]*SpellRow, 0, len(recs))
	index := make(map[int]*SpellRow, len(recs))
	byKey := make(map[string]*SpellRow, len(recs))
	for i, rec := range recs {
		c.line = i + 2 // +1 = 数据行，+1 = 表头占了一行
		if err := checkRowWidth("spell", header, rec, c.line); err != nil {
			return err
		}
		row := &SpellRow{
			Id:            c.int(rec, "id"),
			Key:           c.str(rec, "key"),
			NameCn:        c.str(rec, "name_cn"),
			Elixir:        c.int(rec, "elixir"),
			Rarity:        c.int(rec, "rarity"),
			RadiusMt:      c.int(rec, "radius_mt"),
			InstantDamage: c.int(rec, "instant_damage"),
			CrownDamage:   c.int(rec, "crown_damage"),
			DurationMs:    c.int(rec, "duration_ms"),
			TickMs:        c.int(rec, "tick_ms"),
			DamagePerTick: c.int(rec, "damage_per_tick"),
			HealPerSecond: c.int(rec, "heal_per_second"),
			SpawnKey:      c.str(rec, "spawn_key"),
			SpawnN:        c.int(rec, "spawn_n"),
			SpawnRadiusMt: c.int(rec, "spawn_radius_mt"),
			Pushback:      c.int(rec, "pushback"),
			Anywhere:      c.int(rec, "anywhere"),
			OnWater:       c.int(rec, "on_water"),
			SpriteDir:     c.str(rec, "sprite_dir"),
		}
		if c.err != nil {
			return c.err
		}
		rows = append(rows, row)
		if _, dup := index[row.Id]; !dup {
			index[row.Id] = row
		}
		if row.Key == "" {
			continue // 无 key 的行（理论上不会出现）不入索引，避免 byKey[""] 被抢
		}
		if _, dup := byKey[row.Key]; !dup {
			byKey[row.Key] = row
		}
	}
	t.rows, t.index, t.byKey = rows, index, byKey
	return nil
}
