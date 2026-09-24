# 原版 UI 布局坐标（从 `.sc` 的放置矩阵解出）

> **本文件是生成物，⛔ 不要手改。** 生成器 = `tools/probes/sc-layout.py`
> 复跑：`python tools/probes/sc-layout.py`（读 `原版资源/sc/<name>_v215.sc`；口径同 `ui-sc-index.py`）。
> 权威来源 = **Clash Royale 2.1.5** APK `assets/sc/<name>`（QuickBMS 解密后的明文）。

## 0. 结论先行

**坐标解出来了**（上一片登记为"未解出"的那一环，本片补上）。

| 问题 | 结论 |
|---|---|
| `0c`(MovieClip) 的三元组是什么 | **`(childIdx, matrixIdx, colorIdx)`** = 子元件下标 / 矩阵下标 / 颜色变换下标 |
| 怎么知道首元素是"子元件下标" | `HUD_player` cnt1=cnt2=9，首元素恰为 0..8 连续；cnt2 侧给出 name `darken/panel/slots/elixir_bar/…` |
| 矩阵（tag `08`）怎么编码 | 恒 24B = 6×int32；**a,b,c,d = 定点 1/1024（1024=1.0）**，**tx,ty = 平移（÷20）** |
| `65535` 是什么意思 | matrixIdx/colorIdx 位上的 `0xFFFF` = **无**（按恒等/无变换处理） |
| 单位与原点 | 线性 1/1024、平移 ÷20（twips）；原点是**所在 clip 的局部原点**（root clip 的设计空间原点在中心、y 向下） |
| ⛔ 没解出的 | **元件在 1080×1920 屏幕上的绝对像素** —— 见 §4（`.sc` 不含该信息，需引擎侧布局代码） |

## 1. 出处（格式依据，三条独立来源）

| # | 来源 | 给出什么 | 与本片的对应 |
|---|---|---|---|
| 1 | `Galaxy1036/sc_decode`（本地副本 `.ai-tmp/test/scdfull/sc_decode.py`，上游 github.com/GaLaXy1036/sc_decode）`process()` 第 359-393 行 | tag `08`=读 6×int32；tag `0c`= id/fps/frames/cnt1/**三元组(saTag12Nr,saTag08Nr,saTag09Nr)**/cnt2/sids/opacity/names | `0c` 逐字节结构（本片实测 12/12 clip `consumed` 与 payload 自洽） |
| 2 | `mirsella/clash-royale` `scripts/modern_sc2.py`（本地副本 `.ai-tmp/web/modern_sc2_saved.txt`）`precision_multiplier()` + `parse_matrix_banks()` | `precision==2 → 20.0`、`precision==3 → 1024.0`；**scale 与 translation 用两套精度** | a,b,c,d ÷1024；tx,ty ÷20 |
| 3 | `sc-workshop/SupercellSWF-Animate` | 确认 SC2 体系里 MovieClip 有 **matrix bank + frame elements(instance,matrix,color)** 三元组 | 与 #1 互证三元组语义 |

> 出处受限说明：本机 `api.github.com` 返回 **403（限流）**，未能在本轮现拉新源码；
> 上表 3 份均为**上一片已下载到本工程的本地副本**（路径已给）。**未在联网新源上复核**，如实登记。

## 2. 本片解析的两个 `.sc` 总览

| 源 `.sc` | ShapeCount | MatrixCount(=tag08 条数) | ColorTrans | shape 记录 | clip(0c) | export 名 |
|---|---|---|---|---|---|---|
| `ui` | 914 | 36843 | 3906 | 914 | 2086 | 1016 |
| `ui_battle_end` | 240 | 2069 | 615 | 240 | 96 | 20 |

## 3. 抽出的界面坐标表（每个数字都可复跑）

> 口径：**x/y = clip 局部坐标**（`tx/20`, `ty/20`；单位≈设计空间像素）；
> `scale` = `a/1024 × d/1024`（未列 b/c 者必为 0）；`frame` 列 = 该 objectId 对应 `frame_NNN`（仅 shape 有）。
> 原点：同一 clip 内**所有元件共享同一个局部原点** ⇒ 元件之间的**相对位置/尺寸**是硬结论。

