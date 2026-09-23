# -*- coding: utf-8 -*-
"""CR-T1h(离线量取，不改代码)：原版对局图里「塔体宽 / 格宽」。
口径：
  * 塔体轮廓宽 = 该 crop 内"非草地"像素（草地 = 绿通道明显占优）的水平 extent
  * 垛口宽 = 白色像素 extent（只作交叉参考）
  * 格宽（原版）用三个锚点分别给：① 顶部两座公主塔中心距（同深度、相隔 11 格）
    ② 桥宽（2 格）③ 桥心距（11 格）
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from PIL import Image

ROOT = r"C:\Work\Server\f-v2\clover-project-cr"
img = np.asarray(Image.open(os.path.join(ROOT, r"策划\参考图\03_对局_1320x2868.jpg")).convert("RGB")).astype(int)
H, W = img.shape[:2]
print('原版图 %dx%d' % (W, H))

r, g, b = img[:, :, 0], img[:, :, 1], img[:, :, 2]
grass = (g > r + 12) & (g > b + 25)                     # 草地/背景绿
white = (r > 205) & (g > 195) & (b > 185)


def sil(box, tag):
    x0, y0, x1, y1 = box
    sub = img[y0:y1, x0:x1]
    gr = grass[y0:y1, x0:x1]
    wh = white[y0:y1, x0:x1]
    rr, gg, bb = sub[:, :, 0], sub[:, :, 1], sub[:, :, 2]
    # 塔体 = 非草、非"很暗的投影"（投影亮度低且接近中性）
    lum = (rr + gg + bb) / 3.0
    shadow = (lum < 95) & (abs(rr - gg) < 25) & (abs(gg - bb) < 25)
    body = (~gr) & (~shadow)
    ys, xs = np.nonzero(body)
    wb = None
    wy, wx = np.nonzero(wh)
    if len(wx):
        wb = (int(wx.min()), int(wx.max()), int(wx.max() - wx.min() + 1))
    if len(xs) == 0:
        print('  %-22s 无轮廓' % tag); return None
    print('  %-22s 轮廓 x=%d..%d 宽=%d  垛口 %s' % (tag, xs.min() + x0, xs.max() + x0,
          xs.max() - xs.min() + 1, wb))
    return (int(xs.min() + x0), int(xs.max() + x0), int(xs.max() - xs.min() + 1))


print('\n--- 顶部（RED 侧）三座塔 ---')
tl = sil((10, 470, 330, 800), 'red princess-L')
tr = sil((960, 460, 1290, 800), 'red princess-R')
rk = sil((430, 60, 830, 640), 'red king')
print('--- 底部（BLUE 侧）---')
bk = sil((430, 1850, 880, 2520), 'blue king')
br = sil((1000, 1790, 1320, 2200), 'blue princess-R')

if tl and tr:
    d = ((tr[0] + tr[1]) / 2.0) - ((tl[0] + tl[1]) / 2.0)
    print('\n顶部两公主塔中心距 = %.1f px（= 11 格，BridgeCxATile 3.5 ↔ 14.5）⇒ %.1f px/格' % (d, d / 11.0))
    for nm, t in (('red princess', tl), ('red king', rk)):
        if t:
            print('   %s 轮廓宽 %d px ⇒ %.2f 格（按上值）' % (nm, t[2], t[2] / (d / 11.0)))

print('\n--- 桥（原版 2 格宽）---')
for tag2, bx in (('左桥', (40, 1300, 200, 1450)), ('右桥', (1080, 1300, 1240, 1450))):
    sub = img[bx[1]:bx[3], bx[0]:bx[2]]
    rr, gg, bb = sub[:, :, 0], sub[:, :, 1], sub[:, :, 2]
    wood = (rr > 120) & (rr - bb > 40) & (rr - gg > 15)      # 桥的木板色
    ys, xs = np.nonzero(wood)
    if len(xs):
        print('  %s 木板色 x=%d..%d 宽=%d ⇒ 1 格 = %.1f px' % (tag2, xs.min() + bx[0], xs.max() + bx[0],
              xs.max() - xs.min() + 1, (xs.max() - xs.min() + 1) / 2.0))
        print('      桥心 x=%.1f' % ((xs.min() + xs.max()) / 2.0 + bx[0]))
    else:
        print('  %s 没找到木板色' % tag2)
