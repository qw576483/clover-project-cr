#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
measure-hud2.py  ——  对局 HUD 几何量取辅助工具（离线，不依赖 Unity）
用途：把参考图中的 HUD 区域裁出、放大 2~3 倍、叠上「网格 + 像素标尺（原图坐标）」，
      导出到 <项目根>/.ai-tmp/screenshots/ 供人工读数。
用法：
  python tools/probes/measure-hud2.py crops   # 生成标尺裁图
  python tools/probes/measure-hud2.py scan <img> <x0> <x1> <y0> <y1>  # 列/行颜色扫描
说明：脚本只读参考图、只写 .ai-tmp/screenshots/，不改任何项目文件。
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")
OUT = os.path.join(ROOT, ".ai-tmp", "screenshots")

# 要量的图：(文件名, 显示名)
IMAGES = [
    ("18_对局HUD_1080x1920.jpg", "18"),
    ("20_对局_1080x1920.jpg", "20"),
    ("21_对局HUD_861x1920.jpg", "21"),
    ("23_对局HUD_720x1280.jpg", "23"),
]


def ruler(img, box, scale, path, step=20, major=100, label_every=100):
    """裁剪 box=(x0,y0,x1,y1)（原图坐标），放大 scale 倍，叠加原图坐标网格。"""
    x0, y0, x1, y1 = box
    crop = img.crop(box).convert("RGB")
    w, h = crop.size
    big = crop.resize((w * scale, h * scale), Image.NEAREST)
    d = ImageDraw.Draw(big)
    # 竖线：x = x0 起每 step 原图像素一条
    gx = (x0 // step) * step
    while gx <= x1:
        if gx >= x0:
            px = (gx - x0) * scale
            is_major = (gx % major == 0)
            col = (255, 0, 0) if is_major else (0, 200, 255)
            d.line([(px, 0), (px, big.size[1])], fill=col, width=2 if is_major else 1)
            if gx % label_every == 0:
                d.text((px + 2, 2), str(gx), fill=(255, 255, 0))
                d.text((px + 2, big.size[1] - 14), str(gx), fill=(255, 255, 0))
        gx += step
    gy = (y0 // step) * step
    while gy <= y1:
        if gy >= y0:
            py = (gy - y0) * scale
            is_major = (gy % major == 0)
            col = (255, 0, 0) if is_major else (0, 200, 255)
            d.line([(0, py), (big.size[0], py)], fill=col, width=2 if is_major else 1)
            if gy % label_every == 0:
                d.text((2, py + 2), str(gy), fill=(255, 255, 0))
                d.text((big.size[0] - 34, py + 2), str(gy), fill=(255, 255, 0))
        gy += step
    big.save(path)
    print("saved %s  crop=%s  scale=%d  out=%s" % (os.path.basename(path), box, scale, big.size))


def col_scan(img, x, y0, y1, name=""):
    """打印某列自上而下的颜色变化（只打印与上一像素差异大的行）。"""
    px = img.load()
    prev = None
    print("--- col x=%d  y=%d..%d  %s" % (x, y0, y1, name))
    for y in range(y0, y1):
        c = px[x, y][:3]
        if prev is None or max(abs(c[i] - prev[i]) for i in range(3)) > 40:
            print("  y=%4d  rgb=%s" % (y, c))
        prev = c


def row_scan(img, y, x0, x1, name=""):
    px = img.load()
    prev = None
    print("--- row y=%d  x=%d..%d  %s" % (y, x0, x1, name))
    for x in range(x0, x1):
        c = px[x, y][:3]
        if prev is None or max(abs(c[i] - prev[i]) for i in range(3)) > 40:
            print("  x=%4d  rgb=%s" % (x, c))
        prev = c


def cmd_box(argv):
    """crop <img> <x0> <y0> <x1> <y1> <scale> <outname>"""
    fn, x0, y0, x1, y1, sc = argv[0], int(argv[1]), int(argv[2]), int(argv[3]), int(argv[4]), int(argv[5])
    name = argv[6]
    os.makedirs(OUT, exist_ok=True)
    img = Image.open(os.path.join(REF, fn)).convert("RGB")
    ruler(img, (x0, y0, x1, y1), sc, os.path.join(OUT, name), step=20, major=100)


def cmd_crops():
    os.makedirs(OUT, exist_ok=True)
    for fn, tag in IMAGES:
        p = os.path.join(REF, fn)
        if not os.path.exists(p):
            print("MISSING %s" % fn)
            continue
        img = Image.open(p).convert("RGB")
        W, H = img.size
        print("== %s  size=%dx%d" % (fn, W, H))
        ytop = int(H * 0.75)
        # 底部 HUD 全宽（含圣水条、手牌排）
        ruler(img, (0, ytop, W, H), 2, os.path.join(OUT, "hud_bottom_%s.png" % tag), step=20, major=100)
        # 顶部 HUD（倒计时 / 冠数）
        ruler(img, (0, 0, W, int(H * 0.06)), 3, os.path.join(OUT, "hud_top_%s.png" % tag), step=20, major=100)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return
    cmd = sys.argv[1]
    if cmd == "crops":
        cmd_crops()
    elif cmd == "box":
        cmd_box(sys.argv[2:])
    elif cmd == "col":
        fn, x, y0, y1 = sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
        img = Image.open(os.path.join(REF, fn)).convert("RGB")
        col_scan(img, x, y0, y1, fn)
    elif cmd == "row":
        fn, y, x0, x1 = sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
        img = Image.open(os.path.join(REF, fn)).convert("RGB")
        row_scan(img, y, x0, x1, fn)
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
