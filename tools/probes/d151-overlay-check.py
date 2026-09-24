# -*- coding: utf-8 -*-
# direct-fix: D151 原版 UI 复刻 —— 把**代码常量算出的矩形**直接描在原版基线图上（判据资产）
"""d151-overlay-check.py -- 不开 Unity，把 `DeckEditPanel` 的每个矩形**描边**画在原版
`07_卡组编辑_1242x2208.jpg`（缩放到 1080x1920）上，再裁几个局部放大图。

为什么要有它
------------
`d151-band-diff.py` 给的是**逐带数值**（能判 PASS/FAIL，但只覆盖几条带）。
`d151-layout-preview.py` 给的是**并排图**（人看着"差不多"，但没法判几像素）。
本脚本补的是第三件事：**把我们的框直接压在原版上** —— 框没对齐的地方一眼就能看见，
而且能看见"哪个元素在原版里根本不存在 / 我们多画了 / 少画了"。

这三张图一起才是完整的离线证据：数值（在哪条线上）+ 并排（像不像）+ 压印（对不对齐）。

⛔ 用途边界：它证明的是**版式对位**；⛔ 不能当"实机截图"，⛔ 也不证明素材正确。

输出
----
  .ai-tmp/test/D151-overlay-full.png     整屏压印
  .ai-tmp/test/D151-overlay-top.png      上区放大（0..380）
  .ai-tmp/test/D151-overlay-bottom.png   下区放大（1140..1920）

用法
----
  python tools/probes/d151-overlay-check.py
"""

import io
import os
import re
import sys

from PIL import Image, ImageDraw

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PANEL = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'Panels', 'DeckEditPanel.cs')
STYLE = os.path.join(ROOT, 'client', 'Assets', 'Scripts', 'UI', 'CrUiStyle.cs')
REF = os.path.join(ROOT, u'策划', u'参考图', '07_卡组编辑_1242x2208.jpg')
OUTDIR = os.path.join(ROOT, '.ai-tmp', 'test')


def load():
    raw = {}
    for p, pat in (
        (PANEL, r'private\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:float|int)\s+(\w+)\s*=\s*([^;]+);'),
        (STYLE, r'public\s+(?:static\s+)?(?:readonly\s+)?(?:const\s+)?(?:float|int)\s+(\w+)\s*=\s*([^;]+);'),
    ):
        with io.open(p, encoding='utf-8') as f:
            s = f.read().replace('\r\n', '\n')
        for m in re.finditer(pat, s):
            raw.setdefault(m.group(1), m.group(2).strip())
    vals = {}
    for _ in range(10):
        for name, expr in raw.items():
            if name in vals:
                continue
            e = re.sub(r'\bCrUiStyle\.(\w+)\b', r'\1', expr)
            e = re.sub(r'(\d+(?:\.\d+)?)[fF]\b', r'\1', e)
            e = re.sub(r'//.*$', '', e).strip()
            try:
                vals[name] = eval(e, {'__builtins__': {}}, dict(vals))
            except Exception:
                pass
    return vals


def draw_all(img, C, boxes):
    """boxes: list of (x, y, w, h, label) —— 一律按"距左边 x / 距顶边 y"口径。"""
    d = ImageDraw.Draw(img)
    for x, y, w, h, lab in boxes:
        d.rectangle([x, y, x + w - 1, y + h - 1], outline=(255, 0, 200))
        if lab:
            d.text((x + 2, max(0, y - 13)), lab, fill=(255, 240, 0))
    return img


def main():
    C = load()
    W, H = int(C['DesignW']), int(C['DesignH'])
    ref = Image.open(REF).convert('RGB').resize((W, H), Image.LANCZOS)

    boxes = [
        (C['TabDecksX'], C['TabDecksY'], C['TabW'], C['TabDecksH'], 'TabDecks'),
        (C['TabCollectionX'], C['TabOffY'], C['TabWCollection'], C['TabOffH'], 'TabColl'),
        (0, C['NumRowY'], W, C['NumRowH'], 'NumRow'),
        (0, C['BannerTopY'], W, C['BannerH'], 'Banner'),
    ]
    for i in range(7):
        boxes.append((C['NumBtnX0'] + i * C['NumBtnPitch'], C['NumBtnY'],
                      C['NumBtnW'], C['NumBtnH'], 'N%d' % (i + 1)))
    for i in range(8):
        col, row = i % 4, i // 4
        boxes.append((C['GridLeftX'] + col * C['CardStepX'], C['GridTopY'] + row * C['CardStepY'],
                      C['CardW'], C['CardH'], 'C%d' % (i + 1)))
    boxes.append((C['AvgPillX'], C['BottomRowY'], C['AvgPillW'], C['BottomRowH'], 'Pill'))
    for i in range(3):                       # ★ 第三片：原版底行右侧是 3 颗方形工具钮
        boxes.append((C['BottomBtnX0'] + i * C['BottomBtnPitch'], C['BottomRowY'],
                      C['BottomBtnW'], C['BottomRowH'], 'B%d' % (i + 1)))

    out = draw_all(ref.copy(), C, boxes)
    full = os.path.join(OUTDIR, 'D151-overlay-full.png')
    out.save(full)
    out.crop((0, 0, W, 380)).save(os.path.join(OUTDIR, 'D151-overlay-top.png'))
    out.crop((0, 1140, W, H)).save(os.path.join(OUTDIR, 'D151-overlay-bottom.png'))

    print('画布 %dx%d' % (W, H))
    for p in ('D151-overlay-full.png', 'D151-overlay-top.png', 'D151-overlay-bottom.png'):
        print('  .ai-tmp/test/%s' % p)
    return 0


if __name__ == '__main__':
    sys.exit(main())
