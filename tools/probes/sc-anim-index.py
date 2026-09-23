#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sc-anim-index.py  --  R5b 判据资产（判据 = export 名 <-> clip id <-> shapeID 三元组）

用途
----
把 Supercell `.sc` 单位文件的「Export 表 + Animation(0x0C) 表」解出来，产出：
  1) export 名 -> clip id            （**显式引用**：.sc 的 Export 表自带 id 数组）
  2) clip id  -> shapeID 列表         （0x0C 记录自带）
  3) shapeID  -> frame_NNN.png        （shape 记录在文件中的顺序，0 起）
  4) 动画组表（组名 = export 名去掉结尾的 _N）+ 每档位的 frame_NNN 帧号集合

复跑
----
  python tools/probes/sc-anim-index.py \
      --sc-dir    <解包出的 .sc 所在目录> \
      --units     chr_knight,chr_musketeer,chr_archer \
      --frames-root client/Assets/Resources/Sprites/Units \
      --collage-dir .ai-tmp/screenshots \
      --json      .ai-tmp/test/r5/anim-index.json

判据（关键事实，均在本脚本内断言 / 打印）
------------------------------------------
  A. .sc 结构（自 0x00 到 0x00，无遗留未识别 tag）：
       header(6 x u16) + 5 字节 + ExportCount + ExportIDs[u16] + ExportNames[u8 len + bytes]
       然后顺序：01 纹理 / 1a textfield / 12 shape(每 shape 一条) / 0c animation / 00 结束
  B. `parse.py` 里那些 `!! unknown tag 0b` **不是真的 0b 记录** —— 是 `parse.py`
     在处理 0x0C 时没有按 size 吞掉整条 payload，把 0x0C payload 尾部的
     「placement 数组」残余字节当成了下一条 tag。本脚本按 size 吞 payload 后，
     tag 直方图干净（只有 01/1a/12/0c/00），0b 计数 = 0。
  C. ExportIDs 是**显式**的 id 数组，与 ExportNames 一一对应，其值就是 Animation 记录的 ClipID。
     （chr_musketeer / chr_archer 完全 1:1；chr_knight 数据里有一处重复 id=501 且缺 502，
      属于原资产本身的异常，脚本会打印出来，不做美化。）
  D. ShapeCount == frame_NNN.png 的文件数（486/414/252），且 shape 记录在文件中
     的 sid 顺序 == 0..N-1，所以 shapeID 就是 frame 的编号。
