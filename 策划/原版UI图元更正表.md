# 原版 UI 图元更正表（现在 → 应该）

> 依据 = `策划/原版UI素材名称索引.md`（2.1.5 `.sc` 的显式导出表）。
> **本表可直接照改**：`现用帧号` 一律是 `client/Assets/Resources/Sprites/Ui/<用途>/<源图集>/frame_NNN.png` 的 NNN。

- 核对总数：**65 个** ResPaths 登记的落地图元（`client/**/Sprites/Ui/**` 下另有 2 个未登记的文件，见 §4）
- **判定：命中 55 / 不符 5 / 无法判定 5（合计 65）**

## 1. 逐条清单

| # | 现用途键 | 现用帧号 | 原版命名（导出表显式引用） | 判定 | 建议改用 / 改名 | 依据 |
|---|---|---|---|---|---|---|
| 1 | `PanelPaper` | `ui_out` frame_806 | —（该 shape 不被任何 `0c` 动画引用） | **无法判定** | 可继续当面板底用，但「哪块是底/哪块是角」本片无权威依据 —— 需 9-slice 几何（未解出）或原版布局数据 | 视觉=米黄面板件；该 shape 不在任何 0c 动画的 shapeID 列表里（无命名引用） |
| 2 | `PanelPaperCornerTr` | `ui_out` frame_807 | —（该 shape 不被任何 `0c` 动画引用） | **无法判定** | 同族件，方位无法判定；不要按「右上斜切角」直接摆 9-slice | 无命名引用；缩略图不足以判定切角方位 |
| 3 | `PanelPaperCornerTl` | `ui_out` frame_808 | —（该 shape 不被任何 `0c` 动画引用） | **无法判定** | 同上 | 无命名引用；方位不可判定 |
| 4 | `PanelPaperCornerBl` | `ui_out` frame_811 | —（该 shape 不被任何 `0c` 动画引用） | **无法判定** | 同上 | 无命名引用；方位不可判定 |
| 5 | `PanelPaperCornerBig` | `ui_out` frame_812 | —（该 shape 不被任何 `0c` 动画引用） | **无法判定** | 同上 | 无命名引用；方位不可判定 |
| 6 | `PanelFrameOutline` | `ui_out` frame_802 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（白色空心圆角框） |
| 7 | `PanelFrameWhiteInner` | `ui_out` frame_532 | `card_frame_glow_legendary` | **不符** | 该帧归给「卡牌框」（建议 `CardFrameGlowLegendary`）；通用面板内框需另找 | 原版命名引用 = `card_frame_glow_legendary`（.sc 导出表显式引用，frame 532） |
| 8 | `PanelFrameGrey` | `ui_out` frame_592 | `leaderboard_guild_item_01`, `leaderboard_guild_item_02`, `legend_list_item_01` …(17 条) | **命中** | 保留可用；若做列表项建议改名 `ListItemFrame` | 原版命名引用 = `leaderboard_guild_item_01/02`、`rank_list_item_*`、`legend_list_item_*` |
| 9 | `PanelCornerBlueGold` | `ui_out` frame_505 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（蓝+金边） |
| 10 | `PanelEdgeBlueGold` | `ui_out` frame_506 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（蓝+金边） |
| 11 | `PanelFrameDark` | `ui_battle_end_out` frame_108 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 12 | `ButtonCapsule` | `ui_out` frame_002 | —（该 shape 不被任何 `0c` 动画引用） | **不符** | 要「绿色主按钮底」就用 2/4/5；要「白色胶囊按钮底」⇒ 源内无对应（见更正表 §3） | 视觉=绿色圆角件；原版 .sc 无命名引用 |
| 13 | `ButtonGreenCornerTl` | `ui_out` frame_004 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（绿色圆角） |
| 14 | `ButtonGreenCorner` | `ui_out` frame_005 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（绿色大圆角） |
| 15 | `ButtonBlueCorner` | `ui_out` frame_165 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（蓝色圆角） |
| 16 | `ButtonBlueCornerAlt` | `ui_out` frame_166 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致（蓝色大圆角） |
| 17 | `ButtonGold` | `ui_out` frame_300 | `popup_tournament_end` | **命中** | 可保留 | 视觉=金色立体按钮；命名引用 = `popup_tournament_end` |
| 18 | `ButtonOrange` | `ui_out` frame_357 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留；注意原版橙色小按钮本体是 33-36 | 视觉=橙色圆角；导出表里 `button_small_orange` 覆盖 frame 33-36 |
| 19 | `ButtonOrangeAlt` | `ui_out` frame_359 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉=橙色圆角 |
| 20 | `ButtonWhite` | `ui_out` frame_476 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 21 | `ButtonDarkGrey` | `ui_out` frame_014 | `link_window`, `change_name_window`, `clash_nights_popup_01` …(104 条) | **命中** | 可保留 | 命名引用 = `link_window`/`change_name_window`/`popup_*` 等 100+ 条 clip |
| 22 | `ButtonDarkGreyAlt` | `ui_out` frame_015 | `link_window`, `change_name_window`, `clash_nights_popup_01` …(104 条) | **命中** | 可保留 | 同上 |
| 23 | `ButtonOrangeWide` | `ui_out` frame_551 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 24 | `TitleBarGold` | `ui_out` frame_069 | `shop_card_title_new`, `shop_speical_title`, `shop_special_template` …(8 条) | **命中** | 用途对（标题条），但「金色」描述不成立 ⇒ 建议改名 `PanelTitleBar` | 命名引用 = `shop_card_title_new`/`shop_speical_title`/`panel_battleSpells`/`panel_ingame`；`panel_ingame` 组件含 frame 62-71 |
| 25 | `TitleBarWood` | `ui_out` frame_070 | `shop_card_title_new`, `shop_speical_title`, `shop_special_template` …(8 条) | **命中** | 可保留（名称即「木质」） | 同上 |
| 26 | `ElixirBarTrack` | `ui_out` frame_516 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉=紫色条；原版 .sc 无命名引用（圣水条本体在 HUD 层） |
| 27 | `ElixirBarFrame` | `ui_out` frame_517 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉=深色条 |
| 28 | `ElixirBarFill` | `ui_out` frame_518 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉=品红条 |
| 29 | `BarGold` | `ui_battle_end_out` frame_201 | —（该 shape 不被任何 `0c` 动画引用） | **不符** | 改成 `GoldSquarePlate` 或弃用；结算界面的金色底条请用 `ui_battle_end` 里更宽的条/`win_reward_bar` | 视觉=24×24 方块；用途键描述「宽」不成立 |
| 30 | `BarWhite` | `ui_battle_end_out` frame_222 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 31 | `SlotCard` | `ui_out` frame_043 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 32 | `SlotCardAlt` | `ui_out` frame_054 | `battle_end_chat_selection_bubble` | **不符** | 该帧归给「聊天气泡」（建议 `ChatBubble`）；卡槽底变体需另找 | 原版命名引用 = `battle_end_chat_selection_bubble`（frame 54） |
| 33 | `SlotCardPlain` | `ui_out` frame_531 | `icon_btn_card_common` | **命中** | 可保留 | 命名引用 = `icon_btn_card_common` |
| 34 | `SlotCorner` | `ui_out` frame_011 | `link_device_code` | **命中** | 可保留 | 视觉一致 |
| 35 | `IconElixirDrop` | `ui_out` frame_099 | `clan_badge_43_02`, `clan_badge_43_01` | **命中** | 可保留 | 视觉=紫色水滴；命名引用 = `clan_badge_43_01/02`（复用） |
| 36 | `IconCrownGold` | `ui_out` frame_050 | `sticker_angry_icon`, `sticker_lol_icon` | **命中** | 可保留 | 视觉=金皇冠；命名引用 = `sticker_angry_icon`/`sticker_lol_icon`（复用） |
| 37 | `IconCrownBlack` | `ui_out` frame_075 | `clan_badge_13_01`, `clan_badge_13_02`, `clan_badge_12_01` …(182 条) | **命中** | 可保留 | 视觉=黑皇冠；命名引用 = `clan_badge_13_01` 等 180+ 条（复用为徽章底板） |
| 38 | `IconCrownBlueGem` | `ui_out` frame_187 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 39 | `IconCrownRedGem` | `ui_out` frame_188 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 40 | `IconCrownFivePoint` | `ui_out` frame_878 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 41 | `IconCrownBig` | `ui_out` frame_883 | `hp_enemy_hero`, `hp_player_hero`, `hp_enemy_wizard` …(8 条) | **命中** | 可保留 | 视觉一致；命名引用 = `hp_player_hero`/`hp_enemy_hero` 等（HP 条王冠） |
| 42 | `IconChestGoldOpen` | `ui_out` frame_526 | `icon_boost_crown_chest` | **命中** | 可保留 | 视觉一致；命名引用 = `icon_boost_crown_chest` |
| 43 | `IconChestWoodClosed` | `ui_out` frame_527 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 44 | `IconChestHalfOpen` | `ui_out` frame_555 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 45 | `IconSearch` | `ui_out` frame_279 | `icon_tournament_search` | **命中** | 可保留 | 命名引用 = `icon_tournament_search`；视觉=放大镜 |
| 46 | `IconAttackGear` | `ui_out` frame_280 | `popup_tournament_cancelled_refund`, `popup_tournament_create_confirm`, `popup_tournament_leave_confirm` …(7 条) | **命中** | 可保留 | 命名引用 = `icon_menu_tornament`；视觉=剑盾徽章 |
| 47 | `IconHeal` | `ui_out` frame_281 | `icon_tournament_create` | **不符** | 改名 `IconTournamentCreate`/`IconPlusCircle`；治疗图标需另找（源内无 heal 命名） | 原版命名引用 = `icon_tournament_create`（frame 281） |
| 48 | `IconQuestion` | `ui_out` frame_292 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 49 | `IconBattle` | `ui_out` frame_226 | `popup_2v2_ladder_help`, `icon_menu_battle`, `popup_replay_results` …(4 条) | **命中** | 可保留 | 命名引用 = `icon_menu_battle`；视觉=双剑 |
| 50 | `IconArrowUp` | `ui_out` frame_519 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 51 | `IconPlus` | `ui_out` frame_521 | `item_shop_arena_offer_2`, `item_shop_arena_offer_3` | **命中** | 可保留 | 视觉一致；命名引用 = `item_shop_arena_offer_2/3` |
| 52 | `IconGear` | `ui_out` frame_563 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 53 | `IconGamepad` | `ui_out` frame_570 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 54 | `IconChat` | `ui_out` frame_572 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 55 | `IconGemBlue` | `ui_out` frame_597 | `rank_popup_element`, `legend_top_item`, `legend_top_item_previous` | **命中** | 可保留 | 视觉一致；命名引用 = `rank_popup_element` |
| 56 | `IconTrophy` | `ui_battle_end_out` frame_109 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 57 | `IconTrophyLaurel` | `ui_battle_end_out` frame_195 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 58 | `IconMedal` | `ui_battle_end_out` frame_213 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 59 | `IconGemGreen` | `ui_battle_end_out` frame_214 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 60 | `IconCoin` | `ui_battle_end_out` frame_215 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 61 | `IconCoinPile` | `ui_battle_end_out` frame_216 | `gold_reward` | **命中** | 可保留 | 视觉一致；命名引用 = `gold_reward` |
| 62 | `IconCastle` | `ui_battle_end_out` frame_223 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 63 | `IconShieldBlue` | `ui_battle_end_out` frame_225 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 64 | `IconBanner` | `ui_battle_end_out` frame_226 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |
| 65 | `IconSpellBook` | `ui_battle_end_out` frame_205 | —（该 shape 不被任何 `0c` 动画引用） | **命中** | 可保留 | 视觉一致 |

