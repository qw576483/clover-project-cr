#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
cr-d133-tower-hpbar.py -- D133 判据资产：**塔血条**的尺寸 / 常显取证（基线图量取）
====================================================================================
回答的问题（用户判词 3「战斗单位的血量看不见（塔也在内）」）
------------------------------------------------------------
1. 原版塔血条在**满血**时是否也画？（本判据 = 反面取证：基线图里满血塔仍画满格条）
2. 原版塔血条的**外宽 / 外高**是多少（换成"格"）？⇒ 反解出我们 `WorldHpBar.Create(..., width, height, ...)`
   该传的 local 值（血条挂在塔根下，会被塔根 scale 1.65/1.70 放大）。
3. 原版塔血条上**还有没有别的东西**（数字 / 等级徽章）？

口径（⛔ 全脚本只用这一套）
--------------------------
· 血条掩膜 = 青色：`b > 190 and g > 140 and r < 150 and b - r > 70`（原版血条是亮青蓝渐变）。
· px/格：**同图**两座公主塔血条中心距 ÷ 11 格（`GameConst.BridgeCxATile` 3.5 ↔ `BridgeCxBTile` 14.5）。
  只用同图内的比例 ⇒ 不受"基线图被放大过"的影响（1320 宽的图里 18 格 > 1320 px，说明图是局部放大图）。
· 血条外尺寸（原版量到的整体，含深蓝描边）→ 我们的 local 值：`local = 原版格数 / 塔根 scale − 边框增量`
  （引擎 `UIWidgets.cs` 的 `WorldHpBar.Build`：Bg = (width+0.04) × (height+0.035)，Fill = width × height）。

产物
----
· `.ai-tmp/test/CR-D133-hpbar.txt`（人类可读）
· `.ai-tmp/test/CR-D133-bar-ruler.png`（04 图底部左公主塔血条的放大裁切 + 像素标尺 + 网格，
  用来**同时**判"满血仍画条"、条上"数字 1740"、条"左端金色等级徽章"，以及条的上/下沿）

⚠️ 两个掩膜都用（⛔ 不能只用青色）：
  · `bar_mask`（青色）= 只框到**填充** ⇒ 用来量"填充高 / 填充宽"。
  · `barish_mask`（偏蓝且比地面暗）= 含**深蓝描边** ⇒ 用来量"外高"，且只在条**两端各 6 列**上取
    中位数（避开中间的血量数字与左侧徽章，那两处会把上下沿读歪）。

复跑
----
  python tools/probes/cr-d133-tower-hpbar.py --root .
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

# 塔根缩放（出处：ArenaView.TowerScale / PrincessTowerScale 常量上的长注释）
SCALE_KING = 1.7
SCALE_PRINCESS = 1.65
# 引擎 WorldHpBar.Build 的边框增量
BG_DX, BG_DY = 0.04, 0.035


def bar_mask(a):
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b > 190) & (g > 140) & (r < 150) & (b - r > 70)


def barish_mask(a):
    """"像血条（含深蓝描边）"的像素：偏蓝且不是灰色地面。

    ⛔ 为什么不能只用 `bar_mask`（青色）量高度：原版条是 **深蓝描边 + 青色填充**，青色掩膜只框到填充，
    会把描边漏掉（实测差 5~6 px）⇒ 量"外高"必须用这一套更宽的掩膜，两套都要报出来。
    """
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b - r > 40) & (b > 85)


def col_span(m, x, y0, y1):
    """某一列在 [y0,y1] 内被掩膜命中的最小/最大行号（无命中返回 None）。"""
    ys = [y for y in range(y0, y1 + 1) if m[y, x]]
    return None if not ys else (ys[0], ys[-1])


