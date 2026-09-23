#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2-art-fit.py  ——  对局 HUD 手牌「卡面 ↔ 卡槽」贴合量取（离线，不依赖 Unity）

用途（CR-T2 片）：
  1) 在原版对局基线图 `策划/参考图/18_对局HUD_1080x1920.jpg`（1080x1920，与本项目画布同尺寸
     ⇒ 像素值即画布值，k=1.0）上，量出**手牌每张卡里"卡面（彩色插画）"相对"卡槽外框"的 4 边内缩**。
  2) 量出「下一张」小卡上同样的 4 边内缩。
  3) 在 `client/Assets/Resources/Sprites/Cards/ui_spells_out/frame_NNN.png` 上量出**帧自身的
     透明包围盒**与**有内容像素的包围盒**，用来把"显示尺寸"换算回"裁剪切图尺寸"。

判据口径：
  - 卡槽矩形 = 策划/参考图/几何量取.md §1.3 的 D9/D11/D12（手牌整排左 x=144、单卡宽 140、
    卡顶 y=1614、卡底 y=1785）⇒ 单卡矩形 x = 144 + 143*i, y = 1614..1785。
  - 「卡面」的判据 = **有彩色（饱和度）的像素**：卡框/卡槽是灰银（低饱和），插画是彩色的。
    逐行/逐列统计"该行彩色像素数 >= 行高/列高的一定比例"来确定边界，避免 JPEG 噪点干扰。

用法：
  python tools/probes/cr-t2-art-fit.py hand      # 手牌 4 张 + 下一张，打印 4 边内缩
  python tools/probes/cr-t2-art-fit.py frames    # 卡面帧自身包围盒（对照用）
  python tools/probes/cr-t2-art-fit.py crop      # 导出带标尺的放大裁图到 .ai-tmp/screenshots/
只读输入、只写 .ai-tmp/screenshots/。
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
CARDS = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Cards", "ui_spells_out")

BASE = "18_对局HUD_1080x1920.jpg"

# 几何量取.md §1.3 D9/D11/D12/D13/D14
CARD_X0 = 144          # D9 手牌整排左
CARD_W = 140           # D11
CARD_GAP = 3           # D11
CARD_Y0 = 1614         # D12 卡顶
CARD_Y1 = 1785         # D12 卡底（含）
NEXT_X0, NEXT_X1 = 33, 97      # D14 下一张 x 33..97
NEXT_Y0, NEXT_Y1 = 1634, 1716  # D14 下一张 y 1634..1716


def sat(p):
    return max(p) - min(p)


def colored_mask(img, box, thresh=48):
    """返回 box 内"彩色像素"的逐行/逐列计数。"""
    x0, y0, x1, y1 = box
    px = img.load()
    w, h = x1 - x0, y1 - y0
    rows = [0] * h
    cols = [0] * w
    for j in range(h):
        y = y0 + j
        for i in range(w):
            x = x0 + i
            if sat(px[x, y][:3]) >= thresh:
                rows[j] += 1
                cols[i] += 1
    return rows, cols


def edges_1d(counts, need):
    """找 counts 里第一个 / 最后一个 >= need 的下标；无则 None。"""
    first = last = None
    for i, c in enumerate(counts):
        if c >= need:
            if first is None:
                first = i
            last = i
    return first, last


def measure(img, box, label, thresh=48, frac=0.25):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    rows, cols = colored_mask(img, box, thresh)
    r0, r1 = edges_1d(rows, max(1, int(w * frac)))
    c0, c1 = edges_1d(cols, max(1, int(h * frac)))
    if r0 is None or c0 is None:
        print("%-14s 未找到彩色内容（阈值 sat>=%d）" % (label, thresh))
        return None
    print("%-14s 卡槽 %dx%d @ (%d,%d) ［画布坐标］" % (label, w, h, x0, y0))
    print("   彩色内容 bbox: x %d..%d  y %d..%d  (宽 %d 高 %d)"
          % (c0 + x0, c1 + x0, r0 + y0, r1 + y0, c1 - c0 + 1, r1 - r0 + 1))
    print("   相对卡槽 4 边内缩: 左 %d / 右 %d / 上 %d / 下 %d"
          % (c0, w - 1 - c1, r0, h - 1 - r1))
    print("   内缩占卡槽比例: 左 %.2f%% / 右 %.2f%% / 上 %.2f%% / 下 %.2f%%"
          % (c0 * 100.0 / w, (w - 1 - c1) * 100.0 / w, r0 * 100.0 / h, (h - 1 - r1) * 100.0 / h))
    return dict(box=box, c0=c0, c1=c1, r0=r0, r1=r1, w=w, h=h)


