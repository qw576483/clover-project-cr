# 战斗 HUD 素材索引（圣水条 / 手牌槽 / 冠数 / 倒计时 / 暂停按钮）

> 本片（AA2）**只出索引 + 落素材**，⛔ 未改任何代码（`client/Assets/Scripts/**` 一个字未动，含 `Core/ResPaths.cs`）。
> 权威来源 = **Clash Royale 2.1.5 APK 的 `.sc`**（`assets/sc/ui`，QuickBMS 解密后的明文）；
> 与上一片 W1 的版本指纹一致（本地 `原版资源/cr-assets-png/assets/sc/*_out` = 2.1.5 解包产物）。
> 抓取入口（GitHub REST，实测 200）：`https://api.github.com/repos/smlbiobot/cr/contents/apk/2.1.5/com.supercell.clashroyale-2.1.5/assets/sc`
> 持久副本 = `原版资源/sc/ui_v215.sc`（本片**未新增**任何 `.sc` 文件，见 §5.1）。

---

## 1. 结论（先行）

**HUD 层来源：找到 —— 就在 `ui.sc` 里，不在任何独立 HUD `.sc` 文件里。**

- `smlbiobot/cr` 的 `apk/2.1.5/.../assets/sc/` **完整清单里没有任何 `hud` / `ingame` / `battle` / `card` / `elixir` / `gameplay` 独立文件**
  （只有 `ui` / `ui_battle_end` / `ui_chest` / `ui_chest_3d` / `ui_spells` / `ui_arena` / `loading` / `debug` / `tutorial` /
  `arena_training` / `spell_goblin_barrel` / `effects` / 角色 / 建筑 / 各竞技场 `level_*`，共 28 个 `.sc`，见 §5.1）。
- ⇒ 上一片「圣水条属 HUD 层，这批 `.sc` 不含」的判断**需要修正**：HUD **在** `ui.sc` 里，
  但它不是以**顶层 export** 出现，而是以 **`HUD_*` export 内部的「具名子元件」** 出现 ——
  `.sc` 的 `0c`(animation) 记录里**自带每个子元件的名字串**（§2）。
  上一片只核对了「export 名 → clip → shapeID」，没有下钻到「clip 内部子元件名」，所以把 HUD 判成了不存在。

**5 个用途逐项结论**

