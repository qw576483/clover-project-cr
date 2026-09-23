#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1i-princess.py -- CR-T1i 判据资产：找常态**公主塔**的 art 并给出它的尺寸换算
=================================================================================
候选来自 `building_tower_v215.sc` 的 Export 表（本脚本用 `cr-t1i-hunt.py` 的同一套解析）：
  StarTower_base_blue (clip 236) = rec 10 = 150x200
  StarTower_base_red  (clip 235) = rec 9  = 151x186
  StarTower_top_blue  (clip 234) = rec 8  = 125x71
  StarTower_top_red   (clip 233) = rec 7  = 126x72
本脚本做两件事：
1. 打印这 4 个记录在 **407x471 画布**里的 bbox —— 若 top 的 bbox 正好落在 base 的上沿且水平居中，
   则"同 localPosition 叠放"就是原版对位（与王塔层同理），⛔ 不需要自造偏移。
2. 出**同格尺**并排图：左 = 原版对局图 03 的公主塔裁切（91.5 px/格），
   右 = 我们的 art 按 91.5/100 = 0.915 缩放（同格尺），再给一张按"目标宽 172 px"缩放的。

复跑
----
  python tools/probes/cr-t1i-princess.py --root .
"""
import argparse
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass
try:
    import numpy as np
    from PIL import Image, ImageDraw
except Exception as e:
    print('需要 numpy + PIL：%s' % e)
    raise


def unit_dir(root, unit):
    return os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', unit + '_out')


def sprite(root, unit, rec):
    for fmt in (unit + '_sprite_%03d.png', 'frame_%03d.png'):
        p = os.path.join(unit_dir(root, unit), fmt % rec)
        if os.path.isfile(p):
            return Image.open(p).convert('RGBA')
    raise IOError('缺 rec %d' % rec)


def bbox(im):
    a = np.asarray(im)
    ys, xs = np.nonzero(a[:, :, 3] > 8)
    if not len(xs):
        return None
    return (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))


def stone_width(im, bb):
    """浅色石体宽（原版 03 图上白色掩膜同一口径：r>205,g>195,b>185），用于"同口径"比宽。"""
    a = np.asarray(im)
    r, g, b, al = a[:, :, 0].astype(int), a[:, :, 1].astype(int), a[:, :, 2].astype(int), a[:, :, 3]
    m = (al > 8) & (r > 205) & (g > 195) & (b > 185)
    ys, xs = np.nonzero(m)
    if not len(xs):
        return None
    return (int(xs.min()), int(xs.max()), int(xs.max()) - int(xs.min()) + 1, len(xs))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    U = 'building_tower'
    REC = dict(base_blue=10, base_red=9, top_blue=8, top_red=7)

    ims = {k: sprite(root, U, v) for k, v in REC.items()}
    print('画布 = %dx%d' % ims['base_blue'].size)
    print('\n---- 各记录在画布内的 bbox（x0,y0,x1,y1） ----')
    bbs = {}
    for k, v in REC.items():
        bb = bbox(ims[k])
        bbs[k] = bb
        sw = stone_width(ims[k], bb)
        print('  %-10s rec=%-3d bbox=%-24s 宽=%3d 高=%3d   浅色石体 x=%d..%d 宽=%d'
              % (k, v, str(bb), bb[2] - bb[0] + 1, bb[3] - bb[1] + 1, sw[0], sw[1], sw[2]))

    for team, bk, tk in (('blue', 'base_blue', 'top_blue'), ('red', 'base_red', 'top_red')):
        bb, bt = bbs[bk], bbs[tk]
        print('\n%s: base 上沿 y=%d、top 下沿 y=%d ⇒ 竖直重叠 = %d px；'
              'base 中心 x=%.1f、top 中心 x=%.1f ⇒ 水平错 %.1f px'
              % (team, bb[1], bt[3], bb[1] - bt[3],
                 (bb[0] + bb[2]) / 2.0, (bt[0] + bt[2]) / 2.0,
                 (bt[0] + bt[2]) / 2.0 - (bb[0] + bb[2]) / 2.0))

    # ---- 同格尺并排图 ----
    ref = Image.open(os.path.join(root, '策划', '参考图', '03_对局_1320x2868.jpg')).convert('RGB')
    PX_PER_TILE = 91.5
    ref_crops = [('ref red princess (03)', ref.crop((25, 470, 330, 800))),
                 ('ref blue princess (03)', ref.crop((1006, 1790, 1295, 2200)))]

    def compose(team):
        bk = 'base_%s' % team
        tk = 'top_%s' % team
        out = Image.new('RGBA', ims[bk].size, (0, 0, 0, 0))
        out.alpha_composite(ims[bk])
        out.alpha_composite(ims[tk])          # 同画布 ⇒ 同 localPosition 叠放
        return out

    def fit(im, box):
        k = min(box[0] / im.width, box[1] / im.height)
        return im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)

    cells = []
    for tag, c in ref_crops:
        cells.append((tag, fit(c, (420, 460))))
    for team in ('red', 'blue'):
        comp = compose(team)
        # 单 base（不叠 top）也看一眼，判断 top 到底该不该叠
        cells.append(('%s base only  (art @91.5px/格, 即 x0.915)' % team,
                      fit(ims['base_%s' % team].resize((int(ims['base_%s' % team].width * 0.915),
                                                        int(ims['base_%s' % team].height * 0.915)),
                                                       Image.LANCZOS), (420, 460))))
        cells.append(('%s base+top   (art @91.5px/格)' % team, fit(comp.resize(
            (int(comp.width * 0.915), int(comp.height * 0.915)), Image.LANCZOS), (420, 460))))
        k = 172.0 / 150.0   # 让 art 宽 = 原版量到的 172 px（垛口宽口径）
        cells.append(('%s base+top   (art x%.2f ⇒ 宽=172px)' % (team, k),
                      fit(comp.resize((int(comp.width * k), int(comp.height * k)), Image.LANCZOS), (420, 460))))

    cw, ch = 430, 486
    cols = 3
    rows = (len(cells) + cols - 1) // cols
    canvas = Image.new('RGB', (cols * cw, rows * ch), (245, 245, 245))
    dr = ImageDraw.Draw(canvas)
    for i, (tag, im) in enumerate(cells):
        x = (i % cols) * cw
        y = (i // cols) * ch
        canvas.paste(im.convert('RGB'), (x + (cw - im.width) // 2, y + 22))
        dr.text((x + 4, y + 4), tag, fill=(0, 0, 0))
        dr.rectangle([x + 1, y + 1, x + cw - 2, y + ch - 2], outline=(180, 180, 180))
    out = os.path.join(root, '.ai-tmp', 'test', 'CR-T1i-princess-compare.png')
    canvas.save(out)
    print('\nSHEET -> %s' % out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
