#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1b-occupant.py -- CR-T1b 判据资产：塔的**阵营色**从哪来（红塔为何红、红方公主为何红）
======================================================================================
回答两个问题（都是主 agent 点名要出处的）：
  1. `building_tower_v215.sc` 里 `KingTower_red` / `KingTower_blue` 是**同一套灰度 shape + 颜色变换**，
     还是**各自不同的 shape 记录（颜色烘焙在贴图里）**？
  2. 公主塔乘员（`chr_princess_v215.sc`）有没有红/蓝两套？各自的帧号是多少？

判据（本脚本直接打印，不靠记忆）
--------------------------------
* 逐帧读 PNG 的**非透明像素**，统计「明显偏红」「明显偏蓝」的像素计数（阈值：`r > b+25 且 r-min(g,b) > 30`）
  与均值 RGB。颜色若来自**同一张灰度图 + 运行时 ColorTransform**，两张图的这些统计会**一样**；
  实测 211 与 213 **完全不同** ⇒ 配色烘焙在各自的 shape 贴图里（不同 rec）。
* `chr_princess_v215.sc` 的 Export 表自带 `princess_tower_red_*` 前缀 ⇒ 红套是**原版独立导出**，不是本项目染色。

复跑
----
  python tools/probes/cr-t1b-occupant.py --root .
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


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    import numpy as np
    from PIL import Image

    R = _load(os.path.join(root, 'tools', 'probes', 'sc-anim-index.py'), 'scanim')
    ORIG = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc')

    def resolve(sc_name):
        res = R.parse_unit(open(os.path.join(root, '原版资源', 'sc', sc_name + '_v215.sc'), 'rb').read())
        sid2idx = {}
        for i, s in enumerate(res['shapes']):
            sid2idx.setdefault(s['sid'], i)
        by_id = {c['id']: c for c in res['clips']}

        def exp(cid, seen):
            out = []
            c = by_id.get(cid)
            if c is None:
                return out
            for s in c['sids']:
                if s in sid2idx:
                    out.append(sid2idx[s])
                elif s in by_id and s not in seen:
                    out += exp(s, seen | {s})
            return out
        return res, exp

    def stats(path):
        if not os.path.isfile(path):
            return None
        arr = np.asarray(Image.open(path).convert('RGBA')).astype(int)
        m = arr[:, :, 3] > 200
        if not m.any():
            return None
        r, g, b = arr[:, :, 0][m], arr[:, :, 1][m], arr[:, :, 2][m]
        ys, xs = np.nonzero(arr[:, :, 3] > 8)
        red = int(((r > b + 25) & (r - np.minimum(g, b) > 30)).sum())
        blue = int(((b > r + 25) & (b - np.minimum(g, r) > 30)).sum())
        return dict(n=int(m.sum()), red=red, blue=blue,
                    rgb=(int(r.mean()), int(g.mean()), int(b.mean())),
                    bbox=(int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))

    print('==== 1. 塔体：红/蓝是不同 rec、颜色烘焙在贴图里 ====')
    res, exp = resolve('building_tower')
    for nm in ('KingTower_red', 'KingTower_blue'):
        i = res['exp_names'].index(nm)
        print('  %-16s clip=%-5d recs=%s' % (nm, res['exp_ids'][i],
                                             R.fmt_ranges(sorted(set(exp(res['exp_ids'][i], {res['exp_ids'][i]}))))))
    for n in (211, 212, 213):
        s = stats(os.path.join(ORIG, 'building_tower_out', 'building_tower_sprite_%03d.png' % n))
        print('  frame_%03d  %s' % (n, s))
    s211 = stats(os.path.join(ORIG, 'building_tower_out', 'building_tower_sprite_211.png'))
    s213 = stats(os.path.join(ORIG, 'building_tower_out', 'building_tower_sprite_213.png'))
    same = (s211['red'], s211['blue']) == (s213['red'], s213['blue'])
    print('  判定：211 与 213 的配色统计%s ⇒ 颜色%s'
          % ('相同' if same else '完全不同', '可能来自同一灰度图+ColorTransform' if same else '烘焙在各自贴图里（不同 rec）'))

    print('\n==== 2. 公主塔乘员：红/蓝两套（chr_princess） ====')
    res2, exp2 = resolve('chr_princess')
    for nm, cid in zip(res2['exp_names'], res2['exp_ids']):
        if nm.startswith('princess_tower') and 'idle' in nm:
            recs = sorted(set(exp2(cid, {cid})))
            print('  %-30s clip=%-5d recs=%s  stats=%s'
                  % (nm, cid, R.fmt_ranges(recs), stats(os.path.join(ORIG, 'chr_princess_out',
                                                                    'chr_princess_sprite_%03d.png' % recs[0]))))
    return 0


if __name__ == '__main__':
    sys.exit(main())