| # | 用途 | 结论 | 权威位置（`.sc` → export → clip → 子元件） | 帧（`ui_out`） |
|---|---|---|---|---|
| 1 | **圣水条（槽/框/填充）** | **找到** | `ui.sc` → `HUD_player`(clip 1091) → 子元件 **`elixir_bar`**(clip 1080) | `bar_bg`=**155**、`bar_body`/`ghost`=**157**、`bar_end`=**158**、`elixirBarLeft`=**159**(+155/157)、`d1..d9`=**160**、`elixirRequirementBar`=**161/162**、`bar_tip`/`bar_flow`=**153** |
| 2 | **手牌槽** | **找到** | `ui.sc` → `HUD_player`(1091) → 子元件 **`slots`**(clip 1070) | **frame_200**（原版自身重复 4 次）；Draft 变体 `card_slots`(1099→1098) = **frame_201**（重复 8 次） |
| 3 | **冠数** | **找到** | `ui.sc` → **`printScore_player`**(1026) / **`printScore_enemy`**(1031) → 子元件 `star1/star2/star3`(1024/1029)；侧栏 = `HUD_rightMiddle`(1003) → `starPlayer`(989)/`starEnemy`(992) | **187**（蓝底金冠）/ **188**（红底金冠）/ **197**/**198**（另一态）/ 名条 **196** |
| 4 | **倒计时 / 阶段** | **部分找到**（图形只有 2 个，其余全是文本域 + 空容器） | `ui.sc` → `HUD_topRight`(1017) 子元件 `timeLeft`(1007)/`TID_TIME_LEFT`(1011) + 无名底板(1005)；**`Clock_middle`**(184) = 表盘；`print_endTimer`(1068) = 结束倒计时 | **42**（表盘 15×15）/ **193**（计时板 212×124）/ **159**（圣水回复提示） |
| 5 | **暂停按钮** | **找到（但属「回放 HUD」）** | `ui.sc` → **`replay_HUD_left`**(962) / `replay_HUD_left_landscape`(1134) → 子元件 **`play_pause_button`**(clip 948) | 底板 **163**、播放 **170**（▶）、暂停 **171**（‖）；同族 `quit_button`(924) = 163 + **164**（✕）、`speed_button`(954) = 163 + 172..176 |

⇒ **5 个用途没有一项是 `源内无对应`。** 但有两条必须如实标出的**子项缺口**：

- **「战斗内」暂停按钮没有独立命名元件**：`ui.sc` 里唯一带 `pause` 的命名是 `play_pause_button`，
  它只被 `replay_HUD_left*` 引用。**战斗内**（非回放）的暂停/投降按钮在 `HUD_*` 里没有对应命名
  ⇒ 若要做战斗内暂停，最接近的候选 = `play_pause_button`（frame_163/170/171）。**这是推断，不是原版命名。**
- **倒计时/阶段没有独立图元**：`HUD_topRight` / `HUD_topRight_overtime`(1015) / `HUD_topRight_double_elixir`(1016) /
  `print_endTimer`(1068) 内的 `TID_*` **全是文本域（无 shape）**；阶段切换靠同一容器换贴图 + 文本。
  图形部分只有 `Clock_middle`(frame_042) 与底板 `frame_193`。

### 1.1 与现工程 `ResPaths.cs` 的关系（**建议列，本片不改**）

| 现键 | 现用帧 | 实际是什么（本片证据） | 建议（下一片） |
|---|---|---|---|
| `ElixirBarTrack` | `ui_out 516` | 135×28 深色圆角条；**不在** `HUD_player`/`elixir_bar` 引用闭包内，且**不被任何 `0c` 动画引用** | 换成 **`Bars/ui_out/frame_155`**（原版 `bar_bg`） |
| `ElixirBarFrame` | `ui_out 517` | 212×54 深色圆角框；被 `spell_card_full` / `win_reward_bar` / `UI_menu_arena` 等引用 ⇒ 是**卡牌/奖励条**件 | 换成 **`Bars/ui_out/frame_158`**（原版 `bar_end`） |
| `ElixirBarFill` | `ui_out 518` | 205×45 品红条；同 517（`popup_login_conflict` / `Menu_topLayer` 等） | 换成 **`Bars/ui_out/frame_157`**（原版 `bar_body`/`ghost`） |
| （无键）手牌槽 | — | — | 新增 → **`Slots/ui_out/frame_200`**（玩家侧）/ `frame_201`（Draft） |
| `IconCrownBlueGem` / `IconCrownRedGem` | `ui_out 187` / `188` | 原版命名 = **`starPlayer` / `starEnemy`**（`HUD_rightMiddle`）与 **`star1..star3`**（`printScore_*`） | **帧号不动**（帧已对，现在只是有了权威命名）；键名可考虑改 `CrownPlayer`/`CrownEnemy` |
| （无键）倒计时 | — | — | 新增 → **`Icons/ui_out/frame_042`**（`Clock_middle`）、`Panels/ui_out/frame_193` |
| （无键）暂停按钮 | — | — | 新增 → **`Buttons/ui_out/frame_163`**（底板）+ `Icons/ui_out/frame_170`(▶) + `Icons/ui_out/frame_171`(‖) |

---

## 2. 方法链（每一环都是 `.sc` 里的**显式数据**，不是看图猜的）

```
frame_NNN.png == <dir>_sprite_NNN.png == .sc 里第 NNN 条 `12`(shape) 记录       ← 上一片已证（12/12 个 UI .sc 成立）
`0c`(animation) 记录 = ClipID + FPS + timeline帧数 + **子元件表**：
      · cnt1 条 (u16,u16,u16) 三元组    （语义未解出，见 §2.1）
      · cnt2 条 { sids[i] , opacity , **name[i]** }   ← ★ 本片的关键：**子元件名就在 name[i] 里**
Export 表 = 显式 (名字, ClipID) 数组
⇒  `HUD_player` → clip 1091 → 子元件表 =
      [(899,'darken'), (1076,'panel'), (1070,'slots'), (1080,'elixir_bar'), (920,'elixir_warning'),
       (799,'cardArea'), (803,'nextCard1'), (1081,'TID_NEXT_CARD'), (805,'NextSpellTimer')]
⇒  `elixir_bar` → clip 1080 → 子元件表 =
      [(902,'bar_bg'), (627,None), (905,'ghost'), (906,'bar_end'), (904,'bar_body'), (1077,'elixirBarLeft'),
       (909,'d1')…(909,'d9'), (912,'elixirRequirementBar'), (1079,'elixirBarLeftNumbers'),
       (915,'bar_tip'), (916,'bar_flow')]
⇒  clip → 子 shape id → `12` 记录序号 → `frame_NNN.png`
```

**复现命令**：`python .ai-tmp/test/aa2-resolve.py ui:1091 ui:1080 ui:1070 ui:1003 ui:1017 ui:1026 ui:1031 ui:948 ui:1100`
（解析器复用 `tools/probes/sc-anim-index.py`；探针为 `.ai-tmp/test/` 下的一次性脚本。）

### 2.1 已知局限（**如实登记，不许当成已解出**）

1. `0c` 的 **cnt1 三元组**语义**未解出**（沿用 W1 §1.1 的结论，无参考实现可依）。
   本片结论**基于 cnt2 子元件表**。当 `cnt1 ≠ cnt2` 时（例：`HUD_topRight` cnt1=59、cnt2=4），
   **不保证**该容器内的全部子元件都被列出 ⇒ 冠数/计时的**完整子图元清单可能有遗漏**；
   但**已列出的每一条都是原版显式数据**，不是推测。
2. **坐标 / 缩放未解出**（同 W1）：所以「`frame_155` 是 1×74 的 1 像素宽竖条」是**原版画法**（靠放置矩阵拉伸），
   不是裁错。落地的是**原版像素**；摆放尺寸要等坐标解出或按原版截图量。
3. 落地帧的裁剪矩形取自 `.ai-tmp/screenshots/ui-index-manifest.tsv`（alpha>8 扫描，**与上一片同一份**），非本片自创。

---

## 3. 逐项明细（含 clip id / 子元件名 / 帧号，均可复跑核对）

### 3.1 圣水条

| 原版元件名 | clip | 帧（`ui_out`） | 尺寸(bbox) | 说明 |
|---|---|---|---|---|
| `elixir_bar`（容器） | 1080 | — | — | `HUD_player` 的第 4 个具名子元件 |
| `bar_bg` | 902 | **155** | 1×74 | 槽底（1 像素宽，靠矩阵横向拉伸） |
| （未命名，同族件） | — | **156** | 15×75 | `elixir_bar` 的裸子件 |
| `bar_body` / `ghost` | 904 / 905 | **157** | 59×1 | 填充（品红细线，纵向拉伸） |
| `bar_end` | 906 | **158** | 10×59 | 条端 |
| `elixirBarLeft` | 1077 | 155 / 157 / **159** | 97×115 | 左侧圣水图标 = frame_159 |
| `d1`…`d9`（9 个） | 909 | **160** | 1×1 | 10 格刻度分隔（原版同一 clip 复用 9 次） |
| `elixirRequirementBar`（11 帧） | 912 | **161 / 162** | 71×1 / 14×71 | 需求条 |
| `elixirBarLeftNumbers` | 1079 | — | — | `elixirAmount` + `TID_MAX_ELIXIR`：**纯文本域** |
| `bar_tip`（25 帧）/ `bar_flow`（30 帧） | 915 / 916 | **153** | 65×44 | 黑→透明渐变（与 `darken` 同帧） |
| `elixir_warning`（30 帧） | 920 | — | — | 圣水满提示 = 文本 `TID_MANA_BAR_FULL_NOTIFICATION_vcenter` |

### 3.2 手牌槽

| 原版元件名 | clip | 帧 | 尺寸 | 说明 |
|---|---|---|---|---|
| `slots` | 1070 | **200** | 106×145 | 4 个槽底（原版自己把同一 shape 放了 4 次）⇒ **一张图 × 4** |
| `card_slots`（Draft） | 1099 → 1098 | **201** | 124×151 | Draft 卡槽底（放 8 次） |
| `cardArea` / `nextCard1` | 918 / 805 | — | — | 文本域占位（`HUD_player` 直接子项） |
| `TID_NEXT_CARD` | 1081 | — | — | 文本域 |
| `NextSpellTimer` | — | — | — | 文本域 |
| `Draft_HUD_card_selector` | 1105 | — | — | Draft 卡选择器（0 shape） |

### 3.3 冠数

| 原版元件名 | clip | 帧 | 尺寸 | 说明 |
|---|---|---|---|---|
| `star1`/`star2`/`star3`（`printScore_player`） | 1024（66 帧） | **187** / **197** | 120×98 | 蓝底金冠：两种状态 |
| `star1`/`star2`/`star3`（`printScore_enemy`） | 1029（66 帧） | **188** / **198** | 120×98 | 红底金冠：两种状态 |
| `starPlayer` / `starEnemy`（`HUD_rightMiddle`） | 989 / 992（各 100 帧） | **187** / **188** | 120×98 | 侧栏（2v2/观战）计数 |
| 玩家名条（`printScore_*` 的裸子件） | — | **196** | 247×56 | 白色名条 |
| `scorePlayer` / `scoreEnemy` | 994 / 993 | — | — | 文本域 |
| `playerName` / `enemyName` | 1025 / 1030 | — | — | 文本域 |

> ⇒ 与上一片已落地的 `Icons/ui_out/frame_187.png`、`frame_188.png` **完全一致**；
> 本片的新信息 = **原版命名**（`starPlayer`/`starEnemy`/`star1..3`）以及 `197 / 198 / 196` 这三个同族帧。

### 3.4 倒计时 / 阶段

| 原版元件名 | clip | 帧 | 说明 |
|---|---|---|---|
| `Clock_middle` | 184 | **42**（15×15，fps=60） | 表盘 / 时钟图标 |
| `HUD_topRight`（15 帧）的无名子件 | 1005 | **177**(1×1) / **193**(212×124) | 顶部计时/计数底板 |
| `timeLeft` / `TID_TIME_LEFT` | 1007 / 1011 | — | 文本域 |
| `elixirRegen`（40 帧） | 1014 | **159** + 文本 | 圣水回复提示图标 = 圣水水滴 |
| `print_endTimer`（357 帧） | 1068 | — | `TID_BATTLE_ENDS_IN` + `TID_10`…`TID_1`，**全是文本域** |
| `HUD_topRight_overtime` / `HUD_topRight_double_elixir`（各 15 帧） | 1015 / 1016 | — | **纯容器（0 shape）**，阶段态靠换贴图 |
| `HUD_topRight_replay*`（各 15 帧） | 1012 / 1009 / 1010 | — | 回放态容器 |
| `HUD_topLeft`（名牌）/ `HUD_rightMiddle` | 983 / 1003 | 182 / 184 / 185 / 186 | 顶部左侧名牌（101×31）+ 徽章（133×158）+ 右侧细条（13×72 / 17×74） |

### 3.5 暂停按钮

| 原版元件名 | clip | 帧 | 说明 |
|---|---|---|---|
| `play_pause_button`（由 `replay_HUD_left` / `replay_HUD_left_landscape` 引用） | 948（2 帧） | 底板 **163**(219×219) / 播放 **170**(80×87 ▶) / 暂停 **171**(84×90 ‖) | **唯一带 `pause` 命名的原版元件** |
| `quit_button` | 924 | 163 + **164**(80×81 ✕) | 同族退出按钮 |
| `speed_button`（4 帧） | 954 | 163 + 172–176 | 同族倍速按钮 |
| `time_strip` / `forward_button` / `back_button` | 956 / 959 / 960 | 153 / 178–179 | 回放时间轴 |

---

## 4. 落地（本片实际复制进工程的帧）

规则：**只复制确认要用的那几个**（⛔ 不整目录搬）；源 = `原版资源/cr-assets-png/assets/sc/ui_out/ui_sprite_NNN.png`；
裁剪矩形 = `ui-index-manifest.tsv` 的 bbox；目标 = `client/Assets/Resources/Sprites/Ui/<用途>/ui_out/frame_NNN.png`；
`.meta` **照抄同目录既有 `.meta`**（只改 `guid` / sprite 名 / `rect` 宽高 / `spriteID` / `internalID` / `nameFileIdTable`），
使导入设置与既有落地帧一致、且 rect 与新尺寸匹配（旧口径「删 meta 让 Unity 重切」会让设置与既有帧不一致，故本轮照抄）。

| 用途目录 | 帧 | 原版元件名 | 尺寸(px) |
|---|---|---|---|
| `Bars/ui_out` | frame_155 | `elixir_bar/bar_bg` | 1×74 |
| `Bars/ui_out` | frame_156 | `elixir_bar` 同族件 | 15×75 |
| `Bars/ui_out` | frame_157 | `elixir_bar/bar_body` + `ghost` | 59×1 |
| `Bars/ui_out` | frame_158 | `elixir_bar/bar_end` | 10×59 |
| `Bars/ui_out` | frame_160 | `elixir_bar/d1..d9` | 1×1 |
| `Bars/ui_out` | frame_161 | `elixirRequirementBar` 底 | 69×1 |
| `Bars/ui_out` | frame_162 | `elixirRequirementBar` 端 | 14×69 |
| `Slots/ui_out` | frame_200 | `slots`（手牌槽底） | 96×137 |
| `Slots/ui_out` | frame_201 | `card_slots`（Draft） | 124×151 |
| `Icons/ui_out` | frame_159 | `elixirBarLeft` / `elixirRegen` | 94×115 |
| `Icons/ui_out` | frame_042 | `Clock_middle` | 15×15 |
| `Icons/ui_out` | frame_170 | `play_pause_button` ▶ | 79×87 |
| `Icons/ui_out` | frame_171 | `play_pause_button` ‖ | 84×90 |
| `Icons/ui_out` | frame_164 | `quit_button` ✕ | 80×81 |
| `Panels/ui_out` | frame_193 | `HUD_topRight` 底板 | 212×124 |
| `Panels/ui_out` | frame_196 | `printScore_*` 玩家名条 | 247×56 |
| `Buttons/ui_out` | frame_163 | 按钮底板 | 219×219 |

**共 17 个新帧**（`Test-Path` 全 True，见 §6）。冠数用途**不需要**新帧（187/188 上一片已落地）。

> ⚠️ **登记（不是失败）**：本轮**不改** `Core/ResPaths.cs`，所以这 17 帧暂时**没有任何键引用**，
> `tools/probes/check-ui-keys.ps1` 的 B 项（landed but unreferenced）计数会 **67 → 84**。
> 该脚本在本片**开始前就已经是 `FAIL=2`**（基线见 `.ai-tmp/test/AA2-check-ui-keys-before.txt`：
> 既有 `Bars/loading_out/frame_015.png`、`Icons/loading_out/frame_028.png` 两个未登记文件 + C 项键名与更正表不一致）。
> ⇒ 下一片把 §1.1 的建议列落进 `ResPaths.cs`（并把新帧登记进 `tools/probes/copy-ui-assets.py`）后，B/C 才会真正转绿。

---

## 5. 穷尽记录

### 5.1 已核对的源清单（`smlbiobot/cr` 的 `res/sc` / `assets/sc` 全量文件名）

- **`apk/1.0.0/.../Clash Royale.app/res/sc` = 145 个文件**（iOS 包，`.sc`/`.hash` 带扩展名）：
  `arena_training(+_tex)`、`building_*`（barbarian_hut / barracks / basic_cannon / bomb_tower / elixir_collector /
  general_tower / goblin_hut / inferno_tower / mega_bomb / mortar / tesla / tombstone / tower / xbow，各 `+_tex`）、
  `characters(+_tex)`、`chr_*`（archer / baby_dragon / balloon / baloon / barbarian / bomber / dragon / giant /
  giant_skeleton / goblin / goblin_archer / golem / hog_rider / knight / mini_pekka / minion / musketeer /
  neutral_wizard / pekka / prince / princess_twins / royal_king / skeleton / valkyrie / witch / wizard，各 `+_tex`）、
  `debug(+_tex)`、`effects(+_tex)`、`level(+_tex)`、`level_{barbarian,bone,dark,goblin,royal,spell}_arena(+_tex)`、
  `loading(+_tex)`、`spell_goblin_barrel(+_tex)`、`tutorial(+_tex)`、`ui(+_tex)`、`ui_arena(+_tex)`、`ui_chest(+_tex)`、
  **`ui_panels(+_tex)`**、`ui_spells(+_tex)`、`ui_stickers(+_tex)`
  —— **无 hud / ingame / battle / card / elixir 文件**（清单另存 `.ai-tmp/test/aa2-sc-dirs.txt`）。
- **`apk/2.1.5/com.supercell.clashroyale-2.1.5/assets/sc`** = 唯一有权威版本指纹的目录（与本地 `*_out` 100% 吻合，
  见 W1 §0）；完整 28 个 `.sc` 名：`ui, ui_arena, ui_battle_end, ui_chest, ui_chest_3d, ui_spells, ui_spell_ghost,
  ui_spell_hunter, ui_spell_zappies, loading, debug, tutorial, arena_training, spell_goblin_barrel, effects,
  level_decos, level_*_arena, building_*, chr_*` —— **无 HUD 独立文件**。
- 其余版本（1.7.0 / 1.8.0 / 1.8.1 / 1.8.2 / 1.8.2-20170504 / 1.9.0 / 2.0.1 / 2.2.1）的 `assets/sc` **文件集合与 2.1.5 同构**
  （差异只在角色 / 建筑 / 竞技场数量），**同样没有 HUD 独立文件**。
- **全仓 45549 条路径按 `hud|ingame|gameplay` 正则扫**（`.ai-tmp/test/aa2-tree-scan.py`）：**0 命中**
  （`git/trees?recursive=1` 返回 `truncated=true`，1.8.2 之后的目录不在该树里，已用逐版本 contents API 补齐）。

### 5.2 关键词轮次（≥4 组，中英各半）

| # | 关键词 / 查询（语言） | 站点类别 | 拿到什么 / 结论 |
|---|---|---|---|
| 1 | GitHub REST `contents/apk/2.1.5/.../assets/sc`（EN） | GitHub REST API | ✔ 200，2.1.5 的**完整 `.sc` 文件清单** ⇒ 无独立 HUD `.sc` |
| 2 | GitHub REST `git/trees/master?recursive=1`（EN） | GitHub REST API | ✔ 200，全仓 45549 条路径；抽出**所有** `res/sc`、`assets/sc` 目录（8 个版本） |
| 3 | GitHub 网页目录树 `github.com/smlbiobot/cr/tree/master/apk/2.1.5/.../assets/sc`（EN） | GitHub 仓库树页（HTML） | ✘ `000`（20s 超时）⇒ 改用 REST（第 1 条）；**是换了入口，不是没找** |
| 4 | 全仓路径正则 `hud|ingame|gameplay`（中英混） | 本地树扫描 | ✔ 0 命中 ⇒ HUD 无独立文件；`ui_battle_end` 是**结算**界面不是战斗 HUD |
| 5 | 12 个 `.sc` 内 `shape_names` 扫描：`crown｜pause｜timer｜elixir｜slot｜clock｜score｜star｜settings｜surrender`（EN） | `.sc` 二进制解析（本项目解析器） | ✔✔ **命中**：`elixir_bar` / `slots` / `starPlayer` / `play_pause_button` / `Clock_middle` … ⇒ **本片全部结论出自这一轮** |
| 6 | `皇室战争 圣水条 手牌 素材`（CN）、`clash royale ui hud atlas plist`（EN） | 通用搜索引擎 / 素材站 | ⚠️ 本机**无 web 搜索工具**（宿主只给本地检索 + curl）；`duckduckgo.com/html` 返回 `000` ⇒ **如实登记为「未执行」**（不是「搜过无结果」） |

### 5.3 站点类别（≥3 类）

| 类别 | 具体入口 | 结果 |
|---|---|---|
| GitHub REST API | `api.github.com/repos/smlbiobot/cr/...`（contents / git-trees） | ✔ 200（本轮**未**遇到限流） |
| GitHub 网页（仓库树 / code search） | `github.com/smlbiobot/cr/tree/...`、`github.com/search?...&type=code` | ✘ 树页 `000`（20s 超时）；code search `200` 但**要求登录**（页面含 "Sign in to search"）⇒ 无结果 |
| APK 站 #1 | `apkpure.net/clash-royale/com.supercell.clashroyale` | ✘ **403**（试一次即换站点，与预期一致） |
| APK 站 #2 | `apkmirror.com/?s=clash+royale` | ✘ `000`（连接被阻断） |
| Wiki / Fandom | `clashroyale.fandom.com/wiki/Special:Search?query=elixir+bar` | ✘ `000`（连接被阻断） |
| 开源素材站 | `opengameart.org/art-search?keys=clash+royale+hud` | ⚠️ `200`（页面可达，26 条结果行），但**无 CR HUD / 圣水条相关条目** |
| 本地已解包素材树 | `原版资源/cr-assets-png/assets/sc/*_out`、`原版资源/sc/*.sc`、`cr-sim`、`cr-api-data`、`cr-sfx` | ✔ 帧来源 + `.sc` 明文；`cr-sim` 只有数值/地形，`cr-api-data` 只有卡牌数值，**均无 UI 索引** |

### 5.4 尝试过的格式 / 打包方式（≥3 种）

| 格式 | 结果 |
|---|---|
| `.sc`（Android，QuickBMS 解密后的裸文件，**无扩展名**） | ✔✔ **本片权威来源**（2.1.5） |
| `.sc`（iOS ipa 内 `res/sc/*.sc`，`SC`+LZMA 壳） | ✔ 能解（1.0.0）；**版本对不上**，只作格式/清单参照 |
| `_tex.sc` / `*_tex.png` 图集 | ✔ 用作**版本指纹**（`.sc` 声明尺寸 vs PNG 尺寸） |
| PNG dump（`*_out/*_sprite_NNN.png`，1663×2810 画布 + 小图元） | ✔ 帧像素来源；**无任何语义命名**（这就是"猜"的根源） |
| `.hash` | ✘ 只有哈希串，不含名称 |
| plist / json sprite atlas 索引 | ✘ 全仓 + 本地素材树均无 |
| APK / IPA 本体 | ✘ 未下载（几十~上百 MB；`.sc` 已在仓库里，没必要） |

### 5.5 结论：有没有比 `.sc` 更「成套」的 HUD 来源？

- **游戏内 HUD 图元：没有。** `.sc` 的 Export 表 + `0c` 子元件名**就是原版自己的命名**，已是天花板。
- 有语义命名的第三方素材只覆盖少数题材（`fan_kit/ui/` 的 13 个统计图标 + `crowns.png`、`ui/ui/chest-*.png`、
  `ui/leagues/league-*.png`，见上一片 §4.4），**替代不了** HUD 的条 / 槽 / 按钮 / 计时板。
- ⇒ 本片结论：**针对战斗 HUD，已穷尽，无更优来源**；且**不必再用兜底素材**（原版素材已拿到）。

---

## 6. 机械自检（本片跑过的命令与结果）

| 检查 | 命令 | 结果 |
|---|---|---|
| 落地文件存在 | `Test-Path client/Assets/Resources/Sprites/Ui/{Bars,Slots,Icons,Panels,Buttons}/ui_out/frame_NNN.png`（17 项） | **17/17 True**（含 `.meta` 也 17/17 True） |
| 落地像素 = 原版像素 | `aa2-land.py` 写盘后回读比对 `crop.tobytes()` + `size` | **mismatch = 0** |
| 落地键账（b） | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/check-ui-keys.ps1` | `FAIL=2 keys=65 landed=84`（**基线 67 → 84**，FAIL 数与片前相同；原因见 §4 登记） |
| 索引文档存在 | `Test-Path 策划/战斗HUD素材索引.md` | True |
| 临时产物位置 | `Get-ChildItem client/_dev, tools, Assets -Filter *AA2*` | 0 命中（全部在 `.ai-tmp/test/`、`.ai-tmp/screenshots/`） |
| 代码未改动 | `git status --porcelain client/Assets/Scripts` | 空 |

---

## 7. 本片**没有**做的事（不要当成已完成）

1. **没有改** `client/**` 任何代码（含 `Core/ResPaths.cs`）——键的改动 / 新键登记留给下一片。
2. **没有解出坐标 / 缩放**（`0c` 的 cnt1 三元组语义未解出）⇒ HUD 各元件的**摆放位置与尺寸**仍无权威依据。
3. **没有做「表现类」对照**（不调 unity、不进 Play）⇒ 索引里的尺寸都是 bbox，不是"在屏幕上多大"。
4. **没有登记新键**到 `tools/probes/copy-ui-assets.py` ⇒ 该脚本与本片新增的 17 帧尚未对齐（下一片一并做）。
