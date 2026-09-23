#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
竞技场素材 `arena_training_out` 全 23 帧辨认（**判据资产**，⛔ 不许删）。

用法（项目根下）:
    python tools/probes/arena-frames.py

数据源 = **工程实际使用的那一份**：`client/Assets/Resources/Sprites/Arenas/arena_training_out/`
（同时逐帧 sha256 与 `<根>/原版资源/cr-assets-png/assets/sc/arena_training_out/` 的原版解包 PNG
 对账 —— 两者必须逐字节相同，否则下面的量取不能代表工程里那几张图）。

产出:
    .ai-tmp/screenshots/B1-arena-frames-contact.png   23 帧联络图（整幅画布 + 帧号 + 原始尺寸 + 非透明 bbox）
    .ai-tmp/screenshots/B1-arena-frames-page1..N.png  分页放大图（按内容 bbox 裁剪后放大，看清单个图元）
    .ai-tmp/screenshots/B1-arena-f022-full.png        f022 整帧 1:1（分辨"完整场地 + 两座桥"）
    .ai-tmp/screenshots/B1-arena-f022-riverband.png   f022 河道带 1:1（横向全宽）
    .ai-tmp/screenshots/B1-arena-f006-riverband.png   f006 河道带 1:1（横向全宽）—— 对照组：没有桥
    .ai-tmp/screenshots/B1-arena-f022-bridgeA.png     f022 左桥 2x
    .ai-tmp/screenshots/B1-arena-f022-bridgeB.png     f022 右桥 2x
    .ai-tmp/test/B1-arena-frames.tsv                  逐帧量化表（尺寸 / bbox / 覆盖率 / 均色 / 分类计数 / 河道带）

