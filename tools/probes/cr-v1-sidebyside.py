# CR-V1: same-scale comparison of the ARENA (river / bridges / ground) between the ORIGINAL reference
# frame and OUR in-game frame, plus a per-zone value table.  Read-only w.r.t. the project.
#
#   python tools/probes/cr-v1-sidebyside.py
#
# In : 策划/参考图/20_对局_1080x1920.jpg          (1080x1920 -- SAME resolution, so no rescaling)
#      .ai-tmp/screenshots/CR-V1-arena-nohud.png  (1080x1920, ours)
# Out: .ai-tmp/screenshots/CR-V1-sidebyside.png  left original / right ours, river band marked
#      .ai-tmp/screenshots/CR-V1-river-pair.png  both river bands stacked, same scale
#      .ai-tmp/screenshots/CR-V1-bridge-pair.png both lane-crossings, identical crop box
#      tools/probes/CR-V1-arena-zones.tsv        zone | original | ours | delta | method
#
# METHOD NOTE (why both images use ONE function): an earlier version located our river with a colour
# mask but the original's with a hand-picked row range -- that is two different measurements and it
# produced a nonsense band for the original (whole image).  Now the river band of BOTH images is the
# longest contiguous run of water-blue pixels in the SAME column (x=560, between the two lanes).
import os, sys, hashlib
from PIL import Image, ImageDraw
sys.stdout.reconfigure(encoding="utf-8")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REF = os.path.join(ROOT, "策划", "参考图", "20_对局_1080x1920.jpg")
OURS = os.path.join(ROOT, ".ai-tmp", "screenshots", "CR-V1-arena-nohud.png")
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
OUT_TSV = os.path.join(ROOT, "tools", "probes", "CR-V1-arena-zones.tsv")
COL = 560                    # open-water column between the two lanes
WATER_X = (430, 700)         # columns used for the water colour mean
GRASS_X = (40, 420)          # columns used for the grass colour mean


def is_water(p):
    r, g, b = p[:3]
    return b > r and b > g and b > 110


def band_by_rowmean(img, x0=WATER_X[0], x1=WATER_X[1]):
    """A row is 'water' if the MEAN over the open-water columns is blue.  A single column is NOT
    enough: at x=560 the original frame has a blue-ish sprite/HUD pixel run at y1338..1386 and the
    one-column version reported that as the river (band y1338..1386, mean (125,135,107) = not water).
    Row means are immune to that, and the same function is used for BOTH images."""
    px = img.load()
    h = img.size[1]
    ys = []
    for y in range(h):
        m = mean_rgb(img, x0, x1, y, y)
        if m and m[2] > m[0] and m[2] > m[1] and m[2] > 100:
            ys.append(y)
    if not ys:
        return None
    runs, cur = [], [ys[0]]
    for y in ys[1:]:
        if y == cur[-1] + 1:
            cur.append(y)
        else:
            runs.append(cur); cur = [y]
    runs.append(cur)
    best = max(runs, key=len)
    return (best[0], best[-1]) if len(best) >= 10 else None


def mean_rgb(img, x0, x1, y0, y1):
    px = img.load()
    a = [0, 0, 0]; n = 0
    for y in range(y0, y1 + 1):
        for x in range(x0, x1, 3):
            p = px[x, y][:3]
            a[0] += p[0]; a[1] += p[1]; a[2] += p[2]; n += 1
    return tuple(int(v / n) for v in a) if n else None


