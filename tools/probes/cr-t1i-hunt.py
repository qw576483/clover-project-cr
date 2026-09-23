#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1i-hunt.py -- CR-T1i 判据资产：在**全部** `.sc` 里找"常态公主塔"的塔体 art
====================================================================================
回答的问题
----------
1. 除 `building_tower_v215.sc` 外，还有哪个 `.sc` 里有塔类 clip（名字含 tower 之类）？
2. 这些 clip 引用的 **PNG 记录序** 对应的精灵**非透明宽高**是多少（用 100 PPU 换算成"格"）？
   筛子：原版公主塔垛口宽量到 **1.85~1.91 格**（`03_对局_1320x2868.jpg`，91.5 px/格）
   ⇒ 若某 art 在 PPU=100、scale=1 下宽 ≈ 190~200 px，则它就是"按 1:1 放就是原版尺寸"的那套。

为什么需要它
------------
CR-T1h 量出：我方**公主塔**用的 art = 王塔那套（198~201 px 宽 ⇒ ×1.6 = 3.17 格），
而原版公主塔垛口只有 1.85~1.91 格 ⇒ **公主塔 art 拿错了**（或漏了一个更小的 export）。
本脚本把候选从"全部 106 个 .sc"里筛出来，不靠猜。

复跑
----
  python tools/probes/cr-t1i-hunt.py --root .
  python tools/probes/cr-t1i-hunt.py --root . --unit building_tower      # 只看某个 unit 的全部 export
  python tools/probes/cr-t1i-hunt.py --root . --records building_tower 7,8,9,10,11   # 出这些记录的联络图
"""
import argparse
import importlib.util
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

KW = ('tower', 'Tower', 'TOWER', 'building', 'Building', 'arena', 'castle')


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def png_dir(root, unit):
    return os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', unit + '_out')


def png_bbox(path):
    try:
        from PIL import Image
        import numpy as np
    except Exception:
        return None
    if not os.path.isfile(path):
        return None
    arr = np.asarray(Image.open(path).convert('RGBA'))
    ys, xs = np.nonzero(arr[:, :, 3] > 8)
    if not len(xs):
        return (arr.shape[1], arr.shape[0], 0, 0)
    return (arr.shape[1], arr.shape[0],
            int(xs.max()) - int(xs.min()) + 1, int(ys.max()) - int(ys.min()) + 1)


def rec_bbox(root, unit, rec):
    d = png_dir(root, unit)
    for fmt in (unit + '_sprite_%03d.png', 'frame_%03d.png'):
        p = os.path.join(d, fmt % rec)
        if os.path.isfile(p):
            return png_bbox(p)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--unit', default='', help='只详列这个 unit 的全部 export')
    ap.add_argument('--records', default='', help='unit:r1,r2,... —— 出这些记录的联络图')
    ap.add_argument('--keyword', default='tower')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'scanim')

    scdir = os.path.join(root, '原版资源', 'sc')
    files = sorted(f for f in os.listdir(scdir) if f.endswith('.sc'))
    print('扫 %d 个 .sc，关键词 = %r' % (len(files), a.keyword))

    hits = {}
    cache = {}
    for fn in files:
        unit = os.path.splitext(fn)[0]
        unit = unit.rsplit('_v', 1)[0] if '_v' in unit else unit
        if a.unit and unit != a.unit:
            continue
        try:
            res = R.parse_unit(open(os.path.join(scdir, fn), 'rb').read())
        except Exception as e:
            print('  !! %s 解析失败 %s' % (fn, e))
            continue
        cache[unit] = res
        clip_by_id = {c['id']: c for c in res['clips']}
        sid2idx = {}
        for i, s in enumerate(res['shapes']):
            sid2idx.setdefault(s['sid'], i)

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

        for nm, cid in zip(res['exp_names'], res['exp_ids']):
            if a.keyword and a.keyword.lower() not in nm.lower():
                continue
            c = clip_by_id.get(cid)
            recs = sorted(set(expand(cid, {cid}))) if c else []
            hits.setdefault(unit, []).append((nm, cid, recs))

    print('\n---- 命中：unit / export / clip / 记录序 / 精灵宽（非透明, px） ----')
    for unit in sorted(hits):
        res = cache[unit]
        for nm, cid, recs in hits[unit]:
            info = []
            for r in recs[:6]:
                bb = rec_bbox(root, unit, r)
                info.append('rec%d:%s' % (r, ('%dx%d' % (bb[2], bb[3])) if bb else '-'))
            print('%-26s %-34s clip=%-4s recs=%-16s %s'
                  % (unit, nm, cid, R.fmt_ranges(recs) if recs else '-', ' '.join(info)))

    if a.unit:
        res = cache.get(a.unit)
        if res:
            clip_by_id = {c['id']: c for c in res['clips']}
            sid2idx = {}
            for i, s in enumerate(res['shapes']):
                sid2idx.setdefault(s['sid'], i)

            def expand2(cid, seen):
                out = []
                c = clip_by_id.get(cid)
                if c is None:
                    return out
                for s in c['sids']:
                    if s in sid2idx:
                        out.append(sid2idx[s])
                    elif s in clip_by_id and s not in seen:
                        out += expand2(s, seen | {s})
                return out

            print('\n==== %s 的全部 %d 个 export ====' % (a.unit, len(res['exp_names'])))
            for nm, cid in zip(res['exp_names'], res['exp_ids']):
                recs = sorted(set(expand2(cid, {cid})))
                info = []
                for r in recs[:4]:
                    bb = rec_bbox(root, a.unit, r)
                    info.append('rec%d:%s' % (r, ('%dx%d' % (bb[2], bb[3])) if bb else '-'))
                print('  %-34s clip=%-4s recs=%-18s %s'
                      % (nm, cid, R.fmt_ranges(recs) if recs else '-', ' '.join(info)))

    if a.records:
        unit, lst = a.records.split(':', 1)
        nums = [int(x) for x in lst.split(',') if x.strip()]
        print('\n==== %s 记录 %s 的联络图 ====' % (unit, nums))
        try:
            from PIL import Image, ImageDraw
        except Exception as e:
            print('(无 PIL) %s' % e)
            return 0
        cell, lab = 200, 15
        cols = min(len(nums), 6)
        rows = (len(nums) + cols - 1) // cols
        canvas = Image.new('RGB', (cols * (cell + lab), rows * (cell + lab)), (240, 240, 240))
        dr = ImageDraw.Draw(canvas)
        for i, n in enumerate(nums):
            p = os.path.join(png_dir(root, unit), unit + '_sprite_%03d.png' % n)
            if not os.path.isfile(p):
                p = os.path.join(png_dir(root, unit), 'frame_%03d.png' % n)
            if not os.path.isfile(p):
                print('  缺 png rec %d' % n)
                continue
            im = Image.open(p).convert('RGBA')
            bb = png_bbox(p)
            k = min(cell / im.width, cell / im.height)
            im2 = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
            x = (i % cols) * (cell + lab)
            y = (i // cols) * (cell + lab)
            canvas.paste(im2.convert('RGB'), (x + (cell - im2.width) // 2, y + (cell - im2.height) // 2))
            dr.text((x + 2, y + cell + 1), 'rec %d  %dx%d (画布 %dx%d)' % (n, bb[2], bb[3], bb[0], bb[1]),
                    fill=(0, 0, 0))
        out = os.path.join(root, '.ai-tmp', 'test', 'CR-T1i-hunt-%s.png' % unit)
        canvas.save(out)
        print('SHEET -> %s' % out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
