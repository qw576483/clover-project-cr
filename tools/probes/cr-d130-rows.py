#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130 取证：逐行分类 frame_022.png / frame_006.png 的内容，定位半场边界与泥岸带。
复跑：C:/Python312/python tools/probes/cr-d130-rows.py
"""
import os
import sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
AD = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')


def classify(m):
    r, g, b = float(m[0]), float(m[1]), float(m[2])
    if r > g + 8 and r > b + 25 and g >= b:
        return 'MUD'
    if b > r + 15:
        return 'WATER'
    if g >= r and g >= b:
        return 'GRASS'
    return 'OTHER'


def scan(name):
    p = os.path.join(AD, name)
    im = Image.open(p).convert('RGBA')
    a = np.asarray(im).astype(np.int32)
    H, W = a.shape[:2]
    print('=== %s  %dx%d ===' % (name, W, H))
    prev = None
    for y in range(H):
        row = a[y]
        vis = row[:, 3] > 128
        n = int(vis.sum())
        if n < 5:
            s = 'TRANSP'
            rgb = ''
        else:
            c = row[vis]
            m = c.mean(axis=0)
            k = classify(m)
            s = k
            rgb = ' rgb(%d,%d,%d) n=%d' % (int(m[0]), int(m[1]), int(m[2]), n)
        if s != prev:
            print('%5d %s%s' % (y, s, rgb))
            prev = s


for nm in sys.argv[1:] or ['frame_022.png', 'frame_006.png']:
    scan(nm)
