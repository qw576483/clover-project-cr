#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-t1i-occ.py -- 公主塔乘员层的**落点/缩放**量定（CR-T1i 建立，CR-T1j 修正锚点口径）
====================================================================================
★ CR-T1j 的关键修正（本文件的两个版本差别就在这里）
----------------------------------------------------
工程里的塔/乘员 PNG 是 **Sprite Mode = Multiple + 自动切片** 导入的：运行时 `sprite.rect` 是
**每张图的 alpha 裁剪框**（实测 `frame_009_0 rect=173x198`，而 PNG 本体是 407×471）。
⇒ Unity 画的锚点 = **裁剪框中心**，**不是画布中心**！
所以任何"按画布中心对齐"的偏移（CR-T1i 用的 −66.5/+123）都是错的：
同画布的层（塔体 rec 9/10 与 前墙 rec 7/8）裁剪框位置相近 ⇒ 误差只有几 px（看不出来）；
但跨画布的**乘员**（`chr_princess_out` 的画布 268×180）裁剪框中心离它自己的画布中心很远
⇒ 整块位移 ≈ 0.6~0.8 格 ⇒ 实机就是"公主飘在塔顶外面"（主 agent 读图发现）。

本脚本按**裁剪框锚点**重算（与运行时同一条公式）：
  画布点 P 被画到 `localPos + scale × (P − 裁剪框中心)`
  ⇒ 让乘员的 **bbox 底心** 落在塔体画布上的 (塔腔中心 x, 落脚线 y)：
       localPos = target − trimCenter(body) + (0, scale × h_occ / 2)
  （h_occ = 乘员裁剪框高；bbox 底心相对裁剪框中心 = (0, −h/2)）

锚点自校准
----------
`--calib` 用几种 alpha 阈值算 PNG 的裁剪框，与运行时 dump 的 rect 比对
（实测 `frame_009_0 = 173x198`、`frame_010_0 = 173x210`、乘员 `93x100`），
确定 Unity 自动切片用的阈值 + extrude。

复跑
----
  python tools/probes/cr-t1i-occ.py --root . --calib          # 校准裁剪框
  python tools/probes/cr-t1i-occ.py --root .                  # 打出 localPos（工程要写的数）
  python tools/probes/cr-t1i-occ.py --root . --sweep          # 与原版 1:1 并排逐格看
