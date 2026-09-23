#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2g-card-body.py —— CR-T2g：在**已落地的 UI 图元**里找"卡体"（原版手牌框 = 浅灰卡体露边）。

原版口径（CR-T2f 实测，18 图卡 2）：卡体色 **RGB≈(215,213,216)**、四边在卡面外露出 3~8px、
圆角半径 ≈6~9px、最外缘还有 **1px 深色描边**。
本脚本对 `client/Assets/Resources/Sprites/Ui/**/frame_*.png` 逐张量：
  ① 尺寸 ② 不透明区的**主色**（众数色，量化到 8 级） ③ 主色饱和度 ④ 亮度
  ⑤ 是否"卡形"（w/h 0.6~1.3） ⑥ 边缘 alpha 剖面（判"能不能当卡体/有没有深色描边"）
并按"离 (215,213,216) 的距离"排序，打印最像卡体的那些。
用法： python tools/probes/cr-t2g-card-body.py [topN]
"""
import os
import sys
from collections import Counter

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UI = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui")
TARGET = (215, 213, 216)


def dom_color(im):
    px = im.load()
    w, h = im.size
    c = Counter()
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            q = px[x, y]
            if q[3] < 200:
                continue
            c[(q[0] // 8 * 8, q[1] // 8 * 8, q[2] // 8 * 8)] += 1
    if not c:
        return None, 0.0
    col, n = c.most_common(1)[0]
    return col, n / float(max(1, sum(c.values())))


def profile(path):
    """量一张"卡体"候选：尺寸 / 圆角半径 / 边缘 alpha+RGB 剖面 / 主色（用于定九宫格切边）。"""
    p = path if os.path.isabs(path) else os.path.join(ROOT, path)
    im = Image.open(p).convert("RGBA")
    w, h = im.size
    px = im.load()
    print("== %s  %dx%d" % (os.path.relpath(p, ROOT), w, h))
    print("   顶行 alpha 逐行宽度(前 16 行)：", end=" ")
    for y in range(min(16, h)):
        xs = [x for x in range(w) if px[x, y][3] > 8]
        print("%d" % ((max(xs) - min(xs) + 1) if xs else 0), end=" ")
    print()
    ym = h // 2
    xm = w // 2
    print("   中间行 左缘 10px :", " ".join("%d/%d/%d/%d" % px[i, ym] for i in range(min(10, w))))
    print("   中间行 右缘 10px :", " ".join("%d/%d/%d/%d" % px[w - 1 - i, ym] for i in range(min(10, w))))
    print("   中间列 上缘 10px :", " ".join("%d/%d/%d/%d" % px[xm, i] for i in range(min(10, h))))
    print("   中间列 下缘 10px :", " ".join("%d/%d/%d/%d" % px[xm, h - 1 - i] for i in range(min(10, h))))
    col, frac = dom_color(im)
    print("   主色=%s 占比=%.2f" % (str(col), frac))
    return 0


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "profile":
        return profile(sys.argv[2])
    topn = int(sys.argv[1]) if len(sys.argv) > 1 else 18
    rows = []
    for base, _d, files in os.walk(UI):
        for f in files:
            if not (f.startswith("frame_") and f.endswith(".png")):
                continue
            p = os.path.join(base, f)
            try:
                im = Image.open(p).convert("RGBA")
            except Exception:
                continue
            w, h = im.size
            col, frac = dom_color(im)
            if not col:
                continue
            sat = max(col) - min(col)
            lum = (col[0] * 299 + col[1] * 587 + col[2] * 114) // 1000
            dist = abs(col[0] - TARGET[0]) + abs(col[1] - TARGET[1]) + abs(col[2] - TARGET[2])
            cardish = 0.55 <= w / float(h) <= 1.35 and w >= 80 and h >= 100
            rel = os.path.relpath(p, UI)
            rows.append((dist, col, sat, lum, frac, w, h, cardish, rel))
    rows.sort()
    print("=== 已落地 UI 图元里最接近「浅灰卡体 (215,213,216)」的前 %d 张 ===" % topn)
    for dist, col, sat, lum, frac, w, h, cardish, rel in rows[:topn]:
        print("  d=%3d  col=%-16s sat=%2d lum=%3d 主色占比=%.2f  %dx%d 卡形=%s  %s"
              % (dist, str(col), sat, lum, frac, w, h, "Y" if cardish else "n", rel))
    print("=== 卡形(w80+,h100+,0.55~1.35) 且 亮度>=170 且 饱和<=40 的清单 ===")
    for dist, col, sat, lum, frac, w, h, cardish, rel in rows:
        if cardish and lum >= 170 and sat <= 40:
            print("  col=%-16s sat=%2d lum=%3d  %dx%d  %s" % (str(col), sat, lum, w, h, rel))
    return 0


if __name__ == "__main__":
    sys.exit(main())
