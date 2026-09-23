#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""V4 measurement probe (read-only judgement asset, committed with the project).

Why another probe: g3-measure.py's `row`/`col` modes index x1+1 / y1+1 inclusive and crash on
W-1, and its `colblobs`/`rowblobs` use a single global threshold that mis-segments the deck
screen (its card-grid background is a mid blue whose max channel is already > 120). This probe
segments by "how many pixels in the perpendicular band are brighter than thr", which is stable
for both the card grid (cards bright on a blue field) and the HUD top bar.

Usage (from the project root):
  python tools/probes/v4-measure.py cols <img> <y0> <y1> <thr> [minfrac]
  python tools/probes/v4-measure.py rows <img> <x0> <x1> <thr> [minfrac]

cols: for every x, count pixels y in [y0,y1) with max(r,g,b) >= thr; a column is "on" when
      count >= minfrac*(y1-y0). Prints the "on" runs (candidate card columns / bar ends).
rows: same, swapping the roles (candidate card rows / bar top-bottom).
"""
import sys

from PIL import Image


def main():
    mode = sys.argv[1]
    im = Image.open(sys.argv[2]).convert('RGB')
    W, H = im.size
    a0 = int(sys.argv[3]); a1 = int(sys.argv[4])
    thr = float(sys.argv[5])
    minfrac = float(sys.argv[6]) if len(sys.argv) > 6 else 0.5
    px = im.load()
    print('image=%s size=%dx%d mode=%s band=[%d,%d) thr=%.0f minfrac=%.2f'
          % (sys.argv[2], W, H, mode, a0, a1, thr, minfrac))
    span = a1 - a0
    need = minfrac * span
    if mode == 'cols':
        counts = []
        for x in range(W):
            c = 0
            for y in range(a0, a1):
                p = px[x, y]
                if p[0] >= thr or p[1] >= thr or p[2] >= thr:
                    c += 1
            counts.append(c)
    else:
        counts = []
        for y in range(H):
            c = 0
            for x in range(a0, a1):
                p = px[x, y]
                if p[0] >= thr or p[1] >= thr or p[2] >= thr:
                    c += 1
            counts.append(c)

    start = None
    runs = []
    for i, c in enumerate(counts):
        on = c >= need
        if on and start is None:
            start = i
        elif not on and start is not None:
            runs.append((start, i - 1))
            start = None
    if start is not None:
        runs.append((start, len(counts) - 1))

    axis = 'x' if mode == 'cols' else 'y'
    total = float(span if mode == 'cols' else (a1 - a0))
    for s, e in runs:
        if e - s < 1:
            continue
        print('  %s=%4d..%4d  w=%4d  (%.1f%%..%.1f%% of %d)'
              % (axis, s, e, e - s + 1, 100.0 * s / W if mode == 'cols' else 100.0 * s / H,
                 100.0 * (e + 1) / W if mode == 'cols' else 100.0 * (e + 1) / H, W if mode == 'cols' else H))
    print('  runs=%d  (span=%d need>=%.0f px)' % (len([r for r in runs if r[1] - r[0] >= 1]), span, need))


main()
