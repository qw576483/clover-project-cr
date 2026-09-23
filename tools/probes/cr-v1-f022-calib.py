# CR-V1: 标定 f022(training_area_bg) 的场地范围 / px-per-tile / 车道 / 河带 / 桥位
# 复跑: python .ai-tmp/test/cr-v1-f022-calib.py
import os, sys
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"

for nm, path in (("f006", r"原版资源\cr-assets-png\assets\sc\arena_training_out\arena_training_sprite_06.png"),
                 ("f022", r"原版资源\cr-assets-png\assets\sc\arena_training_out\arena_training_sprite_22.png")):
    p = os.path.join(ROOT, path)
    im = Image.open(p).convert("RGBA")
    a = np.asarray(im).astype(np.int16)
    H, W = a.shape[:2]
    r, g, b, al = a[:, :, 0], a[:, :, 1], a[:, :, 2], a[:, :, 3]
    op = al > 32
    ys, xs = np.where(op)
    print("\n===== %s  canvas %dx%d  opaque bbox x %d..%d y %d..%d" % (nm, W, H, xs.min(), xs.max(), ys.min(), ys.max()))
    grass = op & (g > 120) & (g - r > 10) & (g - b > 40)
    tan = op & (r > 150) & (r - b > 40) & (g > 110) & (np.abs(r - g) < 80) & (g - b > 25)
    water = op & (b - r > 30) & (b > 90)
    dirt = op & (r > 110) & (r - b > 40) & (g > 80) & (g < 140) & (r - g > 25)
    for k, m in (("grass", grass), ("tan", tan), ("water", water), ("dirt", dirt)):
        if m.sum() == 0:
            print("   %-6s 0" % k)
            continue
        yy, xx = np.where(m)
        print("   %-6s px=%-8d bbox x %d..%d y %d..%d  mean RGB%s" %
              (k, int(m.sum()), xx.min(), xx.max(), yy.min(), yy.max(),
               tuple(int(a[:, :, i][m].mean()) for i in range(3))))
    # 逐行：grass / dirt / water 像素数（每 10 行）
    print("   y : grass / tan / water / dirt")
    for y in range(ys.min(), ys.max() + 1, 20):
        print("   %4d : %6d %6d %6d %6d" % (y, int(grass[y].sum()), int(tan[y].sum()), int(water[y].sum()), int(dirt[y].sum())))
    # 车道列带：用下半幅
    ylo = ys.min() + (ys.max() - ys.min()) * 3 // 4
    prof = tan[ylo:ys.max()].sum(axis=0)
    runs, c = [], None
    for x in range(W):
        if prof[x] > 40:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > 15:
                runs.append((c[0], c[1], c[1] - c[0] + 1))
            c = None
    if c and c[1] - c[0] > 15:
        runs.append((c[0], c[1], c[1] - c[0] + 1))
    print("   车道列带(下半幅 y%d..%d) = %s" % (ylo, ys.max(), runs))
