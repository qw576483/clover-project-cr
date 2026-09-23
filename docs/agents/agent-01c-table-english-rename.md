# agent-01c：配表表名英文化 + tsv 解析修复 + 调用点同步

> 这是 **agent-01 / agent-01b** 的**第三次返工**（同一执行者线）。

## 0. 技能（开工必做）

- `<项目根>/tools/ai-skill/{SKILL,conventions,registry,constraints}.md` ← **项目级，首选**
  （`constraints.md` 的「已定案的口径」里已有本轮的两条定案，先读）
- 全局兜底：`~/.codebuddy/skills/ai-skill/SKILL.md` · `<仓库根>/clover-tools/ai-skill/SKILL.md`
- 配表范式：`patterns/table.md`
- ⛔ 不许改任何 skill（发现问题写进回报）

⛔ **红线**：只许读本项目、`clover-server-engine`、`clover-doc`、skill。
工作区里**其它** `clover-project-*` **一律不许读**。

## 1. 要修的两件事（都是你上一轮自己留的缺陷）

### ★ 缺陷 1：中文表名 ⇒ 生成物跨包不可用

你生成的三张表叫 `卡牌_cs` / `战斗单位_cs` / `法术_cs`，生成器按 sheet 名派生 Go 类型与字段，
于是 `table.Tables` 的字段是 `卡牌` / `战斗单位` / `法术`。
**Go 只把「首字符为大写 ASCII 字母」视为导出** ⇒ 这三个字段在 `table` 包外**根本写不出来**：

```
server/game/logic/cardtable.go:36:36: table.Default.战斗单位 undefined (cannot refer to unexported field 战斗单位)
server/game/logic/ai.go:32:36:        table.Default.卡牌   undefined (cannot refer to unexported field 卡牌)
server/game/logic/cardtable.go:228:24: table.Default.卡牌  undefined (cannot refer to unexported field 卡牌)
```

反射也读不到（`reflect.Value.Call using value obtained using unexported field`）。
**业务侧（`game/logic/`）现在绕开了生成物，自带按列名解析器 —— 这是必须消除的绕行。**

**修法（已定案，照做）**：把**表名（sheet 名）改成英文 + `_cs` 后缀**：
`卡牌_cs` → **`card_cs`**、`战斗单位_cs` → **`unit_cs`**、`法术_cs` → **`spell_cs`**。
- 目标访问器：`table.Default.Card.Get(id)` / `table.Default.Unit.Get(id)` / `table.Default.Spell.Get(id)`
- **xlsx 文件名**按全局 skill「一个功能一个中文名 xlsx」可保留中文（如 `卡牌_cs-pack.xlsx`），
  但 **sheet 名必须 = 表名 = `card_cs`**（否则整表静默跳过）
- 源表 `*.txt` 的文件名同步改为 `card_cs.txt` / `unit_cs.txt` / `spell_cs.txt`
  （表名从文件名派生，二者必须一致）

### ★ 缺陷 2：生成的 tsv 解析器吃掉空单元格

生成器用 `encoding/csv` + `Comma='\t'` + **`TrimLeadingSpace=true`**，
而 `unicode.IsSpace('\t')` 为真 ⇒ **每个空单元格被当前导空白吃掉，后续列集体左移且不报错**。
实测：`archers` 行 37 列变 35 列，`summon_key` 读成 `"0"`、`summon_n` 读成 `100`。

**修法**：把 tsv 解析改成**显式 `strings.Split(line, "\t")`**（不用 `encoding/csv`）。
落点选**生成器不会覆盖的那一层**（生成器自己注明"首次生成后不再覆盖"的上层文件，
例如 `server/game/table/base/base_table.go` —— 先读生成器的产物结构再决定落点），
并在文件头注释里写清"为什么不用 encoding/csv"。
⛔ 不许改 `clover-tools/table`（共享工具目录不在你的边界内）—— 若确认根因在共享生成器的模板里，
**只在回报里说明**，我们那边单独处理。

## 2. 你要顺带完成的事

