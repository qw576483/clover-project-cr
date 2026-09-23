#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2c-frame-search.py —— CR-T2c：在 `原版资源/cr-assets-png/assets/sc/**` **全部** `.sc` 导出的
sprite 里，按**形状 + 颜色**（⛔ 不靠命名关键词）找「手牌卡框」及其组成块。

两阶段（大扫描可后台跑，结果落 TSV 可续跑）：
  bbox  —— 遍历所有 `*_out` 目录，记录每张 png 的页面尺寸 + alpha 包围盒 → `.ai-tmp/test/CR-T2c-bboxes.tsv`
  rank  —— 读 TSV，按"卡尺寸比例"过筛，再算：
             goldFrac  : 与「原版框金」(244,244,130) 距离 < 90 的像素占比
             bandFrac  : 顶部 20% 行里与「卡顶紫带」(32,16,65) 距离 < 60 的占比
             frameMad  : 缩放到原版卡尺寸 (135x163) 后，**只比边框环**（去掉中心 62%）与原版卡框环的 MAD
           ⇒ 三个分一起排序，打印 top N，并生成带编号的联络图。
用法：
  python tools/probes/cr-t2c-frame-search.py bbox
  python tools/probes/cr-t2c-frame-search.py rank [N]
  python tools/probes/cr-t2c-frame-search.py sheet <dir> <file1,file2,...>
