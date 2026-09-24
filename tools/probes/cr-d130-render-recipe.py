#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130 离线复核：按 ArenaView.cs 里**落地的六段配方**离线拼一张 18x32 的图，用来在进 Play 之前
先目视确认（消接缝 / 路连通 / 河面 / 河岸灰石木栏）。

配方（与 ArenaView.cs 的 BuildArt/BuildRiver 一一对应，常量名同）：
  BLUE 段① f022 py[536,748] → 格[7.5,17.0]   BLUE 段② py[748,996] → 格[2.33,7.5]
  BLUE 段③ f022 py[0,55]    → 格[0,2.33]
  RED  段① f022 py[1227,1634] → 格[17.0,26.5] RED  段② py[1083,1227] → 格[26.5,29.43]
  RED  段③ f022 py[0,55]     → 格[29.43,32.0]，flipY
  河面 f006 py[806,853] px[207.6,1027.4] → 格[15,17]
  桥面 f022 py[604,692] px[215,295] → 两车道各一次，格 y[14.6,17.3]

复跑：C:/Python312/python tools/probes/cr-d130-render-recipe.py
"""
import os

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
AD = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')
PXT = 45.545
FL = 99.0
FR = FL + 18 * PXT
PPT = 60.0
W, H = int(18 * PPT), int(32 * PPT)


def crop(img, px0, py0, px1, py1):
    return img.crop((int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1))))


def main():
    os.makedirs(OUT, exist_ok=True)
    f22 = Image.open(os.path.join(AD, 'frame_022.png')).convert('RGBA')
    f06 = Image.open(os.path.join(AD, 'frame_006.png')).convert('RGBA')
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    def blit(im, tytop, tybot, txleft, txright, flip=False):
        if flip:
            im = im.transpose(Image.FLIP_TOP_BOTTOM)
        w = max(1, int(round((txright - txleft) * PPT)))
        h = max(1, int(round((tytop - tybot) * PPT)))
        im = im.resize((w, h), Image.LANCZOS)
        c.alpha_composite(im, (int(round(txleft * PPT)), int(round((32 - tytop) * PPT))))

    # ── 地面六段（顺序 = sortingOrder：BLUE 先、RED 后、水再上、桥最上） ──
    blit(crop(f22, FL, 536, FR, 748), 17.0, 7.5, 0, 18)
    blit(crop(f22, FL, 748, FR, 996), 7.5, 2.33, 0, 18)
    blit(crop(f22, FL, 0, FR, 55), 2.33, 0.0, 0, 18)
    blit(crop(f22, FL, 1323, FR, 1600), 24.5, 17.0, 0, 18)
    blit(crop(f22, FL, 1083, FR, 1323), 29.43, 24.5, 0, 18)
    blit(crop(f22, FL, 0, FR, 55), 32.0, 29.43, 0, 18, flip=True)
    # ── 河面 ──
    blit(crop(f06, 207.6, 806, 207.6 + 18 * PXT, 853), 17.0, 15.0, 0, 18)
    # ── 两座桥面（中心 3.5 / 14.5，半宽 = 80px / 45.545 / 2） ──
    half = (295 - 215) / PXT * 0.5
    for cx in (3.5, 14.5):
        blit(crop(f22, 215, 604, 295, 692), 17.3, 14.6, cx - half, cx + half)

    p = os.path.join(OUT, 'recipe-live.png')
    c.convert('RGB').save(p)
    print('wrote', p, c.size)
    # 河区放大
    c.crop((0, int((32 - 19.5) * PPT), W, int((32 - 12.0) * PPT))).save(os.path.join(OUT, 'recipe-live-riverzone.png'))


if __name__ == '__main__':
    main()
