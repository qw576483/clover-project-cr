# CR-V1: calibrate an arena GROUND TEXTURE (level_*_arena_tex{,_}.png / arena_training_tex{,_}.png)
# into a tile grid: find the two tan lane bands -> px/tile(x) -> tile-0 origin; plus per-row
# tan/grass/water statistics.  Read-only; writes nothing but stdout.
#
#   python tools/probes/cr-v1-calib-tex_.py --img <path> --lanes-tiles 11 --width-tiles 18
#   python tools/probes/cr-v1-calib-tex_.py --img <path>              # stats only, NO ruler
#   python tools/probes/cr-v1-calib-tex_.py --selftest                # positive control (see below)
#   python tools/probes/cr-v1-calib-tex_.py --img <path> --lanes-tiles 11 --scale 1.5
#                                                                     # P1: px/tile must scale with
#                                                                     # --scale, tile values must not
#
# !! WHY IT IS PARAMETERISED (2026-09-23) !!
# The first version hard-coded TWO cross-image assumptions: (a) the input path and (b) the ruler --
# "the two tan lanes are 11 tiles apart" and "the field is 18 tiles wide".  CR-V1 subsequently
# measured all 26 arena textures: 24 DISTINCT widths (244..1404) and 24 DISTINCT heights, and
# arena_training_tex_ (850x1178) is NOT the same carrier as the sprite frames (1090x1677).
# => there is NO "every texture is 18 tiles wide" invariant, so the ruler must NEVER be assumed
#    across images.  It is either given explicitly (--lanes-tiles) or it does not exist: a run
#    without --lanes-tiles prints NO ruler and exits 2.  That is, mechanically, what "the texture
#    side has no positive control" means -- and it is why the 23 other arenas must not be given
#    numbers until this control passes.
#
# POSITIVE CONTROL (--selftest): builds synthetic images IN MEMORY using the real measured colour
# families, and requires the logic to recover a KNOWN grid.  Three cases, all must behave:
#   P  lanes at tiles 3.5/14.5 on a 20 px/tile grid -> must report ppt=20.000 and tile-0 at x=0
#   N1 same lane layout, image scaled 2x (40 px/tile) -> must report ppt=40.000 (NOT 20): it measures
#   N2 no lane bands at all -> must FAIL loudly (never fall back to a default ruler)
# exits 0 only if all three behave.  Run --selftest BEFORE trusting any number on a real texture.
#
# TWO MORE REFUSALS (both mechanical, both added after CR-U5R's run of 24 textures):
#   * ORIENTATION -- 轴向未立 ⇒ NOT-JUDGED.  A row-based river thickness is only comparable to the
#     reference frame if the water in THIS carrier runs horizontally; if the carrier shows a vertical
#     water strip, a mechanical 0 must NOT be read as "this arena has no river".
#   * river thickness window RIVER_MIN..RIVER_MAX (0.1..6.0 tiles, = CR-U5R's pre-registered Q3):
#     outside it the line prints **NOT-JUDGED** instead of a number, and water rows are counted
#     against the NON-BLACK width (the black border is unused texture, not field).
import os, sys
import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SC = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc")

# colour families measured on the real assets (used by the control so it tests the REAL thresholds)
C_GRASS = (162, 187, 75)
C_TAN = (208, 150, 117)
C_WATER = (4, 136, 147)
RIVER_MIN, RIVER_MAX = 0.1, 6.0          # CR-U5R's pre-registered Q3 window, in tiles


def river_thickness(water, ppt, nbw):
    """ONE implementation of the Q3 window (the checker imports this, so there is no second one).
    returns (tiles_or_None, row_count); None == outside the window == NOT-JUDGED, never a number."""
    wrows = int(((water.sum(axis=1) > nbw * 0.5)).sum())
    t = wrows / float(ppt)
    return (t if RIVER_MIN <= t <= RIVER_MAX else None), wrows


