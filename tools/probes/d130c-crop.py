#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130c 裁带工具：按行区间裁一条横带出来看（可放大），供"用眼睛定死边沿"用。

只读 + 只写出图，不改产品代码。
用法：
  python d130c-crop.py <img> <y0> <y1> <out.png> [--x0 PX] [--x1 PX] [--scale F]
  python d130c-crop.py <img> --sbs <out.png> --marks "1200,1582,1700"   # 全图 + 水平标线
"""
import os
import sys

from PIL import Image, ImageDraw


def crop(path, y0, y1, out, x0=None, x1=None, scale=1.0):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    x0 = 0 if x0 is None else max(0, x0)
    x1 = W if x1 is None else min(W, x1)
    y0 = max(0, y0)
    y1 = min(H, y1)
    c = im.crop((x0, y0, x1, y1))
    if scale != 1.0:
        c = c.resize((int(c.width * scale), int(c.height * scale)), Image.NEAREST)
    c.save(out)
    print("wrote %s  (%dx%d)  src y[%d,%d) x[%d,%d)"
          % (out, c.width, c.height, y0, y1, x0, x1))


def with_marks(path, out, marks, scale=1.0):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    if scale != 1.0:
        im = im.resize((int(W * scale), int(H * scale)), Image.NEAREST)
    d = ImageDraw.Draw(im)
    for m in marks:
        y = int(m * scale)
        if 0 <= y < im.height:
            d.line([(0, y), (im.width, y)], fill=(255, 0, 0), width=2)
            d.text((4, min(y + 3, im.height - 12)), "y=%d" % m, fill=(255, 0, 0))
    im.save(out)
    print("wrote %s (%dx%d) marks=%s" % (out, im.width, im.height, marks))


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    if "--sbs" in argv:
        out = argv[argv.index("--sbs") + 1]
        marks = []
        if "--marks" in argv:
            marks = [int(v) for v in argv[argv.index("--marks") + 1].split(",") if v.strip()]
        scale = 1.0
        if "--scale" in argv:
            scale = float(argv[argv.index("--scale") + 1])
        with_marks(path, out, marks, scale)
        return 0
    y0, y1, out = int(argv[2]), int(argv[3]), argv[4]
    x0 = x1 = None
    scale = 1.0
    i = 5
    while i < len(argv):
        if argv[i] == "--x0":
            x0 = int(argv[i + 1]); i += 2
        elif argv[i] == "--x1":
            x1 = int(argv[i + 1]); i += 2
        elif argv[i] == "--scale":
            scale = float(argv[i + 1]); i += 2
        else:
            print("unknown arg %s" % argv[i]); return 2
    crop(path, y0, y1, out, x0, x1, scale)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
