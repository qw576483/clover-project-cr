# CR-V1: 所有竞技场 _tex_.png / _tex.png 的缩略联络图（找与参考图 03 同构的那一套）
# 复跑: python .ai-tmp/test/cr-v1-arena-sheet.py
import os, sys
import numpy as np
from PIL import Image, ImageDraw
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
D = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")

names = sorted({f.replace("_tex_.png", "").replace("_tex.png", "")
                for f in os.listdir(D) if (f.endswith("_tex.png") or f.endswith("_tex_.png"))
                and (f.startswith("level_") or f.startswith("arena_training"))})
CW, CH, COLS = 300, 300, 5
rows = (len(names) + COLS - 1) // COLS
sheet = Image.new("RGB", (CW * COLS, CH * rows), (255, 0, 255))
d = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    p = os.path.join(D, n + "_tex_.png")
    if not os.path.isfile(p):
        p = os.path.join(D, n + "_tex.png")
    im = Image.open(p).convert("RGBA")
    a = np.asarray(im)[:, :, 3] > 32
    ys, xs = np.where(a)
    if ys.size:
        im = im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    w, h = im.size
    sc = min(CW / float(w), (CH - 16) / float(h))
    im = im.resize((max(1, int(w * sc)), max(1, int(h * sc))), Image.LANCZOS)
    bg = Image.new("RGBA", im.size, (255, 0, 255, 255)); bg.alpha_composite(im)
    cx, cy = (i % COLS) * CW, (i // COLS) * CH
    sheet.paste(bg.convert("RGB"), (cx + (CW - im.size[0]) // 2, cy + 16))
    d.text((cx + 4, cy + 2), "%s %dx%d" % (n, w, h), fill=(0, 0, 0))
    print("%-32s %dx%d" % (n, w, h))
out = os.path.join(ROOT, ".ai-tmp", "test", "CR-V1-arena-sheet.png")
sheet.save(out)
print("saved", out, sheet.size)