### ui ｜ `HUD_player` ｜ clip=1091 ｜ fps=30 ｜ frames=9 ｜ 元件 9 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `darken` | clip | — | -2.75 | -92.9 | 21.122 | 4.814 | 0/9 | 89 | `ui_v215.sc` tag08 #2447 |
| 1 | `panel` | clip | — | 55 | 21 | 1 | 1 | 1/9 | — | `ui_v215.sc` tag08 #2449 |
| 2 | `slots` | clip | — | 54.55 | -114 | 1 | 1 | 2/9 | — | `ui_v215.sc` tag08 #2408 |
| 3 | `elixir_bar` | clip | — | -132 | -27.45 | 1 | 1 | 3/9 | — | `ui_v215.sc` tag08 #2450 |
| 4 | `elixir_warning` | clip | — | 87 | -25.9 | 1 | 1 | 4/9 | — | `ui_v215.sc` tag08 #2451 |
| 5 | `cardArea` | ? | — | -161 | -186.55 | 1 | 1 | 5/9 | — | `ui_v215.sc` tag08 #2409 |
| 6 | `nextCard1` | ? | — | -286.5 | -112.15 | 1 | 1 | 6/9 | — | `ui_v215.sc` tag08 #2452 |
| 7 | `TID_NEXT_CARD` | clip | — | -216.3 | -98.2 | 1 | 1 | 7/9 | — | `ui_v215.sc` tag08 #2453 |
| 8 | `NextSpellTimer` | ? | — | -266 | -36 | 1 | 1 | 8/9 | — | `ui_v215.sc` tag08 #2454 |

### ui ｜ `HUD_topLeft` ｜ clip=983 ｜ fps=30 ｜ frames=6 ｜ 元件 6 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `darken` | clip | — | 169.75 | -24.75 | 0 | 8.197 | 0/6 | 230 | `ui_v215.sc` tag08 #1380 |
| 1 | `enemyTrophies` | clip | — | 19 | 66.95 | 1 | 1 | 1/6 | — | `ui_v215.sc` tag08 #1381 |
| 2 | `—（无名）` | shape | frame_182 | 81 | 30.8 | 0.952 | 1.547 | 2/6 | 78 | `ui_v215.sc` tag08 #1382 |
| 3 | `enemyClan` | clip | — | 50 | 37.95 | 1 | 1 | 3/6 | — | `ui_v215.sc` tag08 #1389 |
| 4 | `clan_icon` | clip | — | 27 | 33 | 0.6 | 0.6 | 4/6 | — | `ui_v215.sc` tag08 #1384 |
| 5 | `enemyName` | clip | — | 50 | 5.95 | 1 | 1 | 5/6 | — | `ui_v215.sc` tag08 #1390 |

### ui ｜ `HUD_topRight` ｜ clip=1017 ｜ fps=30 ｜ frames=59 ｜ 元件 4 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `—（无名）` | clip | — | -48 | 29 | 1 | 1 | 0/59 | — | `ui_v215.sc` tag08 #1786 |
| 1 | `timeLeft` | clip | — | -222.35 | 172.5 | 1 | 1 | 1/59 | — | `ui_v215.sc` tag08 #1789 |
| 2 | `TID_TIME_LEFT` | clip | — | -315.85 | 72.5 | 1 | 1 | 2/59 | — | `ui_v215.sc` tag08 #1788 |
| 3 | `elixirRegen` | clip | — | -46.75 | 104.15 | 1 | 1 | 3/59 | — | `ui_v215.sc` tag08 #1752 |

