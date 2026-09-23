#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2c-base-row.py —— CR-T2c：量原版手牌排的**整排布局**（用于 1:1 对齐 CardW/CardH/Gap/Left/距底）。

图 = `策划/参考图/20_对局_1080x1920.jpg`（1080×1920 = 本项目画布 ⇒ 像素 = 画布值）。
判据（机械）：
  卡框金 = 与 (244,244,130) 距离 < 120 的像素（原版卡的金边，实测 x=288 → (244,244,130)）；
  圣水条 = 品红 (r>200,g<120,b>200)。
输出：每张卡的左右/上下金边、卡宽、间距、整排左右端、卡底距底、圣水条 bbox。
"""
import io
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
IMG = os.path.join(ROOT, "策划", "参考图", "20_对局_1080x1920.jpg")
GOLD = (244, 244, 130)


def near(p, q, t):
    return abs(p[0] - q[0]) + abs(p[1] - q[1]) + abs(p[2] - q[2]) < t


def main():
    im = Image.open(IMG).convert("RGB")
    W, H = im.size
    px = im.load()
    band = range(1600, 1920)

    # 逐列统计"金"像素数（只在手牌带内）
    colgold = [0] * W
    for x in range(W):
        c = 0
        for y in band:
            if near(px[x, y], GOLD, 120):
                c += 1
        colgold[x] = c
    runs = []
    s = None
    for x in range(W):
        if colgold[x] >= 20:
            if s is None:
                s = x
        else:
            if s is not None and x - s >= 3:
                runs.append((s, x))
            s = None
    print("金色竖列段（>=20 金像素）: %s" % runs)
    # 逐行统计金像素（卡的上/下金边）
    rowgold = []
    for y in range(1600, 1920):
        c = 0
        for x in range(140, 720):
            if near(px[x, y], GOLD, 120):
                c += 1
        rowgold.append((y, c))
    strong = [y for y, c in rowgold if c >= 60]
    print("金色横行（>=60 金像素）: %s .. %s" % (strong[:6], strong[-6:]))

    # 圣水条
    mag_x = [x for x in range(W) if any(near(px[x, y], (255, 0, 255), 200) or
                                        (px[x, y][0] > 200 and px[x, y][2] > 200 and px[x, y][1] < 140)
                                        for y in range(1750, 1920, 3))]
    mag_y = [y for y in range(1750, 1920) if any(
        (px[x, y][0] > 200 and px[x, y][2] > 200 and px[x, y][1] < 140) for x in range(100, 1000, 4))]
    print("圣水条：x %s..%s   行 %s..%s" % (min(mag_x) if mag_x else '-', max(mag_x) if mag_x else '-',
                                          min(mag_y) if mag_y else '-', max(mag_y) if mag_y else '-'))
    return 0


if __name__ == "__main__":
    sys.exit(main())
