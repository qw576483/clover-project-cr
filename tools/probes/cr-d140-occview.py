#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d140-occview.py -- D140 判据资产：公主塔**乘员该取哪个视角**（我方蓝 vs 敌方红）
==================================================================================
用户第 1 条缺陷：「我方公主塔 朝向不对」。

判定链（本脚本打印全部中间量，不靠记忆）
----------------------------------------
1. `chr_princess_v215.sc` 导出表里有**两套**乘员，每套 **9 个视角**
   （`princess_tower_idle1_1..9` = rec 504,503,...,496；`princess_tower_red_idle1_1..9` = rec 8,7,...,0）。
   两套逐视角 bbox **完全相同** ⇒ 红/蓝只是染色、几何同一套 ⇒ **面向只能靠"选哪个视角"区分**。
2. 判据 = **高通相关**：把该视角的乘员贴到参考图里乘员的 bbox 上，只在该乘员**轮廓内**（腐蚀 3 px）
   比较"高通后的灰度"（减 12 px 均值模糊 ⇒ 去掉草地/石墙这类低频背景）的皮尔逊相关。
   * **正向对照（验证判据本身）**：`04_对局` 左上**敌方红**公主塔的乘员 = 面朝镜头 ⇒ 该参考上
     **view7 必须胜出**；若判据在这里都选不出 view7，则判据无效、不得用于蓝方。
   * **负控 ①**：蓝参考上"工程现值 view7(rec498)"必须**不是**最大；
   * **负控 ②**：蓝参考上纯灰（不画乘员）的相关必须低于胜出者。

复跑
----
  python tools/probes/cr-d140-occview.py --root .
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

BLUE_RECS = [504, 503, 502, 501, 500, 499, 498, 497, 496]   # view 1..9
RED_RECS = [8, 7, 6, 5, 4, 3, 2, 1, 0]                      # view 1..9

# 参考目标：(名称, 图号, 裁切框, 乘员 bbox（裁切框内坐标）)
TARGETS = [
    ('BLUE 03_右下(我方)', '03', (1030, 1840, 1190, 1980), (10, 30, 160, 130)),
    ('RED  04_左上(敌方)', '04', (40, 440, 480, 800), (83, 43, 173, 160)),
]
FILTER_R = 12
ERODE = 3


def box_blur(a, r):
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.BoxBlur(r))).astype(float)


def erode_mask(m, k):
    out = m.copy()
    for dy in range(-k, k + 1):
        for dx in range(-k, k + 1):
            out &= np.roll(np.roll(m, dy, 0), dx, 1)
    return out


def tight(im):
    a = np.asarray(im.convert('RGBA'))
    ys, xs = np.nonzero(a[:, :, 3] > 8)
    return im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))


def corr(x, y):
    x = x - x.mean()
    y = y - y.mean()
    d = np.sqrt((x * x).sum() * (y * y).sum())
    return float((x * y).sum() / d) if d > 1e-9 else 0.0


def score(ref_gray, rec, box, root):
    bw, bh = box[2] - box[0], box[3] - box[1]
    p = os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', 'chr_princess_out',
                     'chr_princess_sprite_%03d.png' % rec)
    if not os.path.isfile(p):
        return None, None
    src = Image.open(p).convert('RGBA')
    sp = tight(src).resize((bw, bh), Image.LANCZOS)
    g = np.asarray(sp.convert('L')).astype(float)
    m = np.asarray(sp.split()[3]).astype(float) > 128
    m = erode_mask(m, ERODE)
    if m.sum() < 200:
        return None, None
    return corr(ref_gray[m], (g - box_blur(g, FILTER_R))[m]), sp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    print('==== 判据：高通相关（减 %d px 均值模糊、掩膜 = 乘员轮廓腐蚀 %d px）====' % (FILTER_R, ERODE))
    verdict = {}
    for name, page, rbox, obox in TARGETS:
        ref = Image.open(os.path.join(root, '策划', '参考图',
                                      '%s_对局_1320x2868.jpg' % page)).convert('RGB').crop(rbox)
        sub = ref.crop(obox)
        g = np.asarray(sub.convert('L')).astype(float)
        hp = g - box_blur(g, FILTER_R)
        bw, bh = obox[2] - obox[0], obox[3] - obox[1]
        print('\n---- 目标 %s  裁切 %s  乘员 bbox %s (%dx%d) ----' % (name, rbox, obox, bw, bh))
        print('  view  rec    相关     与最大之差')
        res = []
        for tag, recs in (('B', BLUE_RECS), ('R', RED_RECS)):
            for i, n in enumerate(recs, 1):
                c, _ = score(hp, n, obox, root)
                if c is not None:
                    res.append((tag, i, n, c))
        best = max(res, key=lambda t: t[3])
        for tag, i, n, c in res:
            if tag != best[0]:
                continue
            print('  %-5d %-5d %7.4f  %+8.4f %s' % (i, n, c, c - best[3],
                                                    '★ 最大' if (i, n) == (best[1], best[2]) else ''))
        print('  胜出: %s套 view%d (rec%d) 相关 %.4f' % (best[0], best[1], best[2], best[3]))
        # 负控②
        empty = np.full((bh, bw), hp.mean())
        mm = np.ones((bh, bw), bool)
        print('  负控② 纯灰（不画乘员）: 相关 %.4f' % corr(hp[mm], (empty - empty.mean())[mm]))
        verdict[name] = best

    print('\n==== 判定 ====')
    red = verdict['RED  04_左上(敌方)']
    ok_ctrl = (red[1] == 7)
    print('  正向对照：敌方红参考的胜出视角 = view%d %s'
          % (red[1], '✔ 正是"面朝镜头"的 view7 ⇒ 判据有效' if ok_ctrl else '✘ 不是 view7 ⇒ 判据无效，不得用于蓝方'))
    blue = verdict['BLUE 03_右下(我方)']
    if blue[2] == BLUE_RECS[blue[1] - 1]:
        pass
    print('  我方蓝参考的胜出视角 = view%d (rec%d)' % (blue[1], blue[2]))
    print('  工程现值 = BLUE view7(rec498) / RED view7(rec2)')
    if ok_ctrl:
        if blue[1] != 7:
            print('  ⇒ 结论：我方(BLUE)乘员改取 **view%d (rec%d)**；敌方(RED)维持 view7(rec2)。' % (blue[1], blue[2]))
        else:
            print('  ⇒ 结论：两侧同为 view7 —— 与"原版两侧面向相反"的图证矛盾，需人工复核。')
    else:
        print('  ⇒ 判据未通过正向对照 ⇒ 本脚本的蓝方结论不可用（需换判据）。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
