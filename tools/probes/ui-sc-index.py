# -*- coding: utf-8 -*-
"""ui-sc-index.py -- 判据资产：从原版 UI `.sc` 的**显式导出表**生成「UI 图元权威身份」文档。

复跑
----
  python tools/probes/ui-sc-index.py

输入（只读，缺一不可）
---------------------
  · 原版资源/sc/<name>_v215.sc   12 个 UI `.sc`（CR **2.1.5** APK `assets/sc/<name>`，QuickBMS 解密后的明文；
      来源见 策划/原版UI素材名称索引.md §0「版本指纹」—— 2.1.5 的 ShapeCount/图集尺寸与本地
      `原版资源/cr-assets-png/assets/sc/*_out` 100% 吻合，2.2.1 与 v1.0.0 都对不上）
  · 原版资源/cr-assets-png/assets/sc/<name>_out/*_sprite_NNN.png   （算每个 export 的像素尺寸）
  · client/Assets/Resources/Sprites/Ui/**                          （落地图元清单）

输出（**生成物，⛔ 不要手改**）
-----------------------------
  · 策划/原版UI素材名称索引.md    （每个 .sc 一节 + 全量 export 表 + 穷尽记录）
  · 策划/原版UI图元更正表.md      （65 个落地图元逐条：命中/不符/无法判定 + 建议）
  · 策划/对照表.md                 （W1 的行已由 AC1 折入；本脚本的草稿另写到 .ai-tmp/test/W1-index-draft.md）

关键事实（本脚本依赖，均已实测）
--------------------------------
  A. `frame_NNN.png` == `<dir>_sprite_NNN.png` == `.sc` 里第 NNN 条 `12` 记录（序号 0 起）；
     `12` 记录数 == ShapeCount == `_out` 目录 PNG 数（12/12 个 UI `.sc` 全部成立）。
  B. ⚠️ `sid ≠ 序号`：`12` 记录的 sid 不连续（ui 的 sid ∈ 0..6812）⇒
     「frame -> 动作」必须走 **序号 → sid → clip(`0c`)** 这条链。
  C. `Export` 表 = 显式 (名字, ClipID) 数组；`0c` 记录自带 shapeID 列表 ⇒ 名字->帧是**显式引用**。
  D. 本批 `.sc` **没有 `0b` 记录**（tag 直方图无 0b）；放置矩阵是 tag `08`（6×int32），
     但 `0c` 内 `cnt1` 三元组到 `08` 记录的索引语义**未验证** ⇒ 坐标登记「未解出」，本脚本不输出 x/y/scale。
"""
import os, sys, json, collections
sys.stdout.reconfigure(encoding='utf-8')
import importlib.util
from PIL import Image
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
spec = importlib.util.spec_from_file_location('sai', os.path.join(ROOT, 'tools', 'probes', 'sc-anim-index.py'))
sai = importlib.util.module_from_spec(spec); spec.loader.exec_module(sai)
SC215 = os.path.join(ROOT, '原版资源', 'sc')   # <name>_v215.sc 持久副本（本文件是判据资产，读持久输入）
PNGROOT = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc')
UI = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Ui')
SRC_URL = ('https://raw.githubusercontent.com/smlbiobot/cr/master/'
           'apk/2.1.5/com.supercell.clashroyale-2.1.5/assets/sc/<name>')

FILES = ['ui', 'ui_battle_end', 'ui_chest', 'ui_chest_3d', 'ui_spells', 'ui_arena',
         'loading', 'debug', 'tutorial', 'arena_training', 'spell_goblin_barrel', 'effects']

data, ordmap, sids = {}, {}, {}
for n in FILES:
    raw = open(os.path.join(SC215, n + '_v215.sc'), 'rb').read()
    data[n] = sai.parse_unit(raw)
    S = sai.R(raw)
    S.read_u16(); S.read_u16(); S.read_u16(); S.read_u16(); S.read_u16(); S.read_u16(); S.read(5)
    EC = S.read_u16(); [S.read_u16() for _ in range(EC)]
    for _ in range(EC): S.read(S.read_byte())
    seq = []
    while len(raw) - S.tell() > 0:
        tag = S.read(1).hex(); size = S.read_u32(); payload = S.read(size)
        if tag == '12':
            seq.append(sai.R(payload).read_u16())
    sids[n] = seq
    om = {}
    for i, s in enumerate(seq):
        om.setdefault(s, i)
    ordmap[n] = om

# 每个 _out 帧的非透明包围盒尺寸
SIZE_CACHE = {}
def fsize(scn, idx):
    k = (scn, idx)
    if k in SIZE_CACHE:
        return SIZE_CACHE[k]
    p = os.path.join(PNGROOT, scn + '_out', '%s_sprite_%s.png'
                     % (scn, str(idx).rjust(len(str(data[scn]['ShapeCount'])), '0')))
    if not os.path.isfile(p):
        p2 = os.path.join(PNGROOT, scn + '_out', '%s_sprite_%d.png' % (scn, idx))
        p = p2 if os.path.isfile(p2) else None
    v = None
    if p:
        with Image.open(p) as im:
            im = im.convert('RGBA')
            b = im.getbbox()
            v = (b[2] - b[0], b[3] - b[1]) if b else (0, 0)
    SIZE_CACHE[k] = v
    return v


