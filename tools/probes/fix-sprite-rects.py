#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按 PNG 的真实 alpha 包围盒重算 `Sprites/**` 各 `.png.meta` 的 `spriteSheet` rect。

用法（项目根下）：

    python tools/probes/fix-sprite-rects.py            # 修（先备份，再改）
    python tools/probes/fix-sprite-rects.py --check    # 只报不改（判定用）

口径
----
* 整画布口径（`sprites[0].rect` == 整幅画布）⇒ **不动**（既有口径，不是缺陷）。
* 裁剪框口径 ⇒ rect 必须是**真实 alpha 包围盒**（alpha > 0），不是阈值包围盒：
  阈值算法会把淡的火舌尾 / 暗的塔身部件排除在外，rect 塌成一小块 ⇒ Unity 只画那一小块
  ⇒ 屏幕上看到的是碎片。
* 同一张 PNG 里 Unity 会导入出**多个子 Sprite**（原版资源包自带切片）：客户端的帧数组是
  `LoadAll<Sprite>` 的扁平列表，子切片会插进帧序列里被当成独立帧播 ⇒ 子切片的 rect 同样
  按真实包围盒重算，让帧序列里每一帧都是完整画面。

坐标
----
`.meta` 里 `rect.y` 从贴图**底边**起算；PIL 的 alpha bbox 是**左上角**原点。
换算：`x = left`、`y = H - lower`、`width = right - left`、`height = lower - upper`。