### ui ｜ `card_page_deck_special` ｜ clip=4418 ｜ fps=60 ｜ frames=20 ｜ 元件 20 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `spacing` | ? | — | -445.3 | -7.25 | 1 | 1 | 0/20 | — | `ui_v215.sc` tag08 #19658 |
| 1 | `panel` | clip | — | 0 | 156.8 | 1 | 1 | 1/20 | — | `ui_v215.sc` tag08 #19659 |
| 2 | `battle_cards_area` | ? | — | -280 | 200.4 | 1 | 1 | 2/20 | — | `ui_v215.sc` tag08 #19660 |
| 3 | `—（无名）` | shape | frame_373 | -0.2 | 602.35 | 34.29 | 1.229 | 3/20 | 2394 | `ui_v215.sc` tag08 #19661 |
| 4 | `—（无名）` | shape | frame_372 | -256.5 | 602.35 | 0.979 | 1.229 | 4/20 | 2394 | `ui_v215.sc` tag08 #19662 |
| 5 | `—（无名）` | shape | frame_374 | 256.5 | 602.35 | 0.979 | 1.229 | 5/20 | 2394 | `ui_v215.sc` tag08 #19663 |
| 6 | `—（无名）` | shape | frame_373 | -0.2 | 605.35 | 34.29 | 1.229 | 6/20 | 2395 | `ui_v215.sc` tag08 #19664 |
| 7 | `—（无名）` | shape | frame_372 | -256.5 | 605.35 | 0.979 | 1.229 | 7/20 | 2395 | `ui_v215.sc` tag08 #19665 |
| 8 | `—（无名）` | shape | frame_374 | 256.5 | 605.35 | 0.979 | 1.229 | 8/20 | 2395 | `ui_v215.sc` tag08 #19666 |
| 9 | `elixar_total_title` | ? | — | -115.25 | 595 | 1 | 1 | 9/20 | — | `ui_v215.sc` tag08 #19667 |
| 10 | `elixir_icon` | clip | — | 138.75 | 605.1 | 0.359 | 0.359 | 10/20 | 2396 | `ui_v215.sc` tag08 #19668 |
| 11 | `—（无名）` | shape | frame_270 | -0.5 | 129.5 | 33.158 | 0.3 | 11/20 | — | `ui_v215.sc` tag08 #19669 |
| 12 | `—（无名）` | shape | frame_445 | -0.5 | 120 | 1 | 1 | 12/20 | — | `ui_v215.sc` tag08 #19670 |
| 13 | `—（无名）` | shape | frame_272 | -233.8 | 95.4 | 1.121 | 0.811 | 13/20 | 82 | `ui_v215.sc` tag08 #19671 |
| 14 | `—（无名）` | shape | frame_272 | 235.05 | 95.2 | -1.122 | 0.811 | 14/20 | 82 | `ui_v215.sc` tag08 #19672 |
| 15 | `small_title` | ? | — | -210 | 48 | 1 | 1 | 15/20 | — | `ui_v215.sc` tag08 #19673 |
| 16 | `title` | ? | — | -269.35 | 78 | 1 | 1 | 16/20 | — | `ui_v215.sc` tag08 #19674 |
| 17 | `info_button` | clip | — | 231.5 | 93 | 1 | 1 | 17/20 | — | `ui_v215.sc` tag08 #19675 |
| 18 | `—（无名）` | ? | — | -329 | 667 | 1 | 1 | 18/20 | — | `ui_v215.sc` tag08 #19676 |
| 19 | `random_button` | clip | — | 246.15 | 604.3 | 1 | 1 | 19/20 | — | `ui_v215.sc` tag08 #19677 |

### ui ｜ `card_page_collection` ｜ clip=4423 ｜ fps=60 ｜ frames=4 ｜ 元件 4 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `collection_cards_area` | ? | — | -277 | 101.35 | 1 | 1 | 0/4 | — | `ui_v215.sc` tag08 #19689 |
| 1 | `header` | clip | — | -0.75 | 8 | 1 | 1 | 1/4 | — | `ui_v215.sc` tag08 #19690 |
| 2 | `TID_SELECT_SPELL_TO_BE_REPLACED_HEADER` | ? | — | -267 | -11 | 1 | 1 | 2/4 | — | `ui_v215.sc` tag08 #19691 |
| 3 | `spacing` | ? | — | -280 | 0.4 | 1 | 1 | 3/4 | — | `ui_v215.sc` tag08 #19692 |

