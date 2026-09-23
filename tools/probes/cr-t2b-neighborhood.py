#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2b-neighborhood.py —— CR-T2b：把原版 `ui_out`（914 张 1663×2810 页面级 png，每张只保留自己那块不透明）
里**落在同一片区域**的 sprite 全找出来，拼成联络图，用来人工认「卡框」家族。

做法（机械，不靠命名）：卡底/卡框/卡 mask 在 sheet 上是**相邻摆放**的
（实测 frame_43 白卡底 bbox=(876,2304)、frame_200 槽底=(829,2235)、frame_880 卡 mask=(817,2230)）
⇒ 以这些已知卡族 sprite 的 bbox 为种子，取该区域的邻居。
用法： python tools/probes/cr-t2b-neighborhood.py [x0 y0 x1 y1]
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "ui_out")
OUT = os.path.join(ROOT, ".ai-tmp", "test")


def show(nums):
    """`frames 870,871,...`：把指定帧号（原版 ui_out 页面级 png）按 bbox 裁出来拼联络图。"""
    cells = []
    for s in nums.split(","):
        f = "ui_sprite_%03d.png" % int(s)
        p = os.path.join(SRC, f)
        if not os.path.exists(p):
            print("%s MISSING" % f)
            continue
        im = Image.open(p).convert("RGBA")
        bb = im.getchannel("A").getbbox()
        if not bb:
            print("%s EMPTY" % f)
            continue
        crop = im.crop(bb)
        H = 200
        W = min(300, max(1, int((bb[2] - bb[0]) * 200 / max(1, bb[3] - bb[1]))))
        r = crop.resize((W, H), Image.LANCZOS)
        bg = Image.new("RGB", (W + 6, H + 6), (255, 255, 255))
        bg.paste(r, (3, 3), r)
        cells.append((s, "%dx%d" % (bb[2] - bb[0], bb[3] - bb[1]), bg))
    if not cells:
        return 1
    per = 10
    rows = [cells[i:i + per] for i in range(0, len(cells), per)]
    cw = max(sum(c[2].size[0] + 8 for c in row) for row in rows) + 12
    ch = sum(max(c[2].size[1] for c in row) + 26 for row in rows) + 8
    cv = Image.new("RGB", (cw, ch), (28, 28, 32))
    y = 4
    for row in rows:
        x = 6
        for _s, _d, bg in row:
            cv.paste(bg, (x, y + 20))
            x += bg.size[0] + 8
        y += max(c[2].size[1] for c in row) + 26
    out = os.path.join(OUT, "CR-T2b-pick.png")
    cv.save(out)
    print("saved %s %s cells=%s" % (out, cv.size, ",".join("%s(%s)" % (s, d) for s, d, _ in cells)))
    return 0


def main():
    if len(sys.argv) >= 3 and sys.argv[1] == "frames":
        return show(sys.argv[2])
    box = tuple(int(v) for v in sys.argv[1:5]) if len(sys.argv) >= 5 else (700, 2100, 1050, 2550)
    x0, y0, x1, y1 = box
    hits = []
    files = sorted(f for f in os.listdir(SRC) if f.endswith(".png"))
    for f in files:
        p = os.path.join(SRC, f)
        try:
            bb = Image.open(p).getchannel("A").getbbox()
        except Exception:
            continue
        if not bb:
            continue
        cx, cy = (bb[0] + bb[2]) / 2, (bb[1] + bb[3]) / 2
        if x0 <= cx <= x1 and y0 <= cy <= y1:
            hits.append((f, bb[0], bb[1], bb[2] - bb[0], bb[3] - bb[1]))
    hits.sort(key=lambda t: (t[2], t[1]))
    print("区域 %s 内的 sprite = %d 张" % (str(box), len(hits)))
    for f, bx, by, w, h in hits:
        print("  %-22s bbox=(%d,%d) %dx%d" % (f, bx, by, w, h))
    # 联络图：按 bbox 裁出来，等比例并排
    if not hits:
        return 0
    cells = []
    for f, bx, by, w, h in hits[:60]:
        im = Image.open(os.path.join(SRC, f)).convert("RGBA")
        crop = im.crop((bx, by, bx + w, by + h))
        H = 200
        W = max(1, int(w * H / h))
        r = crop.resize((W, H), Image.LANCZOS)
        bg = Image.new("RGB", (W + 6, H + 6), (255, 255, 255))
        bg.paste(r, (3, 3), r)
        cells.append((f, bg))
    per = 10
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
    out = os.path.join(OUT, "CR-T2b-cardfamily.png")
    cv.save(out)
    print("saved %s %s (cells=%d)" % (out, cv.size, len(cells)))
    with open(os.path.join(OUT, "CR-T2b-cardfamily.txt"), "w", encoding="utf-8") as fh:
        for f, bx, by, w, h in hits:
            fh.write("%s bbox=(%d,%d) %dx%d\n" % (f, bx, by, w, h))
    return 0


if __name__ == "__main__":
    sys.exit(main())
