# -*- coding: utf-8 -*-
"""逐行剖面：frame_022 rows 575..612 每行各多少木色（为"收窄河岸带源行"定界）。

背景：`lead-d130b-banksrc.py` 已证这段整段 WOOD=0.547 且木色列遍布全宽。
要把河岸带收成"只有泥+石"，必须知道"木从第几行开始/结束"。
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
ORDER = ['GRASS', 'MUD', 'STONE', 'WOOD', 'WATER', 'other']


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


def main():
    im = Image.open(F22).convert('RGBA')
    px = im.load()
    print('# frame_022 逐行剖面  x %d..%d' % WIN)
    print('  row |  GRASS    MUD  STONE   WOOD  other   非草')
    for y in range(560, 690):
        cnt, tot = {}, 0
        for x in range(WIN[0], WIN[1] + 1):
            p = px[x, y]
            if p[3] < 16:
                continue
            k = cls(p)
            cnt[k] = cnt.get(k, 0) + 1
            tot += 1
        if tot == 0:
            print('  %3d |  (全透明)' % y)
            continue
        f = {k: cnt.get(k, 0) / float(tot) for k in ORDER}
        mark = ''
        if f['WOOD'] >= 0.40:
            mark = '   <== 大片木'
        elif f['WOOD'] >= 0.15:
            mark = '   <- 少量木'
        print('  %3d | %6.3f %6.3f %6.3f %6.3f %6.3f %6.3f%s'
              % (y, f['GRASS'], f['MUD'], f['STONE'], f['WOOD'], f['other'], 1 - f['GRASS'], mark))
    return 0


if __name__ == '__main__':
    sys.exit(main())
