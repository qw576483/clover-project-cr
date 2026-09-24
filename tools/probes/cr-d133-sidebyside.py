#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d133-sidebyside.py -- D133 判据资产：同机位 / 同尺度并排联络图拼装器
=========================================================================
把「我方实机截图（可裁切）」与「原版基线图（可裁切）」或「原版单帧素材（可裁切）」按**同一像素尺度**
并排成一张图，供人眼做 1:1 判定（skill §4 第 10 条：只有眼睛能判的 → 人 + 同机位并排图）。

命令行里显式给每个面板的 (标签, 路径, 裁切框, 放大倍数) —— ⛔ 脚本不自作裁切/不自作缩放，
所有几何都由调用方给出并写进回报（可复跑）。

复跑
----
  python tools/probes/cr-d133-sidebyside.py ^
    --panel "ours-full|.ai-tmp/screenshots/D133-tower-hpbar.png|0,0,1080,1920|1" ^
    --panel "orig20-full|策划/参考图/20_对局_1080x1920.jpg|0,0,1080,1920|1" ^
    --out .ai-tmp/test/CR-D133-cmp-hpbar.png
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


def parse_box(s):
    v = [int(x) for x in s.split(',')]
    if len(v) != 4:
        raise SystemExit('crop must be x0,y0,x1,y1 : %r' % s)
    return tuple(v)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--panel', action='append', default=[],
                    help='label|path|crop(x0,y0,x1,y1)|zoom')
    ap.add_argument('--cols', type=int, default=0, help='0 = 单行')
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    ims = []
    for spec in a.panel:
        parts = spec.split('|')
        label, path = parts[0], parts[1]
        box = parse_box(parts[2]) if len(parts) > 2 and parts[2] else None
        zoom = int(parts[3]) if len(parts) > 3 and parts[3] else 1
        p = path if os.path.isabs(path) else os.path.join(root, path)
        if not os.path.isfile(p):
            print('MISS %s' % p)
            continue
        im = Image.open(p).convert('RGB')
        if box:
            im = im.crop(box)
        if zoom != 1:
            im = im.resize((im.width * zoom, im.height * zoom), Image.NEAREST)
        ims.append((label, im))
        print('panel %-24s %s  crop=%s zoom=%d -> %dx%d'
              % (label, os.path.basename(p), box, zoom, im.width, im.height))
    if not ims:
        raise SystemExit('no panel')

    cols = a.cols if a.cols > 0 else len(ims)
    rows = (len(ims) + cols - 1) // cols
    cw = max(i.width for _, i in ims)
    ch = max(i.height for _, i in ims)
    lab = 18
    canvas = Image.new('RGB', (cols * cw, rows * (ch + lab)), (245, 245, 245))
    d = ImageDraw.Draw(canvas)
    for idx, (label, im) in enumerate(ims):
        x = (idx % cols) * cw
        y = (idx // cols) * (ch + lab)
        canvas.paste(im, (x + (cw - im.width) // 2, y))
        d.line([(x, y), (x, y + ch)], fill=(0, 0, 0), width=1)
        d.rectangle([(x, y + ch), (x + cw, y + ch + lab)], fill=(255, 255, 255))
        d.text((x + 4, y + ch + 4), label, fill=(0, 0, 0))
    out = a.out if os.path.isabs(a.out) else os.path.join(root, a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    canvas.save(out)
    print('OUT -> %s  (%dx%d)' % (out, canvas.width, canvas.height))
    return 0


if __name__ == '__main__':
    sys.exit(main())
