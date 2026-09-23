#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
特效素材 `effects_out` 全 612 帧辨认（**判据资产**，⛔ 不许删）。

用法（项目根下）:
    python tools/probes/fx-index.py

数据源 = 原版解包 PNG：
    <根>/原版资源/cr-assets-png/assets/sc/effects_out/effects_sprite_000.png .. _611.png

产出:
    .ai-tmp/screenshots/F1-fx-index-page1..N.png   带帧号的联络图（每页 <=100 格，10 列）
    .ai-tmp/test/F1-fx-index.tsv                   逐帧量化表（尺寸 / bbox / 覆盖率 / 均色 / 主色分类）

为什么是"抽样看 + 只落地要用的几帧"：612 帧全量做逐帧 1:1 图既慢又没人读得完；
联络图给的是"哪几帧长得像火球 / 爆炸 / 闪光"的**候选**，最终由 read_file 看图确认。
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC_DIR = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "effects_out")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")
TEST_DIR = os.path.join(ROOT, ".ai-tmp", "test")

TOTAL = 612
COLS = 10
ROWS = 10
PER_PAGE = COLS * ROWS           # 100
CELL = 150                       # 每格四边形宽（高按等比缩放到 CELL 内）
LABEL_H = 26


def classify(a):
    """在 alpha>8 的不透明区内做粗色分类，给辨认一个"是什么颜色"的抓手。"""
    alpha = a[:, :, 3]
    op = alpha > 8
    r, g, b = a[:, :, 0].astype(np.int16), a[:, :, 1].astype(np.int16), a[:, :, 2].astype(np.int16)
    return {
        "red": op & (r > 130) & (r > g + 60) & (r > b + 60),
        "orange": op & (r > 170) & (g > 80) & (g < r - 30) & (b < 120) & (r - b > 80),
        "yellow": op & (r > 180) & (g > 160) & (b < 140) & (abs(r - g) < 60),
        "white": op & (r > 200) & (g > 200) & (b > 200),
        "green": op & (g > r + 15) & (g > b + 20),
        "blue": op & (b > r + 25) & (b > 120),
        "purple": op & (r > 90) & (b > r + 20) & (b > g + 40),
        "grey": op & (abs(r - g) < 16) & (abs(g - b) < 16),
        "dark": op & (r < 60) & (g < 60) & (b < 60),
    }


def bbox_of(mask):
    if not mask.any():
        return None
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def fmt_bbox(bb):
    if bb is None:
        return "-"
    return "x[%d..%d] y[%d..%d]" % (bb[0], bb[2], bb[1], bb[3])