"""
import io
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")
TSV = os.path.join(ROOT, ".ai-tmp", "test", "CR-T2c-bboxes.tsv")
OUT = os.path.join(ROOT, ".ai-tmp", "test")
BASE = os.path.join(ROOT, "策划", "参考图", "20_对局_1080x1920.jpg")
CARD = (286, 1699, 421, 1862)          # 原版手牌#2（金边外沿）；G3/D 系列同源口径
GOLD = (244, 244, 130)
PURPLE = (32, 16, 65)
SIZE_OK = (90, 225, 105, 250)          # w0 w1 h0 h1


def dist(p, q):
    return abs(p[0] - q[0]) + abs(p[1] - q[1]) + abs(p[2] - q[2])


def do_bbox():
    fh = io.open(TSV, "w", encoding="utf-8")
    n = 0
    dirs = sorted(d for d in os.listdir(SC) if d.endswith("_out") and os.path.isdir(os.path.join(SC, d)))
    for d in dirs:
        files = sorted(f for f in os.listdir(os.path.join(SC, d)) if f.endswith(".png"))
        for f in files:
            p = os.path.join(SC, d, f)
            try:
                im = Image.open(p)
                w, h = im.size
                bb = im.getchannel("A").getbbox()
            except Exception as e:
                fh.write("%s\t%s\tERR\t%s\n" % (d, f, str(e)[:40]))
                continue
            n += 1
            if bb:
                fh.write("%s\t%s\t%d\t%d\t%d\t%d\t%d\t%d\n" % (d, f, w, h, bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1]))
            else:
                fh.write("%s\t%s\t%d\t%d\tEMPTY\n" % (d, f, w, h))
        print("scanned %-26s %d files (total %d)" % (d, len(files), n))
        fh.flush()
    fh.close()
    print("---- total sprites scanned = %d -> %s" % (n, TSV))
    return 0


def load_ref_ring():
    ref = Image.open(BASE).convert("RGB").crop(CARD)
    return ref


def ring_pixels(im, keep=0.62):
    w, h = im.size
    x0, y0 = int(w * (1 - keep) / 2), int(h * (1 - keep) / 2)
    x1, y1 = w - x0, h - y0
    px = im.load()
    out = []
    for y in range(h):
        for x in range(w):
            if x0 <= x < x1 and y0 <= y < y1:
                continue
            out.append(px[x, y])
    return out


def do_rank(topn):
    ref = load_ref_ring()
    ref_ring = ring_pixels(ref)
    rows = []
    for line in io.open(TSV, encoding="utf-8"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8:
            continue
        d, f, pw, ph, bx, by, w, h = c[0], c[1], int(c[2]), int(c[3]), int(c[4]), int(c[5]), int(c[6]), int(c[7])
        if not (SIZE_OK[0] <= w <= SIZE_OK[1] and SIZE_OK[2] <= h <= SIZE_OK[3]):
            continue
        if not (0.55 <= w / float(h) <= 1.45):
            continue
        p = os.path.join(SC, d, f)
        try:
            page = Image.open(p)
            im = page.convert("RGBA").crop((bx, by, bx + w, by + h))
        except Exception:
            continue
        px = im.load()
        g = b = t = 0
        for y in range(0, h, 2):
            for x in range(0, w, 2):
                t += 1
                q = px[x, y]
                if q[3] <= 8:
                    continue
                if dist(q, GOLD) < 90:
                    g += 1
                if y < h * 0.2 and dist(q, PURPLE) < 60:
                    b += 1
        gold, band = g / float(t), b / float(t)
        if gold < 0.10 and band < 0.10:
            continue
        flat = Image.new("RGB", im.size, (255, 255, 255))
        flat.paste(im, (0, 0), im)
        flat = flat.resize(ref.size, Image.LANCZOS)
        cand_ring = ring_pixels(flat)
        mad = sum(abs(a[0] - c2[0]) + abs(a[1] - c2[1]) + abs(a[2] - c2[2])
                  for a, c2 in zip(ref_ring, cand_ring)) / (len(ref_ring) * 3)
        rows.append((mad, gold, band, d, f, w, h, bx, by))
    rows.sort()
    print("候选 = %d 张（卡尺寸比例 + 有金/紫像素）；按 环MAD 升序：" % len(rows))
    for mad, gold, band, d, f, w, h, bx, by in rows[:topn]:
        print("  MAD=%6.1f  gold=%.2f band=%.2f  %-24s %-28s %dx%d bbox=(%d,%d)"
              % (mad, gold, band, d, f, w, h, bx, by))
    return 0


def do_band(topn):
    """找「手牌排背板」（原版那根横贯 4 张卡、带菱形的紫带）：宽扁 (w 250..1500, h 12..120) + 紫占比高。"""
    rows = []
    for line in io.open(TSV, encoding="utf-8"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8:
            continue
        d, f, bx, by, w, h = c[0], c[1], int(c[4]), int(c[5]), int(c[6]), int(c[7])
        if not (250 <= w <= 1500 and 12 <= h <= 120):
            continue
        p = os.path.join(SC, d, f)
        try:
            page = Image.open(p)
            im = page.convert("RGBA").crop((bx, by, bx + w, by + h))
        except Exception:
            continue
        px = im.load()
        pur = gold = t = 0
        for y in range(0, h, 2):
            for x in range(0, w, 2):
                t += 1
                q = px[x, y]
                if q[3] <= 8:
                    continue
                if dist(q, PURPLE) < 90:
                    pur += 1
                if dist(q, GOLD) < 90:
                    gold += 1
        if pur / float(t) < 0.15:
            continue
        rows.append((pur / float(t), gold / float(t), d, f, w, h, bx, by))
    rows.sort(reverse=True)
    print("宽扁 + 紫占比>=0.15 的候选 = %d 张：" % len(rows))
    for pur, gold, d, f, w, h, bx, by in rows[:topn]:
        print("  purple=%.2f gold=%.2f  %-24s %-30s %dx%d bbox=(%d,%d)" % (pur, gold, d, f, w, h, bx, by))
    return 0


def do_cap(topn):
    """找「卡顶紫帽」（卡宽、扁、深紫，含菱形）: w 90..230, h 22..85, 深紫占比高。"""
    rows = []
    for line in io.open(TSV, encoding="utf-8"):
        c = line.rstrip("\n").split("\t")
        if len(c) < 8:
            continue
        d, f, bx, by, w, h = c[0], c[1], int(c[4]), int(c[5]), int(c[6]), int(c[7])
        if not (90 <= w <= 230 and 22 <= h <= 85):
            continue
        try:
            page = Image.open(os.path.join(SC, d, f))
            im = page.convert("RGBA").crop((bx, by, bx + w, by + h))
        except Exception:
            continue
        px = im.load()
        pur = gold = t = 0
        for y in range(0, h, 2):
            for x in range(0, w, 2):
                t += 1
                q = px[x, y]
                if q[3] <= 8:
                    continue
                if dist(q, PURPLE) < 110:
                    pur += 1
                if dist(q, GOLD) < 90:
                    gold += 1
        if pur / float(t) < 0.30:
            continue
        rows.append((pur / float(t), gold / float(t), d, f, w, h, bx, by))
    rows.sort(reverse=True)
    print("卡宽扁 + 深紫占比>=0.30 的候选 = %d 张：" % len(rows))
    for pur, gold, d, f, w, h, bx, by in rows[:topn]:
        print("  purple=%.2f gold=%.2f  %-24s %-30s %dx%d bbox=(%d,%d)" % (pur, gold, d, f, w, h, bx, by))
    return 0


def do_sheet(d, files, outname="CR-T2c-sheet.png"):
    cells = []
    for f in files.split(","):
        p = os.path.join(SC, d, f.strip())
        if not os.path.exists(p):
            print("MISSING %s" % p)
            continue
        page = Image.open(p)
        bb = page.getchannel("A").getbbox()
        if not bb:
            continue
        im = page.convert("RGBA").crop(bb)
        H = 200
        W = min(300, max(1, int(im.size[0] * 200 / max(1, im.size[1]))))
        r = im.resize((W, H), Image.LANCZOS)
        bg = Image.new("RGB", (W + 6, H + 6), (255, 255, 255))
        bg.paste(r, (3, 3), r)
        cells.append((f, bg))
    if not cells:
        return 1
    per = 8
    rows = [cells[i:i + per] for i in range(0, len(cells), per)]
    cw = max(sum(c[1].size[0] + 8 for c in row) for row in rows) + 12
    ch = sum(max(c[1].size[1] for c in row) + 26 for row in rows) + 8
    cv = Image.new("RGB", (cw, ch), (28, 28, 32))
    y = 4
    for row in rows:
        x = 6
        for f, bg in row:
            cv.paste(bg, (x, y + 20))
            x += bg.size[0] + 8
        y += max(c[1].size[1] for c in row) + 26
    out = os.path.join(OUT, outname)
    cv.save(out)
    print("saved %s %s cells=%s" % (out, cv.size, ",".join(f for f, _ in cells)))
    return 0


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    if sys.argv[1] == "bbox":
        return do_bbox()
    if sys.argv[1] == "rank":
        return do_rank(int(sys.argv[2]) if len(sys.argv) > 2 else 20)
    if sys.argv[1] == "band":
        return do_band(int(sys.argv[2]) if len(sys.argv) > 2 else 20)
    if sys.argv[1] == "cap":
        return do_cap(int(sys.argv[2]) if len(sys.argv) > 2 else 15)
    if sys.argv[1] == "sheet":
        return do_sheet(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else "CR-T2c-sheet.png")
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