### ui ｜ `popup_card_info` ｜ clip=4437 ｜ fps=60 ｜ frames=31 ｜ 元件 31 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `—（无名）` | shape | frame_013 | 0 | 2.65 | -10.078 | 12.739 | 0/31 | — | `ui_v215.sc` tag08 #19790 |
| 1 | `—（无名）` | shape | frame_014 | -240 | -304.75 | 1 | 1 | 1/31 | — | `ui_v215.sc` tag08 #19757 |
| 2 | `—（无名）` | shape | frame_015 | -240 | 320.25 | 1 | 1 | 2/31 | — | `ui_v215.sc` tag08 #19791 |
| 3 | `—（无名）` | shape | frame_014 | 240 | -304.75 | -1 | 1 | 3/31 | — | `ui_v215.sc` tag08 #19759 |
| 4 | `—（无名）` | shape | frame_015 | 240 | 320.25 | -1 | 1 | 4/31 | — | `ui_v215.sc` tag08 #19792 |
| 5 | `—（无名）` | shape | frame_366 | 0 | -304.75 | -7.936 | 1 | 5/31 | — | `ui_v215.sc` tag08 #19761 |
| 6 | `—（无名）` | shape | frame_017 | 0 | 320.25 | -7.936 | 1 | 6/31 | — | `ui_v215.sc` tag08 #19793 |
| 7 | `—（无名）` | shape | frame_018 | -240 | 2.65 | 1 | 12.739 | 7/31 | — | `ui_v215.sc` tag08 #19794 |
| 8 | `—（无名）` | shape | frame_018 | 240 | 2.65 | -1 | 12.739 | 8/31 | — | `ui_v215.sc` tag08 #19795 |
| 9 | `—（无名）` | shape | frame_019 | -264 | -283.75 | 1 | 1 | 9/31 | — | `ui_v215.sc` tag08 #19765 |
| 10 | `—（无名）` | shape | frame_019 | 264 | -283.75 | -1 | 1 | 10/31 | — | `ui_v215.sc` tag08 #19766 |
| 11 | `—（无名）` | shape | frame_019 | -264 | 344.25 | 1 | -1 | 11/31 | — | `ui_v215.sc` tag08 #19796 |
| 12 | `—（无名）` | shape | frame_019 | 264 | 344.25 | -1 | -1 | 12/31 | — | `ui_v215.sc` tag08 #19797 |
| 13 | `—（无名）` | shape | frame_020 | 0 | 348.25 | 52.85 | 1 | 13/31 | — | `ui_v215.sc` tag08 #19798 |
| 14 | `—（无名）` | shape | frame_020 | 0 | -287.75 | 52.85 | 1 | 14/31 | — | `ui_v215.sc` tag08 #19770 |
| 15 | `—（无名）` | shape | frame_020 | 0 | 31.35 | 54.604 | 63.198 | 15/31 | — | `ui_v215.sc` tag08 #19799 |
| 16 | `—（无名）` | shape | frame_322 | -273 | -291.05 | 18.199 | 1.138 | 16/31 | 115 | `ui_v215.sc` tag08 #19800 |
| 17 | `SpellName` | ? | — | -249.55 | -331 | 1 | 1 | 17/31 | — | `ui_v215.sc` tag08 #19801 |
| 18 | `description_text` | ? | — | -10.5 | -185.9 | 1 | 1 | 18/31 | — | `ui_v215.sc` tag08 #19802 |
| 19 | `stats_area` | ? | — | -262.95 | -31.55 | 1.29 | 0.719 | 19/31 | — | `ui_v215.sc` tag08 #19803 |
| 20 | `TID_MAX_LVL_REACHED` | ? | — | -246.5 | 281 | 1 | 1 | 20/31 | — | `ui_v215.sc` tag08 #19804 |
| 21 | `UpgradeButton` | clip | — | 0 | 304 | 1 | 1 | 21/31 | — | `ui_v215.sc` tag08 #19003 |
| 22 | `select` | clip | — | 160 | 304 | 1 | 1 | 22/31 | — | `ui_v215.sc` tag08 #19805 |
| 23 | `card_placement` | ? | — | -251.5 | -302.05 | 1 | 1 | 23/31 | — | `ui_v215.sc` tag08 #19806 |
| 24 | `card_attribute` | clip | — | 92.35 | -236.5 | 1.015 | 1.259 | 24/31 | — | `ui_v215.sc` tag08 #19807 |
| 25 | `TID_CARD_RARITY` | ? | — | -55 | -258 | 1 | 1 | 25/31 | — | `ui_v215.sc` tag08 #19808 |
| 26 | `TID_CARD_TYPE` | ? | — | 97.05 | -258 | 1 | 1 | 26/31 | — | `ui_v215.sc` tag08 #19809 |
| 27 | `SpellRarity` | ? | — | -49 | -233 | 1 | 1 | 27/31 | — | `ui_v215.sc` tag08 #19810 |
| 28 | `Spell` | ? | — | 98 | -233 | 1 | 1 | 28/31 | — | `ui_v215.sc` tag08 #19811 |
| 29 | `button_cancel` | clip | — | 255 | -320 | 1 | 1 | 29/31 | — | `ui_v215.sc` tag08 #19788 |
| 30 | `UpgradeExp` | clip | — | -234.6 | 280.1 | 1 | 1 | 30/31 | — | `ui_v215.sc` tag08 #19812 |

