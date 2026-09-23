# CR-T6b: 按基线 18 重测——标定（塔 HP 条 → 格位）、纵向色带、河道带、桥位、地面取色
# 复跑：python .ai-tmp/test/t6b-measure.py
import os
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
B18 = os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T6-arena-nohud.png")
a = np.asarray(Image.open(B18).convert("RGB")).astype(np.int16)
H, W = a.shape[:2]
print("18 图", (W, H))
r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]

# ── 1) 塔 HP 条：高饱和红横条（r>150 且 r-g>60）→ 连通行带 ────────────────
hp = (r > 150) & (r - g > 60) & (r - b > 60)
rows = hp.sum(axis=1)
print("\n[HP 条] 逐行红像素数 > 40 的行带：")
runs, cur = [], None
for y in range(H):
    if rows[y] > 40:
        cur = [y, y] if cur is None else [cur[0], y]
    else:
        if cur and cur[1] - cur[0] >= 6:
            runs.append(tuple(cur))
        cur = None
if cur and cur[1] - cur[0] >= 6:
    runs.append(tuple(cur))
for y0, y1 in runs:
    band = hp[y0:y1 + 1]
    cols = np.where(band.sum(axis=0) > (y1 - y0) * 0.5)[0]
    segs, c = [], None
    for x in range(W):
        if cols.size and x in cols:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 30:
                segs.append((c[0], c[1], (c[0] + c[1]) // 2))
            c = None
    if c and c[1] - c[0] > 30:
        segs.append((c[0], c[1], (c[0] + c[1]) // 2))
    print(f"   y {y0}..{y1} (中 {(y0+y1)//2}) 段={segs}")

# ── 2) 河道带（蓝水）：x=540 列 + 全幅 ──────────────────────────────
blue = (b > r + 25) & (b > 110)
col = blue[:, 540]
runs2, cur = [], None
for y in range(H):
    if col[y]:
        cur = [y, y] if cur is None else [cur[0], y]
    else:
        if cur and cur[1] - cur[0] > 4:
            runs2.append(tuple(cur))
        cur = None
if cur and cur[1] - cur[0] > 4:
    runs2.append(tuple(cur))
print("\n[河道 x=540 列] 蓝水行带 =", runs2)
if runs2:
    y0, y1 = max(runs2, key=lambda t: t[1] - t[0])
    sub = a[y0:y1 + 1, :, :]
    m = blue[y0:y1 + 1, :]
    print(f"   最长蓝带 y {y0}..{y1} 厚 {y1-y0+1}；均色 {tuple(int(sub.reshape(-1,3)[m.reshape(-1)][:,i].mean()) for i in range(3))}")
    xs = np.where(m.any(axis=0))[0]
    print(f"   x 范围 {xs.min()}..{xs.max()}（宽 {xs.max()-xs.min()+1}）")
    mid = (y0 + y1) // 2
    rowb = blue[mid]
    segs, c = [], None
    for x in range(W):
        if rowb[x]:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 8:
                segs.append((c[0], c[1]))
            c = None
    if c and c[1] - c[0] > 8:
        segs.append((c[0], c[1]))
    print(f"   中点行 y={mid} 的蓝水段 = {segs}")
    # 河的上/下各 6px 是什么色（看有无土黄压顶/暗带）
    for dy in (-8, -4, 0, (y1 - y0) // 2, 4, 8):
        yy = mid + dy
        print(f"     y={yy} 行均色(全幅)={tuple(int(a[yy,:,i].mean()) for i in range(3))}")

# ── 3) 纵向色带（x=540 列，每 20 行取一次） ───────────────────────────
print("\n[纵向色带 x=540]")
for y in range(90, 1630, 40):
    print(f"   y={y:4d} RGB={tuple(int(a[y,540,i]) for i in range(3))}")

# ── 4) 地面取色（几个格位）───────────────────────────────────────────
print("\n[取样点]")
for tag, x, y in (("敌方半场中", 540, 400), ("敌方半场左", 300, 350), ("我方半场中", 540, 1300),
                  ("我方半场左", 300, 1350), ("车道左(下)", 225, 1000), ("车道左(上)", 225, 300),
                  ("车道右(下)", 815, 1000), ("场地左缘外", 20, 800), ("场地上沿", 540, 120)):
    print(f"   {tag:<10} ({x},{y}) = {tuple(int(a[y,x,i]) for i in range(3))}")
