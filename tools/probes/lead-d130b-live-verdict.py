#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130b 实机判据：在**本链截出来的实机图**上判 ⑥ 地图的三条 + 出一条河道放大图。

为什么必须单独写一份（⛔ 不复用离线复现的结论）：
  离线 `cr-d130b-render-recipe.py` 是"按源码常量在 Python 里重铺一遍"，
  它绿只能证明**常量自洽**，证不了"Unity 真的这么铺了"（材质/排序/相机/透明都可能让它不一样）。
  本脚本吃的是 `capture_game_view --source screen` 的真图。

口径（⛔ 不给任何一条手挑区间，全部现场量）：
  水带行区间  <- `water_rows()`（与 `cr-d130-deliver.py` 同一函数、同一横向窗口 470..640）
  桥列        <- `bridge_centers()`（河带内"非水面"的列段）
  格宽        <- 桥心距 / 11 格（GameConst.BridgeCxBTile − BridgeCxATile）—— 现场反算，不写死 60
  水带 2 格高 <- GameConst.RiverTopTile..RiverBottomTile

判据（每条都能失败）：
  J1a【水上方不应有木板】上沿以上 1 格带的 WOOD 率 ≤ 0.05（**主判据**）。
      出处：原版训练营 `策划/基线图/03_对局_1320x2868.jpg` 水上沿以上实测 = 草 + 约 0.2 格窄泥岸 + 白浪线，
      木色 ≈ 0（出图 `d130x/LEAD-ref03-upperbank-2x.png`）。撤销前本实现该处 WOOD=**0.675** ⇒ FAIL。
  J1b【两岸都应是草】上/下 1 格的「非草率」之差 ≤ 0.35（容差留给原版那条窄泥岸）。
  J1c 参考：mud+stone 上/下占比（**不判绿**，仅存档）。
      ⚠️ 口径变更登记：旧版用「mud+stone 差 ≥ +0.30」。它建立在"那条带是泥岸"的前提上；
      实机证明那条带是「泥 + 成片竖木板」（`lead-d130b-banksrc.py`：整段 WOOD=0.547），
      而分类器把棕色木归进 WOOD ⇒ mud+stone 口径量不出它。故主判据换成 J1a。
  ⛔ 所有 J1 的行带都必须排除桥列：桥自身在两岸的投影不同，混进来会把结论带偏。
  J2【木板墙已消】在我方半场"格 y 11.1..14.9 × 格 x 4.5..13.0"（= D130 第一轮把贴图集残留
      铺成"木板墙"的那块落位）内，木色占比应 ≈ 0（阈值 ≤ 0.02）。
  J3【桥板归位】两条桥心应落在 3.5 / 14.5 格（±0.25 格 = ±15px，用现场反算的格宽）。

用法：python lead-d130b-live-verdict.py [图路径] [放大图输出路径]
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
DEFAULT_IMG = os.path.join(ROOT, '.ai-tmp', 'screenshots', 'D130b-live-natural.png')

WATER_WIN = (470, 640)          # 两条车道之间的开阔水面（与 cr-d130-deliver.py 同窗口）
RIVER_TILES = 2.0               # GameConst.RiverTopTile=15 .. RiverBottomTile=17
BRIDGE_CX_A, BRIDGE_CX_B = 3.5, 14.5   # GameConst.BridgeCxATile / BridgeCxBTile
FIELD_TILES_W = 18.0            # GameConst.ArenaTilesW

# 阈值（写死在这里，⛔ 不随图变；改阈值 = 改判据，必须重跑并登记）
TH_J1_WOOD = 0.05   # 水上沿以上 1 格允许的木色率（原版实测 0；撤销前实测 0.675）
TH_J1_NG = 0.35     # 上/下 1 格「非草率」之差的容差（两岸都应是草；允许原版那条 ~0.2 格窄泥岸）
TH_J2 = 0.02
TH_J3_TILE = 0.25


def is_water(p):
    r, g, b = p[:3]
    return b > g and b > r + 40 and b > 110


def cls(p):
    """与 tools/probes/lead-ref-symdist.py 同口径，另补 WOOD（木板）一类。
    实测：河岸带 = 沙色泥(MUD) + 灰石(STONE)；木栏/桥板 = 饱和棕(WOOD)。"""
    r, g, b = p[:3]
    if g > 95 and g > r + 18 and g > b + 40:
        return 'GRASS'
    if b > 110 and b > r + 35 and g > 110:
        return 'WATER'
    if r > 105 and r > g + 24 and g > b + 8 and b < 130:
        return 'WOOD'
    if r > 120 and r > g + 12 and g >= b and r - b > 25:
        return 'MUD'
    if abs(r - g) < 14 and abs(g - b) < 16 and 70 < r < 215:
        return 'STONE'
    return 'other'


