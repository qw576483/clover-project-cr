// ★ 手写文件（表 unit_cs）：生成器只在文件不存在时写一份模板，此后**永不覆盖**
// （gen.GoGenerate 对上层文件固定 WriteFile(..., false)）——
// 那份模板会嵌入 base 层的 `encoding/csv` 解析器，而它会把空单元格吃掉、整行左移（见 tsv.go 顶部）。
// 所以本文件刻意**不嵌入 base**：自己实现 Load/Get/Rows/Len/Clear，数据仍是同一份 tsv。
// 额外提供 `GetByKey`：生成器只按**第一列（id）**建索引，而战斗实体是按**实体 key** 取的
// （core.CardTable.Unit(key)），所以这里再建一份 key → 行 的索引。
package table

import "fmt"

// UnitRow 表 "unit"（源表 策划/数值文档/unit_cs.txt → unit_cs-pack.xlsx 的 sheet unit_cs）的一行：
// 部队 + 建筑 + 塔 + 投射物都在这张表里，用 kind 区分。
type UnitRow struct {
	Id                  int    // 本表主键（26020001 起）
	Key                 string // 实体键：卡牌行为卡牌 key；非卡牌实体为官方实体名（逐字）
	NameCn              string // 中文名（官方数据无中文名的实体留空）
	Kind                int    // 0=部队 1=建筑 2=塔 3=投射物
	Hp                  int    // 生命值（11 级）
	Damage              int    // 单发伤害（11 级）；本体为 0 时取其上投射物的伤害
	HitSpeedMs          int    // 攻击间隔 ms
	LoadTimeMs          int    // 首次攻击前摇 ms
	Speed               int    // 移动速度（格/分钟）；投射物行为弹道速度
	RangeMt             int    // 攻击射程 milli-tile
	SightMt             int    // 视野 milli-tile
	DeployMs            int    // 部署延迟 ms
	RadiusMt            int    // 碰撞半径 milli-tile；投射物行为弹体半径
	Mass                int    // 质量（推挤分离用）
	AtkAir              int    // 能否攻击空中 0/1
	AtkGround           int    // 能否攻击地面 0/1
	OnlyBuildings       int    // 只打建筑 0/1
	OnlyTowers          int    // 只打塔 0/1
	OnlyTroops          int    // 只打部队 0/1
	ProjectileKey       string // 攻击投射物实体 key（对应本表 kind=3 的行）
	DeathSpawnKey       string // 亡语召唤实体 key
	DeathSpawnN         int    // 亡语召唤数量
	DeathDamage         int    // 亡语伤害（11 级）
	DeathAoeRadiusMt    int    // 亡语爆炸半径 milli-tile
	LifeMs              int    // 存活时长 ms（建筑/临时实体）
	SpawnKey            string // 周期性召唤实体 key
	SpawnN              int    // 周期性召唤数量
	SpawnRadiusMt       int    // 周期性召唤铺设半径 milli-tile
	SpawnIntervalMs     int    // 周期性召唤间隔 ms
	SpawnLimit          int    // 场上召唤上限
	SummonKey           string // 落点召唤实体 key（群体卡）
	SummonN             int    // 落点召唤数量
	SummonRadiusMt      int    // 落点环形铺设半径 milli-tile
	SummonDeployDelayMs int    // 落点召唤的部署延迟 ms
	AoeRadiusMt         int    // 普攻溅射半径 milli-tile（0=无溅射）
	SpriteDir           string // 素材目录名
	Flying              int    // 1=飞行单位（官方 flying_height>0）；0=地面（建筑/塔/投射物恒 0）
}

// UnitTable 表 "unit" 的只读容器（按主键 id + 按实体 key 双索引）。
type UnitTable struct {
	rows    []*UnitRow
	index   map[int]*UnitRow
	byKey   map[string]*UnitRow
}

// NewUnitTable 创建空表。
func NewUnitTable() *UnitTable {
	return &UnitTable{index: make(map[int]*UnitRow), byKey: make(map[string]*UnitRow)}
}

// Get 按主键取行（不存在返回 nil，与生成器 base 层的 Get 同语义）。
func (t *UnitTable) Get(id int) *UnitRow { return t.index[id] }

// GetByKey 按实体 key 取行（不存在返回 nil）。key 为空的行不入索引。
// 重复 key 取**首个**出现的行（重复由业务侧 Warnf 报出，见 logic/cardtable.go）。
func (t *UnitTable) GetByKey(key string) *UnitRow { return t.byKey[key] }

