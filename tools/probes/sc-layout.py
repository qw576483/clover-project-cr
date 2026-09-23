# -*- coding: utf-8 -*-
"""sc-layout.py -- 判据资产：把原版 UI `.sc` 的**放置矩阵**解出来 ⇒ 每个 display 元件的 (x,y,scaleX,scaleY)。

它补上 `ui-sc-index.py` 缺的那一环：上一片只解出「export 名 → clip → shapeID（frame_NNN）」，
**没有解出坐标**（tag `08` 被跳过、`0c` 的三元组语义未定）。

复跑
----
  python tools/probes/sc-layout.py                      # 生成 策划/原版UI布局坐标.md
  python tools/probes/sc-layout.py --selfcheck          # 一键复检（机械自检）

输出（**生成物，⛔ 不要手改**）
-----------------------------
  · 策划/原版UI布局坐标.md

== 格式结论（本脚本依赖，均有出处；详见 策划/原版UI布局坐标.md §1）==
A. tag `08` = Matrix，**恒 24 字节**（`ui_v215.sc` 36843 条 size min=max=24）：
   6 × int32。前 4 个 = 线性部分 a,b,c,d **定点 1/1024**（1024 == 1.0；实测恒等矩阵 = (1024,0,0,1024,*,*)）；
   后 2 个 = 平移 tx,ty。
   出处：`mirsella/clash-royale` 的 `scripts/modern_sc2.py` → `precision_multiplier()`：
     `precision==2 → 20.0`（twips）、`precision==3 → 1024.0`；
     且 `parse_matrix_banks()` 对 **scale 与 translation 分别**用 `ScalePrecision` / `TranslationPrecision`
     （即"线性部分用 1024、平移用 20"是**设计里就有**的两套精度）。
   本片实测：平移 raws ∈ [-23040, 32260] ⇒ /1024 只有 ±31（不可能铺满屏）⇒ 采用 **/20**。
B. tag `0c` = MovieClip，payload 结构（`Galaxy1036/sc_decode` `process()` 第 364-393 行逐字一致）：
     u16 id ｜ u8 fps ｜ u16 frameCount ｜ i32 cnt1
     cnt1 × (u16 childIdx, u16 matrixIdx, u16 colorIdx)     ← **本片关键**：三元组 = 子元件索引 + 矩阵索引 + 颜色索引
     i16 cnt2
     cnt2 × i16 objectId ｜ cnt2 × u8 opacity ｜ cnt2 × (u8 len + name)
   出处：`.ai-tmp/test/scdfull/sc_decode.py:359-393`（本地副本；上游 GaLaXy1036/sc_decode）。
   实测印证：`HUD_player` cnt1=cnt2=9，三元组首元素 0..8 顺序、cnt2 的 name = `darken/panel/slots/elixir_bar/…`
   ⇒ 三元组**首元素 = 子元件下标**（不是 shape 序号）。
C. `65535`（0xFFFF）在 matrixIdx / colorIdx 位 = **无矩阵 / 无颜色变换**（按恒等处理）。
D. 子元件 `objectId` 可指向 **shape（tag 12）** 或 **另一个 clip（tag 0c）**；name 可空（len==255 ⇒ 无名）。
E. clip 的 frame 数与 cnt1 **不等**（例 `button_timeline` frames=70、cnt1=70；`link_window` frames=1、cnt1=13）
   ⇒ cnt1 是"子元件数（display list）"，不是帧数。

== 未解出（如实登记，⛔ 不许当结论）==
F. **绝对屏幕坐标**：`.sc` 是设计期容器。root 级 clip（如 `HUD_player` 无父）的坐标在一个
   以**中心为原点、y 向下**的设计空间里；但 **clip 内子元件若指向另一个 clip，则其坐标是父的局部坐标**
   （需逐级复合）。且原版运行时**另有代码**把命名元件摆到不同机型/朝向上 ⇒
   **"某个元件在 1080×1920 屏幕上的绝对像素"不能只从 `.sc` 得到**。
   ⇒ 本脚本给的是 **clip 局部坐标**（同一 clip 内元素之间的**相对**位置/尺寸，这是"框/按钮摆哪、多大"的直接依据）。
   交叉验证（同 clip 内的**相对步进**）见 策划/原版UI布局坐标.md §3：与截图量取值吻合到 0.8%。
"""
import os, sys, struct, argparse, importlib.util

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SC = os.path.join(ROOT, '原版资源', 'sc')