"""

import argparse
import collections
import io
import json
import lzma
import os
import sys

# Windows 控制台默认 GBK -> 强制 UTF-8，避免中文报告写盘时报 UnicodeEncodeError
for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass


# ----------------------------------------------------------------------------
# 读取 / 解压
# ----------------------------------------------------------------------------
class R(io.BytesIO):
    def read_byte(self):
        return int.from_bytes(self.read(1), 'little')

    def read_u16(self):
        return int.from_bytes(self.read(2), 'little')

    def read_i16(self):
        return int.from_bytes(self.read(2), 'little', signed=True)

    def read_u32(self):
        return int.from_bytes(self.read(4), 'little')

    def read_i32(self):
        return int.from_bytes(self.read(4), 'little', signed=True)


def lzma_hack(d):
    # supercell 的 lzma 头少了 4 字节 uncompressed-size（补 0）
    d = d[0:9] + b"\x00" * 4 + d[9:]
    return lzma.LZMADecompressor().decompress(d)


def load_sc(fn):
    raw = open(fn, 'rb').read()
    if raw[:2] == b'\x53\x43':                     # 'SC'
        off = 10 + int.from_bytes(raw[6:10], 'big')
    else:
        off = 0
    return lzma_hack(raw[off:])


# ----------------------------------------------------------------------------
# 解析
# ----------------------------------------------------------------------------
def parse_unit(data):
    S = R(data)
    ShapeCount = S.read_u16()
    TotalsAnim = S.read_u16()
    TotalsTex = S.read_u16()
    TextFieldCount = S.read_u16()
    MatrixCount = S.read_u16()
    ColorTransCount = S.read_u16()
    S.read(5)

    ExportCount = S.read_u16()
    exp_ids = [S.read_u16() for _ in range(ExportCount)]
    exp_names = []
    for _ in range(ExportCount):
        n = S.read_byte()
        exp_names.append(S.read(n).decode('utf-8', 'replace'))

    sheets = []
    shapes = []
    clips = []
    unknown = []
    tag_hist = collections.Counter()

    while len(data) - S.tell() > 0:
        pos = S.tell()
        tag = S.read(1).hex()
        size = S.read_u32()
        tag_hist[tag] += 1
        payload = S.read(size)

        if tag == '00':
            continue
        if tag in ('01', '18'):
            B = R(payload)
            B.read_byte()
            sheets.append((B.read_u16(), B.read_u16()))
            continue
        if tag in ('1a', '1e'):
            continue
        if tag == '08':
            continue
        if tag == '12':
            B = R(payload)
            sid = B.read_u16()
            nreg = B.read_u16()
            npts = B.read_u16()
            shapes.append(dict(sid=sid, nreg=nreg, npts=npts))
            continue
        if tag == '0c':
            B = R(payload)
            cid = B.read_u16()
            fps = B.read_byte()
            frames = B.read_u16()
            cnt1 = B.read_i32()
            triples = [(B.read_u16(), B.read_u16(), B.read_u16()) for _ in range(cnt1)]
            cnt2 = B.read_i16()
            sids = [B.read_i16() for _ in range(cnt2)]
            opac = [B.read_byte() for _ in range(cnt2)]
            names = []
            for _ in range(cnt2):
                L = B.read_byte()
                names.append(None if L >= 255 else B.read(L).decode('utf-8', 'replace'))
            clips.append(dict(id=cid, fps=fps, frames=frames, cnt1=cnt1, cnt2=cnt2,
                              sids=sids, shape_names=names, opac=opac,
                              triple_count=len(triples)))
            continue
        unknown.append((pos, tag, size))

    return dict(ShapeCount=ShapeCount, TotalsAnim=TotalsAnim, TotalsTex=TotalsTex,
                TextFieldCount=TextFieldCount, MatrixCount=MatrixCount,
                ColorTransCount=ColorTransCount,
                exp_ids=exp_ids, exp_names=exp_names,
                shapes=shapes, clips=clips, sheets=sheets,
                unknown=unknown, tag_hist=dict(tag_hist))


# ----------------------------------------------------------------------------
# 分析
# ----------------------------------------------------------------------------
def analyse(res):
    """返回 (name->clip 映射, 组表)。"""
    clip_by_id = {c['id']: c for c in res['clips']}

    mapping = []
    for i, (nm, cid) in enumerate(zip(res['exp_names'], res['exp_ids'])):
        c = clip_by_id.get(cid)
        mapping.append(dict(i=i, name=nm, clip_id=cid, found=c is not None,
                            fps=None if c is None else c['fps'],
                            frames=None if c is None else c['frames'],
                            sids=[] if c is None else c['sids']))

    # 组名 = export 名去掉结尾的 _数字
    groups = collections.OrderedDict()
    for m in mapping:
        base = m['name'].rsplit('_', 1)[0]
        groups.setdefault(base, []).append(m)

    out = []
    for base, members in groups.items():
        sids = []
        for m in members:
            sids.extend(m['sids'])
        uniq = sorted(set(sids))
        fps = sorted({m['fps'] for m in members if m['fps']})
        frames = sorted({m['frames'] for m in members if m['frames'] is not None})
        out.append(dict(
            group=base,
            clip_ids=[m['clip_id'] for m in members],
            export_names=[m['name'] for m in members],
            fps=fps,
            frames=frames,
            n_sids=len(sids),
            n_sids_unique=len(uniq),
            sid_min=uniq[0] if uniq else None,
            sid_max=uniq[-1] if uniq else None,
            contiguous=(uniq == list(range(uniq[0], uniq[-1] + 1))) if uniq else False,
            sids=uniq,
        ))
    return mapping, out


def tier_mapping(unit, groups):
    """把组名映射到客户端档位（attack / idle / walk / die）。"""
    def pick(pred):
        hits = [g for g in groups if pred(g['group'])]
        return hits

    def is_blue(g):
        n = g['group']
        return ('enemy_' not in n) and ('_blue_' not in n)

    tiers = {
        'idle': pick(lambda n: n.endswith('idle1') and 'enemy_' not in n and '_blue_' not in n),
        'walk': pick(lambda n: n.endswith('run1') and 'enemy_' not in n and '_blue_' not in n),
        'attack': pick(lambda n: n.endswith('attack1') and 'enemy_' not in n and '_blue_' not in n),
        'die': pick(lambda n: ('die' in n) or ('death' in n)),
    }
    return tiers


# ----------------------------------------------------------------------------
# 拼图（表现类取证：首/中/末帧）
# ----------------------------------------------------------------------------
def _tile(paths, labels, cell=110, cols=None):
    from PIL import Image, ImageDraw
    imgs = []
    for p in paths:
        if os.path.isfile(p):
            try:
                imgs.append(Image.open(p).convert('RGBA'))
            except Exception:
                imgs.append(None)
        else:
            imgs.append(None)
    n = len(paths)
    cols = cols or n
    rows = (n + cols - 1) // cols
    lab = 14
    W = cols * cell
    H = rows * (cell + lab)
    canvas = Image.new('RGBA', (W, H), (32, 32, 32, 255))
    dr = ImageDraw.Draw(canvas)
    for i in range(n):
        r, c = divmod(i, cols)
        x = c * cell
        y = r * (cell + lab)
        im = imgs[i]
        if im is not None:
            k = min(cell / max(1, im.width), cell / max(1, im.height))
            im2 = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))))
            canvas.alpha_composite(im2, (x + (cell - im2.width) // 2, y + (cell - im2.height) // 2))
        else:
            dr.rectangle([x + 2, y + 2, x + cell - 3, y + cell - 3], outline=(200, 60, 60, 255))
        dr.text((x + 3, y + cell + 1), str(labels[i])[:20], fill=(230, 230, 230, 255))
    return canvas


def color_stats(path):
    """非透明像素的 平均RGB + '明显偏蓝'/'明显偏红' 像素占比（客观判断蓝方/红方，不靠肉眼）。"""
    from PIL import Image
    if not os.path.isfile(path):
        return None
    im = Image.open(path).convert('RGBA')
    px = im.load()
    n = 0
    nb = nr = 0
    rs = gs = bs = 0
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a <= 32:
                continue
            rs += r
            gs += g
            bs += b
            n += 1
            if b - r > 25:
                nb += 1
            elif r - b > 25:
                nr += 1
    if n == 0:
        return None
    return dict(n=n, rgb=(rs // n, gs // n, bs // n),
                blue=100.0 * nb / n, red=100.0 * nr / n)


def make_collage(group, frames_dir, out_path):
    """一个动画组一张图：行 = 组内第 1/中/末 条 clip，列 = 该 clip 的 首/中/末 frame。"""
    members = group['_members']
    if not members:
        return None
    idxs = sorted({0, len(members) // 2, len(members) - 1})
    paths, labels = [], []
    for ri in idxs:
        m = members[ri]
        sids = m['sids']
        if not sids:
            picks = []
        elif len(sids) == 1:
            picks = [sids[0]]
        else:
            picks = [sids[0], sids[len(sids) // 2], sids[-1]]
        for si in picks:
            paths.append(os.path.join(frames_dir, 'frame_%03d.png' % si))
            labels.append('%s f%d' % (m['name'].rsplit('_', 1)[-1], si))
    if not paths:
        return None
    img = _tile(paths, labels, cell=110)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    img.save(out_path)
    return out_path


# ----------------------------------------------------------------------------
# 文本输出
# ----------------------------------------------------------------------------
def fmt_ranges(sids):
    if not sids:
        return '-'
    out = []
    start = prev = sids[0]
    for s in sids[1:]:
        if s == prev + 1:
            prev = s
            continue
        out.append((start, prev))
        start = prev = s
    out.append((start, prev))
    return ' '.join(('%d-%d' % (a, b)) if a != b else str(a) for a, b in out)


def selfcheck(root, report, args):
    """一键复检入口（机械自检）：数据自洽 + 文档落地 + 证据可达 + 证据新鲜度。"""
    import glob
    ok = True

    def chk(cond, msg):
        nonlocal ok
        print(('  PASS  ' if cond else '  FAIL  ') + msg)
        if not cond:
            ok = False

    print('---- 1. 数据自洽（每个单位） ----')
    for u, r in sorted(report.items()):
        chk(not r['unknown'], '%s: .sc 无未识别 tag（未识别=%d）' % (u, len(r['unknown'])))
        chk(r['png_count'] == r['ShapeCount'],
            '%s: frame_NNN.png 数(%s) == ShapeCount(%s)' % (u, r['png_count'], r['ShapeCount']))
        chk(r['sids_are_0_to_n'], '%s: shape 记录顺序 == 0..N-1（⇒ shapeID 即 frame 编号）' % u)
        chk(r['ExportCount'] == 54, '%s: export 数 == 54（实际 %d）' % (u, r['ExportCount']))
        chk(len(r['groups']) == 6, '%s: 动画组数 == 6（实际 %d）' % (u, len(r['groups'])))
        alls = set()
        for g in r['groups']:
            alls |= set(g['sids'])
        chk(alls == set(range(r['ShapeCount'])), '%s: 组并集覆盖全部 shape（避免"隐藏段"）' % u)
        bad = [m['name'] for m in r['mapping'] if not m['found']]
        chk(not bad, '%s: 每个 export 名都指向存在的 clip（缺=%s）' % (u, bad or '无'))
        oob = [m['name'] for m in r['mapping']
               for s in m['sids'] if s < 0 or s >= r['ShapeCount']]
        chk(not oob, '%s: 所有 shapeID 都在 [0,ShapeCount) 内（越界=%s）' % (u, set(oob) or '无'))

    print('---- 2. 主干结论（三个目录的档位帧集合） ----')
    for u, exp in (('chr_knight', {'idle': '108-115', 'walk': '0-107', 'attack': '108-115 359-485'}),
                   ('chr_musketeer', {'idle': '117-125', 'walk': '9-116', 'attack': '117-125 324-413'}),
                   ('chr_archer', {'idle': '198-206', 'walk': '126-197', 'attack': '198-251'})):
        r = report.get(u)
        if not r:
            chk(False, '%s: 报告缺失' % u)
            continue
        by = {}
        for g in r['groups']:
            for t, sfx in (('idle', 'idle1'), ('walk', 'run1'), ('attack', 'attack1')):
                n = g['group']
                if n.endswith(sfx) and 'enemy_' not in n and '_blue_' not in n:
                    by[t] = fmt_ranges(g['sids'])
        for t, want in exp.items():
            chk(by.get(t) == want, '%s: %s 帧集合 == %s（实际 %s）' % (u, t, want, by.get(t)))
        has_die = any(('die' in g['group']) or ('death' in g['group']) for g in r['groups'])
        chk(not has_die, '%s: die 组不存在 ⇒ 表里必须填「未找到」' % u)

    print('---- 3. 文档落地与内容 ----')
    tp = os.path.join(root, '策划', '单位帧段表.md')
    gp = os.path.join(root, '策划', '单位动画分组表.md')
    ip = os.path.join(root, '策划', '单位动画索引调研.md')
    for p in (tp, gp, ip):
        chk(os.path.isfile(p), '文档存在: %s' % p)
    doc = open(tp, encoding='utf-8').read() if os.path.isfile(tp) else ''
    body = doc.split('## 附录 A')[0]
    chk('<!' + '--' not in doc, '单位帧段表.md 无 HTML 注释占位')
    # R1 的结论句（`BLOCKED（该目录不要填 AnimRanges）`）只许出现在附录 A 里；正文提到"推翻它"是允许且必需的
    chk('BLOCKED`（该目录不要填' not in body,
        '单位帧段表.md 主结论（附录 A 之前）不含 R1 的「BLOCKED（该目录不要填 AnimRanges）」结论句')
    chk('推翻 R1 的 `BLOCKED`' in body, '单位帧段表.md 主结论明确写出「本版推翻 R1 的 BLOCKED」')
    chk('已被 R5b 推翻' in doc, '单位帧段表.md 明确标注了被推翻的 R1')
    chk('显式引用' in doc, '单位帧段表.md 说明了 export↔clip 是显式引用')
    for u in report:
        chk(u in doc, '单位帧段表.md 含单位 %s' % u)
    gm = open(gp, encoding='utf-8').read() if os.path.isfile(gp) else ''
    for u, r in report.items():
        chk(gm.count('`%s`' % m['name']) == 1 if False else all(('`%s`' % m['name']) in gm for m in r['mapping']),
            '%s: 全表含全部 54 个 export 名' % u)

    print('---- 4. 证据可达 + 新鲜度（产物必须比脚本新） ----')
    script_mt = os.path.getmtime(__file__)
    ev = [os.path.join(root, '.ai-tmp/test/r5/anim-index.json'),
          os.path.join(root, '.ai-tmp/test/r5/r5b.out.txt')]
    for p in ev:
        chk(os.path.isfile(p), '证据存在: %s' % p)
        if os.path.isfile(p):
            chk(os.path.getmtime(p) >= script_mt, '证据比脚本新: %s' % os.path.basename(p))
    cols = sorted(glob.glob(os.path.join(root, '.ai-tmp/screenshots', 'R5b-*.png')))
    chk(len(cols) >= 6, '拼图 >= 6 张（实际 %d）' % len(cols))
    old = [c for c in cols if os.path.getmtime(c) < script_mt]
    chk(not old, '全部拼图比脚本新（过期=%s）' % [os.path.basename(x) for x in old])

    print('---- 5. 临时文件与硬规则 ----')
    bad_dirs = [os.path.join(root, d) for d in ('client/_dev', 'tools', 'Assets')]
    hits = []
    for d in bad_dirs:
        for pat in ('*R5b*', '*probe-0b*', '*r5b-*'):
            hits += glob.glob(os.path.join(d, pat))
    chk(not hits, '项目根/client/_dev/tools/Assets 下无临时产物（命中=%s）' % hits)
    chk(os.path.isfile(os.path.join(root, 'tools/probes/sc-anim-index.py')),
        '判据资产已落 tools/probes/sc-anim-index.py')
    sc_ok = all(os.path.isfile(os.path.join(root, '原版资源/sc', u + '.sc')) for u in report)
    chk(sc_ok, '.sc 持久副本在 原版资源/sc/（复跑命令引用的路径可达）')

    print('---- 结论: %s ----' % ('全部 PASS' if ok else '存在 FAIL'))
    return 0 if ok else 1


def emit_markdown(report, path):
    """把「export 名 -> clip id -> shapeID」全表写成 markdown（判据资产产出，⛔ 不要手改）。"""
    L = []
    L.append('# 单位动画分组表（`.sc` 显式三元组：export 名 → clip id → shapeID）')
    L.append('')
    L.append('> **本文件是生成物，⛔ 不要手改。** 生成器 = `tools/probes/sc-anim-index.py`；')
    L.append('> 复跑命令 = `策划/单位帧段表.md` §0.2 那条，末尾加 `--md 策划/单位动画分组表.md`。')
    L.append('>')
    L.append('> 本表回答：「**这个动画在原版里叫什么、它的 clip id 是多少、它逐帧用哪些 `frame_NNN.png`**」。')
    L.append('> `export 名 ↔ clip id` 取自 `.sc` **Export 表自带的 id 数组** ⇒ **显式引用，不是顺序推断**。')
    L.append('> 档位（idle / walk / attack / die）的映射与 `AnimRanges` 结论在 **`策划/单位帧段表.md`**。')
    L.append('')
    L.append('命名约定（原版自带，⛔ 本表不改名）：')
    L.append('')
    L.append('- **组名** = export 名去掉结尾的 `_N`；**`_N` = 视角（1..9）**；')
    L.append('- **一条 clip = 一个视角下的一段按时序播放的动画**，它的 `shapeID` 列表就是播放顺序；')
    L.append('- `enemy_` / `blue_` 前缀 = 另一套配色（各目录实际是哪一队，见 `单位帧段表.md` §1.4/§2.4/§3.4）。')
    L.append('')
    L.append('`判定依据` 列的含义：`导出表 idx N` = `.sc` Export 表第 N 项（名与 id 同序，**显式**）。')
    L.append('')
    for unit, r in report.items():
        L.append('---')
        L.append('')
        L.append('## %s' % unit)
        L.append('')
        L.append('`%s` ｜ ShapeCount=%d ｜ ExportCount=%d ｜ TotalsAnimations=%d ｜ PNG %d 张'
                 % (os.path.basename(r['sc']), r['ShapeCount'], r['ExportCount'],
                    r['TotalsAnim'], r['png_count']))
        L.append('')
        L.append('FPS（`.sc` timeline fps）：%s ｜ 导出 id 重复：%s ｜ 导出指向不存在的 clip：%s ｜ 无导出名的 clip：%s'
                 % (', '.join(sorted({str(m['fps']) for m in r['mapping'] if m['fps']})),
                    r['dup_ids'] or '无', r['missing_clip_ids'] or '无', r['orphan_clip_ids'] or '无'))
        L.append('')
        L.append('### 动画组表')
        L.append('')
        L.append('| 组名（原版 export 名去 `_N`） | clip id | FPS | timeline 帧数 | 像素帧数 | frame_NNN 区间 | 蓝/红 | 判定依据 |')
        L.append('| --- | --- | --- | --- | --- | --- | --- | --- |')
        for g in r['groups']:
            side = '另一套(`enemy_`/`blue_`)' if ('enemy_' in g['group'] or '_blue_' in g['group']) else '主套(同名)'
            L.append('| `%s` | %d – %d（%d 条） | %s | %s | %d | `%s` | %s | 显式引用：导出表 idx %s |'
                     % (g['group'], g['clip_ids'][0], g['clip_ids'][-1], len(g['clip_ids']),
                        ','.join(str(x) for x in g['fps']),
                        ','.join(str(x) for x in g['frames']),
                        g['n_sids_unique'], fmt_ranges(g['sids']), side,
                        _idx_span(r, g)))
        L.append('')
        L.append('### export 名 → clip id → shapeID 全表（%d 条，逐条）' % len(r['mapping']))
        L.append('')
        L.append('| # | export 名 | clip id | FPS | timeline 帧数 | 像素帧数 | frame_NNN（shapeID）| 蓝/红 | 判定依据 |')
        L.append('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
        for m in r['mapping']:
            side = '另一套' if ('enemy_' in m['name'] or '_blue_' in m['name']) else '主套'
            L.append('| %d | `%s` | %d | %s | %s | %d | `%s` | %s | 导出表 idx %d（显式） |'
                     % (m['i'], m['name'], m['clip_id'], m['fps'], m['frames'],
                        len(m['sids']), fmt_ranges(sorted(m['sids'])) if m['sids'] else '-',
                        side, m['i']))
        L.append('')
    L.append('---')
    L.append('')
    L.append('## 待裁决 / 未解码（详见 `策划/单位帧段表.md` §7）')
    L.append('')
    L.append('1. `chr_musketeer_blue_*` 与 `archer1_enemy_*` 哪一套是红方 —— 像素证据互相矛盾，**不足以判定**。')
    L.append('2. `Knight_idle1_2` / `Knight_idle1_3` 指向同一 clip 501、且 clip 502 缺失（资产自带异常）。')
    L.append('3. `0c` 记录里 `cnt1` 条 `(u16,u16,u16)` placement 三元组的语义 —— **未解码**（决定 timeline 内的精确帧排布）。')
    L.append('4. 其余 45 个 `chr_*_out` 目录 —— **本轮未跑**（解析链通用，改 `--units` 即可，但没跑就没推导）。')
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        f.write('\n'.join(L) + '\n')
    return path


def _idx_span(r, g):
    idxs = [m['i'] for m in r['mapping'] if m['name'].rsplit('_', 1)[0] == g['group']]
    return '%d–%d' % (min(idxs), max(idxs)) if idxs else '-'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sc-dir', required=True)
    ap.add_argument('--units', required=True)
    ap.add_argument('--frames-root', default='client/Assets/Resources/Sprites/Units')
    ap.add_argument('--collage-dir', default=None)
    ap.add_argument('--json', default=None)
    ap.add_argument('--md', default=None, help='把「export->clip->shapeID」全表写成 markdown（生成物，不要手改）')
    ap.add_argument('--selfcheck', action='store_true', help='一键复检：数据自洽 + 文档落地 + 证据可达/新鲜度')
    ap.add_argument('--root', default='.', help='项目根（--selfcheck 用）')
    ap.add_argument('--collage-groups', default='attack1,idle1,run1')
    ap.add_argument('--color', action='store_true', help='打印各动画组的平均 RGB，客观判断蓝/红方')
    ap.add_argument('--strip', default='',
                    help='逗号分隔的 UNIT:export名，把该 clip 的全部 shapeID 按顺序拼成一长条（判「clip 内的 shape 是时间序还是方向集」）')
    args = ap.parse_args()

    want_collage = [s.strip() for s in args.collage_groups.split(',') if s.strip()]
    report = {}

    for unit in [u.strip() for u in args.units.split(',') if u.strip()]:
        sc = os.path.join(args.sc_dir, unit + '.sc')
        res = parse_unit(load_sc(sc))
        mapping, groups = analyse(res)

        frames_dir = os.path.join(args.frames_root, unit + '_out')
        png_n = len([f for f in os.listdir(frames_dir) if f.endswith('.png')]) if os.path.isdir(frames_dir) else -1

        print('=' * 78)
        print('=== %s' % unit)
        print('  ShapeCount=%d  TotalsAnim=%d  TotalsTex=%d  ExportCount=%d'
              % (res['ShapeCount'], res['TotalsAnim'], res['TotalsTex'], len(res['exp_names'])))
        print('  frame_NNN.png 数量=%d   (目录 %s)' % (png_n, frames_dir))
        print('  tag 直方图: %s   未识别记录=%d' % (res['tag_hist'], len(res['unknown'])))
        print('  shapes 解析=%d  sheets=%s  clips 解析=%d' % (len(res['shapes']), res['sheets'], len(res['clips'])))
        # 断言 A/B/D
        assert not res['unknown'], '存在未识别 tag: %s' % res['unknown'][:5]
        sids_in_order = [s['sid'] for s in res['shapes']]
        if sids_in_order == list(range(len(sids_in_order))):
            print('  shape 记录顺序 == 0..%d（即 shapeID 就是 frame_NNN 的 NNN）' % (len(sids_in_order) - 1))
        else:
            print('  !! shape sid 非 0..N-1 顺序，需改用 sid->文件名映射')

        print('  [导出数] %d   [动画记录数(0x0C)] %d' % (len(res['exp_names']), len(res['clips'])))
        dup = [i for i, v in enumerate(res['exp_ids']) if res['exp_ids'].count(v) > 1]
        miss = [c for c in res['exp_ids'] if c not in {x['id'] for x in res['clips']}]
        if dup:
            print('  !! 导出 id 重复: %s' % sorted({res['exp_ids'][i] for i in dup}))
        if miss:
            print('  !! 导出指向不存在的 clip: %s' % sorted(set(miss)))
        orphan = sorted({x['id'] for x in res['clips']} - set(res['exp_ids']))
        if orphan:
            print('  !! 存在无导出名的 clip: %s' % orphan)

        # --- 三元组全表 ---
        print('  ---- export 名 -> clip id -> shapeID（%d 条；显式引用，来自 .sc 的 Export 表） ----' % len(mapping))
        for m in mapping:
            print('   %-32s clip=%-5d fps=%-3s frames=%-3s nSids=%-3d sids=%s'
                  % (m['name'], m['clip_id'], m['fps'], m['frames'], len(m['sids']),
                     fmt_ranges(sorted(m['sids'])) if m['sids'] else '-'))

        # --- 动画组表 ---
        # --- shape 覆盖：组并集是否覆盖全部 shape（决定"是否存在未被任何组引用的隐藏动画段"）---
        _all = set()
        for g in groups:
            _all |= set(g['sids'])
        _missing = sorted(set(range(res['ShapeCount'])) - _all)
        _extra = sorted(_all - set(range(res['ShapeCount'])))
        print('  ---- shape 覆盖 ----')
        print('   组并集 %d / ShapeCount %d ; 未被任何组引用=%s ; 越界=%s'
              % (len(_all), res['ShapeCount'], _missing[:20] or '无', _extra[:20] or '无'))

        print('  ---- 动画组表 ----')
        print('   %-34s %-14s %-4s %-10s %-6s %-6s %-22s'
              % ('组名', 'clip id', 'FPS', 'timeline帧', '#sid', '唯一', 'frame 区间'))
        for g in groups:
            print('   %-34s %-14s %-4s %-10s %-6d %-6d %-22s'
                  % (g['group'], '%d-%d' % (g['clip_ids'][0], g['clip_ids'][-1]),
                     ','.join(str(x) for x in g['fps']),
                     ','.join(str(x) for x in g['frames']),
                     g['n_sids'], g['n_sids_unique'], fmt_ranges(g['sids'])))

        # --- 档位 -> 帧集合 ---
        tiers = tier_mapping(unit, groups)
        print('  ---- 档位 -> frame_NNN 帧号集合（蓝方 = 组名不含 enemy_ / _blue_） ----')
        for t in ('idle', 'walk', 'attack', 'die'):
            hs = tiers[t]
            if not hs:
                print('   %-7s 未找到' % t)
                continue
            for g in hs:
                print('   %-7s %-34s clips %d-%d  frames %d..%d  区间 %s'
                      % (t, g['group'], g['clip_ids'][0], g['clip_ids'][-1],
                         g['sid_min'], g['sid_max'], fmt_ranges(g['sids'])))

        # --- 拼图 ---
        collage_files = []
        if args.collage_dir:
            for g in groups:
                tags = set(want_collage)
                hit = any(g['group'].endswith(t) for t in tags)
                if not hit:
                    continue
                g2 = dict(g)
                g2['_members'] = [m for m in mapping if m['name'].rsplit('_', 1)[0] == g['group']]
                fn = os.path.join(args.collage_dir, 'R5b-%s-%s.png' % (unit, g['group']))
                if make_collage(g2, frames_dir, fn):
                    collage_files.append(fn)
        if collage_files:
            print('  拼图: %s' % collage_files)

        # --- strip：把一个 clip 的全部 shapeID 按顺序拼成一长条 ---
        if args.strip:
            for spec in [x for x in args.strip.split(',') if x.strip()]:
                if not spec.startswith(unit + ':'):
                    continue
                nm = spec.split(':', 1)[1]
                mm = [m for m in mapping if m['name'] == nm]
                if not mm:
                    print('  !! strip: 找不到 export %s' % nm)
                    continue
                m = mm[0]
                paths = [os.path.join(frames_dir, 'frame_%03d.png' % s) for s in m['sids']]
                out = os.path.join(args.collage_dir, 'R5b-%s-%s-strip.png' % (unit, nm))
                img = _tile(paths, ['%d' % s for s in m['sids']], cell=92)
                os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
                img.save(out)
                print('  strip -> %s  (%d 帧: %s)' % (out, len(paths), m['sids']))

        # --- 平均色：客观判断蓝/红 ---
        if args.color:
            print('  ---- 各组前 6 帧配色统计（非透明像素；偏蓝%/偏红% 用于客观判蓝红方） ----')
            for g in groups:
                cs = [c for c in (color_stats(os.path.join(frames_dir, 'frame_%03d.png' % s))
                                  for s in g['sids'][:6]) if c]
                if not cs:
                    continue
                R = sum(c['rgb'][0] for c in cs) // len(cs)
                G = sum(c['rgb'][1] for c in cs) // len(cs)
                B = sum(c['rgb'][2] for c in cs) // len(cs)
                bl = sum(c['blue'] for c in cs) / len(cs)
                rd = sum(c['red'] for c in cs) / len(cs)
                print('   %-34s meanRGB=(%3d,%3d,%3d)  偏蓝=%5.1f%%  偏红=%5.1f%%'
                      % (g['group'], R, G, B, bl, rd))

        report[unit] = dict(
            sc=sc, ShapeCount=res['ShapeCount'], TotalsAnim=res['TotalsAnim'],
            ExportCount=len(res['exp_names']), png_count=png_n,
            tag_hist=res['tag_hist'], unknown=res['unknown'],
            sids_are_0_to_n=(sids_in_order == list(range(len(sids_in_order)))),
            mapping=mapping, groups=groups,
            dup_ids=sorted({res['exp_ids'][i] for i in dup}),
            missing_clip_ids=sorted(set(miss)),
            orphan_clip_ids=orphan,
            tiers={k: [g['group'] for g in v] for k, v in tiers.items()},
            collages=collage_files,
        )
        print()

    if args.json:
        os.makedirs(os.path.dirname(os.path.abspath(args.json)), exist_ok=True)
        with open(args.json, 'w', encoding='utf-8') as f:
            json.dump(report, f, ensure_ascii=False, indent=1)
        print('JSON -> %s' % args.json)

    if args.md:
        print('MD   -> %s' % emit_markdown(report, args.md))

    if args.selfcheck:
        return selfcheck(args.root, report, args)
    return 0


if __name__ == '__main__':
    sys.exit(main())