def main():
    os.makedirs(SHOT_DIR, exist_ok=True)
    os.makedirs(TEST_DIR, exist_ok=True)

    rows = []
    loaded = {}
    for i in range(TOTAL):
        p = os.path.join(SRC_DIR, "effects_sprite_%03d.png" % i)
        if not os.path.exists(p):
            print("ERROR: missing %s" % p)
            return 1
        im = Image.open(p)
        if im.mode != "RGBA":
            print("  note: f%03d mode=%s -> RGBA" % (i, im.mode))
            im = im.convert("RGBA")
        a = np.asarray(im).astype(np.int16)
        if a.ndim == 2:                      # 灰度 / 调色板退化
            a = np.dstack([a, a, a, np.full(a.shape, 255, dtype=np.int16)])
        elif a.shape[2] == 3:
            a = np.dstack([a, np.full(a.shape[:2], 255, dtype=np.int16)])
        op = a[:, :, 3] > 8
        bb = bbox_of(op)
        cov = op.mean() * 100.0
        mc = a[op][:, :3].mean(axis=0) if op.any() else np.zeros(3)
        cls = classify(a)
        counts = {k: int(v.sum()) for k, v in cls.items()}
        top = "-"
        if op.any():
            top = max(counts.items(), key=lambda kv: kv[1])[0]
        rows.append((i, im, bb, cov, mc, counts, top))
        loaded[i] = (im, bb)
        if i % 100 == 0:
            print("  read %d/%d" % (i, TOTAL))

    tsv = os.path.join(TEST_DIR, "F1-fx-index.tsv")
    with open(tsv, "w", encoding="utf-8") as f:
        f.write("frame\tsize\tbbox\tcoverage%\tmean_rgb\top_color"
                "\tred\torange\tyellow\twhite\tgreen\tblue\tpurple\tgrey\tdark\n")
        for (i, im, bb, cov, mc, counts, top) in rows:
            f.write("f%03d\t%dx%d\t%s\t%.2f\t%.0f,%.0f,%.0f\t%s\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\t%d\n" % (
                i, im.size[0], im.size[1], fmt_bbox(bb), cov, mc[0], mc[1], mc[2], top,
                counts["red"], counts["orange"], counts["yellow"], counts["white"],
                counts["green"], counts["blue"], counts["purple"], counts["grey"], counts["dark"]))
    print("tsv -> %s" % tsv)

    maxw = max(im.size[0] for (_, im, _, _, _, _, _) in rows)
    maxh = max(im.size[1] for (_, im, _, _, _, _, _) in rows)
    print("max frame size: %dx%d" % (maxw, maxh))

    # ── 联络图（**按内容 bbox 裁剪后放大**）──────────────────────────────────
    # 源画布全是 474x537 的大透明框，图元只占几十像素；照画布缩到 150px 宽 ⇒ 图元变成 3~5px 黑点，
    # **看不清就不能当判据**。所以先裁 bbox（留 4px 边），再放大到 CELL 内。
    def cell_image(im, bb):
        if bb is None:
            return Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        pad = 4
        x0, y0 = max(0, bb[0] - pad), max(0, bb[1] - pad)
        x1, y1 = min(im.size[0], bb[2] + pad + 1), min(im.size[1], bb[3] + pad + 1)
        c = im.crop((x0, y0, x1, y1))
        s = min(CELL / float(c.size[0]), CELL / float(c.size[1]))
        s = max(1.0, min(s, 8.0))
        if s > 1.0:
            c = c.resize((max(1, int(c.size[0] * s)), max(1, int(c.size[1] * s))), Image.NEAREST)
        return c

    pages = (TOTAL + PER_PAGE - 1) // PER_PAGE
    for pg in range(pages):
        chunk = list(range(pg * PER_PAGE, min(TOTAL, (pg + 1) * PER_PAGE)))
        cw, ch = CELL + 8, CELL + 8
        sheet = Image.new("RGBA", (COLS * cw, ROWS * (ch + LABEL_H)), (22, 22, 26, 255))
        d = ImageDraw.Draw(sheet)
        for k, i in enumerate(chunk):
            im, bb = loaded[i]
            cx = (k % COLS) * cw
            cy = (k // COLS) * (ch + LABEL_H)
            c = cell_image(im, bb)
            sheet.paste(c, (cx + 4 + (CELL - c.size[0]) // 2, cy + LABEL_H + 4 + (CELL - c.size[1]) // 2), c)
            d.text((cx + 3, cy + 3), "f%03d" % i, fill=(255, 235, 120, 255))
            d.text((cx + 3, cy + 14), "%s %s" % (rows[i][6], fmt_bbox(bb)), fill=(170, 215, 255, 255))
            d.rectangle([cx, cy, cx + cw - 1, cy + LABEL_H + ch - 1], outline=(80, 80, 92, 255), width=1)
        out = os.path.join(SHOT_DIR, "F1-fx-index-page%d.png" % (pg + 1))
        sheet.convert("RGB").save(out)
        print("page%d -> %s (%dx%d) frames %d..%d" % (pg + 1, out, sheet.size[0], sheet.size[1],
                                                      chunk[0], chunk[-1]))

    # ── 候选**逐帧序列**放大条带（第二次看图用：7 页缩略图只能定位"哪一段像什么"，
    #    确认"这一串是不是一条完整动画"必须把该段逐帧放大并排看）──────────────────
    # 每格裁到**该段所有帧 bbox 的并集**再等比放大 ⇒ 段内各帧位置关系保住（能看出"由小到大 / 旋转 / 位移"）。
    CAND = [
        ("A-hitflash-f048-056", 48, 56),
        ("B-hitflash-f155-162", 155, 162),
        ("C-hitflash-f318-330", 318, 330),
        ("D-hitflash-f500-510", 500, 510),
        ("E-arrow-red-f438-466", 438, 466),
        ("F-arrow-gold-f366-398", 366, 398),
        ("G-arrow-blue-f488-502", 488, 502),
        ("H-fireball-f392-404", 392, 404),
        ("I-flame-f400-420", 400, 420),
        ("J-bigfireball-f418-440", 418, 440),
        ("K-blastsmoke-f274-302", 274, 302),
        ("L-comet-f344-352", 344, 352),
    ]
    CAND_CELL = 170
    CAND_COLS = 14
    for name, a, b in CAND:
        idxs = list(range(a, b + 1))
        xs0, ys0, xs1, ys1 = 10 ** 9, 10 ** 9, -1, -1
        for i in idxs:
            bb = loaded[i][1]
            if bb is None:
                continue
            xs0, ys0 = min(xs0, bb[0]), min(ys0, bb[1])
            xs1, ys1 = max(xs1, bb[2]), max(ys1, bb[3])
        if xs1 < 0:
            continue
        ux0, uy0, ux1, uy1 = max(0, xs0 - 6), max(0, ys0 - 6), xs1 + 7, ys1 + 7
        uw, uh = ux1 - ux0, uy1 - uy0
        s = min(CAND_CELL / float(uw), CAND_CELL / float(uh))
        s = max(1.0, min(s, 6.0))
        cw, ch = int(uw * s) + 6, int(uh * s) + 6
        rws = (len(idxs) + CAND_COLS - 1) // CAND_COLS
        sheet = Image.new("RGBA", (CAND_COLS * cw, rws * (ch + LABEL_H) + LABEL_H), (22, 22, 26, 255))
        d = ImageDraw.Draw(sheet)
        d.text((6, 6), "%s : f%03d..f%03d  union x[%d..%d] y[%d..%d]  zoom=%.2fx"
               % (name, a, b, xs0, xs1, ys0, ys1, s), fill=(255, 240, 150, 255))
        for k, i in enumerate(idxs):
            cx = (k % CAND_COLS) * cw
            cy = LABEL_H + (k // CAND_COLS) * (ch + LABEL_H)
            c = loaded[i][0].crop((ux0, uy0, ux1, uy1))
            if s != 1.0:
                c = c.resize((int(c.size[0] * s), int(c.size[1] * s)), Image.NEAREST)
            sheet.paste(c, (cx + 3, cy + 20), c)
            d.text((cx + 3, cy + 3), "f%03d" % i, fill=(255, 235, 120, 255))
            d.rectangle([cx, cy, cx + cw - 1, cy + LABEL_H + ch - 1], outline=(80, 80, 92, 255), width=1)
        out = os.path.join(SHOT_DIR, "F1-fx-cand-%s.png" % name)
        sheet.convert("RGB").save(out)
        print("cand %s -> %s (%dx%d)" % (name, out, sheet.size[0], sheet.size[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
