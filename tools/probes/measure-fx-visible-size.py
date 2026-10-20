#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""量法脚本：从实机截图量「特效可见直径（格）」并拼联络图。

口径
----
  · 屏幕上 **1 格 = 屏宽 / 18 px**（`client/Assets/Scripts/View/ArenaView.cs`：
    `pxPerTileX = 屏宽 / ArenaTilesW(18)`；1080 宽 ⇒ 60 px/格）。
  · 可见直径 = 截图中该特效**亮色像素连通块**的外接框宽度 ÷ 60 px/格
    （量「看得见的像素范围」，不是画布尺寸 ⇒ 与原版素材的观感直径同一口径）。

用法
----
  C:\\Python312\\python.exe tools/probes/measure-fx-visible-size.py ^
      --flight .ai-tmp/screenshots/cr-arc2/fireball-arc-top.png ^
      --land   .ai-tmp/screenshots/cr-arc2/fireball-arc-land.png ^
      --out    .ai-tmp/screenshots/fireball-flight-landing-contact.png
"""
import argparse
import os
import sys

try:
    from PIL import Image, ImageDraw
except Exception as e:                                   # noqa: BLE001
    print('PIL 不可用: %s' % e)
    sys.exit(2)

ARENA_TILES = 18.0
ARENA_TILES_H = 32.0
GROUND_COMPRESS_Y = 0.75
FRAME_BASIS_W = 1080.0
FRAME_BASIS_H = 1920.0
GROUND_BOTTOM_ROW_PX = 1580.0


def world_to_screen(wx, wy, w, h):
    """世界坐标 → 屏幕像素（口径 = `View/ArenaView.cs` 的 `SetupCamera`：非等比正交投影）。"""
    half_w = ARENA_TILES * 0.5
    aspect = float(w) / float(h)
    half_h = half_w / (aspect * GROUND_COMPRESS_Y)
    bottom_margin = (FRAME_BASIS_H - GROUND_BOTTOM_ROW_PX) * (float(w) / FRAME_BASIS_W)
    frac = (float(h) - bottom_margin) / float(h)
    cy = frac * (2.0 * half_h) - half_h - ARENA_TILES_H * 0.5
    sx = (wx + half_w) / (2.0 * half_w) * w
    sy = (cy + half_h - wy) / (2.0 * half_h) * h
    return sx, sy


def is_fire(r, g, b):
    """火球系（橙黄）的亮像素判定。"""
    return r >= 205 and 95 <= g <= 225 and b <= 135 and (r - b) >= 80


def blobs(img, top_crop=260, bottom_crop=420, focus=None, win=170):
    """返回 [(bbox, area)]，按面积降序。`focus` = (screen_x, screen_y) ⇒ 只看该点附近的块。"""
    w, h = img.size
    px = img.load()
    if focus:
        x_lo = max(0, int(focus[0] - win))
        x_hi = min(w - 1, int(focus[0] + win))
        y_lo = max(top_crop, int(focus[1] - win))
        y_hi = min(h - bottom_crop - 1, int(focus[1] + win))
    else:
        x_lo, x_hi, y_lo, y_hi = 0, w - 1, top_crop, h - bottom_crop - 1
    seen = [[False] * w for _ in range(h)]
    out = []
    for y in range(y_lo, y_hi + 1):
        for x in range(x_lo, x_hi + 1):
            if seen[y][x]:
                continue
            r, g, b = px[x, y][:3]
            if not is_fire(r, g, b):
                continue
            stack = [(x, y)]
            seen[y][x] = True
            minx = maxx = x
            miny = maxy = y
            area = 0
            while stack:
                cx, cy = stack.pop()
                area += 1
                minx = min(minx, cx)
                maxx = max(maxx, cx)
                miny = min(miny, cy)
                maxy = max(maxy, cy)
                for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                    if nx < 0 or ny < 0 or nx >= w or ny >= h or seen[ny][nx]:
                        continue
                    rr, gg, bb = px[nx, ny][:3]
                    if is_fire(rr, gg, bb):
                        seen[ny][nx] = True
                        stack.append((nx, ny))
            out.append(((minx, miny, maxx, maxy), area))
    out.sort(key=lambda t: -t[1])
    return out


def measure(path, label, min_area=40, world=None, focus_px=None, win=150):
    """`focus_px` = 截图像素坐标（已由 Unity 屏幕坐标翻转 y）；取**离该点最近**的亮色块。"""
    img = Image.open(path).convert('RGBA')
    ppt = img.width / ARENA_TILES
    focus = None
    if world:
        focus = world_to_screen(world[0], world[1], img.width, img.height)
    if focus_px:
        focus = focus_px
    bs = [b for b in blobs(img, focus=focus, win=win) if b[1] >= min_area]
    if not bs:
        return None
    if focus:
        bs.sort(key=lambda b: ((b[0][0] + b[0][2]) * 0.5 - focus[0]) ** 2 + ((b[0][1] + b[0][3]) * 0.5 - focus[1]) ** 2)
    (x0, y0, x1, y1), area = bs[0]
    wpx = x1 - x0 + 1
    hpx = y1 - y0 + 1
    return dict(label=label, path=path, size=img.size, px_per_tile=round(ppt, 2),
                focus=focus, bbox=(x0, y0, x1, y1), w_px=wpx, h_px=hpx, area=area,
                diameter_tiles=round(wpx / ppt, 3), height_tiles=round(hpx / ppt, 3),
                n_blobs=len(bs))



def contact(f1, f2, out_path, m1, m2):
    a = Image.open(f1).convert('RGB')
    b = Image.open(f2).convert('RGB')
    w = a.width + b.width
    h = max(a.height, b.height) + 110
    sheet = Image.new('RGB', (w, h), (18, 18, 20))
    sheet.paste(a, (0, 0))
    sheet.paste(b, (a.width, 0))
    d = ImageDraw.Draw(sheet)
    d.rectangle([0, 0, a.width - 1, a.height - 1], outline=(90, 90, 90))
    d.rectangle([a.width, 0, w - 1, b.height - 1], outline=(90, 90, 90))
    if m1:
        d.rectangle([m1['bbox'][0], m1['bbox'][1], m1['bbox'][2], m1['bbox'][3]],
                    outline=(255, 80, 80), width=3)
    if m2:
        d.rectangle([m2['bbox'][0] + a.width, m2['bbox'][1],
                     m2['bbox'][2] + a.width, m2['bbox'][3]],
                    outline=(255, 80, 80), width=3)
    y0 = max(a.height, b.height)
    d.text((12, y0 + 10), 'A flight (apex)  %s' % (m1['label'] if m1 else '-'), fill=(255, 235, 120))
    if m1:
        d.text((12, y0 + 32), 'visible width = %d px = %.2f tiles  (1 tile = %.1f px)'
               % (m1['w_px'], m1['diameter_tiles'], m1['px_per_tile']), fill=(235, 235, 235))
    d.text((a.width + 12, y0 + 10), 'B landing blast  %s' % (m2['label'] if m2 else '-'), fill=(255, 235, 120))
    if m2:
        d.text((a.width + 12, y0 + 32), 'visible width = %d px = %.2f tiles  (1 tile = %.1f px)'
               % (m2['w_px'], m2['diameter_tiles'], m2['px_per_tile']), fill=(235, 235, 235))
    d.text((12, y0 + 60), 'red box = measured bright-pixel bbox; px per tile = screen width / 18',
           fill=(170, 170, 170))
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    sheet.save(out_path)
    return out_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--flight', required=True)
    ap.add_argument('--land', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--flight-world', help='弹体世界坐标 x,y（截图那一刻）')
    ap.add_argument('--land-world', help='落地爆世界坐标 x,y')
    ap.add_argument('--flight-px', help='弹体在截图里的像素坐标 x,y（Unity 屏幕坐标已翻 y）')
    ap.add_argument('--land-px', help='落地爆在截图里的像素坐标 x,y')
    args = ap.parse_args()
    fw = tuple(float(v) for v in args.flight_world.split(',')) if args.flight_world else None
    lw = tuple(float(v) for v in args.land_world.split(',')) if args.land_world else None
    fp = tuple(float(v) for v in args.flight_px.split(',')) if args.flight_px else None
    lp = tuple(float(v) for v in args.land_px.split(',')) if args.land_px else None
    m1 = measure(args.flight, os.path.basename(args.flight), world=fw, focus_px=fp)
    m2 = measure(args.land, os.path.basename(args.land), world=lw, focus_px=lp)
    out = contact(args.flight, args.land, args.out, m1, m2)
    for m in (m1, m2):
        if not m:
            print('no bright blob found')
            continue
        print('%-26s %s focus=%s  visible %d x %d px = %.2f x %.2f tiles  area=%d  blobs=%d'
              % (m['label'], m['size'],
                 '(%.0f,%.0f)' % m['focus'] if m['focus'] else '-',
                 m['w_px'], m['h_px'],
                 m['diameter_tiles'], m['height_tiles'], m['area'], m['n_blobs']))
    print('contact sheet -> %s' % out)
    return 0



if __name__ == '__main__':
    sys.exit(main())