### ui ｜ `UI_menu_arena` ｜ clip=4514 ｜ fps=60 ｜ frames=19 ｜ 元件 19 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `background1` | clip | — | 0 | -50 | 1 | 1 | 0/19 | 2402 | `ui_v215.sc` tag08 #15984 |
| 1 | `background2` | clip | — | 0 | 0 | 1 | 1 | 1/19 | — | `ui_v215.sc` tag08 #65535 |
| 2 | `league_glow` | clip | — | 0 | 417.1 | 22.871 | 20.82 | 2/19 | 2596 | `ui_v215.sc` tag08 #20772 |
| 3 | `—（无名）` | shape | frame_277 | -364.35 | -0.95 | 1.41 | 1.305 | 3/19 | 2597 | `ui_v215.sc` tag08 #20773 |
| 4 | `matchmaking_shield_2v2` | clip | — | 0 | 415 | 1 | 1 | 4/19 | — | `ui_v215.sc` tag08 #20774 |
| 5 | `right` | clip | — | 384 | 444 | 1 | 1 | 5/19 | — | `ui_v215.sc` tag08 #20775 |
| 6 | `left` | clip | — | -384 | 444 | 1 | 1 | 6/19 | — | `ui_v215.sc` tag08 #20776 |
| 7 | `battle_2v2_buttons` | clip | — | 20 | 653 | 1 | 1 | 7/19 | — | `ui_v215.sc` tag08 #20777 |
| 8 | `arena` | clip | — | 0 | 408.95 | 1 | 1 | 8/19 | — | `ui_v215.sc` tag08 #20778 |
| 9 | `top_center` | clip | — | 0 | 0 | 1 | 1 | 9/19 | — | `ui_v215.sc` tag08 #65535 |
| 10 | `play_arena` | clip | — | 0 | 668 | 1 | 1 | 10/19 | — | `ui_v215.sc` tag08 #20779 |
| 11 | `arena_count` | ? | — | -635.25 | 551 | 1 | 1 | 11/19 | — | `ui_v215.sc` tag08 #20780 |
| 12 | `bottom_center` | clip | — | -4 | 1024.25 | 1 | 1 | 12/19 | — | `ui_v215.sc` tag08 #20781 |
| 13 | `tips` | clip | — | -2 | 1024 | 1 | 1 | 13/19 | — | `ui_v215.sc` tag08 #20782 |
| 14 | `training_progress` | clip | — | 16.1 | 217.75 | 1 | 1 | 14/19 | — | `ui_v215.sc` tag08 #20783 |
| 15 | `match_making` | clip | — | 0 | 140 | 1 | 1 | 15/19 | — | `ui_v215.sc` tag08 #20784 |
| 16 | `match_making_ladder` | clip | — | 0 | 140 | 1 | 1 | 16/19 | — | `ui_v215.sc` tag08 #20784 |
| 17 | `news_headline` | ? | — | -26.4 | 170.3 | 1 | 1 | 17/19 | — | `ui_v215.sc` tag08 #20785 |
| 18 | `season_end_time` | ? | — | 19.05 | 524 | 1 | 1 | 18/19 | — | `ui_v215.sc` tag08 #20786 |

