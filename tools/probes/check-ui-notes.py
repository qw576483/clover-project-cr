# -*- coding: utf-8 -*-
"""Cross-check the wording of the T4 frame-identification notes against the
MACHINE-MEASURED bounding box in the manifest.

WHY THIS FILE EXISTS (judgement asset, committed)
    The summary sheets built by `ui-index.ps1` render every frame scaled up to fill
    its cell, which destroys the aspect ratio -- so writing the notes from the sheet
    alone reliably produces mis-reads.  Measured on T4c's 292 new rows: 27 of them
    were wrong in exactly this way (a 24x24 frame called a "宽扁横条", a 247x56 frame
    called a "细竖条", a 1x1 single-colour pixel called a "圆角方块 / 渐变块").
    This checker turns "did I read the shape right?" into a command, so the notes
    stay consistent with the only objective record of each frame.
    Run it after editing any `t4-desc-*.tsv`; `flagged = 0` is the pass condition.

WHAT IT CHECKS
    For every note row, the frame's bbox (w,h) from
    `.ai-tmp/screenshots/ui-index-manifest.tsv` must agree with the direction words
    used in the description:
      * 横条 / 横线 / 宽扁 / 横向 / 宽） require w > 1.5*h
      * 竖条 / 竖线 / 纵向 / 竖长          require h > 1.5*w
      * 方块                                requires 0.67 <= w/h <= 1.5
      * bbox area <= 4 ("one solid pixel")  requires the words 纯色像素
    It also reports notes for frames that have no manifest row at all.

SCOPE
    By default this checks only the note files T4c wrote (`ui_battle_end_out` p01-p03
    and `ui_arena_out` p02) and the exit code comes from those.
    `--all` additionally checks every `t4-desc-*.tsv`, including the `ui_out` p03-p10
    files written by an earlier slice with looser wording ("方块" used for any small
    block, not just a square one).  Measured 2026-09-20: the T4c files are clean
    (`flagged = 0`); the full run reports 161 hits, essentially all in those earlier
    files.  They are NOT silently exempted here -- `--all` prints them -- but
    rewriting another slice's eyeball notes off a wording heuristic is not this
    tool's call, so the default scope is deliberate.

USAGE
    python tools/probes/check-ui-notes.py            # exit 0 iff the T4c notes are clean
    python tools/probes/check-ui-notes.py --all      # informational: every note file
"""
from __future__ import annotations

import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
MANIFEST = os.path.join(ROOT, ".ai-tmp", "screenshots", "ui-index-manifest.tsv")

WIDE = ("横条", "横线", "宽扁", "横向", "宽）")
TALL = ("竖条", "竖线", "纵向", "竖长")
SQUARE = "方块"
TINY = "纯色像素"


def main() -> int:
    if not os.path.isfile(MANIFEST):
        print("manifest missing: " + MANIFEST)
        return 2

    bbox = {}
    with open(MANIFEST, encoding="utf-8") as fh:
        fh.readline()
        for line in fh:
            c = line.rstrip("\r\n").split("\t")
            if len(c) >= 10:
                bbox[(c[0], int(c[1]))] = (int(c[7]), int(c[8]))

    everything = "--all" in sys.argv
    all_paths = sorted(glob.glob(os.path.join(ROOT, ".ai-tmp", "test", "t4-desc-*.tsv")))
    if everything:
        paths = all_paths
    else:
        paths = [p for p in all_paths
                 if "ui_battle_end_out" in os.path.basename(p)
                 or os.path.basename(p).startswith("t4-desc-ui_arena_out-p02")]
    print("scope: %s (%d file(s))" % ("ALL note files" if everything else "T4c note files",
                                      len(paths)))

    flagged = 0
    checked = 0
    for path in paths:
        with open(path, encoding="utf-8") as fh:
            for ln, line in enumerate(fh, 1):
                c = line.rstrip("\r\n").split("\t")
                if len(c) < 4:
                    continue
                try:
                    key = (c[0], int(c[1]))
                except ValueError:
                    print("BAD FRAME NUMBER %s:%d %s" % (os.path.basename(path), ln, c[1]))
                    flagged += 1
                    continue
                checked += 1
                if key not in bbox:
                    print("NO MANIFEST ROW %s:%d %s %s" % (os.path.basename(path), ln, c[0], c[1]))
                    flagged += 1
                    continue
                w, h = bbox[key]
                # two accepted layouts (same as tools/probes/build-ui-index.py):
                #   4/5 cols: dir frame desc use [status]
                #   6 cols  : dir frame frame desc use status  (the legacy t4-runs.tsv)
                desc = c[3] if len(c) >= 6 else c[2]
                why = None
                if any(t in desc for t in WIDE) and not (w > h * 1.5):
                    why = "says wide but bbox is %dx%d" % (w, h)
                if any(t in desc for t in TALL) and not (h > w * 1.5):
                    why = "says tall but bbox is %dx%d" % (w, h)
                if SQUARE in desc and not (0.67 <= float(w) / h <= 1.5):
                    why = "says square block but bbox is %dx%d" % (w, h)
                if w * h <= 4 and TINY not in desc:
                    why = "bbox is %dx%d (one solid pixel) but the note does not say %s" % (w, h, TINY)
                if why:
                    flagged += 1
                    print("%s %s frame %s: %s  ->  %s"
                          % (os.path.basename(path), c[0], c[1], desc, why))

    print("checked = %d  flagged = %d" % (checked, flagged))
    return 0 if flagged == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