def exports_of(scn):
    res = data[scn]
    clip_by_id = {c['id']: c for c in res['clips']}
    out = []
    for nm, cid in zip(res['exp_names'], res['exp_ids']):
        c = clip_by_id.get(cid)
        fr = sorted({ordmap[scn][s] for s in c['sids'] if s in ordmap[scn]}) if c else []
        out.append(dict(name=nm, clip=cid, fps=(c or {}).get('fps'), frames=(c or {}).get('frames'),
                        sid_list=(c or {}).get('sids', []), fr=fr))
    return out


EX = {n: exports_of(n) for n in FILES}

# ---------------- 落地图元判定（人工逐条裁定，依据 = 视觉 + 原版命名引用） ----------------
# (purpose_dir, src_dir, frame, ResPaths key, verdict, 实际身份, 建议, 依据)
V = [
 ('Panels','ui_out',806,'PanelPaper','无法判定','米黄+金边面板件；原版 .sc 无任何动画引用',
  '可继续当面板底用，但「哪块是底/哪块是角」本片无权威依据 —— 需 9-slice 几何（未解出）或原版布局数据',
  '视觉=米黄面板件；该 shape 不在任何 0c 动画的 shapeID 列表里（无命名引用）'),
 ('Panels','ui_out',807,'PanelPaperCornerTr','无法判定','米黄面板件（与 806 同族，806-812 中缺 809/810）',
  '同族件，方位无法判定；不要按「右上斜切角」直接摆 9-slice','无命名引用；缩略图不足以判定切角方位'),
 ('Panels','ui_out',808,'PanelPaperCornerTl','无法判定','米黄面板件（同族）','同上','无命名引用；方位不可判定'),
 ('Panels','ui_out',811,'PanelPaperCornerBl','无法判定','米黄面板件（同族，顶边有 V 形缺口）','同上','无命名引用；方位不可判定'),
 ('Panels','ui_out',812,'PanelPaperCornerBig','无法判定','米黄面板件（同族，金边）','同上','无命名引用；方位不可判定'),
 ('Panels','ui_out',802,'PanelFrameOutline','命中','白色厚描边圆角方框（空心）','可保留','视觉一致（白色空心圆角框）'),
 ('Panels','ui_out',532,'PanelFrameWhiteInner','不符','`card_frame_glow_legendary`（传说卡牌框光效；厚白六/八边形描边）',
  '该帧归给「卡牌框」（建议 `CardFrameGlowLegendary`）；通用面板内框需另找',
  '原版命名引用 = `card_frame_glow_legendary`（.sc 导出表显式引用，frame 532）'),
 ('Panels','ui_out',592,'PanelFrameGrey','命中','空心描边矩形框；原版用于排行榜/名次列表项',
  '保留可用；若做列表项建议改名 `ListItemFrame`',
  '原版命名引用 = `leaderboard_guild_item_01/02`、`rank_list_item_*`、`legend_list_item_*`'),
 ('Panels','ui_out',505,'PanelCornerBlueGold','命中','蓝底金边折叠缎带角','可保留','视觉一致（蓝+金边）'),
 ('Panels','ui_out',506,'PanelEdgeBlueGold','命中','蓝底金边缎带边','可保留','视觉一致（蓝+金边）'),
 ('Panels','ui_battle_end_out',108,'PanelFrameDark','命中','深色圆角空心框 212×54','可保留','视觉一致'),
 ('Buttons','ui_out',2,'ButtonCapsule','不符','绿色圆角按钮件（非白色胶囊描边）',
  '要「绿色主按钮底」就用 2/4/5；要「白色胶囊按钮底」⇒ 源内无对应（见更正表 §3）',
  '视觉=绿色圆角件；原版 .sc 无命名引用'),
 ('Buttons','ui_out',4,'ButtonGreenCornerTl','命中','绿色按钮切角','可保留','视觉一致（绿色圆角）'),
 ('Buttons','ui_out',5,'ButtonGreenCorner','命中','绿色按钮大圆角切角','可保留','视觉一致（绿色大圆角）'),
 ('Buttons','ui_out',165,'ButtonBlueCorner','命中','蓝色按钮切角','可保留','视觉一致（蓝色圆角）'),
 ('Buttons','ui_out',166,'ButtonBlueCornerAlt','命中','蓝色按钮切角（变体）','可保留','视觉一致（蓝色大圆角）'),
 ('Buttons','ui_out',300,'ButtonGold','命中','金黄色按钮底（原版用于 `popup_tournament_end`）','可保留',
  '视觉=金色立体按钮；命名引用 = `popup_tournament_end`'),
 ('Buttons','ui_out',357,'ButtonOrange','命中','橙色按钮切角','可保留；注意原版橙色小按钮本体是 33-36',
  '视觉=橙色圆角；导出表里 `button_small_orange` 覆盖 frame 33-36'),
 ('Buttons','ui_out',359,'ButtonOrangeAlt','命中','橙色按钮切角（变体）','可保留','视觉=橙色圆角'),
 ('Buttons','ui_out',476,'ButtonWhite','命中','白色/浅灰按钮底','可保留','视觉一致'),
 ('Buttons','ui_out',14,'ButtonDarkGrey','命中','蓝灰按钮件；原版被 100+ 弹窗复用','可保留',
  '命名引用 = `link_window`/`change_name_window`/`popup_*` 等 100+ 条 clip'),
 ('Buttons','ui_out',15,'ButtonDarkGreyAlt','命中','蓝灰按钮件（变体）','可保留','同上'),
 ('Buttons','ui_out',551,'ButtonOrangeWide','命中','宽扁按钮框（深色底 + 橙描边）','可保留','视觉一致'),
 ('Bars','ui_out',69,'TitleBarGold','命中','木质/棕色面板标题条（原版 `shop_card_title_new`）',
  '用途对（标题条），但「金色」描述不成立 ⇒ 建议改名 `PanelTitleBar`',
  '命名引用 = `shop_card_title_new`/`shop_speical_title`/`panel_battleSpells`/`panel_ingame`；`panel_ingame` 组件含 frame 62-71'),
 ('Bars','ui_out',70,'TitleBarWood','命中','木质标题条（同族）','可保留（名称即「木质」）','同上'),
 ('Bars','ui_out',516,'ElixirBarTrack','命中','紫色宽扁圆角条','可保留','视觉=紫色条；原版 .sc 无命名引用（圣水条本体在 HUD 层）'),
 ('Bars','ui_out',517,'ElixirBarFrame','命中','深色宽扁圆角条（描边/底）','可保留','视觉=深色条'),
 ('Bars','ui_out',518,'ElixirBarFill','命中','品红宽扁圆角条','可保留','视觉=品红条'),
 ('Bars','ui_battle_end_out',201,'BarGold','不符','24×24 金色圆角**方块**（不是「宽底条」）',
  '改成 `GoldSquarePlate` 或弃用；结算界面的金色底条请用 `ui_battle_end` 里更宽的条/`win_reward_bar`',
  '视觉=24×24 方块；用途键描述「宽」不成立'),
 ('Bars','ui_battle_end_out',222,'BarWhite','命中','白色圆角底条 35×14','可保留','视觉一致'),
 ('Slots','ui_out',43,'SlotCard','命中','白色卡片底框 107×159','可保留','视觉一致'),
 ('Slots','ui_out',54,'SlotCardAlt','不符','`battle_end_chat_selection_bubble`（聊天气泡，左下尖角）',
  '该帧归给「聊天气泡」（建议 `ChatBubble`）；卡槽底变体需另找',
  '原版命名引用 = `battle_end_chat_selection_bubble`（frame 54）'),
 ('Slots','ui_out',531,'SlotCardPlain','命中','白色卡片按钮底（原版 `icon_btn_card_common`）','可保留',
  '命名引用 = `icon_btn_card_common`'),
 ('Slots','ui_out',11,'SlotCorner','命中','白色圆角小角件','可保留','视觉一致'),
 ('Icons','ui_out',99,'IconElixirDrop','命中','紫色水滴','可保留','视觉=紫色水滴；命名引用 = `clan_badge_43_01/02`（复用）'),
 ('Icons','ui_out',50,'IconCrownGold','命中','金色皇冠','可保留','视觉=金皇冠；命名引用 = `sticker_angry_icon`/`sticker_lol_icon`（复用）'),
 ('Icons','ui_out',75,'IconCrownBlack','命中','黑色皇冠剪影','可保留','视觉=黑皇冠；命名引用 = `clan_badge_13_01` 等 180+ 条（复用为徽章底板）'),
 ('Icons','ui_out',187,'IconCrownBlueGem','命中','蓝底金皇冠（蓝宝石）','可保留','视觉一致'),
 ('Icons','ui_out',188,'IconCrownRedGem','命中','金皇冠（红宝石）','可保留','视觉一致'),
 ('Icons','ui_out',878,'IconCrownFivePoint','命中','金色五尖皇冠','可保留','视觉一致'),
 ('Icons','ui_out',883,'IconCrownBig','命中','金色五尖皇冠（大）','可保留','视觉一致；命名引用 = `hp_player_hero`/`hp_enemy_hero` 等（HP 条王冠）'),
 ('Icons','ui_out',526,'IconChestGoldOpen','命中','开启宝箱（金币/宝石）','可保留','视觉一致；命名引用 = `icon_boost_crown_chest`'),
 ('Icons','ui_out',527,'IconChestWoodClosed','命中','木纹宝箱（闭合）','可保留','视觉一致'),
 ('Icons','ui_out',555,'IconChestHalfOpen','命中','木纹宝箱（半开）','可保留','视觉一致'),
 ('Icons','ui_out',279,'IconSearch','命中','放大镜（原版 `icon_tournament_search`）','可保留','命名引用 = `icon_tournament_search`；视觉=放大镜'),
 ('Icons','ui_out',280,'IconAttackGear','命中','剑+盾+橄榄枝（原版 `icon_menu_tornament`）','可保留','命名引用 = `icon_menu_tornament`；视觉=剑盾徽章'),
 ('Icons','ui_out',281,'IconHeal','不符','`icon_tournament_create`（蓝底白「+」：新建）',
  '改名 `IconTournamentCreate`/`IconPlusCircle`；治疗图标需另找（源内无 heal 命名）',
  '原版命名引用 = `icon_tournament_create`（frame 281）'),
 ('Icons','ui_out',292,'IconQuestion','命中','金黄色问号','可保留','视觉一致'),
 ('Icons','ui_out',226,'IconBattle','命中','交叉双剑（原版 `icon_menu_battle`）','可保留','命名引用 = `icon_menu_battle`；视觉=双剑'),
 ('Icons','ui_out',519,'IconArrowUp','命中','绿色向上箭头','可保留','视觉一致'),
 ('Icons','ui_out',521,'IconPlus','命中','绿色加号','可保留','视觉一致；命名引用 = `item_shop_arena_offer_2/3`'),
 ('Icons','ui_out',563,'IconGear','命中','灰色齿轮','可保留','视觉一致'),
 ('Icons','ui_out',570,'IconGamepad','命中','白色游戏手柄','可保留','视觉一致'),
 ('Icons','ui_out',572,'IconChat','命中','白色对话气泡','可保留','视觉一致'),
 ('Icons','ui_out',597,'IconGemBlue','命中','灰蓝六边形宝石','可保留','视觉一致；命名引用 = `rank_popup_element`'),
 ('Icons','ui_battle_end_out',109,'IconTrophy','命中','金色奖杯','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',195,'IconTrophyLaurel','命中','奖杯+橄榄枝','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',213,'IconMedal','命中','金色圆奖章（蓝丝带）','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',214,'IconGemGreen','命中','绿色六边形宝石','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',215,'IconCoin','命中','金色圆币','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',216,'IconCoinPile','命中','金币堆','可保留','视觉一致；命名引用 = `gold_reward`'),
 ('Icons','ui_battle_end_out',223,'IconCastle','命中','灰色城堡','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',225,'IconShieldBlue','命中','蓝色盾牌（金星徽）','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',226,'IconBanner','命中','蓝色旗帜（金城堡纹）','可保留','视觉一致'),
 ('Icons','ui_battle_end_out',205,'IconSpellBook','命中','蓝色书本（金盾徽）','可保留','视觉一致'),
]
OTHERS = FILES  # 12
assert len(V) == 65, len(V)
nh = sum(1 for v in V if v[4] == '命中')
nb = sum(1 for v in V if v[4] == '不符')
nk = sum(1 for v in V if v[4] == '无法判定')
print('命中 %d / 不符 %d / 无法判定 %d = %d' % (nh, nb, nk, nh + nb + nk))