### ui_battle_end ｜ `asset_noScale` ｜ clip=478 ｜ fps=60 ｜ frames=18 ｜ 元件 18 个

| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | `—（无名）` | clip | — | 50 | -73.2 | 1 | 1 | 0/18 | — | `ui_battle_end_v215.sc` tag08 #1809 |
| 1 | `—（无名）` | clip | — | 50 | 1.5 | 1 | 1 | 1/18 | — | `ui_battle_end_v215.sc` tag08 #1810 |
| 2 | `—（无名）` | clip | — | 215 | -76 | 1 | 1 | 2/18 | — | `ui_battle_end_v215.sc` tag08 #1811 |
| 3 | `—（无名）` | clip | — | 216 | 3 | 1 | 1 | 3/18 | — | `ui_battle_end_v215.sc` tag08 #1812 |
| 4 | `—（无名）` | clip | — | 392 | 2 | 1 | 1 | 4/18 | — | `ui_battle_end_v215.sc` tag08 #1813 |
| 5 | `—（无名）` | shape | frame_232 | 392 | -77 | 0.8 | 0.8 | 5/18 | 488 | `ui_battle_end_v215.sc` tag08 #1814 |
| 6 | `—（无名）` | shape | frame_235 | 392 | -77 | 0.6 | 0.6 | 6/18 | — | `ui_battle_end_v215.sc` tag08 #1815 |
| 7 | `—（无名）` | ? | — | 357.5 | -119.1 | 1 | 1 | 7/18 | — | `ui_battle_end_v215.sc` tag08 #1816 |
| 8 | `—（无名）` | clip | — | 474 | -76.85 | 1 | 1 | 8/18 | — | `ui_battle_end_v215.sc` tag08 #1817 |
| 9 | `—（无名）` | clip | — | -5 | 70 | 1 | 1 | 9/18 | — | `ui_battle_end_v215.sc` tag08 #1818 |
| 10 | `—（无名）` | shape | frame_011 | 33 | 70 | 1 | 1 | 10/18 | — | `ui_battle_end_v215.sc` tag08 #1819 |
| 11 | `—（无名）` | shape | frame_236 | 91 | 71 | 1 | 1 | 11/18 | — | `ui_battle_end_v215.sc` tag08 #1820 |
| 12 | `—（无名）` | shape | frame_237 | 171 | 96 | 1 | 1 | 12/18 | — | `ui_battle_end_v215.sc` tag08 #1821 |
| 13 | `—（无名）` | shape | frame_238 | 21 | 137.75 | 1 | 1 | 13/18 | — | `ui_battle_end_v215.sc` tag08 #1822 |
| 14 | `—（无名）` | shape | frame_239 | -46 | -93 | 1 | 1 | 14/18 | — | `ui_battle_end_v215.sc` tag08 #1823 |
| 15 | `—（无名）` | clip | — | 195.4 | -155 | 1 | 1 | 15/18 | — | `ui_battle_end_v215.sc` tag08 #1824 |
| 16 | `—（无名）` | clip | — | -135.9 | 2.95 | 1 | 1 | 16/18 | — | `ui_battle_end_v215.sc` tag08 #1825 |
| 17 | `—（无名）` | clip | — | 429.1 | -166.25 | 1 | 1 | 17/18 | — | `ui_battle_end_v215.sc` tag08 #1826 |

## 4. 与截图量取值的交叉验证（本片最硬的证据）

基准：`策划/参考图/几何量取.md` §1.1（`07_卡组编辑_1242x2208.jpg`，已折算 @1080）。

| 项 | `.sc` 解出（本片） | 截图量取 @1080（几何量取.md） | 判定 | 误差 |
|---|---|---|---|---|
| 卡格**列步进** | `card_page_deck_special` 三条同族元件 x = **-256.5 / 0 / +256.5** ⇒ 步进 **256.5** | A3 列步进 = **254.5** | **一致** | **0.8%** |
| 卡格横向对称 | 三元件 x 关于 **0 对称**（±256.5） | 4 列阵列关于画面中线对称 | **一致**（互证"原点在中心"） | — |

