#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时判据：D130 半场归属 × 是否翻转 的 4 种组合（C6/C7/C8/C9）各出一张 18x32 图。

用途：**"泥岸带该在水的哪一侧"** 是唯一还没定死的自由度。原版 `策划/参考图/03_对局_1320x2868.jpg`
的河面：**泥岸（深棕带碎石）在水面之上（远岸）**，水面之下直接是草。
帧 22 的 A 块（行 492..996）自带这条泥岸带（行 588..600，均色 RGB(144,116,89)），B 块（行 1083..1675）
没有。⇒ 4 种组合里只有一种能把泥岸带放到**水之上**。

复跑：C:/Python312/python tools/probes/cr-d130-tmp-halves.py
"""
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
AD = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')
PXT = 45.545
FL = 99.0
FR = 99.0 + 18 * PXT
PPT = 60.0
A = (492, 996)      # 带泥岸带的那块（泥岸在块顶 96..108 行处）
B = (1087, 1608)    # 后沿栅栏那块


def crop(img, px0, py0, px1, py1):
    return img.crop((int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1))))


def build(name, blue, red):
    """blue/red = (rows, tileLow, tileHigh, flip)"""
    f22 = Image.open(os.path.join(AD, 'frame_022.png')).convert('RGBA')
    f06 = Image.open(os.path.join(AD, 'frame_006.png')).convert('RGBA')
    c = Image.new('RGBA', (int(18 * PPT), int(32 * PPT)), (0, 0, 0, 0))

    def blit(im, tytop, tybot, flip):
        if flip:
            im = im.transpose(Image.FLIP_TOP_BOTTOM)
        im = im.resize((int(18 * PPT), max(1, int(round((tytop - tybot) * PPT)))), Image.LANCZOS)
        c.alpha_composite(im, (0, int(round((32 - tytop) * PPT))))

    ra, rt0, rt1, rf = red
    ba, bt0, bt1, bf = blue
    blit(crop(f22, FL, ra[0], FR, ra[1]), rt1, rt0, rf)
    blit(crop(f22, FL, ba[0], FR, ba[1]), bt1, bt0, bf)
    blit(crop(f06, 207.6, 806, 207.6 + 18 * PXT, 853), 17, 15, False)
    p = os.path.join(OUT, name)
    c.convert('RGB').save(p)
    print(name, c.size, p)


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    # C6 = 现在落到代码里的那一种：BLUE=A 不翻 0..17.5 / RED=B 不翻 14.5..32
    build('C6.png', (A, 0, 17.5, False), (B, 14.5, 32, False))
    # C7 = BLUE=B 翻 / RED=A 翻
    build('C7.png', (B, 0, 17.5, True), (A, 14.5, 32, True))
    # C8 = BLUE=A 翻 / RED=B 翻
    build('C8.png', (A, 0, 17.5, True), (B, 14.5, 32, True))
    # C9 = BLUE=B 不翻 / RED=A 不翻
    build('C9.png', (B, 0, 17.5, False), (A, 14.5, 32, False))
