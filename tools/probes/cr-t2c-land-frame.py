#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cr-t2c-land-frame.py —— CR-T2c：把原版 `ui_out` 的 **frame_547**（金卡板）按**既有落地口径**
落进项目 `Resources`（⛔ 不是自绘：像素 = 原版页面级 png 的**同一块区域**的 1:1 裁切）。

做法（与已落地的 74 张 `Ui/**/ui_out/frame_NNN.png` 同口径）：
  源  = `原版资源/cr-assets-png/assets/sc/ui_out/ui_sprite_547.png`（1663×2810 页面级，只有该 sprite 不透明）
  取  = 它的 alpha 包围盒（实测 (798,2224)-(956,2386) = 158×162）
  落  = `client/Assets/Resources/Sprites/Ui/Slots/ui_out/frame_547.png`
  meta= 复制同目录 `frame_531.png.meta` 的导入设置（Single Sprite），只换 `guid`（新生成的 32 位十六进制）
另：自检——落盘文件与源区域**逐像素相同**（读回比对，打印 MISMATCH=0/1）。
用法： python tools/probes/cr-t2c-land-frame.py
"""
import hashlib
import io
import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "ui_out", "ui_sprite_547.png")
DST_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui", "Slots", "ui_out")
DST = os.path.join(DST_DIR, "frame_547.png")
META_TPL = os.path.join(DST_DIR, "frame_043.png.meta")


def main():
    page = Image.open(SRC)
    bb = page.getchannel("A").getbbox()
    if not bb:
        print("source EMPTY")
        return 1
    print("source=%s page=%s alpha_bbox=%s size=%dx%d" % (SRC, page.size, bb, bb[2] - bb[0], bb[3] - bb[1]))
    crop = page.convert("RGBA").crop(bb)
    crop.save(DST)
    print("wrote %s  %s" % (DST, crop.size))

    # 逐像素自检：落盘文件 == 源区域
    back = Image.open(DST).convert("RGBA")
    same = back.size == crop.size and list(back.getdata()) == list(crop.getdata())
    print("SELFTEST pixel-identical to the source region = %s  (MISMATCH=%d)" % (same, 0 if same else 1))

    # meta：复制同目录 `frame_043.png.meta`（同为"整张就是一格"的独立 png），
    # 换 guid + 把那一格的 name/rect/pivot/nameFileIdTable 改成 158×162 全幅。
    # ⚠️ 直接复制 `frame_531.png.meta` 是错的：它的 sprites[0].rect 是 531 自己的 67×74 ⇒
    #    `Resources.Load<Sprite>` 会返回错尺寸的裁剪格。
    if not os.path.exists(META_TPL):
        print("WARN no meta template at %s" % META_TPL)
        return 1
    t = io.open(META_TPL, encoding="utf-8").read()
    guid = hashlib.md5(b"clover-cr-t2c-frame547").hexdigest()
    w, h = crop.size
    t = re.sub(r"^guid: [0-9a-f]{32}$", "guid: " + guid, t, count=1, flags=re.M)
    t = t.replace("frame_043_0", "frame_547_0")
    t = re.sub(r"(name: frame_547_0\s*\n\s*rect:\s*\n\s*serializedVersion: 2\s*\n\s*x: )-?\d+",
               r"\g<1>0", t, count=1)
    t = re.sub(r"(name: frame_547_0[\s\S]{0,120}?y: )-?\d+", r"\g<1>0", t, count=1)
    t = re.sub(r"(name: frame_547_0[\s\S]{0,160}?width: )-?\d+", r"\g<1>%d" % w, t, count=1)
    t = re.sub(r"(name: frame_547_0[\s\S]{0,200}?height: )-?\d+", r"\g<1>%d" % h, t, count=1)
    t = re.sub(r"(name: frame_547_0[\s\S]{0,260}?pivot: \{x: )[-\d.]+(, y: )[-\d.]+",
               r"\g<1>0.5\g<2>0.5", t, count=1)
    io.open(DST + ".meta", "w", encoding="utf-8", newline="\n").write(t)
    m = re.search(r"name: (frame_547_0)\s*\n\s*rect:\s*\n\s*serializedVersion: 2\s*\n\s*x: (-?\d+)\s*\n\s*y: (-?\d+)\s*\n\s*width: (\d+)\s*\n\s*height: (\d+)", t)
    print("wrote %s.meta (guid=%s, template=%s)  sprites[0]=%s"
          % (DST, guid, os.path.basename(META_TPL), m.groups() if m else "PARSE-FAIL"))
    ok = bool(m) and (int(m.group(4)), int(m.group(5))) == (w, h)
    print("SELFTEST meta rect == %dx%d : %s" % (w, h, ok))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
