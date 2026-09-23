#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2b-frame-hunt.py —— CR-T2b：在原版 `ui_out` 的 914 张 sprite 里**筛「卡框」候选**（只读）。

判据（机械，不靠命名 —— 原版 `ui` 表里大量 shape **没有语义名**）：
  1) 尺寸落在卡片框区间（宽 90..200、高 110..230，高>宽）；
  2) **外圈**是暖金色（r>170, g 120..225, b<130）的像素占比高 ⇒ 金框；
  3) **中心**大面积透明（alpha<=8 占比高）⇒ 是"框"不是"底"；
  4) 边框不能太细（金像素占比 > 6%）。
输出 top N，并按分数排序。
用法： python tools/probes/cr-t2b-frame-hunt.py [N]
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "ui_out")
OUT = os.path.join(ROOT, ".ai-tmp", "test")


def goldish(p):
    r, g, b, a = p
    return a > 200 and r > 170 and 110 < g < 230 and b < 140


def score(path):
    # ⚠️ `原版资源/.../ui_out/ui_sprite_NNN.png` 是**页面级** 1663×2810（每张只保留自己那块不透明）
    # ⇒ 必须先用 alpha bbox 裁出这块，再按尺寸/颜色判。
    page = Image.open(path)
    bb = page.getchannel("A").getbbox()
    if not bb:
        return None
    im = page.convert("RGBA").crop(bb)
    w, h = im.size
    if not (90 <= w <= 210 and 110 <= h <= 240 and h > w):
        return None
    px = im.load()
    # ⚠️ 不能只看最外 2px：卡框的金带往往内缩若干像素（外圈是透明留白）⇒ 量**整张**的金占比
    gold = tot = 0
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            tot += 1
            if goldish(px[x, y]):
                gold += 1
    ring_gold = gold / float(tot or 1)
    # 中心透明率
    cx0, cy0, cx1, cy1 = int(w * .25), int(h * .25), int(w * .75), int(h * .75)
    clear = c = 0
    for y in range(cy0, cy1, 2):
        for x in range(cx0, cx1, 2):
            c += 1
            if px[x, y][3] <= 8:
                clear += 1
    center_clear = clear / float(c or 1)
    return ring_gold, center_clear, w, h


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 15
    rows = []
    for f in sorted(os.listdir(SRC)):
        if not f.endswith(".png"):
            continue
        r = score(os.path.join(SRC, f))
        if not r:
            continue
        ring_gold, center_clear, w, h = r
        rows.append((ring_gold * 0.6 + center_clear * 0.4, f, ring_gold, center_clear, w, h))
    rows.sort(reverse=True)
    print("卡片尺寸的 sprite 共 %d 张；下面按「金占比×0.6 + 中心透明率×0.4」排序前 %d" % (len(rows), n))
    for s, f, rg, cc, w, h in rows[:n]:
        print("  score=%.3f  %-22s goldFrac=%.2f centerClear=%.2f  %dx%d" % (s, f, rg, cc, w, h))
    return 0


if __name__ == "__main__":
    sys.exit(main())
