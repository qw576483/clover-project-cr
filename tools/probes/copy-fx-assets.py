#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
把**确实要被引用的那几组特效帧**从解包素材落到工程里（**判据资产**，⛔ 不许删）。

用法（项目根下）:
    python tools/probes/copy-fx-assets.py

源：   <根>/原版资源/cr-assets-png/assets/sc/effects_out/effects_sprite_NNN.png
落点： client/Assets/Resources/Sprites/Effects/<用途>/frame_NNN.png   （NNN = **原版源帧号**）
      （命名规则与 `Core/ResPaths.cs` 的 `FrameName` 必须一致：`frame_` + 3 位十进制）

⛔ **只复制 RANGES 里列的那几组**（37 张）；612 帧整目录搬是被明令禁止的。
   要哪几组、为什么，见 `.ai-tmp/test/F1-特效帧辨认.md`（逐帧看图结论）。

导入设置 = **照抄 `client/Assets/Resources/Sprites/Ui/loading_bg.png.meta`** 的 TextureImporter
（`spriteMode: 2` / `textureType: 8` = Sprite / `spritePixelsToUnits: 100` / `filterMode: 1` /
 `alphaIsTransparency: 1` / `maxTextureSize: 2048` …），只改这三处**必然要改**的字段：
  ① `guid`（项目内唯一；= md5(资产相对路径)）
  ② `spriteSheet.sprites[0]` 的 `name` / `rect` / `spriteID` / `internalID`
     —— rect 取**整幅画布**（474×537），**不是**内容 bbox：
        逐帧内容 bbox 各不相同 ⇒ 若按 bbox 切子图，每帧 pivot 不同 ⇒ 换帧时整帧被重新居中 = 抖动
        （`View/UnitView.cs` 的 `SpritePivotMode.UnifiedCanvasAnchor` 注释里记着这条实测坑）。
        取整幅画布 ⇒ 所有帧 rect 相同 ⇒ pivot 相同 ⇒ 播放时零位移，且这是**美术自己的画布布局**，零编造。
  ③ `internalIDToNameTable.first[].second` / `nameFileIdTable` 的键名
"""
import hashlib
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC_DIR = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc", "effects_out")
DST_ROOT = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Effects")
META_TEMPLATE = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui", "loading_bg.png.meta")

# 画布尺寸（原版 effects_out 全部 612 帧都是这一尺寸；见 .ai-tmp/test/F1-fx-index.tsv 的 max frame size）
CANVAS_W, CANVAS_H = 474, 537

# 用途目录 → 原版源帧号区间（闭区间）。**这份清单 = 落地清单**，改它必须同时改
# `Core/ResPaths.cs` 的特效区段与 `.ai-tmp/test/F1-特效帧辨认.md`。
RANGES = [
    ("Hit", 50, 56),        # 命中/受击闪光：紫光球→黄光球→白四角星芒→淡蓝碎冰
    ("Blast", 418, 427),    # 爆炸/塔毁：黄橙漩涡大火球（由大缩到亮核）
    ("Arrow", 440, 459),    # 弹道/飞行物：红羽白镞箭矢
]


def guid_for(rel_path):
    return hashlib.md5(("clover-cr-fx:" + rel_path.replace("\\", "/")).encode("utf-8")).hexdigest()


def id_for(rel_path):
    """子 Sprite 的 internalID（int64）。取值域避开 Unity 常见的 0/1；用 md5 前 8 字节定。
    注意必须落在 int64 内且**非 0** —— 0 会被 Unity 当成 "没有 internalID"。"""
    h = hashlib.md5(("clover-cr-fx-id:" + rel_path.replace("\\", "/")).encode("utf-8")).digest()
    v = int.from_bytes(h[:8], "big", signed=True)
    if v == 0:
        v = 1
    return v


def build_meta(rel_path, sprite_name):
    with open(META_TEMPLATE, "r", encoding="utf-8") as f:
        t = f.read()
    g = guid_for(rel_path)
    sid = id_for(rel_path)

    out = []
    for line in t.splitlines():
        s = line.strip()
        if s.startswith("guid:"):
            out.append("guid: " + g)
            continue
        if s.startswith("213:"):
            indent = line[:len(line) - len(line.lstrip())]
            out.append(indent + "213: " + str(sid))
            continue
        if s.startswith("second:"):
            indent = line[:len(line) - len(line.lstrip())]
            out.append(indent + "second: " + sprite_name)
            continue
        if s.startswith("name:"):
            indent = line[:len(line) - len(line.lstrip())]
            out.append(indent + "name: " + sprite_name)
            continue
        if s.startswith("spriteID:"):
            if s == "spriteID:":
                out.append(line)
                continue          # texture 级 spriteID（空）原样保留
            indent = line[:len(line) - len(line.lstrip())]
            out.append(indent + "spriteID: " + g)
            continue
        if s.startswith("internalID:"):
            # 有两处：子 Sprite 的（非 0）与 spriteSheet 级的（0）。只改**非 0 / 与 sprites 同块**的那个：
            # 判据 = 后面紧跟 `vertices:` 且当前块是 sprites 块 —— 这里用"值非 0"区分（模板里 spriteSheet 级是 0）。
            if s == "internalID: 0":
                out.append(line)
                continue
            indent = line[:len(line) - len(line.lstrip())]
            out.append(indent + "internalID: " + str(sid))
            continue
        if s.startswith("x: ") and s == "x: 0":
            out.append(line[:len(line) - len(line.lstrip())] + "x: 0")
            continue
        if s.startswith("y: "):
            out.append(line[:len(line) - len(line.lstrip())] + "y: 0")
            continue
        if s.startswith("width: "):
            out.append(line[:len(line) - len(line.lstrip())] + "width: " + str(CANVAS_W))
            continue
        if s.startswith("height: "):
            out.append(line[:len(line) - len(line.lstrip())] + "height: " + str(CANVAS_H))
            continue
        if s.startswith("loading_bg_0:"):
            out.append("  " + sprite_name + ": " + str(sid))
            continue
        out.append(line)
    return "\n".join(out) + "\n"


def main():
    os.makedirs(DST_ROOT, exist_ok=True)
    total = 0
    for use, a, b in RANGES:
        dst_dir = os.path.join(DST_ROOT, use)
        os.makedirs(dst_dir, exist_ok=True)
        for i in range(a, b + 1):
            src = os.path.join(SRC_DIR, "effects_sprite_%03d.png" % i)
            if not os.path.exists(src):
                print("ERROR: 源帧缺失 %s" % src)
                return 1
            name = "frame_%03d.png" % i
            rel = "Assets/Resources/Sprites/Effects/%s/%s" % (use, name)
            dst = os.path.join(dst_dir, name)
            with open(src, "rb") as f:
                data = f.read()
            with open(dst, "wb") as f:
                f.write(data)
            with open(dst + ".meta", "w", encoding="utf-8", newline="\n") as f:
                f.write(build_meta(rel, "frame_%03d_0" % i))
            total += 1
        print("%-6s f%03d..f%03d -> %s (%d 张)" % (use, a, b, dst_dir, b - a + 1))
    print("copied %d png (+%d meta)  源=%s" % (total, total, SRC_DIR))
    return 0


if __name__ == "__main__":
    sys.exit(main())