> 这是**两条独立路径互证**：一条是 `.sc` 二进制里的矩阵平移差，一条是原版截图的像素扫描。
> **相对**步进吻合到 0.8%（<读数容差 ±5px @1242 折算后的 4.3px）⇒ 单位换算（÷20）方向正确。
> ⚠️ **绝对**位置（元件在屏幕第几像素）**未互证**，原因见 §5 —— ⛔ 不写成"一致"。

## 5. 未解出 / BLOCKED（⛔ 不许当结论）

| # | 未解出的东西 | 试过什么 | 为什么解不出 | 消除条件 |
|---|---|---|---|---|
| F1 | 元件在 1080×1920 屏上的**绝对像素** | ① 对 `HUD_player`（root，无父）按"屏幕中心原点/y 向下"换算；② 对 `card_page_deck_special` 同法 | 与 `几何量取.md` §1.3 的 HUD 量取值**对不上**（例：`HUD_player.elixir_bar` ty=-27.45 ⇒ 中心上方 27px，而截图圣水条在 y≈1819）。⇒ `.sc` 的 root 局部系**不是**手机屏系 | 需要引擎侧把命名元件摆到屏上的**布局代码/配置**（原版运行时文件，本工程未取得）；或用户给"某界面某个元件的绝对 x/y"作锚 |
| F2 | 逐级复合后的**全局坐标** | 递归父链 | 子元件指向 clip 时坐标是**父的局部系**；`ui.sc` 里 root clip（如 `HUD_player`）**无父**（全表扫过，无任何 clip 的 children 引用它）⇒ 复合链在 `.sc` 内就到顶 | 同 F1 |
| F3 | `0c` payload 尾部的**嵌套子块**（`0b`+u32 长度+…，内含帧标签如 `Idle`） | dump 了 `button_timeline` 尾巴 | 结构可见但**上层无参考实现**（`sc_decode` 也未解）⇒ 本片只用它不涉及的部分 | 同 #1 的上游更完整实现 |

## 6. 穷尽记录（找参考实现）

| # | 关键词 / 入口 | 站点类别 | 结论 |
|---|---|---|---|
| 1 | `Supercell SWF sc3d MovieClip matrix tag 08 parser`（EN） | GitHub 仓库 + 本地已下载副本 | ✔ 命中：`Galaxy1036/sc_decode`（`08`=6×int32；`0c` 三元组逐字给出） |
| 2 | `SupercellSC2 precision scale translation 20 1024`（EN） | GitHub raw（`mirsella/clash-royale`） | ✔ 命中：`precision_multiplier` 2→20、3→1024；矩阵两套精度 |
| 3 | `SupercellSWF Animate Clash Royale MovieClip frame elements`（EN） | GitHub raw（`sc-workshop/SupercellSWF-Animate`） | ✔ 佐证 SC2 的 matrix bank + (instance,matrix,color) 三元组 |
| 4 | `clash royale sc 放置矩阵 坐标 解析`（CN） | 中文搜索引擎 | ⚠️ 本机无 web 搜索工具（宿主只有本地检索 + `Invoke-WebRequest`）⇒ **未执行**，如实登记 |
| 5 | `.sc` 明文（QuickBMS 产物） | 本机 `原版资源/sc/*_v215.sc` | ✔ 本片解析对象（12 个 UI `.sc` 的权威版） |
| 6 | `_tex.sc` / `*_tex.png` 图集 | 本机 `cr-assets-png/assets/sc/*_out` | ✔ 帧像素（只作版本指纹，不含坐标） |
| 7 | PNG dump（`*_sprite_NNN.png`） | 本机 | ✘ 只有编号，无坐标/命名 |
| 8 | `api.github.com/repos/GlixeDen/supercell-swf/...`（EN） | GitHub REST API | ✘ 本 IP **403 限流** ⇒ 本轮无法现拉新源码 |

**格式覆盖**：`.sc`（明文）✔ ｜ `_tex.sc`/图集 ✔ ｜ PNG dump ✔ ｜ 开源解析器（2 个 Python + 1 个 C#/Blender）✔ ｜ 站点 3 类（GitHub 仓/raw/API + 本机素材树）✔

