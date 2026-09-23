# CR-V1: 查看素材（可选裁切 + 等比缩放；透明区垫品红便于看边界）
# 用法: python .ai-tmp/test/cr-v1-view.py <img> <out.png> [maxdim] [x0 y0 x1 y1]
import os, sys
import numpy as np
from PIL import Image
sys.stdout.reconfigure(encoding="utf-8")
ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
img = sys.argv[1]
if not os.path.isabs(img):
    img = os.path.join(ROOT, img)
out = sys.argv[2]
if not os.path.isabs(out):
    out = os.path.join(ROOT, out)
maxdim = int(sys.argv[3]) if len(sys.argv) > 3 else 900
im = Image.open(img).convert("RGBA")
print("src", im.size, "=", os.path.basename(img))
if len(sys.argv) > 7:
    x0, y0, x1, y1 = (int(v) for v in sys.argv[4:8])
    im = im.crop((x0, y0, x1, y1))
    print("crop", (x0, y0, x1, y1), "->", im.size)
# alpha 合成到品红
bg = Image.new("RGBA", im.size, (255, 0, 255, 255))
bg.alpha_composite(im)
im = bg.convert("RGB")
w, h = im.size
sc = min(1.0, float(maxdim) / max(w, h))
if sc < 1.0:
    im = im.resize((max(1, int(w * sc)), max(1, int(h * sc))), Image.LANCZOS)
    print("scaled to", im.size)
os.makedirs(os.path.dirname(out), exist_ok=True)
im.save(out)
print("saved", out)
