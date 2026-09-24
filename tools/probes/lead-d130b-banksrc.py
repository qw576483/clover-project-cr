# -*- coding: utf-8 -*-
"""D130b 源行构成探针：量 frame_022 里被用到的两段源行**本身**的类别构成。

为什么必须量（⛔ 不许靠常量手算）：
  实机 census 报 `GroundRedBank` 落 yTop 815.4..915.0（= 场地格 13.59..15.25），
  而同一条判据在**旧 DLL** 图上是 上 0.000/下 0.234、新 DLL 图上是 上 0.235/下 0.000
  —— 方向翻正确了，但**上格出现 WOOD=0.675**。必须分清：
    (a) 源行 575..612 本身含成束竖木板（= 素材固有）⇒ 那是"照抄素材"，要跟原版核对；
    (b) 源行不含木，木色来自别的对象（桥/其它裁条）⇒ 说明我的落格或排序层写错了。
  两种结论的修法完全不同，所以先量源。

用法: python lead-d130b-banksrc.py
"""
import os
import sys

from PIL import Image

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
F22 = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas',
                   'arena_training_out', 'frame_022.png')
WIN = (99, 919)


def cls(p):
    r, g, b = p[:3]
    if g > 95 and g > r + 18 and g > b + 40:
        return 'GRASS'
    if b > 110 and b > r + 35 and g > 110:
        return 'WATER'
    if r > 105 and r > g + 24 and g > b + 8 and b < 130:
        return 'WOOD'
    if r > 120 and r > g + 12 and g >= b and r - b > 25:
        return 'MUD'
    if abs(r - g) < 14 and abs(g - b) < 16 and 70 < r < 215:
        return 'STONE'
    return 'other'


def seg_stats(im, y0, y1, x0, x1, label):
    px = im.load()
    cnt = {}
    tot = 0
    trans = 0
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            p = px[x, y]
            if p[3] < 16:
                trans += 1
                continue
            k = cls(p)
            cnt[k] = cnt.get(k, 0) + 1
            tot += 1
    print('--- %s  rows %d..%d x %d..%d  不透明=%d 透明=%d' % (label, y0, y1, x0, x1, tot, trans))
    if tot == 0:
        print('    (全透明)')
        return
    order = ['GRASS', 'MUD', 'STONE', 'WOOD', 'WATER', 'other']
    print('    ' + '  '.join('%s=%.3f' % (k, cnt.get(k, 0) / float(tot)) for k in order)
          + '   非草=%.3f' % (1.0 - cnt.get('GRASS', 0) / float(tot)))
    # 木色列分布（判"是不是成束竖板 / 在哪几列"）
    px2 = im.load()
    colcnt = []
    for x in range(x0, x1 + 1):
        n = 0
        m = 0
        for y in range(y0, y1 + 1):
            p = px2[x, y]
            if p[3] < 16:
                continue
            m += 1
            if cls(p) == 'WOOD':
                n += 1
        colcnt.append((x, n, m))
    runs = []
    s = None
    for x, n, m in colcnt:
        hit = (m > 0 and n / float(m) >= 0.5)
        if hit and s is None:
            s = x
        elif not hit and s is not None:
            runs.append((s, x - 1))
            s = None
    if s is not None:
        runs.append((s, colcnt[-1][0]))
    runs = [r for r in runs if r[1] - r[0] + 1 >= 4]
    print('    木色列（该列 >=50%% 为 WOOD 的连续段，>=4px）: %s' % (runs if runs else '无'))
    return runs


def main():
    im = Image.open(F22).convert('RGBA')
    print('# %s  %dx%d  窗口 x %d..%d（= ArenaView 的 GroundFieldLeftPx..RightPx）'
          % (F22, im.size[0], im.size[1], WIN[0], WIN[1]))
    seg_stats(im, 575, 612, WIN[0], WIN[1], '河岸带源 RiverBankPyTop..RiverBankPyBottom（GroundRedBank 用的就是它）')
    seg_stats(im, 686, 748, WIN[0], WIN[1], '覆盖带源 GapSrcPyTop..GapSrcPyBottom（要求：无木）')
    seg_stats(im, 604, 668, 223, 298, '桥板源 BridgePlank 全窗口（左车道那一束）')
    print()
    print('# 结论口径：河岸带源里若有成束木色列 ⇒ 木色是素材固有的（要跟原版核对该不该有）；若为「无」⇒ 实机上的木色来自别的对象。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