# =========================== 索引 md ===========================
L = []
A = L.append
A('# 原版 UI 素材名称索引（由 `.sc` 显式导出表解出，⛔ 不是看图猜的）')
A('')
A('> **本文件是生成物，⛔ 不要手改**：改内容请改生成器再跑。生成器 = `tools/probes/ui-sc-index.py`')
A('> （复跑：`python tools/probes/ui-sc-index.py`；读的是 `原版资源/sc/<name>_v215.sc`，复用 `tools/probes/sc-anim-index.py` 的解析器）。')
A('> 唯一权威来源 = **Clash Royale 2.1.5 版 APK 的 `.sc`**（`assets/sc/<name>`，QuickBMS 解密后的明文）。')
A('> 抓取 URL = `%s`；持久副本 = `原版资源/sc/<name>_v215.sc`。' % SRC_URL)
A('')
A('## 0. 为什么是 2.1.5（版本指纹，三条独立证据）')
A('')
A('| 证据 | 2.1.5 `.sc` | 2.2.1 `.sc` | v1.0.0(iOS) `.sc` | 本地 `cr-assets-png/*_out` |')
A('|---|---|---|---|---|')
A('| `ui` 的 `ShapeCount` | **914** | 1041 | 482 | **914 张 PNG** ✔ |')
A('| `ui` 第 1 张图集尺寸（`.sc` 声明 vs `ui_tex.png`） | **2812×3038 == 2812×3038** ✔ | 2552×3820 ✘ | — | — |')
A('| `ui_battle_end` | **240 == 240 张**、758×1400 == 758×1400 ✔ | 240 但 768×1396 ✘ | 文件不存在（v1.0.0 无 `ui_battle_end.sc`） | 240 张 |')
A('| `ui_chest` / `ui_spells` / `effects` | 268/92/612 全部 ✔ | 290/93/635 ✘ | — | 268/92/612 |')
A('')
A('**结论**：本地 `cr-assets-png/assets/sc/*_out` 是 **2.1.5** 的解包产物；**只有 2.1.5 的 `.sc` 能对上**。')
A('（上一轮拿到的 3 个角色 `.sc` 是 v1.0.0 的 —— 角色美术跨版本没变所以恰好也能对上，`ui` 就立刻崩了。）')
A('')
A('## 0.1 索引链（每一环都是**显式**数据，无顺序推断）')
A('')
A('```')
A('frame_NNN.png   ==   <dir>_sprite_NNN.png   ==   .sc 里第 NNN 条 `12` 记录（shape 记录序号，0 起）')
A('        12 记录数 == ShapeCount == _out 目录 PNG 数（本片实测 12/12 个 UI .sc 全部相等）')
A('`0c` 记录（动画）= ClipID + FPS + 帧数 + **shapeID 列表**（存的 sid，不是序号）')
A('`Export` 表 = 显式 (名字, ClipID) 数组  ==>  名字 -> clipID -> shapeID 列表 -> 序号 -> frame_NNN')
A('```')
A('')
A('⚠️ **`sid ≠ 序号`**：`12` 记录的 sid 值不连续（`ui` 的 sid 范围 0..6812），')
A('   所以「哪个 frame 属于哪个动作」必须走 **序号 → sid → clip** 这条链，不能拿 sid 当帧号。')
A('')
A('## 1. 本批 UI `.sc` 总览')
A('')
A('| 源 `.sc` | ShapeCount | 12 记录 | export 名数 | `0c` 动画数 | 图集数 | `08` 矩阵记录 | `09` 记录 | `0b` 记录 | clip 覆盖 shape |')
A('|---|---|---|---|---|---|---|---|---|---|')
for n in FILES:
    r = data[n]
    cov = set()
    for c in r['clips']:
        cov |= set(c['sids'])
    A('| `%s` | %d | %d | %d | %d | %d | %d | %d | **0（无 0b tag）** | %d/%d |'
      % (n, r['ShapeCount'], len(sids[n]), len(r['exp_names']), len(r['clips']), len(r['sheets']),
         r['tag_hist'].get('08', 0), r['tag_hist'].get('09', 0),
         len(cov & set(range(r['ShapeCount']))), r['ShapeCount']))
