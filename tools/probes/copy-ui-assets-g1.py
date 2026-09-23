# -*- coding: utf-8 -*-
"""G1: land the TWO extra original-UI frames the portrait layout needs.

WHY THIS FILE EXISTS (and why it is not folded into copy-ui-assets.py)
    `tools/probes/copy-ui-assets.py` is T4c's registry (65 frames, its own report and
    its own three-way check).  Task G1 needs two frames that registry does not carry.
    Rather than edit T4c's file (a parallel task may be reading it), this is the same
    pipeline, same calibre, own registry -- per the task book ("或同口径自写脚本").

    It stays in `tools/probes/` because it is a judgement asset: delete it and the two
    landed frames can no longer be reproduced or audited (which frame, cropped how,
    which ResPaths key).

SAME CALIBRE AS copy-ui-assets.py (all four points, verbatim behaviour):
    1. copy ONLY the referenced frame -- never a whole directory;
    2. the landed file is the frame's own non-transparent bounding box (alpha > 8),
       1:1, no resample -- because a raw copy of a 1663x2810 / 1154x1882 canvas would be
       downscaled by TextureImporter.maxTextureSize = 2048 and, in Multiple mode, one
       auto-sliced sprite per connected alpha island would silently return half an
       element;
    3. path = `Sprites/Ui/<purpose>/<source atlas>/frame_NNN.png` (frame number = the
       number in the ORIGINAL file name `<dir>_sprite_<n>.png`, so any landed file
       reverse-resolves to its row in `策划/原版UI素材索引.md`);
    4. drop a stale `.meta` after rewriting the PNG (the old sprite rects fall outside
       the new bounds and Resources.Load<Sprite> would return null).

WHAT CHANGED vs T4c: the crop rectangle is measured HERE (alpha>8 bbox scan) instead of
being read from `.ai-tmp/screenshots/ui-index-manifest.tsv`, because that manifest is a
throwaway (`.ai-tmp/test/` artefacts are deleted at the end of a task) and is no longer
on disk.  The measurement is cross-checked against the size published per frame in
`策划/原版UI素材索引.md` -- a run fails if the two disagree, so the two documents can
never drift apart silently.

USAGE
    python tools/probes/copy-ui-assets-g1.py --dry-run
    python tools/probes/copy-ui-assets-g1.py
"""
from __future__ import annotations

import os
import re
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")
DST = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites", "Ui")
INDEX = os.path.join(ROOT, "策划", "原版UI素材索引.md")

ALPHA_MIN = 8

# (purpose dir, source atlas dir, source frame, why G1 needs it)
SPRITES = [
    ("Icons", "loading_out", 28,
     "官方 LOGO「CLASH ROYALE」蓝底金冠 520x224 —— BootPanel 的标题图（原来是文字「皇室战争」）"),
    ("Bars", "loading_out", 15,
     "绿色横条 115x39（上缘弧）—— LoadingPanel 进度条填充（索引 §4.2 同组的 014 是它的弧边变体）"),
]

IDX_RE = re.compile(r"_sprite_(\d+)\.png$")


def source_file(atlas: str, frame: int):
    d = os.path.join(SRC, atlas)
    if not os.path.isdir(d):
        return None
    for n in os.listdir(d):
        m = IDX_RE.search(n)
        if m and int(m.group(1)) == frame:
            return os.path.join(d, n)
    return None


def bbox_of(im: Image.Image):
    a = im.split()[-1].point(lambda v: 255 if v > ALPHA_MIN else 0)
    return a.getbbox()


def index_sizes():
    """{atlas section -> {frame: (w, h)}} from the published index document."""
    out = {}
    if not os.path.isfile(INDEX):
        return out
    atlas = None
    with open(INDEX, "r", encoding="utf-8") as fh:
        for line in fh:
            m = re.match(r"^### 4\.\d+ .*（`([a-z_]+)`）", line)
            if m:
                atlas = m.group(1)
                out.setdefault(atlas, {})
                continue
            if atlas is None:
                continue
            m = re.match(r"^\|\s*(\d{3})\s*\|\s*(\d+)x(\d+)\s*\|\s*(\d+)x(\d+)\s*\|", line)
            if m:
                out[atlas][int(m.group(1))] = (int(m.group(4)), int(m.group(5)))
    return out


def main() -> int:
    dry = "--dry-run" in sys.argv
    if not os.path.isdir(SRC):
        print("source dir missing: " + SRC)
        return 2
    published = index_sizes()
    written = skipped = reset_meta = 0
    bad = 0
    rows = []

    for purpose, atlas, frame, why in SPRITES:
        src = source_file(atlas, frame)
        if src is None:
            print("!! source frame missing: %s/%s_sprite_%03d.png" % (SRC, atlas, frame))
            bad += 1
            continue
        with Image.open(src) as im0:
            im = im0.convert("RGBA")
            bb = bbox_of(im)
            if bb is None:
                print("!! frame fully transparent: %s %d" % (atlas, frame))
                bad += 1
                continue
            crop = im.crop(bb)
            w, h = crop.size

        pub = published.get(atlas, {}).get(frame)
        cross = "n/a"
        if pub is not None:
            cross = "MATCH" if pub == (w, h) else "MISMATCH"
            if cross == "MISMATCH":
                print("!! bbox %dx%d disagrees with 策划/原版UI素材索引.md %dx%d for %s %d"
                      % (w, h, pub[0], pub[1], atlas, frame))
                bad += 1
                continue

        rel = "%s/%s/frame_%03d.png" % (purpose, atlas, frame)
        dst = os.path.join(DST, rel.replace("/", os.sep))
        same = False
        if os.path.exists(dst):
            with Image.open(dst) as old:
                same = old.convert("RGBA").tobytes() == crop.tobytes()
        if same:
            skipped += 1
        elif not dry:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            crop.save(dst, format="PNG")
            stale = dst + ".meta"
            if os.path.exists(stale):
                os.remove(stale)
                reset_meta += 1
            written += 1
            with Image.open(dst) as chk:
                if chk.convert("RGBA").tobytes() != crop.tobytes():
                    print("!! pixel mismatch after write: " + rel)
                    bad += 1
        else:
            written += 1
        rows.append((purpose, atlas, frame, rel, (bb[0], bb[1], w, h), cross, why))

    if bad:
        print("!! %d problem(s) - nothing else trusted" % bad)
        return 3

    print("referenced : %d frames (cropped to their own alpha>%d bbox, 1:1, no resample)"
          % (len(rows), ALPHA_MIN))
    print("this run   : written=%d  already identical=%d  stale .meta dropped=%d%s"
          % (written, skipped, reset_meta, "  (dry-run)" if dry else ""))
    print()
    print("| purpose | source atlas | src frame | crop x,y,w,h | size vs 索引 | "
          "project path (Assets/Resources/Sprites/Ui/...) | why G1 needs it |")
    print("|---|---|---|---|---|---|---|")
    for purpose, atlas, frame, rel, bb, cross, why in rows:
        print("| %s | %s | %d | %d,%d,%d,%d | %s | %s | %s |"
              % (purpose, atlas, frame, bb[0], bb[1], bb[2], bb[3], cross, rel, why))
    return 0


if __name__ == "__main__":
    sys.exit(main())
