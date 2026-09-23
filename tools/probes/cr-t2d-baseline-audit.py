#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2d-baseline-audit.py —— 审计两张对局基线图（`18_对局HUD` vs `20_对局`）：
  "谁是与我们要复刻版本一致的原版实机截图？"（主 agent 裁定前只查证，⛔ 不改代码）

输出（每张图）：
  1) md5 / 像素尺寸 / 文件大小
  2) **黑边/裁切痕迹**：逐行"整行接近纯黑"的比例（letterbox 会整行黑）
  3) **宣传字/水印痕迹**：底部/顶部 200 行里"高对比小像素"占比（文字笔画的特征）
     + 逐行给出该占比 → 哪几行异常高，就是有叠字
  4) **圣水条**（品红）行范围/列范围 —— 原版 HUD 的贴底锚点元件，用它的"距底"做档位对照
  5) **右上计时板**（深色板）bbox —— 顶部锚点元件的尺寸，用它的尺寸做"是否整体缩放"的对照
用法： python tools/probes/cr-t2d-baseline-audit.py
"""
import hashlib
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")
FILES = ["18_对局HUD_1080x1920.jpg", "20_对局_1080x1920.jpg"]


def magenta(p):
    r, g, b = p[0], p[1], p[2]
    return r > 190 and b > 150 and g < 150 and (r - g) > 60


def transitions(im, x, y0, y1, thr=70):
    px = im.load()
    out = []
    prev = px[x, y0]
    for y in range(y0 + 1, y1):
        c = px[x, y]
        if abs(c[0] - prev[0]) + abs(c[1] - prev[1]) + abs(c[2] - prev[2]) > thr:
            out.append((y, prev, c))
        prev = c
    return out


def framebox(name, x0=0, x1=None, ytop=None, ybot=1920):
    """在给定图里找**手牌卡框**的左右/上下边（2.1.5 的框是浅灰白：高亮度 + 低饱和）。
    用来给"换基线图重解内缩"提供**可复现的卡框口径**（⛔ 不靠目测）。"""
    p = os.path.join(REF, name)
    im = Image.open(p).convert("RGB")
    W, H = im.size
    x1 = x1 or W
    ytop = ytop or int(H * 0.80)
    px = im.load()

    def frameish(q):
        r, g, b = q
        return max(r, g, b) > 165 and (max(r, g, b) - min(r, g, b)) < 45

    cols = []
    for x in range(x0, x1):
        c = sum(1 for y in range(ytop, ybot) if frameish(px[x, y]))
        cols.append((x, c))
    runs = []
    s = None
    for x, c in cols:
        if c >= 12:
            if s is None:
                s = x
        else:
            if s is not None and x - s >= 3:
                runs.append((s, x))
            s = None
    if s is not None:
        runs.append((s, x1))
    print("%s  浅灰白-边框列-段(>=12) = %s" % (name, runs))
    rows = []
    for y in range(ytop, ybot):
        c = sum(1 for x in range(x0, x1, 2) if frameish(px[x, y]))
        rows.append((y, c))
    strong = [y for y, c in rows if c >= 60]
    print("%s  浅灰白-边框行-段(>=60) = %s" % (name, ("%d..%d" % (min(strong), max(strong))) if strong else "无"))
    return 0


def captop(name, x0, y0, x1, band=44):
    """量某张卡**顶部**那一条的颜色构成：是不是"深紫帽 + 双菱形"（20 图那套）。
    输出：该条的主色、以及 与 (32,16,65) 距离<60 的像素占比。"""
    p = os.path.join(REF, name)
    px = Image.open(p).convert("RGB").load()
    tot = 0
    purple = 0
    acc = [0, 0, 0]
    for y in range(y0, y0 + band):
        for x in range(x0, x1):
            q = px[x, y]
            tot += 1
            acc[0] += q[0]
            acc[1] += q[1]
            acc[2] += q[2]
            if abs(q[0] - 32) + abs(q[1] - 16) + abs(q[2] - 65) < 60:
                purple += 1
    avg = tuple(v // max(1, tot) for v in acc)
    print("%s  卡顶条 x%d..%d y%d..%d：平均色=%s  深紫(32,16,65)±60 占比=%.3f"
          % (name, x0, x1, y0, y0 + band, str(avg), purple / float(tot or 1)))
    return 0


def frameinsets(name, x0, y0, x1, y1):
    """在给定卡框矩形内，**直接量框的厚度** = 卡面四边内缩（不依赖颜色匹配 ⇒ 对灰化手牌也成立）。
    框 = 从边缘起连续满足"亮度>150 且低饱和"的像素带；一旦遇到彩色/深色像素即认为进入卡面。"""
    px = Image.open(os.path.join(REF, name)).convert("RGB").load()

    def frameish(q):
        r, g, b = q
        return max(r, g, b) > 150 and (max(r, g, b) - min(r, g, b)) < 55

    ym = (y0 + y1) // 2
    xm = (x0 + x1) // 2
    L = 0
    while L < 80 and frameish(px[x0 + L, ym]):
        L += 1
    R = 0
    while R < 80 and frameish(px[x1 - 1 - R, ym]):
        R += 1
    T = 0
    while T < 80 and frameish(px[xm, y0 + T]):
        T += 1
    B = 0
    while B < 80 and frameish(px[xm, y1 - 1 - B]):
        B += 1
    w, h = x1 - x0, y1 - y0
    print("%s card=(%d,%d,%d,%d) %dx%d  内缩px 左=%d 上=%d 右=%d 下=%d"
          % (name, x0, y0, x1, y1, w, h, L, T, R, B))
    print("   比例 = 左%.4f 上%.4f 右%.4f 下%.4f   卡面 = %dx%d (%.2f%% x %.2f%%)"
          % (L / float(w), T / float(h), R / float(w), B / float(h),
             w - L - R, h - T - B, (w - L - R) * 100.0 / w, (h - T - B) * 100.0 / h))
    return 0


def badges(name):
    """量圣水**费用泡**（卡左下角的品红圆形）的中心与直径 —— 它是"卡框"的标尺：
    18 图上已知卡框（x 144 起、单卡 140），泡中心相对卡左/卡底的偏移可由 18 标定，
    再用同一套偏移去别的缩放档的图（k 由**泡间距/卡间距**比值给出）。"""
    im = Image.open(os.path.join(REF, name)).convert("RGB")
    W, H = im.size
    px = im.load()
    y0, y1 = int(H * 0.84), H

    def mag(q):
        r, g, b = q
        return r > 170 and b > 130 and g < 150 and (r - g) > 50

    cols = []
    for x in range(W):
        c = sum(1 for y in range(y0, y1, 2) if mag(px[x, y]))
        cols.append(c)
    runs = []
    s = None
    for x, c in enumerate(cols):
        if c >= 6:
            if s is None:
                s = x
        else:
            if s is not None and 20 <= x - s <= 90:
                runs.append((s, x))
            s = None
    print("%s  %dx%d  品红竖段(费用泡/圣水条) = %s" % (name, W, H, runs))
    for (a, b) in runs:
        cx = (a + b) // 2
        ys = [y for y in range(y0, y1) if any(mag(px[x, y]) for x in range(a, b, 3))]
        if ys:
            print("     run x %d..%d  cx=%d  w=%d   y %d..%d  h=%d"
                  % (a, b, cx, b - a, min(ys), max(ys), max(ys) - min(ys) + 1))
    return 0


def columns():
    """按列看颜色突变 → 手牌卡的上/下沿、圣水条的上/下沿（两张图同列同阈值，可比）。"""
    for f in FILES:
        p = os.path.join(REF, f)
        im = Image.open(p).convert("RGB")
        px = im.load()
        print("=" * 78)
        print(f)
        for x in (200, 350, 620):
            print("  --- x=%d ---" % x)
            for y, a, b in transitions(im, x, 1580, 1920):
                print("     y=%4d  %s -> %s" % (y, a, b))
    return 0


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "columns":
        return columns()
    if len(sys.argv) > 1 and sys.argv[1] == "framebox":
        return framebox(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 0,
                        int(sys.argv[4]) if len(sys.argv) > 4 else None)
    if len(sys.argv) > 1 and sys.argv[1] == "badges":
        return badges(sys.argv[2])
    if len(sys.argv) > 1 and sys.argv[1] == "frameinsets":
        return frameinsets(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]))
    if len(sys.argv) > 1 and sys.argv[1] == "captop":
        return captop(sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]),
                      int(sys.argv[6]) if len(sys.argv) > 6 else 44)
    for f in FILES:
        p = os.path.join(REF, f)
        if not os.path.exists(p):
            print("MISSING %s" % p)
            continue
        data = open(p, "rb").read()
        im = Image.open(p).convert("RGB")
        W, H = im.size
        px = im.load()
        print("=" * 78)
        print("%s\n  md5=%s  size=%dx%d  bytes=%d" % (f, hashlib.md5(data).hexdigest(), W, H, len(data)))

        # 2) 黑边：整行 95% 以上像素 < 12
        rows_black = []
        for y in range(H):
            dark = sum(1 for x in range(0, W, 8) if sum(px[x, y]) < 36)
            if dark >= (W // 8) * 0.95:
                rows_black.append(y)
        print("  近黑整行（letterbox 嫌疑）= %s" % (("连续 %d..%d" % (min(rows_black), max(rows_black)))
                                              if rows_black else "无"))

        # 3) 文字痕迹：底部/顶部各 200 行的"高对比边缘"占比（相邻像素差 > 60 且是亮像素）
        def edge_rate(y0, y1):
            out = []
            for y in range(y0, y1, 4):
                c = t = 0
                for x in range(0, W, 4):
                    t += 1
                    a = px[x, y]
                    b = px[min(W - 1, x + 4), y]
                    if sum(a) > 330 and abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2]) > 70:
                        c += 1
                out.append((y, c / float(t)))
            return out

        top = edge_rate(0, 200)
        bot = edge_rate(H - 200, H)
        def hot(rows, thr=0.10):
            return [(y, round(r, 2)) for y, r in rows if r > thr]
        print("  顶部 0..200 高对比亮像素>10%% 的行 = %s" % (hot(top) or "无"))
        print("  底部 %d..%d 同上 = %s" % (H - 200, H, hot(bot) or "无"))

        # 4) 圣水条（品红）
        mag_rows = [y for y in range(int(H * 0.85), H) if sum(1 for x in range(0, W, 6) if magenta(px[x, y])) > 20]
        mag_cols = [x for x in range(W) if sum(1 for y in range(int(H * 0.85), H, 3) if magenta(px[x, y])) > 3]
        if mag_rows:
            print("  圣水条：行 %d..%d（高 %d）  距底 %d   列 %d..%d（宽 %d）"
                  % (min(mag_rows), max(mag_rows), max(mag_rows) - min(mag_rows) + 1, H - 1 - max(mag_rows),
                     min(mag_cols), max(mag_cols), max(mag_cols) - min(mag_cols) + 1))
        else:
            print("  圣水条：未找到（品红阈值过严）")

        # 5) 右上计时板（深板）：在右上 300×200 里找"非竞技场"的深色矩形
        dark_cells = []
        for y in range(20, 220, 2):
            for x in range(W - 300, W - 10, 2):
                r, g, b = px[x, y]
                if r < 90 and g < 90 and b < 110:
                    dark_cells.append((x, y))
        if dark_cells:
            xs = [c[0] for c in dark_cells]
            ys = [c[1] for c in dark_cells]
            print("  右上深色板（粗略）: x %d..%d (w %d)  y %d..%d (h %d)"
                  % (min(xs), max(xs), max(xs) - min(xs), min(ys), max(ys), max(ys) - min(ys)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