// Rows 返回全部行（配表顺序）。
func (t *UnitTable) Rows() []*UnitRow { return t.rows }

// Len 当前行数。
func (t *UnitTable) Len() int { return len(t.rows) }

// Clear 清空全部数据（tsv 缺失时由 registry 调用）。
func (t *UnitTable) Clear() {
	t.rows = nil
	t.index = make(map[int]*UnitRow)
	t.byKey = make(map[string]*UnitRow)
}

// Load 从 tsv 文本加载（首行列名表头，'\t' 分隔），整体替换旧数据。
// 表头缺列 / 行宽与表头不一致 / 整型列非整数 ⇒ 返回错误，不做任何静默降级。
func (t *UnitTable) Load(content string) error {
	header, recs, err := splitTSV(content)
	if err != nil {
		return fmt.Errorf("unit: %w", err)
	}
	c, err := newCols("unit", header)
	if err != nil {
		return err
	}
	if err := c.require("id", "key", "name_cn", "kind", "hp", "damage", "hit_speed_ms",
		"load_time_ms", "speed", "range_mt", "sight_mt", "deploy_ms", "radius_mt", "mass",
		"atk_air", "atk_ground", "only_buildings", "only_towers", "only_troops",
		"projectile_key", "death_spawn_key", "death_spawn_n", "death_damage", "death_aoe_radius_mt",
		"life_ms", "spawn_key", "spawn_n", "spawn_radius_mt", "spawn_interval_ms", "spawn_limit",
		"summon_key", "summon_n", "summon_radius_mt", "summon_deploy_delay_ms", "aoe_radius_mt",
		"sprite_dir", "flying"); err != nil {
		return err
	}

	rows := make([]*UnitRow, 0, len(recs))
	index := make(map[int]*UnitRow, len(recs))
	byKey := make(map[string]*UnitRow, len(recs))
	for i, rec := range recs {
		c.line = i + 2 // +1 = 数据行，+1 = 表头占了一行
		if err := checkRowWidth("unit", header, rec, c.line); err != nil {
			return err
		}
		row := &UnitRow{
			Id:                  c.int(rec, "id"),
			Key:                 c.str(rec, "key"),
			NameCn:              c.str(rec, "name_cn"),
			Kind:                c.int(rec, "kind"),
			Hp:                  c.int(rec, "hp"),
			Damage:              c.int(rec, "damage"),
			HitSpeedMs:          c.int(rec, "hit_speed_ms"),
			LoadTimeMs:          c.int(rec, "load_time_ms"),
			Speed:               c.int(rec, "speed"),
			RangeMt:             c.int(rec, "range_mt"),
			SightMt:             c.int(rec, "sight_mt"),
			DeployMs:            c.int(rec, "deploy_ms"),
			RadiusMt:            c.int(rec, "radius_mt"),
			Mass:                c.int(rec, "mass"),
			AtkAir:              c.int(rec, "atk_air"),
			AtkGround:           c.int(rec, "atk_ground"),
			OnlyBuildings:       c.int(rec, "only_buildings"),
			OnlyTowers:          c.int(rec, "only_towers"),
			OnlyTroops:          c.int(rec, "only_troops"),
			ProjectileKey:       c.str(rec, "projectile_key"),
			DeathSpawnKey:       c.str(rec, "death_spawn_key"),
			DeathSpawnN:         c.int(rec, "death_spawn_n"),
			DeathDamage:         c.int(rec, "death_damage"),
			DeathAoeRadiusMt:    c.int(rec, "death_aoe_radius_mt"),
			LifeMs:              c.int(rec, "life_ms"),
			SpawnKey:            c.str(rec, "spawn_key"),
			SpawnN:              c.int(rec, "spawn_n"),
			SpawnRadiusMt:       c.int(rec, "spawn_radius_mt"),
			SpawnIntervalMs:     c.int(rec, "spawn_interval_ms"),
			SpawnLimit:          c.int(rec, "spawn_limit"),
			SummonKey:           c.str(rec, "summon_key"),
			SummonN:             c.int(rec, "summon_n"),
			SummonRadiusMt:      c.int(rec, "summon_radius_mt"),
			SummonDeployDelayMs: c.int(rec, "summon_deploy_delay_ms"),
			AoeRadiusMt:         c.int(rec, "aoe_radius_mt"),
			SpriteDir:           c.str(rec, "sprite_dir"),
			Flying:              c.int(rec, "flying"),
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
