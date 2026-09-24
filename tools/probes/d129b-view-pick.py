#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d129b-view-pick.py -- D129b 判据资产：把 9 个视角的**代表帧**出成带标签联络图
====================================================================================
用途：给「单位动画该取哪个视角（`_N`）」提供**可复跑的判据图**（不是目测猜：图由本脚本
     从 `D129b-clip-segments.tsv` 的帧号自动取帧拼出，标签含 `视角号 + frame_NNN`）。

数据源：`.ai-tmp/test/D129b-clip-segments.tsv`（生成器 `tools/probes/d129b-clip-segments.py`）。

布局：行 = 档位（idle / walk / attack，蓝方 `main` 套），列 = 视角 1..9。
      每格取该 (dir, 档位, 视角) 的**首段中间帧**（walk/attack 取中段，idle 只有 1 张）。

复跑：
  python tools/probes/d129b-view-pick.py --root . --dirs chr_knight_out chr_minion_out
"""
import argparse
import csv
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass
try:
    from PIL import Image, ImageDraw
except Exception as e:
    print('需要 PIL：%s' % e)
    raise

TIERS = ('idle', 'walk', 'attack')


def load_rows(root, dirs):
    tsv = os.path.join(root, '.ai-tmp', 'test', 'D129b-clip-segments.tsv')
    out = {}
    with open(tsv, 'r', encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            if dirs and r['dir'] not in dirs:
                continue
            if r['side'] != 'main':
                continue
            if r['tier'] not in TIERS:
                continue
            out.setdefault((r['dir'], r['tier']), {})[int(r['view'])] = r
    return out


def pick_frame(row):
    """取该 (档位,视角) 的**首段**中间帧号。"""
    runs = row['runs'].split(',')
    start, ln = int(runs[0]), int(runs[1])
    return start + ln // 2


def sprite(root, d, n):
    base = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', d)
    stem = d[:-4] if d.endswith('_out') else d
    for nm in ('%s_sprite_%03d.png' % (stem, n), 'frame_%03d.png' % n):
        p = os.path.join(base, nm)
        if os.path.isfile(p):
            return Image.open(p).convert('RGBA')
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--dirs', nargs='*', default=None)
    ap.add_argument('--cell', type=int, default=150)
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    data = load_rows(root, set(a.dirs) if a.dirs else None)
    if not data:
        print('无数据（检查 TSV 是否存在 / --dirs 是否拼对）')
        return 2

    dirs = sorted({k[0] for k in data})
    made = []
    for d in dirs:
        cell = a.cell
        cw, ch = cell + 8, cell + 26
        cols, rows = 9, len(TIERS)
        canvas = Image.new('RGB', (cols * cw, rows * ch), (240, 240, 242))
        dr = ImageDraw.Draw(canvas)
        for ri, tier in enumerate(TIERS):
            vmap = data.get((d, tier), {})
            for vi in range(1, 10):
                x, y = (vi - 1) * cw, ri * ch
                dr.rectangle([x, y, x + cw - 2, y + ch - 2], outline=(170, 170, 175))
                row = vmap.get(vi)
                if row is None:
                    dr.text((x + 4, y + 4), '%s v%d: 无' % (tier, vi), fill=(160, 0, 0))
                    continue
                fn = pick_frame(row)
                im = sprite(root, d, fn)
                label = '%s v%d f%03d' % (tier, vi, fn)
                if im is None:
                    dr.text((x + 4, y + 4), label + ' 缺PNG', fill=(160, 0, 0))
                    continue
                k = min(cell / im.width, cell / im.height)
                im = im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)
                canvas.paste(im, (x + (cw - im.width) // 2, y + 20 + (cell - im.height) // 2), im)
                dr.text((x + 4, y + 4), label, fill=(0, 0, 0))
        out = os.path.join(root, '.ai-tmp', 'test', 'D129b-view-pick-%s.png' % d)
        canvas.save(out)
        made.append(out)
        print('SHEET -> %s' % out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