def water_rows(im, x0, x1, ymax_frac=0.72, minrun=15):
    px = im.load()
    _, h = im.size
    ylim = int(h * ymax_frac)
    runs, cur = [], None
    for y in range(ylim):
        n = 0
        tot = 0
        for x in range(x0, x1, 4):
            tot += 1
            if is_water(px[x, y]):
                n += 1
        if n / float(max(1, tot)) > 0.5:
            if cur is None:
                cur = y
        else:
            if cur is not None:
                runs.append((cur, y - 1))
                cur = None
    if cur is not None:
        runs.append((cur, ylim - 1))
    runs = [r for r in runs if r[1] - r[0] + 1 >= minrun]
    return max(runs, key=lambda r: r[1] - r[0]) if runs else None


def bridge_centers(im, wr, minw=55, maxw=130):
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


def band_stats(im, y0, y1, x0, x1, skip_cols):
    """行带 [y0,y1] × 列带 [x0,x1] 内各类占比；skip_cols = 要排除的 (a,b) 闭区间列表。"""
    px = im.load()
    _, h = im.size
    y0 = max(0, y0)
    y1 = min(h - 1, y1)
    x0 = max(0, x0)
    x1 = min(im.size[0] - 1, x1)
    cnt = {}
    tot = 0
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if any(a <= x <= b for a, b in skip_cols):
                continue
            k = cls(px[x, y])
            cnt[k] = cnt.get(k, 0) + 1
            tot += 1
    if tot == 0:
        return None, 0, {}
    return {k: v / float(tot) for k, v in cnt.items()}, tot, cnt


def fmt(d):
    if not d:
        return 'n/a'
    keys = ['MUD', 'STONE', 'WOOD', 'GRASS', 'WATER', 'other']
    return ' '.join('%s=%.3f' % (k, d.get(k, 0.0)) for k in keys if d.get(k, 0.0) > 0 or k in ('MUD', 'STONE', 'WOOD'))


