#!/usr/bin/env python3
"""T3 judged asset - offline check of sprite frame ORDER and per-frame PIVOT anchor.

Why this exists: two of the five root causes of the "unit model jitters" report are
pure resource/import facts, so they can be judged WITHOUT Unity and WITHOUT Play mode
(seconds, not minutes). This script reads the .meta files that Unity itself wrote, so it
is ground truth for what the importer produced - it does not trust any C# code.

  cause 2 (frame order): the importer names each sub-sprite "frame_000_0"
      (see `.meta` -> spriteSheet.sprites[0].name). The old parser took the digits at
      the END of the name, which is always "0" for that shape => every frame had the
      same sort key => the sort was a no-op => playback order was whatever
      Resources.LoadAll happened to return. This script prints the distinct-key count
      for BOTH rules so the defect and its fix are visible in one line.
  cause 4 (per-frame pivot): every sub-sprite's pivot is the centre of its OWN crop
      rect, and the crop rects differ per frame => the body shifts on every frame change.
      A fix must give every frame in a directory the SAME texture-space anchor. This
      script prints the per-frame anchor (rect.xy + pivot) for the first/last 5 frames,
      plus the two candidate shared anchors (union bottom-centre vs union centre) that a
      rebuild can use.

Usage:
  python tools/probes/check_frame_order.py [sprite_dir]
  default sprite_dir = chr_knight_out (486 frames; the directory named in the task book)

Output is ASCII only on purpose: this script must stay readable in any console code page.
Exit code 0 = the directory was checked (regardless of verdict); 2 = directory not found.
"""

import os
import re
import sys

# Repo root = two levels up from this file (tools/probes/check_frame_order.py).
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SPRITE_ROOT = os.path.join(ROOT, "client", "Assets", "Resources", "Sprites")
AREAS = ("Units", "Buildings", "Arenas", "Towers")

# [A-Za-z0-9_] because a Unity sprite name may be "ArenaSeg.1" etc.; the parse only
# cares about the underscore-separated numeric segments.
NAME_RE = re.compile(r"^\s*name:\s*(\S+)\s*$", re.M)
RECT_RE = re.compile(
    r"^\s*name:\s*(\S+)\s*\n"          # the sub-sprite name
    r"\s*rect:\s*\n"
    r"\s*serializedVersion:\s*\d+\s*\n"
    r"\s*x:\s*(-?\d+)\s*\n"
    r"\s*y:\s*(-?\d+)\s*\n"
    r"\s*width:\s*(-?\d+)\s*\n"
    r"\s*height:\s*(-?\d+)\s*\n",
    re.M,
)
PPU_RE = re.compile(r"^\s*spritePixelsToUnits:\s*([\d.]+)\s*$", re.M)


def second_last_numeric(name):
    """Fixed rule: take the second-to-last purely numeric underscore segment.

    frame_000_0 -> 0 ; frame_7_1 -> 7 ; frame_003 -> 3 ; gen_frame_012 -> 12 ;
    White1x1 -> -1 (no '_' numeric segment) ; ArenaSeg -> -1
    """
    if not name:
        return -1
    segs = name.split("_")
    for i in range(len(segs) - 2, -1, -1):
        if segs[i].isdigit():
            return int(segs[i])
    if segs and segs[-1].isdigit():
        return int(segs[-1])
    return -1


def tail_numeric(name):
    """Old (broken) rule: digits at the very end of the name."""
    if not name:
        return -1
    digits = 0
    for ch in reversed(name):
        if ch.isdigit():
            digits += 1
        else:
            break
    return int(name[len(name) - digits:]) if digits else -1


def png_size(path):
    """Read width/height straight out of the PNG IHDR chunk (stdlib only, no Pillow)."""
    with open(path, "rb") as fh:
        head = fh.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n" or head[12:16] != b"IHDR":
        return None
    w = int.from_bytes(head[16:20], "big")
    h = int.from_bytes(head[20:24], "big")
    return w, h


def find_dir(sprite_dir):
    hits = []
    for area in AREAS:
        d = os.path.join(SPRITE_ROOT, area, sprite_dir)
        if os.path.isdir(d):
            hits.append(d)
    return hits


