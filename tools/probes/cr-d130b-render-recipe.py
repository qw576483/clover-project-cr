#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130b 离线复核：按 **ArenaView.cs 当前落地配方**（六段 + D130b 三处叠层 + 河面/桥）离线拼出
18x32 的竞技场，进 Play 之前先目视 + 定量确认：
  ① 场地正中不再有"木板墙"（贴图集残留）
  ② 河岸带（泥带 + 灰石）只在**水的上沿以上**（敌方侧）；水**下沿以下**是纯草
  ③ 两份覆盖带用的是"干净草地 + 两条车道" ⇒ 车道从公主台贯通到河沿
  ④ 无黑带（透明缝）

配方与常量名一一对应 ArenaView.cs：
  BLUE ①f022 py[536,748]→格[7.5,17.0]  ②py[748,996]→格[2.33,7.5]  ③py[0,55]→格[0,2.33]
  RED  ①py[1323,1600]→格[17.0,24.5]    ②py[1083,1323]→格[24.5,29.43]  ③py[0,55]→格[29.43,32] flipY
  D130b 覆盖带 f022 py[686,748] 复制两份 → 格[15.0−2×2.7778, 15.0]
  D130b 敌方河岸带 f022 py[575,612] → 格[16.75,18.41] flipY
  河面 f006 py[806,853] px[207.6,1027.4] → 格[15,17]
  桥面 f022 px[223,298] py[604,668] → 两车道各一次，格 y[14.57,17.43]

复跑：C:/Python312/python tools/probes/cr-d130b-render-recipe.py
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
AD = os.path.join(ROOT, 'client', 'Assets', 'Resources', 'Sprites', 'Arenas', 'arena_training_out')
OUT = os.path.join(ROOT, '.ai-tmp', 'test', 'd130x')
PXT = 45.545          # GroundPxPerTileX
FL = 99.0             # GroundFieldLeftPx
FR = FL + 18 * PXT    # GroundFieldRightPx
PPT = 60.0            # 出图 px/格
W, H = int(18 * PPT), int(32 * PPT)
TILES_H = 32
BLUE_ROWS_PER_TILE = (748.0 - 536.0) / (17.0 - 7.5)


def crop(img, px0, py0, px1, py1):
    return img.crop((int(round(px0)), int(round(py0)), int(round(px1)), int(round(py1))))


