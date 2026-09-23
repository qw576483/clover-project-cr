#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2e-scale.py —— CR-T2e：用**原版 HUD 里最硬的横向量具**（圣水条）反推其它基线图的缩放档 k，
再把 `18_对局HUD` 上量到的手牌卡框（D9/D11/D12）折算过去，得到"换基线图重解内缩"的卡框口径。

为什么用圣水条：18 图上圣水条的左右端是 D5 量过的（x 88..1044，宽 956），且它是**贴底锚定**的
横条 —— 在别的缩放档里只有长度与位置按 k 变，形状不变 ⇒ 可当标尺。

用法： python tools/probes/cr-t2e-scale.py
输出：每张图的黑底 -> k -> 该图 4 张手牌的卡框矩形（供 `cr-t2-fit-solve.py` 直接用）。
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")

# 18 图（= 裁定基线）上的三个量：D5 圣水条左端 / D9 整排左边 / D11 单卡宽 / D12 卡高 / D13 距底
BAR_LEFT_18, BAR_W_18 = 88, 956
ROW_LEFT_18, CARDW_18, CARDH_18, BOTTOM_18 = 144, 140, 171, 135
GAP_18 = 3
IMGS = ["18_对局HUD_1080x1920.jpg", "21_对局HUD_861x1920.jpg", "23_对局HUD_720x1280.jpg"]

# 可手填的替代标尺（若某张图圣水条测不到）：img -> (bar_left, bar_right)
OVERRIDE = {}


def bar_cols(path):
    im = Image.open(path).convert("RGB")
    W, H = im.size
    px = im.load()
    cols = []
    for x in range(W):
        c = 0
        for y in range(int(H * 0.86), H, 2):
            q = px[x, y]
            if q[0] > 190 and q[2] > 150 and q[1] < 150 and (q[0] - q[1]) > 60:
                c += 1
        if c > 2:
            cols.append(x)
    return W, H, (min(cols), max(cols)) if cols else None


def main():
    for n in IMGS:
        p = os.path.join(REF, n)
        if not os.path.exists(p):
            print("MISSING " + n)
            continue
        W, H, r = bar_cols(p)
        if n in OVERRIDE:
            r = OVERRIDE[n]
        if not r:
            print("%-30s %dx%d  圣水条未测到（需 OVERRIDE）" % (n, W, H))
            continue
        bl, br = r
        bw = br - bl + 1
        k = bw / float(BAR_W_18)
        print("%-30s %dx%d  bar %d..%d (w=%d)  => k=%.4f" % (n, W, H, bl, br, bw, k))
        left = bl + (ROW_LEFT_18 - BAR_LEFT_18) * k
        cw, ch, gap = CARDW_18 * k, CARDH_18 * k, GAP_18 * k
        bottom = BOTTOM_18 * k
        y1 = H - bottom
        y0 = y1 - ch
        print("     rowLeft=%.1f  card=%.1fx%.1f  gap=%.1f  bottom=%0.1f" % (left, cw, ch, gap, bottom))
        for i in range(4):
            x0 = left + i * (cw + gap)
            print("     card%d box = %d %d %d %d   (x0 y0 x1 y1)" % (i + 1, round(x0), round(y0), round(x0 + cw), round(y1)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
