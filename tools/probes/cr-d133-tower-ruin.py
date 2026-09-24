#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d133-tower-ruin.py -- D133 判据资产：`building_tower_v215.sc` 的「摧毁 / 废墟」帧归属解析
=============================================================================================
回答的问题（用户判词 7「塔阵亡没废墟」）
----------------------------------------
1. `.sc` 里到底有哪些 export / clip 是 **destroyed / ruin / dead / break** 语义？
2. 每个这样的 export 由**哪些 shape 记录序（= 运行时的 `frame_NNN`）**组成（逐层）？
3. 每个这样的 clip 的第 1 帧放置表（`0x0c` 的 `(childIndex, matrixId, ctId)` 三元组顺序
   = 绘制顺序 + 每层的矩阵缩放/偏移）—— 若废墟是**多层合成**，这就是层配方。
4. 这些帧的 PNG 是否真的在盘上（`building_tower_out/building_tower_sprite_NNN.png`）。

结论落盘：`.ai-tmp/test/CR-D133-tower-ruin.txt`（人类可读）+ `CR-D133-tower-ruin.json`（机器可读）。

复跑
----
  python tools/probes/cr-d133-tower-ruin.py --root .
  python tools/probes/cr-d133-tower-ruin.py --root . --sheet 207,205,204,206,212
