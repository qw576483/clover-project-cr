# -*- coding: utf-8 -*-
"""河道 180° 点对称判据（可判红）。

给定一张图 + 场地矩形 + 水带中线行，逐列比较：
    class(x, yc - d)  ?=  class(x, yc + d)      d = 1..DMAX
输出：
    - 每列的失配行数 / DMAX
    - 全图失配率
    - 把失配最重的 20 列列出来（含两端格坐标与失配的 dy 区间）
负控：把图上下翻转后跑，失配率必须接近 100%（证明判据能失败）。
用法: python lead-ref-symmetry.py <img> <x0> <x1> <yc> [dmax]
"""
import sys
from PIL import Image

P = sys.argv[1]
x0, x1, yc = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
DMAX = int(sys.argv[5]) if len(sys.argv) > 5 else 40

im = Image.open(P).convert('RGB')
W, H = im.size
px = im.load()


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


def sym(use_flip):
    """use_flip=True 时，读下沿用翻转后的图（人为破坏对称）⇒ 用于负控"""
    bad_total = 0
    tot = 0
    percol = []
    for x in range(x0, x1 + 1):
        bad = []
        for d in range(1, DMAX + 1):
            ya, yb = yc - d, yc + d
            if ya < 0 or yb >= H:
                continue
            ca = cls(px[x, ya])
            cb = cls(px[x, H - 1 - yb]) if use_flip else cls(px[x, yb])
            tot += 1
            if ca != cb:
                bad.append(d)
        percol.append((x, bad))
        bad_total += len(bad)
    return bad_total, tot, percol


print(f'# file={P}  x {x0}..{x1}  yc={yc}  dmax={DMAX}')
b, t, percol = sym(False)
print(f'# 失配 {b}/{t} = {b/t*100:.1f}%')
worst = sorted(percol, key=lambda kv: -len(kv[1]))[:20]
print('# 失配最重 20 列 (x, 失配行数, dy 列表)')
for x, bad in worst:
    if not bad:
        break
    print(f'#   x={x:4d}  n={len(bad):3d}  dy={bad[:12]}{"..." if len(bad)>12 else ""}')
# 分带统计：距水带 1..8 行（近岸）/ 9..24 / 25..40
bands = [(1, 8), (9, 24), (25, 40)]
for lo, hi in bands:
    bb = sum(1 for x, bad in percol for d in bad if lo <= d <= hi)
    tt = sum(1 for x in range(x0, x1 + 1) for d in range(lo, min(hi, DMAX) + 1) if yc - d >= 0 and yc + d < H)
    if tt:
        print(f'#   近岸带 d={lo:2d}..{hi:2d}: 失配 {bb}/{tt} = {bb/tt*100:.1f}%')

bf, tf, _ = sym(True)
print(f'# [负控] 下沿改读翻转图 ⇒ 失配 {bf}/{tf} = {bf/tf*100:.1f}%  （应 ~100% 才算判据灵敏）')
