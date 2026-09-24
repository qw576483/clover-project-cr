# -*- coding: utf-8 -*-
"""D130b 取证：frame_022 河岸带的行/列结构（只读）。

目的（3 个问题一次答完）：
  Q1 「河岸泥带 + 灰石」到底占哪些行？木板（tan 竖板）占哪些行/列？
  Q2 段① 里那份木板（cols 224..296 / 328..400）要补草地时，**同一批行里有没有纯草地列**可当样本？
  Q3 灰石（STONE）在哪些行、沿 x 分布如何（决定镜像带的上下沿）。

分类口径与 `cr-d130-rows2.py` / `cr-d130-lane-rowscan.py` 一致：
  T=透明  G=草  R=土/路(tan, r>g+8 且 r>b+25)  W=水/蓝  S=灰/石(|r-g|<14 且 |g-b|<14 且非透明)  ?=其它
用法: python lead-d130b-bands.py [yStart] [yEnd]
"""
import sys
import numpy as np
from PIL import Image

F22 = r"C:\Work\Server\f-v2\clover-project-cr\client\Assets\Resources\Sprites\Arenas\arena_training_out\frame_022.png"
X0, X1 = 99, 918
y0 = int(sys.argv[1]) if len(sys.argv) > 1 else 566
y1 = int(sys.argv[2]) if len(sys.argv) > 2 else 700

im = Image.open(F22).convert("RGBA")
a = np.array(im)
H, W = a.shape[:2]
print(f"# frame_022 {W}x{H}  window_x=[{X0},{X1}]  rows {y0}..{y1}")


def tag(px):
    r, g, b, al = int(px[0]), int(px[1]), int(px[2]), int(px[3])
    if al < 16:
        return "T"
    if g > r + 10 and g > b + 10:
        return "G"
    if b > r + 20 and b > g + 5:
        return "W"
    if r > g + 8 and r > b + 25:
        return "R"
    if abs(r - g) < 14 and abs(g - b) < 14:
        return "S"
    return "?"


print("   row |    T    G    R    W    S    ?  | MUD(R) x-extent(s>=8px)        | STONE x-extent(s>=6px)")
for y in range(y0, y1 + 1):
    row = a[y, X0:X1 + 1]
    tags = [tag(row[i]) for i in range(row.shape[0])]
    c = {k: tags.count(k) for k in "TGRWS?"}

    def extents(ch, minw):
        out = []
        cur = None
        for i, t in enumerate(tags):
            if t == ch:
                if cur is None:
                    cur = i
            else:
                if cur is not None:
                    if i - cur >= minw:
                        out.append((X0 + cur, X0 + i - 1))
                    cur = None
        if cur is not None and len(tags) - cur >= minw:
            out.append((X0 + cur, X1))
        return out

    mud = " ".join(f"{p}-{q}" for p, q in extents("R", 8))
    st = " ".join(f"{p}-{q}" for p, q in extents("S", 6))
    print(f"  {y:4d} | {c['T']:4d} {c['G']:4d} {c['R']:4d} {c['W']:4d} {c['S']:4d} {c['?']:4d}  | {mud:<30s} | {st}")

# Q2：候选补丁列（在 rows 604..668 内**全行都是草**的列）
print()
print("# Q2 候选补丁列：在指定行区间内「每行都是 G」的列区间（宽 >= 16px）")
for (ra, rb) in [(604, 668), (600, 670), (583, 615)]:
    good = np.ones(X1 - X0 + 1, dtype=bool)
    for y in range(ra, rb + 1):
        row = a[y, X0:X1 + 1]
        for i in range(row.shape[0]):
            if tag(row[i]) != "G":
                good[i] = False
    runs = []
    cur = None
    for i, ok in enumerate(good):
        if ok and cur is None:
            cur = i
        elif not ok and cur is not None:
            if i - cur >= 16:
                runs.append((X0 + cur, X0 + i - 1))
            cur = None
    if cur is not None and len(good) - cur >= 16:
        runs.append((X0 + cur, X1))
    print(f"  rows {ra}..{rb}: " + (" ".join(f"{p}-{q}(w{q-p+1})" for p, q in runs) if runs else "（无纯草列区间）"))