## 2. 不符（5 条）——照改

### PanelFrameWhiteInner：`ui_out` frame_532

- 实际是：`card_frame_glow_legendary`（传说卡牌框光效；厚白六/八边形描边）
- 动作：该帧归给「卡牌框」（建议 `CardFrameGlowLegendary`）；通用面板内框需另找
- 依据：原版命名引用 = `card_frame_glow_legendary`（.sc 导出表显式引用，frame 532）

### ButtonCapsule：`ui_out` frame_002

- 实际是：绿色圆角按钮件（非白色胶囊描边）
- 动作：要「绿色主按钮底」就用 2/4/5；要「白色胶囊按钮底」⇒ 源内无对应（见更正表 §3）
- 依据：视觉=绿色圆角件；原版 .sc 无命名引用

### BarGold：`ui_battle_end_out` frame_201

- 实际是：24×24 金色圆角**方块**（不是「宽底条」）
- 动作：改成 `GoldSquarePlate` 或弃用；结算界面的金色底条请用 `ui_battle_end` 里更宽的条/`win_reward_bar`
- 依据：视觉=24×24 方块；用途键描述「宽」不成立

### SlotCardAlt：`ui_out` frame_054

- 实际是：`battle_end_chat_selection_bubble`（聊天气泡，左下尖角）
- 动作：该帧归给「聊天气泡」（建议 `ChatBubble`）；卡槽底变体需另找
- 依据：原版命名引用 = `battle_end_chat_selection_bubble`（frame 54）