"""
import argparse
import os
import sys

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass
import numpy as np
from PIL import Image, ImageDraw

B, P = 'building_tower', 'chr_princess'
ART = dict(blue=10, red=9)
OCC = dict(blue=498, red=2)
CANVAS_H = 471.0          # 塔画布高（PNG 本体 407x471）
FOOT_LINE = 195.0         # 落脚线（塔体画布 y 向下；由 --sweep 逐格目视选定）
CAVITY_CX = 202.5         # 塔腔中心 x（塔体木地板带 x 中心，见量取输出）


def unit_dir(root, unit):
    return os.path.join(root, '原版资源', 'cr-assets-png', 'assets', 'sc', unit + '_out')


def sprite(root, unit, rec):
    for fmt in (unit + '_sprite_%03d.png', 'frame_%03d.png'):
        p = os.path.join(unit_dir(root, unit), fmt % rec)
        if os.path.isfile(p):
            return Image.open(p).convert('RGBA')
    raise IOError('缺 %s rec %d' % (unit, rec))


def trim_rect(im, thr, extrude):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.nonzero(a > thr)
    if len(xs) == 0:
        return None
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())
    x0 = max(0, x0 - extrude); y0 = max(0, y0 - extrude)
    x1 = min(im.width - 1, x1 + extrude); y1 = min(im.height - 1, y1 + extrude)
    return (x0, y0, x1, y1)


def local_pos(root, team, scale, foot=FOOT_LINE, cx=CAVITY_CX, thr=0, extrude=1):
    art = sprite(root, B, ART[team])
    occ = sprite(root, P, OCC[team])
    rb = trim_rect(art, thr, extrude)
    ro = trim_rect(occ, thr, extrude)
    # 画布 y 向上：PNG 行号 y0(顶) ↔ 画布 y = H-1-y0
    tb = ((rb[0] + rb[2] + 1) / 2.0, (art.height - 1 - (rb[1] + rb[3]) / 2.0))
    h_occ = ro[3] - ro[1] + 1
    target = (cx, art.height - foot)
    lx = target[0] - tb[0]
    ly = target[1] - tb[1] + scale * h_occ / 2.0
    return art, occ, rb, ro, tb, h_occ, (lx, ly)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--scale', type=float, default=1.13, help='乘员层缩放 PrincessOccupantScale')
    ap.add_argument('--tower-scale', type=float, default=1.65)
    ap.add_argument('--foot', type=float, default=FOOT_LINE)
    ap.add_argument('--foots', default='165,185', help='扫描用落脚线候选（逗号分隔）')
    ap.add_argument('--calib', action='store_true')
    ap.add_argument('--sweep', action='store_true')
    a = ap.parse_args()
    root = os.path.abspath(a.root)

    if a.calib:
        print('运行时 dump 的 rect: frame_009_0=173x198  frame_010_0=173x210  Princess(498/2)=93x100')
        for thr, ex in ((8, 0), (0, 0), (0, 1), (8, 1)):
            line = 'thr=%-2d extrude=%d : ' % (thr, ex)
            for unit, rec in ((B, 9), (B, 10), (P, 498)):
                r = trim_rect(sprite(root, unit, rec), thr, ex)
                line += '%s%d=%dx%d  ' % (unit[:2], rec, r[2] - r[0] + 1, r[3] - r[1] + 1)
            print('  ' + line)
        print('（与 dump 一致的那一行 = Unity 自动切片的实际口径）')

    cells = []
    ref = Image.open(os.path.join(root, '策划', '参考图', '03_对局_1320x2868.jpg')).convert('RGB')
    if a.sweep:
        cells.append(('REF red princess 1:1', ref.crop((15, 460, 340, 810))))
    feet_list = [a.foot] if not a.sweep else [float(x) for x in a.foots.split(',')]
    for team in ('red', 'blue'):
        art, occ, rb, ro, tb, h_occ, L = local_pos(root, team, a.scale, a.foot)
        print('--- %s --- 塔体 rec %d 裁剪框(画布 y 向下) x=%d..%d y=%d..%d 中心(画布 y 向上)=(%.1f, %.1f)'
              % (team, ART[team], rb[0], rb[2], rb[1], rb[3], tb[0], tb[1]))
        print('    乘员 rec %d 裁剪框 x=%d..%d y=%d..%d 高=%d  ⇒ localPos(画布 px, y 上为正) = (%.1f, %.1f)'
              % (OCC[team], ro[0], ro[2], ro[1], ro[3], h_occ, L[0], L[1]))
        print('    ⇒ 工程写 Dx=%.1f  Dy=%.1f（Dy 为 y 向下口径）' % (L[0], -L[1]))
        if a.sweep:
            ts = a.tower_scale
            comp = Image.new('RGBA', art.size, (0, 0, 0, 0))
            comp.alpha_composite(art)
            # ★ 按裁剪框锚点放置：画布点 P 画到 localPos + scale×(P − trimCenter)
            #   T = 裁剪框中心（**y 向下的图像坐标**）；缩放要绕裁剪框中心做。
            T = ((ro[0] + ro[2] + 1) / 2.0, (ro[1] + ro[3] + 1) / 2.0)
            top = sprite(root, B, ART[team] - 2)
            for fy in feet_list:
                _, _, _, _, _, _, (dx, dy) = local_pos(root, team, a.scale, fy)
                c2 = comp.copy()
                occ_s = occ.resize((max(1, int(occ.width * a.scale)), max(1, int(occ.height * a.scale))),
                                   Image.LANCZOS)
                # 缩放后裁剪框中心相对整幅的位置
                cx_s = occ_s.width / 2.0 + (T[0] - occ.width / 2.0) * a.scale
                cy_s = occ_s.height / 2.0 + (T[1] - occ.height / 2.0) * a.scale
                # 目标：裁剪框中心画到画布点 (art.width/2 + dx, art.height/2 - dy)（y 向下为屏幕坐标）
                px = int(round((art.width / 2.0 + dx) - cx_s))
                py_top = int(round((art.height / 2.0 - dy) - cy_s))
                c2.alpha_composite(occ_s, (px, py_top))
                c2.alpha_composite(top)
                crop = c2.crop((100, 60, 310, 310))
                crop = crop.resize((int(crop.width * ts), int(crop.height * ts)), Image.LANCZOS)
                bg = Image.new('RGB', crop.size, (124, 176, 74))
                bg.paste(crop.convert('RGB'), (0, 0), crop)
                cells.append(('%s foot=%.0f localPos=(%.1f,%.1f) x%.2f' % (team, fy, dx, dy, ts), bg))
    if a.sweep and cells:
        cw = max(c[1].width for c in cells) + 8
        ch = max(c[1].height for c in cells) + 20
        rows = (len(cells) + 1) // 2
        canvas = Image.new('RGB', (2 * cw, rows * ch), (235, 235, 235))
        dd = ImageDraw.Draw(canvas)
        for i, (tag, im) in enumerate(cells):
            x = (i % 2) * cw
            y = (i // 2) * ch
            canvas.paste(im, (x + 4, y + 18))
            dd.text((x + 5, y + 3), tag, fill=(0, 0, 0))
            dd.rectangle([x + 2, y + 16, x + 2 + im.width, y + 16 + im.height], outline=(120, 120, 120))
        out = os.path.join(root, '.ai-tmp', 'test', 'CR-T1j-occ.png')
        canvas.save(out)
        print('SWEEP -> %s' % out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
