#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1i-fit.py -- CR-T1i 判据资产：公主塔/国王塔 art 的**同口径**宽度与缩放推导
=================================================================================
口径定义（两侧必须一致，否则比出来的是"两种量法"而不是"两个尺寸"）：
  `白件宽` = 该图里 (r>205 & g>195 & b>185 & a>8) 的像素水平 extent。
  这是两侧都干净可量的口径：原版图里它 = 塔的白色垛口+石体；art 里同理。

原版值来自 `cr_t1h_scale.py` 的实测（`策划/参考图/03_对局_1320x2868.jpg`）：
  国王塔白件宽 = 268(RED) / 264(BLUE) px
  公主塔白件宽 = 169(RED-L) / 175(RED-R) / 172(BLUE-R) px

复跑
----
  python tools/probes/cr-t1i-fit.py --root .
  python tools/probes/cr-t1i-fit.py --root . --grid      # 出带标尺网格的原版塔放大图（量乘员用）
"""
import argparse
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass
import numpy as np
from PIL import Image, ImageDraw


def unit_dir(root, unit):
    return os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', unit + '_out')


def sprite(root, unit, rec):
    for fmt in (unit + '_sprite_%03d.png', 'frame_%03d.png'):
        p = os.path.join(unit_dir(root, unit), fmt % rec)
        if os.path.isfile(p):
            return Image.open(p).convert('RGBA')
    raise IOError('缺 rec %d' % rec)


def mask_extent(im, box=None):
    a = np.asarray(im)
    r, g, b, al = (a[:, :, 0].astype(int), a[:, :, 1].astype(int),
                   a[:, :, 2].astype(int), a[:, :, 3])
    m = (al > 8) & (r > 205) & (g > 195) & (b > 185)
    ys, xs = np.nonzero(m)
    if not len(xs):
        return None
    return dict(n=len(xs), x0=int(xs.min()), x1=int(xs.max()),
                w=int(xs.max() - xs.min() + 1),
                y0=int(ys.min()), y1=int(ys.max()), h=int(ys.max() - ys.min() + 1))


REF = dict(king_red=268, king_blue=264, prin_red=172, prin_blue=172)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--grid', action='store_true')
    ap.add_argument('--pair', action='store_true')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    U = 'building_tower'
    ART = dict(king_blue=213, king_red=211, prin_blue=10, prin_red=9,
               prin_top_blue=8, prin_top_red=7)
    print('---- art 白件宽（rec）----')
    w = {}
    for k, rec in ART.items():
        im = sprite(root, U, rec)
        e = mask_extent(im)
        w[k] = e
        print('  %-14s rec=%-3d 画布=%dx%d 白件 x=%d..%d 宽=%3d 高=%d y=%d..%d'
              % (k, rec, im.width, im.height, e['x0'], e['x1'], e['w'], e['h'], e['y0'], e['y1']))
    # ★ 单位换算**不能省**：原版图 91.5 px/格、我方 art 100 px/格（PPU=100，1 格 = 1 世界单位）
    #   ⇒ 想"渲染出来一样宽"，要比的是**格**：art_px×scale/100 == ref_px/91.5
    PX_PER_TILE_REF = 91.5
    print('\n---- 推出工程缩放（先都换算成"格"再比：art_px×scale/100  vs  ref_px/%.1f） ----'
          % PX_PER_TILE_REF)
    for k, ref in (('king_red', REF['king_red']), ('king_blue', REF['king_blue']),
                   ('prin_red', REF['prin_red']), ('prin_blue', REF['prin_blue'])):
        ref_tiles = ref / PX_PER_TILE_REF
        art_tiles = w[k]['w'] / 100.0
        print('  %-10s 原版 %3d px = %5.2f 格 ‖ art %3d px = %5.2f 格 ⇒ scale = %.3f'
              % (k, ref, ref_tiles, w[k]['w'], art_tiles, ref_tiles / art_tiles))
    print('  （另一口径「全轮廓宽」：原版公主塔 ≈200~205 px = 2.19~2.24 格、art bbox 151 px = 1.51 格 ⇒ 1.46；'
          '国王塔 ≈300 px = 3.28 格、art bbox 184 px = 1.84 格 ⇒ 1.78。两个口径对公主塔相差 11%、对国王塔 4% ⇒ 登记为误差带。）')
    print('\n（现值 TowerScale = 1.6 是对着"art 全宽 bbox 191 px"推的，口径与上表不同；'
          '上表两侧都是"白件宽"口径。）')

    if a.pair:
        # 并排放大：左 = 原版裁切（绝对坐标标尺），右 = 我们的 art（画布坐标标尺），两侧都 3x。
        # 用途：① 判 art 是否就是原版那座塔（形状）；② 目视读"乘员相对塔腔"的位置。
        ref = Image.open(os.path.join(root, '策划', '参考图', '03_对局_1320x2868.jpg')).convert('RGB')
        pairs = [('redprin', (25, 470, 330, 800), 9), ('blueprin', (1006, 1790, 1295, 2200), 10)]
        cells = []
        for tag, box, rec in pairs:
            c = ref.crop(box).resize(((box[2] - box[0]) * 3, (box[3] - box[1]) * 3), Image.LANCZOS)
            d = ImageDraw.Draw(c)
            for x in range(0, c.width, 60):
                d.line([(x, 0), (x, c.height)], fill=(255, 0, 255))
                d.text((x + 2, 2), str(box[0] + x // 3), fill=(255, 0, 255))
            for y in range(0, c.height, 60):
                d.line([(0, y), (c.width, y)], fill=(0, 170, 255))
                d.text((2, y + 2), str(box[1] + y // 3), fill=(0, 230, 0))
            art = sprite(root, U, rec).crop((60, 40, 360, 320))
            art = art.resize((art.width * 3, art.height * 3), Image.LANCZOS)
            bg = Image.new('RGB', art.size, (250, 250, 250))
            bg.paste(art.convert('RGB'), (0, 0), art)
            d2 = ImageDraw.Draw(bg)
            for x in range(0, bg.width, 60):
                d2.line([(x, 0), (x, bg.height)], fill=(255, 0, 255))
                d2.text((x + 2, 2), str(60 + x // 3), fill=(255, 0, 255))
            for y in range(0, bg.height, 60):
                d2.line([(0, y), (bg.width, y)], fill=(0, 170, 255))
                d2.text((2, y + 2), str(40 + y // 3), fill=(0, 200, 0))
            cells.append(('%s REF (绝对坐标)' % tag, c))
            cells.append(('%s ART rec %d (画布坐标)' % (tag, rec), bg))
        cw = max(c[1].width for c in cells) + 8
        ch = max(c[1].height for c in cells) + 20
        canvas = Image.new('RGB', (2 * cw, 2 * ch), (240, 240, 240))
        dd = ImageDraw.Draw(canvas)
        for i, (tag, im) in enumerate(cells):
            x = (i % 2) * cw
            y = (i // 2) * ch
            canvas.paste(im, (x + 4, y + 18))
            dd.text((x + 5, y + 3), tag, fill=(0, 0, 0))
        out = os.path.join(root, '.ai-tmp', 'test', 'CR-T1i-pair.png')
        canvas.save(out)
        print('PAIR -> %s' % out)

    if a.grid:
        ref = Image.open(os.path.join(root, '策划', '参考图', '03_对局_1320x2868.jpg')).convert('RGB')
        for tag, box in (('redprin', (25, 470, 330, 800)), ('blueprin', (1006, 1790, 1295, 2200)),
                         ('redking', (430, 60, 830, 640))):
            c = ref.crop(box).resize(((box[2] - box[0]) * 3, (box[3] - box[1]) * 3), Image.LANCZOS)
            d = ImageDraw.Draw(c)
            for x in range(0, c.width, 60):     # 每 60 px（原始 20 px）
                d.line([(x, 0), (x, c.height)], fill=(255, 0, 255))
                d.text((x + 2, 2), str(box[0] + x // 3), fill=(255, 0, 255))
            for y in range(0, c.height, 60):
                d.line([(0, y), (c.width, y)], fill=(0, 160, 255))
                d.text((2, y + 2), str(box[1] + y // 3), fill=(0, 220, 0))
            out = os.path.join(root, '.ai-tmp', 'test', 'CR-T1i-grid-%s.png' % tag)
            c.save(out)
            print('GRID -> %s  (绝对坐标标尺)' % out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