### IconHeal：`ui_out` frame_281

- 实际是：`icon_tournament_create`（蓝底白「+」：新建）
- 动作：改名 `IconTournamentCreate`/`IconPlusCircle`；治疗图标需另找（源内无 heal 命名）
- 依据：原版命名引用 = `icon_tournament_create`（frame 281）

## 3. `源内无对应` 的用途（找不到同名/同义的原版命名）

| 用途键 | 源内情况 | 该用途的素材该从哪来 |
|---|---|---|
| `ButtonCapsule`（白色圆角胶囊按钮底框） | `ui.sc` 的 export 里**没有**"白色胶囊"命名；`button_*` 只有 `button_timeline`/`button_small_orange`/`button_small_square_orange`/`button_share_deck`/`full_page_button_tab*` | 白色按钮底用 `ui_out 476`（白色按钮底）或自绘 9-slice；or 用 `ui_battle_end` 的白条 |
| `ElixirBarTrack/Frame/Fill`（圣水条三件） | `ui.sc` 里 `elixir` 命名的只有 `Elixir_bar_drop_anim`/`elixir_float_txt`/`print_elixir_speed`/HUD 相关 clip，**均无 shapeID**（空 clip）⇒ 圣水条本体不在 `ui.sc` 的 shape 表里 | 圣水条是 **HUD 层**（战斗内 UI），本批 `.sc` 里没有 HUD 的图集；若要用原版圣水条，需另找 HUD 素材（`ui.sc` 里的 `HUD_*` 命名只有 clip 无 shape）⇒ **保留现状并登记差异** |
| `IconHeal`（治疗图标） | `ui.sc` 里没有任何 heal 命名 | 保留现状（若确有治疗展示需求），或换 `IconPlus`(521) 语义 |
| `IconGear/Gampad/Chat/ArrowUp/Question`（设置/手柄/聊天/升级/问号） | 视觉一致但**原版无命名引用**（都是静态图形） | 保留现状（视觉已对上，只是原版没给名字） |
| `PanelPaper*`/`PanelFrame*` 的 9-slice 方位 | 原版 `.sc` 对这些 shape **无任何命名引用** ⇒ 哪个件是哪一角在本片无权威依据 | 保留现状，并在 `client/资源欠缺清单.md` 登记「9-slice 方位待原版布局数据确认」 |

