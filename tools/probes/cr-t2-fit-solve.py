#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2-fit-solve.py  ——  用「与素材帧最小平均绝对差」反解原版手牌里卡面的 4 边内缩（离线）

为什么不用"肉眼看着对"：任务书要求每个值有出处（原版图文件 + 量取像素 + 换算式）。
本脚本给出的是**可复跑的最小化判据**：
  取原版对局图 `策划/参考图/20_对局_1080x1920.jpg` 的第 2 张手牌（冰雪精灵 = `frame_007`），
  在给定的卡片外框矩形内，令卡面矩形 = 外框按 (左, 上, 右, 下) 内缩 inset 后的矩形；
  把 `frame_007` 的 alpha 包围盒内容缩放到该矩形，与图上同一区域的像素求**平均绝对差**（排除
  圣水费用泡的品红像素），坐标下降法最小化该差值 ⇒ 得到最优内缩。
只读输入，不写任何文件。
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")
IMG = "20_对局_1080x1920.jpg"
FRAME = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Cards",
                     "ui_spells_out", "frame_007.png")

# 第 2 张手牌的外框（金边外沿），实测：金边列 x 286..290 / 417..421，金边行 y 1699..1703 / 1859..1862
CARD = (286, 1699, 422, 1863)   # x0, y0, x1(不含), y1(不含)  => 136 x 164
# ★ CR-T2d：基线裁定后**可换基线图重解**（离线，只读）：
#   python tools/probes/cr-t2-fit-solve.py 18_对局HUD_1080x1920.jpg 287 1614 427 1785
#   （18 图第 2 张手牌：D9/D11/D12 读数 ⇒ 单卡 140×171、卡 2 外框 x 287..427 / y 1614..1785）
if len(sys.argv) >= 6:
    IMG = sys.argv[1]
    CARD = (int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]))
    GRAY = "--gray" in sys.argv        # 灰化态基线图（如 18 的手牌）加这个旗标
WORK = (48, 58)                 # 比对用的降采样尺寸
GRAY = False                    # True = 把素材帧也灰化后再比对（用于灰化态基线图，见 score() 注释）


def is_badge(p):
    r, g, b = p
    return r > 170 and b > 130 and g < 140 and (r - g) > 60


def score(img, sprite, ins):
    """ins = (l, t, r, b)（相对卡片外框的像素内缩）⇒ 平均绝对差（越小越贴合）。"""
    x0, y0, x1, y1 = CARD
    l, t, r, b = ins
    ax0, ay0, ax1, ay1 = x0 + l, y0 + t, x1 - r, y1 - b
    if ax1 - ax0 < 20 or ay1 - ay0 < 20:
        return 1e9
    region = img.crop((ax0, ay0, ax1, ay1))
    w, h = region.size
    sw, sh = WORK
    a = sprite.resize((sw, sh), Image.LANCZOS).convert("RGB")
    # ★ CR-T2e：18 图的手牌是**灰化态**（当时只有 2 圣水 ⇒ 4 张卡都不可出）。把素材帧也灰化，
    #   才能让"颜色差"不再主导 MAD ⇒ 极值位置可信（否则 score 会被灰化污染，只当结构匹配用）。
    if GRAY:
        a = a.convert("L").convert("RGB")
    c = region.resize((sw, sh), Image.NEAREST)
    pa, pc = a.load(), c.load()
    tot = 0
    n = 0
    for yy in range(sh):
        for xx in range(sw):
            ca, cc = pa[xx, yy], pc[xx, yy]
            if is_badge(cc):
                continue
            tot += (abs(ca[0] - cc[0]) + abs(ca[1] - cc[1]) + abs(ca[2] - cc[2])) / 3.0
            n += 1
    return tot / max(1, n)


def main():
    img = Image.open(os.path.join(REF, IMG)).convert("RGB")
    fr = Image.open(FRAME).convert("RGBA")
    sprite = fr.crop(fr.getchannel("A").getbbox())
    print("卡片外框(实测) = x %d..%d / y %d..%d  => %dx%d" %
          (CARD[0], CARD[2] - 1, CARD[1], CARD[3] - 1, CARD[2] - CARD[0], CARD[3] - CARD[1]))
    print("素材帧 = frame_007，alpha bbox 裁出后 %s" % (sprite.size,))

    ins = [6, 8, 6, 6]
    best = score(img, sprite, ins)
    print("初值 ins(l,t,r,b)=%s  score=%.2f" % (ins, best))
    for p in range(4):
        improved = True
        while improved:
            improved = False
            for i in range(4):
                for d in (-1, 1):
                    cand = list(ins)
                    cand[i] += d
                    if cand[i] < 0 or cand[i] > 24:
                        continue
                    s = score(img, sprite, cand)
                    if s < best - 1e-6:
                        best, ins, improved = s, cand, True
        print("  pass %d -> ins(l,t,r,b)=%s  score=%.2f" % (p + 1, ins, best))

    l, t, r, b = ins
    cw, ch = CARD[2] - CARD[0], CARD[3] - CARD[1]
    print()
    print("最优内缩: 左 %d / 上 %d / 右 %d / 下 %d  （卡片 %dx%d）" % (l, t, r, b, cw, ch))
    print("卡面区域 = %dx%d（占比 %.2f%% x %.2f%%）" %
          (cw - l - r, ch - t - b, (cw - l - r) * 100.0 / cw, (ch - t - b) * 100.0 / ch))
    print("内缩占比: 左 %.4f%% / 上 %.4f%% / 右 %.4f%% / 下 %.4f%%" %
          (l * 100.0 / cw, t * 100.0 / ch, r * 100.0 / cw, b * 100.0 / ch))


if __name__ == "__main__":
    main()