A('')
A('### 1.1 `0b` 记录 / 坐标（**未解出**，如实登记）')
A('')
A('- **`0b` 记录：这批 `.sc` 里一条都没有**（`/`tag` 直方图只有 `00/01/07/08/09/0c/0f/12/17/18/19/1a`）。')
A('  上一轮 `parse.py` 报的 `unknown tag 0b` 是把 `0c` payload 残余当成了新 tag，属误报。')
A('- **放置矩阵存在**：tag `08`（6×int32 矩阵）在 `ui` 里有 **36843 条**，`ui_battle_end` 2069 条 —— 这就是原版 UI 的坐标/缩放数据。')
A('- **但没有解出**：`0c` payload 里的 `cnt1` 条 `(u16,u16,u16)` 三元组与 `08` 记录的对应关系')
A('  在本项目没有参考实现可依（`sc_decode.py` 也不读它：`Stream.read(5)` 后 `continue`）。')
A('  ⇒ 按「指不出出处就不许写」的规矩，本片**不给出 x/y/scale**，登记为**未解出**。')
A('  若要解：需一个读 `08`+`0c` 三元组的参考实现（`mirsella/clash-royale` 的 SC2 渲染器是**现代格式**，不能直接套这一代）。')
A('')
A('## 2. 每个 UI `.sc` 一节')
for n in FILES:
    r = data[n]
    A('### 2.%d `%s`（副本 `原版资源/sc/%s_v215.sc`）' % (FILES.index(n) + 1, n, n))
    A('')
    A('- `ShapeCount` = **%d** ｜ `12` 记录 = %d ｜ export 名 = **%d** ｜ `0c` 动画 = **%d** ｜ 图集 = %s'
      % (r['ShapeCount'], len(sids[n]), len(r['exp_names']), len(r['clips']), r['sheets']))
    A('- tag 直方图：`%s`' % json.dumps({k: v for k, v in sorted(r['tag_hist'].items())}, ensure_ascii=False))
    A('- 未识别 tag（对本片无用、按 size 跳过）：%d 条' % len(r['unknown']))