## 4. 附：`Sprites/Ui/**` 下**未登记进 ResPaths** 的落地文件

| 文件 | 实际是什么 | 建议 |
|---|---|---|
| `Bars/loading_out/frame_015.png` | 绿色圆角条（加载界面进度条底；原版 `loading.sc` 无命名引用；同目录 `loading_bar_bloe` 覆盖 frame 19-23） | 若做加载进度条，改用 `loading_out` frame 19-23（原版 `loading_bar_bloe`）；否则删 |
| `Icons/loading_out/frame_028.png` | **「CLASH ROYALE」整幅 Logo**（520×224，非透明覆盖完整） | 启动/登录页可直接用（原版命名引用为空，但视觉唯一且无歧义） |

## 5. 本片**没有**做的事（不要当成已完成）

1. 没有改 `client/**` 一个字（本片只出表）。
2. 没有解出坐标/缩放（`0b` 不存在；`08` 矩阵与 `0c` 三元组的对应关系未验证 ⇒ 未解出）。
3. `ui_chest`/`ui_chest_3d`/`ui_spells`/`ui_arena`/`tutorial`/`arena_training`/`debug`/`spell_goblin_barrel`/`effects` 的 export 表已生成（见索引 md §3），但**未逐条核对视觉**（客户端未引用它们）。
