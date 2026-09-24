# -*- coding: utf-8 -*-
"""D155 之二：把 `ui_out` 265（1×36 竖向渐变条）拉伸后**乘单一 tint**，
能否复现参考图上页签的竖向渐变？

判据（**反向断言**：本探针是来**证伪**"用 265 铺页签渐变"的，所以"相符"才是红）
    M1  若 `tint(t)` 的逐通道极差 ≤ 0.10 ⇒ **相符** ⇒ 265 能复现参考图渐变（红：则应当采用）；
        极差 > 0.10 ⇒ **不符**（绿：本工程不铺 265，理由实测成立）。
    M2  该 tint 与页签单色 tint（TabOnColor ÷ 帧内填色）若最大通道差 ≤ 0.12 ⇒ 同口径（红）；
        > 0.12 ⇒ 不同口径（绿）。
    ⚠️ 命名用 `M`（mismatch）而不用 `P`，就是为了避免"绿 = 通过判据"被误读成"265 合格"。
"""
import io
import os
import sys

import numpy as np
from PIL import Image

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SRC = os.path.join(ROOT, '原版资源', 'cr-assets-png', 'assets', 'sc', 'ui_out')
MANIFEST = os.path.join(ROOT, '.ai-tmp', 'screenshots', 'ui-index-manifest.tsv')
REF = os.path.join(ROOT, '策划', '参考图', '07_卡组编辑_1242x2208.jpg')

TAB_TOP, TAB_BOT = 28, 156          # Decks 页签签体范围 @1242（E25 上缘 27 / NumRowY 上缘 157）


def manifest_bbox(frame):
    with io.open(MANIFEST, encoding='utf-8') as fh:
        fh.readline()
        for line in fh:
            c = line.rstrip('\r\n').split('\t')
            if len(c) >= 9 and c[0] == 'ui_out' and c[2] == str(frame):
                return int(c[5]), int(c[6]), int(c[7]), int(c[8])
    raise KeyError(frame)


def main():
    x, y, w, h = manifest_bbox(265)
    im = Image.open(os.path.join(SRC, 'ui_sprite_265.png')).convert('RGBA')
    g = np.asarray(im.crop((x, y, x + w, y + h))).astype(float)[:, 0, :3]      # h×3

    ref = np.asarray(Image.open(REF).convert('RGB')).astype(float)
    # 参考图：取 x=150..290（在 Decks 签内、字左边）的逐行中位色
    refcol = np.median(ref[TAB_TOP:TAB_BOT, 150:290, :], axis=1)               # (TAB_BOT-TAB_TOP)×3

    # 265 里有效的渐变段（末尾 4 格是透明）
    valid = g.max(axis=1) > 40
    gv = g[valid]
    print('265 有效渐变 %d 格（%s → %s）' % (len(gv), '#%02X%02X%02X' % tuple(gv[0].astype(int)),
                                     '#%02X%02X%02X' % tuple(gv[-1].astype(int))))
    print('参考图页签 x150..290 逐行：%s → %s（%d 行）' % (
        '#%02X%02X%02X' % tuple(refcol[0].astype(int)),
        '#%02X%02X%02X' % tuple(refcol[-1].astype(int)), len(refcol)))

    # 把 265 线性重采样到参考图的 128 行（= 拉伸）
    idx = np.linspace(0, len(gv) - 1, len(refcol))
    gs = gv[idx.astype(int)].astype(float)

    ratio = refcol / np.maximum(gs, 1.0)
    print()
    print('%4s %-9s %-9s %-9s %s' % ('行', '参考图', '265拉伸', 'tint', ''))
    for i in range(0, len(refcol), 8):
        print('%4d %s %s (%.3f,%.3f,%.3f)' % (
            i, '#%02X%02X%02X' % tuple(refcol[i].astype(int)), '#%02X%02X%02X' % tuple(gs[i].astype(int)),
            ratio[i, 0], ratio[i, 1], ratio[i, 2]))
    print()
    mid = ratio[8:-8]          # 掐掉首尾各 8 行（JPEG 在边缘最脏）
    spread = mid.max(axis=0) - mid.min(axis=0)
    med = np.median(mid, axis=0)
    print('掐首尾 8 行后 tint 的逐通道极差 = (%.3f, %.3f, %.3f)' % tuple(spread))
    print('tint 中位 = (%.4f, %.4f, %.4f)' % tuple(med))
    print('页签单色 tint（TabOnColor (33,124,193) ÷ (76,172,255)）= (0.4342, 0.7209, 0.7569)')
    d_b = float(np.abs(med - np.array([0.4342, 0.7209, 0.7569])).max())
    print('两者最大通道差 = %.4f' % d_b)
    print()
    rc = 0
    if spread.max() <= 0.10:
        print('  NG  M1：单 tint **够**（逐通道极差 %.3f ≤ 0.10）⇒ 265 能复现参考图渐变 '
              '⇒ 本工程**应当**采用 265 铺页签，当前"不铺"的决定要撤回' % spread.max())
        rc = 1
    else:
        print('  OK  M1：单 tint 不够（逐通道极差 %.3f > 0.10）⇒ 265 拉伸 + 单 tint '
              '复现不出参考图页签的渐变 ⇒ 不采用它是对的（残留差异已登记 D155）' % spread.max())
    if d_b <= 0.12:
        print('  NG  M2：该 tint 与页签单色 tint 差 %.4f ≤ 0.12 ⇒ 同口径（那 265 就该能用）' % d_b)
        rc = 1
    else:
        print('  OK  M2：该 tint 与页签单色 tint 差 %.4f > 0.12 ⇒ 两者不是同一口径' % d_b)
    print()
    print('判据 2 条，红 %d 条' % (2 if rc else 0))
    print('Verdict: %s' % ('FAIL（265 其实可用 ⇒ 要改工程）' if rc else
                           'PASS（判据成立：265 与参考图页签剖面不符 ⇒ 本工程⛔ 不铺 265 渐变条，理由实测登记）'))
    return rc


if __name__ == '__main__':
    sys.exit(main())
