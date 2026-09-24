# -*- coding: utf-8 -*-
"""D155：页签带 逐带对比（原版 07 vs 本工程离线渲染），@1080 同尺度。"""
import os
import sys

from PIL import Image, ImageDraw

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
REF = os.path.join(ROOT, '策划', '参考图', '07_卡组编辑_1242x2208.jpg')
MOCK = os.path.join(ROOT, '.ai-tmp', 'test', 'D151-layout-mock.png')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'D155-tabband-cmp.png')

W, H = 1080, 1920
Y0, Y1 = 0, 190                      # 页签带

ref = Image.open(REF).convert('RGB').resize((W, H), Image.LANCZOS).crop((0, Y0, W, Y1))
ours = Image.open(MOCK).convert('RGB').crop((0, Y0, W, Y1))

canvas = Image.new('RGB', (W, (Y1 - Y0) * 2 + 56), (24, 24, 30))
canvas.paste(ref, (0, 26))
canvas.paste(ours, (0, (Y1 - Y0) + 40))
d = ImageDraw.Draw(canvas)
try:
    f = None
    for p in ('C:/Windows/Fonts/msyhbd.ttc', 'C:/Windows/Fonts/simhei.ttf'):
        if os.path.exists(p):
            from PIL import ImageFont
            f = ImageFont.truetype(p, 18)
            break
except Exception:
    f = None
d.text((8, 8), '原版 07_卡组编辑（缩放到 1080）— 页签带', fill=(255, 220, 120), font=f)
d.text((8, (Y1 - Y0) + 30), '本工程（D155：原版 166 镜像九宫格 + 实测 tint + 实测顶亮线）', fill=(150, 230, 255), font=f)
canvas.save(OUT)
print('  %s' % os.path.relpath(OUT, ROOT))