判据: 逐帧非透明包围盒 + 覆盖率 + 颜色分类（草 / 水 / 土黄路 / 木 / 石）+ 目视联络图。
"""
import hashlib
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

# ── 路径（相对项目根）──────────────────────────────────────────────────────────
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Arenas", "arena_training_out")
REF_DIR = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "arena_training_out")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")
TEST_DIR = os.path.join(ROOT, ".ai-tmp", "test")

CELL_W = 300          # 联络图每格宽
GRID_COLS = 5
LABEL_H = 34

PAGE_FRAMES = 6       # 分页放大图：每页帧数（3 列 × 2 行）
PAGE_COLS = 3
PAGE_CELL = 380       # 分页图每格内最长边（像素）


def classify(a):
    """返回各种"语义色"的像素掩码（全部在 alpha>8 的不透明区内）。"""
    alpha = a[:, :, 3]
    opaque = alpha > 8
    r, g, b = a[:, :, 0].astype(np.int16), a[:, :, 1].astype(np.int16), a[:, :, 2].astype(np.int16)
    grass = opaque & (g > r + 15) & (g > b + 30)                       # 草地绿
    water = opaque & (b > r + 20) & (b > 100) & (g > 100)              # 青色水带
    tan = opaque & (r > 150) & (r > b + 40) & (g > b + 20) & (g < r)   # 土黄通路 / 石
    wood = opaque & (r > g + 20) & (g > b + 5) & (r < 170) & (r > 90)  # 棕木
    grey = opaque & (abs(r - g) < 14) & (abs(g - b) < 14) & (r > 80)   # 灰石
    return opaque, {"grass": grass, "water": water, "tan": tan, "wood": wood, "grey": grey}


def bbox_of(mask):
    if not mask.any():
        return None
    ys, xs = np.where(mask)
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def fmt_bbox(bb):
    """⛔ 修过一个真 bug：旧版把 (xmin,ymin,xmax,ymax) 直接套进 "x[%d,%d] y[%d,%d]"，
    打印出 `x[0,695] y[1088,1423]` 这种读起来像"x 从 0 到 695"的错标签（实际 ymin=695）。
    量取表是要给人看的判据，标签错 ⇒ 结论错，所以这里显式命名。"""
    if bb is None:
        return "-"
    return "x[%d..%d] y[%d..%d]" % (bb[0], bb[2], bb[1], bb[3])


def river_band(opaque, cls, width):
    """自动找"河道带"：逐行统计，河 = 该行不透明像素里非草非土黄的（青水 + 灰石）占多数。"""
    rows = []
    h = opaque.shape[0]
    for y in range(h):
        n = int(opaque[y].sum())
        if n < width * 0.25:
            continue
        wet = int(cls["water"][y].sum()) + int(cls["grey"][y].sum())
        if wet > n * 0.5:
            rows.append(y)
    if not rows:
        return None
    return rows[0], rows[-1]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    os.makedirs(SHOT_DIR, exist_ok=True)
    os.makedirs(TEST_DIR, exist_ok=True)

    names = ["frame_%03d.png" % i for i in range(23)]
    missing = [n for n in names if not os.path.exists(os.path.join(SRC_DIR, n))]
    if missing:
        print("ERROR: missing in %s : %s" % (SRC_DIR, missing))
        return 1
    print("frames: %d  from %s" % (len(names), SRC_DIR))

    # ── 0) 与"原版解包 PNG"逐字节对账（量取必须描述工程里真正用的那几张图）───────
    refs = ["arena_training_sprite_%02d.png" % i for i in range(23)]
    same = 0
    for n, r in zip(names, refs):
        a = sha256(os.path.join(SRC_DIR, n))
        rp = os.path.join(REF_DIR, r)
        if os.path.exists(rp) and sha256(rp) == a:
            same += 1
    print("sha256 vs 原版资源: %d/%d identical" % (same, len(names)))
    if same != len(names):
        print("WARN: 工程里的竞技场帧与 原版资源 不完全一致 —— 下面的量取要用工程的图重做")

    rows = []
    frames = []
    for i, n in enumerate(names):
        im = Image.open(os.path.join(SRC_DIR, n)).convert("RGBA")
        a = np.asarray(im).astype(np.int16)
        opaque, cls = classify(a)
        bb = bbox_of(opaque)
        cov = opaque.mean() * 100.0
        mc = a[opaque][:, :3].mean(axis=0) if opaque.any() else np.zeros(3)
        rb = river_band(opaque, cls, im.size[0])
        rows.append({
            "idx": i,
            "file": n,
            "size": "%dx%d" % im.size,
            "bbox": fmt_bbox(bb),
            "cov": "%.2f" % cov,
            "mean": "%.0f,%.0f,%.0f" % (mc[0], mc[1], mc[2]),
            "grass": int(cls["grass"].sum()),
            "water": int(cls["water"].sum()),
            "tan": int(cls["tan"].sum()),
            "wood": int(cls["wood"].sum()),
            "grey": int(cls["grey"].sum()),
            "river": ("y%d..%d" % rb) if rb else "-",
        })
        frames.append((im, bb))

    # ── TSV 量化表 ────────────────────────────────────────────────────────────
    tsv = os.path.join(TEST_DIR, "B1-arena-frames.tsv")
    with open(tsv, "w", encoding="utf-8") as f:
        f.write("frame\tfile\tsize\tbbox\tcoverage%\tmean_rgb\tgrass\twater\ttan\twood\tgrey\triver_band\n")
        for r in rows:
            f.write("f%03d\t%s\t%s\t%s\t%s\t%s\t%d\t%d\t%d\t%d\t%d\t%s\n" % (
                r["idx"], r["file"], r["size"], r["bbox"], r["cov"], r["mean"],
                r["grass"], r["water"], r["tan"], r["wood"], r["grey"], r["river"]))
    print("tsv -> %s" % tsv)

    # ── 联络图（整幅画布，5 列）───────────────────────────────────────────────
    n = len(frames)
    grid_rows = (n + GRID_COLS - 1) // GRID_COLS
    cell_h = int(CELL_W * frames[0][0].size[1] / frames[0][0].size[0])  # 300*1677/1090 = 461
    sheet = Image.new("RGBA", (GRID_COLS * CELL_W, grid_rows * (cell_h + LABEL_H)), (24, 24, 28, 255))
    d = ImageDraw.Draw(sheet)
    for i, (im, bb) in enumerate(frames):
        cx = (i % GRID_COLS) * CELL_W
        cy = (i // GRID_COLS) * (cell_h + LABEL_H)
        thumb = im.resize((CELL_W, cell_h), Image.LANCZOS)
        sheet.paste(thumb, (cx, cy + LABEL_H), thumb)
        if bb:
            sx, sy = CELL_W / float(im.size[0]), cell_h / float(im.size[1])
            d.rectangle([cx + bb[0] * sx, cy + LABEL_H + bb[1] * sy,
                         cx + bb[2] * sx, cy + LABEL_H + bb[3] * sy], outline=(255, 60, 60, 255), width=2)
        d.text((cx + 6, cy + 4), "f%03d  %dx%d" % (i, im.size[0], im.size[1]), fill=(255, 255, 120, 255))
        d.text((cx + 6, cy + 18), "%s cov=%s%%" % (rows[i]["bbox"], rows[i]["cov"]), fill=(180, 220, 255, 255))
        d.rectangle([cx, cy, cx + CELL_W - 1, cy + LABEL_H + cell_h - 1], outline=(90, 90, 100, 255), width=1)
    out = os.path.join(SHOT_DIR, "B1-arena-frames-contact.png")
    sheet.convert("RGB").save(out)
    print("contact -> %s  (%dx%d)" % (out, sheet.size[0], sheet.size[1]))

    # ── 分页放大图（按内容 bbox 裁剪后等比放大，看清单个图元）──────────────────
    for base in range(0, n, PAGE_FRAMES):
        chunk = list(range(base, min(n, base + PAGE_FRAMES)))
        crops = []
        for i in chunk:
            im, bb = frames[i]
            if bb is None:
                continue
            pad = 6
            x0, y0 = max(0, bb[0] - pad), max(0, bb[1] - pad)
            x1, y1 = min(im.size[0], bb[2] + pad + 1), min(im.size[1], bb[3] + pad + 1)
            c = im.crop((x0, y0, x1, y1))
            s = min(PAGE_CELL / float(c.size[0]), PAGE_CELL / float(c.size[1]))
            s = max(1.0, min(s, 4.0))       # 小图最多放 4x，避免糊
            if s > 1.0:
                c = c.resize((int(c.size[0] * s), int(c.size[1] * s)), Image.NEAREST)
            crops.append((i, c, (x0, y0, x1, y1)))
        cellw = max(c[1].size[0] for c in crops) + 16
        cellh = max(c[1].size[1] for c in crops) + 34
        rws = (len(crops) + PAGE_COLS - 1) // PAGE_COLS
        pg = Image.new("RGBA", (PAGE_COLS * cellw, rws * cellh), (24, 24, 28, 255))
        dp = ImageDraw.Draw(pg)
        for k, (i, c, r) in enumerate(crops):
            cx = (k % PAGE_COLS) * cellw
            cy = (k // PAGE_COLS) * cellh
            pg.paste(c, (cx + 8, cy + 24), c)
            dp.text((cx + 8, cy + 6),
                    "f%03d crop x[%d..%d] y[%d..%d] -> %dx%d" % (i, r[0], r[2], r[1], r[3], c.size[0], c.size[1]),
                    fill=(255, 255, 120, 255))
            dp.rectangle([cx + 4, cy + 20, cx + cellw - 5, cy + cellh - 5], outline=(90, 90, 100, 255), width=1)
        p = os.path.join(SHOT_DIR, "B1-arena-frames-page%d.png" % (base // PAGE_FRAMES + 1))
        pg.convert("RGB").save(p)
        print("page%d -> %s (%dx%d)" % (base // PAGE_FRAMES + 1, p, pg.size[0], pg.size[1]))

    # ── f022 整帧 1:1 + 河道带 + 两座桥 2x（"完整场地 + 两座桥"是选底图的判据）────
    f22, bb22 = frames[22]
    f22.crop((bb22[0], bb22[1], bb22[2] + 1, bb22[3] + 1)).convert("RGB").save(
        os.path.join(SHOT_DIR, "B1-arena-f022-full.png"))
    rb22 = river_band(*classify(np.asarray(f22).astype(np.int16)), f22.size[0])
    print("f022 content bbox=%s river_band=%s" % (fmt_bbox(bb22), rb22))
    if rb22:
        y0, y1 = max(0, rb22[0] - 60), min(f22.size[1], rb22[1] + 60)
        f22.crop((0, y0, f22.size[0], y1)).convert("RGB").save(
            os.path.join(SHOT_DIR, "B1-arena-f022-riverband.png"))
        print("f022 riverband -> y%d..%d" % (y0, y1))
        # 两条通路（格 3.5 / 14.5）附近各裁一块 2x
        for tag, xc in (("bridgeA", 300), ("bridgeB", 800)):
            x0, x1 = max(0, xc - 150), min(f22.size[0], xc + 150)
            c = f22.crop((x0, y0, x1, y1))
            c = c.resize((c.size[0] * 2, c.size[1] * 2), Image.LANCZOS)
            c.convert("RGB").save(os.path.join(SHOT_DIR, "B1-arena-f022-%s.png" % tag))
            print("f022 %s -> x%d..%d (2x)" % (tag, x0, x1))

    f06, bb06 = frames[6]
    rb06 = river_band(*classify(np.asarray(f06).astype(np.int16)), f06.size[0])
    print("f006 content bbox=%s river_band=%s" % (fmt_bbox(bb06), rb06))
    if rb06:
        y0, y1 = max(0, rb06[0] - 60), min(f06.size[1], rb06[1] + 60)
        f06.crop((0, y0, f06.size[0], y1)).convert("RGB").save(
            os.path.join(SHOT_DIR, "B1-arena-f006-riverband.png"))
        print("f006 riverband -> y%d..%d" % (y0, y1))

    return 0


if __name__ == "__main__":
    sys.exit(main())