def cmd_hand():
    img = Image.open(os.path.join(REF, BASE)).convert("RGB")
    print("== 基线 %s  size=%s  (1080x1920 = 本项目画布 ⇒ 像素即画布值)" % (BASE, img.size))
    print()
    for i in range(4):
        x0 = CARD_X0 + i * (CARD_W + CARD_GAP)
        measure(img, (x0, CARD_Y0, x0 + CARD_W, CARD_Y1 + 1), "手牌#%d" % (i + 1))
        print()
    measure(img, (NEXT_X0, NEXT_Y0, NEXT_X1 + 1, NEXT_Y1 + 1), "下一张")


def cmd_frames():
    if not os.path.isdir(CARDS):
        print("MISSING dir %s" % CARDS)
        return
    names = sorted(os.listdir(CARDS))
    print("== 帧自身包围盒（%s，%d 个文件）" % (CARDS, len(names)))
    for n in names:
        if not n.endswith(".png"):
            continue
        p = os.path.join(CARDS, n)
        im = Image.open(p).convert("RGBA")
        alpha = im.getchannel("A")
        abox = alpha.getbbox()
        # 有内容像素（alpha>0）的 RGB 饱和度包围盒
        px = im.load()
        w, h = im.size
        minx = miny = None
        maxx = maxy = None
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if a > 0 and (max(r, g, b) - min(r, g, b)) >= 48:
                    if minx is None:
                        minx, miny, maxx, maxy = x, y, x, y
                    else:
                        minx = min(minx, x)
                        maxx = max(maxx, x)
                        miny = min(miny, y)
                        maxy = max(maxy, y)
        print("  %-18s size=%dx%d  alpha_bbox=%s  彩色bbox=%s"
              % (n, w, h, abox, None if minx is None else (minx, miny, maxx, maxy)))


def cmd_crop():
    os.makedirs(SHOTS, exist_ok=True)
    img = Image.open(os.path.join(REF, BASE)).convert("RGB")
    box = (CARD_X0 - 6, CARD_Y0 - 6, CARD_X0 + 4 * CARD_W + 3 * CARD_GAP + 6, CARD_Y1 + 7)
    crop = img.crop(box)
    s = 3
    big = crop.resize((crop.size[0] * s, crop.size[1] * s), Image.NEAREST)
    d = ImageDraw.Draw(big)
    for i in range(5):
        gx = CARD_X0 + i * (CARD_W + CARD_GAP)
        px_ = (gx - box[0]) * s
        d.line([(px_, 0), (px_, big.size[1])], fill=(255, 0, 0), width=1)
        d.text((px_ + 2, 2), str(gx), fill=(255, 255, 0))
        px2 = (gx + CARD_W - 1 - box[0]) * s
        d.line([(px2, 0), (px2, big.size[1])], fill=(255, 0, 0), width=1)
    for gy in (CARD_Y0, CARD_Y1):
        py = (gy - box[1]) * s
        d.line([(0, py), (big.size[0], py)], fill=(0, 200, 255), width=1)
        d.text((2, py + 2), str(gy), fill=(255, 255, 0))
    out = os.path.join(SHOTS, "CR-T2-base-hand-zoom.png")
    big.save(out)
    print("saved %s  crop=%s scale=%d" % (out, box, s))


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "hand"
    if cmd == "hand":
        cmd_hand()
    elif cmd == "frames":
        cmd_frames()
    elif cmd == "crop":
        cmd_crop()
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
