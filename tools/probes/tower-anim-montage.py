#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 `TowerAnimTable.cs` 里国王坐像 idle 的**时间轴顺序**出成联络图（离线，判"这 13 个姿态长什么样"）。

用法: python tools/probes/tower-anim-montage.py
产出: .ai-tmp/test/cr-fx-d/king-idle-timeline.png
"""
import os
import re

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
CS = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'View', 'TowerAnimTable.cs')
FRAMES = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Towers', 'building_tower_out')
OUT = os.path.join(HERE, 'king-idle-timeline.png')


def parse():
    src = open(CS, encoding='utf-8').read()
    out = {}
    for m in re.finditer(r'private static readonly Anim (\w+) = new Anim\((\d+), new\[\]\s*\{(.*?)\}\)', src, re.S):
        keys = [(int(a), int(b), float(c.rstrip('f')), float(d.rstrip('f')))
                for a, b, c, d in re.findall(r'new Key\((-?\d+),\s*(\d+),\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)', m.group(3))]
        out[m.group(1)] = (int(m.group(2)), keys)
    return out


def main():
    t = parse()
    cell, lab, rows = 120, 16, [('KingBlue', 'BLUE KingTower king_idle (clip 251)'),
                                ('KingRed', 'RED KingTower king_idle (clip 244)')]
    cols = max(len({kk[0] for kk in t[k][1]}) for k, _ in rows)
    W, H = cols * cell, len(rows) * (cell + lab * 2)
    canvas = Image.new('RGBA', (W, H), (30, 30, 34, 255))
    dr = ImageDraw.Draw(canvas)
    for ri, (field, title) in enumerate(rows):
        fps, keys = t[field]
        # 去重（按帧号首次出现序）；标注该姿态在时间轴上的总停留帧数。
        agg = {}
        order = []
        for (f, rep, sy, dy) in keys:
            if f not in agg:
                agg[f] = [0, sy, dy]
                order.append(f)
            agg[f][0] += rep
        keys = [(f, agg[f][0], agg[f][1], agg[f][2]) for f in order]
        y0 = ri * (cell + lab * 2)
        dr.text((4, y0 + 2), '%s  fps=%d  段数=%d  时间轴帧=%d' % (title, fps, len(keys), sum(k[1] for k in keys)),
                fill=(240, 240, 240, 255))
        for ci, (f, rep, sy, dy) in enumerate(keys[:cols]):
            p = os.path.join(FRAMES, 'frame_%03d.png' % f)
            x = ci * cell
            yy = y0 + lab
            if os.path.isfile(p):
                im = Image.open(p).convert('RGBA')
                k = min(cell / max(1, im.width), cell / max(1, im.height))
                im2 = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))))
                canvas.alpha_composite(im2, (x + (cell - im2.width) // 2, yy + (cell - im2.height) // 2))
            else:
                dr.rectangle([x + 2, yy + 2, x + cell - 3, yy + cell - 3], outline=(220, 60, 60, 255))
            dr.text((x + 3, yy + cell + 1), 'f%d x%d' % (f, rep), fill=(250, 230, 150, 255))
    canvas.save(OUT)
    print('-> %s  (%dx%d)' % (OUT, W, H))
    # 乘员 idle 的逐帧 squash（数值表，供报告引用）
    fps, keys = t['OccupantBlue']
    print('OccupantBlue fps=%d frames=%d' % (fps, sum(k[1] for k in keys)))
    print('  sy: %s' % [k[2] for k in keys])
    print('  dy: %s' % [k[3] for k in keys])


if __name__ == '__main__':
    main()