1. **重新打表**：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/table.ps1 -Force`
   （你上一轮踩过：不带 `-Force` 不会覆盖已存在的 `*-pack.xlsx`）
2. **改 `server/game/logic/` 的调用点**：把绕行的按列名解析器**换成**生成物的
   `table.Default.Card/Unit/Spell`；若绕行解析器里还有生成物没有的能力（如按 `key` 索引），
   就把它**收敛成薄薄一层适配**（`logic/cardtable.go`），并把「为什么还需要它」写进注释。
   ⛔ 调用点总共不超过 10 处，**不许顺手重构 `logic/` 的其它逻辑**。
3. **确认 D3 差异不受影响**：`The Log` 的 290（Legendary 索引 2）等取值在下标层面不变。
4. **`策划/数值文档/打表对照表.tsv` 是追加而非覆盖** —— 清成单份（工具行为，别只删重复行就交差，
   要让重跑两次的结果一致）。

## 3. 允许改动的范围（⛔ 严格）

- `策划/数值文档/`（源表改名 + 内容不变 + `-pack.xlsx` + 对照表）
- `server/game/table/`（打表产物）
- `server/game/logic/`（**只许改调用点 + 解析路径**，不许改玩法/协议/房间逻辑）
- `tools/table.ps1` / `tools/table.config.yaml`（表名配置）
- `.ai-tmp/hosts/`、`.ai-tmp/test/`（脚本与一次性产物）

⛔ **不许**改：`server/game/core/`、`server/game/def/`、`server/game/datadef/`、`server/main.go`、
`server/configs/`（主 agent 刚修过 `nats.addr`，别动）、`client/`、`docs/`、`策划/策划案/`、
`策划/{验收表,对照表,素材调研}.md`、`tools/verify.ps1`、`tools/ai-skill/`

## 4. 验收标准（做完把原始输出贴进回报）

- [ ] `cd server && go build ./... && go vet ./...` **全绿**（这是本轮的头号判据 —— 之前是红的）
- [ ] `go test ./game/core/... -count=1` 全绿
- [ ] `go test ./game/logic/ -count=1` 全绿（agent-03 写的离线断言必须仍通过）
- [ ] `grep` 确认 `server/` 下**不再出现** `table.Default.卡牌` / `table.Default.战斗单位` / `table.Default.法术`
- [ ] 表名已是 `card_cs` / `unit_cs` / `spell_cs`：贴 `server/game/table/registry.go` 的 `Tables` 结构定义
      与 tsv 文件名
- [ ] **空单元格不再错列**：拿 `archers` 行（或任一含空列的卡）当场解析，断言
      `summon_key` / `summon_n` 等空列读出来是"空"而不是把后面的值左移进来；贴修复前后对照
- [ ] `tools/table.ps1 -Force` 跑两遍，产物 SHA256 相同（贴哈希）
- [ ] 三张表行数仍 **60 / 90 / 10**
- [ ] 起服 `go run . -config configs/all` 无 panic/error，日志里配表加载行数正确
- [ ] **端到端复跑**：用 `.ai-tmp/test/driver/`（我上一轮 agent-03 留下的驱动）重跑一次
      建房 → 加入 → 开打 → 出牌 → 结算，确认改名没打断任何东西（贴关键报文）
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1` 原始输出

## 5. 约束

- 改动四拍：① 只读取证 + 改动清单 → ② 批量改（不编译） → ③ **一次**编译 + 预演 → ④ 集中出证据
- 日志用包级 `logger.Infof/Warnf/Errorf`；⛔ 不许 `log.Printf` / `fmt.Print`
- ⛔ 不许改契约（消息号 / 协议字段 / `core` 公开签名）
- ⛔ 不许再派生任何子 agent
- ⛔ 不许写 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`；未完成项只写在**回报消息**里
- 一次性产物**只许放** `.ai-tmp/`

## 6. 回报格式

```
产出物：<绝对路径清单>
自检：<命令 + 原始输出（build/vet/test + 改名前后 grep + 空列对照 + 起服日志 + 端到端关键报文）>
未决：无 / <具体条目>
```