A('')
A('## 3. 全量总表（源 `.sc` ｜ export 名 ｜ clip id ｜ shapeID → frame_NNN ｜ 尺寸 ｜ 推断用途）')
A('')
A('> 「尺寸」= 该 export 引用的各个 `*_sprite_NNN.png` 的**非透明包围盒**（可空 ⇒ 该帧全透明或该序号没有 PNG）。')
A('> 「frame_NNN」= **12 记录序号**，即客户端 `frame_NNN.png` 的那个 NNN。')
A('> 全量数据（含每个 clip 的 fps/帧数/原始 sid 列表）另存 `.ai-tmp/test/w1_final.json`。')
A('')
for n in FILES:
    A('### 3.%d `%s`（%d 条 export）' % (FILES.index(n) + 1, n, len(EX[n])))
    A('')
    A('| # | export 名（原版命名） | clip id | FPS | timeline 帧 | frame_NNN | 尺寸 | 推断用途 |')
    A('|---|---|---|---|---|---|---|---|')
    for i, e in enumerate(EX[n]):
        fr = e['fr']
        szs = []
        for f in fr:
            s = fsize(n, f)
            if s and s not in szs:
                szs.append(s)
        sizetxt = ' '.join('%d×%d' % s for s in szs[:4]) or '—'
        frtxt = ('`%s`' % (','.join(str(x) for x in fr[:12]) + ('…' if len(fr) > 12 else ''))) if fr else '—'
        A('| %d | `%s` | %d | %s | %s | %s | %s | |' % (i, e['name'], e['clip'], e['fps'], e['frames'], frtxt, sizetxt))
    A('')

