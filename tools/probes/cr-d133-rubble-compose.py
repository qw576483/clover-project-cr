#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d133-rubble-compose.py -- D133 判据资产：废墟帧 vs 塔体帧的「同画布叠加」对照图
=====================================================================================
回答的问题
----------
1. 三张废墟帧（rec 204/205/207）各自长什么样、往哪一型塔上叠才**对位合理**（废墟环包住塔基）？
2. 运行时把废墟放在 `localPosition = (tp − tb)/ppu`（见 ArenaView.CanvasOffsetPx）之后，
   画面上到底长什么样？（用来在**没有原版毁塔基线图**的前提下做人眼判定 —— skill §4 第 10 条）

为什么"直接叠加两张同尺寸 PNG"就等于运行时合成
------------------------------------------------
`building_tower_out` 的**每一张 PNG 都是同一张 407×471 画布的导出**（本脚本会断言尺寸），
且 Unity 的锚点 = 每张图自己的 alpha 裁剪框中心。运行时：塔体层的裁剪框中心落在塔根原点
（localPosition=0），废墟层的裁剪框中心落在塔根 + (tp − tb)。⇒ 合成结果 = 「画布坐标叠加后整体平移 −tb」
⇒ **平移不变** —— 所以直接把两张 PNG 就地 alpha 叠加，就是运行时画面的等价形。

产物
----
`.ai-tmp/test/CR-D133-ruin-pairing.png`
  · 第 1 行 = 三张废墟帧单独看（叠在浅色棋盘上）
  · 第 2 行 = 废墟 **只** 叠在**国王塔**（蓝 rec213 / 红 rec211）上
  · 第 3 行 = 废墟 **只** 叠在**公主塔**（蓝 rec10 / 红 rec9）上
  · 每格标注：rubble 最大宽 / 塔 art 最大宽 的比值（与 cr-d133-tower-ruin.py 的 §F 同源）

复跑
----
  python tools/probes/cr-d133-rubble-compose.py --root .
  python tools/probes/cr-d133-rubble-compose.py --root . --zoom 2
"""
import argparse
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

from PIL import Image, ImageDraw

RUBBLE = [205, 207, 204]
KING = [213, 211]
PRINCESS = [10, 9]
CANVAS = (407, 471)


def alpha_bbox_w(im):
    a = im.getchannel('A')
    w, h = a.size
    px = a.load()
    left, right = w, -1
    for yy in range(0, h, 2):          # 隔行扫：只用来标"最大宽"的示意值，脚本 §F 有精确值
        for xx in range(w):
            if px[xx, yy] > 0:
                if xx < left:
                    left = xx
                if xx > right:
                    right = xx
    return 0 if right < 0 else right - left + 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--zoom', type=int, default=1)
    ap.add_argument('--out', default='.ai-tmp/test/CR-D133-ruin-pairing.png')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    d = os.path.join(root, 'client', 'Assets', 'Resources', 'Sprites', 'Towers', 'building_tower_out')

    def load(n):
        p = os.path.join(d, 'frame_%03d.png' % n)
        if not os.path.isfile(p):
            raise SystemExit('MISS %s' % p)
        return Image.open(p).convert('RGBA')

    # 断言同画布（本脚本的"直接叠加 = 运行时合成"前提）
    for n in RUBBLE + KING + PRINCESS:
        if load(n).size != CANVAS:
            raise SystemExit('画布不是 %s：frame_%03d -> %s' % (CANVAS, n, load(n).size))
    print('OK 所有帧同画布 %dx%d ⇒ 直接叠加 == 运行时的平移等价形' % CANVAS)

    board = Image.new('RGB', (380, 430), (232, 236, 240))
    bd = ImageDraw.Draw(board)
    for y in range(0, 430, 20):
        for x in range(0, 380, 20):
            if (x // 20 + y // 20) % 2:
                bd.rectangle([x, y, x + 19, y + 19], fill=(214, 219, 226))

    cells = []

    def snap(im, label, crop):
        base = board.copy()
        src = im.crop(crop)
        base.paste(src, ((380 - src.width) // 2, (430 - src.height) // 2), src)
        if a.zoom != 1:
            base = base.resize((base.width * a.zoom, base.height * a.zoom), Image.NEAREST)
        return label, base

    def overlay(tower_n, rub_n):
        t = load(tower_n)
        r = load(rub_n)
        out = Image.alpha_composite(t, r)
        # 让"塔体 + 废墟"可分辨：塔体压暗一档当底，废墟原样叠上去
        dark = Image.new('RGBA', out.size, (0, 0, 0, 0))
        dark.paste(Image.new('RGBA', out.size, (40, 40, 40, 255)), (0, 0), t)
        comp = Image.alpha_composite(dark, r)
        return comp

    # 第 1 行：废墟单独
    for n in RUBBLE:
        r = load(n)
        cells.append(snap(r, 'rubble frame_%d  (w=%d)' % (n, alpha_bbox_w(r)), (60, 80, 330, 350)))

    # 第 2/3 行：废墟叠在塔体上（塔体压暗、废墟原样）
    for row, twr in (('KING', KING), ('PRINCESS', PRINCESS)):
        for t in twr:
            for rn in RUBBLE[:2]:
                comp = overlay(t, rn)
                tw = alpha_bbox_w(load(t))
                rw = alpha_bbox_w(load(rn))
                cells.append(snap(comp, '%s %d + rubble %d   w%3d/%-3d=%.3f'
                                  % (row, t, rn, rw, tw, rw / float(tw)), (60, 60, 350, 370)))

    # 拼图
    cols = 3
    rows = (len(cells) + cols - 1) // cols
    cw = max(c[1].width for c in cells)
    ch = max(c[1].height for c in cells)
    lab = 20
    canvas = Image.new('RGB', (cols * cw, rows * (ch + lab)), (255, 255, 255))
    dr = ImageDraw.Draw(canvas)
    for i, (label, im) in enumerate(cells):
        x = (i % cols) * cw
        y = (i // cols) * (ch + lab)
        canvas.paste(im, (x + (cw - im.width) // 2, y))
        dr.text((x + 4, y + ch + 4), label, fill=(0, 0, 0))
    out = a.out if os.path.isabs(a.out) else os.path.join(root, a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    canvas.save(out)
    print('OUT -> %s  (%dx%d)' % (out, canvas.width, canvas.height))
    return 0


if __name__ == '__main__':
    sys.exit(main())
