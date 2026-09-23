#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""T5c 参考图几何量取探针（判据资产，随工程提交）。

用途：把《皇室战争》(A) 官方参考截图的待量区域 **裁出 + 放大 N 倍 + 叠像素网格/标尺**，
导出到 <项目根>/.ai-tmp/screenshots/，让「读数」可复核、可重跑。

配套的数值辅助：
  edge   —— 在一条扫描线/扫描带里找亮度或色差跳变，输出**候选边界的像素坐标**（读数来源）
  profile—— 打印某一列（垂直扫描）/某一行（水平扫描）的统计，用于确认色带范围

⚠️ 本脚本**只读图、只出裁剪图与数值**，不写任何游戏代码。

用法（在 <项目根> 下）：
  python tools/probes/measure-hud.py list
  python tools/probes/measure-hud.py crop  --img 02_对局HUD_1242x2208.jpg --box 0,0,1242,200 --scale 3 --out 02_top.png
  python tools/probes/measure-hud.py edge  --img 02_对局HUD_1242x2208.jpg --axis y --pos 621 --from 0 --to 400
  python tools/probes/measure-hud.py edge  --img 02_对局HUD_1242x2208.jpg --axis x --pos 90  --from 0 --to 1242
  python tools/probes/measure-hud.py profile --img 12_主菜单_750x1334.png --axis y --pos 375 --from 1180 --to 1334
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
REF_DIR = os.path.join(ROOT, "策划", "参考图")
OUT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")

# 参考图清单（文件名 -> 原始分辨率），与 策划/参考图/清单.md 一致
SPECS = {
    "01_启动页_Logo_1320x2868.jpg": (1320, 2868),
    "02_对局HUD_1242x2208.jpg": (1242, 2208),
    "03_对局_1320x2868.jpg": (1320, 2868),
    "04_对局_1320x2868.jpg": (1320, 2868),
    "05_对局_1320x2868.jpg": (1320, 2868),
    "06_对局_1320x2868.jpg": (1320, 2868),
    "07_卡组编辑_1242x2208.jpg": (1242, 2208),
    "08_对局_1242x2208.jpg": (1242, 2208),
    "09_对局_1242x2208.jpg": (1242, 2208),
    "10_对局_1242x2208.jpg": (1242, 2208),
    "11_卡牌进化详情_1242x2208.jpg": (1242, 2208),
    "12_主菜单_750x1334.png": (750, 1334),
    "13_部落页_750x1334.png": (750, 1334),
    "14_商店_720x1280.png": (720, 1280),
    "15_任务菜单_432x768.png": (432, 768),
    "16_免费宝箱_576x1024.png": (576, 1024),
    "17_加载页_640x955.png": (640, 955),
}


def ref_path(name):
    p = os.path.join(REF_DIR, name)
    if not os.path.exists(p):
        sys.exit("no such reference image: %s" % p)
    return p


def load(name):
    return Image.open(ref_path(name)).convert("RGB")


def parse_box(s):
    v = [int(t) for t in s.replace(" ", "").split(",")]
    if len(v) != 4:
        sys.exit("--box needs x0,y0,x1,y1")
    return tuple(v)


