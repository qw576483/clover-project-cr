#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130 交付件：同机位并排图 + 河区对照 + 前/后对照 + 接缝机械判据。

口径（重要）：**两张图用同一个函数量**（车道中心列、草地把 bbox、水带行区间），
⛔ 不给任何一张手挑行区间 —— 那是两种量法，会量出"看起来对"的假结论。

输入
  .ai-tmp/screenshots/D130-arena-live-cam.png     本实现（实机、无 HUD，1080x1920）
  策划/参考图/20_对局_1080x1920.jpg                原版同机位（1080x1920，同分辨率 ⇒ 不缩放）
  C:/Users/.../3acbd58b49dd680ac193c69be1cae3a8.png  用户附图的**旧实机**（判词"接缝"的现场）
输出
  .ai-tmp/screenshots/D130-arena-sbs.png           左=原版参考 / 右=本实现，标出河带与车道
  .ai-tmp/screenshots/D130-arena-riverzone.png     两图同一裁剪框的河区
  .ai-tmp/screenshots/D130-arena-before-after.png  左=用户附图旧实机 / 右=本实现
  .ai-tmp/screenshots/D130-arena-seam.txt          接缝判据（横贯整幅的暗行）三图的原始读数

复跑：C:/Python312/python tools/probes/cr-d130-deliver.py
"""
import os
import sys

from PIL import Image, ImageDraw

for _s in ('stdout', 'stderr'):
    try:
        getattr(sys, _s).reconfigure(encoding='utf-8')
    except Exception:
        pass

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SHOTS = os.path.join(ROOT, '.ai-tmp', 'screenshots')
OURS = os.path.join(SHOTS, 'D130-arena-live-cam.png')
REF = os.path.join(ROOT, '策划', '参考图', '20_对局_1080x1920.jpg')
BEFORE = r'C:\Users\xuanyuan\xwechat_files\wxid_uz7qznm9iv7y22_6de7\temp\RWTemp\2026-09\3acbd58b49dd680ac193c69be1cae3a8.png'
OUT_SBS = os.path.join(SHOTS, 'D130-arena-sbs.png')
OUT_RIV = os.path.join(SHOTS, 'D130-arena-riverzone.png')
OUT_BA = os.path.join(SHOTS, 'D130-arena-before-after.png')
OUT_TXT = os.path.join(SHOTS, 'D130-arena-seam.txt')

WATER_TAG = (0, 140, 160)   # 只有用于"标出河带"的近似水色；判定用的是下面的 is_water()
# ⛔ 两张图必须用**同一个横向窗口**量水带（用不同窗口 = 两种量法 ⇒ 结论不可比）。
#    窗口取两条车道之间的开阔水面：本实现车道约在 x 349/871、参考图约在 230/880 ⇒ 470..640 对两者都是开阔水。
WATER_WIN = (470, 640)


def is_water(p):
    r, g, b = p[:3]
    return b > g and b > r + 40 and b > 110


def grass_bbox(im):
    px = im.load()
    w, h = im.size
    xs, ys = [], []
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            r, g, b = px[x, y][:3]
            if g > r + 12 and g > b + 30 and g > 90:
                xs.append(x)
                ys.append(y)
    if not xs:
        return None
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1)


def water_rows(im, x0, x1, ymax_frac=0.72, minrun=15):
    """水带 = 在给定横向窗口内"该行过半数像元是水色"的最长连续行段。
    ⛔ 只在 y < ymax_frac*H 内找：下面的手牌/圣水条也有蓝色像素（ref20 第一次就被它骗到过，
    报出 y1873..1879 这个"河"）。并要求连续 >= minrun 行，避免一条蓝边被当成河。"""
    px = im.load()
    _, h = im.size
    ylim = int(h * ymax_frac)
    runs, cur = [], None
    for y in range(ylim):
        n = 0
        for x in range(x0, x1, 4):
            if is_water(px[x, y]):
                n += 1
        if n / float(max(1, (x1 - x0) // 4)) > 0.5:
            if cur is None:
                cur = y
        else:
            if cur is not None:
                runs.append((cur, y - 1))
                cur = None
    if cur is not None:
        runs.append((cur, ylim - 1))
    runs = [r for r in runs if r[1] - r[0] + 1 >= minrun]
    if not runs:
        return None
    return max(runs, key=lambda r: r[1] - r[0])


def bridge_centers(im, wr, minw=55, maxw=130):
    """在河带 wr 内找"非水面"的列段 = 跨河的桥板；返回中心列与宽度。
    桥心间距 = BridgeCxBTile − BridgeCxATile = 11 格（GameConst）⇒ 可反算 px/格(x)。"""
    px = im.load()
    w, _ = im.size
    ymid = list(range(wr[0] + 6, wr[1] - 5, 2))
    cnt = [0] * w
    for y in ymid:
        for x in range(w):
            if not is_water(px[x, y]):
                cnt[x] += 1
    thr = max(2, len(ymid) // 3)
    segs, s = [], None
    for x in range(w):
        if cnt[x] > thr and s is None:
            s = x
        elif cnt[x] <= thr and s is not None:
            segs.append((s, x - 1))
            s = None
    if s is not None:
        segs.append((s, w - 1))
    segs = [t for t in segs if minw <= t[1] - t[0] + 1 <= maxw]
    return [((a + b) / 2.0, b - a + 1) for a, b in segs]


def lane_centers_band(im, y0, y1, minw=18, maxw=90):
    """在行带 y0..y1 内找"土路列"（严格阈值，避开灰白台子）：返回宽度在 minw..maxw 的列段中心。"""
    px = im.load()
    w, _ = im.size
    cnt = [0] * w
    rows = list(range(y0, y1, 2))
    for y in rows:
        for x in range(w):
            r, g, b = px[x, y][:3]
            if r > g + 15 and r > b + 40:
                cnt[x] += 1
    thr = max(2, len(rows) // 8)
    segs, s = [], None
    for x in range(w):
        if cnt[x] > thr and s is None:
            s = x
        elif cnt[x] <= thr and s is not None:
            segs.append((s, x - 1))
            s = None
    if s is not None:
        segs.append((s, w - 1))
    segs = [t for t in segs if minw <= t[1] - t[0] + 1 <= maxw]
    return [((a + b) / 2.0, b - a + 1) for a, b in segs]


def dark_rows(im, x0, x1, y0, y1, thresh=8.0, maxrun=6):
    """横贯整幅的暗行（接缝判据，与 .ai-tmp/test/cr-d130-seam.py 同口径）。"""
    g = im.convert('L')
    px = g.load()
    prof = []
    for y in range(y0, y1):
        s = 0
        n = 0
        for x in range(x0, x1, 2):
            s += px[x, y]
            n += 1
        prof.append(s / float(n))
    hits = []
    for i in range(6, len(prof) - 6):
        nb = prof[i - 6:i] + prof[i + 1:i + 7]
        if not nb:
            continue
        m = sum(nb) / len(nb)
        if prof[i] < m - thresh:
            hits.append((y0 + i, round(prof[i], 1), round(m, 1), round(m - prof[i], 1)))
    return hits


def ruler(d, x0, y0, x1, y1, cols):
    d.rectangle([x0 - 1, y0 - 1, x1, y1], outline=(255, 0, 0), width=2)
    for c in cols:
        d.line([c, y0, c, y1], fill=(255, 255, 0), width=1)


def main():
    ours = Image.open(OURS).convert('RGB')
    ref = Image.open(REF).convert('RGB')
    lines = []

    # ── 本实现：水带（河恒 2 格高 ⇒ 水带高度/2 = 纵向 px/格）+ 河带内两条桥心（桥心距 11 格） ──
    ob = grass_bbox(ours)
    lines.append('OURS %s grass_bbox=%s' % (ours.size, ob))
    wr_o = water_rows(ours, WATER_WIN[0], WATER_WIN[1])
    lines.append('OURS water_rows=%s  (window x %s, 两图同窗口)' % (wr_o, WATER_WIN))
    bridges_o = []
    if wr_o:
        lines.append('OURS px/tile(y) = water_h/2 = %.4f  (GameConst.RiverTopTile..RiverBottomTile = 2 格)'
                     % ((wr_o[1] - wr_o[0] + 1) / 2.0))
        bridges_o = bridge_centers(ours, wr_o)
        lines.append('OURS bridges in the water band = %s' % [('c%.1f w%d' % t) for t in bridges_o])
        if len(bridges_o) >= 2:
            sp = bridges_o[-1][0] - bridges_o[0][0]
            lines.append('OURS bridge spacing = %.1f px = BridgeCxBTile−BridgeCxATile = 11 格 => %.4f px/tile(x)'
                         % (sp, sp / 11.0))

    # ── 原版参考：同法同窗口（只取水带与桥；不去猜它的车道/台子：它和本素材不是同一套美术） ──
    rb = grass_bbox(ref)
    lines.append('REF  %s grass_bbox=%s' % (ref.size, rb))
    wr_r = water_rows(ref, WATER_WIN[0], WATER_WIN[1])
    lines.append('REF  water_rows=%s  height=%s px' % (wr_r, (wr_r[1] - wr_r[0] + 1) if wr_r else 'n/a'))
    if wr_r:
        br_r = bridge_centers(ref, wr_r)
        lines.append('REF  bridges in the water band = %s' % [('c%.1f w%d' % t) for t in br_r])
        if len(br_r) >= 2:
            lines.append('REF  bridge spacing = %.1f px => %.4f px/tile(x)' % (br_r[-1][0] - br_r[0][0], (br_r[-1][0] - br_r[0][0]) / 11.0))

    # ── 接缝判据：三图同口径 ──
    for tag, im in (('OURS(live-cam)', ours), ('REF20', ref)):
        bb = grass_bbox(im)
        if bb is None:
            continue
        hits = dark_rows(im, bb[0] + 12, bb[2] - 12, bb[1] + 3, bb[3] - 3)
        hits.sort(key=lambda t: -t[3])
        lines.append('%s dark_rows=%d top=%s' % (tag, len(hits), hits[:6]))
    if os.path.exists(BEFORE):
        b = Image.open(BEFORE).convert('RGB')
        bb = grass_bbox(b)
        lines.append('BEFORE(user attached) %s grass_bbox=%s' % (b.size, bb))
        if bb:
            hits = dark_rows(b, bb[0] + 12, bb[2] - 12, bb[1] + 3, bb[3] - 3)
            hits.sort(key=lambda t: -t[3])
            lines.append('BEFORE dark_rows=%d top=%s' % (len(hits), hits[:6]))

    # ── 并排图（原版 | 本实现，同为 1080x1920 ⇒ 不缩放） ──
    gap, pad = 24, 16
    canvas = Image.new('RGB', (ref.width + ours.width + gap + pad * 2, max(ref.height, ours.height) + pad * 2 + 28), (28, 28, 32))
    canvas.paste(ref, (pad, pad + 28))
    canvas.paste(ours, (pad + ref.width + gap, pad + 28))
    d = ImageDraw.Draw(canvas)
    d.text((pad, pad + 8), 'ORIGINAL  plan/参考图/20_对局_1080x1920.jpg', fill=(230, 230, 230))
    d.text((pad + ref.width + gap, pad + 8), 'OURS  D130-arena-live-cam.png (no HUD, camera)', fill=(230, 230, 230))
    # 河带标记（两张各标一次，坐标 = 各图自己量出来的行区间）
    for (xo, wr) in ((pad, wr_r), (pad + ref.width + gap, wr_o)):
        if wr:
            d.rectangle([xo, pad + 28 + wr[0], xo + ref.width - 1, pad + 28 + wr[1]], outline=(0, 255, 255), width=3)
    # 车道竖线：只标本实现（用本实现河带里量出来的两条桥心 = 车道中心）
    if len(bridges_o) >= 2:
        xo = pad + ref.width + gap
        for c, _w in bridges_o:
            d.line([xo + c, pad + 28, xo + c, pad + 28 + ours.height], fill=(255, 0, 255), width=2)
    canvas.save(OUT_SBS)
    lines.append('wrote %s %s' % (OUT_SBS, canvas.size))

    # ── 河区同框对照：以本实现的水带为中心，两图取同一高度的框 ──
    if wr_o:
        cy_o = (wr_o[0] + wr_o[1]) / 2.0
        cy_r = (wr_r[0] + wr_r[1]) / 2.0 if wr_r else cy_o
        hh = 170
        co = ours.crop((0, int(cy_o - hh), ours.width, int(cy_o + hh)))
        cr = ref.crop((0, int(cy_r - hh), ref.width, int(cy_r + hh)))
        cz = Image.new('RGB', (co.width, co.height + cr.height + 8 + 28), (28, 28, 32))
        cz.paste(cr, (0, 28))
        cz.paste(co, (0, 28 + cr.height + 8))
        dz = ImageDraw.Draw(cz)
        dz.text((6, 8), 'ORIGINAL river zone (ref20)', fill=(230, 230, 230))
        dz.text((6, 28 + cr.height + 12), 'OURS river zone', fill=(230, 230, 230))
        cz.save(OUT_RIV)
        lines.append('wrote %s %s' % (OUT_RIV, cz.size))

    # ── 前/后对照（用户附图旧实机 | 本实现） ──
    if os.path.exists(BEFORE):
        b = Image.open(BEFORE).convert('RGB')
        scale = ours.height / float(b.height)
        bw = int(b.width * scale)
        b2 = b.resize((bw, ours.height), Image.LANCZOS)
        cz = Image.new('RGB', (b2.width + ours.width + gap + pad * 2, ours.height + pad * 2 + 28), (28, 28, 32))
        cz.paste(b2, (pad, pad + 28))
        cz.paste(ours, (pad + b2.width + gap, pad + 28))
        dz = ImageDraw.Draw(cz)
        dz.text((pad, pad + 8), 'BEFORE  user-attached old build (scaled to same height)', fill=(255, 180, 180))
        dz.text((pad + b2.width + gap, pad + 8), 'AFTER  D130-arena-live-cam.png', fill=(180, 255, 180))
        cz.save(OUT_BA)
        lines.append('wrote %s %s' % (OUT_BA, cz.size))

    with open(OUT_TXT, 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    main()
