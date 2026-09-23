#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2-contact.py  ——  把「我方实机截图」与「原版同机位基线图」拼成并排图（离线；只写 .ai-tmp/screenshots/）

两侧的裁剪框都来自**量出来的矩形**，⛔ 不目测对齐：
  我方：`HUD Hand0..3` 的屏幕矩形来自 `.ai-tmp/test/CR-T2-evidence.txt`（驱动在引擎里 dump 的、
        与截图像素同口径的 rect，y 自上而下）；截图 = `.ai-tmp/screenshots/CR-T2-hud-mine.png`。
        卡组编辑同理走 `DECK Card0..7Art` 与 `CR-T2-deck-mine.png`。
  原版：写死在本文件顶部常量里，出处逐条注明（20_对局 / 07_卡组编辑）。
用法： python tools/probes/cr-t2-contact.py hud | deck
"""
import io
import os
import re
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图")
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
EV = os.path.join(ROOT, ".ai-tmp", "test", "CR-T2-evidence.txt")

# ── 原版量取矩形（原图坐标；出处见下）──
# 20_对局_1080x1920.jpg（1080×1920 = 本项目画布 ⇒ 像素即画布值）：金边列 x 286..290 / 417..421、
#   金边行 y 1699..1703 / 1859..1862（第 2 张手牌 = 冰雪精灵）⇒ 4 张卡外框 x 147..705 / y 1699..1862。
# ★ CR-T2g：并排图的"原版"换成**裁定基线 18 图**（20 图是另一版本/模式，主 agent 2026-09-23 裁定不作几何基线）
#   crop = 18 图手牌排（D9/D11/D12：整排 x144 起、单卡 140×171、卡顶 y1614、卡底 y1785）上下各留 ~14px
BASE_HUD = ("18_对局HUD_1080x1920.jpg", (140, 1600, 720, 1800))
# 07_卡组编辑_1242x2208.jpg（DeckEditPanel 的几何出处）：列左边 x=344/651/959、行顶 y=437/934 @1242。
BASE_DECK = ("07_卡组编辑_1242x2208.jpg", (330, 420, 1240, 1225))

W = 1100           # 两条并排图的统一显示宽
H_SINGLE = 420     # 单卡特写高


def rects(node_re):
    """从 evidence 抓 `... rect:x,y wxh`；`node_re` 带一个名为 k 的组（节点名）。取最后一次读数。"""
    out = {}
    if not os.path.exists(EV):
        return out
    # ⚠️ 矩形四个数必须用**具名组**取：`node_re` 自带的组会把序号往后挤（踩过一次，读数整体错位）
    rx = re.compile(node_re + r" active:\w+.*?rect:(?P<x>\d+),(?P<y>\d+) (?P<w>\d+)x(?P<h>\d+)")
    for line in io.open(EV, encoding="utf-8", errors="replace").read().splitlines():
        m = rx.search(line)
        if m:
            out[m.group("k") if "k" in m.groupdict() else "n"] = (
                int(m.group("x")), int(m.group("y")), int(m.group("w")), int(m.group("h")))
    return out


def union(rs):
    return (min(r[0] for r in rs), min(r[1] for r in rs),
            max(r[0] + r[2] for r in rs), max(r[1] + r[3] for r in rs))


def stack(base, mine, bbox, mbox, out_name, title):
    b = base.crop(bbox)
    m = mine.crop(mbox)
    b = b.resize((W, int(b.size[1] * W / b.size[0])), Image.LANCZOS)
    m = m.resize((W, int(m.size[1] * W / m.size[0])), Image.LANCZOS)
    cv = Image.new("RGB", (W + 20, b.size[1] + m.size[1] + 70), (20, 20, 24))
    d = ImageDraw.Draw(cv)
    d.text((10, 4), "ORIGINAL " + title + "  crop=" + str(bbox), fill=(255, 220, 80))
    cv.paste(b, (10, 20))
    d.text((10, b.size[1] + 30), "OURS (in-engine screen rect)  crop=" + str(mbox), fill=(120, 255, 160))
    cv.paste(m, (10, b.size[1] + 50))
    out = os.path.join(SHOTS, out_name)
    cv.save(out)
    print("saved %s  %s" % (out, cv.size))


def single(base, mine, bbox, mbox, out_name, l1, l2):
    b = base.crop(bbox)
    m = mine.crop(mbox)
    b = b.resize((max(1, int(b.size[0] * H_SINGLE / b.size[1])), H_SINGLE), Image.LANCZOS)
    m = m.resize((max(1, int(m.size[0] * H_SINGLE / m.size[1])), H_SINGLE), Image.LANCZOS)
    cv = Image.new("RGB", (b.size[0] + m.size[0] + 30, H_SINGLE + 30), (20, 20, 24))
    d = ImageDraw.Draw(cv)
    cv.paste(b, (10, 24))
    cv.paste(m, (b.size[0] + 20, 24))
    d.text((10, 6), l1, fill=(255, 220, 80))
    d.text((b.size[0] + 20, 6), l2, fill=(120, 255, 160))
    out = os.path.join(SHOTS, out_name)
    cv.save(out)
    print("saved %s  %s" % (out, cv.size))


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "hud"
    if mode == "hud":
        fn, bbox = BASE_HUD
        shot = os.path.join(SHOTS, "CR-T2-hud-mine.png")
        rs = rects(r"HUD (?P<k>Hand[0-3])")
        need = 4
        focus = "Hand2"          # 骑士（frame_022 —— 逐帧裁剪例外那张，最值得单独看）
        # 原版第 1 张卡（金边外沿 x 147..283 / y 1699..1862，上下各留 8px）
        sbox = (144, 1608, 284, 1791)   # 18 图第 1 张手牌（外框 x144..284 / y1614..1785，上下留 6px）
    elif mode == "deck":
        fn, bbox = BASE_DECK
        shot = os.path.join(SHOTS, "CR-T2-deck-mine.png")
        rs = rects(r"DECK (?P<k>Card[0-7]Art)")
        need = 8
        focus = "Card0Art"
        # 原版 07 第 1 个卡池格：列左边 344、行顶 437 @1242（DeckEditPanel 的几何出处）
        sbox = (336, 429, 606, 721)
    else:
        print(__doc__)
        return 2
    if not os.path.exists(shot):
        print("MISSING " + shot)
        return 1
    if len(rs) < need:
        print("MISSING rects in evidence (got %s of %d)" % (sorted(rs), need))
        return 1
    base = Image.open(os.path.join(REF, fn)).convert("RGB")
    mine = Image.open(shot).convert("RGB")
    mb = union(list(rs.values()))
    mb = (max(0, mb[0] - 60), max(0, mb[1] - 70), mb[2] + 20, mb[3] + 40)
    stack(base, mine, bbox, mb, "CR-T2-%s-compare.png" % mode, fn)
    r0 = rs[focus]
    single(base, mine, sbox, (r0[0] - 6, r0[1] - 6, r0[0] + r0[2] + 6, r0[1] + r0[3] + 6),
           "CR-T2-%s-card0.png" % mode, "ORIGINAL " + fn + " " + str(sbox),
           "OURS " + focus + " " + str(r0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
