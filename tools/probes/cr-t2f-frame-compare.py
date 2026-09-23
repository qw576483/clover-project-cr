#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2f-frame-compare.py —— CR-T2f：**逐像素比**「原版 18 图手牌框」vs「我们实际渲染的 `ui_out/200`」
（纯离线，只读 + 出一张放大对照图；⛔ 不改任何代码、不碰 HudPanel）。

三方比对：
  A 原版：`策划/参考图/18_对局HUD_1080x1920.jpg` 第 2 张手牌（裁定基线；注意该手牌是**灰化态**）
  B 我方：`.ai-tmp/screenshots/CR-T2-hud-mine.png` 的 `Hand0`（实机渲染，卡框 = `ui_out/200` 九宫格）
  C 素材：`client/Assets/Resources/Sprites/Ui/Slots/ui_out/frame_200.png`（96×137 原图）
量三件：① 四边**框厚**（px）② 四角**圆角半径**（px）③ 框带**主色**（RGB）。
用法： python tools/probes/cr-t2f-frame-compare.py
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
BASE18 = os.path.join(ROOT, "策划", "参考图", "18_对局HUD_1080x1920.jpg")
MINE = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-T2-hud-mine.png")
SPRITE = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui", "Slots", "ui_out", "frame_200.png")
OUT = os.path.join(ROOT, ".ai-tmp", "test")


def lum(q):
    return (q[0] * 299 + q[1] * 587 + q[2] * 114) // 1000


def analyse(name, im, box):
    x0, y0, x1, y1 = box
    px = im.load()
    w, h = x1 - x0, y1 - y0
    ym = (y0 + y1) // 2
    xm = (x0 + x1) // 2
    print("== %s  box=%s  %dx%d" % (name, str(box), w, h))
    prof = [px[x0 + i, ym] for i in range(min(18, w))]
    print("   左边缘 18px 剖面: " + " ".join("%d/%d/%d(%d)" % (c[0], c[1], c[2], lum(c)) for c in prof))
    profR = [px[x1 - 1 - i, ym] for i in range(min(18, w))]
    print("   右边缘 18px 剖面: " + " ".join("%d/%d/%d(%d)" % (c[0], c[1], c[2], lum(c)) for c in profR))
    profT = [px[xm, y0 + i] for i in range(min(18, h))]
    print("   上边缘 18px 剖面: " + " ".join("%d/%d/%d(%d)" % (c[0], c[1], c[2], lum(c)) for c in profT))
    profB = [px[xm, y1 - 1 - i] for i in range(min(18, h))]
    print("   下边缘 18px 剖面: " + " ".join("%d/%d/%d(%d)" % (c[0], c[1], c[2], lum(c)) for c in profB))

    # 框带（最外 12px 里亮度最高的那 4px）主色
    band = [px[x0 + i, ym] for i in range(4)] + [px[x1 - 1 - i, ym] for i in range(4)]
    avg = tuple(sum(c[k] for c in band) // len(band) for k in range(3))
    print("   框带主色(左右最外各4px 平均) = %s" % str(avg))

    # 圆角：顶行宽度铺满到 w 的行号（= 半径近似）
    full = None
    for i in range(min(40, h)):
        xs = [x for x in range(x0, x1) if lum(px[x, y0 + i]) > lum(px[xm, y1 - 4]) - 40]
        if xs and (max(xs) - min(xs) + 1) >= w - 2:
            full = i
            break
    print("   圆角半径近似(顶行铺满行号) = %s" % ("%d px" % full if full is not None else "未判出"))
    return


def main():
    b18 = Image.open(BASE18).convert("RGB")
    mine = Image.open(MINE).convert("RGB")
    spr = Image.open(SPRITE).convert("RGBA")

    # A 原版 18 卡2（D 系列口径：x287..427 / y1614..1785）
    analyse("A 原版18 卡2", b18, (287, 1614, 427, 1785))
    # B 我方实机 Hand0（实机 dump：144,1614 140x171）
    analyse("B 我方 Hand0(ui_out/200)", mine, (144, 1614, 284, 1785))
    # C 素材原图
    analyse("C 素材 frame_200", spr.convert("RGB"), (0, 0, spr.size[0], spr.size[1]))

    # 放大对照图：三段并排（各裁一张卡）
    def cell(im, box, scale=3):
        c = im.crop(box)
        return c.resize((c.size[0] * scale, c.size[1] * scale), Image.NEAREST)
    ca = cell(b18, (287 - 4, 1614 - 4, 427 + 4, 1785 + 4))
    cb = cell(mine, (144 - 4, 1614 - 4, 284 + 4, 1785 + 4))
    cc = cell(spr.convert("RGB"), (0, 0, spr.size[0], spr.size[1]))
    H = max(ca.size[1], cb.size[1], cc.size[1])
    W = ca.size[0] + cb.size[0] + cc.size[0] + 40
    cv = Image.new("RGB", (W, H + 26), (24, 24, 28))
    d = ImageDraw.Draw(cv)
    d.text((6, 6), "A original 18 (greyed)", fill=(255, 220, 80))
    cv.paste(ca, (6, 22))
    d.text((ca.size[0] + 16, 6), "B ours rendered", fill=(120, 255, 160))
    cv.paste(cb, (ca.size[0] + 16, 22))
    d.text((ca.size[0] + cb.size[0] + 26, 6), "C ui_out/frame_200", fill=(160, 200, 255))
    cv.paste(cc, (ca.size[0] + cb.size[0] + 26, 22))
    p = os.path.join(OUT, "CR-T2f-frames.png")
    cv.save(p)
    print("saved %s %s" % (p, cv.size))
    return 0


if __name__ == "__main__":
    sys.exit(main())
