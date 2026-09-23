import os
import numpy as np
from PIL import Image
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
A = np.asarray(Image.open(os.path.join(ROOT, r"策划\参考图\18_对局HUD_1080x1920.jpg")).convert("RGB")).astype(np.int16)
r, g, b = A[:, :, 0], A[:, :, 1], A[:, :, 2]
gold = (r > 225) & (g > 165) & (b < 165)
for y0, y1, tag in ((950, 1450, "河下方(我方半场)"), (300, 700, "河上方(敌方半场)"), (1640, 1800, "HUD 区")):
    prof = gold[y0:y1].sum(axis=0)
    runs, c = [], None
    for x in range(1080):
        if prof[x] > (y1 - y0) * 0.25:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 15:
                runs.append((c[0], c[1]))
            c = None
    if c and c[1] - c[0] > 15:
        runs.append((c[0], c[1]))
    print(f"[18 金道列带 y{y0}..{y1} {tag}] = {runs}")
    if len(runs) >= 2:
        c1 = (runs[0][0] + runs[0][1]) / 2.0
        c2 = (runs[-1][0] + runs[-1][1]) / 2.0
        ppt = (c2 - c1) / 11.0
        print(f"   ⇒ 中心 {c1:.1f}/{c2:.1f} ⇒ pptx={ppt:.2f}，格0 ⇔ x {c1 - 3.5*ppt:.1f}，格18 ⇔ {c1 + 14.5*ppt:.1f}")
# 场地外框（金）x 范围
prof2 = gold[200:1500].sum(axis=0)
xs = np.where(prof2 > 200)[0]
print("金像素列(>200 行) 范围:", (int(xs.min()), int(xs.max())) if xs.size else "-")
# 灌木/绿 行带
bush = (g > 140) & (g - r > 35) & (g - b > 35)
rows = bush.sum(axis=1)
hit = np.where(rows > 100)[0]
print("灌木绿行(>100px):", (int(hit.min()), int(hit.max())) if hit.size else "-", "共", int(hit.size))
# 石板行带（浅灰）
stone = (np.abs(r - g) < 14) & (np.abs(g - b) < 26) & (r > 175)
rows2 = stone.sum(axis=1)
print("浅灰石板行(>400px):", end=" ")
seg, c = [], None
for y in range(1920):
    if rows2[y] > 400:
        c = [y, y] if c is None else [c[0], y]
    else:
        if c and c[1] - c[0] > 20:
            seg.append((c[0], c[1]))
        c = None
if c and c[1] - c[0] > 20:
    seg.append((c[0], c[1]))
print(seg)
