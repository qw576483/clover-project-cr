// 本文件是**离线秒级断言**（`cd server && go test ./game/table/ -count=1`），
// 把本轮修掉的两条配表缺陷钉成机械判据：
//
//	① 表名英文化 ⇒ 访问器在包外可用。所以本文件刻意用 **包外测试包**（`package table_test`）：
//	   旧的中文字段（`Cards{卡牌 …}`）在这里连写都写不出来 —— 那正是缺陷 1 的现象。
//	② 空单元格不能被吃掉。取 unit.tsv 里真实带空列的 archers 行（summon_key / death_spawn_key …）
//	   断言它读出来是"空"而不是把后面的列左移进来，并顺带断言错列 / 缺列会**报错**而不是静默通过。
package table_test

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"clover-cr/game/table"
)

// loadDir 是测试的工作目录（= 本包目录 server/game/table）下的 tsv 目录。
const loadDir = "tsv"

// TestLoadAllRowCounts 三张表的行数（源表 60 / 90 / 10）与逐表加载钩子。
func TestLoadAllRowCounts(t *testing.T) {
	loaded := map[string]int{}
	table.OnLoadedOne = func(name string, count int) { loaded[name] = count }
	tbl := table.NewTables()
	if err := tbl.LoadAll(loadDir); err != nil {
		t.Fatalf("LoadAll(%s) 失败: %v", loadDir, err)
	}
	if tbl.Card.Len() != 60 || tbl.Unit.Len() != 90 || tbl.Spell.Len() != 10 {
		t.Fatalf("行数应为 60/90/10，实际 %d/%d/%d", tbl.Card.Len(), tbl.Unit.Len(), tbl.Spell.Len())
	}
	if loaded["card"] != 60 || loaded["unit"] != 90 || loaded["spell"] != 10 {
		t.Fatalf("逐表钩子行数应为 card=60 unit=90 spell=10，实际 %v", loaded)
	}
	// ★ 缺陷 1 的判据：下面这些访问器在**包外**必须能写出来（中文表名时它们写不出来，
	// 报 "cannot refer to unexported field"，反射也读不到）。
	if c := table.Default.Card.Get(26010002); c == nil || c.Key != "archers" || c.NameEn != "Archers" {
		t.Fatalf("table.Default.Card.Get(26010002) = %+v（应为 archers / Archers）", c)
	}
	if u := table.Default.Unit.GetByKey("PrincessTower"); u == nil || u.Hp != 3584 {
		t.Fatalf("table.Default.Unit.GetByKey(\"PrincessTower\") = %+v（HP 应为 3584）", u)
	}
	if s := table.Default.Spell.GetByKey("fireball"); s == nil || s.Elixir != 4 {
		t.Fatalf("table.Default.Spell.GetByKey(\"fireball\") = %+v（圣水应为 4）", s)
	}
	t.Logf("card=%d unit=%d spell=%d；访问器在包外可用（Card/Unit/Spell）",
		tbl.Card.Len(), tbl.Unit.Len(), tbl.Spell.Len())
}

