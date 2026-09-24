#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时判据：D130 候选铺法 C1 —— 直接把 frame_022 的两个半场块各铺半场：
  BLUE 半场（屏幕下方）= block A  py 492..1076 → 格 0..17
  RED  半场（屏幕上方）= block B  py 1083..1675 → 格 15..32
  水面 = frame_006 py 806..853 → 格 15..17
出图 .ai-tmp/test/d130x/C1.png 目视判断。
复跑：C:/Python312/python tools/probes/cr-d130-tmp-c1.py
"""
import os
import sys

from PIL import Image
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
AD = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')

PX_PER_TILE_X = 45.545
FIELD_LEFT_PX = 99.0
FIELD_RIGHT_PX = 99.0 + 18 * PX_PER_TILE_X   # 918.8

BLK_A = (492, 1076)     # BLUE half: river(top) → king(bottom)
BLK_B = (1083, 1675)    # RED half: rear(top) → river(bottom)
WATER = (806, 853)
WATER_LEFT_PX = 207.6


def crop(img, px0, py0, px1, py1, tx0, tx1, ty0, ty1, ppt):
    sub = img.crop((int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1))))
    return sub.resize((max(1, int(round((tx1 - tx0) * ppt))), max(1, int(round((ty1 - ty0) * ppt)))), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    f22 = Image.open(os.path.join(AD, 'frame_022.png')).convert('RGBA')
    f06 = Image.open(os.path.join(AD, 'frame_006.png')).convert('RGBA')
    ppt = 60.0
    canvas = Image.new('RGBA', (int(18 * ppt), int(32 * ppt)), (0, 0, 0, 0))

    def blit(im, tile_x0, tile_y_top):
        canvas.alpha_composite(im, (int(round(tile_x0 * ppt)), int(round((32 - tile_y_top) * ppt))))

    # RED 半场：块 B pyTop=1083 → 格 32，pyBottom=1675 → 格 15
    blit(crop(f22, FIELD_LEFT_PX, BLK_B[0], FIELD_RIGHT_PX, BLK_B[1], 0, 18, 15, 32, ppt), 0, 32)
    # BLUE 半场：块 A pyTop=492 → 格 17，pyBottom=1076 → 格 0
    blit(crop(f22, FIELD_LEFT_PX, BLK_A[0], FIELD_RIGHT_PX, BLK_A[1], 0, 18, 0, 17, ppt), 0, 17)
    # 河面
    blit(crop(f06, WATER_LEFT_PX, WATER[0], WATER_LEFT_PX + 18 * PX_PER_TILE_X, WATER[1], 0, 18, 15, 17, ppt), 0, 17)

    canvas.convert('RGB').save(os.path.join(OUT, 'C1.png'))
    print('C1 ->', os.path.join(OUT, 'C1.png'), canvas.size)
    return 0


if __name__ == '__main__':
    sys.exit(main())
