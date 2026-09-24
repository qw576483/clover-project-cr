#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D130c 同框对照：把**原版参考图**与**本实现实机图**放进同一把尺子，量"战斗区 vs 出牌区"。

用途（用户第 1 条「出牌区挤压战斗区，我记得好像是分离的」）：
  这是"同机位并排图"的定量版 —— 两图同为 1080×1920，用**同一个函数、同一个 x 窗口**量：
    · 草地最低行（= 战斗区底沿）      → 本实现应落到 ≈1580，且 **< 出牌区上沿**（否则就是用户说的"挤压"）
    · 出牌区（暗面板）上沿            → 本实现若靠清屏色当面板，这里量到的是"草地消失后的暗带起点"
  并输出并排图（各 0.5 倍），左=原版、右=本实现，两侧各自画一条红线在自家草地底沿。

只读脚本：不改产品代码。
用法： python lead-d130c-sbs.py <OURS.png> <OUT.png> [--ref <REF.jpg>]
"""
import os
import sys

from PIL import Image, ImageDraw

REF_DEFAULT = "策划/参考图/20_对局_1080x1920.jpg"
X0, X1 = 100, 980        # 中央窗（避开左右装饰）
TH_GRASS_FRAC = 0.12     # 一行算"草地行"的最小草色占比


def is_grass(r, g, b):
    return g > 85 and g > r * 1.03 and g > b * 1.05


def row_profile(im, x0=X0, x1=X1):
    px = im.load()
    W, H = im.size
    x1 = min(x1, W)
    out = []
    for y in range(H):
        n = 0
        for x in range(x0, x1):
            if is_grass(*px[x, y]):
                n += 1
        out.append(n / float(x1 - x0))
    return out


def lowest_grass_row(prof):
    for y in range(len(prof) - 1, -1, -1):
        if prof[y] >= TH_GRASS_FRAC:
            return y
    return None


def top_grass_row(prof):
    for y in range(len(prof)):
        if prof[y] >= TH_GRASS_FRAC:
            return y
    return None


def measure(path, label):
    im = Image.open(path).convert("RGB")
    prof = row_profile(im)
    top = top_grass_row(prof)
    bot = lowest_grass_row(prof)
    # 连续草段（允许 6 行空洞）
    runs = []
    s = None
    gap = 0
    for y in range(len(prof)):
        if prof[y] >= TH_GRASS_FRAC:
            if s is None:
                s = y
            gap = 0
        else:
            if s is not None:
                gap += 1
                if gap > 6:
                    runs.append((s, y - gap))
                    s = None
                    gap = 0
    if s is not None:
        runs.append((s, len(prof) - 1))
    runs = [r for r in runs if r[1] - r[0] >= 20]
    runs.sort(key=lambda r: r[1] - r[0], reverse=True)
    main = runs[0] if runs else (top, bot)
    print("=" * 70)
    print("%-10s %s" % (label, os.path.basename(path)))
    print("  尺寸        : %dx%d" % im.size)
    print("  草地首行    : %s" % top)
    print("  草地末行    : %s   <- 战斗区底沿" % bot)
    print("  草地主段    : %s" % (main,))
    if main and main[0] is not None:
        h = main[1] - main[0] + 1
        print("  主段高      : %d px  ⇒ 32 格 = %.2f px/格" % (h, h / 32.0))
    return {"path": path, "size": im.size, "top": top, "bot": bot, "main": main}


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    ours = argv[1]
    out = argv[2]
    ref = REF_DEFAULT
    if "--ref" in argv:
        ref = argv[argv.index("--ref") + 1]

    mref = measure(ref, "ORIGINAL")
    mours = measure(ours, "OURS")
    print("=" * 70)
    print("判据（可失败）：本实现草地底沿 < 出牌区上沿 1614")
    ob = mours["bot"]
    if ob is None:
        print("  VERDICT : FAIL（本实现图里量不到草地）")
    else:
        print("  本实现草地底沿 = %d ；留白 = 1614 - %d = %d px  ⇒ %s"
              % (ob, ob, 1614 - ob, "PASS" if ob < 1614 else "FAIL（仍被出牌区盖住）"))
    print("  尺寸一致性  : %s vs %s ⇒ %s"
          % (mref["size"], mours["size"],
             "PASS" if mref["size"] == mours["size"] else "FAIL（不同尺寸，比例换算需另行处理）"))

    # 并排图（各 0.5 倍）
    a = Image.open(ref).convert("RGB")
    b = Image.open(ours).convert("RGB")
    sc = 0.5
    a = a.resize((int(a.width * sc), int(a.height * sc)), Image.LANCZOS)
    b = b.resize((int(b.width * sc), int(b.height * sc)), Image.LANCZOS)
    canvas = Image.new("RGB", (a.width + b.width + 8, max(a.height, b.height)), (24, 24, 28))
    canvas.paste(a, (0, 0))
    canvas.paste(b, (a.width + 8, 0))
    d = ImageDraw.Draw(canvas)
    for img, xoff, m, tag in ((a, 0, mref, "REF"), (b, a.width + 8, mours, "OURS")):
        for row, col in ((m["top"], (255, 220, 0)), (m["bot"], (255, 0, 0))):
            if row is None:
                continue
            y = int(row * sc)
            d.line([(xoff, y), (xoff + img.width, y)], fill=col, width=2)
            d.text((xoff + 4, max(0, y - 12)), "%s y=%d" % (tag, row), fill=col)
    # 出牌区上沿 1614 参考线（画在右侧）
    y1614 = int(1614 * sc)
    d.line([(a.width + 8, y1614), (a.width + 8 + b.width, y1614)], fill=(0, 200, 255), width=2)
    d.text((a.width + 12, y1614 + 2), "hand-bar top 1614", fill=(0, 200, 255))
    canvas.save(out)
    print("  并排图      : %s  (%dx%d)" % (out, canvas.width, canvas.height))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