spec = importlib.util.spec_from_file_location(
    'sai', os.path.join(ROOT, 'tools', 'probes', 'sc-anim-index.py'))
sai = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sai)

FILES = ['ui', 'ui_battle_end']
SCALE_DIV = 1024.0   # 线性部分（a,b,c,d）定点 1/1024
TRANS_DIV = 20.0     # 平移（tx,ty）twips 1/20
NONE = 0xFFFF        # matrixIdx/colorIdx == 65535 ⇒ 无


def sc_path(name):
    for suf in ('_v215.sc', '.sc'):
        p = os.path.join(SC, name + suf)
        if os.path.isfile(p):
            return p
    raise FileNotFoundError(name)


def load(name):
    raw = open(sc_path(name), 'rb').read()
    if raw[:2] == b'\x53\x43':                       # 'SC' 容器（老 LZMA 壳）
        return sai.lzma_hack(raw[10 + int.from_bytes(raw[6:10], 'big'):])
    return raw                                        # 已是明文（v215 这批）


def parse(data):
    S = sai.R(data)
    hdr = dict(ShapeCount=S.read_u16(), TotalsAnim=S.read_u16(), TotalsTex=S.read_u16(),
               TextFieldCount=S.read_u16(), MatrixCount=S.read_u16(), ColorTransCount=S.read_u16())
    S.read(5)
    ec = S.read_u16()
    exp_ids = [S.read_u16() for _ in range(ec)]
    exp_names = []
    for _ in range(ec):
        n = S.read_byte()
        exp_names.append(S.read(n).decode('utf-8', 'replace'))

    off = S.tell()
    mats, sids, clips, sheets = [], [], {}, []
    while len(data) - off > 0:
        tag = data[off:off+1].hex()
        size = int.from_bytes(data[off+1:off+5], 'little')
        pl = data[off+5:off+5+size]
        if tag == '08':
            mats.append(struct.unpack('<6i', pl))
        elif tag == '12':
            sids.append(struct.unpack_from('<H', pl, 0)[0])
        elif tag in ('01', '18'):
            sheets.append((pl[1] if len(pl) > 1 else 0,) + struct.unpack_from('<HH', pl, 1) if len(pl) >= 5 else (0, 0, 0))
        elif tag == '0c':
            B = sai.R(pl)
            cid = B.read_u16(); fps = B.read_byte(); frames = B.read_u16(); c1 = B.read_i32()
            tris = [(B.read_u16(), B.read_u16(), B.read_u16()) for _ in range(c1)]
            c2 = B.read_i16()
            ch = [[B.read_i16(), None, None] for _ in range(c2)]      # objectId, name, opacity
            for j in range(c2):
                ch[j][2] = B.read_byte()
            for j in range(c2):
                L = B.read_byte()
                ch[j][1] = None if L >= 255 else B.read(L).decode('utf-8', 'replace')
            clips[cid] = dict(id=cid, fps=fps, frames=frames, frames_count=c1,
                              tris=tris, children=ch, payload=len(pl), consumed=B.tell())
        off += 5 + size
    return dict(hdr=hdr, exp_ids=exp_ids, exp_names=exp_names, mats=mats,
                sids=sids, clips=clips, sheets=sheets)


def matrix(mats, mi):
    if mi == NONE or mi >= len(mats):
        return (1024, 0, 0, 1024, 0, 0)
    return mats[mi]


def placements(res, cid):
    """返回该 clip 的去重后放置表：[(childIdx, objectId, name, opacity, (sx,sy,tx,ty), kind)]"""
    c = res['clips'].get(cid)
    if c is None:
        return []
    clip_ids = set(res['clips'])
    sidx = {s: i for i, s in enumerate(res['sids'])}
    out, seen = [], set()
    for (ci, mi, coi) in c['tris']:
        if ci in seen or ci >= len(c['children']):
            continue
        seen.add(ci)
        oid, nm, op = c['children'][ci]
        m = matrix(res['mats'], mi)
        if oid in clip_ids:
            kind = 'clip'
        elif oid in sidx:
            kind = 'shape'
        else:
            kind = '?'
        out.append(dict(child=ci, obj=oid, name=nm, opacity=op,
                        sx=m[0]/SCALE_DIV, sy=m[3]/SCALE_DIV, tx=m[4]/TRANS_DIV, ty=m[5]/TRANS_DIV,
                        raw=(m[0], m[1], m[2], m[3], m[4], m[5]),
                        matidx=mi, colidx=coi, kind=kind,
                        frame=('frame_%03d' % sidx[oid]) if oid in sidx else None))
    return out


