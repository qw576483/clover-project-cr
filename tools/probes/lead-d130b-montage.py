# -*- coding: utf-8 -*-
"""D130b 四格对照图：原版 / 改前(旧DLL) / 含多余木板(round2) / 最终(round3)。

口径（⛔ 不手挑：按"格"对齐，不是按像素数对齐）：
  本实现实机 60.00 px/格（census: pxPerWorldX=60.00000）；原版训练营 03 实测 103.3 px/格。
  所有格子都取"水带中心上下各 2 格"，再统一缩放到 1080 宽 ⇒ 四张图的**格尺度一致**，
  可以横着比"水边有没有木板 / 桥落在哪 / 车道通不通"。
输出 .ai-tmp/test/d130x/D130b-montage-4way.png
"""
import os
import sys

from PIL import Image, ImageDraw

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
D = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')
OUT = os.path.join(D, 'D130b-montage-4way.png')

OURS_PXT = 59.5          # 实机纵向 px/格（水带 119px = 2 格，逐轮实测同值）
OURS_WATER_CY = 959.0    # 实机水带中心行（900..1018）
REF_PXT = (1442 - 1337 + 1) / 2.0   # 原版 03 水带 rows 1337..1442 = 2 格 ⇒ px/格
REF_WATER_CY = (1337 + 1442) / 2.0

PANELS = [
    (os.path.join(ROOT, '策划', '基线图', '03_对局_1320x2868.jpg'), REF_PXT, REF_WATER_CY,
     'ORIGINAL  training camp 03_对局_1320x2868.jpg',
     'grass + narrow mud lip, NO planks; bridge crosses the water', (255, 235, 160)),
    (os.path.join(D, 'D130b-R1-olddll-natural.png'), OURS_PXT, OURS_WATER_CY,
     'BEFORE  round-1 shot = CR.dll 19:49 (PRE-change build)',
     'in-tile-set plank wall sits in the middle of the field', (255, 160, 160)),
    (os.path.join(D, 'D130b-R2-withRedBank-natural.png'), OURS_PXT, OURS_WATER_CY,
     'round-2  (plank wall gone, but GroundRedBank had been added)',
     'a whole band of planks was pasted above the water', (255, 200, 140)),
    (os.path.join(ROOT, '.ai-tmp', 'screenshots', 'D130b-live-natural.png'), OURS_PXT, OURS_WATER_CY,
     'AFTER  round-3  (GroundRedBank REVERTED = current build)',
     'grass on both banks, lanes run to the water, two bridges cross it', (160, 255, 170)),
]

TARGET_W = 1080
HALF_TILES = 2.0
PAD = 12
LABEL_H = 20
GAP = 34          # 每格下方给副标题留的高度
UNIFIED_H = int(round(4 * OURS_PXT))   # 4 格统一渲染成 238px ⇒ 四格图的**格尺度一致**


def main():
    panels = []
    for path, pxt, cy, title, sub, color in PANELS:
        if not os.path.exists(path):
            print('MISSING %s' % path)
            return 1
        im = Image.open(path).convert('RGB')
        half = int(round(pxt * HALF_TILES))
        y0 = max(0, int(round(cy - half)))
        y1 = min(im.height, int(round(cy + half)))
        c = im.crop((0, y0, im.width, y1))
        # ⛔ 统一高度（不是统一宽度）：原版 53 px/格、我们 59.5 px/格 ⇒ 只统一宽度会让两图"格"不一样大
        c = c.resize((TARGET_W, UNIFIED_H), Image.LANCZOS)
        panels.append((c, title, sub, color))
        print('panel %-58s crop rows %d..%d (%.1f px/tile) -> %s' % (os.path.basename(path), y0, y1, pxt, c.size))

    hh = UNIFIED_H
    total_h = sum(LABEL_H + hh + GAP for _ in panels) + PAD
    canvas = Image.new('RGB', (TARGET_W + PAD * 2, total_h), (24, 24, 28))
    d = ImageDraw.Draw(canvas)
    y = PAD
    for im, title, sub, color in panels:
        d.text((PAD, y + 2), title, fill=color)
        y += LABEL_H
        canvas.paste(im, (PAD, y))
        # 水带上下沿横线（水带恒 2 格 ⇒ 中心 ±1 格；统一高度下 ±hh/4）
        yc = y + hh // 2
        d.line([PAD, yc - hh // 4, PAD + TARGET_W, yc - hh // 4], fill=(255, 90, 90), width=2)
        d.line([PAD, yc + hh // 4, PAD + TARGET_W, yc + hh // 4], fill=(255, 90, 90), width=2)
        d.text((PAD + 4, y + hh + 6), sub, fill=(190, 190, 190))
        y += hh + GAP
    canvas.save(OUT)
    print('wrote %s %s' % (OUT, canvas.size))
    return 0


if __name__ == '__main__':
    sys.exit(main())