# --------------------------------------------------------------------------- crop
def cmd_crop(a):
    im = load(a.img)
    x0, y0, x1, y1 = parse_box(a.box)
    x0 = max(0, x0); y0 = max(0, y0)
    x1 = min(im.width, x1); y1 = min(im.height, y1)
    c = im.crop((x0, y0, x1, y1))
    s = a.scale
    c = c.resize((c.width * s, c.height * s), Image.NEAREST)
    d = ImageDraw.Draw(c)
    step = a.step
    if step > 0:
        # 竖线：原图坐标 x 为 step 的整数倍
        gx = ((x0 // step) + 1) * step
        while gx < x1:
            px = (gx - x0) * s
            d.line([(px, 0), (px, c.height)], fill=(255, 0, 255), width=1)
            d.text((px + 2, 2), str(gx), fill=(255, 0, 255))
            gx += step
        gy = ((y0 // step) + 1) * step
        while gy < y1:
            py = (gy - y0) * s
            d.line([(0, py), (c.width, py)], fill=(0, 255, 255), width=1)
            d.text((2, py + 2), str(gy), fill=(0, 255, 255))
            gy += step
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR)
    out = os.path.join(OUT_DIR, a.out)
    c.save(out)
    print("WROTE %s  (crop=%s scale=%d step=%d) src=%s %dx%d"
          % (out, (x0, y0, x1, y1), s, step, a.img, im.width, im.height))


# --------------------------------------------------------------------------- edge
def cmd_edge(a):
    """沿 axis 方向扫描，报告相邻采样点的差异峰（= 候选边界）。
    axis=y: 固定 x=pos，自上而下扫 y in [from,to)；axis=x: 固定 y=pos，自左向右扫 x。"""
    im = load(a.img)
    px = im.load()
    vals = []
    if a.axis == "y":
        rng = range(a.frm, a.to)
        prev = None
        for y in rng:
            c = px[a.pos, y]
            lum = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
            if prev is not None:
                vals.append((y, abs(lum - prev), c))
            prev = lum
    else:
        rng = range(a.frm, a.to)
        prev = None
        for x in rng:
            c = px[x, a.pos]
            lum = 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
            if prev is not None:
                vals.append((x, abs(lum - prev), c))
            prev = lum
    vals.sort(key=lambda t: -t[1])
    print("# %s  axis=%s pos=%d range=[%d,%d)  -> top %d jumps"
          % (a.img, a.axis, a.pos, a.frm, a.to, a.top))
    for coord, delta, c in vals[:a.top]:
        print("  %s=%-6d dLum=%-7.1f rgb=%s" % (a.axis, coord, delta, c))


# --------------------------------------------------------------------------- profile
def cmd_profile(a):
    """打印扫描带上每隔 band 像素的区间均色，用于确认色带范围。"""
    im = load(a.img)
    px = im.load()
    band = a.band
    print("# %s axis=%s pos=%d range=[%d,%d) band=%d"
          % (a.img, a.axis, a.pos, a.frm, a.to, band))
    s = a.frm
    while s < a.to:
        e = min(s + band, a.to)
        rs = gs = bs = n = 0
        for i in range(s, e):
            c = px[a.pos, i] if a.axis == "y" else px[i, a.pos]
            rs += c[0]; gs += c[1]; bs += c[2]; n += 1
        if n:
            print("  %s[%4d,%4d)  rgb=(%3d,%3d,%3d)" % (a.axis, s, e, rs // n, gs // n, bs // n))
        s = e


# --------------------------------------------------------------------------- hrange
def cmd_hrange(a):
    """在给定行 y 上，找出「与背景色差 > thr」的连续横向区间（用于量某条横带的左右边界）。"""
    im = load(a.img)
    px = im.load()
    bg = [int(t) for t in a.bg.split(",")] if a.bg else None
    if bg is None:
        # 默认取两端各 5 px 的均色为背景
        n = 5
        rs = sum(px[x, a.pos][0] for x in range(a.frm, a.frm + n)) / n
        gs = sum(px[x, a.pos][1] for x in range(a.frm, a.frm + n)) / n
        bs = sum(px[x, a.pos][2] for x in range(a.frm, a.frm + n)) / n
        bg = [rs, gs, bs]
    runs = []
    cur = None
    for x in range(a.frm, a.to):
        c = px[x, a.pos]
        d = max(abs(c[0] - bg[0]), abs(c[1] - bg[1]), abs(c[2] - bg[2]))
        if d > a.thr:
            if cur is None:
                cur = [x, x]
            else:
                cur[1] = x
        else:
            if cur is not None and cur[1] - cur[0] >= a.minrun:
                runs.append(tuple(cur))
            cur = None
    if cur is not None and cur[1] - cur[0] >= a.minrun:
        runs.append(tuple(cur))
    print("# %s hrange y=%d x=[%d,%d) thr=%d bg=%s minrun=%d"
          % (a.img, a.pos, a.frm, a.to, a.thr, tuple(round(v) for v in bg), a.minrun))
    for r in runs:
        print("  run x=%d..%d  width=%d" % (r[0], r[1], r[1] - r[0] + 1))


def cmd_list(a):
    for name, (w, h) in SPECS.items():
        p = os.path.join(REF_DIR, name)
        mark = "ok " if os.path.exists(p) else "MISS"
        real = ""
        if os.path.exists(p):
            rw, rh = Image.open(p).size
            real = "%dx%d%s" % (rw, rh, "" if (rw, rh) == (w, h) else "  <-- MISMATCH")
        print("%s %-34s declared=%dx%d  %s" % (mark, name, w, h, real))


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("list"); p.set_defaults(fn=cmd_list)

    p = sub.add_parser("crop")
    p.add_argument("--img", required=True)
    p.add_argument("--box", required=True)
    p.add_argument("--scale", type=int, default=3)
    p.add_argument("--step", type=int, default=50)
    p.add_argument("--out", required=True)
    p.set_defaults(fn=cmd_crop)

    p = sub.add_parser("edge")
    p.add_argument("--img", required=True)
    p.add_argument("--axis", choices=["x", "y"], required=True)
    p.add_argument("--pos", type=int, required=True)
    p.add_argument("--from", dest="frm", type=int, required=True)
    p.add_argument("--to", type=int, required=True)
    p.add_argument("--top", type=int, default=12)
    p.set_defaults(fn=cmd_edge)

    p = sub.add_parser("profile")
    p.add_argument("--img", required=True)
    p.add_argument("--axis", choices=["x", "y"], required=True)
    p.add_argument("--pos", type=int, required=True)
    p.add_argument("--from", dest="frm", type=int, required=True)
    p.add_argument("--to", type=int, required=True)
    p.add_argument("--band", type=int, default=4)
    p.set_defaults(fn=cmd_profile)

    p = sub.add_parser("hrange")
    p.add_argument("--img", required=True)
    p.add_argument("--pos", type=int, required=True)
    p.add_argument("--from", dest="frm", type=int, required=True)
    p.add_argument("--to", type=int, required=True)
    p.add_argument("--thr", type=int, default=40)
    p.add_argument("--bg")
    p.add_argument("--minrun", type=int, default=3)
    p.set_defaults(fn=cmd_hrange)

    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
