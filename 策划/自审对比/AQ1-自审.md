# AQ1 自审对比（收敛 + 盘顶边符号 + 卡面贴合）

- ⛔ 判定只有 `一致` / `不一致（差在哪）` / `未验（为什么验不了）`—— **不写"基本一致 / 大致像"**。
- ⚠️ **本片没有重采实机图**（原因见文末「为什么未验」）：因此凡是**只有眼睛能判**的行，一律标 `未验`，
  并写明验法；**代码级可机械判**的行（参数逐项同口径 / grep）标 `一致` 并给出断言原文。

## 1. 收敛公共件（Login / Register / Nickname 三面板）

| # | 项 | 判定 | 依据 / 差在哪 |
|---|---|---|---|
| 1 | 自备实现是否已全部删除 | **一致** | `rg "private static (Text\|Image\|InputField\|void) (Outlined\|BlueButton\|SlateButton\|SlateField\|Wire)\(" client/Assets/Scripts/` ⇒ **0 命中** |
| 2 | 调用点是否全改 `CrUiStyle.*` | **一致** | 13 处调用点 grep 原文（`CrUiStyle.Outlined` ×3 / `CrUiStyle.BlueButton` ×3 / `CrUiStyle.SlateButton` ×2 / `CrUiStyle.SlateField` ×5） |
| 3 | 收敛前后**参数**是否同口径 | **一致** | 逐项对照：`Skin(...)` 的帧（165 / 014）、`corner`（10 / 24）、`border`（`Vector4.zero`）、`anchor/pivot`（左上 0/1）、`fallback`、四态 tint（白 / 1.08 / 0.82 / 0.55 / fade=`ButtonFade`）、标签（白字 + `OutlineDark`、`effectDistance (2,−2)`、`FontBody`）**全部逐字相同**；唯一差异 = `BlueButton` 的**退化兜底色**（`ButtonBg` vs `ButtonPressed`，仅帧加载失败时可见）⇒ 已登记 `AQ1-允许差异.md` AQ1-D1 |
| 4 | 收敛**是否改变了外观** | **未验**（表现类） | 必须给"改前/改后"两张实机图 + 像素尺寸一致或差异说明；本片**未重采** ⇒ 无法判。验法：同一 Play 会话里按 `AP2-*`/`AO1-nickname.png` 的同机位重采三面板，逐项比 `Image.sprite.name` + 并排图 |
| 5 | 三个面板的换帧结果是否被改坏（L3 运行时读数） | **未验** | 同上；可用 AP2 原断言口径（遍历 `[UI]` 读 `Image.sprite.name` / `Image.type`）复跑 |

## 2. 主菜单状态条盘顶边符号

| # | 项 | 判定 | 依据 / 差在哪 |
|---|---|---|---|
| 6 | 符号是否正确（`statusY + GapS`） | **一致** | `MainMenuPanel.cs:197` 现有原文 `new Vector2(InsetX, statusY + GapS)`（与 AP1 在 `RoomListPanel` / `RoomPanel` 的修法逐字同口径） |
| 7 | 盘是否真的包住文本（上下各 `GapS`） | **一致** | 盘高 = `StatusH + 2*GapS`、盘顶 = `statusY + GapS`、文本顶 = `statusY`、文本高 `StatusH` ⇒ 上留白 = 下留白 = `GapS`（纯算式，可离线判） |
| 8 | 主菜单实机图与 `AO1-mainmenu-compare.png` 重比 | **未验**（表现类） | 该条从 `不一致` → `一致` 需要**重采主菜单实机图**；本片未进 Play ⇒ 未验 |

## 3. 卡面贴合（`D-AP1-9`）

| # | 项 | 判定 | 依据 / 差在哪 |
|---|---|---|---|
| 9 | 卡面左右内边距 | **一致**（按比例） | 实测原版 3.05% 卡格宽（`AQ1-量取.md` C3）⇒ 我们的 6 / 205 = 2.93%（改前 9 = 4.39%）⇒ 差 **−0.12 pt**（改前 +1.34 pt） |
| 10 | 卡面距格顶 | **一致**（按比例） | 实测原版 1.52% ⇒ 我们的 3 / 205 = 1.46%（改前 6 = 2.93%）⇒ 差 **−0.06 pt**（改前 +1.41 pt） |
| 11 | 卡面宽占比 | **一致** | 改后 94.1% 落在实测区间 93.4%~95.6% 内（改前 91.2% 在区间外） |
| 12 | 卡面**高**占比 | **未验** | 原版值**未量到**（`AQ1-量取.md` C5：卡面底与 `Level` 带分界在原版图上判不出）⇒ 无对照值 |
| 13 | 「卡面偏上留白」症状是否消除 | **未验**（表现类） | 需要"改前/改后"并排实机图；本片未重采 |
| 14 | 「槽位偏左」症状 | **未验 / 未定位根因** | 实测卡面在格内**水平居中**（`anchor(0.5,1)` + `pos.x=0`，`DeckEditPanel.cs:513-514`）⇒ 无"偏左"的几何依据；本次只改了内边距（把卡面整体放大），**未找到"偏左"的机械根因**，如实记 `未定位`（⛔ 未填估计值） |

## 为什么未验（本片的环境事实）

- 本片开工时**并行片 AQ2 正在同一台机器上占用同一个 Unity 编辑器 / 同一个 Play 会话**
  （`AQ2-drive.cs` 16:22 刚落盘；AP1 已实测过"两片同时进 Play ⇒ 截图内容互相干扰"）。
- ⇒ 本片**不再并发进 Play**：4 张被本片改动作废的实机图（`Y2-mainmenu.png` / `AJ2-longnick.png` /
  `AO1-nickname.png` / `AL3-deck-full-9th.png`）**留待串行 Play 重采**（`tools/verify.ps1` 已把它们标红）。
- `tools/verify.ps1` 当前：`SUMMARY: FAIL=2  HUMAN-ONLY=2`（2026-09-22 16:24:13）—— 与基线 `FAIL=0` 相比，
  **新增红 = 上述 4 张过期截图**（另 1 行 `AN1-result.png` 属并行片 AQ2 的改动，非本片）。
