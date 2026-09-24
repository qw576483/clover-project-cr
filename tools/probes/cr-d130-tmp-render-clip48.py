#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时判据：按 .sc 的 placement 表把 training_area_bg(clip 48) 还原出来，验证
"矩阵 d 到底是相对什么锚点" —— 出两种假设的图，目视判断哪种是真（d130x/clip48-A.png / -B.png）。

复跑：C:/Python312/python tools/probes/cr-d130-tmp-render-clip48.py
"""
import io
import os
import sys

from PIL import Image
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SC = os.path.join(ROOT, '原版资源', 'sc', 'arena_training_v215.sc')
PNG_DIR = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')


class R(io.BytesIO):
    def rb(self):
        return int.from_bytes(self.read(1), 'little')

    def ru16(self):
        return int.from_bytes(self.read(2), 'little')

    def ri16(self):
        return int.from_bytes(self.read(2), 'little', signed=True)

    def ru32(self):
        return int.from_bytes(self.read(4), 'little')

    def ri32(self):
        return int.from_bytes(self.read(4), 'little', signed=True)


def load_sc(fn):
    raw = open(fn, 'rb').read()
    if raw[:2] == b'\x53\x43':
        import lzma
        off = 10 + int.from_bytes(raw[6:10], 'big')
        d = raw[off:]
        return lzma.LZMADecompressor().decompress(d[0:9] + b"\x00" * 4 + d[9:])
    return raw


def parse():
    data = load_sc(SC)
    S = R(data)
    S.read(12)
    S.read(5)
    n = S.ru16()
    ids = [S.ru16() for _ in range(n)]
    names = []
    for _ in range(n):
        L = S.rb()
        names.append(S.read(L).decode('utf-8', 'replace'))
    recs = []
    while len(data) - S.tell() > 0:
        tag = S.read(1).hex()
        size = S.ru32()
        recs.append((tag, S.read(size)))
    mats = [p for t, p in recs if t == '08']
    clips = {}
    for tag, payload in recs:
        if tag != '0c':
            continue
        B = R(payload)
        cid = B.ru16()
        B.rb()
        B.ru16()
        cnt1 = B.ri32()
        trip = [(B.ru16(), B.ru16(), B.ru16()) for _ in range(max(0, cnt1))]
        cnt2 = B.ri16()
        sids = [B.ri16() for _ in range(max(0, cnt2))]
        clips[cid] = dict(trip=trip, sids=sids)
    # export -> clip id
    ex = dict(zip(names, ids))
    return mats, clips, ex


def load_png(shape_id):
    return Image.open(os.path.join(PNG_DIR, 'frame_%03d.png' % shape_id)).convert('RGBA')


def bbox_center(im):
    a = np.array(im)
    ys, xs = np.where(a[:, :, 3] > 16)
    return (xs.min() + xs.max()) / 2.0, (ys.min() + ys.max()) / 2.0


def main():
    os.makedirs(OUT, exist_ok=True)
    mats, clips, ex = parse()
    cid = ex['training_area_bg']
    c = clips[cid]
    print('clip=%d sids=%d trip=%d' % (cid, len(c['sids']), len(c['trip'])))

    def mat(idx):
        if idx == 65535:
            return dict(sx=1.0, sy=1.0, px=0.0, py=0.0)
        b = mats[idx]
        v = [int.from_bytes(b[i * 4:i * 4 + 4], 'little', signed=True) for i in range(6)]
        return dict(sx=v[0] / 1024.0, sy=v[3] / 1024.0, px=v[4] / 20.0, py=v[5] / 20.0)

    cw, ch = 3000, 4200
    for mode in ('A', 'B', 'C'):
        canvas = Image.new('RGBA', (cw, ch), (0, 0, 0, 0))
        ox, oy = cw // 2, ch // 2
        for ci, mi, ti in c['trip']:
            sid = c['sids'][ci]
            im = load_png(sid)
            m = mat(mi)
            if abs(m['sx'] - 1.0) > 0.001:
                im = im.resize((max(1, int(round(im.width * m['sx']))), max(1, int(round(im.height * m['sy'])))), Image.LANCZOS)
            if mode == 'A':
                # final = natural(pos in png) + d
                pos = (int(round(ox + m['px'])), int(round(oy + m['py'])))
            elif mode == 'B':
                # d 是 bbox 中心
                cx, cy = bbox_center(im)
                pos = (int(round(ox + m['px'] - cx)), int(round(oy + m['py'] - cy)))
            else:
                # d 是 bbox 左上角
                a = np.array(im)
                ys, xs = np.where(a[:, :, 3] > 16)
                pos = (int(round(ox + m['px'] - xs.min())), int(round(oy + m['py'] - ys.min())))
            canvas.alpha_composite(im, pos)
        a = np.array(canvas)
        ys, xs = np.where(a[:, :, 3] > 16)
        if len(xs) == 0:
            print(mode, 'EMPTY')
            continue
        box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
        cr = canvas.crop(box)
        p = os.path.join(OUT, 'clip48-%s.png' % mode)
        cr.convert('RGB').save(p)
        print('mode %s bbox=%s size=%s -> %s' % (mode, box, cr.size, p))
    return 0


if __name__ == '__main__':
    sys.exit(main())
