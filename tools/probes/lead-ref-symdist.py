# -*- coding: utf-8 -*-
"""河道对称性「色距剖面」判据。

对每个 d（距水带中线的行数）算：
    dist(d) = mean_x || RGB(x, yc-d) - RGB(x, yc+d) ||_1 / 3
并同时给出"上沿行均色 / 下沿行均色"，便于判读是颜色不同还是厚度不同。
桥列必须排除（桥自身在两岸投影不同）。桥列由参数传入。

用法: python lead-ref-symdist.py <img> <x0> <x1> <yc> <exclude_csv_ranges> [dmax] [pxt]
   exclude_csv_ranges 形如 "157-262,817-922"（x 像素区间，闭区间）
"""
import sys
from PIL import Image

P = sys.argv[1]
x0, x1, yc = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
excl = sys.argv[5]
DMAX = int(sys.argv[6]) if len(sys.argv) > 6 else 60
PXT = float(sys.argv[7]) if len(sys.argv) > 7 else 0.0

ranges = []
for part in excl.split(','):
    part = part.strip()
    if not part:
        continue
    a, b = part.split('-')
    ranges.append((int(a), int(b)))


def excluded(x):
    return any(a <= x <= b for a, b in ranges)


cols = [x for x in range(x0, x1 + 1) if not excluded(x)]
im = Image.open(P).convert('RGB')
W, H = im.size
px = im.load()
print(f'# file={P}  x {x0}..{x1}  用列 {len(cols)} 条（已排除 {excl}）  yc={yc}  pxt={PXT:.2f}')


def cls(c):
    r, g, b = c
    if g > 95 and g > r + 18 and g > b + 40:
        return 'GRASS'
    if b > 110 and b > r + 35 and g > 110:
        return 'WATER'
    if r > 120 and r > g + 12 and g >= b and r - b > 25:
        return 'MUD'
    if abs(r - g) < 14 and abs(g - b) < 16 and 70 < r < 215:
        return 'STONE'
    return 'other'


print('   d   tile_d     dist   上沿均色          下沿均色          上沿主类/下沿主类   失配列占比')
prev = None
for d in range(1, DMAX + 1):
    ya, yb = yc - d, yc + d
    if ya < 0 or yb >= H:
        break
    tot = 0.0
    sa = [0, 0, 0]
    sb = [0, 0, 0]
    ca, cb = {}, {}
    bad = 0
    for x in cols:
        pa = px[x, ya]
        pb = px[x, yb]
        tot += (abs(pa[0] - pb[0]) + abs(pa[1] - pb[1]) + abs(pa[2] - pb[2])) / 3.0
        for i in range(3):
            sa[i] += pa[i]
            sb[i] += pb[i]
        k = cls(pa)
        ca[k] = ca.get(k, 0) + 1
        k2 = cls(pb)
        cb[k2] = cb.get(k2, 0) + 1
        if k != k2:
            bad += 1
    n = len(cols)
    ma = tuple(v // n for v in sa)
    mb = tuple(v // n for v in sb)
    ta = max(ca.items(), key=lambda kv: kv[1])[0]
    tb = max(cb.items(), key=lambda kv: kv[1])[0]
    td = f'{d/PXT:6.2f}' if PXT else '   n/a'
    print(f'  {d:3d} {td}   {tot/n:6.2f}   ({ma[0]:3d},{ma[1]:3d},{ma[2]:3d})    ({mb[0]:3d},{mb[1]:3d},{mb[2]:3d})   {ta:6s}/{tb:6s}   {bad/n*100:5.1f}%')