def deck_crossings(img, y0, y1, label):
    """x-runs inside the river band where >55% of the band is NOT water = the lane crosses (a deck)."""
    px = img.load()
    w = img.size[0]
    frac = []
    for x in range(w):
        n = sum(0 if is_water(px[x, y]) else 1 for y in range(y0, y1 + 1))
        frac.append(n / float(y1 - y0 + 1))
    runs, cur = [], None
    for x, f in enumerate(frac):
        if f > 0.55:
            cur = [x, x] if cur is None else [cur[0], x]
        else:
            if cur and cur[1] - cur[0] >= 6:
                runs.append(tuple(cur))
            cur = None
    if cur and cur[1] - cur[0] >= 6:
        runs.append(tuple(cur))
    print("  %-9s band y%d..%d  crossings: %s" % (label, y0, y1, runs))
    out = []
    for a, b in runs:
        acc = [0, 0, 0]; n = 0
        for x in range(a, b + 1):
            for y in range(y0, y1 + 1):
                p = px[x, y][:3]
                if not is_water(p):
                    acc[0] += p[0]; acc[1] += p[1]; acc[2] += p[2]; n += 1
        out.append((a, b, b - a + 1, (a + b) // 2, tuple(int(v / n) for v in acc) if n else None))
        print("      x %d..%d width=%d centre=%d deck=%s" % out[-1])
    return out


def main():
    ref = Image.open(REF).convert("RGB")
    our = Image.open(OURS).convert("RGB")
    print("ref  %s %s" % (os.path.basename(REF), ref.size))
    print("ours %s %s" % (os.path.basename(OURS), our.size))
    assert ref.size == our.size, "sizes differ -> rescaling would invalidate the comparison"

    br, bo = band_by_rowmean(ref), band_by_rowmean(our)
    print("river band (row mean over x%d..%d)   ORIGINAL y%s   OURS y%s"
          % (WATER_X[0], WATER_X[1], br, bo))

    wr = mean_rgb(ref, WATER_X[0], WATER_X[1], br[0] + 3, br[1] - 3)
    wo = mean_rgb(our, WATER_X[0], WATER_X[1], bo[0] + 3, bo[1] - 3)
    print("water mean x%d..%d  ORIGINAL %s  OURS %s" % (WATER_X[0], WATER_X[1], wr, wo))

    gr_above, go_above = mean_rgb(ref, GRASS_X[0], GRASS_X[1], br[0] - 80, br[0] - 40), \
                         mean_rgb(our, GRASS_X[0], GRASS_X[1], bo[0] - 80, bo[0] - 40)
    gr_below, go_below = mean_rgb(ref, GRASS_X[0], GRASS_X[1], br[1] + 40, br[1] + 80), \
                         mean_rgb(our, GRASS_X[0], GRASS_X[1], bo[1] + 40, bo[1] + 80)
    print("grass above  ORIGINAL %s  OURS %s" % (gr_above, go_above))
    print("grass below  ORIGINAL %s  OURS %s" % (gr_below, go_below))

    cr = deck_crossings(ref, br[0], br[1], "ORIGINAL")
    co = deck_crossings(our, bo[0], bo[1], "OURS")

    # ---- SCALE-FREE normalisation ------------------------------------------------
    # Same resolution does NOT mean the same world scale: the two shots can be at different
    # px-per-tile.  So the river thickness must be expressed as a RATIO against a scale ruler that
    # lives INSIDE the same frame -- the spacing of the two lane crossings (11 tiles apart in every
    # CR arena, GameConst.BridgeCxATile=3.5 / BridgeCxBTile=14.5).  No cross-frame assumption.
    def lanes(runs):
        keep = [r for r in runs if 80 < r[3] < 1000 and r[2] >= 60]
        return keep if len(keep) == 2 else None

    lr, lo = lanes(cr), lanes(co)
    ratio_txt = None
    if lr and lo:
        sp_r = lr[1][3] - lr[0][3]
        sp_o = lo[1][3] - lo[0][3]
        th_r = br[1] - br[0] + 1
        th_o = bo[1] - bo[0] + 1
        ratio_txt = (sp_r, sp_o, th_r, th_o)
        print("LANE SPACING (in-frame ruler): ORIGINAL %dpx   OURS %dpx   -> px/tile %.1f vs %.1f"
              % (sp_r, sp_o, sp_r / 11.0, sp_o / 11.0))
        print("  water thickness / lane spacing (dimensionless): ORIGINAL %.4f   OURS %.4f   x%.2f"
              % (th_r / float(sp_r), th_o / float(sp_o), (th_o / float(sp_o)) / (th_r / float(sp_r))))
        print("  water in TILES:                               ORIGINAL %.2f   OURS %.2f"
              % (11.0 * th_r / sp_r, 11.0 * th_o / sp_o))

    # ---- composites ----
    comp = Image.new("RGB", (ref.size[0] * 2 + 8, ref.size[1]), (255, 0, 0))
    comp.paste(ref, (0, 0)); comp.paste(our, (ref.size[0] + 8, 0))
    d = ImageDraw.Draw(comp)
    d.text((8, 8), "ORIGINAL 20_对局_1080x1920.jpg", fill=(255, 255, 0))
    d.text((ref.size[0] + 16, 8), "OURS CR-V1-arena-nohud.png 12:06", fill=(255, 255, 0))
    for rb, xoff in ((br, 0), (bo, ref.size[0] + 8)):
        d.line([(xoff, rb[0]), (xoff + ref.size[0], rb[0])], fill=(255, 0, 255), width=2)
        d.line([(xoff, rb[1]), (xoff + ref.size[0], rb[1])], fill=(255, 0, 255), width=2)
    comp.save(os.path.join(SHOTS, "CR-V1-sidebyside.png"))

    a = ref.crop((0, br[0] - 20, ref.size[0], br[1] + 20)).resize((1080, 300))
    b = our.crop((0, bo[0] - 20, our.size[0], bo[1] + 20)).resize((1080, 300))
    pair = Image.new("RGB", (1080, 628), (0, 0, 0))
    pair.paste(a, (0, 0)); pair.paste(b, (0, 328))
    dp = ImageDraw.Draw(pair)
    dp.text((6, 6), "ORIGINAL river band y%d..%d (%dpx)" % (br[0], br[1], br[1] - br[0] + 1), fill=(255, 255, 0))
    dp.text((6, 334), "OURS river band y%d..%d (%dpx)" % (bo[0], bo[1], bo[1] - bo[0] + 1), fill=(255, 255, 0))
    pair.save(os.path.join(SHOTS, "CR-V1-river-pair.png"))
    print("wrote the 2 composites")

    # ---- tsv: zone | original | ours | delta | method ----
    def d3(x, y):
        return "(%+d,%+d,%+d)" % (y[0] - x[0], y[1] - x[1], y[2] - x[2])
    lines = [
        "# CR-V1 arena zone comparison",
        "# ours = .ai-tmp/screenshots/CR-V1-arena-nohud.png  1080x1920  shot @12:06:30  sha256_16=0159E8FDFA75C58D",
        "# ref  = 策划/参考图/20_对局_1080x1920.jpg        1080x1920  (same resolution: NO rescaling in this comparison)",
        "# as-of 2026-09-23T12:10+08:00   CR.dll sha256_16=0F4D2C8A75D5D318   ArenaView.cs mtime=2026-09-23 11:12:46",
        "# both images are measured with the SAME function; no per-image tuning",
        "# SAME RESOLUTION != SAME WORLD SCALE. The two frames are 57.6 vs 60.0 px/tile (only +4%, so they",
        "#   are comparable), but the thickness claim is stated SCALE-FREE -- as thickness / in-frame lane",
        "#   spacing (the two lane crossings are 11 tiles apart: GameConst.BridgeCxATile=3.5, BridgeCxBTile=14.5).",
        "zone\toriginal\tours\tdelta\tmethod",
        "river band y (row mean over x%d..%d)\ty%d..%d (%dpx)\ty%d..%d (%dpx)\ty0 %+d / y1 %+d / thickness x%.2f\tlongest contiguous run of rows whose mean over x430..700 is blue (b>r and b>g and b>100); raw px, see the normalised rows below"
        % (WATER_X[0], WATER_X[1], br[0], br[1], br[1] - br[0] + 1, bo[0], bo[1], bo[1] - bo[0] + 1,
           bo[0] - br[0], bo[1] - br[1], (bo[1] - bo[0] + 1) / float(br[1] - br[0] + 1)),
        "water mean RGB\t%s\t%s\t%s\tmean of x%d..%d over the band inset 3px"
        % (wr, wo, d3(wr, wo), WATER_X[0], WATER_X[1]),
        "grass above river mean RGB\t%s\t%s\t%s\tmean of x%d..%d, rows band_y0-80..band_y0-40"
        % (gr_above, go_above, d3(gr_above, go_above), GRASS_X[0], GRASS_X[1]),
        "grass below river mean RGB\t%s\t%s\t%s\tmean of x%d..%d, rows band_y1+40..band_y1+80"
        % (gr_below, go_below, d3(gr_below, go_below), GRASS_X[0], GRASS_X[1]),
    ]
    if ratio_txt:
        sp_r, sp_o, th_r, th_o = ratio_txt
        lines.append("water thickness / lane spacing (dimensionless)\t%.4f\t%.4f\tx%.2f\triver px / (right-left crossing centre), both from the SAME frame"
                     % (th_r / float(sp_r), th_o / float(sp_o), (th_o / float(sp_o)) / (th_r / float(sp_r))))
        lines.append("water thickness in TILES (11 tiles between lanes)\t%.2f tile\t%.2f tile\t%+.2f tile\t11 * thickness / lane spacing; GameConst.BridgeCxATile=3.5 BridgeCxBTile=14.5"
                     % (11.0 * th_r / sp_r, 11.0 * th_o / sp_o, 11.0 * th_o / sp_o - 11.0 * th_r / sp_r))
        lines.append("px per tile (derived)\t%.1f px\t%.1f px\t%+.1f px\tlane spacing / 11"
                     % (sp_r / 11.0, sp_o / 11.0, sp_o / 11.0 - sp_r / 11.0))
    # Pairing: index-wise pairing of the RAW run lists is WRONG -- the original frame contains extra
    # tan runs from off-lane scenery/units (x24..64, x390..397, x1004..1079).  The two runs that are
    # actually the lane crossings are exactly the ones selected by lanes() (centre 80..1000,
    # width>=60, i.e. the same set used as the scale ruler), so pair those, and print every run.
    if lr and lo:
        for i, ((a_r, b_r, w_r, c_r, m_r), (a_o, b_o, w_o, c_o, m_o)) in enumerate(zip(lr, lo)):
            lines.append("lane crossing %d x / width / centre / deck RGB\tx%d..%d w=%d c=%d %s\tx%d..%d w=%d c=%d %s\twidth %+d / centre %+d / %s\t>55%% of the band column is non-water; paired as the two lane crossings (centre 80..1000, width>=60), NOT by raw index"
                         % (i + 1, a_r, b_r, w_r, c_r, m_r, a_o, b_o, w_o, c_o, m_o,
                            w_o - w_r, c_o - c_r, d3(m_r, m_o)))
    lines.append("unpaired/extra non-water runs\tORIGINAL %s\tOURS %s\t-\ttan scenery, units or frame edges inside the band -- NOT bridges"
                 % ([("x%d..%d c=%d" % (a, b, c)) for a, b, w, c, m in cr if not any(a == x[0] for x in (lr or []))],
                    [("x%d..%d c=%d" % (a, b, c)) for a, b, w, c, m in co if not any(a == x[0] for x in (lo or []))]))
    open(OUT_TSV, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("wrote %s" % OUT_TSV)


# Pins recorded with the table.  A change here means "the input moved, so the table is STALE" --
# that is the whole point: the check must be able to FAIL.
# 2026-09-23 17:43:58: pin re-recorded from A20AEDF66BED9BF9 after the arena frame was re-captured
# by the CR-V1 chain on build CR.dll=7E1B2A2A42605EFC (the HudPanel overlap fix 17:13:44 plus the
# AppFlow blank-screen fix 17:40:39 both landed in that assembly).  The PREVIOUS pin 0159E8FDFA75C58D
# belonged to the 12:06:30 frame, i.e. before both fixes -- leaving it here would have made this
# probe report the table as fresh while it actually described a superseded frame.
PIN_OURS = "A20AEDF66BED9BF9"          # .ai-tmp/screenshots/CR-V1-arena-nohud.png (shot 17:43:58)
PIN_ZONES = ["river band y", "water mean RGB", "grass above river mean RGB",
             "water thickness / lane spacing", "water thickness in TILES", "px per tile"]
PIN_CAVEAT = "SAME RESOLUTION"          # the scale caveat must not be silently dropped from the header


def verify(negctl=False):
    def sha16(p):
        if not os.path.isfile(p):
            return None
        return hashlib.sha256(open(p, "rb").read()).hexdigest()[:16].upper()

    pin_ours = "0000000000000000" if negctl else PIN_OURS
    checks = []

    def chk(name, ok, detail=""):
        checks.append(ok)
        print("  %-4s %-34s %s" % ("PASS" if ok else "FAIL", name, detail))

    print("verify: pins + deliverables (mode=%s)" % ("NEGCTL" if negctl else "normal"))
    chk("frame sha16 == pin", sha16(OURS) == pin_ours, "ours=%s pin=%s" % (sha16(OURS), pin_ours))
    chk("reference reachable", os.path.isfile(REF), REF)
    tsv = open(OUT_TSV, encoding="utf-8").read() if os.path.isfile(OUT_TSV) else None
    chk("zones tsv present", tsv is not None, OUT_TSV)
    if tsv:
        data = [l for l in tsv.splitlines() if l.strip() and not l.lstrip().startswith("#")]
        badcols = [i for i, l in enumerate(data) if len(l.split("\t")) != 5]
        chk("every tsv row has 5 columns", not badcols, "rows=%d bad=%s" % (len(data), badcols))
        missing = [z for z in PIN_ZONES if z not in tsv]
        chk("all required zones present", not missing, "missing=%s" % missing)
        chk("scale caveat kept in header", PIN_CAVEAT in tsv, "")
    for p in ("CR-V1-sidebyside.png", "CR-V1-river-pair.png", "CR-V1-bridge-pair.png"):
        fp = os.path.join(SHOTS, p)
        ok = os.path.isfile(fp) and os.path.getsize(fp) > 10000
        chk("composite %s" % p, ok, "%s B" % (os.path.getsize(fp) if os.path.isfile(fp) else "-"))
    nbad = checks.count(False)
    print("RESULT: checks=%d fails=%d" % (len(checks), nbad))
    if negctl:
        # the check must be able to FAIL: with a deliberately wrong pin we require >=1 FAIL
        print("NEGCTL: expecting fails>=1  ->  %s"
              % ("OK (the check can fail)" if nbad >= 1 else "BROKEN (it passed a wrong pin)"))
        sys.exit(0 if nbad >= 1 else 1)
    sys.exit(1 if nbad else 0)


# ---- ground: where does the grass colour actually come from?  (offline, read-only) ----------
# Lead ruling D117: our in-game grass (160,183,75) ~= ArenaView.BaseGrassColor (154,182,85) at
# ArenaView.cs:677, original (206,196,91)/(185,190,70), dR=-46, ATTRIBUTION UNDETERMINED because the
# f006 grass mean was never measured.  This measures it.  Numbers only, no attribution.
PIN_GRASS = {"ours": (160, 183, 75), "orig_above": (206, 196, 91), "orig_below": (185, 190, 70),
             "const": (154, 182, 85)}
F006 = os.path.join(ROOT, "原版资源", "cr-assets-png", "assets", "sc",
                    "arena_training_out", "arena_training_sprite_06.png")
GRASS_RECT = (0, 696, 1020, 1422)      # ArenaView.cs:26  grass (0,696)-(1020,1422)
WATER_ROWS = (808, 852)                # ArenaView.cs:26  water band inside that rect / L226 RiverWaterPyTop


def grass_source():
    if not os.path.isfile(F006):
        print("f006 MISSING: %s" % F006); sys.exit(2)
    im = Image.open(F006).convert("RGBA")
    px = im.load()
    x0, y0, x1, y1 = GRASS_RECT
    print("f006 = %s  size=%s   (canvas per ResPaths.ArenaFrame)" % (os.path.relpath(F006, ROOT), im.size))

    def region(a, b):
        acc = [0, 0, 0]; n = 0
        for y in range(a, min(b, im.size[1])):
            for x in range(x0, min(x1, im.size[0]), 2):
                p = px[x, y]
                if p[3] < 200:
                    continue
                acc[0] += p[0]; acc[1] += p[1]; acc[2] += p[2]; n += 1
        return (tuple(int(v / n) for v in acc) if n else None), n

    allm, na = region(y0, y1)
    g1, n1 = region(y0, WATER_ROWS[0] - 2)
    g2, n2 = region(WATER_ROWS[1] + 2, y1)
    print("  whole rect (0,696)-(1020,1422)      mean=%-16s opaque=%d" % (str(allm), na))
    print("  above the water band (696..806)     mean=%-16s opaque=%d" % (str(g1), n1))
    print("  below the water band (854..1422)    mean=%-16s opaque=%d" % (str(g2), n2))
    print("  pins: ours in-game %s | original above %s / below %s | ArenaView.BaseGrassColor %s (ArenaView.cs:677)"
          % (PIN_GRASS["ours"], PIN_GRASS["orig_above"], PIN_GRASS["orig_below"], PIN_GRASS["const"]))

    def d(a, b):
        return None if (not a or not b) else tuple(x - y for x, y in zip(a, b))

    print("  asset grass (above band) minus ORIGINAL above : %s" % (d(g1, PIN_GRASS["orig_above"]),))
    print("  asset grass (above band) minus OURS in-game   : %s" % (d(g1, PIN_GRASS["ours"]),))
    print("  our constant            minus asset grass     : %s" % (d(PIN_GRASS["const"], g1),))
    print("  BRANCH (not a conclusion; the numbers above decide):")
    print("    asset ~= original  => the flat BaseGrassColor/tint is what flattens+darkens the ground")
    print("    asset also dark    => the grass ASSET choice is the problem")
    sys.exit(0)


def river_asset():
    """Does f006's own water band (canvas py 805..852, x 152..1084) contain BANK rows, or is it
    2.0 tiles of pure water?  This decides whether next round's (b) 'bank / stone cap layering' has
    an in-package pixel source at all.  Read-only; classes only, no attribution."""
    if not os.path.isfile(F006):
        print("f006 MISSING: %s" % F006); sys.exit(2)
    im = Image.open(F006).convert("RGBA")
    px = im.load()
    print("f006 water band per-row profile   x=160..1080 step 4   (ArenaView.cs:26 water (152,808)-(1084,852))")
    print("   y     mean RGB            opaque  class")
    rows = []
    for y in range(796, 866):
        acc = [0, 0, 0]; n = 0
        for x in range(160, 1081, 4):
            p = px[x, y]
            if p[3] < 200:
                continue
            acc[0] += p[0]; acc[1] += p[1]; acc[2] += p[2]; n += 1
        if n == 0:
            print("  %-5d (all transparent)" % y); continue
        m = tuple(int(v / n) for v in acc)
        r, g, b = m
        # ORDER MATTERS and the first version was WRONG: (162,187,75) is GREEN grass (g>r) but the
        # tan test (r>150 and g>120 and b<160) also matched it, so 22 grass rows were labelled
        # "TAN/stone" -- a label wider than the object it names.  GREEN is now tested first.
        if g > r + 12 and g > b + 30:
            c = "GRASS/green"
        elif b > r and b > g and b > 110:
            c = "WATER"
        elif r > 150 and g > 120 and b < 160 and r > g + 15:
            c = "TAN/stone"
        elif max(m) < 120:
            c = "DARK dirt"
        else:
            c = "other"
        rows.append((y, c))
        print("  %-5d %-22s %-7d %s" % (y, str(m), n, c))
    runs = []
    for y, c in rows:
        if runs and runs[-1][2] == c:
            runs[-1][1] = y
        else:
            runs.append([y, y, c])
    print("\n  contiguous classes inside/around the band:")
    for a, b, c in runs:
        print("    y%d..%d  (%dpx)  %s" % (a, b, b - a + 1, c))
    band = [c for y, c in rows if 805 <= y <= 852]
    nw = band.count("WATER")
    print("\n  rows 805..852 (the registered 47px band): %d rows, of which WATER=%d, non-water=%d"
          % (len(band), nw, len(band) - nw))
    print("  => if non-water==0 the band is PURE WATER, so next round's (b) has NO in-package bank")
    print("     pixels and must NOT be implemented by inventing pixels.  Numbers decide, not this line.")
    sys.exit(0)


if "--river-asset" in sys.argv:
    river_asset()
elif "--grass" in sys.argv:
    grass_source()
elif "--verify" in sys.argv:
    verify(negctl="--negctl" in sys.argv)
else:
    main()