改动面
------
只改 `rect:` 下的 `x` / `y` / `width` / `height` 四个数；`guid` / `internalID` /
`internalIDToNameTable` / `spriteID` / `pivot` / `alignment` / `nameFileIdTable` /
`spriteMode` / `spritePixelsToUnits` 一律不碰（碰了精灵身份就变了 ⇒ 帧数组取不到）。
"""
import argparse
import os
import re
import shutil
import sys
from collections import Counter, defaultdict

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SPRITE_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites")
BACKUP_DIR = os.path.join(ROOT, ".ai-tmp", "test", "fxassets-bak")

TOLERANCE = 2  # 像素；与真实包围盒的差 ≤ 2px 视为同一口径，不动

ENTRY_RE = re.compile(
    r"(?P<head>\n    - serializedVersion: 2\r?\n"
    r"      name: (?P<name>.+?)\r?\n"
    r"      rect:\r?\n"
    r"        serializedVersion: 2\r?\n"
    r"        x: )(?P<x>-?\d+)"
    r"(?P<mid>\r?\n        y: )(?P<y>-?\d+)"
    r"(?P<mid2>\r?\n        width: )(?P<w>\d+)"
    r"(?P<mid3>\r?\n        height: )(?P<h>\d+)"
)


def content_bbox(path):
    """PNG 的真实 alpha 包围盒。

    返回 (PIL 口径 (left, upper, right, lower), 贴图尺寸, Unity 口径 rect (x, y, w, h))。
    PIL 的 bbox 是**左上角**原点；Unity 的 rect.y 从贴图**底边**起算 ⇒ y = H - lower。
    """
    with Image.open(path) as im:
        width, height = im.size
        alpha = im.convert("RGBA").split()[3]
        box = alpha.getbbox()
    if box is None:
        return None, (width, height), None
    left, upper, right, lower = box
    return box, (width, height), (left, height - lower, right - left, lower - upper)


def parse_entries(text):
    """列出 (match_start, match_end, name, x, y, w, h)。"""
    out = []
    for m in ENTRY_RE.finditer(text):
        out.append((m.start(), m.end(), m.group("name"), int(m.group("x")),
                    int(m.group("y")), int(m.group("w")), int(m.group("h"))))
    return out


def rect_delta(entry, truth_pil, height):
    """裁剪框与真实包围盒的差（四个量各比，单位像素）。

    两个矩形都换算到**左上角原点**再比：meta rect 的上边 = H - y - h，
    内容的上边 = PIL bbox 的 upper。
    """
    x, y, w, h = entry
    left, upper, right, lower = truth_pil
    entry_top = height - y - h
    return max(abs(x - left), abs(entry_top - upper),
               abs(w - (right - left)), abs(h - (lower - upper)))


def collect():
    """逐张 PNG 判定：返回 (缺陷项, 待修项)。"""
    defects = []
    fixes = []
    for dirpath, _dirnames, filenames in os.walk(SPRITE_DIR):
        for fn in sorted(filenames):
            if not fn.endswith(".png"):
                continue
            png = os.path.join(dirpath, fn)
            meta = png + ".meta"
            if not os.path.exists(meta):
                continue
            rel = os.path.relpath(png, SPRITE_DIR).replace("\\", "/")
            box, (width, height), truth = content_bbox(png)
            if truth is None:
                continue
            with open(meta, "r", encoding="utf-8", newline="") as f:
                text = f.read()
            entries = parse_entries(text)
            if not entries:
                continue
            first = entries[0]
            if (first[3], first[4], first[5], first[6]) == (0, 0, width, height):
                continue  # 整画布口径：既有口径，不是缺陷
            delta = rect_delta((first[3], first[4], first[5], first[6]), box, height)
            if delta > TOLERANCE:
                defects.append((rel, delta, len(entries)))
            # 要改的：失配的 sprites[0] + 一律的子切片（子切片进了帧序列就是碎片）
            targets = []
            if delta > TOLERANCE:
                targets.append(0)
            if len(entries) > 1:
                targets.extend(range(1, len(entries)))
            if targets:
                fixes.append((rel, meta, truth, targets, delta, len(entries)))
    return defects, fixes


def rewrite(meta, truth, targets):
    """只改 rect 下的四个数，其余字节原样写回。"""
    with open(meta, "r", encoding="utf-8", newline="") as f:
        text = f.read()
    entries = parse_entries(text)
    tx, ty, tw, th = truth
    out = []
    last = 0
    for idx, (start, end, _name, _x, _y, _w, _h) in enumerate(entries):
        if idx not in targets:
            continue
        out.append(text[last:start])
        m = ENTRY_RE.match(text, start)
        head, mid, mid2, mid3 = m.group("head"), m.group("mid"), m.group("mid2"), m.group("mid3")
        out.append("%s%d%s%d%s%d%s%d" % (head, tx, mid, ty, mid2, tw, mid3, th))
        last = end
    out.append(text[last:])
    new_text = "".join(out)
    if new_text == text:
        return False
    with open(meta, "w", encoding="utf-8", newline="") as f:
        f.write(new_text)
    return True


def backup(meta, rel):
    dst = os.path.join(BACKUP_DIR, rel + ".meta")
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if not os.path.exists(dst):
        shutil.copy2(meta, dst)
    return dst


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只判定，不改文件")
    args = ap.parse_args()

    if not os.path.isdir(SPRITE_DIR):
        print("找不到精灵目录：%s" % SPRITE_DIR)
        return 2

    defects, fixes = collect()
    per_dir_defect = Counter()
    for rel, _delta, _n in defects:
        per_dir_defect[os.path.dirname(rel)] += 1

    print("扫描目录：%s" % os.path.relpath(SPRITE_DIR, ROOT))
    print("缺陷（裁剪框口径失配 > %dpx）：%d 张" % (TOLERANCE, len(defects)))
    for d in sorted(per_dir_defect):
        print("    %-44s %d" % (d, per_dir_defect[d]))
    print("待修文件（含子切片归一）：%d 个" % len(fixes))

    if args.check:
        return 0 if not defects else 1

    changed = 0
    per_dir_changed = Counter()
    for rel, meta, truth, targets, _delta, _n in fixes:
        backup(meta, rel)
        if rewrite(meta, truth, targets):
            changed += 1
            per_dir_changed[os.path.dirname(rel)] += 1
    print("已改文件：%d" % changed)
    for d in sorted(per_dir_changed):
        print("    %-44s %d" % (d, per_dir_changed[d]))
    print("备份目录：%s" % os.path.relpath(BACKUP_DIR, ROOT))

    # 复判：改完再算一次缺陷数
    defects2, _ = collect()
    print("复判缺陷数：%d" % len(defects2))
    return 0 if not defects2 else 1


if __name__ == "__main__":
    sys.exit(main())
