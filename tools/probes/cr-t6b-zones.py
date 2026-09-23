# CR-T6b: 基线换成 18 后的逐区量取（原版 18 vs 我方 CR-T6-arena-nohud.png），全部给像素读数
# 复跑：python .ai-tmp/test/t6b-final.py
import os
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
B18 = os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")
OUR = os.path.join(ROOT, r".ai-tmp\screenshots\CR-T6-arena-nohud.png")
OUT = os.path.join(ROOT, r".ai-tmp\test\t6b")

A = np.asarray(Image.open(B18).convert("RGB")).astype(np.int16)
O = np.asarray(Image.open(OUR).convert("RGB")).astype(np.int16)
H, W = A.shape[:2]


def masks(a):
    r, g, b = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    return {
        "cyan": (b > r + 35) & (g > r + 20) & (b > 140),          # 河水（青蓝）
        "gold": (r > 225) & (g > 165) & (b < 165),                # 金道 / 金边
        "bush": (g > 140) & (g - r > 35) & (g - b > 35),          # 灌木绿
        "grass": (g > 140) & (g - r > 20) & (g - b > 50) & (r < 190),
        "stone": (np.abs(r - g) < 14) & (np.abs(g - b) < 26) & (r > 175),
        "red": (r > 150) & (r - g > 60) & (r - b > 60),
    }


MA, MO = masks(A), masks(O)
print("=== 18 原版 1080x1920 ===")
for k, m in MA.items():
    rows = m.sum(axis=1)
    hit = np.where(rows > 60)[0]
    ys = f"{int(hit.min())}..{int(hit.max())}" if hit.size else "-"
    print(f"  {k:<6} 像素数 {int(m.sum()):>7}  行(>60px) {ys}")
print("=== 我方 CR-T6-arena-nohud 1080x1920 ===")
for k, m in MO.items():
    rows = m.sum(axis=1)
    hit = np.where(rows > 60)[0]
    ys = f"{int(hit.min())}..{int(hit.max())}" if hit.size else "-"
    print(f"  {k:<6} 像素数 {int(m.sum()):>7}  行(>60px) {ys}")

# ── 18 的河带：精确边界 + 颜色 + 中点行的非水段 ────────────────────────
cy = MA["cyan"]
rows = cy.sum(axis=1)
hit = np.where(rows > 150)[0]
if hit.size:
    y0, y1 = int(hit.min()), int(hit.max())
    m = cy[y0:y1 + 1]
    xs = np.where(m.any(axis=0))[0]
    mean = tuple(int(A[y0:y1 + 1].reshape(-1, 3)[m.reshape(-1)][:, i].mean()) for i in range(3))
    print(f"\n[18 河水] y {y0}..{y1}（厚 {y1 - y0 + 1}） x {xs.min()}..{xs.max()} 均色 RGB{mean}")
    mid = (y0 + y1) // 2
    row = cy[mid]
    segs, c = [], None
    for x in range(W):
        if row[x]:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 10:
                segs.append((c[0], c[1]))
            c = None
    if c and c[1] - c[0] > 10:
        segs.append((c[0], c[1]))
    print(f"   中点行 y={mid} 水段 = {segs}")
    for yy in range(y0 - 12, y1 + 13, 6):
        print(f"     y={yy} 行均色(全幅) RGB{tuple(int(A[yy,:,i].mean()) for i in range(3))}"
              f"  金像素 {int(MA['gold'][yy].sum())}")

# ── 18 的车道 x（金道）：取河下方一段 ────────────────────────────────
gl = MA["gold"]
prof = gl[1000:1450].sum(axis=0)
runs, c = [], None
for x in range(W):
    if prof[x] > 150:
        c = [x, x] if c is None else [c[0], x]
    else:
        if c and c[1] - c[0] > 20:
            runs.append((c[0], c[1]))
        c = None
if c and c[1] - c[0] > 20:
    runs.append((c[0], c[1]))
print(f"\n[18 金道列带 y1000..1450] = {runs}")
if len(runs) >= 2:
    c1 = (runs[0][0] + runs[0][1]) / 2.0
    c2 = (runs[-1][0] + runs[-1][1]) / 2.0
    pptx = (c2 - c1) / 11.0
    x0 = c1 - 3.5 * pptx
    print(f"   ⇒ 车道中心 {c1:.1f}/{c2:.1f} ⇒ pptx={pptx:.2f} px/格，格0 ⇔ x {x0:.1f}，格18 ⇔ {x0 + 18 * pptx:.1f}")
else:
    pptx = x0 = None

# ── 18 的纵向定标：玩家公主塔（灌木簇）中心 = 格 6.5 ────────────────
bs = MA["bush"]
col = bs[:, 200:280].sum(axis=1)          # 左车道那一列
hit2 = np.where(col > 20)[0]
segs2, c = [], None
for y in range(H):
    if col[y] > 20:
        c = [y, y] if c is None else [c[0], y]
    else:
        if c and c[1] - c[0] > 20:
            segs2.append((c[0], c[1], (c[0] + c[1]) // 2))
        c = None
if c and c[1] - c[0] > 20:
    segs2.append((c[0], c[1], (c[0] + c[1]) // 2))
print(f"\n[18 左车道灌木簇 y 段 (x200..280)] = {segs2}")
if hit.size and segs2:
    river_mid = (int(hit.min()) + int(hit.max())) / 2.0
    print(f"   河水带中心 y = {river_mid:.1f}（= 格 16）")
    for (a0, b0, cc) in segs2:
        print(f"   簇 y{cc} ⇒ 相对河心 {(cc - river_mid):+.0f}px ⇒ 若为格 6.5 则 ppty = "
              f"{abs(cc - river_mid) / 9.5:.2f} px/格；若为格 25.5 则 {abs(cc - river_mid) / 9.5:.2f}")
    # 玩家王塔（格 3.5? 用契约值核对）
    print(f"   折算：pptx={pptx:.2f} ⇒ 若 ppty 取 43.2，格0(后沿) ⇔ y {river_mid + 16 * 43.2:.0f}，格32 ⇔ y {river_mid - 16 * 43.2:.0f}")

# ── 双方同格位取色（格由契约给：x 0..18、y 0..32）────────────────────
print("\n[同格位取色] 18 用上面标定；我方 60px/格、格0 ⇔ y1920、格x0 ⇔ x0")
if pptx:
    for tag, tx, ty in (("地面·中(无车道)", 9.0, 10.0), ("地面·中(近河)", 9.0, 13.5),
                        ("地面·左车道外", 1.5, 10.0), ("车道左", 3.5, 10.0), ("车道右", 14.5, 10.0),
                        ("河心", 9.0, 16.0), ("我方后沿", 9.0, 1.0), ("敌方后沿", 9.0, 31.0)):
        bx = int(x0 + tx * pptx)
        # 18: 用 43.2 的纵向定标 + 河心 584 作为锚（下面会按实测河心修正）
        b18y = int(river_mid - (ty - 16.0) * 43.2) if hit.size else int(1920 - ty * 60)
        ox, oy = int(tx * 60.0), int(1920 - ty * 60.0)
        # 3x3 均值
        def avg(a, x, y):
            x = max(1, min(W - 2, x)); y = max(1, min(H - 2, y))
            return tuple(int(a[y - 1:y + 2, x - 1:x + 2, i].mean()) for i in range(3))
        print(f"   {tag:<16} 格({tx},{ty})  18@({bx},{b18y})={avg(A, bx, b18y)}   我方@({ox},{oy})={avg(O, ox, oy)}")
