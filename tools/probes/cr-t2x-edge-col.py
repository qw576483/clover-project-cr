#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2x-edge-col.py —— 「手牌 #1 卡缘列」构建判别口径的**独立复核**（离线，无 Play）。

背景：CR-T3 拿它自己 4 张帧量了左缘列 x=146 与右缘列 x=282，发现左缘列**不是** 100% 纯黑
（66.3% 黑 + 23.8% 草）⇒ 我原先给的"100% 草绿 / 100% 纯黑"口径过强。本脚本用**另一张独立图**
（CR-T2 自己 01:03:48 的实机图，非 CR-T3 帧）量同样的两列，看结论是否一致 + 说明残差来源。

判据（与两侧一致）：black=max(RGB)<=25；white=min(RGB)>=230；grass=g>r+20 且 g>b+20 且 g>110；其余=other
用法： python tools/probes/cr-t2x-edge-col.py [image]
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEF = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2-hud-mine.png")
COLS = [("左缘 x=146", 146), ("右缘 x=282", 282)]
ROWS = (1614, 1785)          # 我方 Hand0 rect y1614..1785（dump 实测）


def cls(q):
    r, g, b = q[:3]
    if max(r, g, b) <= 25:
        return "black"
    if min(r, g, b) >= 230:
        return "white"
    if g > r + 20 and g > b + 20 and g > 110:
        return "grass"
    return "other"


def rowscan(path):
    """沿手牌行做逐 x 的亮度/色泽扫描 ⇒ 判"旧槽底 ui_out/200（α≈0.23 深色板）到底画没画"：
    若画了，卡区（x144..284）会整体比**紧邻的外侧**暗 ≈20%；没画则两侧亮度同级。
    这决定了文档里写「实机不渲染」还是「渲染了但只是 23% 暗罩」。"""
    im = Image.open(path).convert("RGB")
    p = im.load()
    print("rowscan: %s" % os.path.relpath(path, ROOT))
    print("  y 均值带 1630..1790；每 10px 一档，输出 x / 均值亮度 / 均值色")
    for x0 in range(40, 460, 10):
        sr = sg = sb = 0
        n = 0
        for x in range(x0, x0 + 10):
            for y in range(1630, 1790, 4):
                q = p[x, y]
                sr += q[0]
                sg += q[1]
                sb += q[2]
                n += 1
        r, g, b = sr // n, sg // n, sb // n
        lum = (r * 299 + g * 587 + b * 114) // 1000
        bar = "#" * max(0, (lum - 40) // 8)
        print("  x=%3d..%3d  lum=%3d  rgb=(%3d,%3d,%3d)  %s" % (x0, x0 + 9, lum, r, g, b, bar))
    return 0


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--rowscan":
        return rowscan(sys.argv[2])
    path = sys.argv[1] if len(sys.argv) > 1 else DEF
    im = Image.open(path).convert("RGB")
    print("图：%s  %dx%d" % (os.path.relpath(path, ROOT), im.size[0], im.size[1]))
    print("列范围 y=%d..%d（%d 行）" % (ROWS[0], ROWS[1] - 1, ROWS[1] - ROWS[0]))
    for name, x in COLS:
        p = im.load()
        c = {}
        for y in range(ROWS[0], ROWS[1]):
            k = cls(p[x, y])
            c[k] = c.get(k, 0) + 1
        tot = float(sum(c.values()))
        dom = max(c.items(), key=lambda t: t[1])[0]
        parts = "  ".join("%s=%d(%.1f%%)" % (k, c[k], 100.0 * c[k] / tot)
                          for k in ("black", "white", "grass", "other") if k in c)
        print("  %-12s %s   dominant=%s" % (name, parts, dom))
    # 残差来源：卡体(ui_out/43) 圆角半径 ~20 ⇒ 列的两端各 ~20 行落在圆角外
    print("残差说明：frame_43 圆角半径 ≈20px（G3/本片实测）⇒ 卡缘列的**上下各约 20 行**落在圆角外，")
    print("          那里露出的就是背景 ⇒ 左缘列出现 grass/other、右缘列出现少量 black（底边）。")
    print("          故该列只作**方向性二值判别**，不写「100%」。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
