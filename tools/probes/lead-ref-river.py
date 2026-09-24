# -*- coding: utf-8 -*-
"""lead 复核：用户附图（原版实机 / 训练营）河道两岸逐行剖面。

判据（可失败）：
  A. 场地草地矩形 = 灰/绿/沙的连片区域；量出宽高比，验证 == 18/32 的比例（无透视压缩）。
  B. 在「非桥」列上，逐行打印水带上沿/下沿各 40 行的 RGB，
     分类为 GRASS / WATER / MUD(棕土, r>g>b 且 r-b>25) / STONE(灰, |r-g|<12 且 |g-b|<14) / WOOD。
  C. 结论：上沿方向 MUD/STONE 的连续行数 vs 下沿方向 —— 若两侧都 ≈0 则"原版两岸无泥带灰石"成立。
"""
import sys
from PIL import Image

P = sys.argv[1] if len(sys.argv) > 1 else r'C:/Work/Server/f-v2/clover-project-cr/.ai-tmp/screenshots/REF-user-original.png'
im = Image.open(P).convert('RGB')
W, H = im.size
px = im.load()
print(f'# file={P} size={W}x{H}')


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
    if r > 90 and r - b > 30 and g > 60:
        return 'WOOD'
    return 'other'


# --- A. 场地边界（草地/沙地连片区域）---
def solid(c):
    r, g, b = c
    return (g > 80 and g > b + 25) or (r > 150 and g > 120 and b < 150)


colmark = []
for x in range(W):
    n = sum(1 for y in range(0, H, 3) if solid(px[x, y]))
    colmark.append(n)
n3 = [x for x in range(W) if colmark[x] > 30]
rowmark = []
for y in range(H):
    n = sum(1 for x in range(0, W, 3) if solid(px[x, y]))
    rowmark.append(n)
n4 = [y for y in range(H) if rowmark[y] > 25]
if not n3 or not n4:
    print('# 场地边界判定失败')
    sys.exit(2)
x0, x1 = n3[0], n3[-1]
y0, y1 = n4[0], n4[-1]
fw, fh = x1 - x0 + 1, y1 - y0 + 1
print(f'# FIELD x {x0}..{x1} (w={fw})   y {y0}..{y1} (h={fh})')
print(f'# aspect w/h = {fw/fh:.4f}   期望 18/32 = {18/32:.4f}   相对误差 {abs(fw/fh-18/32)/(18/32)*100:.2f}%')
print(f'# px per tile  x: {fw/18:.2f}   y: {fh/32:.2f}')
PXT = fh / 32.0

# --- 找水带行 ---
waterrows = []
for y in range(y0, y1 + 1):
    n = 0
    for x in range(x0, x1 + 1, 2):
        if cls(px[x, y]) == 'WATER':
            n += 1
    waterrows.append((y, n))
wsel = [y for y, n in waterrows if n > (x1 - x0) / 2 / 2]
if not wsel:
    print('# 水带未找到')
    sys.exit(2)
wy0, wy1 = wsel[0], wsel[-1]
print(f'# WATER rows {wy0}..{wy1}  h={wy1-wy0+1} rows = {(wy1-wy0+1)/PXT:.2f} tile')
print(f'# 水带中线 y = {(wy0+wy1)/2:.1f}   场地中线 y = {(y0+y1)/2:.1f}')

# --- B. 非桥列逐行剖面 ---
# 先从水带里找"桥"列：水带中段出现 WOOD 的列
bridgecols = []
for x in range(x0, x1 + 1):
    n = sum(1 for y in range(wy0, wy1 + 1) if cls(px[x, y]) == 'WOOD')
    if n > (wy1 - wy0) * 0.4:
        bridgecols.append(x)
groups = []
for x in bridgecols:
    if groups and x - groups[-1][-1] <= 2:
        groups[-1].append(x)
    else:
        groups.append([x])
groups = [g for g in groups if len(g) >= 5]
print('# 水带内 WOOD 列分组（桥）: ' + ' | '.join(f'{g[0]}..{g[-1]}' for g in groups))

# 选非桥列：水带两端与中间各取一条
cand = [x0 + int(fw * f) for f in (0.12, 0.25, 0.45, 0.55, 0.75, 0.88)]
def on_bridge(x):
    return any(g[0] - 4 <= x <= g[-1] + 4 for g in groups)
cols = [x for x in cand if not on_bridge(x)]
print(f'# 剖面列（非桥）: {cols}')

for x in cols:
    print(f'\n## x={x}  (格 x = {(x-x0)/PXT:.2f})')
    print('   row   dy_from_water   RGB            class')
    for y in list(range(max(y0, wy0 - int(PXT * 2)), wy0)) + list(range(wy1 + 1, min(y1, wy1 + int(PXT * 2)) + 1)):
        r, g, b = px[x, y]
        d = y - (wy0 if y < wy0 else wy1)
        print(f'   {y:4d}  {d:+5d}          ({r:3d},{g:3d},{b:3d})   {cls(px[x,y])}')

# --- C. 汇总：上沿/下沿 1 格内的 MUD+STONE 连续长度 ---
up = [cls(px[x, y]) for y in range(max(y0, wy0 - int(PXT + .5)), wy0) for x in cols]
dn = [cls(px[x, y]) for y in range(wy1 + 1, min(y1, wy1 + int(PXT + .5)) + 1) for x in cols]
for name, arr in (('水带上沿外 1 格', up), ('水带下沿外 1 格', dn)):
    tot = len(arr)
    mud = arr.count('MUD')
    st = arr.count('STONE')
    gr = arr.count('GRASS')
    print(f'# {name}: 采样 {tot}   GRASS {gr}  MUD {mud}  STONE {st}  ⇒ MUD+STONE 占比 {(mud+st)/tot*100:.1f}%')