// TestEmptyCellsPreserved 空单元格必须原样保留（缺陷 2 的判据）。
// archers 行在 unit.tsv 里有 2 个空单元格（death_spawn_key / spawn_key）：
// 旧解析器（encoding/csv + TrimLeadingSpace=true）会把它们吃掉、后面的列集体左移，
// 于是 summon_key 读成 "0"、summon_n 读成 100 —— 本断言就是钉死这件事。
func TestEmptyCellsPreserved(t *testing.T) {
	var units table.UnitTable
	if err := units.Load(readTSV(t, "unit.tsv")); err != nil {
		t.Fatalf("unit 表加载失败: %v", err)
	}
	row := units.GetByKey("archers")
	if row == nil {
		t.Fatalf("unit 表里没有 archers 行")
	}
	if row.DeathSpawnKey != "" || row.SpawnKey != "" {
		t.Fatalf("空单元格被读成了值：death_spawn_key=%q spawn_key=%q（应为空串）", row.DeathSpawnKey, row.SpawnKey)
	}
	if row.ProjectileKey != "ArcherArrow" {
		t.Fatalf("projectile_key=%q（应为 ArcherArrow：非空列串位了）", row.ProjectileKey)
	}
	if row.SummonKey != "Archer" || row.SummonN != 2 {
		t.Fatalf("summon_key=%q summon_n=%d（应为 Archer / 2；旧解析器读成 \"0\" / 100）", row.SummonKey, row.SummonN)
	}
	// 逐行列宽 = 表头列宽（错列这一类故障的机械判据）。
	lines := strings.Split(strings.TrimRight(readTSV(t, "unit.tsv"), "\n"), "\n")
	want := len(strings.Split(lines[0], "\t"))
	if want != 37 {
		t.Fatalf("unit.tsv 表头应为 37 列，实际 %d", want)
	}
	for i, line := range lines {
		if got := len(strings.Split(line, "\t")); got != want {
			t.Fatalf("第 %d 行列数 %d != 表头 %d", i+1, got, want)
		}
	}
	t.Logf("archers: summon_key=%q summon_n=%d projectile_key=%q death_spawn_key=%q spawn_key=%q（%d 行全部 %d 列）",
		row.SummonKey, row.SummonN, row.ProjectileKey, row.DeathSpawnKey, row.SpawnKey, len(lines)-1, want)
}

// TestLoadRejectsRaggedRow 少一列的行必须报错（⛔ 不许静默按空值处理 —— 那就是缺陷 2 的静默形态）。
func TestLoadRejectsRaggedRow(t *testing.T) {
	lines := strings.Split(strings.TrimRight(readTSV(t, "unit.tsv"), "\n"), "\n")
	fields := strings.Split(lines[1], "\t")
	mutated := append([]string{lines[0], strings.Join(fields[:len(fields)-1], "\t")}, lines[2:]...)
	var units table.UnitTable
	err := units.Load(strings.Join(mutated, "\n") + "\n")
	if err == nil {
		t.Fatalf("少一列的行应被拒绝，实际加载成功（Len=%d）", units.Len())
	}
	if !strings.Contains(err.Error(), "列数不一致") {
		t.Fatalf("报错信息没点明错列：%v", err)
	}
	t.Logf("主动改坏一行（%d 列 → %d 列）后加载被拒: %v", len(fields), len(fields)-1, err)
}

// TestLoadRejectsMissingColumn 表头缺列必须报错（漏一列会让对应字段恒为 0，属于静默错数据）。
func TestLoadRejectsMissingColumn(t *testing.T) {
	content := readTSV(t, "unit.tsv")
	header, rest, ok := strings.Cut(strings.TrimRight(content, "\n"), "\n")
	if !ok {
		t.Fatalf("unit.tsv 只有一行？")
	}
	mutated := strings.Replace(header, "\thp\t", "\thitpoints\t", 1) + "\n" + rest + "\n"
	if mutated == content {
		t.Fatalf("表头里没有 \\thp\\t 锚点，测试本身写错了")
	}
	var units table.UnitTable
	err := units.Load(mutated)
	if err == nil {
		t.Fatalf("表头缺 hp 列应被拒绝，实际加载成功（Len=%d）", units.Len())
	}
	if !strings.Contains(err.Error(), "hp") {
		t.Fatalf("报错信息没点明缺哪列：%v", err)
	}
	t.Logf("把表头的 hp 改名成 hitpoints 后加载被拒: %v", err)
}

// readTSV 读一张生成物 tsv。
func readTSV(t *testing.T, name string) string {
	t.Helper()
	b, err := os.ReadFile(filepath.Join(loadDir, name))
	if err != nil {
		t.Fatalf("读 %s 失败: %v", name, err)
	}
	return string(b)
}