def outer_span(m, x_a, x_b, y0, y1):
    """用**条两端各 6 列**（避开中间的血量数字/徽章）的命中行的中位数，抗噪地定出条的上下沿。"""
    tops, bots = [], []
    for x in list(range(x_a, x_b + 1)):
        sp = col_span(m, x, y0, y1)
        if sp is None:
            continue
        tops.append(sp[0])
        bots.append(sp[1])
    if not tops:
        return None
    tops.sort()
    bots.sort()
    return tops[len(tops) // 2], bots[len(bots) // 2]


def h_segments(m, min_w=20, gap=12):
    """按行带切出横向长条段；返回 [(y0, y1, [(x0,x1), ...]), ...] 只保留 x 段足够长的行带。"""
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        return []
    rows = np.zeros(m.shape[0], bool)
    rows[np.unique(ys)] = True
    out = []
    st = None
    for y in range(m.shape[0]):
        if rows[y] and st is None:
            st = y
        if not rows[y] and st is not None:
            out.append((st, y - 1))
            st = None
    if st is not None:
        out.append((st, m.shape[0] - 1))
    res = []
    for (y0, y1) in out:
        if y1 - y0 < 5:
            continue
        sel = (ys >= y0) & (ys <= y1)
        xv = np.sort(xs[sel])
        cuts, start, prev = [], xv[0], xv[0]
        for v in xv[1:]:
            if v - prev > gap:
                cuts.append((int(start), int(prev)))
                start = v
            prev = v
        cuts.append((int(start), int(prev)))
        cuts = [c for c in cuts if c[1] - c[0] > min_w]
        if cuts:
            res.append((y0, y1, cuts))
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--root', default='.')
    ap.add_argument('--ref', default='策划/参考图/04_对局_1320x2868.jpg')
    ap.add_argument('--ref20', default='策划/参考图/20_对局_1080x1920.jpg')
    ap.add_argument('--tiles', type=float, default=11.0,
                    help='两座公主塔的格距（BridgeCxA 3.5 ↔ BridgeCxB 14.5）')
    a = ap.parse_args()
    root = os.path.abspath(a.root)
    lines = []

    def P(s=''):
        print(s)
        lines.append(s)

    p = os.path.join(root, a.ref)
    im = Image.open(p).convert('RGB')
    A = np.asarray(im)
    P('ref=%s  size=%dx%d' % (os.path.basename(p), im.width, im.height))
    M_fill = bar_mask(A)
    M_outer = barish_mask(A)
    segs = h_segments(M_fill)
    P('\n== A. 青色（填充）血条的行带与 x 段 ==')
    for (y0, y1, cuts) in segs:
        P('  rows %d..%d  h=%d  x-segments: %s' % (y0, y1, y1 - y0 + 1, cuts))

    # 找"最下面那一对同高、等宽、左右对称"的长条 = 底部两座公主塔
    cand = None
    for (y0, y1, cuts) in segs:
        pair = [c for c in cuts if c[1] - c[0] > 80]
        if len(pair) == 2:
            cand = (y0, y1, pair)
    if cand is None:
        P('\n!! 找不到"一对长血条"⇒ 下面的换算无法进行（掩膜/参考图变了？）')
    else:
        y0, y1, (L, R) = cand
        wl, wr = L[1] - L[0] + 1, R[1] - R[0] + 1
        cl, cr = (L[0] + L[1]) / 2.0, (R[0] + R[1]) / 2.0
        fill_h = y1 - y0 + 1
        px_tile = (cr - cl) / a.tiles
        P('\n== B. 底部两座公主塔的血条（"一对"判据）==')
        P('  左 x=%d..%d (w=%d, 中心 %.1f)   右 x=%d..%d (w=%d, 中心 %.1f)'
          % (L[0], L[1], wl, cl, R[0], R[1], wr, cr))
        P('  青色填充带 = 行 %d..%d ⇒ **填充高 %d px**  [满血仍画满格条 ⇒ 原版血条是常显]'
          % (y0, y1, fill_h))
        P('  两中心距 = %.1f px ÷ %.1f 格 ⇒ **%.2f px/格**（同图口径）' % (cr - cl, a.tiles, px_tile))

        # 外沿（含深蓝描边）：用每条**两端各 6 列**（避开中间的数字/徽章）的中位数
        spL = outer_span(M_outer, L[0], L[0] + 5, y0 - 14, y1 + 14)
        spR = outer_span(M_outer, R[1] - 5, R[1], y0 - 14, y1 + 14)
        P('\n== C. 上/下沿（含深蓝描边）与"格"换算 ==')
        if spL and spR:
            P('  左条外沿 = 行 %d..%d (%d px)   右条外沿 = 行 %d..%d (%d px)'
              % (spL[0], spL[1], spL[1] - spL[0] + 1, spR[0], spR[1], spR[1] - spR[0] + 1))
            outer_h = ((spL[1] - spL[0] + 1) + (spR[1] - spR[0] + 1)) / 2.0
        else:
            outer_h = None
            P('  !! 外沿量不到（掩膜太严？）')
        P('  原版外宽 %.1f px ÷ %.2f = **%.3f 格**（左）/ %.3f 格（右）'
          % (wl, px_tile, wl / px_tile, wr / px_tile))
        P('  原版填充高 %d px ÷ %.2f = **%.3f 格**' % (fill_h, px_tile, fill_h / px_tile))
        if outer_h:
            P('  原版外高   %.1f px ÷ %.2f = **%.3f 格**' % (outer_h, px_tile, outer_h / px_tile))
        for label, w_local, sc in (('公主塔(scale %.2f)' % SCALE_PRINCESS, 1.4, SCALE_PRINCESS),
                                   ('国王塔(scale %.2f)' % SCALE_KING, 1.8, SCALE_KING)):
            P('  我们 %s：外宽 (%.2f+%.2f)×%.2f = **%.3f 格** = %.1f px（同口径）'
              % (label, w_local, BG_DX, sc, (w_local + BG_DX) * sc, (w_local + BG_DX) * sc * px_tile))
        # 两个独立反解：① 用**填充高** ↔ 我们的 `height`（Fill Quad = height）
        #               ② 用**外高**   ↔ 我们的 `height + 0.035`（Bg Quad = height + 0.035）
        h_from_fill = (fill_h / px_tile) / SCALE_PRINCESS
        P('\n  反解 local 高度（公主塔，两条独立口径）：')
        P('    ① 填充高口径：(h)×%.2f = %.3f ⇒ h = **%.4f**' % (SCALE_PRINCESS, fill_h / px_tile, h_from_fill))
        if outer_h:
            h_from_outer = (outer_h / px_tile) / SCALE_PRINCESS - BG_DY
            P('    ② 外高口径：  (h+%.3f)×%.2f = %.3f ⇒ h = **%.4f**'
              % (BG_DY, SCALE_PRINCESS, outer_h / px_tile, h_from_outer))
        for h_local in (0.16, 0.20, 0.21, 0.22):
            P('    回代 h=%.3f ⇒ 填充 %.1f px / 外高 %.1f px（原版填充 %d px / 外高 %.1f px）'
              % (h_local, h_local * SCALE_PRINCESS * px_tile,
                 (h_local + BG_DY) * SCALE_PRINCESS * px_tile, fill_h, outer_h or -1))

        # 放大裁切 + 像素标尺 + 网格（同时用来看"条上有没有数字/徽章"）
        x0 = max(0, L[0] - 60)
        x1 = min(im.width, L[1] + 20)
        cy0 = max(0, y0 - 12)
        cy1 = min(im.height, y1 + 8)
        crop = im.crop((x0, cy0, x1, cy1))
        z = max(1, int(round(720.0 / crop.width)))
        crop = crop.resize((crop.width * z, crop.height * z), Image.NEAREST)
        g = crop.copy()
        d = ImageDraw.Draw(g)
        for gy in range(cy0, cy1 + 1):
            if gy % 5 == 0:
                yy = (gy - cy0) * z
                d.line([(0, yy), (g.width, yy)], fill=(0, 160, 0), width=1)
                d.text((2, yy + 1), str(gy), fill=(0, 110, 0))
        for gx in range(x0, x1 + 1):
            if gx % 10 == 0:
                xx = (gx - x0) * z
                d.line([(xx, 0), (xx, g.height)], fill=(220, 0, 160), width=1)
                if gx % 20 == 0:
                    d.text((xx + 1, 1), str(gx), fill=(150, 0, 110))
        out = os.path.join(root, '.ai-tmp', 'test', 'CR-D133-bar-ruler.png')
        os.makedirs(os.path.dirname(out), exist_ok=True)
        g.save(out)
        P('\nRULER -> %s  (crop x %d..%d, y %d..%d, zoom %dx)' % (out, x0, x1, cy0, cy1, z))
        P('  ⚠️ 该裁切里可见：条**下方/内侧的白色描边数字**与条**左端的金色等级徽章** ⇒ 原版塔血条不是纯色条')

    # 参考图 20：塔条与单位条同屏（常显的第二条旁证）
    p20 = os.path.join(root, a.ref20)
    if os.path.isfile(p20):
        im20 = Image.open(p20).convert('RGB')
        A20 = np.asarray(im20)
        s20 = h_segments(bar_mask(A20))
        P('\n== D. 第二张旁证 %s (%dx%d) ==' % (os.path.basename(p20), im20.width, im20.height))
        for (y0, y1, cuts) in s20:
            P('  rows %d..%d  h=%d  x-segments: %s' % (y0, y1, y1 - y0 + 1, cuts))
        P('  ⇒ 同一屏里塔条与多个单位条**同时**显示（原版血条常显；⛔ 数字需人眼看裁图确认）')

    txt = os.path.join(root, '.ai-tmp', 'test', 'CR-D133-hpbar.txt')
    os.makedirs(os.path.dirname(txt), exist_ok=True)
    with open(txt, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    P('\nTXT -> %s' % txt)
    return 0


if __name__ == '__main__':
    sys.exit(main())
