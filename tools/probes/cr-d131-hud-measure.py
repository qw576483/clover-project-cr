#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D131 片判据资产：对局 HUD（手牌栏 + 圣水条 10 格刻度）在**基线图**上的量取，离线可复跑。

判据基线 = `策划/基线图/18_对局HUD_1080x1920.jpg`（1080x1920，与本项目竖屏画布同尺寸
⇒ **像素值 = 画布值**，k=1.0；`18` 与 `19` MD5 相同，只当一张用，见 `策划/基线图/索引.md`）。

本脚本只做**像素判据**（蓝度表决 / 列均值 / 移动中值基线），⛔ 不做 OCR、⛔ 不把命中框当美术语义。

输出（全部是 `HudPanel.cs` 里那几个常量**能反查到的**读数）：
  §1 手牌栏：卡体外沿 / 卡缝 / 整排左右端 / 卡顶卡底
  §2 圣水条：条上下边 / 条右端 / 刻度竖线中心（9 条）+ 最小二乘反解栅格左端与格宽
  §3 圣水徽章：外接框中心

用法（Windows 本机 `python` 无 PIL，用 `py -3`）：
  py -3 tools/probes/cr-d131-hud-measure.py
"""
import os
import statistics
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE = os.path.join(ROOT, "策划", "基线图", "18_对局HUD_1080x1920.jpg")


def blueness(p):
    return p[2] - p[0]


def main():
    im = Image.open(BASE).convert("RGB")
    px = im.load()
    print("基线图 %s  size=%s" % (BASE, im.size))

    # ── §1 手牌栏 ────────────────────────────────────────────────────────────
    print("\n§1 手牌栏（卡片外沿含深色卡框；判据 = 列蓝度 B−R>45 记 1，在 y1640..1779 多数表决）")
    edges = []
    prev = None
    for x in range(120, 780):
        n = t = 0
        for y in range(1640, 1780):
            t += 1
            if blueness(px[x, y]) < 45:
                n += 1
        cur = (n / t) > 0.5
        if prev is None:
            prev = cur
        if cur != prev:
            edges.append((x, "卡起" if cur else "卡止"))
            prev = cur
    print("   边缘:", edges)
    if len(edges) >= 2:
        print("   整排左边=%d  整排右边=%d  整排宽=%d"
              % (edges[0][0], edges[-1][0] - 1, edges[-1][0] - edges[0][0]))
    for i in range(0, len(edges) - 1, 2):
        if i + 1 < len(edges):
            w = edges[i + 1][0] - edges[i][0]
            print("   卡%d x %d..%d 宽=%d" % (i // 2 + 1, edges[i][0], edges[i + 1][0] - 1, w))
    for i in range(1, len(edges) - 1, 2):
        g = edges[i + 1][0] - edges[i][0]
        print("   卡缝 x %d..%d 宽=%d" % (edges[i][0], edges[i + 1][0] - 1, g))

    print("   纵向（x150..275 行多数表决）：")
    prev = False
    for y in range(1560, 1800):
        n = t = 0
        for x in range(150, 275):
            t += 1
            if blueness(px[x, y]) < 45:
                n += 1
        cur = (n / t) > 0.5
        if cur != prev:
            print("     y=%d %s" % (y, "卡起" if cur else "卡止"))
            prev = cur
    print("     （卡顶 1614 / 卡底 1784 ⇒ 高 171；底距画布底 = 1920−1785 = 135）")

    # ── §2 圣水条 ────────────────────────────────────────────────────────────
    print("\n§2 圣水条")
    print("   纵向 x=600 亮度剖面（找条外框）:")
    for y in range(1794, 1845):
        p = px[600, y]
        lum = (p[0] + p[1] + p[2]) / 3
        if lum < 80:
            print("     y=%d lum=%.1f" % (y, lum))
    print("     （条外框 y1798..1841 = 44 高 ⇒ ElixirBarH=44、底距 1920−1841=79）")

    print("   条右端（列均值 y1803..1834 < 80 的最后一列）:")
    last = None
    for x in range(20, 1080):
        s = 0
        for y in range(1803, 1835):
            p = px[x, y]
            s += (p[0] + p[1] + p[2]) / 3
        if s / 32 < 80:
            last = x
    print("     条右端 x=%d（D4=1044）" % last)

    print("   §2.2 刻度竖线自动检测（列均值 y1802..1837 vs 移动中值基线 k=40，段内取最深列）:")
    cols = []
    for x in range(120, 1040):
        s = 0
        for y in range(1802, 1838):
            p = px[x, y]
            s += (p[0] + p[1] + p[2]) / 3
        cols.append(s / 36)

    def med(a, i, k):
        return statistics.median(a[max(0, i - k):min(len(a), i + k + 1)])

    depth = [med(cols, i, 40) - cols[i] for i in range(len(cols))]
    auto = []
    i = 0
    while i < len(depth):
        if depth[i] > 3.0:
            j = i
            while j + 1 < len(depth) and depth[j + 1] > 3.0:
                j += 1
            best = max(range(i, j + 1), key=lambda t: depth[t])
            auto.append((120 + best, round(depth[best], 1)))
            i = j + 1
        else:
            i += 1
    print("     自动候选:", auto)

    # (k, x) 读数：来自 §2.2 的逐列剖面（每条都能在候选里或原列均值里核到）。
    #   k=1 在**品红填充内**（x≈198，暗线叠在填充上）；k=2 = 填充(2 圣水)的右沿 x≈289.5；
    #   k=3..9 在空槽暗底区，全部落在 §2.2 自动候选里（572 那条最浅，未过 3.0 阈值，取自列剖面）。
    pts = [(1, 198.0), (2, 289.5), (3, 384.0), (4, 479.0), (5, 572.0),
           (6, 666.0), (7, 759.0), (8, 853.0), (9, 947.0)]
    n = len(pts)
    mk = sum(k for k, _ in pts) / n
    mx = sum(x for _, x in pts) / n
    num = sum((k - mk) * (x - mx) for k, x in pts)
    den = sum((k - mk) ** 2 for k, _ in pts)
    step = num / den
    left = mx - step * mk
    print("   §2.3 最小二乘（9 点 k=1..9）: 栅格左端=%.2f 每格宽=%.2f 末格右沿=%.2f"
          % (left, step, left + 10 * step))
    print("     残差:", [(k, round(x - (left + k * step), 2)) for k, x in pts])
    print("     交叉核对：自动候选里落在这条栅格 ±2px 内的条数 = %d"
          % sum(1 for x, _ in auto if min(abs(x - (left + k * step)) for k, _ in pts) <= 2.0))

    # ── §2.4 刻度线"深浅"读数（`ElixirTickTint` α=0.30 的出处） ──────────────
    # 刻度线的**颜色**来源：原版 `elixir_bar/d*`（frame_160，1×1）☓ 放置矩阵的颜色项（`0c` 的
    # colorIdx，索引 §2.1 第 2 条：坐标/缩放未解出）⇒ 颜色也拿不到协议值，只能按基线图反解
    # 「刻度处 = 底色 × 多少」。
    def col(x, y):
        return px[x, y]

    def lum(p):
        return (p[0] + p[1] + p[2]) / 3.0

    print("\n   §2.4 刻度线深浅（列均值 y1803..1834；刻度列 vs 邻列基线）:")
    for k, x in ((1, 198), (3, 384), (8, 853)):
        xt = int(round(x))
        acc = [0.0, 0.0, 0.0]
        for c in (xt - 12, xt - 9, xt + 9, xt + 12):
            for y in range(1803, 1835):
                p = col(c, y)
                for i in range(3):
                    acc[i] += p[i]
        base = tuple(v / 128.0 for v in acc)
        acc2 = [0.0, 0.0, 0.0]
        for y in range(1803, 1835):
            p = col(xt, y)
            for i in range(3):
                acc2[i] += p[i]
        tick = tuple(v / 32.0 for v in acc2)
        print("      k=%d x=%d  底色(±9..12px 均)=%s  刻度列均=%s  每通道 Δ=%s"
              % (k, xt, tuple(round(v, 1) for v in base), tuple(round(v, 1) for v in tick),
                 tuple(round(base[i] - tick[i], 1) for i in range(3))))

    # ── §3 徽章 ─────────────────────────────────────────────────────────────
    # 徽章 = 原版 `elixir_bar/elixirBarLeft`（frame_159，原生 94×115 的**水滴**）⇒ 高 > 宽。
    # 判据：徽章中轴 x=63 上，纵向"非竞技场蓝"的连续段 = 徽章外高；横向在 y=1815 上找徽章左外沿
    # （x 从 0 起第一次不再是竞技场蓝的列）。
    print("\n§3 圣水徽章（`ElixirBadgeDx=63` 中轴；判据 = 非竞技场蓝 B−R<45）")
    top = bot = None
    for y in range(1740, 1900):
        if blueness(px[63, y]) < 45:
            if top is None:
                top = y
            bot = y
    leftx = None
    for x in range(0, 100):
        if blueness(px[x, 1815]) < 45:
            leftx = x
            break
    print("   中轴 x=63 纵向 y %s..%s 高=%s ⇒ 竖直心=%.1f"
          % (top, bot, bot - top + 1, (top + bot) / 2))
    print("   y=1815 左外沿 x=%s ⇒ 与中轴 63 的半宽=%s" % (leftx, 63 - leftx))
    print("   （D7：心 (63,1815) 径 60；原生 94×115 ⇒ 宽 60 时高 = 60×115/94 = 73.4）")

    # ── §4 我方实机图 vs 基线（同一把尺子量两张图） ─────────────────────────
    # 判据：把 §2.2 的同一算法套到我方实机 HUD 截图上，逐条对照刻度的**屏幕 x**。
    # 这是「同机位并排」的机械版：两张图都是 1080×1920、同一把尺子 ⇒ 可直接逐条相减。
    live = os.path.join(ROOT, ".ai-tmp", "screenshots", "D131-hud-live-t0.png")
    if not os.path.exists(live):
        print("\n§4 略过：缺我方实机图 %s" % live)
        return 0
    lim = Image.open(live).convert("RGB")
    print("\n§4 我方实机图 %s  size=%s" % (live, lim.size))
    lpx = lim.load()
    lcols = []
    for x in range(120, 1040):
        s = 0
        for y in range(1802, 1838):
            p = lpx[x, y]
            s += (p[0] + p[1] + p[2]) / 3
        lcols.append(s / 36)
    ldepth = [med(lcols, i, 40) - lcols[i] for i in range(len(lcols))]
    lauto = []
    i = 0
    while i < len(ldepth):
        if ldepth[i] > 3.0:
            j = i
            while j + 1 < len(ldepth) and ldepth[j + 1] > 3.0:
                j += 1
            best = max(range(i, j + 1), key=lambda t: ldepth[t])
            lauto.append((120 + best, round(ldepth[best], 1)))
            i = j + 1
        else:
            i += 1
    print("   刻度自动候选（同一算法）:", lauto)
    grid = [left + k * step for k in range(1, 10)]
    odd = [c for c in lauto if min(abs(c[0] - g) for g in grid) <= 8.0]
    print("   落在基线栅格 ±8px 内的候选条数 = %d / %d"
          % (len(odd), len(lauto)))
    print("   逐条对照（基线 9 点 vs 我方最近候选）:")
    for k in range(1, 10):
        bx = left + k * step
        if not lauto:
            break
        cx, d = min(lauto, key=lambda c: abs(c[0] - bx))
        print("     k=%d 基线 x=%.1f  我方 x=%d  差=%+.1fpx"
              % (k, bx, cx, cx - bx))
    print("   我方刻度线深浅（列均值；与 §2.4 同一把尺子）:")
    for k, x in ((3, 382), (8, 851)):
        xt = int(round(x))
        acc = [0.0, 0.0, 0.0]
        for c in (xt - 12, xt - 9, xt + 9, xt + 12):
            for y in range(1803, 1835):
                p = lpx[c, y]
                for i in range(3):
                    acc[i] += p[i]
        base = tuple(v / 128.0 for v in acc)
        acc2 = [0.0, 0.0, 0.0]
        for y in range(1803, 1835):
            p = lpx[xt, y]
            for i in range(3):
                acc2[i] += p[i]
        tick = tuple(v / 32.0 for v in acc2)
        print("      k=%d x=%d  底色=%s  刻度列均=%s  每通道 Δ=%s"
              % (k, xt, tuple(round(v, 1) for v in base), tuple(round(v, 1) for v in tick),
                 tuple(round(base[i] - tick[i], 1) for i in range(3))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
