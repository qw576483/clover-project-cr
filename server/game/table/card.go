// ★ 手写文件（表 card_cs）：生成器只在文件不存在时写一份模板，此后**永不覆盖**
// （gen.GoGenerate 对上层文件固定 WriteFile(..., false)）——
// 那份模板会嵌入 base 层的 `encoding/csv` 解析器，而它会把空单元格吃掉、整行左移（见 tsv.go 顶部）。
// 所以本文件刻意**不嵌入 base**：自己实现 Load/Get/Rows/Len/Clear，数据仍是同一份 tsv。
// ⛔ 不保留模板里的 OnBeforeLoad/OnLoadRow/OnAfterLoad 钩子 —— 钩子的参数类型是 base 的行结构，
// 而本实现不走 base 层，留着会变成"永不触发的钩子"；要加加载期逻辑就直接写进 Load。
package table

import "fmt"

// CardRow 表 "card"（源表 策划/数值文档/card_cs.txt → card_cs-pack.xlsx 的 sheet card_cs）的一行。
// 字段名与生成器给 base 层的命名逐字一致（ToGoIdent(列名)），便于两层对照阅读。
type CardRow struct {
	Id        int    // 本表主键（26010001 起）
	Key       string // 官方 cards.json 的 key（逐字）
	NameCn    string // 中文名（cards_i18n）
	NameEn    string // 英文名（cards_i18n）
	Type      int    // 0=部队 1=法术 2=建筑
	Rarity    int    // 0=普通 1=稀有 2=史诗 3=传说
	Elixir    int    // 圣水消耗
	Arena     int    // 官方 arena（解锁竞技场）
	Icon      string // 客户端卡面图名（约定 card_<key>）
	SpriteDir string // 素材目录名（多目录以 ; 连接）
	DeployFx  string // 部署特效名（官方数据无此字段 ⇒ 留空）
}

// CardTable 表 "card" 的只读容器（按主键索引 + 保留配表顺序）。
type CardTable struct {
	rows  []*CardRow
	index map[int]*CardRow
}

// NewCardTable 创建空表。
func NewCardTable() *CardTable {
	return &CardTable{index: make(map[int]*CardRow)}
}

// Get 按主键取行（不存在返回 nil，与生成器 base 层的 Get 同语义）。
func (t *CardTable) Get(id int) *CardRow { return t.index[id] }

// Rows 返回全部行（**配表顺序**：卡池下发与 AI 选卡都依赖它）。
func (t *CardTable) Rows() []*CardRow { return t.rows }

// Len 当前行数。
func (t *CardTable) Len() int { return len(t.rows) }

// Clear 清空全部数据（tsv 缺失时由 registry 调用）。
func (t *CardTable) Clear() {
	t.rows = nil
	t.index = make(map[int]*CardRow)
}

// Load 从 tsv 文本加载（首行列名表头，'\t' 分隔），整体替换旧数据。
// 表头缺列 / 行宽与表头不一致 / 整型列非整数 ⇒ 返回错误，不做任何静默降级。
func (t *CardTable) Load(content string) error {
	header, recs, err := splitTSV(content)
	if err != nil {
		return fmt.Errorf("card: %w", err)
	}
	c, err := newCols("card", header)
	if err != nil {
		return err
	}
	if err := c.require("id", "key", "name_cn", "name_en", "type", "rarity", "elixir",
		"arena", "icon", "sprite_dir", "deploy_fx"); err != nil {
		return err
	}

	rows := make([]*CardRow, 0, len(recs))
	index := make(map[int]*CardRow, len(recs))
	for i, rec := range recs {
		c.line = i + 2 // +1 = 数据行，+1 = 表头占了一行
		if err := checkRowWidth("card", header, rec, c.line); err != nil {
			return err
		}
		row := &CardRow{
			Id:        c.int(rec, "id"),
			Key:       c.str(rec, "key"),
			NameCn:    c.str(rec, "name_cn"),
			NameEn:    c.str(rec, "name_en"),
			Type:      c.int(rec, "type"),
			Rarity:    c.int(rec, "rarity"),
			Elixir:    c.int(rec, "elixir"),
			Arena:     c.int(rec, "arena"),
			Icon:      c.str(rec, "icon"),
			SpriteDir: c.str(rec, "sprite_dir"),
			DeployFx:  c.str(rec, "deploy_fx"),
		}
		if c.err != nil {
			return c.err
		}
		rows = append(rows, row)
		if _, dup := index[row.Id]; !dup {
			index[row.Id] = row // 重复主键取首个（重复由业务侧 Warnf 报出，见 logic/cardtable.go）
		}
	}
	t.rows, t.index = rows, index
	return nil
}