# 穷尽记录
A('## 4. 穷尽记录（素材调研）')
A('')
A('### 4.1 关键词轮次（≥4 组，中英各半）')
A('')
A('| # | 关键词（语言） | 站点类别 | 拿到什么 / 结论 |')
A('|---|---|---|---|')
A('| 1 | `clash royale res/sc ui.sc ui_battle_end.sc`（EN） | GitHub 仓库树页（HTML embeddedData） | ✔ 定位到唯一有 `.sc` 的路径：`apk/1.0.0/…/Clash Royale.app/res/sc`（145 文件）；但**缺 `ui_battle_end.sc`** |')
A('| 2 | `clash royale 2.2.1 apk assets/sc ui ui_battle_end`（EN） | GitHub 仓库树页 | ✔ `apk/2.2.1/…/assets/sc/` 有**无扩展名的 `.sc` 明文**（QuickBMS 产物）；但版本对不上（ShapeCount/图集尺寸） |')
A('| 3 | `supercell sc file format animation block 0c 08 parser`（EN） | GitHub raw（参考实现）+ 本地工具 | ✔ `tools/probes/sc-anim-index.py` + `.ai-tmp/test/scdfull/sc_decode.py` 可直接解 `12`/`0c`；`0b` 无实现 ⇒ 坐标登记未解出 |')
A('| 4 | `clash royale 2.1.5 apk assets sc ui`（EN） | GitHub raw 单文件 | ✔ **命中**：2.1.5 的 `.sc` 与 `cr-assets-png/*_out` 指纹 100% 吻合 ⇒ 本片权威来源 |')
A('| 5 | `皇室战争 sc 解包 UI 图集 命名 索引`（CN） | 中文站 / 搜索引擎 | ⚠️ 本机无 web 搜索工具（宿主只给了本地检索 + curl），中文轮无法执行 ⇒ **如实登记为「未执行」** |')
A('| 6 | `皇室战争 官方 FanKit UI 组件包`（CN/EN） | GitHub 仓库树页（`fan_kit/`、`cr-fankit-official`） | ✔ 存在 `fan_kit/ui/`：`crowns.png` + `icons_stats_*.png`（13 个**语义命名**图标）；`ui/ui/chest-*.png`（9 种宝箱）、`ui/leagues/league-*.png`（9 个段位）—— 但**不含游戏内的 9-slice 面板/按钮** |')
A('| 7 | `clash royale sprite atlas plist json index ui`（EN） | GitHub 仓库树页（`ui/ui_trim*`、`ui/chests/`、`ui/arenas/`） | ⚠️ `ui/ui_trim_215/` = 2.1.5 `ui_out` 的**裁剪版**（仍只有 `ui_sprite_NNN` 编号，**没有语义名**）⇒ 无更优来源 |')
A('| 8 | `clash royale assets sc hash file`（EN） | GitHub 仓库树页（`.hash` 文件） | ⚠️ `res/sc/` 里 26 个 `.hash` 只是校验/索引哈希串，**不含名称** |')
A('')
A('### 4.2 站点类别（≥3 类）')
A('')
A('| 类别 | 具体入口 | 结果 |')
A('|---|---|---|')
A('| GitHub 仓库树页（HTML embeddedData） | `github.com/smlbiobot/cr/tree/master/...` | 唯一可靠的目录枚举方式（REST API 本机被限流 403/rate-limit；`git clone` TLS `unexpected eof`；HTML 页可用） |')
A('| GitHub raw 单文件 | `raw.githubusercontent.com/smlbiobot/cr/master/...` | ✔ 抓 `.sc` 本体（14 + 12 个） |')
A('| GitHub REST API | `api.github.com/repos/.../contents/res/sc` | ✘ 本 IP 限流（`rate limit exceeded`） |')
A('| GitHub 仓库（另仓） | `smlbiobot/cr-assets-png`（本地已克隆）、`cr-fankit-official` | ✔ PNG dump 与 FanKit；均**无索引文件** |')
A('| 本地本机素材树 | `原版资源/cr-assets-png/assets/sc/*_out`、`原版资源/cr-sfx`、`原版资源/cr-sim` | ✔ 帧来源；`cr-sim` 只有数值/地形，无 UI 索引 |')
A('')
A('### 4.3 尝试过的格式 / 打包方式（≥3 种）')
A('')
A('| 格式 | 结果 |')
A('|---|---|')
A('| `.sc`（iOS ipa 内，`SC`+LZMA 壳） | ✔ 能解（v1.0.0）；**但版本对不上**，只作格式参照 |')
A('| `.sc`（Android，QuickBMS 解密后的裸文件，**无扩展名**） | ✔✔ **本片权威来源**（2.1.5 / 2.2.1） |')
A('| `_tex.sc` / `*_tex.png` 图集 | ✔ 用作**版本指纹**（声明尺寸 vs PNG 尺寸） |')
A('| PNG dump（`*_out/*_sprite_NNN.png`） | ✔ 帧像素；**无任何语义**（这就是"猜"的根源） |')
A('| `.hash` | ✘ 只有哈希串 |')
A('| APK / IPA 本体 | ✘ 不下载（几十 MB~上百 MB；`.sc` 已在仓库里，没必要） |')
A('| plist / json 索引 | ✘ 全仓 + 本地素材树均无 |')
A('')
A('### 4.4 结论：有没有比 `.sc` 更「成套」的来源？')
A('')
A('- **游戏内 UI 图元：没有。** `.sc` 的 Export 表就是原版自己的命名，已是天花板。')
A('  （`ui/ui_trim_215/`、`ui_out/` 都只是同一批图的编号转储，没有任何语义名。）')
A('- **有，但只覆盖少数题材**：`fan_kit/ui/`（13 个统计图标 + crowns）、`ui/ui/chest-*.png`（9 宝箱）、')
A('  `ui/leagues/league-*.png`（9 段位）、`ui/chests/icons|ui`。这些**语义命名**的图可以直接用起来，')
A('  但它们**替代不了**面板 / 按钮 / 条 / 卡槽这些 9-slice 组件 —— 后者的权威身份只能从 `.sc` 得到。')
A('- 因此本片结论：**已穷尽，无更优来源（针对 9-slice UI 组件）**。')