def masks(a):
    r, g, b = a[:, :, 0].astype(np.int16), a[:, :, 1].astype(np.int16), a[:, :, 2].astype(np.int16)
    tan = (r > 175) & (r - b > 45) & (g > 130) & (r - g > 15) & (r - g < 75)
    grass = (g > 110) & (g - r > 5) & (g - b > 40) & (b < 140)
    water = (b - r > 30) & (b > 90)
    return tan, grass, water


def lane_runs(tan, H, W, min_w=10, frac=0.25):
    prof = tan.sum(axis=0)
    runs, c = [], None
    for x in range(W):
        if prof[x] > H * frac:
            c = [x, x] if c is None else [c[0], x]
        else:
            if c and c[1] - c[0] > min_w:
                runs.append((c[0], c[1], c[1] - c[0] + 1, (c[0] + c[1]) // 2))
            c = None
    if c and c[1] - c[0] > min_w:
        runs.append((c[0], c[1], c[1] - c[0] + 1, (c[0] + c[1]) // 2))
    return runs


def ruler_from(runs, lanes_tiles, width_tiles):
    """returns (ppt, x0, msg) or (None, None, msg) -- never invents a ruler."""
    if lanes_tiles is None:
        return None, None, "no --lanes-tiles given => ruler NOT established (assumption refused)"
    if len(runs) < 2:
        return None, None, "fewer than 2 lane bands found (%d) => ruler NOT established" % len(runs)
    c1, c2 = runs[0][3], runs[-1][3]
    ppt = (c2 - c1) / float(lanes_tiles)
    x0 = c1 - 3.5 * ppt
    msg = "lane centres %d / %d ; %.3f px/tile (x) from --lanes-tiles=%.3f" % (c1, c2, ppt, lanes_tiles)
    if width_tiles:
        msg += " ; tile %d <=> x %.1f" % (width_tiles, x0 + width_tiles * ppt)
    return ppt, x0, msg


def orientation_note(water, W, H):
    """ALWAYS returns exactly one line.  Why always: if the success path stayed silent, then
    "no ORIENTATION line" would be indistinguishable from "this branch was never reached" -- the
    "读不到 vs 成功 无法区分" family.  With a line on every outcome, the absence of a line can only
    mean the tool did not get that far.  (Requested by team-lead, item B, after CR-U5R's run.)"""
    if not water.any():
        return ("ORIENTATION: no water pixels detected => water axis NOT APPLICABLE"
                " => any river-thickness reading here is NOT-JUDGED (this is NOT 'no river in game')")
    wy, wx = np.where(water)
    ww, wh = int(wx.max() - wx.min() + 1), int(wy.max() - wy.min() + 1)
    if ww < 0.25 * W and wh > 0.5 * H:
        return ("ORIENTATION: water is a VERTICAL strip (%dx%d) => a row-based river thickness on THIS"
                " carrier is NOT comparable to the frame's horizontal river => judge NOT-JUDGED,"
                " do NOT read it as 'this arena has no river'" % (ww, wh))
    if wh < 0.25 * H and ww > 0.5 * W:
        return ("ORIENTATION: water is a HORIZONTAL band (%dx%d) => comparable to the frame"
                " (same axis): row-based river thickness IS usable" % (ww, wh))
    return ("ORIENTATION: water bbox %dx%d is neither a clear horizontal band nor a clear vertical"
            " strip => axis NOT established => NOT-JUDGED" % (ww, wh))


def report(path, lanes_tiles=None, width_tiles=None, scale=1.0, resample="nearest"):
    """scale != 1.0 resamples the bitmap first.  That is what the judge's P1 (scale invariance) needs:
    the pixel numbers must move with the magnification while the TILE-space numbers must not.
    Default resample = NEAREST on purpose: it preserves the exact colour values, so the classifier
    thresholds are exercised identically and only the geometry changes."""
    if not os.path.isfile(path):
        print("INPUT MISSING -> NOT-JUDGED: %s" % path)
        return 2
    if abs(scale - 1.0) > 1e-9:
        im = Image.open(path).convert("RGB")
        w0, h0 = im.size
        rw, rh = int(round(w0 * scale)), int(round(h0 * scale))
        filt = {"nearest": Image.NEAREST, "bilinear": Image.BILINEAR}.get(resample, Image.NEAREST)
        im = im.resize((rw, rh), filt)
        a = np.asarray(im).astype(np.int16)
        print("SCALE %.3f via %s : %dx%d -> %dx%d" % (scale, resample, w0, h0, rw, rh))
    else:
        a = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    H, W = a.shape[:2]
    print("img %s  %dx%d  (accepts ANY arena texture: no size assumption)" % (os.path.basename(path), W, H))
    tan, grass, water = masks(a)
    for k, m in (("tan", tan), ("grass", grass), ("water", water)):
        if m.sum() == 0:
            print("  %-6s 0" % k)
            continue
        yy, xx = np.where(m)
        print("  %-6s px=%-8d bbox x %d..%d y %d..%d mean RGB%s"
              % (k, int(m.sum()), xx.min(), xx.max(), yy.min(), yy.max(),
                 tuple(int(a[:, :, i][m].mean()) for i in range(3))))
    # non-black bbox (unused/black border) -- needed by the judge's Q2 sanity ratio
    lum0 = (a[:, :, 0] + a[:, :, 1] + a[:, :, 2]) > 24
    if lum0.any():
        ys, xs = np.where(lum0)
        bx0, bx1, by0, by1 = xs.min(), xs.max(), ys.min(), ys.max()
        print("non-black bbox x %d..%d y %d..%d  (w=%d h=%d)" % (bx0, bx1, by0, by1, bx1 - bx0 + 1, by1 - by0 + 1))

    runs = lane_runs(tan, H, W)
    print("lane column runs (x0,x1,w,cx) = %s" % runs)
    ppt, x0, msg = ruler_from(runs, lanes_tiles, width_tiles)
    if ppt is None:
        print("RULER NOT ESTABLISHED -> %s  => no tile-space number is produced (exit 2)" % msg)
        print(orientation_note(water, W, H) + "   [orientation evaluated even though the ruler failed]")
        return 2
    print("RULER %s  => tile-0 <=> x %.1f" % (msg, x0))
    # ---- tile-space block: pure aggregation of the masks above + the ruler (no new method) ----
    # Row basis = the NON-BLACK width (the black border is unused texture, not field).  With W = full
    # image width this line emitted NUMBERS THAT ARE NOT RIVERS: a right-side water strip gave 0 rows,
    # and a whole-water arena (spell_) gave 1024 rows => "river thickness 22.4 tiles".  CR-U5R's
    # pre-registered Q3 window is [0.1, 6.0] tiles, so anything outside it is emitted as NOT-JUDGED,
    # never as a number.  (Reported by CR-U5R; fixed here by CR-V1 on team-lead's instruction.)
    nbw = bx1 - bx0 + 1
    thick, wrows = river_thickness(water, ppt, nbw)
    if thick is not None:
        print("TILE-SPACE  px/tile=%.3f | water-rows=%d (basis = non-black width %d) => river thickness"
              " %.3f tile | non-black width %.2f tile" % (ppt, wrows, nbw, thick, nbw / ppt))
    else:
        print("TILE-SPACE  px/tile=%.3f | water-rows=%d (basis = non-black width %d) => river thickness"
              " **NOT-JUDGED** (%.3f tile outside [%.1f,%.1f]) | non-black width %.2f tile"
              % (ppt, wrows, nbw, wrows / ppt, RIVER_MIN, RIVER_MAX, nbw / ppt))
    # ---- MECHANICAL orientation guard --------------------------------------------------------
    # A "river row count" is only comparable to the reference FRAME if the water in THIS carrier
    # runs horizontally.  arena_training_tex_ has its water as a VERTICAL strip (x800..849 of 850),
    # so the row count is mechanically 0 there.  Printing the orientation keeps a mechanical 0 from
    # being read as "this arena has no river".  Measured on the water mask, not assumed.
    # ALWAYS prints exactly one line, including the no-water and the success paths -- so "no
    # ORIENTATION line" can only mean "the tool did not get this far" (team-lead item B).
    print(orientation_note(water, W, H))
    print("            (P1 scale-invariance: px/tile MUST move with the magnification, the tile values MUST NOT)")
    return 0


def _synth(ppt, with_lanes=True, tile_w=18, tile_h=32):
    """synthetic ground: grass everywhere, tan lane bands at tiles 3.5 and 14.5 (1 tile wide)."""
    W, H = int(tile_w * ppt), int(tile_h * ppt)
    img = np.zeros((H, W, 3), dtype=np.uint8)
    img[:, :] = C_GRASS
    if with_lanes:
        for centre in (3.5, 14.5):
            x0 = int(round((centre - 0.5) * ppt))
            img[:, x0:x0 + int(round(ppt))] = C_TAN
    return img


def selftest():
    ok = True
    print("SELFTEST: the ruler logic must recover a KNOWN grid before any real texture gets numbers")
    # P: positive
    a = _synth(20.0)
    runs = lane_runs(masks(a)[0], a.shape[0], a.shape[1])
    ppt, x0, msg = ruler_from(runs, 11, 18)
    g = (ppt is not None) and abs(ppt - 20.0) < 0.6 and abs(x0 - 0.0) < 6
    ok &= g
    print("  [P ] 20 px/tile  runs=%s  => ppt=%s x0=%s  %s" % (runs, None if ppt is None else round(ppt, 3),
                                                              None if x0 is None else round(x0, 1), "PASS" if g else "FAIL"))
    # N1: same layout scaled 2x -> the ruler must MEASURE (40), not echo 20
    a = _synth(40.0)
    runs = lane_runs(masks(a)[0], a.shape[0], a.shape[1])
    ppt, x0, msg = ruler_from(runs, 11, 18)
    g = (ppt is not None) and abs(ppt - 40.0) < 1.0
    ok &= g
    print("  [N1] 40 px/tile  runs=%s  => ppt=%s          %s" % (runs, None if ppt is None else round(ppt, 3),
                                                               "PASS" if g else "FAIL"))
    # N2: no lanes -> must refuse, not default
    a = _synth(20.0, with_lanes=False)
    runs = lane_runs(masks(a)[0], a.shape[0], a.shape[1])
    ppt, x0, msg = ruler_from(runs, 11, 18)
    g = (ppt is None) and (ruler_from(runs, None, 18)[0] is None)
    ok &= g
    print("  [N2] no lanes    runs=%s  => ppt=%s           %s (must refuse)" % (runs, ppt, "PASS" if g else "FAIL"))
    # N3: lanes present but --lanes-tiles NOT given -> must refuse to assume 11
    a = _synth(20.0)
    runs = lane_runs(masks(a)[0], a.shape[0], a.shape[1])
    g = ruler_from(runs, None, 18)[0] is None
    ok &= g
    print("  [N3] no --lanes-tiles => ppt=%s              %s (assumption refused)"
          % (ruler_from(runs, None, 18)[0], "PASS" if g else "FAIL"))
    print("SELFTEST RESULT: %s" % ("PASS (the ruler measures, and it refuses to assume)" if ok else "FAIL"))
    return 0 if ok else 1


def main(argv):
    if "--selftest" in argv:
        return selftest()
    img = None
    lanes = None
    width = None
    scale = 1.0
    resample = "nearest"
    for i, v in enumerate(argv):
        if v == "--img" and i + 1 < len(argv):
            img = argv[i + 1]
        if v == "--lanes-tiles" and i + 1 < len(argv):
            lanes = float(argv[i + 1])
        if v == "--width-tiles" and i + 1 < len(argv):
            width = int(argv[i + 1])
        if v == "--scale" and i + 1 < len(argv):
            scale = float(argv[i + 1])
        if v == "--resample" and i + 1 < len(argv):
            resample = argv[i + 1]
    if img is None:
        img = os.path.join(SC, "arena_training_tex_.png")
        print("NOTE: no --img given -> defaulting to the TRAINING texture only; the ruler still needs"
              " --lanes-tiles, because 11/18 is a training-arena fact, not a cross-image invariant")
    return report(img, lanes, width, scale, resample)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