def check(path):
    metas = sorted(f for f in os.listdir(path) if f.endswith(".png.meta"))
    frames = []
    ppu = None
    for m in metas:
        text = open(os.path.join(path, m), encoding="utf-8").read()
        mm = RECT_RE.search(text)
        if not mm:
            print("  !! no sprite rect found in %s" % m)
            continue
        if ppu is None:
            p = PPU_RE.search(text)
            ppu = float(p.group(1)) if p else None
        frames.append(
            {"meta": m, "name": mm.group(1),
             "x": int(mm.group(2)), "y": int(mm.group(3)),
             "w": int(mm.group(4)), "h": int(mm.group(5))}
        )

    print("dir            = %s" % path)
    print("png.meta count = %d   (sprites parsed = %d)   ppu = %s" % (len(metas), len(frames), ppu))
    if not frames:
        return

    # Canvas check: a union anchor is only meaningful if every frame really is a crop of
    # ONE shared canvas. Print it instead of assuming it.
    sizes = {}
    for f in frames:
        s = png_size(os.path.join(path, f["meta"][:-5]))   # strip the ".meta" suffix
        sizes.setdefault(s, []).append(f["name"])
    print("canvas size(s) = %s" % ", ".join("%sx%s (%d frames)" % (k[0], k[1], len(v))
                                            for k, v in sizes.items() if k))

    # --- frame ORDER (cause 2) -------------------------------------------------
    by_new = sorted(frames, key=lambda f: second_last_numeric(f["name"]))
    keys_new = [second_last_numeric(f["name"]) for f in by_new]
    keys_old = set(tail_numeric(f["name"]) for f in frames)
    print("")
    print("[cause 2] frame order")
    print("  sortedFirst5 (by fixed rule) = %s" % " ".join(f["name"] for f in by_new[:5]))
    print("  sortedLast5  (by fixed rule) = %s" % " ".join(f["name"] for f in by_new[-5:]))
    print("  indexFirst5 = %s   indexLast5 = %s" % (keys_new[:5], keys_new[-5:]))
    print("  distinctKeys new(second-last) = %d   old(tail) = %d  <-- old == 1 means the sort was a NO-OP"
          % (len(set(keys_new)), len(keys_old)))
    print("  strictlyIncreasing = %s" % all(keys_new[i] > keys_new[i - 1] for i in range(1, len(keys_new))))

    # --- per-frame anchor (cause 4) -------------------------------------------
    # Unity's importer sets pivot = centre of each sprite's own rect (alignment 0).
    # Textures of one directory share the same canvas (checked below), so the anchor in
    # TEXTURE pixels for frame i is (rect.x + w/2, rect.y + h/2).
    x0 = min(f["x"] for f in frames)
    y0 = min(f["y"] for f in frames)
    x1 = max(f["x"] + f["w"] for f in frames)
    y1 = max(f["y"] + f["h"] for f in frames)
    print("")
    print("[cause 4] per-frame pivot / shared anchor")
    print("  union rect (canvas px) = x=%d y=%d w=%d h=%d" % (x0, y0, x1 - x0, y1 - y0))
    print("  candidate anchor A (union bottom-centre) = (%.1f, %.1f)" % ((x0 + x1) / 2.0, float(y0)))
    print("  candidate anchor B (union centre)        = (%.1f, %.1f)" % ((x0 + x1) / 2.0, (y0 + y1) / 2.0))
    print("  rect.y range = %d..%d   rect.x range = %d..%d"
          % (min(f["y"] for f in frames), max(f["y"] for f in frames),
             min(f["x"] for f in frames), max(f["x"] for f in frames)))
    sample = by_new[:5] + by_new[-5:]
    print("  per-frame anchor of 10 frames (imported = own rect centre; MUST differ => the shift):")
    for f in sample:
        ax = f["x"] + f["w"] / 2.0
        ay = f["y"] + f["h"] / 2.0
        print("    idx=%-4s name=%-14s rect=(%d,%d,%d,%d) anchor=(%.1f,%.1f)"
              % (second_last_numeric(f["name"]), f["name"], f["x"], f["y"], f["w"], f["h"], ax, ay))
    axs = set((f["x"] + f["w"] / 2.0, f["y"] + f["h"] / 2.0) for f in frames)
    print("  distinct anchors across ALL %d frames = %d  <-- >1 is exactly the per-frame shift"
          % (len(frames), len(axs)))
    spread_x = max(a[0] for a in axs) - min(a[0] for a in axs)
    spread_y = max(a[1] for a in axs) - min(a[1] for a in axs)
    print("  anchor spread = %.1f px x %.1f px  (= %.3f x %.3f tiles at ppu=%s)"
          % (spread_x, spread_y, spread_x / (ppu or 100.0), spread_y / (ppu or 100.0), ppu))
    print("")
    print("  note: Sprite.Create's pivot argument is RECT-RELATIVE normalized (proved by")
    print("        FlowProbe.PivotSemantics, not from memory), so a shared TEXTURE anchor")
    print("        (ax, ay) is expressed per frame as ((ax-x)/w, (ay-y)/h).")


def main():
    sprite_dir = sys.argv[1] if len(sys.argv) > 1 else "chr_knight_out"
    dirs = find_dir(sprite_dir)
    if not dirs:
        print("directory not found under %s : %s" % (SPRITE_ROOT, sprite_dir))
        return 2
    for d in dirs:
        check(d)
    return 0


if __name__ == "__main__":
    sys.exit(main())