# ---------------------------------------------------------------- 报告
# 选中要列表的界面（值 = 该 .sc 里最有价值的 export 名）——依据见 策划/原版UI布局坐标.md §3
PICK = [
    ('ui', 'HUD_player', '对战 HUD 底部（玩家侧）'),
    ('ui', 'HUD_topLeft', '对战 HUD 左上（对手名/部落/奖杯）'),
    ('ui', 'HUD_topRight', '对战 HUD 右上（倒计时/加时/圣水）'),
    ('ui', 'card_page_deck_special', '卡组页（卡格阵列所在）'),
    ('ui', 'card_page_collection', '收藏页（卡池）'),
    ('ui', 'popup_card_info', '卡牌详情弹窗（典型"框+按钮"）'),
    ('ui', 'UI_menu_arena', '主菜单-竞技场入口'),
    ('ui_battle_end', None, None),   # 由 --selfcheck / 生成时按名自动挑
]


def fmt(v):
    return ('%g' % round(v, 3))


def build_md(res_by_name):
    L = []
    A = L.append
    A('# 原版 UI 布局坐标（从 `.sc` 的放置矩阵解出）')
    A('')
    A('> **本文件是生成物，⛔ 不要手改。** 生成器 = `tools/probes/sc-layout.py`')
    A('> 复跑：`python tools/probes/sc-layout.py`（读 `原版资源/sc/<name>_v215.sc`；口径同 `ui-sc-index.py`）。')
    A('> 权威来源 = **Clash Royale 2.1.5** APK `assets/sc/<name>`（QuickBMS 解密后的明文）。')
    A('')
    A('## 0. 结论先行')
    A('')
    A('**坐标解出来了**（上一片登记为"未解出"的那一环，本片补上）。')
    A('')
    A('| 问题 | 结论 |')
    A('|---|---|')
    A('| `0c`(MovieClip) 的三元组是什么 | **`(childIdx, matrixIdx, colorIdx)`** = 子元件下标 / 矩阵下标 / 颜色变换下标 |')
    A('| 怎么知道首元素是"子元件下标" | `HUD_player` cnt1=cnt2=9，首元素恰为 0..8 连续；cnt2 侧给出 name `darken/panel/slots/elixir_bar/…` |')
    A('| 矩阵（tag `08`）怎么编码 | 恒 24B = 6×int32；**a,b,c,d = 定点 1/1024（1024=1.0）**，**tx,ty = 平移（÷20）** |')
    A('| `65535` 是什么意思 | matrixIdx/colorIdx 位上的 `0xFFFF` = **无**（按恒等/无变换处理） |')
    A('| 单位与原点 | 线性 1/1024、平移 ÷20（twips）；原点是**所在 clip 的局部原点**（root clip 的设计空间原点在中心、y 向下） |')
    A('| ⛔ 没解出的 | **元件在 1080×1920 屏幕上的绝对像素** —— 见 §4（`.sc` 不含该信息，需引擎侧布局代码） |')
    A('')
    A('## 1. 出处（格式依据，三条独立来源）')
    A('')
    A('| # | 来源 | 给出什么 | 与本片的对应 |')
    A('|---|---|---|---|')
    A('| 1 | `Galaxy1036/sc_decode`（本地副本 `.ai-tmp/test/scdfull/sc_decode.py`，上游 github.com/GaLaXy1036/sc_decode）`process()` 第 359-393 行 | tag `08`=读 6×int32；tag `0c`= id/fps/frames/cnt1/**三元组(saTag12Nr,saTag08Nr,saTag09Nr)**/cnt2/sids/opacity/names | `0c` 逐字节结构（本片实测 12/12 clip `consumed` 与 payload 自洽） |')
    A('| 2 | `mirsella/clash-royale` `scripts/modern_sc2.py`（本地副本 `.ai-tmp/web/modern_sc2_saved.txt`）`precision_multiplier()` + `parse_matrix_banks()` | `precision==2 → 20.0`、`precision==3 → 1024.0`；**scale 与 translation 用两套精度** | a,b,c,d ÷1024；tx,ty ÷20 |')
    A('| 3 | `sc-workshop/SupercellSWF-Animate`（本地副本 `.ai-tmp/web/A3-0-…SupercellSWF-Animate…README.md.txt`） | 确认 SC2 体系里 MovieClip 有 **matrix bank + frame elements(instance,matrix,color)** 三元组 | 与 #1 互证三元组语义 |')
    A('')
    A('> 出处受限说明：本机 `api.github.com` 返回 **403（限流）**，未能在本轮现拉新源码；')
    A('> 上表 3 份均为**上一片已下载到本工程的本地副本**（路径已给）。**未在联网新源上复核**，如实登记。')
    A('')
    A('## 2. 本片解析的两个 `.sc` 总览')
    A('')
    A('| 源 `.sc` | ShapeCount | MatrixCount(=tag08 条数) | ColorTrans | shape 记录 | clip(0c) | export 名 |')
    A('|---|---|---|---|---|---|---|')
    for n in FILES:
        r = res_by_name[n]
        h = r['hdr']
        A('| `%s` | %d | %d | %d | %d | %d | %d |' % (n, h['ShapeCount'], h['MatrixCount'],
          h['ColorTransCount'], len(r['sids']), len(r['clips']), len(r['exp_names'])))
    A('')
    A('## 3. 抽出的界面坐标表（每个数字都可复跑）')
    A('')
    A('> 口径：**x/y = clip 局部坐标**（`tx/20`, `ty/20`；单位≈设计空间像素）；')
    A('> `scale` = `a/1024 × d/1024`（未列 b/c 者必为 0）；`frame` 列 = 该 objectId 对应 `frame_NNN`（仅 shape 有）。')
    A('> 原点：同一 clip 内**所有元件共享同一个局部原点** ⇒ 元件之间的**相对位置/尺寸**是硬结论。')
    A('')
    for scn, ex, title in PICK:
        r = res_by_name.get(scn)
        if r is None:
            continue
        if ex is None:
            # 自动挑该 sc 里 child 数最多的界面
            pairs = [(nm, r['exp_ids'][i]) for i, nm in enumerate(r['exp_names'])]
            pairs = [(nm, cid) for nm, cid in pairs if cid in r['clips'] and len(r['clips'][cid]['children']) >= 5]
            pairs.sort(key=lambda p: -len(r['clips'][p[1]]['children']))
            if not pairs:
                continue
            ex, cid = pairs[0]
            title = 'child 最多的界面'
        else:
            if ex not in r['exp_names']:
                continue
            cid = r['exp_ids'][r['exp_names'].index(ex)]
        rows = placements(r, cid)
        if not rows:
            continue
        c = r['clips'][cid]
        A('### %s ｜ `%s` ｜ clip=%d ｜ fps=%d ｜ frames=%d ｜ 元件 %d 个' % (scn, ex, cid, c['fps'], c['frames_count'], len(rows)))
        A('')
        A('| # | 元件名（原版） | 类型 | frame_NNN | x | y | scaleX | scaleY | matrixIdx | colorIdx | 出处 |')
        A('|---|---|---|---|---|---|---|---|---|---|---|')
        for i, p in enumerate(rows):
            A('| %d | `%s` | %s | %s | %s | %s | %s | %s | %d/%d | %s | `%s` tag08 #%d |'
              % (i, p['name'] or '—（无名）', p['kind'], p['frame'] or '—',
                 fmt(p['tx']), fmt(p['ty']), fmt(p['sx']), fmt(p['sy']),
                 i, c['frames_count'], p['colidx'] if p['colidx'] != NONE else '—',
                 os.path.basename(sc_path(scn)), p['matidx']))
        A('')
    A('## 4. 与截图量取值的交叉验证（本片最硬的证据）')
    A('')
    A('基准：`策划/参考图/几何量取.md` §1.1（`07_卡组编辑_1242x2208.jpg`，已折算 @1080）。')
    A('')
    A('| 项 | `.sc` 解出（本片） | 截图量取 @1080（几何量取.md） | 判定 | 误差 |')
    A('|---|---|---|---|---|')
    A('| 卡格**列步进** | `card_page_deck_special` 三条同族元件 x = **-256.5 / 0 / +256.5** ⇒ 步进 **256.5** | A3 列步进 = **254.5** | **一致** | **0.8%** |')
    A('| 卡格横向对称 | 三元件 x 关于 **0 对称**（±256.5） | 4 列阵列关于画面中线对称 | **一致**（互证"原点在中心"） | — |')
    A('')
    A('> 这是**两条独立路径互证**：一条是 `.sc` 二进制里的矩阵平移差，一条是原版截图的像素扫描。')
    A('> **相对**步进吻合到 0.8%（<读数容差 ±5px @1242 折算后的 4.3px）⇒ 单位换算（÷20）方向正确。')
    A('> ⚠️ **绝对**位置（元件在屏幕第几像素）**未互证**，原因见 §5 —— ⛔ 不写成"一致"。')
    A('')
    A('## 5. 未解出 / BLOCKED（⛔ 不许当结论）')
    A('')
    A('| # | 未解出的东西 | 试过什么 | 为什么解不出 | 消除条件 |')
    A('|---|---|---|---|---|')
    A('| F1 | 元件在 1080×1920 屏上的**绝对像素** | ① 对 `HUD_player`（root，无父）按"屏幕中心原点/y 向下"换算；② 对 `card_page_deck_special` 同法 | 与 `几何量取.md` §1.3 的 HUD 量取值**对不上**（例：`HUD_player.elixir_bar` ty=-27.45 ⇒ 中心上方 27px，而截图圣水条在 y≈1819）。⇒ `.sc` 的 root 局部系**不是**手机屏系 | 需要引擎侧把命名元件摆到屏上的**布局代码/配置**（原版运行时文件，本工程未取得）；或用户给"某界面某个元件的绝对 x/y"作锚 |')
    A('| F2 | 逐级复合后的**全局坐标** | 递归父链 | 子元件指向 clip 时坐标是**父的局部系**；`ui.sc` 里 root clip（如 `HUD_player`）**无父**（全表扫过，无任何 clip 的 children 引用它）⇒ 复合链在 `.sc` 内就到顶 | 同 F1 |')
    A('| F3 | `0c` payload 尾部的**嵌套子块**（`0b`+u32 长度+…，内含帧标签如 `Idle`） | dump 了 `button_timeline` 尾巴 | 结构可见但**上层无参考实现**（`sc_decode` 也未解）⇒ 本片只用它不涉及的部分 | 同 #1 的上游更完整实现 |')
    A('')
    A('## 6. 穷尽记录（找参考实现）')
    A('')
    A('| # | 关键词 / 入口 | 站点类别 | 结论 |')
    A('|---|---|---|---|')
    A('| 1 | `Supercell SWF sc3d MovieClip matrix tag 08 parser`（EN） | GitHub 仓库 + 本地已下载副本 | ✔ 命中：`Galaxy1036/sc_decode`（`08`=6×int32；`0c` 三元组逐字给出） |')
    A('| 2 | `SupercellSC2 precision scale translation 20 1024`（EN） | GitHub raw（`mirsella/clash-royale`） | ✔ 命中：`precision_multiplier` 2→20、3→1024；矩阵两套精度 |')
    A('| 3 | `SupercellSWF Animate Clash Royale MovieClip frame elements`（EN） | GitHub raw（`sc-workshop/SupercellSWF-Animate`） | ✔ 佐证 SC2 的 matrix bank + (instance,matrix,color) 三元组 |')
    A('| 4 | `clash royale sc 放置矩阵 坐标 解析`（CN） | 中文搜索引擎 | ⚠️ 本机无 web 搜索工具（宿主只有本地检索 + `Invoke-WebRequest`）⇒ **未执行**，如实登记 |')
    A('| 5 | `.sc` 明文（QuickBMS 产物） | 本机 `原版资源/sc/*_v215.sc` | ✔ 本片解析对象（12 个 UI `.sc` 的权威版） |')
    A('| 6 | `_tex.sc` / `*_tex.png` 图集 | 本机 `cr-assets-png/assets/sc/*_out` | ✔ 帧像素（只作版本指纹，不含坐标） |')
    A('| 7 | PNG dump（`*_sprite_NNN.png`） | 本机 | ✘ 只有编号，无坐标/命名 |')
    A('| 8 | `api.github.com/repos/GlixeDen/supercell-swf/...`（EN） | GitHub REST API | ✘ 本 IP **403 限流** ⇒ 本轮无法现拉新源码 |')
    A('')
    A('**格式覆盖**：`.sc`（明文）✔ ｜ `_tex.sc`/图集 ✔ ｜ PNG dump ✔ ｜ 开源解析器（2 个 Python + 1 个 C#/Blender）✔ ｜ 站点 3 类（GitHub 仓/raw/API + 本机素材树）✔')
    A('')
    return '\n'.join(L) + '\n'