def main():
    os.makedirs(OUT, exist_ok=True)
    f22 = Image.open(os.path.join(AD, 'frame_022.png')).convert('RGBA')
    f06 = Image.open(os.path.join(AD, 'frame_006.png')).convert('RGBA')
    c = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    def blit(im, tytop, tybot, txleft, txright, flip=False):
        if flip:
            im = im.transpose(Image.FLIP_TOP_BOTTOM)
        w = max(1, int(round((txright - txleft) * PPT)))
        h = max(1, int(round((tytop - tybot) * PPT)))
        im = im.resize((w, h), Image.LANCZOS)
        c.alpha_composite(im, (int(round(txleft * PPT)), int(round((TILES_H - tytop) * PPT))))

    # ── orderBlueHalf (+1) ──
    blit(crop(f22, FL, 536, FR, 748), 17.0, 7.5, 0, 18)
    blit(crop(f22, FL, 748, FR, 996), 7.5, 2.33, 0, 18)
    blit(crop(f22, FL, 0, FR, 55), 2.33, 0.0, 0, 18)
    # ── orderRedHalf (+2) ──
    blit(crop(f22, FL, 1323, FR, 1600), 24.5, 17.0, 0, 18)
    blit(crop(f22, FL, 1083, FR, 1323), 29.43, 24.5, 0, 18)
    blit(crop(f22, FL, 0, FR, 55), 32.0, 29.43, 0, 18, flip=True)
    # ── orderGroundOverlay (+3)：D130b 覆盖带（两份）+ 敌方河岸带 ──
    span = (748.0 - 686.0) / BLUE_ROWS_PER_TILE          # 2.7778 格
    blit(crop(f22, FL, 686, FR, 748), 15.0, 15.0 - span, 0, 18)
    blit(crop(f22, FL, 686, FR, 748), 15.0 - span, 15.0 - 2 * span, 0, 18)
    blit(crop(f22, FL, 575, FR, 612), 18.41, 16.75, 0, 18, flip=True)
    # ── orderWater (+4) ──
    blit(crop(f06, 207.6, 806, 207.6 + 18 * PXT, 853), 17.0, 15.0, 0, 18)
    # ── orderBridge (+5) ──
    half = (298 - 223) / PXT * 0.5
    for cx in (3.5, 14.5):
        blit(crop(f22, 223, 604, 298, 668), 17.43, 14.57, cx - half, cx + half)

    p = os.path.join(OUT, 'D130b-recipe.png')
    c.convert('RGB').save(p)
    print('wrote', p, c.size)
    c.crop((0, int((TILES_H - 19.5) * PPT), W, int((TILES_H - 12.0) * PPT))).convert('RGB').save(
        os.path.join(OUT, 'D130b-recipe-riverzone.png'))

    # ── 定量自检 ──
    a = np.array(c)
    rgb, al = a[:, :, :3].astype(np.int16), a[:, :, 3]

    def band(ty0, ty1):
        """格 y [ty0,ty1] → 图像行区间（y 向上）"""
        r0 = int((TILES_H - ty1) * PPT)
        r1 = int((TILES_H - ty0) * PPT)
        return rgb[r0:r1], al[r0:r1]

    def tag(arr):
        r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
        lum = (r + g + b) / 3.0
        out = np.full(arr.shape[:2], '?', dtype='<U1')
        out[(g > r + 6) & (g > b + 6)] = 'G'
        out[(b > r + 20) & (b > g + 5)] = 'B'
        out[(r > g + 8) & (r > b + 25)] = 'M'
        gy = (np.abs(r - g) < 14) & (np.abs(g - b) < 14) & (lum > 40) & (lum < 215)
        out[gy & (out == '?')] = 'S'
        return out

    print('\n# 自检 1：透明（= 黑带）像元 —— 全图 / 场地内（格 x 0..18）')
    print(f'   全图 alpha<16 占比 = {(al < 16).mean():.5f}')

    def stats(label, ty0, ty1, tx0=0.0, tx1=18.0):
        r, al2 = band(ty0, ty1)
        c0, c1 = int(tx0 * PPT), int(tx1 * PPT)
        sub = tag(r[:, c0:c1])
        n = sub.size
        out = {k: float((sub == k).sum()) / n for k in 'GMBS?'}
        a_frac = float((al2[:, c0:c1] < 16).mean())
        print(f'   {label:<34s} G={out["G"]:.3f} M={out["M"]:.3f} B={out["B"]:.3f} '
              f'S={out["S"]:.3f} ?={out["?"]:.3f}  透明={a_frac:.4f}')
        return out

    print('\n# 自检 2：水带上沿以上 1 格 / 下沿以下 1 格（原版判据：上沿有泥带+石台，下沿纯草）')
    stats('水上方 1.0 格 (y 17.0..18.0)', 17.0, 18.0)
    stats('水上方 1.0 格 (y 18.0..19.0)', 18.0, 19.0)
    stats('水下方 1.0 格 (y 14.0..15.0)', 14.0, 15.0)
    stats('水下方 1.0 格 (y 13.0..14.0)', 13.0, 14.0)

    print('\n# 自检 3：我方近场（格 y 7.5..15.0）内的"木色"该只剩两条车道（各 ≈0.96 格宽 ⇒ 占比 ≈0.09）')
    stats('BLUE 近场 全宽 y7.5..15.0', 7.5, 15.0)
    for lbl, x0, x1 in (('  车道A 列带 x3.03..4.00', 3.03, 4.00), ('  车道B 列带 x13.40..14.36', 13.40, 14.36),
                        ('  车道之间 x4.5..13.0（应几乎无木色）', 4.5, 13.0)):
        stats(lbl, 7.5, 15.0, x0, x1)

    print('\n# 自检 4：河岸带（M+S）在水上方 vs 水下方 的"1 格带内"占比差（原版：上方显著 > 下方）')
    up = stats('水上方 0..0.7 格 (y 17.0..17.7)', 17.0, 17.7)
    dn = stats('水下方 0..0.7 格 (y 14.3..15.0)', 14.3, 15.0)
    print(f'   差 (上−下) M+S = {(up["M"] + up["S"]) - (dn["M"] + dn["S"]):+.3f}')


if __name__ == '__main__':
    main()