os.makedirs(os.path.join(ROOT, '策划'), exist_ok=True)
open(os.path.join(ROOT, '策划', '原版UI素材名称索引.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('OK 策划/原版UI素材名称索引.md')

# =========================== 更正表 md ===========================
M = []
B = M.append
B('# 原版 UI 图元更正表（现在 → 应该）')
B('')
B('> 依据 = `策划/原版UI素材名称索引.md`（2.1.5 `.sc` 的显式导出表）。')
B('> **本表可直接照改**：`现用帧号` 一律是 `client/Assets/Resources/Sprites/Ui/<用途>/<源图集>/frame_NNN.png` 的 NNN。')
B('')
B('- 核对总数：**65 个** ResPaths 登记的落地图元（`client/**/Sprites/Ui/**` 下另有 2 个未登记的文件，见 §4）')
B('- **判定：命中 %d / 不符 %d / 无法判定 %d（合计 %d）**' % (nh, nb, nk, nh + nb + nk))
B('')
B('## 1. 逐条清单')
B('')
B('| # | 现用途键 | 现用帧号 | 原版命名（导出表显式引用） | 判定 | 建议改用 / 改名 | 依据 |')
B('|---|---|---|---|---|---|---|')
for i, (pur, src, fr, key, vd, act, sug, basis) in enumerate(V):
    nm = '—'
    for e in EX.get(src.replace('_out', '') if src in ('ui_out', 'ui_battle_end_out') else src, []):
        pass
    scn = {'ui_out': 'ui', 'ui_battle_end_out': 'ui_battle_end', 'loading_out': 'loading'}[src]
    hitnames = []
    if fr < len(sids[scn]):
        sid = sids[scn][fr]
        for e in EX[scn]:
            if sid in e['sid_list'] and e['name'] not in hitnames:
                hitnames.append(e['name'])
    nm = ('`%s`' % '`, `'.join(hitnames[:3]) + (' …(%d 条)' % len(hitnames) if len(hitnames) > 3 else '')) if hitnames else '—（该 shape 不被任何 `0c` 动画引用）'
    B('| %d | `%s` | `%s` frame_%03d | %s | **%s** | %s | %s |'
      % (i + 1, key, src, fr, nm, vd, sug, basis))
B('')
B('## 2. 不符（%d 条）——照改' % nb)
B('')
for i, (pur, src, fr, key, vd, act, sug, basis) in enumerate(V):
    if vd != '不符':
        continue
    B('### %s：`%s` frame_%03d' % (key, src, fr))
    B('')
    B('- 实际是：%s' % act)
    B('- 动作：%s' % sug)
    B('- 依据：%s' % basis)
    B('')
B('## 3. `源内无对应` 的用途（找不到同名/同义的原版命名）')
B('')
B('| 用途键 | 源内情况 | 该用途的素材该从哪来 |')
B('|---|---|---|')
B('| `ButtonCapsule`（白色圆角胶囊按钮底框） | `ui.sc` 的 export 里**没有**"白色胶囊"命名；`button_*` 只有 `button_timeline`/`button_small_orange`/`button_small_square_orange`/`button_share_deck`/`full_page_button_tab*` | 白色按钮底用 `ui_out 476`（白色按钮底）或自绘 9-slice；or 用 `ui_battle_end` 的白条 |')
B('| `ElixirBarTrack/Frame/Fill`（圣水条三件） | `ui.sc` 里 `elixir` 命名的只有 `Elixir_bar_drop_anim`/`elixir_float_txt`/`print_elixir_speed`/HUD 相关 clip，**均无 shapeID**（空 clip）⇒ 圣水条本体不在 `ui.sc` 的 shape 表里 | 圣水条是 **HUD 层**（战斗内 UI），本批 `.sc` 里没有 HUD 的图集；若要用原版圣水条，需另找 HUD 素材（`ui.sc` 里的 `HUD_*` 命名只有 clip 无 shape）⇒ **保留现状并登记差异** |')
B('| `IconHeal`（治疗图标） | `ui.sc` 里没有任何 heal 命名 | 保留现状（若确有治疗展示需求），或换 `IconPlus`(521) 语义 |')
B('| `IconGear/Gampad/Chat/ArrowUp/Question`（设置/手柄/聊天/升级/问号） | 视觉一致但**原版无命名引用**（都是静态图形） | 保留现状（视觉已对上，只是原版没给名字） |')
B('| `PanelPaper*`/`PanelFrame*` 的 9-slice 方位 | 原版 `.sc` 对这些 shape **无任何命名引用** ⇒ 哪个件是哪一角在本片无权威依据 | 保留现状，并在 `client/资源欠缺清单.md` 登记「9-slice 方位待原版布局数据确认」 |')
B('')
B('## 4. 附：`Sprites/Ui/**` 下**未登记进 ResPaths** 的落地文件')
B('')
B('| 文件 | 实际是什么 | 建议 |')
B('|---|---|---|')
B('| `Bars/loading_out/frame_015.png` | 绿色圆角条（加载界面进度条底；原版 `loading.sc` 无命名引用；同目录 `loading_bar_bloe` 覆盖 frame 19-23） | 若做加载进度条，改用 `loading_out` frame 19-23（原版 `loading_bar_bloe`）；否则删 |')
B('| `Icons/loading_out/frame_028.png` | **「CLASH ROYALE」整幅 Logo**（520×224，非透明覆盖完整） | 启动/登录页可直接用（原版命名引用为空，但视觉唯一且无歧义） |')
B('')
B('## 5. 本片**没有**做的事（不要当成已完成）')
B('')
B('1. 没有改 `client/**` 一个字（本片只出表）。')
B('2. 没有解出坐标/缩放（`0b` 不存在；`08` 矩阵与 `0c` 三元组的对应关系未验证 ⇒ 未解出）。')
B('3. `ui_chest`/`ui_chest_3d`/`ui_spells`/`ui_arena`/`tutorial`/`arena_training`/`debug`/`spell_goblin_barrel`/`effects` 的 export 表已生成（见索引 md §3），但**未逐条核对视觉**（客户端未引用它们）。')
open(os.path.join(ROOT, '策划', '原版UI图元更正表.md'), 'w', encoding='utf-8').write('\n'.join(M) + '\n')
print('OK 策划/原版UI图元更正表.md')

# ============ 对照表行 ============
C = []
D = C.append
D('> W1 待折入 `策划/对照表.md` 的行（本片不直接改 `对照表.md`，由主 agent 折入）。全部内容来自 `.sc` 显式导出表。')
D('| 项 | 原版来源（权威） | 结论 | 落地 |')
D('|---|---|---|---|')
D('| UI 图元身份 | CR **2.1.5** APK `assets/sc/<name>`（`.sc` 导出表） | 65 个落地图元：命中 %d / 不符 %d / 无法判定 %d | `策划/原版UI素材名称索引.md`、`策划/原版UI图元更正表.md` |' % (nh, nb, nk))
D('| 版本指纹 | `ShapeCount` + `_tex.png` 尺寸 | `cr-assets-png/*_out` = **2.1.5** 解包产物（非 2.2.1，非 v1.0.0） | 同上 §0 |')
D('| `frame_NNN` 语义 | `12` 记录序号（`sid ≠ 序号`） | `frame_NNN` = 第 NNN 条 shape 记录；`sid` 要经 `序号→sid` 才能查 clip | 同上 §0.1 |')
D('| 坐标 | tag `08` 矩阵（36843 条） | **未解出**（`0b` 不存在；`0c` 三元组语义未验证） | 同上 §1.1 |')
open(os.path.join(ROOT, '.ai-tmp', 'test', 'W1-index-draft.md'), 'w', encoding='utf-8').write('\n'.join(C) + '\n')
print('OK .ai-tmp/test/W1-index-draft.md  (W1 rows folded into 策划/对照表.md by AC1)')
print('index md lines:', len(L), 'corr md lines:', len(M))
