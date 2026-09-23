#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-tower-compose.py -- CR-T1 判据资产：`building_tower_v215.sc` 的「塔 = 多 shape 合成」配方解析
================================================================================================
回答的问题
----------
1. 每个 export 名（如 `KingTower_blue` / `princesstower_goldrush_01`）**由哪些 frame_NNN 合成**？
2. 每一条子项的名字（`turret` / `king_idle` / ...）对应哪个 **PNG 记录序**（= frame_NNN 的 NNN）？
3. 每个 export 各层的**逐帧非透明包围盒**（用于算叠放位置）。

为什么需要它
------------
`策划/塔与建筑动画表.md` §52-53 已写：**塔为多 shape 合成（塔体 + 国王人物 + 炮塔），
单取一帧只得塔体层** —— 而 `ArenaView` 现在每座塔只取一帧 ⇒ 画出来是个「空盒子」。
本脚本把「哪几帧是一套」从 `.sc` 的 **Export 表（显式 id 引用）** 里读出来，不靠猜。

复跑
----
  python tools/probes/cr-tower-compose.py --root .
  python tools/probes/cr-tower-compose.py --root . --sheet KingTower_blue,KingTower_red
"""
import argparse
import importlib.util
import json
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--unit', default='building_tower')
    ap.add_argument('--sheet', default='', help='逗号分隔的 export 名：为每个 export 出一条联络图')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'scanim')
    sc = os.path.join(root, '原版资源', 'sc', a.unit + '_v215.sc')
    res = R.parse_unit(open(sc, 'rb').read())

    fdir = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', a.unit + '_out')
    png_n = len([f for f in os.listdir(fdir) if f.endswith('.png')])
    print('sc=%s  ShapeCount=%d  exports=%d  clips=%d  PNG=%d'
          % (os.path.basename(sc), res['ShapeCount'], len(res['exp_names']), len(res['clips']), png_n))

    sid2idx = {}
    for i, s in enumerate(res['shapes']):
        sid2idx.setdefault(s['sid'], i)
    clip_by_id = {c['id']: c for c in res['clips']}

    offs = sorted({(sid, idx) for sid, idx in sid2idx.items() if sid != idx})
    print('sid -> 记录序 不一致条目数 = %d，前 8 个 %s' % (len(offs), offs[:8]))

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

    recipes = {}
    print('\n---- export 名 -> 配方（名字 / raw sid / 记录序） ----')
    for nm, cid in zip(res['exp_names'], res['exp_ids']):
        c = clip_by_id.get(cid)
        if c is None:
            print('%-32s clip=%-5d  !!! clip 不存在' % (nm, cid))
            continue
        raw = list(c['sids'])
        names = list(c['shape_names'])
        recs = sorted(set(expand(cid, {cid})))
        parts = []
        for i, s in enumerate(raw):
            n = names[i] if i < len(names) else '?'
            if s in sid2idx:
                parts.append('%s(sid=%d->rec=%d)' % (n, s, sid2idx[s]))
            elif s in clip_by_id:
                sub = sorted(set(expand(s, {s, cid})))
                parts.append('%s(clip=%d->recs=%s)' % (n, s, R.fmt_ranges(sub) if sub else '-'))
            else:
                parts.append('%s(sid=%d->悬空)' % (n, s))
        recipes[nm] = dict(clip=cid, fps=c['fps'], timeline_frames=c['frames'],
                           raw=raw, names=names, records=recs, parts=parts)
        print('%-32s clip=%-5d fps=%s tf=%-4s recs=%-26s %s'
              % (nm, cid, c['fps'], c['frames'],
                 R.fmt_ranges(recs) if recs else '-', ' | '.join(parts)))

    try:
        from PIL import Image, ImageDraw
        import numpy as np
    except Exception as e:
        print('\n(无 PIL，跳过联络图) %s' % e)
        return 0

    bbox_cache = {}
    # 原版导出名 = `<unit>_sprite_NNN.png`；进工程的副本被改名成 `frame_NNN.png`。两种都认。
    probe0 = [os.path.join(fdir, 'frame_%03d.png' % 0),
              os.path.join(fdir, '%s_sprite_%03d.png' % (a.unit, 0))]
    name_fmt = 'frame_%03d.png' if os.path.isfile(probe0[0]) else (a.unit + '_sprite_%03d.png')
    print('PNG 命名规则 = %s' % name_fmt.replace('%03d', 'NNN'))

    def bbox(n):
        if n in bbox_cache:
            return bbox_cache[n]
        p = os.path.join(fdir, name_fmt % n)
        if not os.path.isfile(p):
            bbox_cache[n] = None
            return None
        arr = np.asarray(Image.open(p).convert('RGBA'))
        ys, xs = np.nonzero(arr[:, :, 3] > 8)
        v = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1) if len(xs) else None
        bbox_cache[n] = v
        return v

    print('\n---- 逐帧 bbox（画布 407x471） ----')
    for nm in recipes:
        recs = recipes[nm]['records']
        shown = recs[:3] + (recs[-3:] if len(recs) > 6 else recs[3:])
        print('%-32s recs=%-22s bboxes=%s'
              % (nm, R.fmt_ranges(recs) if recs else '-', [bbox(r) for r in shown]))

    json_path = os.path.join(root, '.ai-tmp', 'test', 'CR-T1-tower-recipes.json')
    os.makedirs(os.path.dirname(json_path), exist_ok=True)
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(recipes, f, ensure_ascii=False, indent=1)
    print('\nJSON -> %s' % json_path)

    if a.sheet:
        outdir = os.path.join(root, '.ai-tmp', 'test')
        for nm in [x.strip() for x in a.sheet.split(',') if x.strip()]:
            r = recipes.get(nm)
            if not r:
                print('!! 无 export %s' % nm)
                continue
            nums = r['records']
            cell, lab = 190, 14
            cols = min(len(nums), 8)
            rows = (len(nums) + cols - 1) // cols
            canvas = Image.new('RGB', (cols * cell, rows * (cell + lab)), (235, 235, 235))
            dr = ImageDraw.Draw(canvas)
            for i, num in enumerate(nums):
                p = os.path.join(fdir, name_fmt % num)
                if not os.path.isfile(p):
                    continue
                im = Image.open(p).convert('RGBA')
                k = min(cell / im.width, cell / im.height)
                im2 = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
                x = (i % cols) * cell
                y = (i // cols) * (cell + lab)
                canvas.paste(im2.convert('RGB'), (x + (cell - im2.width) // 2, y + (cell - im2.height) // 2))
                dr.text((x + 3, y + cell + 1), 'rec %d' % num, fill=(0, 0, 0))
            out = os.path.join(outdir, 'CR-T1-recipe-%s.png' % nm)
            canvas.save(out)
            print('RECIPE_SHEET %s  (%d recs) -> %s' % (nm, len(nums), out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
