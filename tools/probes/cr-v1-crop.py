# CR-V1: 裁图 + 叠像素网格（读原版参考图的河/桥/地面）
# 用法: python .ai-tmp/test/cr-v1-crop.py <img> <out.png> <x0> <y0> <x1> <y1> [scale] [step]
import sys
import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
img = sys.argv[1]
if not os.path.isabs(img):
    img = os.path.join(ROOT, img)
out = sys.argv[2]
if not os.path.isabs(out):
    out = os.path.join(ROOT, out)
x0, y0, x1, y1 = (int(v) for v in sys.argv[3:7])
scale = int(sys.argv[7]) if len(sys.argv) > 7 else 2
step = int(sys.argv[8]) if len(sys.argv) > 8 else 0

im = Image.open(img).convert("RGB").crop((x0, y0, x1, y1))
w, h = im.size
im = im.resize((w * scale, h * scale), Image.NEAREST)
d = ImageDraw.Draw(im)
for x in range(0, w + 1, 10 if step == 0 else step):
    d.line([(x * scale, 0), (x * scale, h * scale)], fill=(255, 0, 255), width=1)
    if (x // (10 if step == 0 else step)) % 2 == 0:
        d.text((x * scale + 2, 2), str(x + x0), fill=(255, 0, 255))
for y in range(0, h + 1, 10 if step == 0 else step):
    d.line([(0, y * scale), (w * scale, y * scale)], fill=(0, 255, 255), width=1)
    if (y // (10 if step == 0 else step)) % 2 == 0:
        d.text((2, y * scale + 2), str(y + y0), fill=(0, 255, 255))
os.makedirs(os.path.dirname(out), exist_ok=True)
im.save(out)
print("saved", out, im.size, "src box", (x0, y0, x1, y1), "scale", scale, "step", step)
