# CR-V1: 量原版对局图（参考图 18_对局HUD_1080x1920.jpg）的 河/地面/桥 —— 全部给像素读数
# 复跑：python .ai-tmp/test/cr-v1-ref-measure.py
import os
import sys
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
IMG = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")

a = np.asarray(Image.open(IMG).convert("RGB")).astype(np.int16)
H, W = a.shape[:2]
r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
print("图=%s  %dx%d" % (os.path.basename(IMG), W, H))

# ── 1) 水：青蓝 = g 与 b 都明显高于 r ────────────────────────────────
water = (g - r > 25) & (b - r > 25) & (b > 110)
rows = water.sum(axis=1)
hit = np.where(rows > 150)[0]
print("\n[水] 行(>150px) =", (int(hit.min()), int(hit.max())) if hit.size else "-")
if hit.size:
    # 连通行带
    runs, cur = [], None
    for y in range(H):
        if rows[y] > 150:
            cur = [y, y] if cur is None else [cur[0], y]
        else:
            if cur and cur[1] - cur[0] > 5:
                runs.append(tuple(cur))
            cur = None
    if cur and cur[1] - cur[0] > 5:
        runs.append(tuple(cur))
    print("   行带 =", runs)
    y0, y1 = max(runs, key=lambda t: t[1] - t[0])
    m = water[y0:y1 + 1]
    vs = a[y0:y1 + 1].reshape(-1, 3)[m.reshape(-1)]
    xs = np.where(m.any(axis=0))[0]
    print("   主水带 y %d..%d 厚 %d  x %d..%d 宽 %d  均色 RGB%s"
          % (y0, y1, y1 - y0 + 1, xs.min(), xs.max(), xs.max() - xs.min() + 1,
             tuple(int(vs[:, i].mean()) for i in range(3))))
    print("   分位: r 5/50/95%% = %s" % [(int(np.percentile(vs[:, i], q)) for q in (5, 50, 95)) for i in range(3)])
    mid = (y0 + y1) // 2
    # 中点行的水段（桥会把水切断）
    row = water[mid]
    segs, c = [], None
    for x in range(W):
        if row[x]:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 4:
                segs.append((c[0], c[1], c[1] - c[0] + 1))
            c = None
    if c and c[1] - c[0] > 4:
        segs.append((c[0], c[1], c[1] - c[0] + 1))
    print("   中点行 y=%d 水段(x0,x1,w) = %s" % (mid, segs))
    # 水上下各 14px 的行均色 + 是不是金/土
    for yy in list(range(y0 - 16, y0 + 2, 4)) + list(range(y1 - 1, y1 + 17, 4)):
        if 0 <= yy < H:
            print("     y=%4d 行均色 RGB%s" % (yy, tuple(int(a[yy, :, i].mean()) for i in range(3))))

# ── 2) 车道（金）：列带 ──────────────────────────────────────────────
gold = (r > 225) & (g > 165) & (b < 165)
prof = gold[1000:1450].sum(axis=0)
runs, c = [], None
for x in range(W):
    if prof[x] > 150:
        c = [x, x] if c is None else [c[0], x]
    else:
        if c and c[1] - c[0] > 20:
            runs.append((c[0], c[1], c[1] - c[0] + 1))
        c = None
if c and c[1] - c[0] > 20:
    runs.append((c[0], c[1], c[1] - c[0] + 1))
print("\n[金车道 列带 y1000..1450] =", runs)
if len(runs) >= 2:
    c1 = (runs[0][0] + runs[0][1]) / 2.0
    c2 = (runs[-1][0] + runs[-1][1]) / 2.0
    pptx = (c2 - c1) / 11.0
    x0 = c1 - 3.5 * pptx
    print("   车道中心 %.1f / %.1f ⇒ px/格=%.2f，格0 ⇔ x %.1f，格18 ⇔ %.1f"
          % (c1, c2, pptx, x0, x0 + 18 * pptx))

# ── 3) 地面：石材底 + 石板缝周期 ────────────────────────────────────
floor = (np.abs(r - g) < 12) & (b - r > 10) & (b - r < 45) & (r > 170)
print("\n[石材地面] 像素数 %d ; 取样:" % int(floor.sum()))
for tag, x, y in (("我方地面(9,10)", 540, 1320), ("我方地面(1.5,10)", 120 + 9 * 0, 120),
                  ("敌方地面", 540, 300), ("我方地面(15,10)", 900, 1320)):
    print("   %-14s (%d,%d) 3x3=%s" % (tag, x, y, tuple(int(a[y - 1:y + 2, x - 1:x + 2, i].mean()) for i in range(3))))
# 石板缝：x=540 列上，y 1000..1450 的亮度波动周期
lum = a[1000:1450, 540, :].mean(axis=1)
d = np.abs(np.diff(lum))
edges = np.where(d > 6)[0]
print("   石材缝(亮度突变>6) y 位置 =", [int(e) + 1000 for e in edges][:40])
if len(edges) > 2:
    gaps = np.diff(edges)
    print("   缝间距 中位数 = %.1f px" % float(np.median(gaps)))

# ── 4) 纵向每 20 行的列色（x=540 与 x=100 两条）─────────────────────
print("\n[纵向色带] x=540 / x=100")
for y in range(120, 1700, 40):
    print("   y=%4d  x540 RGB%-16s x100 RGB%s"
          % (y, tuple(int(a[y, 540, i]) for i in range(3)), tuple(int(a[y, 100, i]) for i in range(3))))