def main():
    img_path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IMG
    zoom_out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(
        ROOT, '.ai-tmp', 'test', 'd130x', 'D130b-live-riverzone-x2.png')
    im = Image.open(img_path).convert('RGB')
    W, H = im.size
    out = []
    out.append('# file=%s  %dx%d' % (img_path, W, H))

    wr = water_rows(im, WATER_WIN[0], WATER_WIN[1])
    if wr is None:
        out.append('J1/J2/J3 UNKNOWN: water band not found in window %s -> 判不了（不算绿）' % (WATER_WIN,))
        print('\n'.join(out))
        with open(os.path.join(ROOT, '.ai-tmp', 'test', 'D130b-live-verdict.out.txt'), 'w', encoding='utf-8') as f:
            f.write('\n'.join(out) + '\n')
        return 1
    wh = wr[1] - wr[0] + 1
    pxt = wh / RIVER_TILES                      # 纵向 px/格（现场反算：水带恒 2 格）
    out.append('WATER band rows %d..%d  height=%d px  => px/tile(y) = %.3f' % (wr[0], wr[1], wh, pxt))

    bcs = bridge_centers(im, wr)
    out.append('BRIDGES in water band = %s' % [('c%.1f w%d' % t) for t in bcs])
    if len(bcs) >= 2:
        sp = bcs[-1][0] - bcs[0][0]
        pxx = sp / (BRIDGE_CX_B - BRIDGE_CX_A)
        out.append('BRIDGE spacing = %.1f px = %.1f 格 => px/tile(x) = %.3f' % (sp, BRIDGE_CX_B - BRIDGE_CX_A, pxx))
    else:
        pxx = W / FIELD_TILES_W                 # 兜底：18 格铺满画宽
        out.append('BRIDGES 数不足 2（%d）-> px/tile(x) 用兜底 = 画宽/18 = %.3f' % (len(bcs), pxx))

    # 排除桥列（外扩 10px）
    skip = []
    for c, w in bcs:
        skip.append((int(c - w / 2.0 - 10), int(c + w / 2.0 + 10)))
    out.append('SKIP cols (bridges +-10px) = %s' % skip)

    one_tile = int(round(pxt))
    up = band_stats(im, wr[0] - one_tile, wr[0] - 1, 0, W - 1, skip)
    dn = band_stats(im, wr[1] + 1, wr[1] + one_tile, 0, W - 1, skip)
    out.append('')
    out.append('J1 [水上方不应有木板] 水带上沿以上 %d px（=1 格）: %s' % (one_tile, fmt(up[0])))
    out.append('J1                    水带下沿以下 %d px（=1 格）: %s' % (one_tile, fmt(dn[0])))
    if up[0] is None or dn[0] is None:
        out.append('J1  UNKNOWN: band stats empty => 不算绿')
    else:
        up_wood = up[0].get('WOOD', 0.0)
        up_ms = up[0].get('MUD', 0) + up[0].get('STONE', 0)
        dn_ms = dn[0].get('MUD', 0) + dn[0].get('STONE', 0)
        up_ng = 1.0 - up[0].get('GRASS', 0.0)
        dn_ng = 1.0 - dn[0].get('GRASS', 0.0)
        out.append('J1a 上格 WOOD=%.3f (阈值 <= %.2f) => %s   ← 主判据（原版水上沿以上无木板）'
                   % (up_wood, TH_J1_WOOD, 'PASS' if up_wood <= TH_J1_WOOD else 'FAIL'))
        out.append('J1b 非草率 上 %.3f / 下 %.3f，差 %+.3f (容差 %.2f) => %s   ← 两岸都应是草'
                   % (up_ng, dn_ng, up_ng - dn_ng, TH_J1_NG, 'PASS' if abs(up_ng - dn_ng) <= TH_J1_NG else 'FAIL'))
        out.append('J1c 参考读数（不判绿，仅存档）：mud+stone 上 %.3f / 下 %.3f'
                   % (up_ms, dn_ms))
        # 负控：撤销前的那张图（源带含木）必须判红 —— 证明 J1a 能失败
        out.append('J1-negctl 撤销前同一位置的 WOOD=0.675（`D130b-R1-olddll-natural.png` 是旧 DLL、'
                   '`D130b-R1` 批次另有含 GroundRedBank 的图）=> 该情形 J1a 判 FAIL（能失败 ✓）')

    # J2 木板墙：格 y 11.1..14.9 × 格 x 4.5..13.0（第一轮"木板墙"的落位）
    y_hi = wr[1] + int(round((15.0 - 14.9) * pxt))
    y_lo = wr[1] + int(round((15.0 - 11.1) * pxt))
    x0 = int(round(4.5 * pxx))
    x1 = int(round(13.0 * pxx))
    st = band_stats(im, y_hi, y_lo, x0, x1, skip)
    wood = st[0].get('WOOD', 0.0) if st[0] else None
    out.append('')
    out.append('J2 [木板墙已消] rows %d..%d x %d..%d  (=格 y11.1..14.9 x4.5..13.0): %s'
               % (y_hi, y_lo, x0, x1, fmt(st[0])))
    if wood is not None:
        out.append('J2  wood=%.4f (阈值 <= %.2f) => %s' % (wood, TH_J2, 'PASS' if wood <= TH_J2 else 'FAIL'))
    else:
        out.append('J2  UNKNOWN: empty => 不算绿')
    # 正控：车道 A 列带（格 3.03..4.00）应当有木色（车道本身是板路）=> 证明 WOOD 分类器不瞎
    xa0, xa1 = int(round(3.03 * pxx)), int(round(4.00 * pxx))
    stA = band_stats(im, y_hi, y_lo, xa0, xa1, [])
    out.append('J2-posctl (车道A列带 x %d..%d) 应有木: %s' % (xa0, xa1, fmt(stA[0])))

    # J3 桥归位
    out.append('')
    if len(bcs) >= 2:
        exp = [BRIDGE_CX_A * pxx, BRIDGE_CX_B * pxx]
        got = [bcs[0][0], bcs[-1][0]]
        ok = all(abs(got[i] - exp[i]) <= TH_J3_TILE * pxx for i in range(2))
        out.append('J3 [桥板归位] 期望桥心 x = %.1f / %.1f (格 %.1f/%.1f × %.2f px/格)；实测 %.1f / %.1f；'
                   '偏差 %.1f / %.1f px (阈值 ±%.1f) => %s'
                   % (exp[0], exp[1], BRIDGE_CX_A, BRIDGE_CX_B, pxx, got[0], got[1],
                      got[0] - exp[0], got[1] - exp[1], TH_J3_TILE * pxx, 'PASS' if ok else 'FAIL'))
    else:
        out.append('J3 UNKNOWN: 桥数不足 => 不算绿')

    # 河道放大图（3x）
    pad = int(1.2 * pxt)
    cy0 = max(0, wr[0] - pad)
    cy1 = min(H - 1, wr[1] + pad)
    crop = im.crop((0, cy0, W, cy1))
    crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
    d = ImageDraw.Draw(crop)
    d.line([0, (wr[0] - cy0) * 2, crop.width, (wr[0] - cy0) * 2], fill=(255, 0, 0), width=2)
    d.line([0, (wr[1] - cy0) * 2, crop.width, (wr[1] - cy0) * 2], fill=(255, 0, 0), width=2)
    for c, w in bcs:
        d.line([c * 2, 0, c * 2, crop.height], fill=(0, 255, 255), width=2)
    crop.save(zoom_out)
    out.append('')
    out.append('wrote %s %s' % (zoom_out, crop.size))

    txt = '\n'.join(out) + '\n'
    print(txt)
    with open(os.path.join(ROOT, '.ai-tmp', 'test', 'D130b-live-verdict.out.txt'), 'w', encoding='utf-8') as f:
        f.write(txt)
    return 0


if __name__ == '__main__':
    sys.exit(main())
