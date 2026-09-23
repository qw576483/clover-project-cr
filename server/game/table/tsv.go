// ★ 本文件与 card.go / unit.go / spell.go **由人维护**（不是生成物）：生成器只在文件不存在时
// 写一份模板，之后永不覆盖 —— gen.GoGenerate 对上层文件固定 WriteFile(..., false)，
// 见 clover-tools/table/core/internal/gen/go.go。四个文件共同构成打表产物的上层表实现。
//
// ★ 为什么不用生成器 base 层的那套解析（`encoding/csv` + `r.Comma = '\t'` + `r.TrimLeadingSpace = true`）：
// `unicode.IsSpace('\t')` 为真 ⇒ csv 在**每个字段**前都会把「前导空白」整片剥掉，
// 于是一个**空单元格**会把它前面那个 tab 一起吃掉、后面的列集体左移，而且不报任何错。
// 实测 unit.tsv 的 archers 行：原始 37 列被读成 35 列，
// `summon_key` 读成 "0"（实际 "Archer"）、`summon_n` 读成 100（实际 2）。
//
// 本文件用显式 `strings.Split(line, "\t")`：空单元格原样保留；
// 取值一律按**列名**（表头建 name → index 索引，与列顺序无关，所以插列不会打偏）；
// 三类情况**直接报错**，宁可起服失败也不让错数据流进 core：
//
//	① 某行字段数与表头不一致（正是上面那类错列）；
//	② 表头缺少本实现会读的列（例如有人把 hp 改名成 hitpoints）；
//	③ 整型列里出现非整数值（生成物的 parseInt 会静默按 0 处理）。
package table

import (
	"fmt"
	"sort"
	"strconv"
	"strings"
)

// splitTSV 把 tsv 文本切成「表头 + 数据行」。
// 行分隔只看 '\n'（并剥掉行尾 '\r'，兼容 CRLF）；空行（含只有空白/tab 的行）跳过 ——
// 打表工具写出的 tsv 是「一列不落地写全 + 行尾 '\n'」，不会产生这种行。
func splitTSV(content string) ([]string, [][]string, error) {
	lines := strings.Split(content, "\n")
	var header []string
	rows := make([][]string, 0, len(lines))
	for _, raw := range lines {
		line := strings.TrimRight(raw, "\r")
		if strings.TrimSpace(line) == "" {
			continue
		}
		if header == nil {
			header = strings.Split(line, "\t")
			continue
		}
		rows = append(rows, strings.Split(line, "\t"))
	}
	if header == nil {
		return nil, nil, fmt.Errorf("tsv 无内容")
	}
	return header, rows, nil
}

// cols 表头索引 + 取值器。
// 取值错误（整型列非整数）记在 err 上，由调用方在构造完一行后检查 ——
// 这样每个字段一行代码，错误信息里仍能带上表名/行号/列名。
type cols struct {
	table string
	idx   map[string]int
	line  int   // 当前数据行号（1 = 表头，故第 1 行数据是 2）
	err   error // 首个取值错误（nil = 至今没有错误）
}

// newCols 建表头索引。列名去首尾空白；重复列名直接报错（重复列必然让取值语义含糊）。
func newCols(tableName string, header []string) (*cols, error) {
	c := &cols{table: tableName, idx: make(map[string]int, len(header))}
	for i, h := range header {
		h = strings.TrimSpace(h)
		if h == "" {
			continue // 打表工具不写空列名；真出现就跳过（取值恒空）
		}
		if _, dup := c.idx[h]; dup {
			return nil, fmt.Errorf("%s: 表头有重复列名 %q", tableName, h)
		}
		c.idx[h] = i
	}
	return c, nil
}

// require 校验表头包含本实现的代码会读的全部列。
// 漏一列会让对应字段恒为 0/空（静默错），所以这里 fail-fast 并把实际表头打出来。
func (c *cols) require(cols ...string) error {
	var miss []string
	for _, col := range cols {
		if _, ok := c.idx[col]; !ok {
			miss = append(miss, col)
		}
	}
	if len(miss) > 0 {
		return fmt.Errorf("%s: 表头缺少列 %s；实际表头 = [%s]",
			c.table, strings.Join(miss, ", "), strings.Join(c.header(), " "))
	}
	return nil
}

// header 还原表头（报错信息用）：形如 [#0=id #1=key …]，按列下标排序。
func (c *cols) header() []string {
	out := make([]string, 0, len(c.idx))
	for name, i := range c.idx {
		out = append(out, fmt.Sprintf("#%d=%s", i, name))
	}
	sort.Strings(out)
	return out
}

// str 取字符串列（列不存在 / 越界 / 单元格为空都返回空串）。
func (c *cols) str(rec []string, col string) string {
	i, ok := c.idx[col]
	if !ok || i < 0 || i >= len(rec) {
		return ""
	}
	return strings.TrimSpace(rec[i])
}

// int 取整型列：空单元格 → 0（与打表工具 parseInt 同口径）；非整数 → 记错误、返回 0。
func (c *cols) int(rec []string, col string) int {
	s := c.str(rec, col)
	if s == "" {
		return 0
	}
	v, err := strconv.Atoi(s)
	if err != nil {
		if c.err == nil {
			c.err = fmt.Errorf("%s: 第 %d 行列 %s 的值 %q 不是整数", c.table, c.line, col, s)
		}
		return 0
	}
	return v
}

// flag 取 0/1 列（非 0 即真）。
func (c *cols) flag(rec []string, col string) bool { return c.int(rec, col) != 0 }

// checkRowWidth 行宽必须与表头一致：少一列 / 多一列都意味着这行**错位**了，
// 而错位的行是"看起来正常、数值全错"的那种故障（见文件头注释的缺陷 2）。
func checkRowWidth(tableName string, header, rec []string, line int) error {
	if len(rec) != len(header) {
		return fmt.Errorf("%s: 第 %d 行有 %d 列，表头 %d 列（列数不一致 ⇒ 数据错位，拒绝加载）",
			tableName, line, len(rec), len(header))
	}
	return nil
}