"""
import argparse
import collections
import importlib.util
import json
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

# 语义关键词（大小写不敏感）：原版 export / shape 名里出现即视作摧毁/废墟候选
KEYWORDS = ('destroy', 'ruin', 'rubble', 'debris', 'wreck', 'broken', 'break',
            'dead', 'death', 'collapse', 'damage', 'destroyed')


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def parse_triples(data, want_clip):
    """按 cr-t1c-placement.py 的口径解 `0x0c` payload 的 (childIndex, matrixId, ctId) 三元组。"""
    import io

    class RR(io.BytesIO):
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

    S = RR(data)
    # header
    for _ in range(6):
        S.read_u16()
    S.read(5)
    ExportCount = S.read_u16()
    for _ in range(ExportCount):
        S.read_u16()
    for _ in range(ExportCount):
        n = S.read_byte()
        S.read(n)

    mats, cts, out = [], [], {}
    recs = []
    while len(data) - S.tell() > 0:
        pos = S.tell()
        tag = S.read(1).hex()
        size = S.read_u32()
        payload = S.read(size)
        recs.append((pos, tag, size, payload))

    for pos, tag, size, payload in recs:
        if tag == '08':
            mats.append(payload)
        elif tag == '09':
            cts.append(payload)

    def mat_of(idx):
        if idx == 65535 or idx >= len(mats):
            return None
        b = mats[idx]
        v = [int.from_bytes(b[i * 4:i * 4 + 4], 'little', signed=True) for i in range(6)]
        return dict(a=v[0], b=v[1], c=v[2], d=v[3], tx=v[4], ty=v[5],
                    sx=v[0] / 1024.0, sy=v[3] / 1024.0,
                    px=v[4] / 20.0, py=v[5] / 20.0)

    for pos, tag, size, payload in recs:
        if tag != '0c':
            continue
        B = RR(payload)
        cid = B.read_u16()
        if cid not in want_clip:
            continue
        fps = B.read_byte()
        frames = B.read_u16()
        cnt1 = B.read_i32()
        trip = [(B.read_u16(), B.read_u16(), B.read_u16()) for _ in range(max(0, cnt1))]
        cnt2 = B.read_i16()
        sids = [B.read_i16() for _ in range(max(0, cnt2))]
        opac = [B.read_byte() for _ in range(max(0, cnt2))]
        names = []
        for _ in range(max(0, cnt2)):
            L = B.read_byte()
            names.append(None if L >= 255 else B.read(L).decode('utf-8', 'replace'))
        n0 = 0
        while n0 < len(trip) and (n0 == 0 or trip[n0][0] != 0):
            n0 += 1
        first = []
        for k, (ci, mi, ti) in enumerate(trip[:max(1, n0)]):
            first.append(dict(order=k,
                              childIndex=ci,
                              childId=sids[ci] if 0 <= ci < len(sids) else None,
                              childName=names[ci] if 0 <= ci < len(names) else None,
                              matrix=mi, ct=ti,
                              mat=mat_of(mi)))
        out[cid] = dict(clip=cid, fps=fps, frames=frames, cnt1=cnt1, cnt2=cnt2,
                        childTableIds=sids, childTableNames=names, opacity=opac,
                        firstFrame=first)
    return out


def parse_meta_sprites(meta_path):
    """解 `*.png.meta` 的 `spriteSheet.sprites` 列表 → [(name, x, y, w, h), ...]。

    Unity 的 `rect.y` 原点在**贴图左下**、y 向上；PNG 文件自己的第 0 行在**上**。
    本函数**原样返回 meta 里的坐标**（不做翻转），调用方自己折算。
    """
    import io as _io
    if not os.path.isfile(meta_path):
        return None
    txt = _io.open(meta_path, encoding='utf-8', errors='replace').read()
    i = txt.find('spriteSheet:')
    if i < 0:
        return None
    seg = txt[i:]
    out = []
    for b in seg.split('      name: ')[1:]:
        name = b.split('\n', 1)[0].strip()
        j = b.find('        x: ')
        if j < 0:
            continue
        kv = {}
        for ln in b[j:j + 260].split('\n'):
            ln = ln.strip()
            if ': ' in ln:
                k, _, v = ln.partition(': ')
                if k in ('x', 'y', 'width', 'height'):
                    kv[k] = float(v)
                elif k == 'alignment':
                    break
        if len(kv) == 4:
            out.append((name, kv['x'], kv['y'], kv['width'], kv['height']))
    return out


def alpha_bbox(im, thresh=0):
    """在给定图上求 alpha > thresh 的紧包围盒（PNG 行号口径：origin 左上，返回 top/bottom/left/right 含端点）。"""
    a = im.getchannel('A') if im.mode == 'RGBA' else im.convert('RGBA').getchannel('A')
    w, h = a.size
    px = a.load()
    left, right, top, bottom = w, -1, -1, -1
    for yy in range(h):
        row_has = False
        for xx in range(w):
            if px[xx, yy] > thresh:
                if xx < left:
                    left = xx
                if xx > right:
                    right = xx
                row_has = True
        if row_has:
            if top < 0:
                top = yy
            bottom = yy
    if right < 0:
        return None
    return dict(left=left, right=right, top=top, bottom=bottom,
                w=right - left + 1, h=bottom - top + 1)


def band_width(im, bbox, frac, thresh=0):
    """在 bbox 的**底 frac 段**里逐行量非透明列宽，返回 (并集宽, 单行最大宽, 该段行数)。

    口径：bbox 取自 PNG 行号（origin 左上）⇒ "底" = 行号大的那一侧。
    """
    a = im.getchannel('A') if im.mode == 'RGBA' else im.convert('RGBA').getchannel('A')
    px = a.load()
    h = bbox['bottom'] - bbox['top'] + 1
    rows = max(1, int(round(h * frac)))
    y0 = bbox['bottom'] - rows + 1
    u_left, u_right, mx = None, None, 0
    for yy in range(y0, bbox['bottom'] + 1):
        xs = [xx for xx in range(bbox['left'], bbox['right'] + 1) if px[xx, yy] > thresh]
        if not xs:
            continue
        if u_left is None or xs[0] < u_left:
            u_left = xs[0]
        if u_right is None or xs[-1] > u_right:
            u_right = xs[-1]
        if xs[-1] - xs[0] + 1 > mx:
            mx = xs[-1] - xs[0] + 1
    union = 0 if u_left is None else (u_right - u_left + 1)
    return union, mx, rows


# 白件掩膜（本工程 CR-T1h/CR-T1i 一直用的同一口径）：亮 + 近中性偏白
def white_mask(px):
    r, g, b, a = px
    return a > 0 and r > 205 and g > 195 and b > 185


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--unit', default='building_tower')
    ap.add_argument('--sheet', default='', help='逗号分隔的 rec 帧号：出联络图')
    ap.add_argument('--bbox', default='213,211,10,9,207,205,204',
                    help='逗号分隔的 rec 帧号：量 bbox / 底 15%% 带宽（section F）')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'scanim')
    sc = os.path.join(root, '原版资源', 'sc', a.unit + '_v215.sc')
    data = open(sc, 'rb').read()
    res = R.parse_unit(data)

    lines = []
    def P(s=''):
        print(s)
        lines.append(s)

    fdir = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', a.unit + '_out')
    pngs = sorted(f for f in os.listdir(fdir) if f.endswith('.png')) if os.path.isdir(fdir) else []
    P('sc=%s  ShapeCount=%d  exports=%d  clips=%d  PNG=%d'
      % (os.path.basename(sc), res['ShapeCount'], len(res['exp_names']), len(res['clips']), len(pngs)))

    sid2idx = {}
    for i, s in enumerate(res['shapes']):
        sid2idx.setdefault(s['sid'], i)
    clip_by_id = {c['id']: c for c in res['clips']}

    def expand(cid, seen):
        out = []
        c = clip_by_id.get(cid)
        if c is None:
            return out
        for s in c['sids']:
            if s in sid2idx:
                out.append(sid2idx[s])
            elif s in clip_by_id and s not in seen:
                out += expand(s, seen | {s})
        return out

    def hit(text):
        if not text:
            return False
        t = text.lower()
        return any(k in t for k in KEYWORDS)

    P('\n================ A. export 名命中摧毁语义 ================')
    hit_exports = []
    for nm, cid in zip(res['exp_names'], res['exp_ids']):
        if hit(nm):
            recs = sorted(set(expand(cid, {cid})))
            hit_exports.append(dict(export=nm, clip=cid, records=recs))
            P('  %-40s clip=%-5d recs=%s' % (nm, cid, R.fmt_ranges(recs) if recs else '-'))

    P('\n================ B. clip 的 shape 名命中摧毁语义 ================')
    hit_clips = collections.OrderedDict()
    for c in res['clips']:
        names = [n for n in (c['shape_names'] or []) if n]
        exp_hit = [n for n in names if hit(n)]
        # 反查哪些 export 引用了这条 clip
        owners = [nm for nm, cid in zip(res['exp_names'], res['exp_ids']) if cid == c['id']]
        if exp_hit or any(hit(o) for o in owners):
            recs = sorted(set(expand(c['id'], {c['id']})))
            hit_clips[c['id']] = dict(clip=c['id'], fps=c['fps'], frames=c['frames'],
                                      owners=owners, shapeNames=names, records=recs)
            P('  clip=%-5d owners=%-28s frames=%-4s recs=%-24s names=%s'
              % (c['id'], ','.join(owners) or '-', c['frames'],
                 R.fmt_ranges(recs) if recs else '-', exp_hit or names[:6]))

    P('\n================ C. 全 export 一览（含 rec 区间） ================')
    for nm, cid in zip(res['exp_names'], res['exp_ids']):
        recs = sorted(set(expand(cid, {cid})))
        P('  %-40s clip=%-5d recs=%s' % (nm, cid, R.fmt_ranges(recs) if recs else '-'))

    P('\n================ D. 摧毁候选的处理表（第 1 帧放置顺序） ================')
    want = set(hit_clips.keys()) | {int(x) for x in a.sheet.split(',') if x.strip() and x.strip().isdigit()}
    # 文档点名的 4 个 clip id：301 ground1 / 298 ground2 / 297 2v2 / 300 残墙
    doc_clips = set()
    for nm, cid in zip(res['exp_names'], res['exp_ids']):
        if any(k in (nm or '').lower() for k in ('destroy', '2v2_destroyed')):
            doc_clips.add(cid)
    trip_info = parse_triples(data, want | doc_clips | {307, 308, 235, 236}) if want or doc_clips else {}
    for cid, info in sorted(trip_info.items()):
        owners = [nm for nm, c in zip(res['exp_names'], res['exp_ids']) if c == cid]
        P('  clip=%d owners=%s fps=%s frames=%s cnt1=%d cnt2=%d'
          % (cid, ','.join(owners) or '-', info['fps'], info['frames'], info['cnt1'], info['cnt2']))
        P('    childTable ids=%s' % (info['childTableIds'],))
        P('    childTable names=%s' % (info['childTableNames'],))
        for f in info['firstFrame']:
            m = f['mat']
            P('    #%d child[%d] id=%-5s name=%-14s matrix=%-6s ct=%-5s %s'
              % (f['order'], f['childIndex'], f['childId'], f['childName'], f['matrix'], f['ct'],
                 '无矩阵(画布原位)' if m is None else
                 'scale=(%.3f,%.3f) dPx=(%.2f,%.2f)' % (m['sx'], m['sy'], m['px'], m['py'])))

    P('\n================ E. PNG 存在性 ================')
    check = sorted({r for e in hit_exports for r in e['records']}
                   | {r for ci in hit_clips.values() for r in ci['records']}
                   | {int(x) for x in a.sheet.split(',') if x.strip() and x.strip().isdigit()})
    for n in check:
        cands = [p for p in pngs if ('_sprite_%03d.png' % n) in p or ('frame_%03d' % n) in p]
        P('  rec %-4d -> %s' % (n, cands[0] if cands else '!!! 缺 PNG'))

    outdir = os.path.join(root, '.ai-tmp', 'test')
    os.makedirs(outdir, exist_ok=True)

    # ── F. 尺寸量取（废墟 vs 塔基）：给「国王塔用哪一帧 / 公主塔用哪一帧」提供可复算的数 ──
    # 口径说明（⛔ 全节只用两个掩膜，不许换阈值）：
    #   · alpha>0     = 该 PNG 里"有像素"的范围（紧包围盒 / 带宽）
    #   · 白件掩膜    = r>205 & g>195 & b>185（本工程 CR-T1h/CR-T1i 一直用的同一口径）
    # 单位 = **导入后的 PNG 像素**（PPU=100 ⇒ 1 格 = 100 art px）。
    bbox_recs = [int(x) for x in a.bbox.split(',') if x.strip().isdigit()]
    fsec = []
    pngdir_r = os.path.join(root, 'client', 'Assets', 'Resources', 'Sprites', 'Towers', a.unit + '_out')
    try:
        from PIL import Image
    except Exception as e:
        P('\n================ F. 尺寸量取 ================')
        P('  (无 PIL，跳过) %s' % e)
        Image = None
    metrics = {}
    if Image is not None:
        P('\n================ F. 尺寸量取（废墟 vs 塔基；口径见脚本注释） ================')
        for n in bbox_recs:
            p = os.path.join(pngdir_r, 'frame_%03d.png' % n)
            if not os.path.isfile(p):
                P('  rec %-4d !!! 缺 PNG %s' % (n, p))
                continue
            im = Image.open(p).convert('RGBA')
            W, H = im.size
            subs = parse_meta_sprites(p + '.meta') or []
            subs2 = [(nm, x, y, w, h) for (nm, x, y, w, h) in subs
                     if nm.startswith('frame_%03d' % n)]
            ubb = alpha_bbox(im, 0)
            P('  rec %-4d canvas=%dx%d  meta 子精灵=%d' % (n, W, H, len(subs2 or subs)))
            for (nm, x, y, w, h) in (subs2 or subs):
                # meta y 原点在左下 ⇒ PNG 行号 = H − (y + h)
                top = int(round(H - (y + h)))
                sub_im = im.crop((int(x), top, int(x + w), top + int(h)))
                sb = alpha_bbox(sub_im, 0)
                P('      %-16s meta=(x=%g,y=%g,w=%g,h=%g)  画布行=%d..%d  实测alpha紧框=%s'
                  % (nm, x, y, w, h, top, top + int(h) - 1,
                     '-' if sb is None else '%dx%d @(%d,%d)' % (sb['w'], sb['h'], sb['left'], sb['top'])))
            if ubb is None:
                P('      !!! 整图 alpha 全 0')
                continue
            bw_u, bw_mx, bw_rows = band_width(im, ubb, 0.15, 0)
            # 白件宽（整图掩膜，仅参考；塔基/废墟都用 alpha 口径才可比）
            px = im.load()
            wl, wr = None, None
            for yy in range(H):
                for xx in range(W):
                    if white_mask(px[xx, yy]):
                        if wl is None or xx < wl:
                            wl = xx
                        if wr is None or xx > wr:
                            wr = xx
            P('      alpha紧框(整图)= %dx%d @(%d,%d)  底15%%带: 并集宽=%d 单行最大宽=%d(%d行)  白件宽=%s'
              % (ubb['w'], ubb['h'], ubb['left'], ubb['top'], bw_u, bw_mx, bw_rows,
                 '-' if wr is None else (wr - wl + 1)))
            metrics[n] = dict(canvas=[W, H], alpha_bbox=ubb, band_union=bw_u, band_max=bw_mx,
                              band_rows=bw_rows, white_w=None if wr is None else wr - wl + 1,
                              subs=[dict(name=nm, x=x, y=y, w=w, h=h, png_top=int(round(H - (y + h))))
                                    for (nm, x, y, w, h) in (subs2 or subs)])

        # 方案对比：**废墟最大宽（alpha>0 紧框宽） / 该型塔 art 最大宽**。
        # 为什么用"art 最大宽"而不是"白件宽"：废墟是**贴地的一圈碎石/残墙**，外缘对应的是塔**外轮廓**
        # （art 的最大宽），不是垛口白件（白件只是塔顶那一圈）。而"该型塔更大 ⇒ 废墟也更大"是唯一
        # 可用的判据 —— 两帧废墟的**带宽几乎一样**（见上），所以只能比整体宽。
        kb = [metrics[n]['alpha_bbox']['w'] for n in (213, 211) if n in metrics]
        pb = [metrics[n]['alpha_bbox']['w'] for n in (10, 9) if n in metrics]
        rk = metrics.get(205, {}).get('alpha_bbox', {}).get('w')
        rp = metrics.get(207, {}).get('alpha_bbox', {}).get('w')
        if kb and pb and rk and rp:
            kavg = sum(kb) / len(kb)
            pavg = sum(pb) / len(pb)
            A = (rk / kavg, rp / pavg)
            B = (rp / kavg, rk / pavg)
            P('\n  ── 方案对比（废墟最大宽 / 该型塔 art 最大宽；两比值越接近越好） ──')
            P('    塔 art 最大宽: 王塔 [%s] 均=%.1f   公主塔 [%s] 均=%.1f  ⇒ 王/公 = %.3f'
              % (','.join(str(x) for x in kb), kavg, ','.join(str(x) for x in pb), pavg, kavg / pavg))
            P('    废墟最大宽: rec205=%d  rec207=%d  ⇒ 205/207 = %.3f'
              % (rk, rp, rk / float(rp)))
            P('    方案A(王=205/公=207): 王 %.3f  公 %.3f  ⇒ 两比值差 %.3f'
              % (A[0], A[1], abs(A[0] - A[1])))
            P('    方案B(王=207/公=205): 王 %.3f  公 %.3f  ⇒ 两比值差 %.3f'
              % (B[0], B[1], abs(B[0] - B[1])))
            metrics['_choice'] = dict(king_art_w=kb, princess_art_w=pb, rubble205_w=rk,
                                      rubble207_w=rp, ratios_A=A, ratios_B=B,
                                      spread_A=abs(A[0] - A[1]), spread_B=abs(B[0] - B[1]))
    else:
        metrics = {}

    txt = os.path.join(outdir, 'CR-D133-tower-ruin.txt')
    with open(txt, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    js = os.path.join(outdir, 'CR-D133-tower-ruin.json')
    with open(js, 'w', encoding='utf-8') as f:
        json.dump(dict(exports=hit_exports,
                       clips=[v for _, v in hit_clips.items()],
                       triples=trip_info,
                       metrics=metrics), f, ensure_ascii=False, indent=1)
    P('\nTXT -> %s' % txt)
    P('JSON -> %s' % js)

    # 联络图（可选）
    if a.sheet:
        try:
            from PIL import Image, ImageDraw
        except Exception as e:
            P('(无 PIL，跳过联络图) %s' % e)
            return 0
        nums = [int(x) for x in a.sheet.split(',') if x.strip().isdigit()]
        cell, lab = 230, 16
        cols = min(len(nums), 6) or 1
        rows = (len(nums) + cols - 1) // cols
        canvas = Image.new('RGB', (cols * cell, rows * (cell + lab)), (235, 235, 235))
        dr = ImageDraw.Draw(canvas)
        name_fmt = 'frame_%03d.png' if os.path.isfile(os.path.join(fdir, 'frame_000.png')) else (a.unit + '_sprite_%03d.png')
        for i, num in enumerate(nums):
            p = os.path.join(fdir, name_fmt % num)
            if not os.path.isfile(p):
                continue
            im = Image.open(p).convert('RGBA')
            bg = Image.new('RGB', im.size, (255, 255, 255))
            bg.paste(im, (0, 0), im)
            k = min(cell / im.width, cell / im.height)
            im2 = bg.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
            x = (i % cols) * cell
            y = (i // cols) * (cell + lab)
            canvas.paste(im2, (x + (cell - im2.width) // 2, y + (cell - im2.height) // 2))
            dr.text((x + 3, y + cell + 1), 'frame %d' % num, fill=(0, 0, 0))
        sheet = os.path.join(outdir, 'CR-D133-tower-ruin-sheet.png')
        canvas.save(sheet)
        P('SHEET -> %s' % sheet)
    return 0


if __name__ == '__main__':
    sys.exit(main())
