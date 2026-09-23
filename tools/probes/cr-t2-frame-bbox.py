#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2-frame-bbox.py —— 逐帧量 `ui_spells_out` 的**透明包围盒**（alpha>阈值 的行列集合）。

为什么必须逐帧量：原版把 60 张卡面挤在一张/多张图上打包导出，**每一帧自己内容物的大小与
横向位置都不同**（例：`frame_022` 的内容贴着图左缘，`frame_049` 的内容整体右移）。原先
`CrUiStyle` 只用**一张固定的窗口** (98,0,197,251)（= 全表并集）裁所有帧 ⇒ 内容偏左/偏右的帧被
裁掉一半、偏空的帧周围留一大圈透明（实机表现为「卡面像贴纸、大小不一、被切断」）。

输出：
  1) 逐帧包围盒表（可直接粘进 `CrUiStyle.CardArtCropTable`）
  2) 全表并集（旧口径，用作对照）
用法： python tools/probes/cr-t2-frame-bbox.py
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Cards", "ui_spells_out")
ALPHA = 8          # 与 CrUiStyle 的透明判定同口径（alpha > 8 才算内容）


def bbox(path):
    im = Image.open(path).convert("RGBA")
    a = im.getchannel("A")
    return im.size, a.getbbox() if hasattr(a, "getbbox") else None


def bbox_threshold(path, thr=ALPHA):
    im = Image.open(path).convert("RGBA")
    px = im.load()
    w, h = im.size
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > thr:
                if x < x0: x0 = x
                if y < y0: y0 = y
                if x > x1: x1 = x
                if y > y1: y1 = y
    if x1 < 0:
        return w, h, None
    return w, h, (x0, y0, x1 + 1, y1 + 1)


def main():
    files = sorted(f for f in os.listdir(DIR) if f.lower().endswith(".png") and f.lower().startswith("frame_"))
    if not files:
        print("no frames in " + DIR)
        return 1
    rows = []
    ux0, uy0, ux1, uy1 = 10 ** 9, 10 ** 9, -1, -1
    for f in files:
        w, h, bb = bbox_threshold(os.path.join(DIR, f))
        if bb is None:
            rows.append((f, w, h, None))
            continue
        rows.append((f, w, h, bb))
        ux0, uy0 = min(ux0, bb[0]), min(uy0, bb[1])
        ux1, uy1 = max(ux1, bb[2]), max(uy1, bb[3])
    print("frames=%d  union=(%d,%d)-(%d,%d)  unionWxH=%dx%d"
          % (len(files), ux0, uy0, ux1, uy1, ux1 - ux0, uy1 - uy0))
    print("---- per-frame bbox (name  WxH  x0,y0,x1,y1  w x h) ----")
    for f, w, h, bb in rows:
        if bb is None:
            print("%s  %dx%d  EMPTY" % (f, w, h))
        else:
            print("%s  %dx%d  %d,%d,%d,%d  %dx%d" % (f, w, h, bb[0], bb[1], bb[2], bb[3], bb[2] - bb[0], bb[3] - bb[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