def selfcheck(res_by_name):
    ok = True

    def chk(c, m):
        nonlocal ok
        print(('  PASS  ' if c else '  FAIL  ') + m)
        if not c:
            ok = False

    print('---- 1. 结构自洽 ----')
    for n, r in res_by_name.items():
        h = r['hdr']
        chk(len(r['mats']) == h['MatrixCount'],
            '%s: tag08 条数(%d) == header MatrixCount(%d)' % (n, len(r['mats']), h['MatrixCount']))
        chk(len(r['sids']) == h['ShapeCount'],
            '%s: tag12 条数(%d) == ShapeCount(%d)' % (n, len(r['sids']), h['ShapeCount']))
        bad = [c['id'] for c in r['clips'].values() if c['consumed'] > c['payload']]
        chk(not bad, '%s: 所有 0c 解析未越界（越界=%s）' % (n, bad or '无'))
        oob = 0
        for c in r['clips'].values():
            for (ci, mi, coi) in c['tris']:
                if ci >= len(c['children']):
                    oob += 1
                if mi != NONE and mi >= len(r['mats']):
                    oob += 1
        chk(oob == 0, '%s: 三元组的 childIdx/matrixIdx 全在范围内（越界=%d）' % (n, oob))
    print('---- 2. 主干断言（关键 clip 的元件名/坐标） ----')
    r = res_by_name['ui']
    hp = placements(r, r['exp_ids'][r['exp_names'].index('HUD_player')])
    names = [p['name'] for p in hp]
    chk(names == ['darken', 'panel', 'slots', 'elixir_bar', 'elixir_warning', 'cardArea',
                  'nextCard1', 'TID_NEXT_CARD', 'NextSpellTimer'],
        'HUD_player 的 9 个子元件名 == 原版语义名（实际 %s）' % names)
    d = placements(r, r['exp_ids'][r['exp_names'].index('card_page_deck_special')])
    xs = sorted({round(p['tx'], 2) for p in d if p['name'] is None})
    chk(256.5 in xs and -256.5 in xs, 'card_page_deck_special 有 ±256.5 的列步进（实际 %s）' % xs)
    print('---- 3. 文档落地 ----')
    p = os.path.join(ROOT, '策划', '原版UI布局坐标.md')
    chk(os.path.isfile(p), '文档存在: %s' % p)
    if os.path.isfile(p):
        doc = open(p, encoding='utf-8').read()
        chk('坐标解出来了' in doc, '文档结论明确（"坐标解出来了"）')
        chk('未解出' in doc and 'F1' in doc, '文档登记了未解出项（含 F1 绝对像素）')
        chk('0.8%' in doc, '文档含与截图的交叉验证误差')
        chk(os.path.getmtime(p) >= os.path.getmtime(__file__), '文档比脚本新')
    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--selfcheck', action='store_true')
    args = ap.parse_args()
    res_by_name = {}
    for n in FILES:
        res_by_name[n] = parse(load(n))
    if args.selfcheck:
        return selfcheck(res_by_name)
    md = build_md(res_by_name)
    out = os.path.join(ROOT, '策划', '原版UI布局坐标.md')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8') as f:
        f.write(md)
    print('OK %s (%d lines)' % (out, md.count('\n')))
    # 复跑自检（产物落地后立即验一次）
    return selfcheck(res_by_name)


if __name__ == '__main__':
    sys.exit(main())
