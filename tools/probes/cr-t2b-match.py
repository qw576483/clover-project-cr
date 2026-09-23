#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2b-match.py —— CR-T2b：把「原版对局图上的卡框」与候选 sprite 做**边框环**比对（只读）。

口径：
  原版卡 = `策划/参考图/20_对局_1080x1920.jpg` 第 3 张手牌（金边外沿实测 x 432..567 / y 1699..1862 = 135×163）；
  候选  = `原版资源/cr-assets-png/assets/sc/ui_out/ui_sprite_NNN.png`（按 alpha bbox 裁出）。
  比法  = 两边都缩放到同一尺寸后，取**外圈环**（排除中心 60%）算逐像素平均绝对差 + 平均色。
用法： python tools/probes/cr-t2b-match.py 547,200,43,880,877
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE = os.path.join(ROOT, "策划", "参考图", "20_对局_1080x1920.jpg")
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "ui_out")
CARD = (432, 1699, 567, 1862)          # 原版第 3 张手牌（金边外沿，G3/D 系列同源口径）


def ring(im, keep=0.62):
    """返回 (边框环像素列表, 平均色)。keep = 保留中心的比例（越小环越宽）。"""
    w, h = im.size
    x0, y0 = int(w * (1 - keep) / 2), int(h * (1 - keep) / 2)
    x1, y1 = w - x0, h - y0
    px = im.load()
    out = []
    for y in range(h):
        for x in range(w):
            if x0 <= x < x1 and y0 <= y < y1:
                continue
            out.append(px[x, y])
    avg = tuple(int(sum(c[i] for c in out) / len(out)) for i in range(3))
    return out, avg


def main():
    base = Image.open(BASE).convert("RGB").crop(CARD)
    ref_ring, ref_avg = ring(base)
    print("原版手牌#3 边框环 平均色 = %s   尺寸=%s" % (str(ref_avg), str(base.size)))
    for s in sys.argv[1].split(","):
        s = s.strip()
        p = os.path.join(SRC, "ui_sprite_%03d.png" % int(s))
        if not os.path.exists(p):
            print("  %-5s MISSING" % s)
            continue
        page = Image.open(p)
        bb = page.getchannel("A").getbbox()
        if not bb:
            print("  %-5s EMPTY" % s)
            continue
        im = page.convert("RGBA").crop(bb)
        # 白底合成（原版截图是有背景的 RGB）
        flat = Image.new("RGB", im.size, (255, 255, 255))
        flat.paste(im, (0, 0), im)
        flat = flat.resize(base.size, Image.LANCZOS)
        cand_ring, cand_avg = ring(flat)
        mad = sum(abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])
                  for a, b in zip(ref_ring, cand_ring)) / (len(ref_ring) * 3)
        print("  %-5s bbox=%s  环平均色=%s  环MAD=%.1f" % (s, str(bb), str(cand_avg), mad))
    return 0


if __name__ == "__main__":
    sys.exit(main())
